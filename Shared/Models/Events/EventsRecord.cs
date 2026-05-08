using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Shared.Models.Proxy;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Models.Templates;
using Shared.Services;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace Shared.Models.Events;

public record EventLoadKit(BaseSettings defaultinit, BaseSettings init, JObject userconf, RequestModel requestInfo);

public record EventAccsdb(HttpContext httpContext, RequestModel requestInfo);

public record EventMiddleware(bool first, HttpContext httpContext);

public record EventBadInitialization(BaseSettings init, bool? rch, RequestModel requestInfo, string host, HttpRequest request, HttpContext httpContext);

public record EventSisiChannels(BaseController controller, HttpContext httpContext, IList<ChannelItem> channels);

public record EventSisiPlaylistResult(BaseController controller, BaseSettings init, HttpContext httpContext, IList<PlaylistItem> playlists, bool singleCache, IList<MenuItem> menu, int total_pages, List<HeadersModel> headers_stream, List<HeadersModel> headers_image);

public record EventSisiOnResult(BaseController controller, BaseSettings init, HttpContext httpContext, StreamItem stream_links, List<HeadersModel> headers_stream, List<HeadersModel> headers_image);

public record EventSisiBookmarks(BaseController controller, HttpContext httpContext, IList<MenuItem> menu, IList<PlaylistItem> bookmarks, int pg, int pageSize, int total_pages);

public record EventSisiHistorys(BaseController controller, HttpContext httpContext, IList<PlaylistItem> historys, int pg, int pageSize);

public record EventCatalogChannels(BaseController controller, JObject channels, HttpContext httpContext);

public record EventCatalogList(BaseController controller, HttpContext httpContext, List<Catalog.PlaylistItem> playlists, string query, string plugin, string cat, string sort, int page, int? total_pages, bool? next_page);

public record EventCatalogCard(BaseController controller, HttpContext httpContext, JObject card, string plugin, string uri, string type);

public record EventAppReplace(string source, string token, string arg, string host, RequestModel requestInfo, HttpRequest request);

public record EventExternalids(string id, string imdb_id, string kinopoisk_id, int serial);

public record EventHostStreamProxy(BaseSettings conf, string uri, List<HeadersModel> headers, WebProxy proxy, RequestModel requestInfo, HttpContext httpContext);

public record EventHostImgProxy(RequestModel requestInfo, HttpContext httpContext, string uri, int height, List<HeadersModel> headers, string plugin);

public record EventMyLocalIp(RequestModel requestInfo, HttpRequest request, HttpContext httpContext);

public record EventControllerHttpHeaders(string site, Dictionary<string, string> headers, RequestModel requestInfo, HttpRequest request, HttpContext httpContext);

public record EventStreamQuality(string link, string quality, bool prepend);

public record EventStreamQualityFirts(IReadOnlyList<StreamQualityDto> data);

public record EventVideoTpl(VideoDto video, HttpContext httpContext);

public record EventOnline(BaseController controller, List<(string name, string url, string plugin, int index)> online, Shared.Models.Module.OnlineEventsModel moduleArgs, JObject kitconf, HttpContext httpContext);

public record EventOnlineTpl(BaseController controller, BaseSettings init, HttpContext httpContext, bool rjson, ITplResult tpl);

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
