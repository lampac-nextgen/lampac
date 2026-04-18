using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Shared.Services;
using Shared.Models.Proxy;
using Shared.Models.Templates;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace Shared.Models.Events
{
    public record EventLoadKit(BaseSettings defaultinit, BaseSettings init, JObject userconf, RequestModel requestInfo);

    public record EventMiddleware(bool first, HttpContext httpContext);

    public record EventBadInitialization(BaseSettings init, bool? rch, RequestModel requestInfo, string host, HttpRequest request, HttpContext httpContext);

    public record EventAppReplace(string source, string token, string arg, string host, RequestModel requestInfo, HttpRequest request);

    public record EventExternalids(string id, string imdb_id, string kinopoisk_id, int serial);

    public record EventHostStreamProxy(BaseSettings conf, string uri, List<HeadersModel> headers, WebProxy proxy, RequestModel requestInfo, HttpContext httpContext);

    public record EventHostImgProxy(RequestModel requestInfo, HttpContext httpContext, string uri, int height, List<HeadersModel> headers, string plugin);

    public record EventMyLocalIp(RequestModel requestInfo, HttpRequest request, HttpContext httpContext);

    public record EventControllerHttpHeaders(string site, Dictionary<string, string> headers, RequestModel requestInfo, HttpRequest request, HttpContext httpContext);

    public record EventStreamQuality(string link, string quality, bool prepend);

    public record EventStreamQualityFirts(IReadOnlyList<StreamQualityDto> data);

    public record EventOnlineApiQuality(string balanser, JObject kitconf);

    public record EventHttpHandler(string url, HttpClientHandler handler, WebProxy proxy, CookieContainer cookieContainer);

    public record EventHttpHeaders(string url, HttpRequestMessage client, string cookie, string referer, List<HeadersModel> headers, bool useDefaultHeaders);

    public record EventHttpResponse(string url, HttpClient client, HttpContent data, HttpResponseMessage response, string result);

    public record EventPlaywrightHttpResponse(string url, string method, int status, Dictionary<string, string> requestHeaders, Dictionary<string, string> responseHeaders, string result, string error);

    public record EventProxyApiCreateHttpRequest(ProxyLinkModel decryptLink, string plugin, HttpRequest request, List<HeadersModel> headers, Uri uri, HttpRequestMessage requestMessage);

    public record EventProxyApiCacheStream(HttpContext httpContext, ProxyLinkModel decryptLink);

    public record EventProxyApiOverride(HttpContext httpContext, RequestModel requestInfo, ProxyLinkModel decryptLink, HttpClientHandler proxyHandler);

    public record EventProxyImgMd5key(HttpContext httpContext, RequestModel requestInfo, ProxyLinkModel decryptLink, string href, int width, int height);

    public record EventStaticache(HttpContext httpContext, RequestModel requestInfo);

    public record EventRchRegistry(string connectionId, string ip, string host, RchClientInfo info, NwsConnection connection);

    public record EventRchDisconnected(string connectionId);

    public record EventNwsConnected(string connectionId, RequestModel requestInfo, NwsConnection connection, CancellationToken token);

    public record EventNwsDisconnected(string connectionId);

    public record EventNwsMessage(string connectionId, ReadOnlyMemory<byte> payload, string method, JsonElement args);
}
