using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Telemetry.Models;

namespace Telemetry;

public static class RequestListener
{
    public static readonly ConcurrentQueue<(LogModelSql log, UserInfoModelSql user)> Queue = new();
    private static int _queueSize = 0;
    private const int MaxQueueSize = 50000;

    private static readonly MemoryCache _rateLimitCache = new(new MemoryCacheOptions { SizeLimit = 100_000 });
    private static readonly MemoryCache _balancerDedupCache = new(new MemoryCacheOptions { SizeLimit = 50_000 });
    private static readonly MemoryCache _userIdCache = new(new MemoryCacheOptions { SizeLimit = 50_000 });

    private static readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "database", "Telemetry", "settings.json");

    public static volatile string[] SkipPrefixes = {
        "/.well-known", "/admin/health", "/admin/ping",
        "/lite/telemetry", "/telemetry", "/adminpanel", "/stats", "/weblog",
        "/storage/", "/timecode/", "/bookmark/", "/tmdb/api/", "/lite/events", "/externalids", "/sisi"
    };
    public static volatile string[] SkipExtensions = { ".js", ".css", ".svg", ".png", ".jpg", ".woff", ".ico" };
    public static volatile string[] RegexFilters = { };
    private static volatile HashSet<string> _blacklistIps = new();
    public static volatile int RateLimitMs = 33;

    public static string[] BlacklistIps
    {
        get => _blacklistIps.ToArray();
        set => _blacklistIps = new HashSet<string>(value, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsBlacklistedIp(string ip) => _blacklistIps.Contains(ip);

    public static void DequeueItem() => Interlocked.Decrement(ref _queueSize);

    public static Task<bool> InvokeAsync(bool first, EventMiddleware e)
    {
        if (first)
        {
            e.httpContext.Items["Telemetry_StartTime"] = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        var httpContext = e.httpContext;
        var requestInfo = httpContext.Features.Get<RequestModel>();

        if (requestInfo == null || requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
            return Task.FromResult(true);

        try
        {
            var path = httpContext.Request.Path.Value ?? "";

            // Configurable Regex filters with ReDoS protection (timeout 100ms)
            try
            {
                if (RegexFilters.Any(pattern => Regex.IsMatch(path, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100))))
                    return Task.FromResult(true);
            }
            catch (RegexMatchTimeoutException)
            {
                // ReDoS protection triggered, skip this request and log warning
                return Task.FromResult(true);
            }

            // Extension filter
            if (SkipExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(true);

            // Prefix filter
            if (SkipPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(true);

            var realIP = requestInfo.IP;
            if (string.IsNullOrEmpty(realIP) || realIP == "127.0.0.1" || realIP == "::1")
                return Task.FromResult(true);

            // IP Blacklist check
            if (IsBlacklistedIp(realIP))
                return Task.FromResult(true);

            // Rate limit
            if (RateLimitMs > 0)
            {
                var rateLimitKey = $"{realIP}:{path}";
                if (_rateLimitCache.TryGetValue(rateLimitKey, out _))
                    return Task.FromResult(true);

                _rateLimitCache.Set(rateLimitKey, true, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(RateLimitMs),
                    Size = 1
                });
            }

            string userAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown";
            if (userAgent.Length > 1024) userAgent = userAgent[..1024];

            string userUid = httpContext.Request.Query["uid"].FirstOrDefault() ?? httpContext.Request.Cookies["uid"] ?? "anonymous";
            if (userUid.Length > 256) userUid = userUid[..256];

            var cacheKey = $"{realIP}:{userAgent}";
            if (!_userIdCache.TryGetValue(cacheKey, out string? userId))
            {
                userId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))).ToLower();
                _userIdCache.Set(cacheKey, userId, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2), Size = 1 });
            }

            var user = new UserInfoModelSql
            {
                Id = userId!,
                Ip = realIP,
                UserAgent = userAgent
            };

            var fullUri = path + httpContext.Request.QueryString;
            if (fullUri.Length > 2048) fullUri = fullUri[..2048];

            string balancer = "";
            if (path.StartsWith("/lite/")) balancer = path[6..].Split('/', '?')[0];
            else if (path.StartsWith("/rc/")) balancer = path[4..].Split('/', '?')[0];
            else
            {
                // Parse 18+ (sisi) modules which sit at the root
                string[] adultRoutes = { "bgs", "chu", "elo", "epr", "hqr", "phub", "phubgay", "phubsml", "phubprem", "ptx", "runetki", "sbg", "tizam", "xmr", "xmrgay", "xmrsml", "xnx", "xds", "xdsgay", "xdssml", "xdsred", "nexthub" };
                var rootSegment = path.TrimStart('/').Split('/', '?')[0].ToLowerInvariant();

                if (adultRoutes.Contains(rootSegment))
                {
                    balancer = "18+ " + rootSegment;
                }
            }

            // Filter out system endpoints that are not actual movie balancers
            string[] systemEndpoints = { "events", "sync", "auth", "ws", "subs", "vtt", "search", "suggest", "torrent", "tracker", "info", "mdl" };
            if (systemEndpoints.Contains(balancer.ToLowerInvariant()))
            {
                balancer = "";
            }

            // Deduplication for balancers (First + Last within 30 seconds)
            if (!string.IsNullOrEmpty(balancer))
            {
                var dedupKey = $"{userUid}:{balancer}";
                var lastSeen = _balancerDedupCache.Get<DateTime?>(dedupKey);

                if (lastSeen.HasValue && (DateTime.UtcNow - lastSeen.Value).TotalSeconds < 30)
                {
                    return Task.FromResult(true);
                }
                else
                {
                    _balancerDedupCache.Set(dedupKey, DateTime.UtcNow, new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromSeconds(30),
                        Size = 1
                    });
                }
            }

            int durationMs = 0;
            if (httpContext.Items.TryGetValue("Telemetry_StartTime", out var startTimeObj) && startTimeObj is DateTime startTime)
            {
                durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            }

            string? mTitle = null, mTmdb = null, mKp = null, mImdb = null;
            bool mIsTv = false;

            var reqQuery = httpContext.Request.Query;
            if (reqQuery.TryGetValue("title", out var titleVal)) mTitle = titleVal.ToString();
            if (reqQuery.TryGetValue("id", out var idVal)) mTmdb = idVal.ToString();
            if (reqQuery.TryGetValue("kinopoisk_id", out var kpVal)) mKp = kpVal.ToString();
            if (reqQuery.TryGetValue("imdb_id", out var imdbVal)) mImdb = imdbVal.ToString();
            if (reqQuery.TryGetValue("serial", out var serialVal)) mIsTv = serialVal.ToString() == "1" || serialVal.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

            var log = new LogModelSql
            {
                Time = DateTime.UtcNow,
                Uri = fullUri,
                Uid = userUid,
                UnfoId = user.Id,
                DurationMs = durationMs,
                Balancer = balancer,
                StatusCode = httpContext.Response?.StatusCode ?? 0,
                MovieTitle = mTitle,
                TmdbId = mTmdb,
                KpId = mKp,
                ImdbId = mImdb,
                IsTv = mIsTv
            };

            if (Interlocked.Increment(ref _queueSize) <= MaxQueueSize)
            {
                try { Queue.Enqueue((log, user)); }
                catch { Interlocked.Decrement(ref _queueSize); }
            }
            else
            {
                Interlocked.Decrement(ref _queueSize);
                // Dropped
            }
        }
        catch { }

        return Task.FromResult(true);
    }

    public static void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var data = new Dictionary<string, object>
            {
                ["prefixes"] = SkipPrefixes,
                ["extensions"] = SkipExtensions,
                ["regexes"] = RegexFilters,
                ["ips"] = BlacklistIps,
                ["rateLimit"] = RateLimitMs
            };
            File.WriteAllText(_settingsPath, System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telemetry] SaveSettings Error: {ex.Message}");
        }
    }

    public static void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var json = File.ReadAllText(_settingsPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("prefixes", out var p)) SkipPrefixes = p.EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (root.TryGetProperty("extensions", out var e2)) SkipExtensions = e2.EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (root.TryGetProperty("regexes", out var r)) RegexFilters = r.EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (root.TryGetProperty("ips", out var i)) BlacklistIps = i.EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (root.TryGetProperty("rateLimit", out var rl)) RateLimitMs = rl.GetInt32();

            Console.WriteLine("[Telemetry] Settings loaded from disk.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telemetry] LoadSettings Error: {ex.Message}");
        }
    }
}
