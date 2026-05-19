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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<TmdbProxyController>();

    static readonly JsonWriterOptions jsonWriterOptions = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = true
    };
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
        if (HttpContext.Request.Path.Value.StartsWith("/tmdb/api/", StringComparison.OrdinalIgnoreCase))
            return API(HttpContext, hybridCache, requestInfo);

        if (HttpContext.Request.Path.Value.StartsWith("/tmdb/img/", StringComparison.OrdinalIgnoreCase))
            return IMG(HttpContext, requestInfo);

        string path = Regex.Replace(HttpContext.Request.Path.Value, "^/tmdb/https?://", "", RegexOptions.IgnoreCase).Replace("/tmdb/", "");
        string uri = Regex.Match(path, "^[^/]+/(.*)", RegexOptions.IgnoreCase).Groups[1].Value + HttpContext.Request.QueryString.Value;

        if (path.Contains("api.themoviedb.org", StringComparison.OrdinalIgnoreCase))
        {
            HttpContext.Request.Path = $"/tmdb/api/{uri}";
            return API(HttpContext, hybridCache, requestInfo);
        }
        else if (path.Contains("image.tmdb.org", StringComparison.OrdinalIgnoreCase))
        {
            HttpContext.Request.Path = $"/tmdb/img/{uri}";
            return IMG(HttpContext, requestInfo);
        }

        HttpContext.Response.StatusCode = 403;
        return Task.CompletedTask;
    }


    #region API
    async static Task API(HttpContext httpContex, IHybridCache hybridCache, RequestModel requestInfo)
    {
        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContex.RequestAborted))
        {
            ctsHttp.CancelAfter(TimeSpan.FromSeconds(15));
            httpContex.Response.ContentType = "application/json; charset=utf-8";

            string path = httpContex.Request.Path.Value.Replace("/tmdb/api", "", StringComparison.OrdinalIgnoreCase);
            path = Regex.Replace(path, "^/https?://api.themoviedb.org", "", RegexOptions.IgnoreCase);
            path = Regex.Replace(path, "/$", "", RegexOptions.IgnoreCase);

            string query = Regex.Replace(httpContex.Request.QueryString.Value, "(&|\\?)(account_email|email|uid|token|nws_id)=[^&]+", "");
            string uri = "https://api.themoviedb.org" + path + query;

            string mkey = $"tmdb/api:{path}:{query}";
            var entryCache = await hybridCache.EntryAsync<CacheModel>(mkey, textJson: true);

            var bodyWriter = httpContex.Response.BodyWriter;

            if (entryCache.success)
            {
                httpContex.Response.Headers["X-Cache-Status"] = "HIT";
                httpContex.Response.StatusCode = entryCache.value.statusCode;
                httpContex.Response.ContentType = "application/json; charset=utf-8";

                using (var writer = new Utf8JsonWriter(new ChunkBufferWriter<byte>(bodyWriter), jsonWriterOptions))
                    JsonSerializer.Serialize(writer, entryCache.value.json);

                await bodyWriter.FlushAsync().ConfigureAwait(false);
                return;
            }
            else
            {
                httpContex.Response.Headers["X-Cache-Status"] = "MISS";

                var proxyManager = new ProxyManager("tmdb_api", ModInit.conf.proxyapi);
                var proxy = proxyManager.Get();

                var result = await Http.BaseGetAsync<JsonObject>(uri, textJson: true, timeoutSeconds: 15, httpversion: ModInit.conf.httpversion, proxy: proxy, statusCodeOK: false, httpClient: http2ApiClient);
                if (result.content == null)
                {
                    httpContex.Response.ContentType = "application/json; charset=utf-8";
                    httpContex.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    httpContex.Response.BodyWriter.Write("{\"error\":true,\"msg\":\"json null\"}"u8);
                    await httpContex.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                    return;
                }

                int statusCode = (int)result.response.StatusCode;
                httpContex.Response.StatusCode = statusCode;

                if (result.content.ContainsKey("status_message") || result.response.StatusCode != HttpStatusCode.OK)
                {
                    hybridCache.Set(mkey, new CacheModel(result.content, statusCode), DateTime.Now.AddMinutes(1), inmemory: true);

                    using (var writer = new Utf8JsonWriter(new ChunkBufferWriter<byte>(bodyWriter), jsonWriterOptions))
                        JsonSerializer.Serialize(writer, result.content);

                    await bodyWriter.FlushAsync().ConfigureAwait(false);
                    return;
                }
                else
                {
                    hybridCache.Set(mkey, new CacheModel(result.content, statusCode), DateTime.Now.AddMinutes(ModInit.conf.cache_api), textJson: true);

                    httpContex.Response.ContentType = "application/json; charset=utf-8";

                    using (var writer = new Utf8JsonWriter(new ChunkBufferWriter<byte>(bodyWriter), jsonWriterOptions))
                        JsonSerializer.Serialize(writer, result.content);

                    await bodyWriter.FlushAsync().ConfigureAwait(false);
                }
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

            string path = httpContext.Request.Path.Value.Replace("/tmdb/img", "", StringComparison.OrdinalIgnoreCase);
            path = Regex.Replace(path, "^/https?://image.tmdb.org", "", RegexOptions.IgnoreCase);

            string query = Regex.Replace(httpContext.Request.QueryString.Value, "(&|\\?)(account_email|email|uid|token|nws_id)=[^&]+", "");
            string uri = "https://image.tmdb.org" + path + query;

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

                try
                {
                    await httpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    ModInit.fileWatcher.Remove(md5key);
                    Log.Error(ex, "CatchId={CatchId}", "id_aspi1mjf");
                }
            }
            #endregion

            var headers = HeadersModel.Init(
                // используем старый ua что-бы гарантировать image/jpeg вместо image/webp
                ("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5"),
                ("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/534.57.2 (KHTML, like Gecko) Version/5.1.7 Safari/534.57.2"),
                ("Cache-Control", "max-age=0")
            );

            var semaphore = new SemaphorManager(outFile, ctsHttp.Token);

            try
            {
                if (semaphore != null)
                {
                    bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                    if (!_acquired)
                    {
                        httpContext.Response.Redirect(uri);
                        return;
                    }
                }

                if (ctsHttp.IsCancellationRequested)
                    return;

                #region cacheFiles
                if (ModInit.fileWatcher.TryGetValue(md5key, out _fileCache))
                {
                    httpContext.Response.Headers["X-Cache-Status"] = "HIT";

                    if (ModInit.conf.responseContentLength && _fileCache.Length > 0)
                        httpContext.Response.ContentLength = _fileCache.Length;

                    semaphore?.Release();
                    await httpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                    return;
                }
                #endregion

                var proxyManager = new ProxyManager("tmdb_img", ModInit.conf.proxyimg);

                var client = FriendlyHttp.MessageClient(
                    "proxyimg",
                    Http.Handler(uri, proxyManager.Get()),
                    out bool disposeHttpClient,
                    httpClient: http2ImgClient
                );

                var req = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Version = ModInit.conf.httpversion switch
                    {
                        2 => HttpVersion.Version20,
                        3 => HttpVersion.Version30,
                        _ => HttpVersion.Version11
                    }
                };

                foreach (var h in headers)
                {
                    if (!req.Headers.TryAddWithoutValidation(h.name, h.val))
                    {
                        if (req.Content?.Headers != null)
                            req.Content.Headers.TryAddWithoutValidation(h.name, h.val);
                    }
                }

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
                                    #region cache
                                    httpContext.Response.Headers["X-Cache-Status"] = "MISS";

                                    try
                                    {
                                        int cacheLength = 0;
                                        bool isFullyRead = false;

                                        ModInit.fileWatcher.EnsureDirectory(md5key);

                                        await using (var cacheStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: PoolInvk.bufferSize, options: FileOptions.Asynchronous))
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
                                                await httpContext.Response.Body.WriteAsync(wrm).ConfigureAwait(false);
                                            }
                                        }

                                        if (isFullyRead)
                                        {
                                            if (response.Content.Headers.ContentLength.HasValue)
                                            {
                                                if (response.Content.Headers.ContentLength.Value == cacheLength)
                                                {
                                                    ModInit.fileWatcher.Add(md5key, cacheLength);
                                                }
                                                else
                                                {
                                                    IO.File.Delete(outFile);
                                                }
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
                                    httpContext.Response.Headers["X-Cache-Status"] = "bypass";

                                    while ((bytesRead = await responseStream.ReadAsync(memBuf, ctsHttp.Token).ConfigureAwait(false)) > 0)
                                    {
                                        if (ctsHttp.IsCancellationRequested)
                                            break;

                                        await httpContext.Response.Body.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
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
                httpContext.Response.Redirect(uri);
            }
            finally
            {
                semaphore?.Release();
            }
        }
    }
    #endregion
}
