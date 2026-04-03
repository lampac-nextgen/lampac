using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Services.Pools;
using Shared.Models.Base;
using Shared.Models.AppConf;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shared.Services.Utilities;

namespace Core.Middlewares
{
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
        #endregion

        public Task Invoke(HttpContext httpContext)
        {
            var waf = CoreInit.conf.WAF;
            var disabled = waf.disabled ?? new WafDisabled();
            if (!waf.enable)
                return _next(httpContext);

            var requestInfo = httpContext.Features.Get<RequestModel>();
            if (requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
                return _next(httpContext);

            if (waf.whiteIps != null && waf.whiteIps.Contains(requestInfo.IP))
                return _next(httpContext);

            if (waf.bypassLocalIP && requestInfo.IsLocalIp)
                return _next(httpContext);

            #region BruteForce
            if (!disabled.bruteForceProtection && waf.bruteForceProtection && !requestInfo.IsLocalIp && CoreInit.conf.accsdb.enable)
            {
                var currentMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
                var perIp = BruteForceByMinute.GetOrAdd(currentMinute, static _ => new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>());
                var ids = perIp.GetOrAdd(requestInfo.IP, static _ => new ConcurrentDictionary<string, byte>());

                ids.TryAdd(AccsDbInvk.Args(string.Empty, httpContext), 0);

                if (ids.Count > 5)
                {
                    httpContext.Response.StatusCode = 429;
                    return httpContext.Response.WriteAsync("Many devices for IP, set up KnownProxies to get the user's real IP");
                }
            }
            #endregion

            #region country
            if (!disabled.country && waf.countryAllow != null)
            {
                // если мы не знаем страну или точно знаем, что она не в списке разрешенных
                if (string.IsNullOrEmpty(requestInfo.Country) || !waf.countryAllow.Contains(requestInfo.Country))
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }

            if (!disabled.country && waf.countryDeny != null)
            {
                // точно знаем страну и она есть в списке запрещенных
                if (!string.IsNullOrEmpty(requestInfo.Country) && waf.countryDeny.Contains(requestInfo.Country))
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }
            #endregion

            #region ASN
            if (!disabled.asn && waf.asnAllow != null)
            {
                // если мы не знаем asn или точно знаем, что он не в списке разрешенных
                if (requestInfo.ASN == -1 || !waf.asnAllow.Contains(requestInfo.ASN))
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }

            if (!disabled.asn && waf.asnDeny != null)
            {
                if (waf.asnDeny.Contains(requestInfo.ASN))
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

            #region ips
            if (!disabled.ips && waf.ipsDeny != null)
            {
                if (waf.ipsDeny.Contains(requestInfo.IP))
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }

                var clientIPAddress = IPAddress.Parse(requestInfo.IP);
                foreach (string ip in waf.ipsDeny)
                {
                    if (ip.Contains("/"))
                    {
                        string[] parts = ip.Split('/');
                        if (int.TryParse(parts[1], out int prefixLength))
                        {
                            if (new System.Net.IPNetwork(IPAddress.Parse(parts[0]), prefixLength).Contains(clientIPAddress))
                            {
                                httpContext.Response.StatusCode = 403;
                                return Task.CompletedTask;
                            }
                        }
                    }
                }
            }

            if (!disabled.ips && waf.ipsAllow != null)
            {
                if (!waf.ipsAllow.Contains(requestInfo.IP))
                {
                    bool deny = true;
                    var clientIPAddress = IPAddress.Parse(requestInfo.IP);
                    foreach (string ip in waf.ipsAllow)
                    {
                        if (ip.Contains("/"))
                        {
                            string[] parts = ip.Split('/');
                            if (int.TryParse(parts[1], out int prefixLength))
                            {
                                if (new System.Net.IPNetwork(IPAddress.Parse(parts[0]), prefixLength).Contains(clientIPAddress))
                                {
                                    deny = false;
                                    break;
                                }
                            }
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
                        if (Regex.IsMatch(headerValue, header.Value, RegexOptions.IgnoreCase))
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
                        httpContext.Response.StatusCode = 429;
                        return httpContext.Response.WriteAsync("429 Too Many Requests");
                    }
                }
            }
            #endregion

            return _next(httpContext);
        }


        #region MapLimited
        static WafLimitRootMap MapLimited(WafConf waf, string path)
        {
            if (waf.limit_map != null)
            {
                foreach (var pathLimit in waf.limit_map)
                {
                    if (Regex.IsMatch(path, pathLimit.pattern, RegexOptions.IgnoreCase))
                        return pathLimit;
                }
            }

            return new("default", new WafLimitMap() { limit = waf.limit_req });
        }
        #endregion

        #region RateLimited
        static bool RateLimited(HttpContext httpContext, IMemoryCache cache, string userip, WafLimitMap map, string pattern)
        {
            var sb = StringBuilderPool.ThreadInstance;

            sb.Append("WAF:RateLimited:");
            sb.Append(userip);
            sb.Append(":");
            sb.Append(pattern);
            sb.Append(":");

            if (map.pathId)
            {
                sb.Append(httpContext.Request.Path.Value);
                sb.Append(":");
            }

            if (map.queryIds != null)
            {
                foreach (string queryId in map.queryIds)
                {
                    if (httpContext.Request.Query.TryGetValue(queryId, out StringValues val) && val.Count > 0)
                    {
                        sb.Append(val[0]);
                        sb.Append(":");
                    }
                }
            }

            var counter = cache.GetOrCreate(sb.ToString(), entry =>
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
}
