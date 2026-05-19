using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares;

public partial class ProxyAPI
{
    #region static
    static CacheFileWatcher fileWatcher;

    public static int Stat_ContCacheFiles
        => fileWatcher.FilesCount;

    public static void Initialization()
    {
        CacheFileWatcher.Configure("hls", CoreInit.conf.serverproxy.cache_hls);
        fileWatcher = new CacheFileWatcher("hls");
        EventListener.UpdateInitFile += cacheDefaultRequestHeaders.Clear;
    }
    #endregion

    public ProxyAPI(RequestDelegate next)
    {
    }

    async public Task InvokeAsync(HttpContext httpContext)
    {
        #region decryptLink
        var init = CoreInit.conf.serverproxy;
        if (!init.enable)
        {
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            httpContext.Response.BodyWriter.Write("decryptLink"u8);
            await httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted).ConfigureAwait(false);
            return;
        }

        var requestInfo = httpContext.Features.Get<RequestModel>();
        bool Isdash = httpContext.Request.Path.Value.StartsWith("/proxy-dash/", StringComparison.OrdinalIgnoreCase);

        string servPath = Isdash
            ? httpContext.Request.Path.Value.Replace("/proxy-dash/", "", StringComparison.OrdinalIgnoreCase)
            : httpContext.Request.Path.Value.Replace("/proxy/", "", StringComparison.OrdinalIgnoreCase);

        var decryptLink = Isdash
            ? ProxyLink.Decrypt(servPath.AsSpan(0, servPath.IndexOf('/')), requestInfo.IP)
            : ProxyLink.Decrypt(servPath.AsSpan(), requestInfo.IP);

        string servUri = decryptLink?.uri;

        if (string.IsNullOrEmpty(servUri) || !servUri.StartsWith("http"))
        {
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            httpContext.Response.BodyWriter.Write("servUri empty"u8);
            await httpContext.Response.BodyWriter.FlushAsync(httpContext.RequestAborted).ConfigureAwait(false);
            return;
        }
        #endregion

        if (init.showOrigUri)
            httpContext.Response.Headers["PX-Orig"] = servUri;

        #region proxyHandler
        HttpClientHandler proxyHandler = null;

        if (decryptLink.proxy != null)
        {
            proxyHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = Http.AlwaysAllowCertificate,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false,
                UseProxy = true,
                Proxy = decryptLink.proxy
            };
        }
        #endregion

        #region cacheFiles
        (string uriKey, string contentType) cacheStream = default;

        if (EventListener.ProxyApiCacheStream != null)
        {
            var em = new EventProxyApiCacheStream(httpContext, decryptLink);
            foreach (Func<EventProxyApiCacheStream, (string uriKey, string contentType)> handler in EventListener.ProxyApiCacheStream.GetInvocationList())
            {
                var res = handler(em);
                if (!string.IsNullOrWhiteSpace(res.uriKey))
                    cacheStream = res;
            }
        }

        if (cacheStream.uriKey != null && init.showOrigUri)
            httpContext.Response.Headers["PX-CacheStream"] = cacheStream.uriKey;

        if (cacheStream.uriKey != null)
        {
            string md5key = CrypTo.md5(cacheStream.uriKey);

            if (fileWatcher.TryGetValue(md5key, out var _fileCache))
            {
                using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
                {
                    if (ctsHttp.IsCancellationRequested)
                        return;

                    ctsHttp.CancelAfter(TimeSpan.FromSeconds(30));

                    httpContext.Response.Headers["PX-Cache"] = "HIT";
                    httpContext.Response.Headers["accept-ranges"] = "bytes";
                    httpContext.Response.ContentType = cacheStream.contentType ?? "application/octet-stream";

                    long cacheLength = _fileCache.Length;
                    string cachePath = _fileCache.FullPath;

                    if (RangeHeaderValue.TryParse(httpContext.Request.Headers["Range"], out var range))
                    {
                        RangeItemHeaderValue rangeItem = null;
                        foreach (var r in range.Ranges)
                        {
                            rangeItem = r;
                            break;
                        }

                        if (rangeItem != null)
                        {
                            long start = rangeItem.From ?? 0;
                            long end = rangeItem.To ?? (cacheLength - 1);

                            if (start >= cacheLength)
                            {
                                httpContext.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                                httpContext.Response.Headers["content-range"] = $"bytes */{cacheLength}";
                                return;
                            }

                            if (end >= cacheLength)
                                end = cacheLength - 1;

                            long length = end - start + 1;

                            httpContext.Response.StatusCode = StatusCodes.Status206PartialContent;
                            httpContext.Response.Headers["content-range"] = $"bytes {start}-{end}/{cacheLength}";

                            if (init.responseContentLength)
                                httpContext.Response.ContentLength = length;

                            await httpContext.Response.SendFileAsync(cachePath, start, length, ctsHttp.Token).ConfigureAwait(false);
                            return;
                        }
                    }

                    if (init.responseContentLength)
                        httpContext.Response.ContentLength = cacheLength;

                    await httpContext.Response.SendFileAsync(cachePath, ctsHttp.Token).ConfigureAwait(false);
                    return;
                }
            }
        }
        #endregion

        try
        {
            if (EventListener.ProxyApiOverride != null)
            {
                var ev = new EventProxyApiOverride(httpContext, requestInfo, decryptLink, proxyHandler);

                foreach (Func<EventProxyApiOverride, Task<bool>> handler in EventListener.ProxyApiOverride.GetInvocationList())
                {
                    bool next = await handler(ev);
                    if (!next)
                        return;
                }
            }

            if (Isdash)
            {
                await ProxyDash(httpContext, init, decryptLink, servUri, servPath, proxyHandler, cacheStream);
            }
            else
            {
                #region Video OR
                if (servUri.Contains(" or "))
                {
                    string[] links = servUri.Split(" or ");
                    servUri = links[0].Trim();

                    try
                    {
                        var hdlr = new HttpClientHandler
                        {
                            AllowAutoRedirect = true,
                            ServerCertificateCustomValidationCallback = Http.AlwaysAllowCertificate
                        };

                        if (decryptLink.proxy != null)
                        {
                            hdlr.UseProxy = true;
                            hdlr.Proxy = decryptLink.proxy;
                        }
                        else
                        {
                            hdlr.UseProxy = false;
                        }

                        var clientor = FriendlyHttp.MessageClient(
                            "base",
                            hdlr,
                            out bool disposeHttpClientor
                        );

                        try
                        {
                            var reqUri = new Uri(servUri);

                            using (var requestor = CreateProxyHttpRequest(decryptLink.plugin, httpContext, decryptLink.headers, reqUri))
                            {
                                if (EventListener.ProxyApiCreateHttpRequest != null)
                                {
                                    var em = new EventProxyApiCreateHttpRequest(decryptLink, decryptLink.plugin, httpContext.Request, decryptLink.headers, reqUri, requestor);
                                    await InvokeProxyApiCreateHttpRequestHandlers(em).ConfigureAwait(false);
                                }

                                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7)))
                                {
                                    using (var response = await clientor.SendAsync(requestor, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                                    {
                                        if ((int)response.StatusCode is 200 or 206) { }
                                        else
                                            servUri = links[1].Trim();
                                    }
                                }
                            }
                        }
                        finally
                        {
                            if (disposeHttpClientor)
                                clientor.Dispose();
                        }
                    }
                    catch
                    {
                        servUri = links[1].Trim();
                    }

                    decryptLink.uri = servUri;

                    if (init.showOrigUri)
                        httpContext.Response.Headers["PX-Set-Orig"] = decryptLink.uri;
                }
                #endregion

                var client = FriendlyHttp.MessageClient(
                    "proxy",
                    proxyHandler ?? baseHandler,
                    out bool disposeHttpClient
                );

                try
                {
                    var reqUri = new Uri(servUri);

                    using (var request = CreateProxyHttpRequest(decryptLink.plugin, httpContext, decryptLink.headers, reqUri))
                    {
                        if (EventListener.ProxyApiCreateHttpRequest != null)
                        {
                            var em = new EventProxyApiCreateHttpRequest(decryptLink, decryptLink.plugin, httpContext.Request, decryptLink.headers, reqUri, request);
                            await InvokeProxyApiCreateHttpRequestHandlers(em).ConfigureAwait(false);
                        }

                        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
                        {
                            if (ctsHttp.IsCancellationRequested)
                                return;

                            ctsHttp.CancelAfter(TimeSpan.FromSeconds(30));

                            if (init.showOrigUri)
                                httpContext.Response.Headers["PX-Req"] = request.RequestUri.ToString();

                            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctsHttp.Token).ConfigureAwait(false))
                            {
                                if (ctsHttp.IsCancellationRequested)
                                    return;

                                if ((int)response.StatusCode is 301 or 302 or 303 or 0 || response.Headers.Location != null)
                                {
                                    httpContext.Response.Redirect(
                                        ProxyLink.Encrypt(
                                            response.Headers.Location.AbsoluteUri,
                                            decryptLink,
                                            prefix: [CoreInit.Host(httpContext), "/proxy/"]
                                        )
                                    );

                                    return;
                                }

                                string contentType = null;
                                if (response.Content?.Headers != null && response.Content.Headers.TryGetValues("Content-Type", out IEnumerable<string> _contentType))
                                    contentType = _contentType?.FirstOrDefault();

                                bool ists =
                                    servPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                                    servPath.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase);

                                bool ism3u = servPath.Contains(".m3u", StringComparison.OrdinalIgnoreCase);

                                if (!ism3u)
                                {
                                    if (contentType != null)
                                    {
                                        ism3u =
                                            contentType.StartsWith("application/x-mpegurl", StringComparison.OrdinalIgnoreCase) ||
                                            contentType.StartsWith("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
                                            contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase);
                                    }
                                }

                                if (!ists && ism3u)
                                {
                                    await ProxyM3u8(httpContext, init, decryptLink, response, contentType, ctsHttp);
                                }
                                else if (servPath.Contains(".mpd", StringComparison.OrdinalIgnoreCase) || contentType?.StartsWith("application/dash+xml") == true)
                                {
                                    await ProxyMpd(httpContext, init, decryptLink, response, contentType, ctsHttp);
                                }
                                else
                                {
                                    httpContext.Response.Headers["PX-Cache"] = cacheStream.uriKey != null ? "MISS" : "BYPASS";
                                    await CopyProxyHttpResponse(httpContext, response, cacheStream.uriKey, ctsHttp.Token).ConfigureAwait(false);
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
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (CoreInit.conf.serilog)
                Serilog.Log.Error(ex, "CatchId={CatchId}", "id_1wmuzgfc");
        }
    }
}
