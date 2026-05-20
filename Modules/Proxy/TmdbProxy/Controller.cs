using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Shared;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using IO = System.IO;

namespace TmdbProxy;

public class TmdbProxyController : BaseController
{
    #region static
    static readonly HttpClient http2ApiClient = FriendlyHttp.CreateHttp2Client();
    static readonly HttpClient http2ImgClient = FriendlyHttp.CreateHttp2Client();

    const string tmdbApiHost = "api.themoviedb.org";
    const string tmdbImgHost = "image.tmdb.org";

    static readonly Version httpVersion = ModInit.conf.httpversion switch
    {
        2 => HttpVersion.Version20,
        3 => HttpVersion.Version30,
        _ => HttpVersion.Version11
    };

    static readonly JsonWriterOptions jsonWriterOptions = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = true
    };

    static readonly IReadOnlyList<HeadersModel> headersImg = HeadersModel.Init(
        // используем старый ua что-бы гарантировать image/jpeg вместо image/webp
        ("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5"),
        ("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/534.57.2 (KHTML, like Gecko) Version/5.1.7 Safari/534.57.2"),
        ("Cache-Control", "max-age=0")
    );
    #endregion

    [HttpGet]
    [AllowAnonymous]
    [Route("tmdbproxy.js")]
    [Route("tmdbproxy/js/{token}")]
    public ActionResult TmdbProxy(string token)
    {
        SetHeadersNoCache();

        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "tmdbproxy.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return Content(plugin, "application/javascript; charset=utf-8");
    }

    [HttpGet]
    [Route("tmdb/{*suffix}")]
    public Task Tmdb()
    {
        string path = HttpContext.Request.Path.Value;

        if (path.StartsWith("/tmdb/api/", StringComparison.Ordinal))
            return API(HttpContext, hybridCache, requestInfo);

        if (path.StartsWith("/tmdb/img/", StringComparison.Ordinal))
            return IMG(HttpContext, requestInfo);

        if (path.Contains(tmdbApiHost, StringComparison.Ordinal))
            return API(HttpContext, hybridCache, requestInfo);

        if (path.Contains(tmdbImgHost, StringComparison.Ordinal))
            return IMG(HttpContext, requestInfo);

        HttpContext.Response.StatusCode = 403;
        return Task.CompletedTask;
    }


    #region API
    async static Task API(HttpContext httpContext, IHybridCache hybridCache, RequestModel requestInfo)
    {
        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
        {
            ctsHttp.CancelAfter(TimeSpan.FromSeconds(15));

            var bodyWriter = httpContext.Response.BodyWriter;
            httpContext.Response.ContentType = "application/json; charset=utf-8";

            ReadOnlySpan<char> path = RequestPath(httpContext.Request.Path.Value, "/tmdb/api/", tmdbApiHost);
            string uri = RequestUri(tmdbApiHost, path, httpContext.Request.Query);

            var entryCache = await hybridCache.EntryAsync<CacheModel>(uri, textJson: true);

            if (entryCache.success)
            {
                httpContext.Response.Headers["X-Cache-Status"] = "HIT";
                httpContext.Response.StatusCode = entryCache.value.statusCode;

                using (var writer = new Utf8JsonWriter(new ChunkBufferWriter<byte>(bodyWriter), jsonWriterOptions))
                    JsonSerializer.Serialize(writer, entryCache.value.json);

                await bodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                return;
            }
            else
            {
                httpContext.Response.Headers["X-Cache-Status"] = "MISS";

                var proxyManager = ModInit.conf.proxyapi?.useproxy == true
                    ? new ProxyManager("tmdb_api", ModInit.conf.proxyapi)
                    : null;

                var result = await Http.BaseGetAsync<JsonObject>(
                    uri,
                    textJson: true,
                    timeoutSeconds: 15,
                    httpversion: ModInit.conf.httpversion,
                    proxy: proxyManager?.Get(),
                    statusCodeOK: false,
                    httpClient: ModInit.conf.httpversion == 2
                        ? http2ApiClient
                        : null
                ).ConfigureAwait(false);

                if (result.content == null)
                {
                    proxyManager?.Refresh();
                    httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    bodyWriter.Write("{\"error\":true,\"msg\":\"json null\"}"u8);
                    await bodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                    return;
                }

                int statusCode = (int)result.response.StatusCode;
                httpContext.Response.StatusCode = statusCode;

                var cacheModel = new CacheModel(result.content, statusCode);

                if (result.content.ContainsKey("status_message") || result.response.StatusCode != HttpStatusCode.OK)
                    hybridCache.Set(uri, cacheModel, DateTime.Now.AddMinutes(1), inmemory: true);
                else
                    hybridCache.Set(uri, cacheModel, DateTime.Now.AddMinutes(ModInit.conf.cache_api), textJson: true);

                using (var writer = new Utf8JsonWriter(new ChunkBufferWriter<byte>(bodyWriter), jsonWriterOptions))
                    JsonSerializer.Serialize(writer, result.content);

                await bodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
            }
        }
    }
    #endregion

    #region IMG
    async static Task IMG(HttpContext httpContext, RequestModel requestInfo)
    {
        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
        {
            ctsHttp.CancelAfter(TimeSpan.FromSeconds(15));

            ReadOnlySpan<char> path = RequestPath(httpContext.Request.Path.Value, "/tmdb/img/", tmdbImgHost);
            string uri = RequestUri(tmdbImgHost, path, httpContext.Request.Query);

            string md5key = CrypTo.md5(uri);
            string outFile = ModInit.fileWatcher.OutFile(md5key);

            httpContext.Response.Headers[HeaderNames.CacheControl] = "public,max-age=86400,immutable";
            httpContext.Response.ContentType = path.Contains(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : path.Contains(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/jpeg";

            #region cacheFiles
            if (ModInit.fileWatcher.TryGetValue(md5key, out var _fileCache))
            {
                httpContext.Response.Headers["X-Cache-Status"] = "HIT";

                if (ModInit.conf.responseContentLength && _fileCache.Length > 0)
                    httpContext.Response.ContentLength = _fileCache.Length;

                await httpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                return;
            }
            #endregion

            ProxyManager proxyManager = null;
            var semaphore = new SemaphorManager(outFile, ctsHttp.Token);

            try
            {
                bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                if (!_acquired)
                {
                    httpContext.Response.Redirect(uri);
                    return;
                }

                if (ctsHttp.IsCancellationRequested)
                    return;

                #region cacheFiles
                if (ModInit.fileWatcher.TryGetValue(md5key, out _fileCache))
                {
                    httpContext.Response.Headers["X-Cache-Status"] = "HIT";

                    if (ModInit.conf.responseContentLength && _fileCache.Length > 0)
                        httpContext.Response.ContentLength = _fileCache.Length;

                    semaphore.Release();
                    await httpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                    return;
                }
                #endregion

                proxyManager = ModInit.conf.proxyimg?.useproxy == true
                    ? new ProxyManager("tmdb_img", ModInit.conf.proxyimg)
                    : null;

                #region http client
                var client = FriendlyHttp.MessageClient(
                    "proxyimg",
                    Http.Handler(uri, proxyManager?.Get()),
                    out bool disposeHttpClient,
                    httpClient: ModInit.conf.httpversion == 2
                        ? http2ImgClient
                        : null
                );

                var req = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Version = httpVersion
                };

                foreach (var h in headersImg)
                    req.Headers.TryAddWithoutValidation(h.name, h.val);
                #endregion

                try
                {
                    using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctsHttp.Token).ConfigureAwait(false))
                    {
                        httpContext.Response.StatusCode = (int)response.StatusCode;

                        if (ModInit.conf.responseContentLength && response.Content?.Headers?.ContentLength > 0)
                            httpContext.Response.ContentLength = response.Content.Headers.ContentLength.Value;

                        await using (var responseStream = await response.Content.ReadAsStreamAsync(ctsHttp.Token).ConfigureAwait(false))
                        {
                            using (var nbuf = new BufferPool())
                            {
                                int bytesRead;
                                var memBuf = nbuf.Memory;

                                if (response.StatusCode == HttpStatusCode.OK)
                                {
                                    #region cache img
                                    httpContext.Response.Headers["X-Cache-Status"] = "MISS";

                                    try
                                    {
                                        int cacheLength = 0;
                                        bool isFullyRead = false;

                                        ModInit.fileWatcher.EnsureDirectory(md5key);

                                        await using (var cacheStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None,
                                            bufferSize: PoolInvk.bufferSize,
                                            options: FileOptions.Asynchronous))
                                        {
                                            while (true)
                                            {
                                                bytesRead = await responseStream.ReadAsync(memBuf, ctsHttp.Token).ConfigureAwait(false);
                                                if (bytesRead <= 0)
                                                {
                                                    isFullyRead = true;
                                                    break;
                                                }

                                                cacheLength += bytesRead;
                                                if (ctsHttp.IsCancellationRequested)
                                                    break;

                                                var wrm = memBuf.Slice(0, bytesRead);
                                                await cacheStream.WriteAsync(wrm).ConfigureAwait(false);
                                                await httpContext.Response.Body.WriteAsync(wrm, ctsHttp.Token).ConfigureAwait(false);
                                            }
                                        }

                                        if (isFullyRead)
                                        {
                                            proxyManager?.Success();

                                            if (response.Content.Headers.ContentLength.HasValue)
                                            {
                                                if (response.Content.Headers.ContentLength.Value == cacheLength)
                                                    ModInit.fileWatcher.Add(md5key, cacheLength);
                                                else
                                                    IO.File.Delete(outFile);
                                            }
                                            else
                                            {
                                                ModInit.fileWatcher.Add(md5key, cacheLength);
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        IO.File.Delete(outFile);
                                    }
                                    #endregion
                                }
                                else
                                {
                                    #region проксируем ошибку
                                    proxyManager?.Refresh();

                                    httpContext.Response.Headers["X-Cache-Status"] = "bypass";

                                    while ((bytesRead = await responseStream.ReadAsync(memBuf, ctsHttp.Token).ConfigureAwait(false)) > 0)
                                    {
                                        if (ctsHttp.IsCancellationRequested)
                                            break;

                                        await httpContext.Response.Body.WriteAsync(memBuf.Slice(0, bytesRead), ctsHttp.Token).ConfigureAwait(false);
                                    }
                                    #endregion
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (disposeHttpClient)
                        client.Dispose();
                }
            }
            catch
            {
                proxyManager?.Refresh();
                httpContext.Response.Redirect(uri);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
    #endregion


    #region Utilities
    static ReadOnlySpan<char> RequestPath(string pathString, string route, string tmdbHost)
    {
        ReadOnlySpan<char> path = pathString.AsSpan();

        if (path.StartsWith(route))
            path = path.Slice(9);

        else if (path.StartsWith("/tmdb/"))
            path = path.Slice(5);

        if (path.StartsWith("/https:"))
            path = path.Slice(9 + tmdbApiHost.Length);
        else if (path.StartsWith("/http:"))
            path = path.Slice(8 + tmdbApiHost.Length);

        if (path.StartsWith("//"))
            path = path.Slice(1);

        if (path.EndsWith('/'))
            path = path[..^1];

        return path;
    }

    static string RequestUri(ReadOnlySpan<char> host, ReadOnlySpan<char> path, IQueryCollection query)
    {
        var uri = StringBuilderPool.ThreadInstance;

        uri.Append("https://")
           .Append(host)
           .Append(path);

        var firstArg = true;

        foreach (var q in query)
        {
            if (q.Key is "account_email" or "email" or "box_mac" or "uid" or "token" or "nws_id")
                continue;

            var values = q.Value;
            if (values.Count == 0)
                continue;

            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (string.IsNullOrEmpty(value))
                    continue;

                uri.Append(firstArg ? '?' : '&');
                uri.Append(q.Key).Append('=').Append(value);
                firstArg = false;
            }
        }

        return uri.ToString();
    }
    #endregion
}
