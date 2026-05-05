using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
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
    [HttpGet]
    [AllowAnonymous]
    [Route("cubproxy.js")]
    [Route("cubproxy/js/{token}")]
    public ActionResult CubProxy(string token)
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
    async public Task Cub()
    {
        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
        {
            ctsHttp.CancelAfter(TimeSpan.FromSeconds(10));

            var requestInfo = HttpContext.Features.Get<RequestModel>();
            var hybridCache = HybridCache.Get();

            var init = ModInit.conf;
            string domain = init.domain;
            string path = HttpContext.Request.Path.Value.Replace("/cub/", "", StringComparison.OrdinalIgnoreCase);
            string query = HttpContext.Request.QueryString.Value;
            string uri = Regex.Match(path, "^[^/]+/(.*)", RegexOptions.IgnoreCase).Groups[1].Value + query;

            if (domain == "ws")
            {
                HttpContext.Response.Redirect($"https://{path}/{query}");
                return;
            }

            if (path.Split(".")[0] is "geo" or "tmdb" or "tmapi" or "apitmdb" or "imagetmdb" or "cdn" or "ad" or "ws")
                domain = $"{path.Split(".")[0]}.{domain}";

            if (domain.StartsWith("geo", StringComparison.OrdinalIgnoreCase))
            {
                string country = requestInfo.Country;
                if (country == null)
                {
                    var ipify = await Http.Get<JObject>("https://api.ipify.org/?format=json");
                    if (ipify != null || !string.IsNullOrEmpty(ipify.Value<string>("ip")))
                        country = GeoIP2.Country(ipify.Value<string>("ip"));
                }

                await HttpContext.Response.WriteAsync(country ?? "", ctsHttp.Token);
                return;
            }

            #region checker
            if (path.StartsWith("api/checker", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("api/checker", StringComparison.OrdinalIgnoreCase))
            {
                if (HttpMethods.IsPost(HttpContext.Request.Method))
                {
                    if (HttpContext.Request.ContentType != null &&
                        HttpContext.Request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
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

                await HttpContext.Response.WriteAsync("ok", ctsHttp.Token);
                return;
            }
            #endregion

            #region blacklist
            if (uri.StartsWith("api/plugins/blacklist", StringComparison.OrdinalIgnoreCase))
            {
                HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await HttpContext.Response.WriteAsync("[]", ctsHttp.Token);
                return;
            }
            #endregion

            #region ads/log/metric
            if (uri.StartsWith("api/metric/", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("api/ad/stat", StringComparison.OrdinalIgnoreCase))
            {
                await HttpContext.Response.WriteAsJsonAsync(new { secuses = true });
                return;
            }

            if (uri.StartsWith("api/ad/vast", StringComparison.OrdinalIgnoreCase))
            {
                await HttpContext.Response.WriteAsJsonAsync(new
                {
                    secuses = true,
                    ad = new string[] { },
                    day_of_month = DateTime.Now.Day,
                    days_in_month = 31,
                    month = DateTime.Now.Month
                });
                return;
            }
            #endregion

            var proxyManager = new ProxyManager("cub_api", init);
            var proxy = proxyManager.Get();

            bool isMedia = Regex.IsMatch(uri, "\\.(jpe?g|png|gif|webp|ico|svg|mp4|js|css)", RegexOptions.IgnoreCase);

            if (0 >= init.cache_api || !HttpMethods.IsGet(HttpContext.Request.Method) || isMedia ||
                (path.Split(".")[0] is "imagetmdb" or "cdn" or "ad") ||
                HttpContext.Request.Headers.ContainsKey("token") || HttpContext.Request.Headers.ContainsKey("profile"))
            {
                #region bypass or media cache
                string md5key = CrypTo.md5($"{domain}:{uri}");
                string outFile = ModInit.fileWatcher.OutFile(md5key);

                if (ModInit.fileWatcher.TryGetValue(md5key, out var _fileCache))
                {
                    HttpContext.Response.Headers["X-Cache-Status"] = "HIT";
                    HttpContext.Response.ContentType = getContentType(uri);

                    if (_fileCache.Length > 0)
                        HttpContext.Response.ContentLength = _fileCache.Length;

                    await HttpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                    return;
                }

                var handler = new HttpClientHandler()
                {
                    AutomaticDecompression = DecompressionMethods.None,
                    AllowAutoRedirect = true
                };

                handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

                if (proxy != null)
                {
                    handler.UseProxy = true;
                    handler.Proxy = proxy;
                }
                else { handler.UseProxy = false; }

                var client = FriendlyHttp.MessageClient("proxyRedirect", handler);
                var request = CreateProxyHttpRequest(HttpContext, new Uri($"{init.scheme}://{domain}/{uri}"), requestInfo, init.viewru && path.Split(".")[0] == "tmdb");

                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctsHttp.Token).ConfigureAwait(false))
                {
                    if (init.cache_img > 0 && isMedia && HttpMethods.IsGet(HttpContext.Request.Method) && response.StatusCode == HttpStatusCode.OK)
                    {
                        #region cache
                        HttpContext.Response.ContentType = getContentType(uri);
                        HttpContext.Response.Headers["X-Cache-Status"] = "MISS";

                        if (init.responseContentLength && response.Content?.Headers?.ContentLength > 0)
                        {
                            if (!CoreInit.CompressionMimeTypes.Contains(HttpContext.Response.ContentType))
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

                                    await using (var cacheStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: PoolInvk.bufferSize, options: FileOptions.Asynchronous))
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
                                        {
                                            ModInit.fileWatcher.Add(md5key, cacheLength);
                                        }
                                        else
                                        {
                                            System.IO.File.Delete(outFile);
                                        }
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
                #endregion
            }
            else
            {
                #region cache string
                string memkey = $"cubproxy:key2:{domain}:{uri}";
                (byte[] content, int statusCode, string contentType) cache = default;

                var semaphore = new SemaphorManager(memkey, ctsHttp.Token);

                try
                {
                    bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                    if (!_acquired)
                    {
                        HttpContext.Response.StatusCode = 502;
                        await HttpContext.Response.WriteAsync("502 Bad Gateway", ctsHttp.Token).ConfigureAwait(false);
                    }

                    if (!hybridCache.TryGetValue(memkey, out cache))
                    {
                        var headers = HeadersModel.Init();

                        if (requestInfo.Country != null)
                        {
                            headers.Add(new HeadersModel("X-Forwarded-For", requestInfo.IP));
                            headers.Add(new HeadersModel("X-Real-IP", requestInfo.IP));
                        }

                        if (path.Split(".")[0] == "tmdb")
                        {
                            if (init.viewru)
                                headers.Add(new HeadersModel("cookie", "viewru=1"));

                            headers.Add(new HeadersModel("user-agent", HttpContext.Request.Headers.UserAgent.ToString()));
                        }
                        else
                        {
                            foreach (var header in HttpContext.Request.Headers)
                            {
                                if (header.Key.ToLower() is "cookie" or "user-agent")
                                    headers.Add(new HeadersModel(header.Key, header.Value.ToString()));
                            }
                        }

                        var result = await Http.BaseGet($"{init.scheme}://{domain}/{uri}", timeoutSeconds: 10, proxy: proxy, headers: headers, statusCodeOK: false, useDefaultHeaders: false).ConfigureAwait(false);
                        if (string.IsNullOrEmpty(result.content))
                        {
                            proxyManager.Refresh();
                            HttpContext.Response.StatusCode = (int)result.response.StatusCode;
                            return;
                        }

                        cache.content = Encoding.UTF8.GetBytes(result.content);
                        cache.statusCode = (int)result.response.StatusCode;
                        cache.contentType = result.response.Content?.Headers?.ContentType?.ToString() ?? getContentType(uri);

                        if (domain.StartsWith("tmdb") || domain.StartsWith("tmapi") || domain.StartsWith("apitmdb"))
                        {
                            if (result.content == "{\"blocked\":true}")
                            {
                                var header = HeadersModel.Init(("lcrqpasswd", CoreInit.rootPasswd));
                                string json = await Http.Get($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/tmdb/api/{uri}", timeoutSeconds: 5, headers: header).ConfigureAwait(false);
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
                            proxyManager.Success();
                            hybridCache.Set(memkey, cache, DateTime.Now.AddMinutes(init.cache_api), inmemory: false);
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

                if (!CoreInit.CompressionMimeTypes.Contains(cache.contentType))
                    HttpContext.Response.ContentLength = cache.content.Length;

                HttpContext.Response.StatusCode = cache.statusCode;
                HttpContext.Response.ContentType = cache.contentType;
                await HttpContext.Response.Body.WriteAsync(cache.content, ctsHttp.Token).ConfigureAwait(false);
                #endregion
            }
        }
    }


    #region getContentType
    static string getContentType(string uri)
    {
        return Path.GetExtension(uri).ToLowerInvariant() switch
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

    #region CreateProxyHttpRequest
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

            if (key.Equals("host", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("origin", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("referer", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("content-disposition", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase))
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
        requestMessage.Method = new HttpMethod(request.Method);
        //requestMessage.Version = new Version(2, 0);

        return requestMessage;
    }
    #endregion

    #region CopyProxyHttpResponse
    async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage, CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = (int)responseMessage.StatusCode;

        #region UpdateHeaders
        void UpdateHeaders(HttpHeaders headers)
        {
            foreach (var header in headers)
            {
                string key = header.Key;

                if (key.Equals("server", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("etag", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("connection", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("content-security-policy", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("content-disposition", StringComparison.OrdinalIgnoreCase))
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
        UpdateHeaders(responseMessage.Content.Headers);

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
}
