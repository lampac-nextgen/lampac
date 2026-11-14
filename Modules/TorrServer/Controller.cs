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
        // HttpClient drops the Authorization header on redirect, so TorrServer answers 401.
        // Redirects are passed to the client instead, see CopyProxyHttpResponse.
        AllowAutoRedirect = false,
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
            .Replace("{defaultPasswd}", ModInit.conf.defaultPasswd)
            .Replace("{accsdb}", CoreInit.conf.accsdb.enable ? "true" : "false");

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
    [HttpGet, HttpPost, HttpHead]
    [Route("ts/echo")]
    [Route("ts/gst/echo")]
    [Route("ts/site.webmanifest")]
    [Route("ts/msx/{suffix}")]
    [Route("ts/search/{suffix}")]
    [Route("ts/torznab/{suffix}")]
    [Route("ts/ffp/{suffix}")]
    public Task Bypass()
    {
        string path = HttpContext.Request.Path.Value;
        return path == "/ts/echo" || path == "/ts/gst/echo" || path == "/ts/site.webmanifest"
            ? TorAPI() : Index();
    }
    #endregion

    #region TorAPI
    [AllowAnonymous]
    [HttpGet, HttpPost, HttpHead, HttpOptions]
    [Route("ts/{*suffix}")]
    async public Task Index()
    {
        // Preflight carries no user data and never reaches TorrServer.
        if (HttpMethods.IsOptions(Request.Method))
        {
            Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!CoreInit.conf.accsdb.enable || requestInfo.IsLocalRequest)
        {
            await TorAPI().ConfigureAwait(false);
            return;
        }

        var user = MediaAccess.User(HttpContext);
        if (user == null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Basic realm=\"Authorization Required\"";
            return;
        }

        await TorAPI(user).ConfigureAwait(false);
    }

    [NonAction]
    async public Task TorAPI(AccsUser user = null)
    {
        string rawPath = HttpContext.Request.Path.Value ?? "";
        string pathRequest = Regex.Replace(Regex.Replace(rawPath, "^/ts", "", RegexOptions.IgnoreCase), "[^a-zA-Z0-9\\./]", "");

        foreach (string segment in pathRequest.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Trim('.').Length == 0)
            {
                HttpContext.Response.StatusCode = 404;
                return;
            }
        }

        string servUri = $"http://{CoreInit.conf.listen.localhost}:{ModInit.conf.tsport}{pathRequest}{HttpContext.Request.QueryString.Value}";
        string absPath = Regex.Replace(new Uri(servUri).AbsolutePath, "/{2,}", "/").TrimEnd('/', '.');
        if (absPath.Length == 0)
            absPath = "/";

        if (absPath.Equals("/shutdown", StringComparison.OrdinalIgnoreCase)
            || absPath.StartsWith("/shutdown/", StringComparison.OrdinalIgnoreCase))
        {
            HttpContext.Response.StatusCode = 404;
            return;
        }

        using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
        {
            ctsHttp.CancelAfter(TimeSpan.FromSeconds(5));

            #region settings
            if (absPath.StartsWith("/settings", StringComparison.OrdinalIgnoreCase))
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
                    else if (Shared.Services.Utilities.IPNetwork.IsLocalIp(requestInfo.IP, forced: true))
                    {
                        var data = new StringContent(requestJson, Encoding.UTF8, "application/json");
                        await httpClient.PostAsync("/settings", data, ctsHttp.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }

                    await HttpContext.Response.WriteAsync(string.Empty, ctsHttp.Token).ConfigureAwait(false);
                    return;
                }
            }
            #endregion

            #region playlist
            if (absPath.StartsWith("/stream/", StringComparison.OrdinalIgnoreCase) && HttpContext.Request.Query.ContainsKey("m3u"))
            {
                using var playlistResponse = await httpClient.GetAsync(servUri, ctsHttp.Token).ConfigureAwait(false);
                if (playlistResponse.IsSuccessStatusCode)
                {
                    string m3u = await playlistResponse.Content.ReadAsStringAsync(ctsHttp.Token).ConfigureAwait(false);
                    playlistResponse.Content = new StringContent(
                        MediaAccess.Playlist(m3u, playlistResponse.RequestMessage.RequestUri, new Uri(host + "/ts/"), user),
                        Encoding.UTF8, "audio/x-mpegurl");
                }
                await CopyProxyHttpResponse(HttpContext, playlistResponse, user).ConfigureAwait(false);
                return;
            }
            #endregion

            #region gst-playlist
            if (HttpContext.Request.Method == "GET"
                && absPath.StartsWith("/gst/", StringComparison.OrdinalIgnoreCase)
                && absPath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                using (var ctsGst = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
                {
                    ctsGst.CancelAfter(TimeSpan.FromSeconds(30));

                    try
                    {
                        var gstResponse = await httpClient.GetAsync(servUri, ctsGst.Token).ConfigureAwait(false);

                        if (gstResponse.IsSuccessStatusCode)
                        {
                            string body = await gstResponse.Content.ReadAsStringAsync(ctsGst.Token).ConfigureAwait(false);
                            gstResponse.Content = new StringContent(
                                MediaAccess.Playlist(body, gstResponse.RequestMessage.RequestUri, new Uri(host + "/ts/"), user),
                                Encoding.UTF8,
                                "application/vnd.apple.mpegurl"
                            );
                        }

                        await CopyProxyHttpResponse(HttpContext, gstResponse, user).ConfigureAwait(false);
                    }
                    catch (System.Exception ex)
                    {
                        Serilog.Log.Error(ex, "{Class} {CatchId}", "TorrServerController", "id_gst_playlist");
                        HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                    }
                }
                return;
            }
            #endregion
        }

        var request = CreateProxyHttpRequest(HttpContext, new Uri(servUri));

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted).ConfigureAwait(false);
        string mediaType = response.Content?.Headers.ContentType?.MediaType;
        if (HttpMethods.IsGet(Request.Method) && response.StatusCode == HttpStatusCode.OK
            && (mediaType == "application/vnd.apple.mpegurl" || mediaType == "application/x-mpegurl"
                || mediaType == "audio/mpegurl" || mediaType == "audio/x-mpegurl"))
        {
            string body = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted).ConfigureAwait(false);
            response.Content = new StringContent(
                MediaAccess.Playlist(body, response.RequestMessage.RequestUri, new Uri(host + "/ts/"), user),
                Encoding.UTF8, mediaType);
        }
        await CopyProxyHttpResponse(HttpContext, response, user).ConfigureAwait(false);
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

    async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage, AccsUser user = null)
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

        if (responseMessage.Headers.NonValidated.TryGetValues("Location", out var location))
        {
            string path = location.ToString();
            string media = MediaAccess.Url(path, responseMessage.RequestMessage.RequestUri, new Uri(host + "/ts/"), user);
            if (media != path)
                response.Headers.Location = media;
            else if (path.StartsWith('/') && !path.StartsWith("//") && !path.StartsWith("/ts/", StringComparison.OrdinalIgnoreCase))
                response.Headers.Location = "/ts" + path;
        }

        if (user != null)
            response.Headers.CacheControl = "private, no-store";

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
