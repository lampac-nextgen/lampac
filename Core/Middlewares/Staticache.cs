using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models.AppConf;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares
{
    public record CacheModel(DateTimeOffset ex, string contentType, string inFile);

    public record StaticacheFeature(StaticacheRoute route, string cachekey);

    public class Staticache
    {
        #region static
        static readonly Serilog.ILogger Log = Serilog.Log.ForContext<Staticache>();

        public readonly static ConcurrentDictionary<string, CacheModel> cacheFiles = new();

        static readonly Timer cleanupTimer = new Timer(cleanup, null, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));

        public static void Initialization()
        {
            Directory.CreateDirectory("cache/static");

            var now = DateTime.Now;

            foreach (string inFile in Directory.EnumerateFiles("cache/static", "*"))
            {
                try
                {
                    // cacheKey-<time>.<type>
                    ReadOnlySpan<char> fileName = inFile.AsSpan();
                    int lastSlash = fileName.LastIndexOfAny('\\', '/');
                    if (lastSlash >= 0)
                        fileName = fileName.Slice(lastSlash + 1);

                    int dash = fileName.IndexOf('-');
                    if (dash <= 0)
                    {
                        deleteFile(inFile);
                        continue;
                    }

                    // '.' после дефиса
                    int dotRel = fileName.Slice(dash + 1).IndexOf('.');
                    if (dotRel < 0)
                    {
                        deleteFile(inFile);
                        continue;
                    }
                    int firstDot = dash + 1 + dotRel;

                    ReadOnlySpan<char> fileTimeSpan = fileName.Slice(dash + 1, firstDot - dash - 1);
                    if (!long.TryParse(fileTimeSpan, out long fileTime) || fileTime == 0)
                    {
                        deleteFile(inFile);
                        continue;
                    }

                    // <type> = первое расширение после точки (игнорируем ".gz" и любые суффиксы)
                    int typeEndRel = fileName.Slice(firstDot + 1).IndexOf('.');
                    int typeEnd = typeEndRel < 0 ? fileName.Length : firstDot + 1 + typeEndRel;

                    ReadOnlySpan<char> typeSpan = fileName.Slice(firstDot + 1, typeEnd - (firstDot + 1));
                    if (typeSpan.Length == 0)
                    {
                        deleteFile(inFile);
                        continue;
                    }

                    string cachekey = new string(fileName.Slice(0, dash));

                    string contentType = typeSpan.SequenceEqual("html")
                        ? "text/html; charset=utf-8"
                        : "application/json; charset=utf-8";

                    var ex = DateTimeOffset.FromUnixTimeMilliseconds(fileTime);

                    if (now > ex)
                    {
                        deleteFile(inFile);
                        continue;
                    }

                    cacheFiles.TryAdd(cachekey, new(ex, contentType, inFile));
                }
                catch
                {
                    deleteFile(inFile);
                }
            }
        }

        static void cleanup(object state)
        {
            try
            {
                var cutoff = DateTimeOffset.Now;

                foreach (var _c in cacheFiles)
                {
                    if (_c.Value.ex > cutoff)
                        continue;

                    if (cacheFiles.TryRemove(_c.Key, out _))
                    {
                        string cachefile = getFilePath(_c.Key, _c.Value.ex, _c.Value.contentType);
                        deleteFile(cachefile);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_h3352g2f");
            }
        }

        static void deleteFile(string file)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);

                if (File.Exists($"{file}.gz"))
                    File.Delete($"{file}.gz");
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_wfl5s3rn");
            }
        }


        private readonly RequestDelegate _next;

        public Staticache(RequestDelegate next)
        {
            _next = next;
        }
        #endregion

        public Task Invoke(HttpContext httpContext)
        {
            var init = CoreInit.conf.Staticache;
            if (!init.enable)
                return _next(httpContext);

            var requestInfo = httpContext.Features.Get<RequestModel>();
            if (requestInfo.AesGcmKey != null || requestInfo.IsWsRequest || requestInfo.IsProxyRequest || requestInfo.IsProxyImg)
                return _next(httpContext);

            if (EventListener.Staticache != null)
            {
                var em = new EventStaticache(httpContext, requestInfo);

                foreach (Func<EventStaticache, bool> handler in EventListener.Staticache.GetInvocationList())
                {
                    if (!handler(em))
                        return _next(httpContext);
                }
            }

            StaticacheRoute route = null;

            if (init.routes?.Count > 0)
            {
                foreach (var r in init.routes)
                {
                    if (Regex.IsMatch(httpContext.Request.Path.Value, r.pathRex, RegexOptions.IgnoreCase))
                    {
                        route = r;
                        break;
                    }
                }
            }

            if (route == null)
            {
                var endpoint = httpContext.GetEndpoint();
                var staticache = endpoint?.Metadata.GetMetadata<StaticacheAttribute>();

                if (staticache == null)
                    return _next(httpContext);

                if (init.minimalCacheMinutes > staticache.cacheMinutes)
                    return _next(httpContext);

                if (init.disabledPaths?.Any(path => string.Equals(httpContext.Request.Path.Value, path, StringComparison.OrdinalIgnoreCase)) == true)
                    return _next(httpContext);

                string[] queryKeys = endpoint.Metadata
                    .GetMetadata<ControllerActionDescriptor>()?
                    .Parameters?
                    .Select(p => p.Name)
                    .ToArray();

                route = new StaticacheRoute(httpContext.Request.Path.Value, staticache.cacheMinutes, queryKeys);
            }

            if (0 >= route.cacheMinutes)
                route.cacheMinutes = 1;

            string cachekey = getQueryKeys(httpContext, route.queryKeys);

            if (cacheFiles.TryGetValue(cachekey, out CacheModel _r))
            {
                httpContext.Response.Headers["X-StatiCache-Status"] = "HIT";
                httpContext.Response.ContentType = _r.contentType;

                return httpContext.Response.SendFileAsync(_r.inFile);
            }

            httpContext.Features.Set(new StaticacheFeature(route, cachekey));
            return _next(httpContext);
        }


        static string getQueryKeys(HttpContext httpContext, string[] keys)
        {
            if (keys == null || keys.Length == 0)
                return string.Empty;

            var sb = StringBuilderPool.ThreadInstance;

            sb.Append(httpContext.Request.Scheme);
            sb.Append(":");

            sb.Append(httpContext.Request.Host.Value);
            sb.Append(":");

            sb.Append(httpContext.Request.Path.Value);
            sb.Append(":");

            foreach (string key in keys)
            {
                if (httpContext.Request.Query.TryGetValue(key, out StringValues value) && value.Count > 0)
                {
                    sb.Append(key);
                    sb.Append(":");
                    sb.Append(value[0]);
                    sb.Append(":");
                }
            }

            if (!keys.Contains("rjson") && httpContext.Request.Query.TryGetValue("rjson", out StringValues rjson) && rjson.Count > 0)
            {
                sb.Append("rjson:");
                sb.Append(rjson[0]);
            }

            return CrypTo.md5(sb);
        }

        public static string getFilePath(string cachekey, DateTimeOffset ex, string contentType)
        {
            return $"cache/static/{cachekey}-{ex.ToUnixTimeMilliseconds()}.{(contentType?.Contains("text/html") == true ? "html" : "json")}";
        }
    }
}
