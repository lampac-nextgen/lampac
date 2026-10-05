using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares;

public class WAF
{
    #region static
    static readonly ConcurrentDictionary<long, ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>> BruteForceByMinute = new();
    static readonly Timer BruteForceCleanupTimer = new(CleanupBruteForceState, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    IMemoryCache memoryCache;
    private readonly RequestDelegate _next;

    public WAF(RequestDelegate next, IMemoryCache mem)
    {
        _next = next;
        memoryCache = mem;
        _ = BruteForceCleanupTimer;
    }

    static FrozenSet<System.Net.IPNetwork> ipsDeny = null;
    static FrozenSet<System.Net.IPNetwork> ipsAllow = null;
    static FrozenSet<string> whiteIps = null;
    static FrozenSet<string> countryAllow = null, countryDeny = null;
    static FrozenSet<long> asnAllow = null, asnDeny = null;

    static WAF()
    {
        void UpdateFiled()
        {
            var waf = CoreInit.conf.WAF;

            #region ipsDeny
            if (waf.ipsDeny != null && waf.ipsDeny.Count > 0)
            {
                List<System.Net.IPNetwork> ips = new(waf.ipsDeny.Count);

                foreach (string ip in waf.ipsDeny)
                {
                    if (ip.Contains("/"))
                    {
                        string[] parts = ip.Split('/');
                        if (int.TryParse(parts[1], out int prefixLength))
                            ips.Add(new System.Net.IPNetwork(IPAddress.Parse(parts[0]), prefixLength));
                    }
                    else
                    {
                        IPAddress address = IPAddress.Parse(ip);

                        int prefixLength = address.AddressFamily == AddressFamily.InterNetwork
                            ? 32   // IPv4
                            : 128; // IPv6

                        ips.Add(new System.Net.IPNetwork(address, prefixLength));
                    }
                }

                ipsDeny = ips.ToFrozenSet();
            }
            else
            {
                ipsDeny = null;
            }
            #endregion

            #region ipsAllow
            if (waf.ipsAllow != null && waf.ipsAllow.Count > 0)
            {
                List<System.Net.IPNetwork> ips = new(waf.ipsAllow.Count);

                foreach (string ip in waf.ipsAllow)
                {
                    if (ip.Contains("/"))
                    {
                        string[] parts = ip.Split('/');
                        if (int.TryParse(parts[1], out int prefixLength))
                            ips.Add(new System.Net.IPNetwork(IPAddress.Parse(parts[0]), prefixLength));
                    }
                    else
                    {
                        IPAddress address = IPAddress.Parse(ip);

                        int prefixLength = address.AddressFamily == AddressFamily.InterNetwork
                            ? 32   // IPv4
                            : 128; // IPv6

                        ips.Add(new System.Net.IPNetwork(address, prefixLength));
                    }
                }

                ipsAllow = ips.ToFrozenSet();
            }
            else
            {
                ipsAllow = null;
            }
            #endregion

            whiteIps = waf.whiteIps != null && waf.whiteIps.Count > 0
                ? waf.whiteIps.ToFrozenSet()
                : null;

            countryAllow = waf.countryAllow != null && waf.countryAllow.Count > 0
                ? waf.countryAllow.ToFrozenSet()
                : null;

            countryDeny = waf.countryDeny != null && waf.countryDeny.Count > 0
                ? waf.countryDeny.ToFrozenSet()
                : null;

            asnAllow = waf.asnAllow != null && waf.asnAllow.Count > 0
                ? waf.asnAllow.ToFrozenSet()
                : null;

            asnDeny = waf.asnDeny != null && waf.asnDeny.Count > 0
                ? waf.asnDeny.ToFrozenSet()
                : null;
        }

        UpdateFiled();
        EventListener.UpdateInitFile += UpdateFiled;
    }
    #endregion

    public Task Invoke(HttpContext httpContext)
    {
        var waf = CoreInit.conf.WAF;
        if (!waf.enable)
            return _next(httpContext);

        var requestInfo = httpContext.Features.Get<RequestModel>();
        if (requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
            return _next(httpContext);

        if (whiteIps != null && whiteIps.Contains(requestInfo.IP))
            return _next(httpContext);

        if (waf.bypassLocalIP && requestInfo.IsLocalIp)
            return _next(httpContext);

        IPAddress clientIPAddress = null;
        var disabled = waf.disabled;

        #region BruteForce
        if (!disabled.bruteForceProtection && waf.bruteForceProtection && !requestInfo.IsLocalIp && CoreInit.conf.accsdb.enable)
        {
            var currentMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            var perIp = BruteForceByMinute.GetOrAdd(currentMinute, static _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>());
            var ids = perIp.GetOrAdd(requestInfo.IP, static _ => new ConcurrentDictionary<string, byte>());

            ids.TryAdd(AccsDbInvk.Args(string.Empty, httpContext), 0);

            if (ids.Count > 5)
            {
                httpContext.Response.ContentType = "text/plain; charset=utf-8";
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return httpContext.Response.WriteAsync("Many devices for IP, set up KnownProxies to get the user's real IP");
            }
        }
        #endregion

        #region country
        if (!disabled.country && countryAllow != null)
        {
            // если мы не знаем страну или точно знаем, что она не в списке разрешенных
            if (requestInfo.Country == null || !countryAllow.Contains(requestInfo.Country))
            {
                httpContext.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
        }

        if (!disabled.country && countryDeny != null)
        {
            // точно знаем страну и она есть в списке запрещенных
            if (requestInfo.Country != null && countryDeny.Contains(requestInfo.Country))
            {
                httpContext.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
        }
        #endregion

        #region ASN
        if (!disabled.asn && asnAllow != null)
        {
            // если мы не знаем asn или точно знаем, что он не в списке разрешенных
            if (requestInfo.ASN == -1 || !asnAllow.Contains(requestInfo.ASN))
            {
                httpContext.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
        }

        if (!disabled.asn && asnDeny != null)
        {
            if (asnDeny.Contains(requestInfo.ASN))
            {
                httpContext.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
        }
        #endregion

        #region ASN Range Deny
        if (!disabled.asns && waf.asnsDeny != null && requestInfo.ASN != -1)
        {
            long asn = requestInfo.ASN;

            foreach (var r in waf.asnsDeny)
            {
                if (asn >= r.start && asn <= r.end)
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }
        }
        #endregion

        #region ipsDeny
        if (!disabled.ips && ipsDeny != null)
        {
            if (clientIPAddress == null)
            {
                clientIPAddress = CoreInit.conf.listen.frontend == "cloudflare"
                    ? IPAddress.Parse(requestInfo.IP)
                    : httpContext.Connection.RemoteIpAddress;
            }

            foreach (var ip in ipsDeny)
            {
                if (ip.Contains(clientIPAddress))
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }
        }
        #endregion

        #region ipsAllow
        if (!disabled.ips && ipsAllow != null)
        {
            if (clientIPAddress == null)
            {
                clientIPAddress = CoreInit.conf.listen.frontend == "cloudflare"
                    ? IPAddress.Parse(requestInfo.IP)
                    : httpContext.Connection.RemoteIpAddress;
            }

            if (!waf.ipsAllow.Contains(requestInfo.IP))
            {
                bool deny = true;

                foreach (var ip in ipsAllow)
                {
                    if (ip.Contains(clientIPAddress))
                    {
                        deny = false;
                        break;
                    }
                }

                if (deny)
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }
        }
        #endregion

        #region headers
        if (!disabled.headers && waf.headersDeny != null)
        {
            foreach (var header in waf.headersDeny)
            {
                if (httpContext.Request.Headers.TryGetValue(header.Key, out StringValues headerValue) && headerValue.Count > 0)
                {
                    if (headerValue == header.Value || Regex.IsMatch(headerValue, header.Value, RegexOptions.IgnoreCase))
                    {
                        httpContext.Response.StatusCode = 403;
                        return Task.CompletedTask;
                    }
                }
            }
        }
        #endregion

        #region limit_req
        if (!disabled.limit_req)
        {
            var _limit = MapLimited(waf, httpContext.Request.Path.Value);
            if (_limit?.map?.limit > 0)
            {
                if (RateLimited(httpContext, memoryCache, requestInfo.IP, _limit.map, _limit.pattern))
                {
                    httpContext.Response.ContentType = "text/plain; charset=utf-8";
                    httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    return httpContext.Response.WriteAsync("429 Too Many Requests");
                }
            }
        }
        #endregion

        return _next(httpContext);
    }


    #region MapLimited
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static WafLimitRootMap MapLimited(WafConf waf, string path)
    {
        if (waf.limit_map != null)
        {
            foreach (var pathLimit in waf.limit_map)
            {
                if (pathLimit.path != null && pathLimit.path == path)
                    return pathLimit;

                if (pathLimit.pattern != null && Regex.IsMatch(path, pathLimit.pattern, RegexOptions.IgnoreCase))
                    return pathLimit;
            }
        }

        return new("default", new WafLimitMap() { limit = waf.limit_req });
    }
    #endregion

    #region RateLimited
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool RateLimited(HttpContext httpContext, IMemoryCache cache, string userip, WafLimitMap map, string pattern)
    {
        var hash = Fnv1a.Hash("WAF:RateLimited:");
        Fnv1a.Append(ref hash, userip);
        Fnv1a.Append(ref hash, pattern);

        if (map.pathId)
            Fnv1a.Append(ref hash, httpContext.Request.Path.Value);

        if (map.queryIds != null)
        {
            foreach (string queryId in map.queryIds)
            {
                if (httpContext.Request.Query.TryGetValue(queryId, out StringValues val) && val.Count > 0)
                {
                    Fnv1a.Append(ref hash, val[0]);
                    Fnv1a.Append(ref hash, ':');
                }
            }
        }

        var counter = cache.GetOrCreate(hash.H2, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(map.second == 0 ? 60 : map.second);
            return new Counter();
        });

        return Interlocked.Increment(ref counter.Value) > map.limit;
    }
    #endregion


    sealed class Counter
    {
        public int Value;
    }

    static void CleanupBruteForceState(object _)
    {
        var currentMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        foreach (var key in BruteForceByMinute.Keys)
        {
            if (key < currentMinute)
                BruteForceByMinute.TryRemove(key, out ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _);
        }
    }
}
