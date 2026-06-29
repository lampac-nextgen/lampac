using Gst;
using GStreamer.Models;
using Shared.Services.Pools;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GStreamer.Services;

public class GStask
{
    #region GStask
    public System.DateTime lastActive { get; private set; } = System.DateTime.UtcNow;

    public SemaphoreSlim semaphore { get; private set; } = new(1, 1);

    public bool IsDead { get; private set; }

    public bool IsFrozen { get; private set; }

    public bool IsEos { get; private set; }

    public int lastSentSegment = -1;
    int audioIndex;
    long? contentLength;

    double positionSeconds = 0;
    double positionSeekSeconds = 0;

    public readonly ulong id;
    public readonly string user_uid;
    public readonly ProbeInfo probe;
    public readonly string sourceUrl;
    public readonly ModuleConf conf;

    public byte[] initMp4 { get; private set; }

    bool statePlaying = false;
    (int index, bool complete, Segment seg) readySegment = (-1, false, default);

    Mp4BoxReader mp4Reader;

    Pipeline pipeline;
    Bus bus;
    Gst.Bin bin;
    GstApp.AppSink sink;

    CancellationTokenSource busWatchCts;
    System.Threading.Tasks.Task busWatchTask;

    public GStask(ProbeInfo probe, ModuleConf conf, string sourceUrl, ulong id, string user_uid, int audio, long? contentLength)
    {
        this.id = id;
        this.probe = probe;
        this.user_uid = user_uid;
        this.sourceUrl = sourceUrl;
        this.conf = conf;
        this.contentLength = contentLength;

        if (probe.Tracks.FirstOrDefault(i => i.Type == "audio" && i.Index == audio) != null)
            audioIndex = audio;

        mp4Reader = new Mp4BoxReader(
           onInit: data =>
           {
               initMp4 = data;
           },
           onSegment: seg =>
           {
               readySegment.seg = seg;
               readySegment.complete = true;

               if (seg.startSeconds >= 0)
                   positionSeconds = seg.startSeconds + positionSeekSeconds;
           },
           segmentSeconds: conf.segment_seconds
        );
    }
    #endregion

    #region CreatePipelineArgs
    string CreatePipelineArgs(ProbeInfo probe)
    {
        var sb = StringBuilderPool.ThreadInstance;
        double version = ModInit.conf.gst_version;

        #region AppendTranscodeToH264
        void AppendTranscodeToH264()
        {
            int segmentSeconds = Math.Max(1, conf.segment_seconds);
            int frameRateNum = probe.Video?.FrameRateNum ?? 0;
            int frameRateDen = probe.Video?.FrameRateDen ?? 0;

            int keyIntMax = frameRateNum > 0 && frameRateDen > 0
                ? Math.Max(
                    1,
                    (int)Math.Round(
                        (double)frameRateNum * segmentSeconds / frameRateDen
                    )
                )
                : 25 * segmentSeconds;

            sb.AppendLine($$"""
            mq.src_0 !
            decodebin !
            videoconvert !
            video/x-raw,
                format=I420 !
            x264enc
                tune=zerolatency
                speed-preset=veryfast
                bitrate={{conf.video_bitrate}}
                key-int-max={{keyIntMax}}
                bframes=0
                byte-stream=false !
            video/x-h264,
                profile=main,
                stream-format=avc,
                alignment=au !
            h264parse
                config-interval=0 !
            h264timestamper !
            video/x-h264,
                profile=main,
                stream-format=avc,
                alignment=au !
            mux.video_0
            """);
        }
        #endregion

        #region souphttpsrc
        string downloadLimit = conf.pipeline_downloadRate > 0
            ? $$"""
            identity
                datarate={{conf.pipeline_downloadRate * 1_000_000 / 8}}
                sync=true
                silent=true !
            """
            : string.Empty;

        string httpqueue = string.Empty;

        if (conf.tempfs)
        {
            const int targetSeconds = 30;
            int maxBytes = 32 * 1024 * 1024;

            if (contentLength.HasValue && contentLength.Value > 0 && probe.DurationSeconds > 0)
            {
                maxBytes = (int)Math.Ceiling(
                    (double)contentLength.Value / probe.DurationSeconds * targetSeconds
                );
            }

            long ringBytes = (long)maxBytes * (conf.tempfs_ring + 3);

            string tempTemplate = Path.Combine(
                "cache",
                "gstranscoding",
                $"{id}-XXXXXX"
            ).Replace('\\', '/');

            httpqueue = $$"""
            queue2
                use-buffering=false
                temp-template="{{tempTemplate}}"
                temp-remove=true
                ring-buffer-max-size={{ringBytes + (1024 * 1024)}}
                max-size-bytes={{maxBytes}}
                max-size-buffers=0
                max-size-time=0 !
            """;
        }

        sb.AppendLine($$"""
        souphttpsrc
            location="{{sourceUrl}}"
            is-live=false
            keep-alive=true
            timeout=60
            retries=5
            {{(version >= 1.26 ? "retry-backoff-factor=0.5 retry-backoff-max=10" : string.Empty)}} !
        {{downloadLimit}}
        {{httpqueue}}
        """);
        #endregion

        sb.AppendLine($$"""
        matroskademux
            name=d
        multiqueue
            name=mq
            use-buffering=false
            max-size-buffers=5
        """);

        #region d.video
        sb.AppendLine("""
        d.video_0 !
        mq.sink_0
        """);

        if (probe.IsH264)
        {
            #region H264
            if (conf.transcodeH264)
            {
                AppendTranscodeToH264();
            }
            else
            {
                sb.AppendLine("""
                mq.src_0 !
                h264parse
                    config-interval=0 !
                h264timestamper !
                video/x-h264,
                    stream-format=avc,
                    alignment=au !
                mux.video_0
                """);
            }
            #endregion
        }
        else if (probe.IsH265)
        {
            #region H265
            if (conf.transcodeH265)
            {
                AppendTranscodeToH264();
            }
            else
            {
                sb.AppendLine("""
                mq.src_0 !
                h265parse
                    config-interval=0 !
                h265timestamper !
                video/x-h265,
                    stream-format=hvc1,
                    alignment=au !
                mux.video_0
                """);
            }
            #endregion
        }
        else if (probe.IsAV1)
        {
            #region AV1
            if (conf.transcodeAV1)
            {
                AppendTranscodeToH264();
            }
            else
            {
                sb.AppendLine("""
                mq.src_0 !
                av1parse !
                video/x-av1,
                    stream-format=obu-stream,
                    alignment=tu !
                mux.video_0
                """);
            }
            #endregion
        }
        else if (probe.IsVP9)
        {
            #region VP9
            if (conf.transcodeVP9)
            {
                AppendTranscodeToH264();
            }
            else
            {
                sb.AppendLine("""
                mq.src_0 !
                vp9parse !
                video/x-vp9,
                    alignment=frame !
                mux.video_0
                """);
            }
            #endregion
        }
        else
        {
            throw new NotSupportedException("Unsupported video codec");
        }
        #endregion

        #region d.audio
        var selectedAudio = probe.Tracks.FirstOrDefault(i =>
            i.Type == "audio" &&
            i.Index == audioIndex
        );

        int aacChannels   = conf.aac_channels   > 0 ? conf.aac_channels   : (selectedAudio?.Channels ?? 2);
        int aacSamplerate = conf.aac_samplerate > 0 ? conf.aac_samplerate : (selectedAudio?.Rate      ?? 48000);

        sb.AppendLine($$"""
        d.audio_{{audioIndex}} !
        mq.sink_1
        """);

        if (selectedAudio?.IsAAC == true)
        {
            sb.AppendLine("""
            mq.src_1 !
            aacparse !
            audio/mpeg,
                mpegversion=4,
                stream-format=raw !
            mux.audio_0
            """);
        }
        else
        {
            sb.AppendLine($$"""
            mq.src_1 !
            decodebin !
            audioconvert
                dithering=none
                noise-shaping=none !
            audioresample
                quality=2
                sinc-filter-mode=full !
            audio/x-raw,
                format=F32LE,
                layout=interleaved,
                rate={{aacSamplerate}},
                channels={{aacChannels}} !
            avenc_aac
                bitrate={{conf.aac_bitrate * 1000}} !
            aacparse !
            audio/mpeg,
                mpegversion=4,
                stream-format=raw,
                rate={{aacSamplerate}},
                channels={{aacChannels}} !
            mux.audio_0
            """);
        }
        #endregion

        sb.AppendLine($$"""
        mp4mux
            name=mux
            fragment-mode=dash-or-mss
            fragment-duration={{conf.segment_seconds * 1000}}
            streamable=true !
        """);

        if (version >= 1.24 && conf.pipeline_appsinkBuffers > 1)
        {
            sb.AppendLine($$"""
            appsink
                name=out
                emit-signals=false
                sync=false
                max-buffers={{conf.pipeline_appsinkBuffers}}
                max-bytes={{136L * 1024 * 1024}}
                {{(version >= 1.28 ? "leaky-type=none" : "drop=false")}}
                wait-on-eos=false
            """);
        }
        else
        {
            sb.AppendLine($$"""
            appsink
                name=out
                emit-signals=false
                sync=false
                max-buffers={{(conf.pipeline_appsinkBuffers > 1 ? conf.pipeline_appsinkBuffers : 1)}}
                {{(version >= 1.28 ? "leaky-type=none" : "drop=false")}}
                wait-on-eos=false
            """);
        }

        return sb.ToString();
    }
    #endregion

    #region BusWatch
    void StartBusWatch()
    {
        busWatchCts = new CancellationTokenSource();

        var token = busWatchCts.Token;

        busWatchTask = System.Threading.Tasks.Task.Factory.StartNew(
            () => BusWatch(token),
            token,
            System.Threading.Tasks.TaskCreationOptions.LongRunning,
            System.Threading.Tasks.TaskScheduler.Default
        );
    }

    void StopBusWatch()
    {
        var cts = busWatchCts;
        var task = busWatchTask;

        busWatchCts = null;
        busWatchTask = null;

        if (cts == null)
            return;

        cts.Cancel();

        try
        {
            if (task != null && System.Threading.Tasks.Task.CurrentId != task.Id)
                task.Wait(TimeSpan.FromMilliseconds(100));
        }
        catch { }

        cts.Dispose();
    }

    void BusWatch(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using (var msg = bus.TimedPop(50_000_000UL))
                {
                    if (msg == null)
                        continue;

                    uint type = BusReader.GetType(msg);

                    if (type == BusReader.Error)
                    {
                        IsDead = true;
                        Dispose();
                        return;
                    }
                    else if (type == BusReader.Eos)
                    {
                        double duration = probe.DurationSeconds;
                        if (duration <= 0)
                        {
                            // длительность неизвестна — EOS нельзя признать ошибочным
                            IsEos = true;
                            return;
                        }

                        double eosThreshold = duration -
                            conf.segment_seconds -
                            120; // 120s

                        if (Volatile.Read(ref positionSeconds) >= eosThreshold)
                        {
                            // выход в пределах допустимого отступления от конца
                            IsEos = true;
                            return;
                        }
                        else
                        {
                            IsDead = true;
                            Dispose();
                            return;
                        }
                    }
                }
            }
            catch { }
        }
    }
    #endregion

    #region UpdateLastActive
    public void UpdateLastActive()
    {
        lastActive = System.DateTime.UtcNow;
    }
    #endregion

    #region Seek
    public bool Seek(double seconds)
    {
        if (IsDead || !statePlaying)
            return false;

        if (pipeline != null)
        {
            StopBusWatch();
            pipeline.SetState(State.Null);
            pipeline.Dispose();
            sink.Dispose();
            bus.Dispose();
        }

        string pipelineArgs = CreatePipelineArgs(probe);
        pipeline = (Pipeline)Gst.Functions.ParseLaunch(pipelineArgs);
        bus = pipeline.GetBus();

        bin = pipeline;
        sink = (GstApp.AppSink)bin.GetByName("out");

        StateChangeReturn ret;

        if (seconds > 0)
        {
            ret = pipeline.SetState(State.Paused);
            if (ret == StateChangeReturn.Failure)
            {
                IsDead = true;
                Dispose();
                return false;
            }

            if (ret == StateChangeReturn.Async)
            {
                // ждём завершение команды в pipeline
                using (var msg = bus.TimedPopFiltered(5_000_000_000UL, MessageType.AsyncDone | MessageType.Error | MessageType.Eos))
                {
                    if (BusReader.GetType(msg) == BusReader.Error || BusReader.GetType(msg) == BusReader.Eos)
                    {
                        IsDead = true;
                        Dispose();
                        return false;
                    }
                }
            }

            bool ok = pipeline.SeekSimple(
                Format.Time,
                SeekFlags.Flush | SeekFlags.KeyUnit | SeekFlags.SnapAfter,
                (long)Math.Round(seconds * 1_000_000_000d)
            );

            if (!ok)
            {
                IsDead = true;
                Dispose();
                return false;
            }

            // После flushing seek тоже лучше дождаться ASYNC_DONE.
            using (var flushing = bus.TimedPopFiltered(5_000_000_000UL, MessageType.AsyncDone | MessageType.Error | MessageType.Eos))
            {
                if (BusReader.GetType(flushing) == BusReader.Error || BusReader.GetType(flushing) == BusReader.Eos)
                {
                    IsDead = true;
                    Dispose();
                    return false;
                }
            }
        }

        ret = pipeline.SetState(State.Playing);
        if (ret == StateChangeReturn.Failure)
        {
            IsDead = true;
            Dispose();
            return false;
        }

        if (ret == StateChangeReturn.Async)
        {
            using (var msg = bus.TimedPopFiltered(5_000_000_000UL, MessageType.AsyncDone | MessageType.Error | MessageType.Eos))
            {
                if (BusReader.GetType(msg) == BusReader.Error || BusReader.GetType(msg) == BusReader.Eos)
                {
                    IsDead = true;
                    Dispose();
                    return false;
                }
            }
        }

        mp4Reader.ResetSegment();
        mp4Reader.SeekReset(seconds);
        readySegment = (-1, false, default);

        IsFrozen = false;
        IsEos = false;
        positionSeconds = seconds;
        positionSeekSeconds = seconds;
        StartBusWatch();
        return true;
    }
    #endregion

    #region GetSegment
    public Segment GetSegment(int index, CancellationToken ct, int audio = 0)
    {
        if (IsDead)
            return default;

        #region start Playing
        if (!statePlaying)
        {
            statePlaying = true;

            if (probe.Tracks.FirstOrDefault(i => i.Type == "audio" && i.Index == audio) != null)
                audioIndex = audio;

            string pipelineArgs = CreatePipelineArgs(probe);
            pipeline = (Pipeline)Gst.Functions.ParseLaunch(pipelineArgs);
            bus = pipeline.GetBus();

            bin = pipeline;
            sink = (GstApp.AppSink)bin.GetByName("out");
            var ret = pipeline.SetState(State.Playing);
            if (ret == StateChangeReturn.Failure)
            {
                IsDead = true;
                Dispose();
                return default;
            }

            if (ret == StateChangeReturn.Async)
            {
                using (var msg = bus.TimedPopFiltered(5_000_000_000UL, MessageType.AsyncDone | MessageType.Error | MessageType.Eos))
                {
                    if (BusReader.GetType(msg) == BusReader.Error || BusReader.GetType(msg) == BusReader.Eos)
                    {
                        IsDead = true;
                        Dispose();
                        return default;
                    }
                }
            }

            StartBusWatch();
        }
        else if (IsFrozen)
        {
            if (!Seek(positionSeconds))
            {
                IsDead = true;
                Dispose();
                return default;
            }
        }
        #endregion

        if (readySegment.index == index && readySegment.complete)
            return readySegment.seg;

        mp4Reader.ResetSegment();
        readySegment = (-1, false, default);

        try
        {
            long start = Stopwatch.GetTimestamp();
            var timeout = TimeSpan.FromSeconds(10);

            while (Stopwatch.GetElapsedTime(start) < timeout)
            {
                if (ct.IsCancellationRequested || IsDead)
                    return default;

                // 100 ms
                using (var sample = sink.TryPullSample(100_000_000UL))
                {
                    using (var buffer = sample?.GetBuffer())
                    {
                        if (buffer == null)
                        {
                            if (IsEos)
                            {
                                // В _deferred может лежать полный Segment
                                if (mp4Reader.TryProcessDeferred() && readySegment.complete)
                                {
                                    readySegment.index = index;
                                    return readySegment.seg;
                                }

                                // Последний fragment может быть неполным:
                                // только moof, только часть mdat либо fragment одной дорожки
                                if (mp4Reader.TryBuildEndOfStreamRemainder() && readySegment.complete)
                                {
                                    readySegment.index = index;
                                    return readySegment.seg;
                                }

                                // очередь appsink полностью вычитана
                                return default;
                            }

                            continue;
                        }

                        nuint size = buffer.GetSize(); 
                        if (size == 0)
                            continue;

                        mp4Reader.Push(buffer, (int)size);

                        if (readySegment.complete)
                        {
                            readySegment.index = index > 0 ? index : 0;
                            return readySegment.seg;
                        }
                    }
                }
            }

            return default;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_qv6la4ny");
            IsDead = true;
            Dispose();
            return default;
        }
    }
    #endregion

    #region Dispose
    public void Dispose()
    {
        mp4Reader?.Dispose();
        mp4Reader = null;

        StopBusWatch();

        if (pipeline == null)
            return;

        pipeline.SetState(State.Null);
        pipeline.Dispose();
        pipeline = null;

        sink.Dispose();
        sink = null;
        bus.Dispose();
        bus = null;

        semaphore.Dispose();
        semaphore = null;
        initMp4 = null;
        bin = null;
    }
    #endregion

    #region Frozen
    public void Frozen()
    {
        if (pipeline == null)
            return;

        IsFrozen = true;
        StopBusWatch();

        pipeline.SetState(State.Null);
        pipeline.Dispose();
        pipeline = null;

        sink.Dispose();
        sink = null;
        bus.Dispose();
        bus = null;
        bin = null;
    }
    #endregion
}
