using Gst;
using GStreamer.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace GStreamer.Services;

public partial class GStask
{
    #region GStask
    public System.DateTime lastActive { get; private set; } = System.DateTime.UtcNow;

    public readonly SemaphoreSlim semaphore = new(1, 1);

    int isDead, isEos;

    public bool IsDead
        => Volatile.Read(ref isDead) != 0;

    public bool IsEos
    {
        get => Volatile.Read(ref isEos) != 0;
        private set => Volatile.Write(ref isEos, value ? 1 : 0);
    }

    public bool IsFrozen { get; private set; }

    int audioIndex;
    long? contentLength;

    const ulong GstSecond = 1_000_000_000UL;

    ulong positionSeconds = 0;
    ulong positionSeekSeconds = 0;

    readonly ConcurrentDictionary<int, ulong> segmentStartNsByIndex = new();
    int lastClientSegmentIndex = -1;

    public readonly ulong id;
    public readonly string user_uid;
    public readonly ProbeInfo probe;
    public readonly string sourceUrl;
    public readonly ModuleConf conf;
    public readonly CueTimeline cueTimeline;

    bool statePlaying = false;

    int pipelineGeneration;
    int pipelineStopping;
    int ensureSegmentActive;

    readonly object pipelineLock = new();

    readonly ManualResetEventSlim busWatchIdle = new(true);
    readonly ManualResetEventSlim ensureSegmentIdle = new(true);
    readonly ManualResetEventSlim pipelineDisposeIdle = new(true);

    long initMp4Length;

    bool InitMp4Ready
        => Volatile.Read(ref initMp4Length) > 0;

    public HlsVariantInfo hlsVariantInfo { get; private set; }

    Mp4BoxReader mp4Reader;

    Pipeline pipeline;
    Bus bus;
    Gst.Bin bin;
    GstApp.AppSink sink;

    CancellationTokenSource busWatchCts;
    System.Threading.Tasks.Task busWatchTask;

    public GStask(
        ProbeInfo probe,
        ModuleConf conf,
        string sourceUrl,
        ulong id,
        string user_uid,
        int audio,
        long? contentLength,
        CueTimeline cueTimeline = null
    )
    {
        this.id = id;
        this.probe = probe;
        this.user_uid = user_uid;
        this.sourceUrl = sourceUrl;
        this.conf = conf;
        this.contentLength = contentLength;
        this.cueTimeline = cueTimeline;

        InitSegmentCache();

        if (probe.Tracks.FirstOrDefault(i => i.Type == "audio" && i.Index == audio) != null)
            audioIndex = audio;

        mp4Reader = new Mp4BoxReader(
           onInit: OnInitMp4,
           onSegment: OnSegmentReady,
           segmentSeconds: conf.segment_seconds,
           segmentDiff: IsVideoTranscoded ? 0 : conf.segment_diff,
           cueMode: cueTimeline != null
        );
    }
    #endregion

    #region OnSegmentReady
    void OnSegmentReady(Segment segment)
    {
        int index = activeSegmentIndex;
        if (index < 0)
            return;

        ulong segmentPositionNs = segment.startNs;

        if (cueTimeline != null)
        {
            if (!cueTimeline.TryGetSegment(index, out CueSegment expected))
                throw new InvalidOperationException($"Cue segment {index} does not exist.");

            segmentPositionNs = expected.StartNs;
        }

        if (segmentPositionNs > 0)
            Volatile.Write(ref positionSeconds, segmentPositionNs);

        if (!StoreSegmentFile(index, segment))
        {
            activeSegmentStoreFailed = true;
        }
        else
        {
            segmentStartNsByIndex[index] = segmentPositionNs;
            Volatile.Write(ref readerSegmentIndex, index);
        }
    }
    #endregion

    #region OnInitMp4
    void OnInitMp4(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty || InitMp4Ready)
            return;

        HlsVariantInfo parsed = Mp4InitInfoReader.Read(data.Span);
        if (parsed != null)
        {
            parsed.FrameRate = probe.Video?.FrameRate ?? 0;
            if (parsed.Width <= 0)
                parsed.Width = probe.Video?.Width ?? 0;
            if (parsed.Height <= 0)
                parsed.Height = probe.Video?.Height ?? 0;
            if (conf.hdr_to_sdr && probe.Video?.IsHdr == true)
                parsed.VideoRange = "SDR";
            else if (parsed.VideoRange == null && !IsVideoTranscoded)
            {
                parsed.VideoRange = probe.Video?.VideoTransfer switch
                {
                    VideoTransfer.Pq => "PQ",
                    VideoTransfer.Hlg => "HLG",
                    VideoTransfer.Sdr => "SDR",
                    _ => null
                };
            }
        }

        StoreInitFile(data.Span);
        hlsVariantInfo = parsed;
        Volatile.Write(ref initMp4Length, data.Length);
    }
    #endregion

    #region BusWatch
    bool StartBusWatch()
    {
        lock (pipelineLock)
        {
            if (Volatile.Read(ref pipelineStopping) != 0 ||
                IsDead ||
                pipeline == null ||
                bus == null)
            {
                return false;
            }

            if (busWatchCts != null)
                return true;

            var watchBus = bus;
            int generation = Volatile.Read(ref pipelineGeneration);
            var cts = new CancellationTokenSource();

            busWatchCts = cts;
            busWatchIdle.Reset();

            try
            {
                busWatchTask = System.Threading.Tasks.Task.Factory.StartNew(
                    () => BusWatch(watchBus, generation, cts.Token),
                    CancellationToken.None,
                    System.Threading.Tasks.TaskCreationOptions.LongRunning,
                    System.Threading.Tasks.TaskScheduler.Default
                );

                return true;
            }
            catch
            {
                busWatchCts = null;
                busWatchTask = null;

                busWatchIdle.Set();
                cts.Dispose();

                throw;
            }
        }
    }

    CancellationTokenSource StopBusWatch()
    {
        var cts = busWatchCts;

        busWatchCts = null;
        busWatchTask = null;

        if (cts == null)
            return null;

        try
        {
            cts.Cancel();
        }
        catch { }

        return cts;
    }

    void BusWatch(Bus watchBus, int generation, CancellationToken ct)
    {
        bool disposeTask = false;

        try
        {
            while (!ct.IsCancellationRequested && !IsDead)
            {
                try
                {
                    if (generation != Volatile.Read(ref pipelineGeneration))
                        return;

                    using var msg = watchBus.TimedPop(50_000_000UL);

                    if (ct.IsCancellationRequested)
                        return;

                    if (generation != Volatile.Read(ref pipelineGeneration))
                        return;

                    if (msg == null)
                        continue;

                    uint type = BusReader.GetType(msg);

                    if (type == BusReader.Error)
                    {
                        BusReader.TryParseError(
                            msg,
                            out string error,
                            out string debug
                        );

                        lock (pipelineLock)
                        {
                            if (ct.IsCancellationRequested ||
                                generation != Volatile.Read(ref pipelineGeneration) ||
                                !ReferenceEquals(watchBus, bus))
                            {
                                return;
                            }

                            disposeTask = true;
                        }

                        LogTaskError(
                            "BusWatch",
                            $"GStreamer bus error. Error={error}, Debug={debug}",
                            messageType: type
                        );

                        return;
                    }

                    if (type == BusReader.Eos)
                    {
                        lock (pipelineLock)
                        {
                            if (ct.IsCancellationRequested ||
                                generation != Volatile.Read(ref pipelineGeneration) ||
                                !ReferenceEquals(watchBus, bus))
                            {
                                return;
                            }

                            int duration = probe.DurationSeconds;
                            if (duration <= 0)
                            {
                                IsEos = true;
                                return;
                            }

                            ulong durationNs = SecondsToClockTime(duration);
                            ulong eosBackoffNs = SecondsToClockTime(120);

                            ulong eosThreshold = durationNs > eosBackoffNs
                                ? durationNs - eosBackoffNs
                                : 0;

                            if (Volatile.Read(ref positionSeconds) >= eosThreshold)
                            {
                                IsEos = true;
                                return;
                            }

                            LogTaskError(
                                "BusWatch",
                                "GStreamer bus emitted EOS before the allowed end threshold.",
                                messageType: type
                            );

                            disposeTask = true;
                        }

                        return;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                        return;

                    if (generation != Volatile.Read(ref pipelineGeneration))
                        return;

                    LogTaskError(
                        "BusWatch",
                        "Unhandled exception while polling GStreamer bus.",
                        ex
                    );

                    return;
                }
            }
        }
        finally
        {
            busWatchIdle.Set();

            if (disposeTask)
                Dispose();
        }
    }
    #endregion

    #region UpdateLastActive
    public void UpdateLastActive()
    {
        lastActive = System.DateTime.UtcNow;
    }
    #endregion

    #region GetHlsBandwidth
    public long GetHlsBandwidth(out long? averageBandwidth)
    {
        var audio = probe.Tracks.FirstOrDefault(track =>
            track.Type == "audio" &&
            track.Index == audioIndex
        );

        int channels = conf.aac_channels > 0
            ? conf.aac_channels
            : Math.Max(1, audio?.Channels ?? 2);

        long audioBitrate = Math.Max(1, conf.aac_bitrate) * 1000L;

        if (channels > 2)
            audioBitrate *= 2;

        if (IsVideoTranscoded)
        {
            averageBandwidth =
                Math.Max(1, conf.video_bitrate) * 1000L +
                audioBitrate;

            // x264 bitrate является целевым, отдельные сегменты могут быть больше
            return averageBandwidth.Value * 125 / 100;
        }

        if (contentLength is > 0 && probe.DurationNs > 0)
        {
            double durationSeconds =
                probe.DurationNs / 1_000_000_000d;

            double average =
                contentLength.Value * 8d / durationSeconds;

            if (double.IsFinite(average) &&
                average > 0 &&
                average <= long.MaxValue / 2d)
            {
                averageBandwidth = (long)Math.Ceiling(average);
                return averageBandwidth.Value * 150 / 100;
            }
        }

        averageBandwidth = null;

        long fallback =
            Math.Max(1, conf.video_bitrate) * 1000L +
            audioBitrate;

        return Math.Max(4_000_000L, fallback * 150 / 100);
    }
    #endregion

    #region Frozen
    public void Frozen()
    {
        if (pipeline == null)
            return;

        IsFrozen = true;
        DisposePipeline();
        ClearSegmentCache();
    }
    #endregion

    #region Defrost
    public void Defrost()
    {
        if (IsFrozen)
        {
            ulong seekNs;

            if (cueTimeline?.TryGetSegment(lastClientSegmentIndex, out CueSegment cueSegment) == true)
                seekNs = cueSegment.StartNs;
            else if (segmentStartNsByIndex.TryGetValue(lastClientSegmentIndex, out ulong startNs))
                seekNs = startNs;
            else
                seekNs = positionSeconds;

            InitSegmentCache();
            if (!SeekClockTime(seekNs, accurate: cueTimeline != null))
            {
                LogTaskError(
                    "Defrost",
                    "SeekClockTime failed while defrosting task.",
                    seekNs: seekNs
                );

                Dispose();
            }
        }
    }
    #endregion

    #region Dispose
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDead, 1) != 0)
            return;

        DisposePipeline();
        ClearSegmentCache();
        Volatile.Write(ref initMp4Length, 0);
        TryDeleteFile(initMp4Path);
        segmentStartNsByIndex.Clear();
        mp4Reader?.Dispose();
        mp4Reader = null;
        hlsVariantInfo = null;
    }
    #endregion

    #region DisposePipeline
    void DisposePipeline()
    {
        Pipeline disposingPipeline = null;
        CancellationTokenSource watchCts = null;
        bool waitForOtherDispose;

        lock (pipelineLock)
        {
            if (Volatile.Read(ref pipelineStopping) != 0)
            {
                waitForOtherDispose = true;
            }
            else
            {
                if (pipeline == null)
                    return;

                waitForOtherDispose = false;

                Volatile.Write(ref pipelineStopping, 1);
                pipelineDisposeIdle.Reset();

                disposingPipeline = pipeline;

                Interlocked.Increment(ref pipelineGeneration);

                watchCts = StopBusWatch();
                CancelSegmentPrefetch();
            }
        }

        if (waitForOtherDispose)
        {
            pipelineDisposeIdle.Wait();
            return;
        }

        try
        {
            ensureSegmentIdle.Wait();
            busWatchIdle.Wait();

            lock (pipelineLock)
            {
                if (!ReferenceEquals(pipeline, disposingPipeline))
                    return;

                RemoveVideoStartProbe();
                RemoveVideoSegmentClipProbe();
                RemoveSourceThrottleProbe();

                try
                {
                    disposingPipeline.SetState(State.Null);
                }
                catch { }

                try
                {
                    disposingPipeline.Dispose();
                }
                catch { }

                foreach (var subSink in subsSinks.Values)
                {
                    try
                    {
                        subSink.Dispose();
                    }
                    catch { }
                }

                subsSinks.Clear();

                try
                {
                    sink?.Dispose();
                }
                catch { }

                sink = null;

                try
                {
                    bus?.Dispose();
                }
                catch { }

                bus = null;
                bin = null;
                pipeline = null;
            }
        }
        finally
        {
            watchCts?.Dispose();

            lock (pipelineLock)
            {
                Volatile.Write(ref pipelineStopping, 0);
                pipelineDisposeIdle.Set();
            }
        }
    }
    #endregion

    #region Helpers
    static ulong SecondsToClockTime(int seconds)
    {
        if (seconds <= 0)
            return 0;

        return checked((ulong)seconds * GstSecond);
    }

    static ulong AddClockTime(ulong left, ulong right)
    {
        return ulong.MaxValue - left < right
            ? ulong.MaxValue
            : left + right;
    }

    bool IsVideoTranscoded
        => conf.hdr_to_sdr && probe.Video?.IsHdr == true ||
           probe.IsH264 && conf.transcodeH264 ||
           probe.IsH265 && conf.transcodeH265 ||
           probe.IsAV1 && conf.transcodeAV1 ||
           probe.IsVP9 && conf.transcodeVP9 ||
           probe.IsVP8 && conf.transcodeVP8 ||
           probe.IsAVI && conf.transcodeAVI;

    void LogTaskError(
        string stage,
        string reason,
        Exception exception = null,
        int? segmentIndex = null,
        ulong? seekNs = null,
        uint? messageType = null
    )
    {
        if (exception == null)
        {
            Serilog.Log.Error(
                "GStreamer task error. Stage={Stage}, Reason={Reason}, TaskId={TaskId}, User={User}, Segment={Segment}, SeekNs={SeekNs}, MessageType={MessageType}, PositionNs={PositionNs}, PositionSeekNs={PositionSeekNs}, ReaderSegment={ReaderSegment}, ActiveSegment={ActiveSegment}, IsDead={IsDead}, IsFrozen={IsFrozen}, IsEos={IsEos}, StatePlaying={StatePlaying}",
                stage,
                reason,
                id,
                user_uid,
                segmentIndex,
                seekNs,
                messageType,
                positionSeconds,
                positionSeekSeconds,
                Volatile.Read(ref readerSegmentIndex),
                Volatile.Read(ref activeSegmentIndex),
                IsDead,
                IsFrozen,
                IsEos,
                statePlaying
            );
            return;
        }

        Serilog.Log.Error(
            exception,
            "GStreamer task error. Stage={Stage}, Reason={Reason}, TaskId={TaskId}, User={User}, Segment={Segment}, SeekNs={SeekNs}, MessageType={MessageType}, PositionNs={PositionNs}, PositionSeekNs={PositionSeekNs}, ReaderSegment={ReaderSegment}, ActiveSegment={ActiveSegment}, IsDead={IsDead}, IsFrozen={IsFrozen}, IsEos={IsEos}, StatePlaying={StatePlaying}",
            stage,
            reason,
            id,
            user_uid,
            segmentIndex,
            seekNs,
            messageType,
            positionSeconds,
            positionSeekSeconds,
            Volatile.Read(ref readerSegmentIndex),
            Volatile.Read(ref activeSegmentIndex),
            IsDead,
            IsFrozen,
            IsEos,
            statePlaying
        );
    }

    #endregion
}
