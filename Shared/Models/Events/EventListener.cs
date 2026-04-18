using Microsoft.AspNetCore.Mvc;
using Shared.Models.Templates;

namespace Shared.Models.Events
{
    public class EventListener
    {
        public static Action<EventLoadKit> LoadKitInit;

        public static Action<EventLoadKit> LoadKit;

        public static Action UpdateInitFile;

        public static Action UpdateCurrentConf;

        public static Func<EventProxyApiCreateHttpRequest, Task> ProxyApiCreateHttpRequest;

        public static Func<EventProxyApiCacheStream, (string uriKey, string contentType)> ProxyApiCacheStream;

        public static Func<EventProxyApiOverride, Task<bool>> ProxyApiOverride;

        public static Func<EventProxyImgMd5key, string> ProxyImgMd5key;

        public static Func<EventStaticache, bool> Staticache;

        public static Func<EventBadInitialization, Task<ActionResult>> BadInitialization;

        public static Func<EventHostStreamProxy, string> HostStreamProxy;

        public static Func<EventHostImgProxy, string> HostImgProxy;

        public static Func<EventMyLocalIp, Task<string>> MyLocalIp;

        public static Func<EventControllerHttpHeaders, Dictionary<string, string>> HttpHeaders;

        public static Func<bool, EventMiddleware, Task<bool>> Middleware;

        public static Func<string, EventAppReplace, string> AppReplace;

        public static Action<EventHttpHandler> HttpHandler;

        public static Action<EventHttpHeaders> HttpRequestHeaders;

        public static Func<EventHttpResponse, Task> HttpResponse;

        public static Func<EventPlaywrightHttpResponse, Task> PlaywrightHttpResponse;

        public static Func<EventExternalids, (string imdb_id, string kinopoisk_id)> Externalids;

        public static Func<EventStreamQuality, (bool? next, string link)> StreamQuality;

        public static Func<EventStreamQualityFirts, StreamQualityDto?> StreamQualityFirts;

        public static Func<EventOnlineApiQuality, string> OnlineApiQuality;


        public static Action<EventRchRegistry> RchRegistry;

        public static Action<EventRchDisconnected> RchDisconnected;


        public static Action<EventNwsConnected> NwsConnected;

        public static Action<EventNwsMessage> NwsMessage;

        public static Action<EventNwsDisconnected> NwsDisconnected;
    }
}
