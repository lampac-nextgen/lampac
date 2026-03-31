using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares
{
    public class Accsdb
    {
        static readonly Regex rexAuthorize = new Regex("^/(stats|weblog|admin)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex rexStaticAssets = new Regex("\\.(js|css|ico|png|svg|jpe?g|woff|webmanifest)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex rexLockBypass = new Regex("^/(lifeevents|externalids|sisi/(bookmark|history))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static Accsdb()
        {
            Directory.CreateDirectory("cache/logs/accsdb");
        }

        private readonly RequestDelegate _next;
        IMemoryCache memoryCache;

        public Accsdb(RequestDelegate next, IMemoryCache mem)
        {
            _next = next;
            memoryCache = mem;
        }

        public Task Invoke(HttpContext httpContext)
        {
            var endpoint = httpContext.GetEndpoint();
            var requestInfo = httpContext.Features.Get<RequestModel>();

            #region Authorize
            bool IsAuthorize = endpoint != null && endpoint.Metadata.GetMetadata<IAuthorizeData>() != null
                || rexAuthorize.IsMatch(httpContext.Request.Path.Value);

            if (IsAuthorize)
            {
                if (endpoint != null && endpoint.Metadata.GetMetadata<AuthorizeAnonymousAttribute>() != null)
                    return _next(httpContext);

                if (httpContext.Request.Cookies.TryGetValue("accspasswd", out string passwd))
                {
                    string ipKey = $"Accsdb:auth:IP:{requestInfo.IP}";
                    if (!memoryCache.TryGetValue(ipKey, out ConcurrentDictionary<string, byte> passwds))
                    {
                        passwds = new ConcurrentDictionary<string, byte>();
                        memoryCache.Set(ipKey, passwds, DateTime.Today.AddDays(1));
                    }

                    passwds.TryAdd(passwd, 0);

                    if (passwds.Count > 10)
                    {
                        httpContext.Response.StatusCode = 404;
                        return Task.CompletedTask;
                    }

                    if (passwd == CoreInit.rootPasswd)
                        return _next(httpContext);
                }

                httpContext.Response.StatusCode = 404;
                return Task.CompletedTask;
            }
            #endregion

            if (requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
                return _next(httpContext);

            if (httpContext.Request.Path.Value.StartsWith("/proxy", StringComparison.OrdinalIgnoreCase))
                return _next(httpContext);

            if (CoreInit.conf.accsdb.enable)
            {
                var accsdb = CoreInit.conf.accsdb;

                if (!string.IsNullOrEmpty(accsdb.whitepattern) && Regex.IsMatch(httpContext.Request.Path.Value, accsdb.whitepattern, RegexOptions.IgnoreCase))
                {
                    requestInfo.IsAnonymousRequest = true;
                    return _next(httpContext);
                }

                bool limitip = false;

                var user = requestInfo.user;

                if (requestInfo.user_uid != null && accsdb.white_uids != null && accsdb.white_uids.Contains(requestInfo.user_uid))
                    return _next(httpContext);

                string uri = httpContext.Request.Path.Value + httpContext.Request.QueryString.Value;

                if (IsLockHostOrUser(memoryCache, requestInfo.user_uid, requestInfo.IP, uri, out limitip)
                    || user == null
                    || user.ban
                    || DateTime.Now > user.expires)
                {
                    if (rexStaticAssets.IsMatch(httpContext.Request.Path.Value))
                    {
                        httpContext.Response.StatusCode = 404;
                        httpContext.Response.ContentType = "application/octet-stream";
                        return Task.CompletedTask;
                    }

                    #region msg
                    string msg = limitip ? $"Превышено допустимое количество ip/запросов на аккаунт."
                        : string.IsNullOrEmpty(requestInfo.user_uid) ? accsdb.authMesage
                        : accsdb.denyMesage.Replace("{account_email}", requestInfo.user_uid).Replace("{user_uid}", requestInfo.user_uid).Replace("{host}", httpContext.Request.Host.Value);

                    if (user != null)
                    {
                        if (user.ban)
                            msg = user.ban_msg ?? "Вы заблокированы";

                        else if (DateTime.Now > user.expires)
                        {
                            msg = accsdb.expiresMesage
                                .Replace("{account_email}", requestInfo.user_uid)
                                .Replace("{user_uid}", requestInfo.user_uid)
                                .Replace("{expires}", user.expires.ToString("dd.MM.yyyy"));
                        }
                    }
                    #endregion

                    #region denymsg
                    string denymsg = limitip ? $"Превышено допустимое количество ip/запросов на аккаунт." : null;

                    if (user != null)
                    {
                        if (user.ban)
                            denymsg = user.ban_msg ?? "Вы заблокированы";

                        else if (DateTime.Now > user.expires)
                        {
                            denymsg = accsdb.expiresMesage
                                .Replace("{account_email}", requestInfo.user_uid)
                                .Replace("{user_uid}", requestInfo.user_uid)
                                .Replace("{expires}", user.expires.ToString("dd.MM.yyyy"));
                        }
                    }
                    #endregion

                    using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
                    {
                        ctsHttp.CancelAfter(TimeSpan.FromSeconds(CoreInit.conf.listen.ResponseCancelAfter));

                        return httpContext.Response.WriteAsJsonAsync(new { accsdb = true, msg, denymsg, user }, ctsHttp.Token);
                    }
                }
            }

            return _next(httpContext);
        }


        #region IsLock
        static bool IsLockHostOrUser(IMemoryCache memoryCache, string account_email, string userip, string uri, out bool islock)
        {
            if (string.IsNullOrEmpty(account_email))
            {
                islock = false;
                return islock;
            }

            if (rexLockBypass.IsMatch(uri))
            {
                islock = false;
                return islock;
            }

            if (IsLockIpHour(memoryCache, account_email, userip, out islock, out ConcurrentDictionary<string, byte> ips) |
                IsLockReqHour(memoryCache, account_email, uri, out islock, out ConcurrentDictionary<string, byte> urls))
            {
                setLogs("lock_hour", account_email);
                countlock_day(memoryCache, true, account_email);

                File.WriteAllLines($"cache/logs/accsdb/{CrypTo.md5(account_email)}.ips.log", ips.Keys);
                File.WriteAllLines($"cache/logs/accsdb/{CrypTo.md5(account_email)}.urls.log", urls.Keys);

                return islock;
            }

            if (countlock_day(memoryCache, false, account_email) > CoreInit.conf.accsdb.maxlock_day)
            {
                if (CoreInit.conf.accsdb.blocked_hour != -1)
                    memoryCache.Set($"Accsdb:blocked_hour:{account_email}", 0, DateTime.Now.AddHours(CoreInit.conf.accsdb.blocked_hour));

                setLogs("lock_day", account_email);
                islock = true;
                return islock;
            }

            if (memoryCache.TryGetValue($"Accsdb:blocked_hour:{account_email}", out _))
            {
                setLogs("blocked", account_email);
                islock = true;
                return islock;
            }

            islock = false;
            return islock;
        }


        static bool IsLockIpHour(IMemoryCache memoryCache, string account_email, string userip, out bool islock, out ConcurrentDictionary<string, byte> ips)
        {
            ips = memoryCache.GetOrCreate($"Accsdb:IsLockIpHour:{account_email}", entry =>
            {
                var now = DateTimeOffset.Now;
                entry.AbsoluteExpiration = new DateTimeOffset(
                    now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero
                ).AddHours(1);

                return new ConcurrentDictionary<string, byte>();
            });

            ips.TryAdd(userip, 0);

            if (ips.Count > CoreInit.conf.accsdb.maxip_hour)
            {
                islock = true;
                return islock;
            }

            islock = false;
            return islock;
        }

        static bool IsLockReqHour(IMemoryCache memoryCache, string account_email, string uri, out bool islock, out ConcurrentDictionary<string, byte> urls)
        {
            urls = memoryCache.GetOrCreate($"Accsdb:IsLockReqHour:{account_email}", entry =>
            {
                var now = DateTimeOffset.Now;
                entry.AbsoluteExpiration = new DateTimeOffset(
                    now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero
                ).AddHours(1);

                return new ConcurrentDictionary<string, byte>();
            });

            urls.TryAdd(uri, 0);

            if (urls.Count > CoreInit.conf.accsdb.maxrequest_hour)
            {
                islock = true;
                return islock;
            }

            islock = false;
            return islock;
        }
        #endregion


        #region setLogs
        static string logsLock = string.Empty;

        static void setLogs(string name, string account_email)
        {
            var now = DateTime.Now;
            string logFile = $"cache/logs/accsdb/{now:dd-MM-yyyy}.lock.txt";
            if (logsLock != string.Empty && !File.Exists(logFile))
                logsLock = string.Empty;

            string line = $"{name} / {account_email} / {CrypTo.md5(account_email)}.*.log";

            if (!logsLock.Contains(line))
            {
                logsLock += $"{now}: {line}\n";
                File.WriteAllText(logFile, logsLock);
            }
        }
        #endregion

        #region countlock_day
        static int countlock_day(IMemoryCache memoryCache, bool update, string account_email)
        {
            var lockhour = memoryCache.GetOrCreate($"Accsdb:lock_day:{account_email}", entry =>
            {
                var now = DateTimeOffset.Now;
                entry.AbsoluteExpiration = new DateTimeOffset(
                    now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero
                ).AddDays(1);

                return new ConcurrentDictionary<int, byte>();
            });

            if (update)
                lockhour.TryAdd(DateTime.Now.Hour, 0);

            return lockhour.Count;
        }
        #endregion
    }
}
