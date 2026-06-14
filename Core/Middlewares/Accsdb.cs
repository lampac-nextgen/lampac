using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Core.Middlewares;

public class Accsdb
{
    static Accsdb()
    {
        Directory.CreateDirectory("logs/accsdb");
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

        string path = httpContext.Request.Path.Value;

        #region Authorization
        bool IsAuthorize = path.StartsWith("/stats", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/weblog", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase);

        if (IsAuthorize == false && endpoint != null)
        {
            IsAuthorize =
                endpoint.Metadata.GetMetadata<IAuthorizeData>() != null ||
                endpoint.Metadata.GetMetadata<AuthorizationAttribute>() != null;
        }


        if (IsAuthorize)
        {
            if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
                return _next(httpContext);

            AuthorizationAttribute authAttribute = endpoint?.Metadata?.GetMetadata<AuthorizationAttribute>();

            if (httpContext.Request.Cookies.TryGetValue("accspasswd", out string passwd))
            {
                var passwds = memoryCache.GetOrCreate($"Accsdb:auth:IP:{requestInfo.IP}", entry =>
                {
                    entry.AbsoluteExpiration = DateTimeOffset.Now.Date.AddDays(1);
                    return new ConcurrentDictionary<string, byte>();
                });

                if (passwds.Count > 10)
                {
                    httpContext.Response.StatusCode = 404;
                    return Task.CompletedTask;
                }

                if (passwd == CoreInit.rootPasswd)
                    return _next(httpContext);

                passwds.TryAdd(passwd, 0);
            }

            if (authAttribute?.redirectUri != null)
            {
                httpContext.Response.Redirect(authAttribute.redirectUri);
                return Task.CompletedTask;
            }

            if (authAttribute?.accessDeniedMessage != null)
            {
                httpContext.Response.StatusCode = 401;
                return httpContext.Response.WriteAsync(authAttribute.accessDeniedMessage);
            }

            httpContext.Response.StatusCode = 404;
            return Task.CompletedTask;
        }
        #endregion

        if (CoreInit.conf.accsdb.enable)
        {
            var accsdb = CoreInit.conf.accsdb;

            if (requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
                return _next(httpContext);

            if (path.StartsWith("/proxy", StringComparison.OrdinalIgnoreCase))
                return _next(httpContext);

            if (!string.IsNullOrEmpty(accsdb.whitepattern) && Regex.IsMatch(path, accsdb.whitepattern, RegexOptions.IgnoreCase))
            {
                requestInfo.IsAnonymousRequest = true;
                return _next(httpContext);
            }

            if (requestInfo.user_uid != null && accsdb.white_uids != null && accsdb.white_uids.Contains(requestInfo.user_uid))
                return _next(httpContext);

            var now = DateTime.Now;
            var user = requestInfo.user;

            if (user?.bypass_accsdb == true)
                return _next(httpContext);

            string uri = path + httpContext.Request.QueryString.Value;
            bool limitip = false;

            if (IsLockHostOrUser(memoryCache, requestInfo.user_uid, requestInfo.IP, uri, out limitip)
                || user == null
                || user.ban
                || now > user.expires)
            {
                if (IsStaticAsset(path))
                {
                    httpContext.Response.StatusCode = 404;
                    httpContext.Response.ContentType = "application/octet-stream";
                    return Task.CompletedTask;
                }

                if (EventListener.Accsdb != null)
                {
                    var ev = new EventAccsdb(httpContext, requestInfo);

                    foreach (Func<EventAccsdb, bool> handler in EventListener.Accsdb.GetInvocationList())
                    {
                        if (handler(ev))
                            return _next(httpContext);
                    }
                }

                #region msg
                string msg = limitip ? $"Превышено допустимое количество ip/запросов на аккаунт."
                    : string.IsNullOrEmpty(requestInfo.user_uid) ? accsdb.authMesage
                    : accsdb.denyMesage.Replace("{account_email}", requestInfo.user_uid).Replace("{user_uid}", requestInfo.user_uid).Replace("{host}", httpContext.Request.Host.Value);

                if (user != null)
                {
                    if (user.ban)
                        msg = user.ban_msg ?? "Вы заблокированы";

                    else if (now > user.expires)
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

                    else if (now > user.expires)
                    {
                        denymsg = accsdb.expiresMesage
                            .Replace("{account_email}", requestInfo.user_uid)
                            .Replace("{user_uid}", requestInfo.user_uid)
                            .Replace("{expires}", user.expires.ToString("dd.MM.yyyy"));
                    }
                }
                #endregion

                return httpContext.Response.WriteAsJsonAsync(new
                {
                    accsdb = true,
                    msg,
                    denymsg,
                    user
                });
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

        if (uri.StartsWith("/lifeevents", StringComparison.Ordinal) ||
            uri.StartsWith("/externalids", StringComparison.Ordinal) ||
            uri.StartsWith("/testaccsdb", StringComparison.Ordinal) ||
            uri.StartsWith("/sisi/bookmark", StringComparison.Ordinal) ||
            uri.StartsWith("/sisi/history", StringComparison.Ordinal) ||
            uri.StartsWith("/tmdb/", StringComparison.Ordinal) ||
            uri.StartsWith("/timecode/", StringComparison.Ordinal) ||
            uri.StartsWith("/bookmark/", StringComparison.Ordinal) ||
            uri.StartsWith("/storage/", StringComparison.Ordinal) ||
            uri.StartsWith("/cub/", StringComparison.Ordinal) ||
            uri.StartsWith("/dlna/", StringComparison.Ordinal))
        {
            islock = false;
            return islock;
        }

        bool lockIp = IsLockIpHour(memoryCache, account_email, userip, out islock, out ConcurrentDictionary<string, byte> ips);
        bool lockReq = IsLockReqHour(memoryCache, account_email, uri, out islock, out ConcurrentDictionary<string, byte> urls);

        if (lockIp || lockReq)
        {
            setLogs("lock_hour", account_email);
            countlock_day(memoryCache, true, account_email);

            File.WriteAllLines($"logs/accsdb/{CrypTo.md5(account_email)}.ips.log", ips.Keys);
            File.WriteAllLines($"logs/accsdb/{CrypTo.md5(account_email)}.urls.log", urls.Keys);

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
        ips = memoryCache.GetOrCreate($"Accsdb:IsLockIpHour:{account_email}:{DateTime.Now.Hour}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
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
        urls = memoryCache.GetOrCreate($"Accsdb:IsLockReqHour:{account_email}:{DateTime.Now.Hour}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
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
        string logFile = $"logs/accsdb/{now:dd-MM-yyyy}.lock.txt";
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
        var lockhour = memoryCache.GetOrCreate($"Accsdb:lock_day:{account_email}:{DateTime.Now.Day}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
            return new ConcurrentDictionary<int, byte>();
        });

        if (update)
            lockhour.TryAdd(DateTime.Now.Hour, 0);

        return lockhour.Count;
    }
    #endregion


    #region IsStaticAsset
    static bool IsStaticAsset(string s)
    {
        return s.EndsWith(".js", StringComparison.Ordinal)
            || s.EndsWith(".css", StringComparison.Ordinal)
            || s.EndsWith(".ico", StringComparison.Ordinal)
            || s.EndsWith(".png", StringComparison.Ordinal)
            || s.EndsWith(".svg", StringComparison.Ordinal)
            || s.EndsWith(".jpg", StringComparison.Ordinal)
            || s.EndsWith(".jpeg", StringComparison.Ordinal)
            || s.EndsWith(".woff", StringComparison.Ordinal)
            || s.EndsWith(".webmanifest", StringComparison.Ordinal);
    }
    #endregion
}
