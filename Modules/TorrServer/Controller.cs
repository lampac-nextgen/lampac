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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace TorrServer
{
    public class TorrServerController : BaseController
    {
        #region HttpClient
        private static readonly HttpClient httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
            MaxConnectionsPerServer = 100
        })
        {
            BaseAddress = new Uri($"http://{CoreInit.conf.listen.localhost}:{ModInit.conf.tsport}"),
            DefaultRequestHeaders =
            {
                Authorization = new AuthenticationHeaderValue("Basic", CrypTo.Base64($"ts:{ModInit.tspass}")),
            },
            Timeout = TimeSpan.FromSeconds(30)
        };
        #endregion

        #region ts.js
        [HttpGet]
        [AllowAnonymous]
        [Route("ts.js")]
        [Route("ts/js/{token}")]
        public ActionResult Plugin(string token)
        {
            SetHeadersNoCache();

            string plugins = FileCache.ReadAllText($"{ModInit.modpath}/plugins.js", "ts.js")
                .Replace("{localhost}", Regex.Replace(host, "^https?://", ""));

            if (!string.IsNullOrEmpty(token))
                plugins = Regex.Replace(plugins, "Lampa.Storage.set\\('torrserver_login'[^\n\r]+", $"Lampa.Storage.set('torrserver_login','{HttpUtility.UrlEncode(token)}');");

            return Content(plugins, "application/javascript; charset=utf-8");
        }
        #endregion


        #region Main
        [HttpGet]
        [AllowAnonymous]
        [Route("ts")]
        [Route("ts/static/js/{suffix}")]
        async public Task<ActionResult> Main()
        {
            string html = null;
            string pathRequest = Regex.Replace(HttpContext.Request.Path.Value, "^/ts", "");

            try
            {
                using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
                {
                    ctsHttp.CancelAfter(TimeSpan.FromSeconds(5));

                    var responseMessage = await httpClient.GetAsync(pathRequest + HttpContext.Request.QueryString.Value, ctsHttp.Token).ConfigureAwait(false);
                    html = await responseMessage.Content.ReadAsStringAsync(ctsHttp.Token).ConfigureAwait(false);
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "TorrServerController", "id_gqd64vu0");
            }

            if (html == null)
                return StatusCode(500);

            if (pathRequest.Contains(".js"))
            {
                string key = Regex.Match(html, "\\.concat\\(([^,]+),\"/echo\"").Groups[1].Value;
                html = html.Replace($".concat({key},\"/", $".concat({key},\"/ts/");
                return Content(html, "application/javascript; charset=utf-8");
            }
            else
            {
                html = html.Replace("href=\"/", "href=\"/ts/").Replace("src=\"/", "src=\"/ts/");
                html = html.Replace("src=\"./", "src=\"/ts/");
                return Content(html, "text/html; charset=utf-8");
            }
        }
        #endregion

        #region TorAPI
        [HttpGet]
        [HttpPost]
        [AllowAnonymous]
        [Route("ts/{*suffix}")]
        async public Task Index()
        {
            if (HttpContext.Request.Path.Value.StartsWith("/shutdown"))
            {
                HttpContext.Response.StatusCode = 404;
                return;
            }

            if (CoreInit.conf.accsdb.enable)
            {
                #region Обработка stream потока
                if (HttpContext.Request.Method == "GET" && Regex.IsMatch(HttpContext.Request.Path.Value, "^/ts/(stream|play)"))
                {
                    await TorAPI().ConfigureAwait(false);
                    return;

                    //if (ModInit.clientIps.Contains(HttpContext.Connection.RemoteIpAddress.ToString()))
                    //{
                    //    await TorAPI();
                    //    return;
                    //}
                    //else
                    //{
                    //    HttpContext.Response.StatusCode = 404;
                    //    return;
                    //}
                }
                #endregion

                #region Access-Control-Request-Headers
                if (HttpContext.Request.Method == "OPTIONS" && HttpContext.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var AccessControl) && AccessControl == "authorization")
                {
                    HttpContext.Response.StatusCode = 204;
                    return;
                }
                #endregion

                if (HttpContext.Request.Headers.TryGetValue("Authorization", out var Authorization))
                {
                    byte[] data = Convert.FromBase64String(Authorization.ToString().Replace("Basic ", ""));
                    string[] decodedString = Encoding.UTF8.GetString(data).Split(":");

                    string login = decodedString[0].ToLowerAndTrim();
                    string passwd = decodedString[1];

                    if (CoreInit.conf.accsdb.findUser(login) is AccsUser user && !user.ban && user.expires > DateTime.UtcNow && passwd == ModInit.conf.defaultPasswd)
                    {
                        if (ModInit.conf.group > user.group)
                        {
                            await HttpContext.Response.WriteAsync("NoAccessGroup", HttpContext.RequestAborted).ConfigureAwait(false);
                            return;
                        }

                        await TorAPI(user).ConfigureAwait(false);
                        return;
                    }
                }

                if (HttpContext.Request.Path.Value.StartsWith("/ts/echo"))
                {
                    await HttpContext.Response.WriteAsync("MatriX.API", HttpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }

                HttpContext.Response.StatusCode = 401;
                HttpContext.Response.Headers["Www-Authenticate"] = "Basic realm=Authorization Required";
                return;
            }
            else
            {
                await TorAPI().ConfigureAwait(false);
                return;
            }
        }

        async public Task TorAPI(AccsUser user = null)
        {
            string pathRequest = Regex.Replace(HttpContext.Request.Path.Value, "^/ts", "");
            string servUri = $"http://{CoreInit.conf.listen.localhost}:{ModInit.conf.tsport}{pathRequest + HttpContext.Request.QueryString.Value}";

            using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
            {
                ctsHttp.CancelAfter(TimeSpan.FromSeconds(5));

                #region settings
                if (pathRequest.StartsWith("/settings"))
                {
                    if (HttpContext.Request.Method != "POST")
                    {
                        HttpContext.Response.StatusCode = 404;
                        await HttpContext.Response.WriteAsync("404 page not found", HttpContext.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, false, PoolInvk.bufferSize, leaveOpen: true))
                    {
                        string requestJson = await reader.ReadToEndAsync(ctsHttp.Token).ConfigureAwait(false);

                        if (requestJson.Contains("\"get\""))
                        {
                            var data = new StringContent("{\"action\":\"get\"}", Encoding.UTF8, "application/json");
                            var rs = await httpClient.PostAsync("/settings", data, ctsHttp.Token).ConfigureAwait(false);
                            await rs.Content.CopyToAsync(HttpContext.Response.Body, HttpContext.RequestAborted).ConfigureAwait(false);
                            return;
                        }
                        else if (!ModInit.conf.rdb || requestInfo.IP == "127.0.0.1" || requestInfo.IP.StartsWith("192.168."))
                        {
                            var data = new StringContent(requestJson, Encoding.UTF8, "application/json");
                            await httpClient.PostAsync("/settings", data, ctsHttp.Token).ConfigureAwait(false);
                        }

                        await HttpContext.Response.WriteAsync(string.Empty, ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }
                }
                #endregion

                #region playlist
                if (pathRequest.StartsWith("/stream/") && HttpContext.Request.QueryString.Value.Contains("&m3u"))
                {
                    string m3u = await httpClient.GetStringAsync(servUri, ctsHttp.Token).ConfigureAwait(false);
                    HttpContext.Response.ContentType = "audio/x-mpegurl; charset=utf-8";
                    await HttpContext.Response.WriteAsync((m3u ?? string.Empty).Replace("/stream/", "/ts/stream/"), ctsHttp.Token).ConfigureAwait(false);
                    return;
                }
                #endregion
            }

            var request = CreateProxyHttpRequest(HttpContext, new Uri(servUri));

            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted).ConfigureAwait(false);
            await CopyProxyHttpResponse(HttpContext, response).ConfigureAwait(false);
        }
        #endregion


        #region CreateProxyHttpRequest
        HttpRequestMessage CreateProxyHttpRequest(HttpContext context, Uri uri)
        {
            var request = context.Request;

            var requestMessage = new HttpRequestMessage();
            var requestMethod = request.Method;
            if (HttpMethods.IsPost(requestMethod))
            {
                var streamContent = new StreamContent(request.Body);
                requestMessage.Content = streamContent;
            }

            foreach (var header in request.Headers)
            {
                if (header.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            requestMessage.Headers.Host = string.IsNullOrEmpty(CoreInit.conf.listen.host) ? context.Request.Host.Value : CoreInit.conf.listen.host;
            requestMessage.RequestUri = uri;
            requestMessage.Method = new HttpMethod(request.Method);

            return requestMessage;
        }
        #endregion

        #region CopyProxyHttpResponse
        async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage)
        {
            var response = context.Response;
            response.StatusCode = (int)responseMessage.StatusCode;

            #region UpdateHeaders
            void UpdateHeaders(HttpHeaders headers)
            {
                foreach (var header in headers)
                {
                    if (header.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("etag", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("connection", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("content-security-policy", StringComparison.OrdinalIgnoreCase) ||
                        header.Key.Equals("content-disposition", StringComparison.OrdinalIgnoreCase))
                        continue;

                    response.Headers[header.Key] = header.Value.ToArray();
                }
            }
            #endregion

            UpdateHeaders(responseMessage.Headers);
            UpdateHeaders(responseMessage.Content.Headers);

            await using (var responseStream = await responseMessage.Content.ReadAsStreamAsync(context.RequestAborted).ConfigureAwait(false))
            {
                using (var nbuf = new BufferPool())
                {
                    int bytesRead;
                    var memBuf = nbuf.Memory;

                    while ((bytesRead = await responseStream.ReadAsync(memBuf, context.RequestAborted).ConfigureAwait(false)) > 0)
                    {
                        if (context.RequestAborted.IsCancellationRequested)
                            break;

                        await response.Body.WriteAsync(memBuf.Slice(0, bytesRead), context.RequestAborted).ConfigureAwait(false);
                    }
                }
            }
        }
        #endregion
    }
}
