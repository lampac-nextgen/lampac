using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace LogUserRequest;

public static class LogUserRequestListener
{
    public static readonly ConcurrentQueue<(LogModelSql jurnal, UserInfoModelSql unfo, HeaderModelSql header)> Queue = new();
    private static int _queueSize = 0;
    private const int MaxQueueSize = 20000;
    private static long _lastQueueOverflowLog = 0;
    private static long _lastErrorLog = 0;
    private static readonly MemoryCache _rateLimitCache = new(new MemoryCacheOptions { SizeLimit = 100_000 });

    private static readonly string[] _skipPrefixes =
    {
        "/.well-known",
        "/admin/health",
        "/admin/ping",
        "/testaccsdb",
        "/nws",
        "/lifeevents",
        "/proxyimg",
        "/lite/logrequest",
        "/lite/events",
        "/nexthub",
        "/externalids",
        "/lampa-main",
        "/lite/withsearch",
        "/timecode",
        "/bookmark",
        "/storage",
        "/cub",
		"/cub/", 
        "/sisi/bookmarks",
        "/proxy/"
    };

    private static readonly string[] _skipExtensions =
    {
        ".js", ".css", ".svg", ".png", ".jpg", ".jpeg",
        ".woff", ".woff2", ".ogg", ".ico", ".map"
    };

    public static void DequeueItem() => Interlocked.Decrement(ref _queueSize);

    private static bool ShouldLogError()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastErrorLog);
        if (nowTicks - last > TimeSpan.FromMinutes(1).Ticks)
        {
            Interlocked.Exchange(ref _lastErrorLog, nowTicks);
            return true;
        }
        return false;
    }

    public static Task<bool> InvokeAsync(bool first, EventMiddleware e)
    {
        if (first)
            return Task.FromResult(true);

        var httpContext = e.httpContext;
        var requestInfo = httpContext.Features.Get<RequestModel>();

        if (requestInfo == null)
            return Task.FromResult(true);

        if (requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
            return Task.FromResult(true);

        try
        {
            var path = httpContext.Request.Path.Value ?? "";

            // Отсев статики по расширениям
            foreach (var ext in _skipExtensions)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(true);
            }

            // Отсев по префиксам
            foreach (var prefix in _skipPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(true);
            }

            var realIP = requestInfo.IP;

            if (string.IsNullOrEmpty(realIP) || realIP == "127.0.0.1" || realIP == "::1")
                return Task.FromResult(true);

            if (_rateLimitCache.TryGetValue(realIP, out _))
                return Task.FromResult(true);

            _rateLimitCache.Set(realIP, DateTime.UtcNow, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(33),
                Size = 1
            });

            string userAgent = httpContext.Request.Headers["User-Agent"].FirstOrDefault() ?? "unknown";
            if (userAgent.Length > 512) userAgent = userAgent[..512] + "...[truncated]";

            string userUid = httpContext.Request.Query["uid"].FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(userUid)) userUid = httpContext.Request.Cookies["uid"] ?? "";
            if (string.IsNullOrEmpty(userUid)) userUid = "anonymous";
            if (userUid.Length > 256) userUid = userUid[..256];

            string country = "";
            if (httpContext.Request.Headers.TryGetValue("CF-IPCountry", out var cfCountry))
                country = cfCountry.ToString();

            static string GetHash(string input)
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = SHA256.HashData(bytes);
                return Convert.ToHexString(hash).ToLower();
            }

            var unfo = new UserInfoModelSql
            {
                Id = GetHash($"{realIP}:{country}:{userAgent}"),
                IP = realIP,
                Country = country,
                UserAgent = userAgent
            };

            var header = new HeaderModelSql { Headers = new Dictionary<string, string>() };
            header.Id = "";

            var fullUri = path + httpContext.Request.QueryString;
            if (fullUri.Length > 2048) fullUri = fullUri[..2048];

            string balancer = "";
            if (path.StartsWith("/lite/"))
                balancer = path[6..].Split('/', '?')[0];
            else if (path.StartsWith("/rc/"))
                balancer = path[4..].Split('/', '?')[0];

            var jurnal = new LogModelSql
            {
                time = DateTime.UtcNow,
                uri = fullUri,
                uid = userUid,
                duration_ms = 0,
                balancer = balancer,
                status_code = httpContext.Response?.StatusCode ?? 0
            };

            if (Interlocked.Increment(ref _queueSize) <= MaxQueueSize)
            {
                try { Queue.Enqueue((jurnal, unfo, header)); }
                catch { Interlocked.Decrement(ref _queueSize); }
            }
            else
            {
                Interlocked.Decrement(ref _queueSize);
                var nowTicks = DateTime.UtcNow.Ticks;
                var last = Interlocked.Read(ref _lastQueueOverflowLog);
                if (nowTicks - last > TimeSpan.FromMinutes(1).Ticks)
                {
                    if (Interlocked.CompareExchange(ref _lastQueueOverflowLog, nowTicks, last) == last)
                    {
                        Console.WriteLine($"[LogUserRequest] Queue overflow, dropping logs. Size: {_queueSize}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogError())
                Console.WriteLine($"[LogUserRequest] Listener error: {ex}");
        }

        return Task.FromResult(true);
    }
}