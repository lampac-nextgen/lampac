using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models.Base;
using System;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares;

public class RequestInfo
{
    #region static
    private readonly RequestDelegate _next;

    public RequestInfo(RequestDelegate next)
    {
        _next = next;
    }
    #endregion

    public Task Invoke(HttpContext httpContext)
    {
        string path = httpContext.Request.Path.Value;

        bool IsWsRequest = path.StartsWith("/nws", StringComparison.OrdinalIgnoreCase);
        bool IsProxyImg = path.StartsWith("/proxyimg", StringComparison.OrdinalIgnoreCase);
        bool IsProxyRequest = path.StartsWith("/proxy/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/proxy-dash/", StringComparison.OrdinalIgnoreCase);

        if (CoreInit.conf.openstat.enable)
        {
            RequestInfoStats.Increment(
                IsWsRequest
                ? RequestStatsType.Nws
                : IsProxyRequest
                    ? RequestStatsType.Proxy
                    : IsProxyImg
                        ? RequestStatsType.Img
                        : RequestStatsType.Request
            );
        }

        bool IsLocalRequest = false;
        string cf_country = null;
        string clientIp = httpContext.Connection.RemoteIpAddress.ToString();

        bool IsLocalIp = IsWsRequest || IsProxyImg || IsProxyRequest
            ? false
            : Shared.Services.Utilities.IPNetwork.IsLocalIp(clientIp);

        if (httpContext.Request.Headers.TryGetValue("lcrqpasswd", out StringValues _localpasswd) && _localpasswd.Count > 0)
        {
            if (!CoreInit.conf.WAF.allowExternalIpAccessToLocalRequest && CoreInit.conf.listen.localhost == "127.0.0.1" && !IsLocalIp)
                return httpContext.Response.WriteAsync("listen.localhost");

            if (!CoreInit.IsLocalPasswd(_localpasswd[0]))
                return httpContext.Response.WriteAsync("error passwd");

            IsLocalRequest = true;

            if (httpContext.Request.Headers.TryGetValue("x-client-ip", out StringValues xip) && xip.Count > 0)
            {
                if (!string.IsNullOrEmpty(xip[0]))
                    clientIp = xip[0];
            }
        }
        else if (!IsLocalIp && CoreInit.conf.listen.frontend == "cloudflare")
        {
            #region cloudflare
            if (Program.cloudflare_ips != null && Program.cloudflare_ips.Count > 0)
            {
                try
                {
                    IPAddress clientIPAddress = httpContext.Connection.RemoteIpAddress;

                    foreach (var cf in Program.cloudflare_ips)
                    {
                        if (cf.Contains(clientIPAddress))
                        {
                            if (httpContext.Request.Headers.TryGetValue("CF-Connecting-IP", out StringValues xip) && xip.Count > 0)
                            {
                                if (!string.IsNullOrEmpty(xip[0]))
                                    clientIp = xip[0];
                            }

                            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-Proto", out StringValues xfp) && xfp.Count > 0)
                            {
                                if (xfp[0] == "http" || xfp[0] == "https")
                                    httpContext.Request.Scheme = xfp[0];
                            }

                            if (httpContext.Request.Headers.TryGetValue("CF-IPCountry", out StringValues xcountry) && xcountry.Count > 0)
                            {
                                if (!string.IsNullOrEmpty(xcountry[0]))
                                    cf_country = xcountry[0];
                            }

                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId}", "RequestInfo", "id_1ijosbv0");
                }
            }
            #endregion
        }

        var req = new RequestModel()
        {
            IsLocalIp = IsLocalIp,
            IsLocalRequest = IsLocalRequest,
            IsWsRequest = IsWsRequest,
            IsProxyRequest = IsProxyRequest,
            IsProxyImg = IsProxyImg,
            IP = clientIp,
            Country = cf_country,
            UserAgent = httpContext.Request.Headers.UserAgent
        };

        if (CoreInit.conf.kit.enable)
        {
            if (httpContext.Request.Headers.TryGetValue("X-Kit-AesGcm", out StringValues aesGcmKey) && aesGcmKey.Count > 0)
                req.AesGcmKey = aesGcmKey;
        }

        if (!string.IsNullOrEmpty(CoreInit.conf.accsdb.domainId_pattern))
        {
            string uid = Regex.Match(httpContext.Request.Host.Host, CoreInit.conf.accsdb.domainId_pattern).Groups[1].Value;
            req.user = CoreInit.conf.accsdb.findUser(uid);
            req.user_uid = uid;

            if (req.user == null)
                return httpContext.Response.WriteAsync("user not found");

            req.@params = CoreInit.conf.accsdb.@params;

            httpContext.Features.Set(req);
            return _next(httpContext);
        }
        else
        {
            if (!IsWsRequest)
            {
                req.user = CoreInit.conf.accsdb.findUser(httpContext, out string uid);
                req.user_uid = uid;

                if (req.user != null)
                    req.@params = CoreInit.conf.accsdb.@params;

                if (string.IsNullOrEmpty(req.user_uid))
                    req.user_uid = getuid(httpContext);
            }

            if (CoreInit.conf.kit.uidIdentity)
                req.AesGcmKey = req.user_uid;

            httpContext.Features.Set(req);
            return _next(httpContext);
        }
    }


    #region getuid
    static readonly string[] uids = ["token", "account_email", "uid", "box_mac"];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string getuid(HttpContext httpContext)
    {
        foreach (string id in uids)
        {
            if (httpContext.Request.Query.TryGetValue(id, out StringValues val) && val.Count > 0)
            {
                ReadOnlySpan<char> value = val[0];
                if (value.IsEmpty)
                    continue;

                if (!CoreInit.conf.BaseModule.ValidateIdentity)
                    return val[0];

                foreach (char ch in value)
                {
                    if
                    (
                        (ch >= 'a' && ch <= 'z') ||
                        (ch >= 'A' && ch <= 'Z') ||
                        (ch >= '0' && ch <= '9') ||
                        ch == '_' || ch == '+' || ch == '.' || ch == '-' || ch == '@' || ch == '='
                    )
                    {
                        // valid character
                    }
                    else
                    {
                        return null;
                    }
                }

                return val[0];
            }
        }

        return null;
    }
    #endregion
}


#region RequestInfoStats
public static class RequestInfoStats
{
    const int RingSize = 64;

    sealed class MinuteSlot
    {
        public long Minute;
        public int Count;
    }

    sealed class PrefixCounters
    {
        public readonly MinuteSlot[] Slots = new MinuteSlot[RingSize];

        public PrefixCounters()
        {
            for (int i = 0; i < Slots.Length; i++)
                Slots[i] = new MinuteSlot();
        }
    }

    static readonly PrefixCounters[] _counters =
    [
        new PrefixCounters(),
        new PrefixCounters(),
        new PrefixCounters(),
        new PrefixCounters(),
        new PrefixCounters(),
        new PrefixCounters()
    ];

    static readonly Timer _clockTimer;

    static long _currentUnixMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
    static int _currentIndex = (int)(_currentUnixMinute & (RingSize - 1));

    static RequestInfoStats()
    {
        int index = (int)(_currentUnixMinute & (RingSize - 1));

        foreach (PrefixCounters counters in _counters)
        {
            MinuteSlot slot = counters.Slots[index];
            Volatile.Write(ref slot.Count, 0);
            Volatile.Write(ref slot.Minute, _currentUnixMinute);
        }

        Volatile.Write(ref _currentUnixMinute, _currentUnixMinute);
        Volatile.Write(ref _currentIndex, index);

        _clockTimer = new Timer(static _ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public static void Increment(RequestStatsType kind)
    {
        int index = Volatile.Read(ref _currentIndex);
        Interlocked.Increment(ref _counters[(int)kind].Slots[index].Count);
    }

    static void Tick()
    {
        long unixMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        long oldMinute = Volatile.Read(ref _currentUnixMinute);

        if (unixMinute == oldMinute)
            return;

        long from = oldMinute + 1;
        long to = unixMinute;

        if (to - from >= RingSize)
            from = to - RingSize + 1;

        for (long minute = from; minute <= to; minute++)
        {
            int index = (int)(minute & (RingSize - 1));

            foreach (PrefixCounters counters in _counters)
            {
                MinuteSlot slot = counters.Slots[index];

                Volatile.Write(ref slot.Minute, minute);
                Volatile.Write(ref slot.Count, 0);
            }
        }

        Volatile.Write(ref _currentUnixMinute, unixMinute);
        Volatile.Write(ref _currentIndex, (int)(unixMinute & (RingSize - 1)));
    }

    public static (long reqMin, long reqHour) GetCounters(RequestStatsType kind, DateTime now)
    {
        PrefixCounters counters = _counters[(int)kind];

        long currentMinute = ((DateTimeOffset)now).ToUnixTimeSeconds() / 60;
        long requestMinute = ReadMinute(counters, currentMinute - 1);
        long requestHour = 0;

        for (int i = 1; i <= 60; i++)
            requestHour += ReadMinute(counters, currentMinute - i);

        return (requestMinute, requestHour);
    }

    static int ReadMinute(PrefixCounters counters, long minute)
    {
        MinuteSlot slot = counters.Slots[(int)(minute & (RingSize - 1))];

        if (Volatile.Read(ref slot.Minute) != minute)
            return 0;

        return Volatile.Read(ref slot.Count);
    }
}

public enum RequestStatsType : byte
{
    Base = 0,
    Nws = 1,
    Proxy = 2,
    Img = 3,
    Request = 4,
    Bot = 5
}
#endregion
