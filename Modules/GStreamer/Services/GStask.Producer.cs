using Gst;
using GStreamer.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GStreamer.Services;

public partial class GStask
{
    int readerSegmentIndex = -1;
    int activeSegmentIndex = -1;
    bool activeSegmentStoreFailed;

    int prefetchRunning;
    int prefetchRequested;
    int prefetchGeneration;
    CancellationTokenSource prefetchCts;

    #region Seek
    public bool Seek(int seconds)
    {
        // берем на conf.segment_seconds ниже позиции, что бы браузер не вернулся на -1 сегмент
        ulong ns = SecondsToClockTime(seconds - conf.segment_seconds);
        return SeekClockTime(ns);
    }

    bool SeekSegment(int index)
    {
        if (cueTimeline?.TryGetSegment(index, out CueSegment segment) == true)
            return SeekClockTime(segment.StartNs, accurate: true);

        return Seek(index * conf.segment_seconds);
    }

    bool SeekClockTime(ulong seekNs, bool accurate = false)
    {
        CancellationTokenSource watchCts = null;

        if (IsDead || !statePlaying)
            return false;

        if (seekNs > long.MaxValue)
            return false;

        segmentStartNsByIndex.Clear();

        try
        {
            bool reusePipeline = pipeline != null;

            if (reusePipeline)
            {
                lock (pipelineLock)
                {
                    if (Volatile.Read(ref pipelineStopping) != 0 ||
                        pipeline == null ||
                        bus == null ||
                        sink == null)
                    {
                        return false;
                    }

                    Interlocked.Increment(ref pipelineGeneration);
                    watchCts = StopBusWatch();
                    CancelSegmentPrefetch();
                }

                ensureSegmentIdle.Wait();
                busWatchIdle.Wait();
            }
            else
            {
                string pipelineArgs = CreatePipelineArgs(probe);
                pipeline = (Pipeline)Gst.Functions.ParseLaunch(pipelineArgs);
                bus = pipeline.GetBus();

                bin = pipeline;
                sink = (GstApp.AppSink)bin.GetByName("out");

                if (bus == null || sink == null)
                {
                    LogTaskError(
                        "SeekClockTime",
                        "Pipeline was created without bus or output appsink.",
                        seekNs: seekNs
                    );

                    Dispose();
                    return false;
                }

                InstallSourceThrottleProbe();

                subsSinks.Clear();

                foreach (var track in subtitleTracks)
                {
                    var subSink = (GstApp.AppSink)bin.GetByName($"subs_{track.Index}");
                    if (subSink != null)
                        subsSinks[track.Index] = subSink;
                }
            }

            StateChangeReturn ret;

            if (reusePipeline || seekNs > 0)
            {
                ret = pipeline.SetState(State.Paused);
                if (ret == StateChangeReturn.Failure)
                {
                    LogTaskError(
                        "SeekClockTime",
                        "SetState(Paused) failed before seek.",
                        seekNs: seekNs
                    );

                    Dispose();
                    return false;
                }

                if (ret == StateChangeReturn.Async)
                {
                    // ждём завершение команды в pipeline
                    using var msg = bus.TimedPopFiltered(
                        5_000_000_000UL,
                        MessageType.AsyncDone | MessageType.Error | MessageType.Eos
                    );

                    uint type = BusReader.GetType(msg);

                    if (type == BusReader.Error ||
                        type == BusReader.Eos)
                    {
                        LogTaskError(
                            "SeekClockTime",
                            "Pipeline pause preroll finished with error, EOS, or timeout.",
                            seekNs: seekNs,
                            messageType: type
                        );

                        Dispose();
                        return false;
                    }
                }

                if (reusePipeline)
                {
                    // appsink снимает backpressure до сброса timeline mp4mux
                    using var mux = bin?.GetByName("mux");
                    using var videoTimestamper = bin?.GetByName("video_timestamper");
                    using var videoEncoder = IsVideoTranscoded
                        ? bin?.GetByName("video_encoder")
                        : null;

                    if (mux == null ||
                        (IsVideoTranscoded && videoEncoder == null) ||
                        sink.SetState(State.Ready) == StateChangeReturn.Failure ||
                        mux.SetState(State.Ready) == StateChangeReturn.Failure ||
                        (videoTimestamper != null &&
                         videoTimestamper.SetState(State.Ready) == StateChangeReturn.Failure) ||
                        (videoEncoder != null && videoEncoder.SetState(State.Ready) == StateChangeReturn.Failure) ||
                        (videoEncoder != null && videoEncoder.SetState(State.Paused) == StateChangeReturn.Failure) ||
                        (videoTimestamper != null &&
                         videoTimestamper.SetState(State.Paused) == StateChangeReturn.Failure) ||
                        mux.SetState(State.Paused) == StateChangeReturn.Failure ||
                        sink.SetState(State.Paused) == StateChangeReturn.Failure)
                    {
                        LogTaskError(
                            "SeekClockTime",
                            "Unable to reset mux/video state before reusing pipeline.",
                            seekNs: seekNs
                        );

                        Dispose();
                        return false;
                    }
                }

                mp4Reader.SeekReset(seekNs);

                Volatile.Write(ref positionSeconds, seekNs);
                Volatile.Write(ref positionSeekSeconds, seekNs);

                InstallVideoStartProbe(seekNs);
                InstallVideoSegmentClipProbe(
                    accurate ? seekNs : ulong.MaxValue
                );

                SeekFlags seekFlags =
                    SeekFlags.Flush |
                    SeekFlags.KeyUnit |
                    SeekFlags.SnapAfter;

                if (accurate)
                    seekFlags |= SeekFlags.Accurate;

                using var multiqueue = bin.GetByName("mq");
                using var videoPad = multiqueue?.GetStaticPad("src_0");

                bool ok = videoPad != null && videoPad.SendEvent(
                    Event.NewSeek(
                        1.0,
                        Format.Time,
                        seekFlags,
                        SeekType.Set,
                        (long)seekNs,
                        SeekType.None,
                        -1
                    )
                );

                if (!ok)
                {
                    LogTaskError(
                        "SeekClockTime",
                        "Video seek event returned false.",
                        seekNs: seekNs
                    );

                    Dispose();
                    return false;
                }

                // после flushing seek тоже лучше дождаться ASYNC_DONE
                using var flushing = bus.TimedPopFiltered(
                    5_000_000_000UL,
                    MessageType.AsyncDone | MessageType.Error | MessageType.Eos
                );

                uint flushingType = BusReader.GetType(flushing);

                if (flushingType == BusReader.Error ||
                    flushingType == BusReader.Eos)
                {
                    LogTaskError(
                        "SeekClockTime",
                        "Flushing seek finished with error, EOS, or timeout.",
                        seekNs: seekNs,
                        messageType: flushingType
                    );

                    Dispose();
                    return false;
                }
            }
            else
            {
                mp4Reader.SeekReset();

                Volatile.Write(ref positionSeconds, 0);
                Volatile.Write(ref positionSeekSeconds, 0);
            }

            ret = pipeline.SetState(State.Playing);
            if (ret == StateChangeReturn.Failure)
            {
                LogTaskError(
                    "SeekClockTime",
                    "SetState(Playing) failed after seek reset.",
                    seekNs: seekNs
                );

                Dispose();
                return false;
            }

            if (ret == StateChangeReturn.Async)
            {
                using var msg = bus.TimedPopFiltered(
                    5_000_000_000UL,
                    MessageType.AsyncDone | MessageType.Error | MessageType.Eos
                );

                uint type = BusReader.GetType(msg);

                if (type == BusReader.Error ||
                    type == BusReader.Eos)
                {
                    LogTaskError(
                        "SeekClockTime",
                        "Pipeline playing transition finished with error, EOS, or timeout.",
                        seekNs: seekNs,
                        messageType: type
                    );

                    Dispose();
                    return false;
                }
            }

            IsFrozen = false;
            IsEos = false;

            if (!StartBusWatch())
            {
                DisposePipeline();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogTaskError("SeekClockTime", "Exception", ex);

            Dispose();
            return false;
        }
        finally
        {
            watchCts?.Dispose();
        }
    }
    #endregion

    #region EnsureSegment
    public bool EnsureSegment(int index, CancellationToken ct, int audio = 0)
    {
        int segmentIndex = index > 0 ? index : 0;
        bool segmentReadStarted = false;
        bool disposeTask = false;
        int readGeneration = -1;

        lock (pipelineLock)
        {
            if (Volatile.Read(ref pipelineStopping) != 0 ||
                IsDead ||
                (ct != default && ct.IsCancellationRequested))
            {
                return false;
            }

            if (statePlaying && pipeline == null)
                return false;

            ensureSegmentActive++;

            if (ensureSegmentActive == 1)
                ensureSegmentIdle.Reset();
        }

        try
        {
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

                if (bus == null || sink == null)
                {
                    LogTaskError(
                        "EnsureSegment",
                        "Initial pipeline was created without bus or output appsink.",
                        segmentIndex: index
                    );

                    disposeTask = true;
                    return false;
                }

                InstallSourceThrottleProbe();

                subsSinks.Clear();

                foreach (var track in subtitleTracks)
                {
                    var subSink = (GstApp.AppSink)bin.GetByName($"subs_{track.Index}");
                    if (subSink != null)
                        subsSinks[track.Index] = subSink;
                }

                var ret = pipeline.SetState(State.Playing);
                if (ret == StateChangeReturn.Failure)
                {
                    LogTaskError(
                        "EnsureSegment",
                        "Initial SetState(Playing) failed.",
                        segmentIndex: index
                    );

                    disposeTask = true;
                    return false;
                }

                if (ret == StateChangeReturn.Async)
                {
                    using var msg = bus.TimedPopFiltered(
                        5_000_000_000UL,
                        MessageType.AsyncDone | MessageType.Error | MessageType.Eos
                    );

                    uint type = BusReader.GetType(msg);

                    if (type == BusReader.Error ||
                        type == BusReader.Eos)
                    {
                        LogTaskError(
                            "EnsureSegment",
                            "Initial playing transition finished with error, EOS, or timeout.",
                            segmentIndex: index,
                            messageType: type
                        );

                        disposeTask = true;
                        return false;
                    }
                }

                if (!StartBusWatch())
                    return false;
            }
            #endregion

            if (index < 0 && InitMp4Ready)
                return true;

            if (index >= 0 && SegmentFileReady(segmentIndex))
                return true;

            if (index >= 0 && cueTimeline != null)
            {
                if (!cueTimeline.TryGetSegment(segmentIndex, out CueSegment targetSegment))
                    return false;

                mp4Reader.SetTargetSegment(
                    targetSegment.StartNs,
                    targetSegment.EndNs,
                    Math.Max(1UL, cueTimeline.TimestampScaleNs)
                );
            }

            lock (pipelineLock)
            {
                if (Volatile.Read(ref pipelineStopping) != 0 ||
                    IsDead ||
                    pipeline == null ||
                    sink == null)
                {
                    return false;
                }

                readGeneration = Volatile.Read(ref pipelineGeneration);
            }

            activeSegmentIndex = segmentIndex;
            activeSegmentStoreFailed = false;
            segmentReadStarted = true;

            long start = Stopwatch.GetTimestamp();
            var timeout = TimeSpan.FromSeconds(45);

            while (Stopwatch.GetElapsedTime(start) < timeout)
            {
                if (ct != default && ct.IsCancellationRequested)
                    return false;

                if (IsDead)
                    return false;

                if (readGeneration != Volatile.Read(ref pipelineGeneration))
                    return false;

                DrainSubtitles(ct, readGeneration);

                // 100 ms
                using var sample = sink.TryPullSample(100_000_000UL);

                if (IsDead)
                    return false;

                if (readGeneration != Volatile.Read(ref pipelineGeneration))
                    return false;

                using var buffer = sample?.GetBuffer();

                if (buffer == null)
                {
                    if (!IsEos)
                        continue;

                    // в _deferred может лежать полный segment
                    if (mp4Reader.TryProcessDeferred())
                    {
                        if (activeSegmentStoreFailed)
                            return false;

                        if (index >= 0 && SegmentFileReady(segmentIndex))
                            return true;
                    }

                    // Последний fragment может быть неполным:
                    // только moof, только часть mdat либо fragment одной дорожки
                    if (mp4Reader.TryBuildEndOfStreamRemainder())
                    {
                        if (activeSegmentStoreFailed)
                            return false;

                        if (index >= 0 && SegmentFileReady(segmentIndex))
                            return true;
                    }

                    return false;
                }

                nuint size = buffer.GetSize();
                if (size == 0)
                    continue;

                if (size > int.MaxValue)
                    throw new InvalidDataException("GStreamer sample is too large.");

                mp4Reader.Push(buffer, (int)size);

                if (activeSegmentStoreFailed)
                    return false;

                if (index < 0 && InitMp4Ready)
                    return true;

                if (index >= 0 && SegmentFileReady(segmentIndex))
                {
                    DrainSubtitles(ct, readGeneration);
                    return true;
                }
            }

            return false;
        }
        catch (OperationCanceledException) when (
            ct != default &&
            ct.IsCancellationRequested)
        {
            return false;
        }
        catch (ObjectDisposedException)
            when (
                IsDead ||
                IsFrozen ||
                (readGeneration >= 0 &&
                readGeneration != Volatile.Read(ref pipelineGeneration))
            )
        {
            return false;
        }
        catch (Exception ex)
        {
            LogTaskError(
                "EnsureSegment",
                "Unhandled exception while reading segment.",
                ex,
                segmentIndex: segmentIndex
            );

            disposeTask = true;
            return false;
        }
        finally
        {
            if (segmentReadStarted && activeSegmentIndex == segmentIndex)
                activeSegmentIndex = -1;

            if (segmentReadStarted)
                activeSegmentStoreFailed = false;

            lock (pipelineLock)
            {
                ensureSegmentActive--;

                if (ensureSegmentActive == 0)
                    ensureSegmentIdle.Set();
            }

            if (disposeTask)
                Dispose();
        }
    }
    #endregion

    #region EnsureInitAsync
    public async System.Threading.Tasks.Task<bool> EnsureInitAsync(int audio, CancellationToken cancellationToken)
    {
        if (InitMp4Ready)
            return true;

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (InitMp4Ready)
                return true;

            EnsureSegment(-1, cancellationToken, audio);
            return InitMp4Ready;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return InitMp4Ready;
        }
        finally
        {
            semaphore.Release();
        }
    }
    #endregion

    #region EnsureClientSegment
    public bool EnsureClientSegment(int index, CancellationToken ct)
    {
        if (index < 0 ||
            IsDead ||
            (cueTimeline != null && index >= cueTimeline.Count))
        {
            return false;
        }

        Volatile.Write(ref lastClientSegmentIndex, index);

        if (SegmentFileReady(index))
            return true;

        int readerIndex = Volatile.Read(ref readerSegmentIndex);

        // Если нужный сегмент позади физической позиции reader-а,
        // а в cache его нет — без реального seek его уже нельзя получить.
        if (readerIndex >= 0 && index <= readerIndex)
        {
            if (!SeekSegment(index))
                return false;

            Volatile.Write(ref readerSegmentIndex, index - 1);
            return EnsureSegment(index, ct);
        }

        int diff = index - readerIndex;

        if (CanReadForwardWithoutSeek(diff))
        {
            for (int next = readerIndex + 1; next < index; next++)
            {
                if (ct.IsCancellationRequested || IsDead)
                    return false;

                if (!EnsureSegment(next, ct))
                    return false;
            }

            return EnsureSegment(index, ct);
        }

        if (!SeekSegment(index))
            return false;

        Volatile.Write(ref readerSegmentIndex, index - 1);
        return EnsureSegment(index, ct);
    }
    #endregion

    #region QueueSegmentPrefetch
    public void QueueSegmentPrefetch(int currentIndex)
    {
        int generation = Volatile.Read(
            ref prefetchGeneration
        );

        if (currentIndex < 0 || IsDead || IsEos)
            return;

        int clientIndex = ClientSegmentIndex();

        if (clientIndex >= 0 && clientIndex > currentIndex)
            currentIndex = clientIndex;

        if (!NeedsSegmentPrefetch(currentIndex))
            return;

        if (Interlocked.CompareExchange(ref prefetchRunning, 1, 0) != 0)
        {
            Volatile.Write(ref prefetchRequested, 1);
            return;
        }

        var cts = new CancellationTokenSource();

        Volatile.Write(ref prefetchCts, cts);

        // CancelSegmentPrefetch мог выполниться между чтением
        if (generation != Volatile.Read(ref prefetchGeneration))
            cts.Cancel();

        int startedFrom = currentIndex;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            bool canceled = false;

            try
            {
                await PrefetchSegments(
                    startedFrom + 1,
                    generation,
                    cts.Token
                ).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            catch (Exception ex)
            {
                if (!IsDead && !cts.IsCancellationRequested)
                {
                    Serilog.Log.Warning(
                        ex,
                        "GStreamer segment prefetch failed."
                    );
                }
            }
            finally
            {
                canceled |= cts.IsCancellationRequested;

                if (ReferenceEquals(Volatile.Read(ref prefetchCts), cts))
                    Volatile.Write(ref prefetchCts, null);

                cts.Dispose();

                Volatile.Write(ref prefetchRunning, 0);
                bool restartRequested = Interlocked.Exchange(ref prefetchRequested, 0) != 0;

                int latestClientIndex = ClientSegmentIndex();
                bool sameGeneration = Volatile.Read(ref prefetchGeneration) == generation;

                if ((restartRequested ||
                     !canceled && latestClientIndex > startedFrom && sameGeneration) &&
                    !IsDead &&
                    !IsEos &&
                    !IsFrozen &&
                    NeedsSegmentPrefetch(latestClientIndex))
                {
                    QueueSegmentPrefetch(latestClientIndex);
                }
            }
        });
    }
    #endregion

    #region CancelSegmentPrefetch
    public void CancelSegmentPrefetch()
    {
        Interlocked.Increment(ref prefetchGeneration);
        Volatile.Write(ref prefetchRequested, 0);

        try
        {
            Volatile.Read(ref prefetchCts)?.Cancel();
        }
        catch { }
    }
    #endregion


    #region PrefetchSegments
    async System.Threading.Tasks.Task PrefetchSegments(
        int nextIndex,
        int generation,
        CancellationToken ct
    )
    {
        while (true)
        {
            if (ct.IsCancellationRequested || IsDead || IsEos)
                return;

            if (Volatile.Read(ref prefetchGeneration) != generation)
                return;

            int clientIndex = ClientSegmentIndex();
            if (clientIndex < 0)
                return;

            if (!NeedsSegmentPrefetch(clientIndex))
                return;

            int targetIndex = clientIndex + SegmentBuffer();

            if (nextIndex <= clientIndex)
                nextIndex = clientIndex + 1;

            if (nextIndex > targetIndex)
                return;

            if (SegmentFileReady(nextIndex))
            {
                nextIndex++;
                continue;
            }

            if (!CanReadFromCurrentReader(nextIndex))
                return;

            await semaphore.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                if (ct.IsCancellationRequested || IsDead || IsEos)
                    return;

                if (Volatile.Read(ref prefetchGeneration) != generation)
                    return;

                clientIndex = ClientSegmentIndex();
                if (clientIndex < 0)
                    return;

                if (!NeedsSegmentPrefetch(clientIndex))
                    return;

                targetIndex = clientIndex + SegmentBuffer();

                if (nextIndex <= clientIndex)
                    nextIndex = clientIndex + 1;

                if (nextIndex > targetIndex)
                    return;

                if (SegmentFileReady(nextIndex))
                {
                    nextIndex++;
                    continue;
                }

                if (!CanReadFromCurrentReader(nextIndex))
                    return;

                if (!EnsureSegment(nextIndex, ct))
                    return;
            }
            finally
            {
                semaphore.Release();
            }

            nextIndex++;

            await System.Threading.Tasks.Task.Yield();
        }
    }
    #endregion

    #region Helpers
    bool CanReadForwardWithoutSeek(int diff)
    {
        if (diff <= 0)
            return false;

        if (cueTimeline != null)
            return diff <= Math.Max(2, conf.segment_buffer);

        int segmentSeconds = Math.Max(1, conf.segment_seconds);
        int cutoff = Math.Max(2, conf.segment_buffer) * segmentSeconds;

        return (long)diff * segmentSeconds <= cutoff;
    }

    bool CanReadFromCurrentReader(int index)
    {
        int readerIndex = Volatile.Read(ref readerSegmentIndex);
        return (long)index == (long)readerIndex + 1;
    }
    #endregion
}
