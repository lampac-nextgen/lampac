using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Events;
using Shared.Models.Proxy;
using Shared.Models.ServerProxy;
using Shared.Services;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares;

public partial class ProxyAPI
{
    #region Mpd
    async public Task ProxyMpd(HttpContext httpContext, ServerproxyConf init, ProxyLinkModel decryptLink, HttpResponseMessage response, string contentType, CancellationTokenSource ctsHttp)
    {
        using (HttpContent content = response.Content)
        {
            if (response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.PartialContent ||
                response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (response.Content?.Headers?.ContentLength > init.maxlength_m3u)
                {
                    httpContext.Response.StatusCode = 503;
                    httpContext.Response.ContentType = "text/plain";
                    await httpContext.Response.WriteAsync("bigfile", ctsHttp.Token).ConfigureAwait(false);
                    return;
                }

                string mpd = await content.ReadAsStringAsync(ctsHttp.Token).ConfigureAwait(false);
                if (mpd == null)
                {
                    httpContext.Response.StatusCode = 503;
                    await httpContext.Response.WriteAsync("error array mpd", ctsHttp.Token).ConfigureAwait(false);
                    return;
                }

                if (!mpd.Contains("<BaseURL>"))
                {
                    var uri = new Uri(decryptLink.uri);
                    var basePath = uri.GetLeftPart(UriPartial.Path);
                    basePath = basePath.Substring(0, basePath.LastIndexOf('/') + 1);

                    string enc = ProxyLink.Encrypt(basePath, decryptLink, forceMd5: true);
                    mpd = mpd.Replace("<Period>", $"<BaseURL>{CoreInit.Host(httpContext)}/proxy-dash/{enc}/</BaseURL>\n<Period>");
                }
                else
                {
                    mpd = Regex.Replace(mpd, "<BaseURL>([^<]+)</BaseURL>", m =>
                    {
                        string enc = ProxyLink.Encrypt(m.Groups[1].Value, decryptLink, forceMd5: true);
                        return $"<BaseURL>{CoreInit.Host(httpContext)}/proxy-dash/{enc}/</BaseURL>";
                    });
                }

                int contentLength = Encoding.UTF8.GetByteCount(mpd);

                httpContext.Response.ContentType = contentType ?? "application/dash+xml";
                httpContext.Response.StatusCode = (int)response.StatusCode;

                if (response.Headers.AcceptRanges != null)
                    httpContext.Response.Headers["accept-ranges"] = "bytes";

                if (httpContext.Response.StatusCode is 206 or 416)
                {
                    var contentRange = response.Content.Headers.ContentRange;
                    if (contentRange != null)
                    {
                        httpContext.Response.Headers["content-range"] = contentRange.ToString();
                    }
                    else
                    {
                        if (httpContext.Response.StatusCode == 206)
                            httpContext.Response.Headers["content-range"] = $"bytes 0-{contentLength - 1}/{contentLength}";

                        if (httpContext.Response.StatusCode == 416)
                            httpContext.Response.Headers["content-range"] = $"bytes */{contentLength}";
                    }
                }
                else
                {
                    if (init.responseContentLength && !CoreInit.CompressionMimeTypes.Contains(httpContext.Response.ContentType))
                        httpContext.Response.ContentLength = contentLength;
                }

                await httpContext.Response.WriteAsync(mpd, ctsHttp.Token).ConfigureAwait(false);
            }
            else
            {
                // проксируем ошибку
                await CopyProxyHttpResponse(httpContext, response, null, ctsHttp.Token).ConfigureAwait(false);
            }
        }
    }
    #endregion

    #region Dash
    async public Task ProxyDash(HttpContext httpContext, ServerproxyConf init, ProxyLinkModel decryptLink, string servUri, string servPath, HttpClientHandler proxyHandler, (string uriKey, string contentType) cacheStream)
    {
        var uri = new Uri($"{servUri}{Regex.Replace(servPath, "^[^/]+/", "", RegexOptions.IgnoreCase)}{httpContext.Request.QueryString.Value}");

        if (init.showOrigUri)
            httpContext.Response.Headers["PX-Orig"] = uri.ToString();

        var client = FriendlyHttp.MessageClient("proxy", proxyHandler ?? baseHandler);

        using (var request = CreateProxyHttpRequest(decryptLink.plugin, httpContext, decryptLink.headers, uri))
        {
            if (EventListener.ProxyApiCreateHttpRequest != null)
            {
                var em = new EventProxyApiCreateHttpRequest(decryptLink, decryptLink.plugin, httpContext.Request, decryptLink.headers, uri, request);
                await InvokeProxyApiCreateHttpRequestHandlers(em).ConfigureAwait(false);
            }

            if (init.showOrigUri)
                httpContext.Response.Headers["PX-Req"] = request.RequestUri.ToString();

            using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
            {
                ctsHttp.CancelAfter(TimeSpan.FromSeconds(30));

                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctsHttp.Token).ConfigureAwait(false))
                {
                    httpContext.Response.Headers["PX-Cache"] = "BYPASS";
                    await CopyProxyHttpResponse(httpContext, response, cacheStream.uriKey, ctsHttp.Token).ConfigureAwait(false);
                }
            }
        }
    }
    #endregion
}
