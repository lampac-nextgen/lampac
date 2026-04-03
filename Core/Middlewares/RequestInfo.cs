using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models.Base;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares
{
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
            bool IsWsRequest = httpContext.Request.Path.Value.StartsWith("/nws", StringComparison.OrdinalIgnoreCase);
            bool IsProxyImg = httpContext.Request.Path.Value.StartsWith("/proxyimg", StringComparison.OrdinalIgnoreCase);
            bool IsProxyRequest = httpContext.Request.Path.Value.StartsWith("/proxy/", StringComparison.OrdinalIgnoreCase)
                || httpContext.Request.Path.Value.StartsWith("/proxy-dash/", StringComparison.OrdinalIgnoreCase);

            if (CoreInit.conf.openstat.enable)
                RequestInfoStats.Increment(IsWsRequest ? "nws" : IsProxyRequest ? "proxy" : IsProxyImg ? "img" : "request");

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

                if (_localpasswd[0] != CoreInit.rootPasswd)
                    return httpContext.Response.WriteAsync("error passwd");

                IsLocalRequest = true;

                if (httpContext.Request.Headers.TryGetValue("x-client-ip", out StringValues xip) && xip.Count > 0)
                {
                    if (!string.IsNullOrEmpty(xip[0]))
                        clientIp = xip[0];
                }
            }
            else if (CoreInit.conf.listen.frontend == "cloudflare")
            {
                #region cloudflare
                if (Program.cloudflare_ips != null && Program.cloudflare_ips.Count > 0)
                {
                    try
                    {
                        var clientIPAddress = IPAddress.Parse(clientIp);
                        foreach (var cf in Program.cloudflare_ips)
                        {
                            if (new System.Net.IPNetwork(cf.prefix, cf.prefixLength).Contains(clientIPAddress))
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
                    catch (System.Exception ex)
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

            if (httpContext.Request.Headers.TryGetValue("X-Kit-AesGcm", out StringValues aesGcmKey) && aesGcmKey.Count > 0)
                req.AesGcmKey = aesGcmKey;

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

        static string getuid(HttpContext httpContext)
        {
            foreach (string id in uids)
            {
                if (httpContext.Request.Query.ContainsKey(id))
                {
                    StringValues val = httpContext.Request.Query[id];
                    if (val.Count > 0)
                    {
                        if (!CoreInit.conf.BaseModule.ValidateIdentity)
                            return val[0];

                        if (IsValidUid(val[0]))
                            return val[0];
                    }
                }
            }

            return null;
        }

        static bool IsValidUid(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty)
                return false;

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
                    continue;
                }

                return false;
            }

            return true;
        }
        #endregion
    }


    #region RequestInfoStats
    public static class RequestInfoStats
    {
        sealed class PrefixCounters
        {
            public readonly ConcurrentDictionary<long, int[]> Hours = new();
        }

        static readonly ConcurrentDictionary<string, PrefixCounters> _counters = new();
        static readonly Timer _cleanupTimer;

        static RequestInfoStats()
        {
            _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public static void Increment(string prefix)
        {
            var now = DateTime.UtcNow;
            var prefixCounters = _counters.GetOrAdd(prefix, static _ => new PrefixCounters());
            long hourKey = now.Ticks / TimeSpan.TicksPerHour;
            int[] minutes = prefixCounters.Hours.GetOrAdd(hourKey, static _ => new int[60]);
            Interlocked.Increment(ref minutes[now.Minute]);
        }

        public static (long reqMin, long reqHour) GetCounters(string prefix, DateTime now)
        {
            if (!_counters.TryGetValue(prefix, out PrefixCounters prefixCounters))
                return (0, 0);

            long currentHourKey = now.Ticks / TimeSpan.TicksPerHour;
            long prevHourKey = currentHourKey - 1;
            int prevMinute = now.Minute == 0 ? 59 : now.Minute - 1;

            long requestMinute = 0;
            if (now.Minute == 0)
            {
                if (prefixCounters.Hours.TryGetValue(prevHourKey, out int[] prevHourMinutes))
                    requestMinute = prevHourMinutes[59];
            }
            else
            {
                if (prefixCounters.Hours.TryGetValue(currentHourKey, out int[] currentHourMinutes))
                    requestMinute = currentHourMinutes[prevMinute];
            }

            long requestHour = 0;
            for (int i = 1; i <= 60; i++)
            {
                int minuteIndex = now.Minute - i;
                long hourKey = currentHourKey;

                if (minuteIndex < 0)
                {
                    minuteIndex += 60;
                    hourKey = prevHourKey;
                }

                if (prefixCounters.Hours.TryGetValue(hourKey, out int[] minutes))
                    requestHour += minutes[minuteIndex];
            }

            return (requestMinute, requestHour);
        }

        static void Cleanup()
        {
            long currentHourKey = DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour;
            long prevHourKey = currentHourKey - 1;

            foreach (PrefixCounters prefixCounters in _counters.Values)
            {
                foreach (long hourKey in prefixCounters.Hours.Keys)
                {
                    if (hourKey != currentHourKey && hourKey != prevHourKey)
                        prefixCounters.Hours.TryRemove(hourKey, out _);
                }
            }
        }
    }
    #endregion
}
