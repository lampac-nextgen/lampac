using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
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

namespace TorrServer;

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
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("ts.js")]
    [Route("ts/js/{token}")]
    public ActionResult Plugin(string token)
    {
        string plugins = FileCache.ReadAllText($"{ModInit.modpath}/plugins.js", "ts.js")
            .Replace("{localhost}", host)
            .Replace("{token}", token ?? string.Empty)
            .Replace("{tshost}", Regex.Replace(host, "^https?://", ""))
            .Replace("{defaultPasswd}", ModInit.conf.defaultPasswd);

        return ContentTo(plugins, "application/javascript; charset=utf-8");
    }
    #endregion


    #region Main
    [HttpGet, AllowAnonymous]
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

    #region Bypass
    [AllowAnonymous]
    [HttpGet, HttpPost]
    [Route("ts/echo")]
    [Route("ts/gst/echo")]
    [Route("ts/site.webmanifest")]
    [Route("ts/msx/{suffix}")]
    [Route("ts/search/{suffix}")]
    [Route("ts/torznab/{suffix}")]
    [Route("ts/ffp/{suffix}")]
    public Task Bypass()
    {
        return TorAPI();
    }
    #endregion

    #region TorAPI
    [AllowAnonymous]
    [HttpGet, HttpPost]
    [Route("ts/{*suffix}")]
    async public Task Index()
    {
        string path = HttpContext.Request.Path.Value;

        if (path.StartsWith("/shutdown", StringComparison.OrdinalIgnoreCase))
        {
            HttpContext.Response.StatusCode = 404;
            return;
        }

        if (CoreInit.conf.accsdb.enable)
        {
            bool isStream = Regex.IsMatch(path, "^/ts/(stream|playlist|play/|download/|gst/)", RegexOptions.IgnoreCase);

            if (HttpContext.Request.Method == "GET" && isStream)
            {
                await TorAPI().ConfigureAwait(false);
            }
            else
            {
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
                            HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                            HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            HttpContext.Response.BodyWriter.Write("NoAccessGroup"u8);
                            return;
                        }

                        await TorAPI(user).ConfigureAwait(false);
                        return;
                    }
                }

                HttpContext.Response.StatusCode = 401;
                HttpContext.Response.Headers["Www-Authenticate"] = "Basic realm=Authorization Required";
            }
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
        string servUri = $"http://{CoreInit.conf.listen.localhost}:{ModInit.conf.tsport}{Regex.Replace(pathRequest, "[^a-zA-Z0-9\\./]", "") + HttpContext.Request.QueryString.Value}";

        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
        {
            ctsHttp.CancelAfter(TimeSpan.FromSeconds(5));

            #region settings
            if (pathRequest.StartsWith("/settings"))
            {
                if (HttpContext.Request.Method != "POST")
                {
                    HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, false, leaveOpen: true))
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
        "transfer-encoding",
        "etag",
        "connection",
        "content-security-policy",
        "content-disposition"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage)
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
