using Microsoft.AspNetCore.Http;
using Shared.Models.Events;
using Shared.Models.Proxy;
using Shared.Models.ServerProxy;
using Shared.Services;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares
{
    public partial class ProxyAPI
    {
        async public Task ProxyDash(HttpContext httpContext, ServerproxyConf init, ProxyLinkModel decryptLink, string servUri, string servPath, HttpClientHandler proxyHandler, (string uriKey, string contentType) cacheStream)
        {
            var uri = new Uri($"{servUri}{Regex.Replace(servPath, "^/[^/]+/[^/]+/", "", RegexOptions.IgnoreCase)}{httpContext.Request.QueryString.Value}");

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
    }
}
