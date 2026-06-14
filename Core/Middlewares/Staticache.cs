using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Shared;
using Shared.Attributes;
using Shared.Models.AppConf;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services.Buckets;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares;

public class Staticache
{
    #region static
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<Staticache>();

    public readonly static ConcurrentDictionary<string, StaticacheCacheModel> cacheFiles = new();

    static readonly Timer cleanupTimer = new Timer(cleanup, null, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));

    static StaticachePreparedRoute[] preparedRoutes = Array.Empty<StaticachePreparedRoute>();

    public static void Initialization()
    {
        #region load cache files
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        string cacheDir = Path.Combine("cache", "static");
        BucketFolders.Create(cacheDir);

        foreach (string inFile in Directory.EnumerateFiles(cacheDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                /// cache\static\62\-DDVxczeTgFWOm32NktG6A-1779890234674_26362.jpg
                ReadOnlySpan<char> fileName = inFile.AsSpan();

                /// cacheKey-<time>_<length>.<type>
                fileName = fileName.Slice(fileName.LastIndexOfAny('\\', '/') + 1);

                int dotIndex = fileName.LastIndexOf('.');

                /// jpg
                string ext = fileName.Slice(dotIndex + 1).ToString();

                /// cacheKey-<time>_<length>
                fileName = fileName.Slice(0, dotIndex);

                int dashIndex = fileName.LastIndexOf('-');

                // DDVxczeTgFWOm32NktG6A
                string cachekey = new string(fileName.Slice(0, dashIndex));

                int underIndex = fileName.LastIndexOf('_');

                /// 26362
                int contentLength = int.Parse(fileName.Slice(underIndex + 1));

                /// 1779890234674
                long unixTime = long.Parse(fileName.Slice(0, underIndex).Slice(dashIndex + 1));

                if (now > unixTime || string.IsNullOrEmpty(cachekey) || string.IsNullOrEmpty(ext))
                {
                    deleteFile(inFile);
                    continue;
                }

                cacheFiles.TryAdd(cachekey, new StaticacheCacheModel(unixTime, ext, 200, contentLength));
            }
            catch
            {
                deleteFile(inFile);
            }
        }
        #endregion

        void UpdateRoutes()
        {
            var routes = CoreInit.conf.Staticache.routes;
            if (routes == null || routes.Count == 0)
                preparedRoutes = Array.Empty<StaticachePreparedRoute>();
            else
            {
                preparedRoutes = routes.Select(r => new StaticachePreparedRoute
                {
                    Route = r,
                    PathRegex = r.pathRex != null
                        ? new Regex(
                            r.pathRex,
                            RegexOptions.IgnoreCase |
                            RegexOptions.CultureInvariant |
                            RegexOptions.Compiled,
                            TimeSpan.FromMilliseconds(100)
                        )
                        : null
                }).ToArray();
            }
        }

        UpdateRoutes();
        EventListener.UpdateInitFile += UpdateRoutes;
    }

    static void cleanup(object state)
    {
        try
        {
            var cutoff = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            foreach (var _c in cacheFiles)
            {
                if (_c.Value.ex > cutoff)
                    continue;

                if (cacheFiles.TryRemove(_c.Key, out _))
                {
                    string cachefile = GetFilePath(_c.Key, _c.Value.ex, _c.Value.contentLength, _c.Value.ext);
                    deleteFile(cachefile);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_h3352g2f");
        }
    }

    static void deleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_wfl5s3rn");
        }
    }
    #endregion

    private readonly RequestDelegate _next;

    public Staticache(RequestDelegate next)
    {
        _next = next;
    }

    public Task Invoke(HttpContext httpContext)
    {
        if (!HttpMethods.IsGet(httpContext.Request.Method))
            return _next(httpContext);

        var requestInfo = httpContext.Features.Get<RequestModel>();
        if (requestInfo.AesGcmKey != null || requestInfo.IsProxyRequest || requestInfo.IsProxyImg)
            return _next(httpContext);

        var endpoint = httpContext.GetEndpoint();
        var staticache = endpoint?.Metadata?.GetMetadata<StaticacheAttribute>();

        if (staticache == null)
            return _next(httpContext);

        if (staticache.setHeadersNoCache)
            WriteNoCache(httpContext);

        var init = CoreInit.conf.Staticache;

        #region EventListener
        if (EventListener.Staticache != null)
        {
            var em = new EventStaticache(httpContext, requestInfo);

            foreach (Func<EventStaticache, bool> handler in EventListener.Staticache.GetInvocationList())
            {
                if (!handler(em))
                    return _next(httpContext);
            }
        }
        #endregion

        bool customRoute = false;
        StaticacheRoute route = default;
        string path = httpContext.Request.Path.Value;

        #region init routes
        if (init.enable)
        {
            foreach (var p in preparedRoutes)
            {
                var r = p.Route;

                if ((r.path != null && path.Equals(r.path, StringComparison.OrdinalIgnoreCase))
                    || (r.pathRex != null && p.PathRegex.IsMatch(path)))
                {
                    customRoute = true;
                    route = r;
                    break;
                }
            }
        }
        #endregion

        if (staticache.always == false)
        {
            if (customRoute == false)
            {
                // endpoint или настройки init требует явный routes
                if (init.enable == false || staticache.manually || init.manually)
                    return _next(httpContext);
            }

            if (init.minimalCacheMinutes > staticache.cacheMinutes)
                return _next(httpContext);
        }

        if (init.disabledPaths != null && init.disabledPaths.Contains(path))
            return _next(httpContext);

        if (0 >= route.cacheMinutes)
            route.cacheMinutes = staticache.cacheMinutes;

        if (route.queryKeys == null)
            route.queryKeys = staticache.queryKeys;

        if (route.ignoreQueryKeys == null)
            route.ignoreQueryKeys = staticache.ignoreQueryKeys;

        var parameters = endpoint.Metadata
            .GetMetadata<ControllerActionDescriptor>()?
            .Parameters;

        string cachekey = getQueryKeys(httpContext, route.skipUids || staticache.skipUids, parameters, route.queryKeys, route.ignoreQueryKeys);

        if (cacheFiles.TryGetValue(cachekey, out StaticacheCacheModel _r))
        {
            httpContext.Response.StatusCode = _r.statusCode;
            httpContext.Response.Headers["X-StatiCache-Status"] = "HIT";
            httpContext.Response.Headers["X-StatiCache-Bucket"] = BucketFolders.Name(cachekey[0]);
            httpContext.Response.Headers["X-StatiCache-Id"] = cachekey;

            string ext = _r.ext switch
            {
                "html" => "text/html; charset=utf-8",
                "json" => "application/json; charset=utf-8",
                "js" => "application/javascript; charset=utf-8",
                "css" => "text/css; charset=utf-8",
                "png" => "image/png",
                "jpg" => "image/jpeg",
                "svg" => "image/svg+xml",
                "webp" => "image/webp",
                _ => "application/octet-stream"
            };

            if (_r.contentLength > 0)
            {
                httpContext.Response.ContentLength = _r.contentLength;
                httpContext.Response.Headers[HeaderNames.CacheControl] = "public,max-age=86400,immutable";
            }
            else if (ext is "json" or "html")
                WriteNoCache(httpContext);

            httpContext.Response.ContentType = ext;

            string file = GetFilePath(cachekey, _r.ex, _r.contentLength, _r.ext);
            return httpContext.Response.SendFileAsync(file);
        }
        else
        {
            httpContext.Features.Set(new StaticacheFeature(route.cacheMinutes, cachekey));
            return _next(httpContext);
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string getQueryKeys(HttpContext httpContext, bool skipUids, IList<ParameterDescriptor> parameters, string[] queryKeys, string[] ignoreQueryKeys)
    {
        var hash = Fnv1a.Empty;

        Fnv1a.Append(ref hash, httpContext.Request.Scheme);
        Fnv1a.Append(ref hash, httpContext.Request.Host.Value);
        Fnv1a.Append(ref hash, httpContext.Request.Path.Value);

        if (httpContext.Request.Query.TryGetValue("rjson", out StringValues rjson) && rjson.Count > 0)
            Fnv1a.Append(ref hash, rjson[0]);

        if (queryKeys != null && queryKeys.Length > 0)
        {
            if (queryKeys.Length == 1 && queryKeys[0] == ".*")
            {
                foreach (var q in httpContext.Request.Query.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    string key = q.Key;

                    if (skipUids && CoreInit.SkipQueryKeys.Contains(key))
                        continue;

                    if (ignoreQueryKeys != null && ignoreQueryKeys.Contains(key))
                        continue;

                    Fnv1a.Append(ref hash, key);
                    Fnv1a.Append(ref hash, q.Value);
                }
            }
            else
            {
                foreach (string key in queryKeys)
                    QueryAppend(ref hash, key, httpContext, skipUids, ignoreQueryKeys);
            }
        }
        else if (parameters != null && parameters.Count > 0)
        {
            foreach (var param in parameters)
                QueryAppend(ref hash, param.Name, httpContext, skipUids, ignoreQueryKeys);
        }

        return Fnv1a.Base64Url(hash);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void QueryAppend(ref Fnv1aHash hash, string key, HttpContext httpContext, bool skipUids, string[] ignoreQueryKeys)
    {
        if (key == null)
            return;

        if (skipUids && CoreInit.SkipQueryKeys.Contains(key))
            return;

        if (ignoreQueryKeys != null && ignoreQueryKeys.Contains(key))
            return;

        if (httpContext.Request.Query.TryGetValue(key, out StringValues value) && value.Count > 0)
        {
            Fnv1a.Append(ref hash, key);
            Fnv1a.Append(ref hash, value[0]);
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetFilePath(string cachekey, long ex, int length, string ext)
        => Path.Combine("cache", "static", BucketFolders.Name(cachekey[0]), $"{cachekey}-{ex}_{length}.{ext}");


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteNoCache(HttpContext httpContext)
    {
        httpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate"; // HTTP 1.1.
        httpContext.Response.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
        httpContext.Response.Headers["Expires"] = "0"; // Proxies.
    }
}
