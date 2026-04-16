using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using Shared.Models;
using Shared.Models.Events;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Shared
{
    public class BaseOnlineController : BaseOnlineController<OnlinesSettings>
    {
        public BaseOnlineController() : base(default) { }

        public BaseOnlineController(OnlinesSettings init) : base(init) { }
    }

    public class BaseOnlineController<T> : BaseController where T : BaseSettings, ICloneable
    {
        static readonly EmptyResult _emptyResult = new();

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


        public BaseOnlineController(T init)
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
                badInitMsg = new JsonResult(new { accsdb = true, msg = error_msg });
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
                badInitMsg = OnError("disable", gbcache: false, statusCode: 403);
                return true;
            }

            if (rch != null)
            {
                if ((bool)rch)
                {
                    if (init.rhub && !CoreInit.conf.rch.enable)
                    {
                        badInitMsg = ShowError(RchClient.ErrorMsg);
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
                            badInitMsg = ShowError(rch_error);
                            return true;
                        }
                    }
                }
                else
                {
                    if (init.rhub)
                    {
                        badInitMsg = ShowError(RchClient.ErrorMsg);
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
            => OnError(msg, null, refresh_proxy, null, statusCode);

        public ActionResult OnError(string msg, bool? gbcache = true, bool refresh_proxy = false, string weblog = null, int statusCode = 503)
        {
            if (string.IsNullOrEmpty(msg) || !msg.StartsWith("{\"rch\""))
            {
                if (refresh_proxy && rch?.enable != true)
                    proxyManager?.Refresh();
            }

            if (!string.IsNullOrEmpty(msg))
            {
                if (msg.StartsWith("{\"rch\""))
                    return Content(msg);
            }

            if (gbcache == true && rch?.enable != true)
                memoryCache.Set(ResponseCache.ErrorKey(HttpContext), msg ?? string.Empty, DateTime.Now.AddSeconds(15));

            HttpContext.Response.StatusCode = statusCode;
            return ContentTo(msg ?? string.Empty);
        }
        #endregion

        #region OnResult
        public ActionResult OnResult<Tresut>(CacheResult<Tresut> cache, Func<string> html)
        {
            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            if (cache.Value != null && IsOrigsourceRequest())
                return Json(cache.Value);

            return ContentTo(html.Invoke());
        }
        #endregion

        #region ContentTpl
        public ActionResult ContentTpl<Tresut>(CacheResult<Tresut> cache, Func<ITplResult> tpl)
        {
            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            if (cache.Value != null && IsOrigsourceRequest())
                return Json(cache.Value);

            return ContentTpl(tpl());
        }

        public ActionResult ContentTpl(ITplResult tpl)
        {
            bool rjson = IsRjsonRequest();

            if (tpl == null || tpl.IsEmpty)
                return OnError(rjson ? "{}" : string.Empty);

            var response = HttpContext.Response;
            response.Headers.CacheControl = "no-cache";
            response.ContentType = rjson
                ? "application/json; charset=utf-8"
                : "text/html; charset=utf-8";

            var encoder = Encoding.UTF8.GetEncoder();

            var sb = rjson
                ? tpl.ToBuilderJson()
                : tpl.ToBuilderHtml();

            var utf8Writer = StatiCacheDisabled
                ? null
                : HttpContext.Features.Get<BufferWriterPool<byte>>();

            IBufferWriter<byte> writer = utf8Writer ?? (IBufferWriter<byte>)Response.BodyWriter;
            writer.GetSpan(128 * 1024); // прогрев на одинаковые блоки

            try
            {
                foreach (var chunk in sb.GetChunks())
                {
                    ReadOnlySpan<char> chars = chunk.Span;
                    if (chars.IsEmpty)
                        continue;

                    while (!chars.IsEmpty)
                    {
                        Span<byte> span = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(chars.Length));

                        encoder.Convert(
                            chars,
                            span,
                            flush: false,
                            out int charsUsed,
                            out int bytesUsed,
                            out _);

                        writer.Advance(bytesUsed);
                        chars = chars.Slice(charsUsed);
                    }
                }
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }

            #region границы чанков/суррогаты
            Span<byte> tail = writer.GetSpan(256);

            encoder.Convert(
                ReadOnlySpan<char>.Empty,
                tail,
                flush: true,
                out int _,
                out int _bytesUsed,
                out bool _);

            writer.Advance(_bytesUsed);
            #endregion

            if (utf8Writer != null)
            {
                Response.BodyWriter.GetSpan(128 * 1024); // прогрев на одинаковые блоки
                Response.BodyWriter.Write(utf8Writer.WrittenSpan);
            }

            return _emptyResult;
        }
        #endregion

        #region ShowError
        public ActionResult ShowError(string msg)
            => Json(new OnlineErrorPayload(msg));

        public string ShowErrorString(string msg)
            => JsonSerializer.Serialize(new OnlineErrorPayload(msg), BaseOnlineControllerErrorJsonContext.Default.OnlineErrorPayload);
        #endregion


        #region IsRhubFallback
        public bool IsRhubFallback<Tresut>(CacheResult<Tresut> cache, bool safety = false)
        {
            if (cache.IsSuccess)
                return false;

            if (cache.ErrorMsg != null && cache.ErrorMsg.StartsWith("{\"rch\""))
                return false;

            if (cache.Value == null && init.rhub && init.rhub_fallback)
            {
                init.rhub = false;

                if (safety && init.rhub_safety)
                    return false;

                return rch != null;
            }

            return false;
        }
        #endregion

        #region InvokeCacheResult
        async public ValueTask<CacheResult<Tresut>> InvokeCacheResult<Tresut>(string key, int cacheTime, Func<Task<Tresut>> onget, bool? memory = null, JsonTypeInfo<Tresut> jsonType = null, bool textJson = false)
            => await InvokeBaseCacheResult<Tresut>(key, this.cacheTime(cacheTime), rch, proxyManager, async e => e.Success(await onget()), memory, jsonType, textJson);

        public ValueTask<CacheResult<Tresut>> InvokeCacheResult<Tresut>(string key, TimeSpan time, Func<CacheResult<Tresut>, Task<CacheResult<Tresut>>> onget, bool? memory = null, JsonTypeInfo<Tresut> jsonType = null, bool textJson = false)
            => InvokeBaseCacheResult(key, time, rch, proxyManager, onget, memory, jsonType, textJson);

        public ValueTask<CacheResult<Tresut>> InvokeCacheResult<Tresut>(string key, int cacheTime, Func<CacheResult<Tresut>, Task<CacheResult<Tresut>>> onget, bool? memory = null, JsonTypeInfo<Tresut> jsonType = null, bool textJson = false)
            => InvokeBaseCacheResult(key, this.cacheTime(cacheTime), rch, proxyManager, onget, memory, jsonType, textJson);
        #endregion

        #region InvokeCache
        public ValueTask<Tresut> InvokeCache<Tresut>(string key, TimeSpan time, Func<Task<Tresut>> onget, bool? memory = null, JsonTypeInfo<Tresut> jsonType = null, bool textJson = false)
            => InvokeBaseCache(key, time, rch, onget, proxyManager, memory, jsonType, textJson);

        public ValueTask<Tresut> InvokeCache<Tresut>(string key, int cacheTime, Func<Task<Tresut>> onget, bool? memory = null, JsonTypeInfo<Tresut> jsonType = null, bool textJson = false)
            => InvokeBaseCache(key, this.cacheTime(cacheTime), rch, onget, proxyManager, memory, jsonType, textJson);
        #endregion

        #region HostStreamProxy
        public string HostStreamProxy(string uri, List<HeadersModel> headers = null, bool force_streamproxy = false, bool forceMd5 = false, object userdata = null)
            => HostStreamProxy(init, uri, headers, proxy, force_streamproxy, rch, forceMd5, userdata);
        #endregion

        #region InvkSemaphore
        public Task<ActionResult> InvkSemaphore(string key, Func<string, Task<ActionResult>> func)
            => InvkSemaphore(key, rch, () => func.Invoke(key));

        public Task<ActionResult> InvkSemaphore(string key, Func<Task<ActionResult>> func)
            => InvkSemaphore(key, rch, func);
        #endregion


        #region cacheTime
        public TimeSpan cacheTime(int multiaccess)
        {
            return cacheTimeBase(multiaccess, init: baseconf);
        }
        #endregion

        #region ipkey
        public string ipkey(string key)
            => ipkey(key, proxyManager, rch);
        #endregion


        #region IsRjsonRequest
        public bool IsRjsonRequest()
        {
            return HttpContext.Request.Query.TryGetValue("rjson", out StringValues value)
                && value.Count > 0
                && value[0] != null
                && value[0].Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region IsOrigsourceRequest
        public bool IsOrigsourceRequest()
        {
            return HttpContext.Request.Query.TryGetValue("origsource", out StringValues value)
                && value.Count > 0
                && value[0] != null
                && value[0].Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        #endregion
    }

    internal sealed class OnlineErrorPayload(string msg)
    {
        public bool accsdb { get; } = true;

        public string msg { get; } = msg;
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
    [JsonSerializable(typeof(OnlineErrorPayload))]
    internal partial class BaseOnlineControllerErrorJsonContext : JsonSerializerContext
    {
    }
}
