using GStreamer.Models;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace GStreamer.Services;

public static class GService
{
    static ConcurrentDictionary<ulong, GStask> tasks = new();

    static int cleanupRunning;
    static readonly Timer cleanupTimer = new(
        static _ => CleanupInactive(),
        null,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(1)
    );

    public static async Task<GStask> GetOrAdd(string sourceUrl, string uid, int audio = 0)
    {
        if (string.IsNullOrEmpty(sourceUrl) || string.IsNullOrEmpty(uid))
            return null;

        var hash = Fnv1a.Hash(sourceUrl);
        Fnv1a.Append(ref hash, uid);
        Fnv1a.Append(ref hash, audio);

        if (tasks.TryGetValue(hash.H1, out var task) && !task.IsDead)
        {
            task.UpdateLastActive();
            return task;
        }

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException("Invalid URL", nameof(sourceUrl));
        }

        sourceUrl = await Http.GetLocation(sourceUrl, timeoutSeconds: 45);
        if (sourceUrl == null)
            return null;

        var hybridCache = HybridCache.Get();

        string probeKey = $"ProbeInfo:{sourceUrl}";
        if (!hybridCache.TryGetValue(probeKey, out ProbeInfo probe))
        {
            probe = await GSProbe.Get(sourceUrl);
            //Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(probe, Newtonsoft.Json.Formatting.Indented));
            if (probe == null)
                return null;

            hybridCache.Set(probeKey, probe, TimeSpan.FromDays(10));
        }

        if (!probe.IsH264 && !probe.IsH265 && !probe.IsAV1 && !probe.IsVP9)
            return null;

        var conf = ModInit.conf;
        if (ModInit.conf.conf_uids != null && ModInit.conf.conf_uids.TryGetValue(uid, out var uidconf))
            conf = uidconf;

        task = new GStask(probe, conf, sourceUrl, hash.H1, uid, audio);

        if (tasks.TryAdd(hash.H1, task))
        {
            foreach (var tk in tasks)
            {
                if (tk.Value.user_uid == uid && tk.Key != hash.H1)
                {
                    if (tasks.TryRemove(tk.Key, out var removed))
                        removed.Dispose();
                }
            }

            return task;
        }
        else
        {
            return tasks[hash.H1];
        }
    }

    public static GStask Get(ulong id)
    {
        if (tasks.TryGetValue(id, out var task) && !task.IsDead)
        {
            task.UpdateLastActive();
            return task;
        }

        return null;
    }

    public static bool TryRemove(ulong id)
    {
        if (tasks.TryRemove(id, out var task))
        {
            task.Dispose();
            return true;
        }

        return false;
    }

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

                if (now > task.lastActive.AddMinutes(60) || item.Value.IsDead)
                {
                    if (tasks.TryRemove(id, out var removed))
                        removed.Dispose();
                }
                else if (!item.Value.IsFrozen && now > task.lastActive.AddMinutes(ModInit.conf.inactiveMinutes))
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
}
