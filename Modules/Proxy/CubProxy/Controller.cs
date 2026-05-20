using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.Buffers;
using System.Collections.Frozen;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace CubProxy;

public class CubProxyController : BaseController
{
    static readonly string[] adEmpty = new string[] { };

    [HttpGet]
    [AllowAnonymous]
    [Route("cubproxy.js")]
    [Route("cubproxy/js/{token}")]
    public ActionResult Plugin(string token)
    {
        SetHeadersNoCache();

        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "cubproxy.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return Content(plugin, "application/javascript; charset=utf-8");
    }


    [HttpGet]
    [HttpPost]
    [AllowAnonymous]
    [Route("cub/{*suffix}")]
    async public Task Proxy()
    {
        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
        {
            ctsHttp.CancelAfter(TimeSpan.FromSeconds(10));

            var init = ModInit.conf;

            string path = HttpContext.Request.Path.Value
                .Substring(5)
                .ToLowerInvariant();

            int dotIndex = path.IndexOf('.');
            string subdomain = dotIndex >= 0 ? path[..dotIndex] : string.Empty;
            string domain = GetDomain(subdomain, init.domain);

            int slashIndex = path.IndexOf('/');
            string uri = (slashIndex >= 0 ? path.Substring(slashIndex + 1) : path) + HttpContext.Request.QueryString.Value;

            #region ws/geo
            if (subdomain.Equals("ws"))
            {
                HttpContext.Response.Redirect($"https://{domain}{HttpContext.Request.QueryString.Value}");
                return;
            }
            else if (subdomain.Equals("geo"))
            {
                string country = requestInfo.Country;
                if (country == null)
                    country = await mylocalip();

                await HttpContext.Response.WriteAsync(country ?? string.Empty, ctsHttp.Token);
                return;
            }
            #endregion

            #region checker
            if (path.StartsWith("api/checker") || uri.StartsWith("api/checker"))
            {
                if (HttpMethods.IsPost(HttpContext.Request.Method))
                {
                    var ct = HttpContext.Request.ContentType;
                    if (ct != null && ct.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, false, PoolInvk.bufferSize, leaveOpen: true))
                        {
                            string form = await reader.ReadToEndAsync();

                            var match = Regex.Match(form, @"(?:^|&)data=([^&]+)");
                            if (match.Success)
                            {
                                string dataValue = Uri.UnescapeDataString(match.Groups[1].Value);
                                await HttpContext.Response.WriteAsync(dataValue, ctsHttp.Token);
                                return;
                            }
                        }
                    }
                }

                HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                HttpContext.Response.StatusCode = StatusCodes.Status200OK;
                HttpContext.Response.BodyWriter.Write("ok"u8);
                await HttpContext.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                return;
            }
            #endregion

            #region blacklist
            if (uri.StartsWith("api/plugins/blacklist"))
            {
                HttpContext.Response.ContentType = "application/json; charset=utf-8";
                HttpContext.Response.StatusCode = StatusCodes.Status200OK;
                HttpContext.Response.BodyWriter.Write("[]"u8);
                await HttpContext.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                return;
            }
            #endregion

            #region ads/log/metric
            if (uri.StartsWith("api/metric/") || uri.StartsWith("api/ad/stat"))
            {
                HttpContext.Response.ContentType = "application/json; charset=utf-8";
                HttpContext.Response.StatusCode = StatusCodes.Status200OK;
                HttpContext.Response.BodyWriter.Write("{\"secuses\":true}"u8);
                await HttpContext.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                return;
            }

            if (uri.StartsWith("api/ad/vast"))
            {
                await HttpContext.Response.WriteAsJsonAsync(new
                {
                    secuses = true,
                    ad = adEmpty,
                    day_of_month = DateTime.Now.Day,
                    days_in_month = 31,
                    month = DateTime.Now.Month
                });
                return;
            }
            #endregion

            var proxyManager = init.useproxy
                ? new ProxyManager("cub_api", init)
                : null;

            var proxy = proxyManager?.Get();

            bool isMedia = Regex.IsMatch(path, "\\.(jpe?g|png|gif|webp|ico|svg|mp4|js|css)", RegexOptions.IgnoreCase);

            if (0 >= init.cache_api || !HttpMethods.IsGet(HttpContext.Request.Method) || isMedia ||
                (subdomain is "imagetmdb" or "cdn" or "ad") ||
                HttpContext.Request.Headers.ContainsKey("token") || HttpContext.Request.Headers.ContainsKey("profile"))
            {
                #region bypass or media cache
                string md5key = CrypTo.md5Builder(writer =>
                {
                    writer.Append(domain);
                    writer.Append(':');
                    writer.Append(uri);
                });

                string outFile = ModInit.fileWatcher.OutFile(md5key);

                if (ModInit.fileWatcher.TryGetValue(md5key, out var _fileCache))
                {
                    HttpContext.Response.Headers["X-Cache-Status"] = "HIT";
                    HttpContext.Response.ContentType = getContentType(path);

                    if (_fileCache.Length > 0)
                        HttpContext.Response.ContentLength = _fileCache.Length;

                    await HttpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                    return;
                }
                else
                {
                    var handler = new HttpClientHandler()
                    {
                        ServerCertificateCustomValidationCallback = Http.AlwaysAllowCertificate,
                        AutomaticDecompression = DecompressionMethods.None,
                        AllowAutoRedirect = true
                    };

                    if (proxy != null)
                    {
                        handler.UseProxy = true;
                        handler.Proxy = proxy;
                    }
                    else
                    {
                        handler.UseProxy = false;
                    }

                    var client = FriendlyHttp.MessageClient(
                        "proxyRedirect",
                        handler,
                        out bool disposeHttpClient
                    );

                    try
                    {
                        var request = CreateProxyHttpRequest(
                            HttpContext,
                            new Uri($"{init.scheme}://{domain}/{uri}"),
                            requestInfo,
                            init.viewru && subdomain == "tmdb"
                        );

                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctsHttp.Token).ConfigureAwait(false))
                        {
                            if (init.cache_img > 0 && isMedia && HttpMethods.IsGet(HttpContext.Request.Method) && response.StatusCode == HttpStatusCode.OK)
                            {
                                #region cache img
                                HttpContext.Response.ContentType = getContentType(path);
                                HttpContext.Response.Headers["X-Cache-Status"] = "MISS";

                                if (init.responseContentLength && response.Content?.Headers?.ContentLength > 0)
                                {
                                    if (!CoreInit.ContainsMimeTypes(HttpContext.Response.ContentType))
                                        HttpContext.Response.ContentLength = response.Content.Headers.ContentLength.Value;
                                }

                                var semaphore = new SemaphorManager(outFile, ctsHttp.Token);

                                try
                                {
                                    bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                                    if (!_acquired)
                                        return;

                                    using (var nbuf = new BufferPool())
                                    {
                                        try
                                        {
                                            int cacheLength = 0;
                                            var memBuf = nbuf.Memory;

                                            ModInit.fileWatcher.EnsureDirectory(md5key);

                                            await using (var cacheStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None,
                                                bufferSize: PoolInvk.bufferSize,
                                                options: FileOptions.Asynchronous))
                                            {
                                                await using (var responseStream = await response.Content.ReadAsStreamAsync(ctsHttp.Token).ConfigureAwait(false))
                                                {
                                                    int bytesRead;

                                                    while ((bytesRead = await responseStream.ReadAsync(memBuf, ctsHttp.Token).ConfigureAwait(false)) > 0)
                                                    {
                                                        if (ctsHttp.IsCancellationRequested)
                                                            break;

                                                        cacheLength += bytesRead;
                                                        await cacheStream.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                                                        await HttpContext.Response.Body.WriteAsync(memBuf.Slice(0, bytesRead), ctsHttp.Token).ConfigureAwait(false);
                                                    }
                                                }
                                            }

                                            if (response.Content.Headers.ContentLength.HasValue)
                                            {
                                                if (response.Content.Headers.ContentLength.Value == cacheLength)
                                                    ModInit.fileWatcher.Add(md5key, cacheLength);
                                                else
                                                    System.IO.File.Delete(outFile);
                                            }
                                            else
                                            {
                                                ModInit.fileWatcher.Add(md5key, cacheLength);
                                            }
                                        }
                                        catch
                                        {
                                            System.IO.File.Delete(outFile);
                                        }
                                    }
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                                #endregion
                            }
                            else
                            {
                                HttpContext.Response.Headers["X-Cache-Status"] = "bypass";
                                await CopyProxyHttpResponse(HttpContext, response, ctsHttp.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        if (disposeHttpClient)
                            client.Dispose();
                    }
                }
                #endregion
            }
            else
            {
                #region cache string
                string memkey = CrypTo.md5Builder(writer =>
                {
                    writer.Append("cubproxy:key2:");
                    writer.Append(domain);
                    writer.Append(':');
                    writer.Append(uri);
                });

                (byte[] content, int statusCode, string contentType) cache = default;

                var semaphore = new SemaphorManager(memkey, ctsHttp.Token);

                try
                {
                    bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                    if (!_acquired)
                    {
                        HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                        HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                        HttpContext.Response.BodyWriter.Write("502 Bad Gateway"u8);
                        await HttpContext.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }

                    if (!hybridCache.TryGetValue(memkey, out cache))
                    {
                        var headers = HeadersModel.Init();

                        if (requestInfo.Country != null)
                        {
                            headers.Add(new("X-Forwarded-For", requestInfo.IP));
                            headers.Add(new("X-Real-IP", requestInfo.IP));
                        }
                        else
                        {
                            string myip = await mylocalip();
                            headers.Add(new("X-Forwarded-For", myip));
                            headers.Add(new("X-Real-IP", myip));
                        }

                        if (subdomain == "tmdb")
                        {
                            if (init.viewru)
                                headers.Add(new("cookie", "viewru=1"));

                            headers.Add(new("user-agent", HttpContext.Request.Headers.UserAgent.ToString()));
                        }
                        else
                        {
                            foreach (var header in HttpContext.Request.Headers)
                            {
                                if (header.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
                                    header.Key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                                    headers.Add(new(header.Key, header.Value.ToString()));
                            }
                        }

                        var result = await Http.BaseGet(
                            $"{init.scheme}://{domain}/{uri}",
                            timeoutSeconds: 10,
                            proxy: proxy,
                            headers: headers,
                            statusCodeOK: false,
                            useDefaultHeaders: false
                        ).ConfigureAwait(false);

                        if (string.IsNullOrEmpty(result.content))
                        {
                            proxyManager?.Refresh();
                            HttpContext.Response.StatusCode = (int)result.response.StatusCode;
                            return;
                        }

                        cache.content = Encoding.UTF8.GetBytes(result.content);
                        cache.statusCode = (int)result.response.StatusCode;
                        cache.contentType = result.response.Content?.Headers?.ContentType?.ToString() ?? getContentType(path);

                        if (subdomain is "tmdb" or "tmapi" or "apitmdb")
                        {
                            if (result.content == "{\"blocked\":true}")
                            {
                                string json = await Http.Get(
                                    $"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/tmdb/api/{uri}",
                                    timeoutSeconds: 5,
                                    headers: HeadersModel.Init(("lcrqpasswd", CoreInit.rootPasswd))
                                ).ConfigureAwait(false);

                                if (!string.IsNullOrEmpty(json))
                                {
                                    cache.statusCode = 200;
                                    cache.contentType = "application/json; charset=utf-8";
                                    cache.content = Encoding.UTF8.GetBytes(json);
                                }
                            }
                        }

                        HttpContext.Response.Headers["X-Cache-Status"] = "MISS";

                        if (cache.statusCode == 200)
                        {
                            proxyManager?.Success();
                            hybridCache.Set(memkey, cache, DateTime.Now.AddMinutes(init.cache_api), inmemory: false);
                        }
                        else
                        {
                            proxyManager?.Refresh();
                            hybridCache.Set(memkey, cache, DateTime.Now.AddSeconds(5), inmemory: true);
                        }
                    }
                    else
                    {
                        HttpContext.Response.Headers["X-Cache-Status"] = "HIT";
                    }
                }
                finally
                {
                    semaphore.Release();
                }

                if (!CoreInit.ContainsMimeTypes(cache.contentType))
                    HttpContext.Response.ContentLength = cache.content.Length;

                HttpContext.Response.StatusCode = cache.statusCode;
                HttpContext.Response.ContentType = cache.contentType;
                await HttpContext.Response.Body.WriteAsync(cache.content, ctsHttp.Token).ConfigureAwait(false);
                #endregion
            }
        }
    }


    #region CreateProxyHttpRequest
    static readonly FrozenSet<string> excludedRequestHeaders = new[]
    {
        "host",
        "origin",
        "referer",
        "content-disposition",
        "accept-encoding"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    HttpRequestMessage CreateProxyHttpRequest(HttpContext context, Uri uri, RequestModel requestInfo, bool viewru)
    {
        var request = context.Request;

        var requestMessage = new HttpRequestMessage();

        var requestMethod = request.Method;
        if (HttpMethods.IsPost(requestMethod))
        {
            var streamContent = new StreamContent(request.Body);
            requestMessage.Content = streamContent;
        }

        if (viewru)
            request.Headers["Cookie"] = "viewru=1";

        if (requestInfo.Country != null)
        {
            request.Headers["X-Forwarded-For"] = requestInfo.IP;
            request.Headers["X-Real-IP"] = requestInfo.IP;
        }

        #region Headers
        foreach (var header in request.Headers)
        {
            string key = header.Key;

            if (excludedRequestHeaders.Contains(key))
                continue;

            if (viewru && key.Equals("cookie", StringComparison.OrdinalIgnoreCase))
                continue;

            if (key.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                if (requestMessage.Content?.Headers != null)
                    requestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }
        #endregion

        requestMessage.Headers.Host = uri.Authority;
        requestMessage.RequestUri = uri;
        //requestMessage.Version = new Version(2, 0);

        requestMessage.Method = HttpMethods.IsGet(request.Method)
            ? HttpMethod.Get
            : HttpMethods.IsPost(request.Method)
                ? HttpMethod.Post
                : new HttpMethod(request.Method);

        return requestMessage;
    }
    #endregion

    #region CopyProxyHttpResponse
    static readonly FrozenSet<string> excludedResponseHeaders = new[]
    {
        "server",
        "transfer-encoding",
        "etag",
        "connection",
        "content-security-policy",
        "content-disposition"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = (int)responseMessage.StatusCode;

        #region UpdateHeaders
        void UpdateHeaders(HttpHeaders headers)
        {
            if (headers == null)
                return;

            foreach (var header in headers)
            {
                string key = header.Key;

                if (excludedResponseHeaders.Contains(key))
                    continue;

                if (key.StartsWith("x-", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("alt-", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (key.StartsWith("access-control", StringComparison.OrdinalIgnoreCase))
                    continue;

                var values = header.Value;

                using (var e = values.GetEnumerator())
                {
                    if (!e.MoveNext())
                        continue;

                    var first = e.Current;

                    response.Headers[key] = e.MoveNext()
                        ? string.Join("; ", values)
                        : first;
                }
            }
        }
        #endregion

        UpdateHeaders(responseMessage.Headers);
        UpdateHeaders(responseMessage.Content?.Headers);

        await using (var responseStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            using (var nbuf = new BufferPool())
            {
                int bytesRead;
                var memBuf = nbuf.Memory;

                while ((bytesRead = await responseStream.ReadAsync(memBuf, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await response.Body.WriteAsync(memBuf.Slice(0, bytesRead), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
    #endregion


    #region Utilities
    static string GetDomain(string subdomain, string domain)
    {
        if (subdomain is "geo" or "tmdb" or "tmapi" or "apitmdb" or "imagetmdb" or "cdn" or "ad" or "ws")
        {
            var uri = StringBuilderPool.ThreadInstance;

            uri.Append(subdomain)
               .Append('.')
               .Append(domain);

            return uri.ToString();
        }

        return domain;
    }

    static string getContentType(string path)
    {
        return Path.GetExtension(path) switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".mp4" => "video/mp4",
            ".js" => "application/javascript",
            ".css" => "text/css",
            _ => "application/octet-stream"
        };
    }
    #endregion
}
