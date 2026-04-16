using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Shared.Models;
using Shared.Models.Events;
using Shared.Models.SISI;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System.Buffers;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shared
{
    public class BaseSisiController : BaseSisiController<SisiSettings>
    {
        public BaseSisiController(SisiSettings init) : base(init) { }
    }

    public class BaseSisiController<T> : BaseController where T : BaseSettings, ICloneable
    {
        #region static
        static readonly EmptyResult _emptyResult = new();
        static readonly List<MenuItem> emptyMenu = new();
        public static readonly SisiJsonContext jsonContext = SisiJsonContext.Default;

        static readonly JsonWriterOptions jsonWriterOptions = new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true
        };
        #endregion

        #region RchClient
        RchClient _rch = null;

        public RchClient rch
        {
            get
            {
                if (_rch == null && init != null && CoreInit.conf.rch.enable)
                {
                    if (init.rhub || init.rchstreamproxy != null || CoreInit.conf.rch.requiredConnected)
                        _rch = new RchClient(HttpContext, host, init, requestInfo);
                }

                return _rch;
            }
        }
        #endregion

        #region HttpHydra
        HttpHydra _httpHydra = null;

        public HttpHydra httpHydra
        {
            get
            {
                if (_httpHydra == null && init != null)
                    _httpHydra = new HttpHydra(init, httpHeaders(init), requestInfo, rch, proxy);

                return _httpHydra;
            }
        }
        #endregion

        #region proxyManager
        ProxyManager _proxyManager = null;

        public ProxyManager proxyManager
        {
            get
            {
                if (_proxyManager == null && init != null && (init.useproxy || init.useproxystream))
                    _proxyManager = new ProxyManager(init, rch);

                return _proxyManager;
            }
        }
        #endregion

        #region proxy
        WebProxy _proxy = null;

        public WebProxy proxy
        {
            get
            {
                if (_proxy == null)
                    _proxy = proxyManager?.Get();

                return _proxy;
            }
        }
        #endregion

        #region proxy_data
        public (string ip, string username, string password) _proxy_data = default;

        public (string ip, string username, string password) proxy_data
        {
            get
            {
                if (_proxy_data == default && proxyManager != null)
                    _proxy_data = proxyManager.BaseGet().data;

                return _proxy_data;
            }
        }
        #endregion

        public T init { get; private set; }

        BaseSettings baseconf { get; set; }

        public Func<JObject, T, T, T> loadKitInitialization { get; set; }

        public Action requestInitialization { get; set; }

        public Func<ValueTask> requestInitializationAsync { get; set; }

        public BaseSisiController(T init)
        {
            if (init != default)
                Initialization(init);
        }

        public void Initialization(T init)
        {
            if (baseconf != default)
                return;

            baseconf = init;
            this.init = (T)init.Clone();
            this.init.IsCloneable = true;
        }


        #region IsRequestBlocked
        public ValueTask<bool> IsRequestBlocked(T init, bool? rch = null, int? rch_keepalive = null, bool rch_check = true)
        {
            Initialization(init);
            return IsRequestBlocked(rch, rch_keepalive, rch_check);
        }

        async public ValueTask<bool> IsRequestBlocked(bool? rch = null, int? rch_keepalive = null, bool rch_check = true)
        {
            if (IsLoadKit(init))
            {
                if (loadKitInitialization != null)
                    init = loadKit(init, loadKitInitialization);
                else
                    init = loadKit(init);
            }

            requestInitialization?.Invoke();

            if (requestInitializationAsync != null)
                await requestInitializationAsync.Invoke();

            if (EventListener.BadInitialization != null)
            {
                var em = new EventBadInitialization(init, rch, requestInfo, host, HttpContext.Request, HttpContext);

                foreach (Func<EventBadInitialization, Task<ActionResult>> handler in EventListener.BadInitialization.GetInvocationList())
                {
                    badInitMsg = await handler(em);
                    if (badInitMsg != null)
                        return true;
                }
            }

            if (NoAccessGroup(init, out string error_msg))
            {
                badInitMsg = OnError(error_msg, rcache: false, statusCode: 401);
                return true;
            }

            if (IsOverridehost(init))
            {
                var overridehost = await InvokeOverridehost(init);
                if (overridehost != null)
                {
                    badInitMsg = overridehost;
                    return true;
                }
            }

            if (!init.enable || init.rip)
            {
                badInitMsg = OnError("disable", rcache: false, statusCode: 403);
                return true;
            }

            if (rch != null)
            {
                if ((bool)rch)
                {
                    if (init.rhub && !CoreInit.conf.rch.enable)
                    {
                        badInitMsg = OnError(RchClient.ErrorMsg);
                        return true;
                    }

                    if (rch_check && this.rch != null)
                    {
                        if (this.rch.IsNotConnected())
                        {
                            badInitMsg = ContentTo(this.rch.connectionMsg);
                            return true;
                        }

                        if (this.rch.IsNotSupport(out string rch_error))
                        {
                            badInitMsg = OnError(rch_error);
                            return true;
                        }
                    }
                }
                else
                {
                    if (init.rhub)
                    {
                        badInitMsg = OnError(RchClient.ErrorMsg);
                        return true;
                    }
                }
            }

            if (rch_check && this.rch != null && this.rch.IsRequiredConnected())
            {
                badInitMsg = ContentTo(this.rch.connectionMsg);
                return true;
            }

            return IsCacheError(init, this.rch);
        }
        #endregion

        #region OnError
        public ActionResult OnError(int statusCode = 503, bool refresh_proxy = false)
            => OnError(string.Empty, statusCode, refresh_proxy);

        public ActionResult OnError(string msg, int statusCode, bool refresh_proxy = false)
            => OnError(msg, true, refresh_proxy, statusCode);

        public ActionResult OnError(string msg, bool rcache = true, bool refresh_proxy = true, int statusCode = 503)
        {
            var model = new OnErrorResult(msg);

            if (rcache && rch?.enable != true)
                memoryCache.Set(ResponseCache.ErrorKey(HttpContext), model, DateTime.Now.AddSeconds(15));

            if (refresh_proxy && rch?.enable != true)
                proxyManager?.Refresh();

            HttpContext.Response.StatusCode = statusCode;
            return Json(model);
        }
        #endregion

        #region PlaylistResult
        public ActionResult PlaylistResult(CacheResult<List<PlaylistItem>> cache, IList<MenuItem> menu = null, int total_pages = 0)
        {
            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            return PlaylistResult(
                cache.Value,
                cache.ISingleCache,
                menu,
                total_pages
            );
        }

        public ActionResult PlaylistResult(IList<PlaylistItem> playlists, bool singleCache, IList<MenuItem> menu, int total_pages = 0)
        {
            if (playlists == null || playlists.Count == 0)
                return OnError("playlists", false);

            var utf8Writer = StatiCacheDisabled
                ? null
                : HttpContext.Features.Get<BufferWriterPool<byte>>();

            var headers_stream = HeadersModel.InitOrNull(init.headers_stream);
            var headers_image = httpHeaders(init.host, HeadersModel.InitOrNull(init.headers_image));

            if (singleCache && utf8Writer == null)
            {
                foreach (var pl in playlists)
                {
                    pl.picture = HostImgProxy(init, pl.picture, 0, headers_image);

                    if (!pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        pl.video = $"{host}/{pl.video}";
                    }
                    else
                    {
                        if (!pl.video.Contains(host, StringComparison.OrdinalIgnoreCase))
                            pl.video = HostStreamProxy(pl.video, headers_stream);
                    }
                }

                return Json(new Channel() { list = playlists, menu = menu, total_pages = total_pages });
            }
            else
            {
                IBufferWriter<byte> bufferWriter = utf8Writer ?? (IBufferWriter<byte>)Response.BodyWriter;
                bufferWriter.GetSpan(128 * 1024); // прогрев на одинаковые блоки

                Response.ContentType = "application/json; charset=utf-8";
                Response.Headers.CacheControl = "no-cache";

                #region Json Writer
                using (var writer = new Utf8JsonWriter(bufferWriter, jsonWriterOptions))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("count", playlists.Count);
                    writer.WriteNumber("totalPages", total_pages);

                    writer.WritePropertyName("menu");
                    JsonSerializer.Serialize(writer, menu ?? emptyMenu, SisiJsonContext.Default.ListMenuItem);

                    writer.WritePropertyName("list");
                    writer.WriteStartArray();

                    foreach (var pl in playlists)
                    {
                        string video = pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? pl.video
                            : $"{host}/{pl.video}";

                        if (!video.Contains(host, StringComparison.OrdinalIgnoreCase))
                            video = HostStreamProxy(video, headers_stream);

                        writer.WriteStartObject();

                        writer.WriteString("video", video);

                        if (pl.name != null)
                            writer.WriteString("name", pl.name);

                        if (pl.picture != null)
                            writer.WriteString("picture", HostImgProxy(init, pl.picture, 0, headers_image));

                        if (pl.preview != null)
                            writer.WriteString("preview", pl.preview);

                        if (pl.quality != null)
                            writer.WriteString("quality", pl.quality);

                        if (pl.time != null)
                            writer.WriteString("time", pl.time);

                        if (pl.myarg != null)
                            writer.WriteString("myarg", pl.myarg);

                        writer.WriteBoolean("json", pl.json);
                        writer.WriteBoolean("hide", pl.hide);
                        writer.WriteBoolean("related", pl.related);

                        if (pl.model != null)
                        {
                            writer.WritePropertyName("model");
                            writer.WriteStartObject();

                            if (pl.model.uri != null)
                                writer.WriteString("uri", pl.model.uri);

                            if (pl.model.name != null)
                                writer.WriteString("name", pl.model.name);

                            writer.WriteEndObject();
                        }

                        if (pl.qualitys != null)
                        {
                            writer.WritePropertyName("qualitys");
                            writer.WriteStartObject();
                            foreach (var quality in pl.qualitys)
                                writer.WriteString(quality.Key, quality.Value);
                            writer.WriteEndObject();
                        }

                        if (pl.bookmark != null)
                        {
                            writer.WritePropertyName("bookmark");
                            writer.WriteStartObject();

                            if (pl.bookmark.uid != null)
                                writer.WriteString("uid", pl.bookmark.uid);

                            if (pl.bookmark.site != null)
                                writer.WriteString("site", pl.bookmark.site);

                            if (pl.bookmark.image != null)
                                writer.WriteString("image", pl.bookmark.image);

                            if (pl.bookmark.href != null)
                                writer.WriteString("href", pl.bookmark.href);

                            writer.WriteEndObject();
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                #endregion

                if (utf8Writer != null)
                {
                    Response.BodyWriter.GetSpan(128 * 1024); // прогрев на одинаковые блоки
                    Response.BodyWriter.Write(utf8Writer.WrittenSpan);
                }

                return _emptyResult;
            }
        }
        #endregion

        #region OnResult
        public ActionResult OnResult(CacheResult<Dictionary<string, string>> cache)
        {
            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            return OnResult(cache.Value);
        }

        public ActionResult OnResult(Dictionary<string, string> stream_links)
            => OnResult(new StreamItem() { qualitys = stream_links });

        public ActionResult OnResult(CacheResult<StreamItem> cache)
        {
            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            return OnResult(cache.Value);
        }

        public ActionResult OnResult(StreamItem stream_links)
        {
            if (stream_links?.qualitys == null)
                return OnError("stream_links.qualitys");

            var headers_stream = HeadersModel.InitOrNull(init.headers_stream);
            var headers_image = httpHeaders(init.host, HeadersModel.InitOrNull(init.headers_image));

            Response.ContentType = "application/json; charset=utf-8";
            Response.Headers.CacheControl = "no-cache";

            var utf8Writer = StatiCacheDisabled
                ? null
                : HttpContext.Features.Get<BufferWriterPool<byte>>();

            IBufferWriter<byte> bufferWriter = utf8Writer ?? (IBufferWriter<byte>)Response.BodyWriter;
            bufferWriter.GetSpan(128 * 1024); // прогрев на одинаковые блоки

            #region Json Writer
            using (var writer = new Utf8JsonWriter(bufferWriter, jsonWriterOptions))
            {
                writer.WriteStartObject();

                writer.WritePropertyName("qualitys");
                writer.WriteStartObject();
                foreach (var quality in stream_links.qualitys)
                    writer.WriteString(quality.Key, HostStreamProxy(quality.Value, headers_stream));
                writer.WriteEndObject();

                if (!init.streamproxy && init.qualitys_proxy)
                {
                    writer.WritePropertyName("qualitys_proxy");
                    writer.WriteStartObject();
                    foreach (var quality in stream_links.qualitys)
                        writer.WriteString(quality.Key, HostStreamProxy(quality.Value, headers_stream, force_streamproxy: true));
                    writer.WriteEndObject();
                }

                var head_stream = init.streamproxy ? null : Http.NormalizeHeaders(init.headers_stream);
                if (head_stream != null)
                {
                    writer.WritePropertyName("headers_stream");
                    writer.WriteStartObject();
                    foreach (var header in head_stream)
                        writer.WriteString(header.Key, header.Value);
                    writer.WriteEndObject();
                }

                if (stream_links.recomends != null && stream_links.recomends.Count > 0)
                {
                    writer.WritePropertyName("recomends");
                    writer.WriteStartArray();

                    foreach (var pl in stream_links.recomends)
                    {
                        if (pl == null)
                            continue;

                        writer.WriteStartObject();

                        if (pl.name != null)
                            writer.WriteString("name", pl.name);

                        if (pl.video != null)
                            writer.WriteString("video", pl.video.StartsWith("http") ? pl.video : $"{host}/{pl.video}");

                        if (pl.picture != null)
                            writer.WriteString("picture", HostImgProxy(init, pl.picture, 110, headers_image));

                        writer.WriteBoolean("json", pl.json);

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }
            #endregion

            if (utf8Writer != null)
            {
                Response.BodyWriter.GetSpan(128 * 1024); // прогрев на одинаковые блоки
                Response.BodyWriter.Write(utf8Writer.WrittenSpan);
            }

            return _emptyResult;
        }
        #endregion

        #region IsRhubFallback
        public bool IsRhubFallback()
        {
            if (init.rhub && init.rhub_fallback)
            {
                if (rch?.enable == true)
                {
                    init.rhub = false;
                    return true;
                }
            }

            return false;
        }

        public bool IsRhubFallback<Tresut>(CacheResult<Tresut> cache)
        {
            if (cache.IsSuccess)
                return false;

            if (cache.ErrorMsg != null && cache.ErrorMsg.StartsWith("{\"rch\""))
                return false;

            if (cache.Value == null && init.rhub && init.rhub_fallback)
            {
                if (rch?.enable == true)
                {
                    init.rhub = false;
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region InvkSemaphore
        public Task<ActionResult> InvkSemaphore(string key, Func<string, Task<ActionResult>> func)
            => InvkSemaphore(key, rch, () => func.Invoke(key));
        #endregion

        #region cacheTime
        public TimeSpan cacheTime(int multiaccess)
            => cacheTimeBase(multiaccess, init: baseconf);
        #endregion

        #region HostStreamProxy
        public string HostStreamProxy(string uri, List<HeadersModel> headers = null, bool force_streamproxy = false, bool forceMd5 = false, object userdata = null)
            => HostStreamProxy(init, uri, headers, proxy, force_streamproxy, rch, forceMd5, userdata);
        #endregion

        #region InvokeCacheResult
        public ValueTask<CacheResult<Tresut>> InvokeCacheResult<Tresut>(string key, int cacheTime, JsonTypeInfo<Tresut> jsonType, Func<CacheResult<Tresut>, Task<CacheResult<Tresut>>> onget, bool? memory = null, bool textJson = false)
            => InvokeBaseCacheResult(key, this.cacheTime(cacheTime), rch, proxyManager, onget, memory, jsonType, textJson);

        public ValueTask<CacheResult<Tresut>> InvokeCacheResult<Tresut>(string key, int cacheTime, Func<CacheResult<Tresut>, Task<CacheResult<Tresut>>> onget, bool? memory = null, JsonTypeInfo<Tresut> jsonType = null, bool textJson = false)
            => InvokeBaseCacheResult(key, this.cacheTime(cacheTime), rch, proxyManager, onget, memory, jsonType, textJson);
        #endregion

        #region ipkey
        public string ipkey(string key)
            => ipkey(key, proxyManager, rch);
        #endregion

        #region headerKeys
        public string headerKeys(string key, params string[] headersKey)
            => headerKeys(key, proxyManager, rch, headersKey);
        #endregion
    }
}
