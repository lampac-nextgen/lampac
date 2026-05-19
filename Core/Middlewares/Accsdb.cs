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

partial class AccsdbRegex
{
    [GeneratedRegex("^/(stats|weblog|admin)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Authorize();

    [GeneratedRegex(@"\.(js|css|ico|png|svg|jpe?g|woff|webmanifest)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex StaticAssets();

    [GeneratedRegex("^/(lifeevents|externalids|sisi/(bookmark|history))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex LockBypass();
}

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

        #region Authorization
        bool IsAuthorize = AccsdbRegex.Authorize().IsMatch(httpContext.Request.Path.Value);
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
            bool limitip = false;
            var user = requestInfo.user;
            var accsdb = CoreInit.conf.accsdb;

            if (user?.bypass_accsdb == true)
                return _next(httpContext);

            if (requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
                return _next(httpContext);

            if (httpContext.Request.Path.Value.StartsWith("/proxy", StringComparison.OrdinalIgnoreCase))
                return _next(httpContext);

            if (!string.IsNullOrEmpty(accsdb.whitepattern) && Regex.IsMatch(httpContext.Request.Path.Value, accsdb.whitepattern, RegexOptions.IgnoreCase))
            {
                requestInfo.IsAnonymousRequest = true;
                return _next(httpContext);
            }

            if (requestInfo.user_uid != null && accsdb.white_uids != null && accsdb.white_uids.Contains(requestInfo.user_uid))
                return _next(httpContext);

            string uri = httpContext.Request.Path.Value + httpContext.Request.QueryString.Value;

            if (IsLockHostOrUser(memoryCache, requestInfo.user_uid, requestInfo.IP, uri, out limitip)
                || user == null
                || user.ban
                || DateTime.Now > user.expires)
            {
                if (AccsdbRegex.StaticAssets().IsMatch(httpContext.Request.Path.Value))
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

        if (AccsdbRegex.LockBypass().IsMatch(uri))
        {
            islock = false;
            return islock;
        }

        if (IsLockIpHour(memoryCache, account_email, userip, out islock, out ConcurrentDictionary<string, byte> ips) |
            IsLockReqHour(memoryCache, account_email, uri, out islock, out ConcurrentDictionary<string, byte> urls))
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
}
