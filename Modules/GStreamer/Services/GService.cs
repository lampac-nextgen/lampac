using GStreamer.Models;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GStreamer.Services;

public static class GService
{
    public readonly record struct TaskResult(GStask task, string error);

    public readonly record struct ProbeResult(ProbeInfo probe, string error);

    static ConcurrentDictionary<ulong, GStask> tasks = new();
    static readonly ConcurrentDictionary<ulong, TaskCompletionSource<bool>> waiters = new();
    static readonly ConcurrentDictionary<string, Lazy<Task<ProbeResult>>> probeWaiters = new(StringComparer.Ordinal);
    static readonly object taskAddLock = new();

    static int cleanupRunning;
    static readonly Timer cleanupTimer = new(
        static _ => CleanupInactive(),
        null,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(1)
    );

    #region GetOrAdd
    public static async Task<TaskResult> GetOrAdd(string sourceUrl, string uid, int audio = 0)
    {
        if (string.IsNullOrEmpty(sourceUrl) || string.IsNullOrEmpty(uid))
            return new(null, "uid");

        var hash = Fnv1a.Hash(sourceUrl);
        Fnv1a.Append(ref hash, uid);
        Fnv1a.Append(ref hash, audio);

        ulong id = hash.H1;

        if (tasks.TryGetValue(id, out var task) && !task.IsDead)
        {
            task.UpdateLastActive();
            return new(task, null);
        }

        var ownWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = waiters.GetOrAdd(id, ownWaiter);

        if (!ReferenceEquals(waiter, ownWaiter))
        {
            if (await waiter.Task.ConfigureAwait(false) &&
                tasks.TryGetValue(id, out task) &&
                !task.IsDead)
            {
                task.UpdateLastActive();
                return new(task, null);
            }

            return new(null, "probe");
        }
        else
        {
            try
            {
                if (tasks.TryGetValue(id, out task) && !task.IsDead)
                {
                    task.UpdateLastActive();
                    return new(task, null);
                }

                sourceUrl = Regex.Replace(sourceUrl, "/stream/[^\\?]+", "/stream");

                if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    string.IsNullOrEmpty(uri.Host))
                {
                    return new(null, "Uri");
                }

                string probeKey = $"ProbeInfo:{uri.AbsoluteUri}";

                var httpHeaders = await Http.ResponseHeaders(sourceUrl, timeoutSeconds: 45);
                if (httpHeaders == null)
                    return new(null, "ResponseHeaders");

                #region sourceUrl
                Uri requestUri = httpHeaders.RequestMessage?.RequestUri;
                if (requestUri == null)
                    return new(null, "RequestUri");

                bool redirect =
                    (int)httpHeaders.StatusCode == 301 ||
                    (int)httpHeaders.StatusCode == 302 ||
                    (int)httpHeaders.StatusCode == 307 ||
                    (int)httpHeaders.StatusCode == 308;

                if (redirect && httpHeaders.Headers.Location != null)
                    sourceUrl = new Uri(requestUri, httpHeaders.Headers.Location).AbsoluteUri;
                else
                    sourceUrl = requestUri.AbsoluteUri;

                if (string.IsNullOrEmpty(sourceUrl))
                    return new(null, "sourceUrl");
                #endregion

                var probeResult = await GetProbeInfo(probeKey, sourceUrl).ConfigureAwait(false);
                if (probeResult.probe == null)
                    return new(null, probeResult.error);

                var probe = probeResult.probe;

                if (!probe.Tracks.Exists(i => i.Type == "audio"))
                    return new(null, "audio track not found");

                var conf = ModInit.conf;
                if (ModInit.conf.conf_uids != null && ModInit.conf.conf_uids.TryGetValue(uid, out var uidconf))
                    conf = uidconf;

                if (conf.hdr_to_sdr && probe.Video?.IsHdr == true)
                {
                    if (probe.Video.VideoTransfer != VideoTransfer.Pq &&
                        probe.Video.VideoTransfer != VideoTransfer.Hlg)
                    {
                        return new(null, "HDR tone mapping requires a PQ or HLG base layer");
                    }

                    if (!HdrToneMappingBackend.IsAvailable)
                        return new(null, HdrToneMappingBackend.UnavailableError);
                }

                bool transcodeAVI = probe.IsAVI && conf.transcodeAVI;

                if (!probe.IsMatroskaOrWebM && !transcodeAVI)
                    return new(null, $"not matroska/webm: {probe.ContainerCapsName ?? probe.ContainerName ?? "unknown"}");

                bool supportedVideo =
                    probe.IsH264 ||
                    probe.IsH265 ||
                    probe.IsAV1 ||
                    probe.IsVP9 ||
                    probe.IsVP8 && conf.transcodeVP8 ||
                    transcodeAVI && probe.Video != null;

                if (!supportedVideo)
                    return new(null, "not mp4");

                long? contentLength = httpHeaders.Content.Headers.ContentLength;
                bool videoTranscoded =
                    conf.hdr_to_sdr && probe.Video?.IsHdr == true ||
                    probe.IsH264 && conf.transcodeH264 ||
                    probe.IsH265 && conf.transcodeH265 ||
                    probe.IsAV1 && conf.transcodeAV1 ||
                    probe.IsVP9 && conf.transcodeVP9 ||
                    probe.IsVP8 && conf.transcodeVP8 ||
                    probe.IsAVI && conf.transcodeAVI;

                CueTimeline cueTimeline = null;

                if (probe.IsMatroskaOrWebM && !videoTranscoded)
                {
                    cueTimeline = await MatroskaCueReader.Read(
                        sourceUrl,
                        contentLength,
                        probe.DurationNs
                    ).ConfigureAwait(false);
                }

                task = new GStask(
                    probe,
                    conf,
                    sourceUrl,
                    id,
                    uid,
                    audio,
                    contentLength,
                    cueTimeline
                );

                var removedTasks = new List<GStask>();

                lock (taskAddLock)
                {
                    foreach (var tk in tasks)
                    {
                        if (tk.Value.user_uid == uid && tk.Key != id)
                        {
                            if (tasks.TryRemove(tk.Key, out var removed))
                                removedTasks.Add(removed);
                        }
                    }

                    int maxTasks = ModInit.conf.maxTasks;

                    while (maxTasks > 0 && tasks.Count >= maxTasks)
                    {
                        ulong oldestId = 0;
                        GStask oldestTask = null;
                        DateTime oldestLastActive = DateTime.MaxValue;

                        foreach (var item in tasks)
                        {
                            DateTime lastActive = item.Value.lastActive;
                            if (oldestTask == null || lastActive < oldestLastActive)
                            {
                                oldestId = item.Key;
                                oldestTask = item.Value;
                                oldestLastActive = lastActive;
                            }
                        }

                        if (oldestTask == null)
                            break;

                        if (tasks.TryRemove(oldestId, out var removed))
                            removedTasks.Add(removed);
                    }

                    tasks[id] = task;
                }

                foreach (var removed in removedTasks)
                    removed.Dispose();

                return new(task, null);
            }
            finally
            {
                bool success = tasks.TryGetValue(id, out var completed) && !completed.IsDead;

                ownWaiter.TrySetResult(success);
                waiters.TryRemove(id, out _);
            }
        }
    }
    #endregion

    #region Get
    public static GStask Get(ulong id)
    {
        if (tasks.TryGetValue(id, out var task) && !task.IsDead)
        {
            task.UpdateLastActive();
            return task;
        }

        return null;
    }
    #endregion

    #region TryRemove
    public static bool TryRemove(ulong id)
    {
        if (tasks.TryRemove(id, out var task))
        {
            task.Dispose();
            return true;
        }

        return false;
    }
    #endregion


    #region GetProbe
    public static async Task<ProbeResult> GetProbe(string sourceUrl)
    {
        if (string.IsNullOrEmpty(sourceUrl))
            return new(null, "Uri");

        sourceUrl = Regex.Replace(sourceUrl, "/stream/[^\\?]+", "/stream");

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            return new(null, "Uri");
        }

        sourceUrl = uri.AbsoluteUri;
        string probeKey = $"ProbeInfo:{sourceUrl}";

        return await GetProbeInfo(probeKey, sourceUrl).ConfigureAwait(false);
    }

    static async Task<ProbeResult> GetProbeCore(string sourceUrl, string probeKey)
    {
        var hybridCache = HybridCache.Get();
        if (hybridCache.TryGetValue(probeKey, out ProbeInfo cachedProbe))
            return new(cachedProbe, null);

        var probe = await GSProbe.Get(sourceUrl);
        if (probe == null)
            return new(null, "probe");

        hybridCache.Set(probeKey, probe, TimeSpan.FromDays(1));

        return new(probe, null);
    }
    #endregion

    #region GetProbeInfo
    static async Task<ProbeResult> GetProbeInfo(string probeKey, string sourceUrl)
    {
        var hybridCache = HybridCache.Get();
        if (hybridCache.TryGetValue(probeKey, out ProbeInfo cachedProbe))
            return new(cachedProbe, null);

        var ownWaiter = new Lazy<Task<ProbeResult>>(
            () => GetProbeCore(sourceUrl, probeKey),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        var waiter = probeWaiters.GetOrAdd(probeKey, ownWaiter);

        try
        {
            return await waiter.Value.ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(waiter, ownWaiter))
                probeWaiters.TryRemove(probeKey, out _);
        }
    }
    #endregion


    #region Cleanup/Dispose
    static void CleanupInactive()
    {
        if (Interlocked.Exchange(ref cleanupRunning, 1) == 1)
            return;

        try
        {
            var now = DateTime.UtcNow;

            foreach (var item in tasks)
            {
                var id = item.Key;
                var task = item.Value;
                var lastActive = task.lastActive;

                if (now > lastActive.AddMinutes(60) || item.Value.IsDead)
                {
                    if (tasks.TryRemove(id, out var removed))
                        removed.Dispose();
                }
                else if (!item.Value.IsFrozen && now > lastActive.AddMinutes(ModInit.conf.inactiveMinutes))
                {
                    item.Value.Frozen();
                }
            }
        }
        catch { }
        finally
        {
            Volatile.Write(ref cleanupRunning, 0);
        }
    }

    public static void Dispose()
    {
        foreach (var item in tasks)
            item.Value.Dispose();
    }
    #endregion
}
