using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using System.Net;

namespace LogUserRequest;

public class LogUserRequestMiddleware
{
    public static ConcurrentQueue<(LogModelSql jurnal, UserInfoModelSql unfo, HeaderModelSql header)> Queue = new();
    private static int _queueSize = 0;
    private static int _maxQueueSize = 20000;
    private static long _lastQueueOverflowLog = 0;
    private readonly RequestDelegate _next;
    private static readonly MemoryCache _rateLimitCache = new(new MemoryCacheOptions { SizeLimit = 100_000 });
    
    // Только служебные пути, которые НЕ нужно логировать
    private static readonly HashSet<string> _blacklistPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/.well-known", "/admin/health", "/admin/ping", "/testaccsdb", "/nws",
        "/lifeevents", "/proxyimg", "/lite/logrequest", 
        "/lite/events", "/nexthub?plugin", "/externalids",
        "/lampa-main", "/lite/withsearch", "/lite/mirage/trans/master.m3u8",
        "/timecode", "/bookmark", "/storage", "/sisi/bookmarks?box_mac", "/cub" 
    };
    
    private static readonly HashSet<string> _blacklistParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "box_mac"
    };
    
    private static readonly MemoryCache _skipPathCache = new(new MemoryCacheOptions 
    { 
        SizeLimit = 10000,
        ExpirationScanFrequency = TimeSpan.FromMinutes(5)
    });
    
    public LogUserRequestMiddleware(RequestDelegate next) => _next = next;

    private static bool IsPrivateIP(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        if (!IPAddress.TryParse(ip, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
        }
        return false;
    }

    private static string GetRealIP(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var headers = context.Request.Headers;
        if (!IsPrivateIP(remoteIp)) return remoteIp;
        if (headers.ContainsKey("CF-Connecting-IP"))
        {
            var cfIp = headers["CF-Connecting-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(cfIp)) return cfIp;
        }
        if (headers.ContainsKey("X-Forwarded-For"))
        {
            var forwarded = headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwarded)) return forwarded.Split(',')[0].Trim();
        }
        return remoteIp;
    }
    
    private static bool ShouldLog(string path, string query)
    {
        var cacheKey = $"skip:{path}";
        if (_skipPathCache.TryGetValue(cacheKey, out bool shouldSkip) && shouldSkip)
            return false;
        
        foreach (var black in _blacklistPaths)
        {
            if (path.StartsWith(black, StringComparison.OrdinalIgnoreCase) ||
                (black.Contains('?') && (path + query).Contains(black, StringComparison.OrdinalIgnoreCase)))
            {
                _skipPathCache.Set(cacheKey, true, TimeSpan.FromMinutes(10));
                return false;
            }
        }
        
        foreach (var param in _blacklistParams)
        {
            if (query.Contains(param + "=", StringComparison.OrdinalIgnoreCase) ||
                query.Contains("&" + param + "=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        
        return true;
    }

    public async Task InvokeAsync(HttpContext context)
    {

        await _next(context);
        
        try
        {
            var path = context.Request.Path.Value ?? "";
            var query = context.Request.QueryString.Value ?? "";
            
            if (path == "/" || path == "/favicon.ico" ||
                path.EndsWith(".js") || path.EndsWith(".css") || path.EndsWith(".svg") || 
                path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".woff") || 
                path.EndsWith(".woff2") || path.EndsWith(".ogg") ||
                path.StartsWith("/proxy/"))
            {
                return;
            }
            
            if (!ShouldLog(path, query))
            {
                return;
            }
            
            var realIP = GetRealIP(context);
            var headers = context.Request.Headers;

            if (_rateLimitCache.TryGetValue(realIP, out _))
            {
                return;
            }
            
            _rateLimitCache.Set(realIP, DateTime.UtcNow, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(33),
                Size = 1
            });

            if (realIP == "127.0.0.1" || realIP == "::1")
            {
                return;
            }

            string userAgent = headers["User-Agent"].FirstOrDefault() ?? "unknown";
            if (userAgent.Length > 512) userAgent = userAgent[..512] + "...[truncated]";

            string userUid = context.Request.Query["uid"].FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(userUid)) userUid = context.Request.Cookies["uid"] ?? "";
            if (string.IsNullOrEmpty(userUid)) userUid = "anonymous";
            if (userUid.Length > 256) userUid = userUid[..256];

            string country = "";
            if (context.Request.Headers.TryGetValue("CF-IPCountry", out var cfCountry))
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

            var fullUri = path + query;
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
                status_code = context.Response.StatusCode
            };

            if (Interlocked.Increment(ref _queueSize) <= _maxQueueSize)
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
        catch
        {

        }
    }

    public static void DequeueItem() => Interlocked.Decrement(ref _queueSize);
}

public static class LogUserRequestMiddlewareExtensions
{
    public static IApplicationBuilder UseLogUserRequest(this IApplicationBuilder app)
        => app.UseMiddleware<LogUserRequestMiddleware>();
}