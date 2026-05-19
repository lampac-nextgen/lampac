using Jint;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Attributes;
using Shared.Models;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.SISI.OnResult;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Kit;
using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Web;
using IO = System.IO;

namespace Shared;

public class BaseController : Controller
{
    #region static
    protected ActionResult badInitMsg { get; set; }

    public bool StatiCacheDisabled { get; set; }
    #endregion

    #region hybridCache
    private IHybridCache _hybridCache;

    public IHybridCache hybridCache
        => _hybridCache ??= HybridCache.Get();
    #endregion

    #region memoryCache
    private IMemoryCache _memoryCache;

    public IMemoryCache memoryCache
    {
        get
        {
            if (_memoryCache != null)
                return _memoryCache;

            var httpContext = HttpContext;
            if (httpContext == null)
            {
                Serilog.Log.Error("HttpContext is not available. MemoryCache can only be accessed during an HTTP request.", "CatchId={CatchId}", "id_gfjonbxq");
                throw new InvalidOperationException("HttpContext is not available. MemoryCache can only be accessed during an HTTP request.");
            }

            _memoryCache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();

            return _memoryCache;
        }
    }
    #endregion

    #region requestInfo
    private RequestModel _requestInfo;

    public RequestModel requestInfo
        => _requestInfo ??= HttpContext.Features.Get<RequestModel>();
    #endregion

    #region host
    private string _host;

    public string host
        => _host ??= CoreInit.Host(HttpContext);
    #endregion


    #region mylocalip
    static string lastMyIp = null;

    async public ValueTask<string> mylocalip()
    {
        string key = "BaseController:mylocalip";
        if (!memoryCache.TryGetValue(key, out string userIp))
        {
            if (EventListener.MyLocalIp != null)
            {
                var em = new EventMyLocalIp(requestInfo, HttpContext.Request, HttpContext);

                foreach (Func<EventMyLocalIp, Task<string>> handler in EventListener.MyLocalIp.GetInvocationList())
                {
                    string eip = await handler(em);
                    if (!string.IsNullOrEmpty(eip))
                    {
                        userIp = eip;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(userIp))
            {
                var myip = await Http.Get<JObject>("https://api.ipify.org/?format=json");
                if (myip == null || string.IsNullOrEmpty(myip.Value<string>("ip")))
                    return lastMyIp;

                userIp = myip.Value<string>("ip");
                lastMyIp = userIp;
            }

            memoryCache.Set(key, userIp, DateTime.Now.AddMinutes(5));
        }

        return userIp;
    }
    #endregion

    #region httpHeaders
    public IReadOnlyList<HeadersModel> httpHeaders(BaseSettings init, IReadOnlyList<HeadersModel> startHeaders = null)
    {
        if (init.headers == null || init.headers.Count == 0)
        {
            return httpHeaders(
                init.host,
                startHeaders
            );
        }

        if (startHeaders == null || startHeaders.Count == 0)
        {
            return httpHeaders(
                init.host,
                init.headersList
            );
        }

        var joinResult = new List<HeadersModel>(init.headers.Count + startHeaders.Count);

        foreach (var h in init.headers)
        {
            if (!string.IsNullOrEmpty(h.Value))
                joinResult.Add(new(h.Key, h.Value));
        }

        foreach (var h in startHeaders)
        {
            if (!string.IsNullOrEmpty(h.val))
                joinResult.Add(new(h.name, h.val));
        }

        return httpHeaders(
            init.host,
            joinResult
        );
    }

    public IReadOnlyList<HeadersModel> httpHeaders(string site, IReadOnlyList<HeadersModel> headers)
    {
        if (ShouldModifyHttpHeaders(headers))
            return httpHeadersMutable(site, headers);

        return headers;
    }

    public IReadOnlyList<HeadersModel> httpHeaders(string site, IReadOnlyDictionary<string, string> headers)
    {
        var result = HeadersModel.InitOrNull(headers);
        if (ShouldModifyHttpHeaders(result))
            return httpHeadersMutable(site, result);

        return result;
    }

    static bool ShouldModifyHttpHeaders(IReadOnlyList<HeadersModel> _headers)
    {
        if (_headers == null || _headers.Count == 0)
            return false;

        if (EventListener.HttpHeaders != null)
            return true;

        bool mutable = false;

        foreach (var h in _headers)
        {
            string val = h.val;

            if (string.IsNullOrEmpty(val))
                continue;

            if (val.StartsWith("encrypt:") || val.Contains('{'))
            {
                mutable = true;
                break;
            }
        }

        return mutable;
    }

    IReadOnlyList<HeadersModel> httpHeadersMutable(string site, IReadOnlyList<HeadersModel> headers)
    {
        if (headers == null || headers.Count == 0)
            return null;

        var tempHeaders = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var h in headers)
        {
            if (string.IsNullOrEmpty(h.val) || string.IsNullOrEmpty(h.name))
                continue;

            string val = h.val;

            if (val.StartsWith("encrypt:"))
            {
                val = BaseSettings.BaseDecrypt(val.Substring(8));
            }
            else if (val.Contains('{'))
            {
                if (val.Contains("{account_email}"))
                    val = val.Replace("{account_email}", requestInfo.user_uid ?? string.Empty);

                if (val.Contains("{user_uid}"))
                    val = val.Replace("{user_uid}", requestInfo.user_uid ?? string.Empty);

                if (val.Contains("{ip}"))
                    val = val.Replace("{ip}", requestInfo.IP);

                if (val.Contains("{host}"))
                    val = val.Replace("{host}", site);

                if (val.Contains("{arg:"))
                {
                    foreach (Match m in Regex.Matches(val, "\\{arg:([^\\}]+)\\}"))
                    {
                        string _a = Regex.Match(HttpContext.Request.QueryString.Value ?? string.Empty, $"(&|\\?){m.Groups[1].Value}=([^&]+)").Groups[2].Value;
                        val = val.Replace(m.Groups[0].Value, _a);
                    }
                }

                if (val.Contains("{head:"))
                {
                    foreach (Match m in Regex.Matches(val, "\\{head:([^\\}]+)\\}"))
                    {
                        if (HttpContext.Request.Headers.TryGetValue(m.Groups[1].Value, out StringValues _h) && _h.Count > 0)
                        {
                            val = val.Replace(m.Groups[0].Value, string.Join(" ", _h.ToString()));
                        }
                        else
                        {
                            val = val.Replace(m.Groups[0].Value, string.Empty);
                        }

                        string _a = Regex.Match(HttpContext.Request.QueryString.Value ?? string.Empty, $"(&|\\?){m.Groups[1].Value}=([^&]+)").Groups[2].Value;
                        val = val.Replace(m.Groups[0].Value, _a);
                    }
                }
            }

            tempHeaders[h.name] = val;
        }

        if (EventListener.HttpHeaders != null)
        {
            var em = new EventControllerHttpHeaders(site, tempHeaders, requestInfo, HttpContext.Request, HttpContext);

            foreach (Func<EventControllerHttpHeaders, Dictionary<string, string>> handler in EventListener.HttpHeaders.GetInvocationList())
            {
                var eventHeaders = handler.Invoke(em);
                if (eventHeaders != null)
                {
                    tempHeaders = eventHeaders;
                    break;
                }
            }
        }

        return HeadersModel.InitOrNull(tempHeaders);
    }
    #endregion

    #region HostImgProxy
    public string HostImgProxy(BaseSettings conf, string uri, int height = 0, IReadOnlyList<HeadersModel> headers = null)
    {
        if (!CoreInit.conf.sisi.rsize || string.IsNullOrWhiteSpace(uri))
            return uri;

        var init = CoreInit.conf.sisi;
        int width = init.widthPicture;
        height = height > 0 ? height : init.heightPicture;

        if (conf.imgcorshost != null)
        {
            string aes = CrypTo.Base64(System.Text.Json.JsonSerializer.Serialize(new
            {
                u = uri,
                p = conf.plugin,
                h = httpHeaders(conf.host ?? conf.apihost, headers)?.ToDictionary(),
                t = "img",
                i = requestInfo.user_uid
            }));

            if (uri.Contains(".png"))
                aes += ".png";
            else if (uri.Contains(".webp"))
                aes += ".webp";
            else
                aes += ".jpg";

            return conf.imgcorshost.Replace("{payload}", aes);
        }

        if (conf.plugin != null && init.proxyimg_disable != null && init.proxyimg_disable.Contains(conf.plugin))
            return uri;

        if (EventListener.HostImgProxy != null)
        {
            var em = new EventHostImgProxy(requestInfo, HttpContext, uri, height, headers, conf.plugin);

            foreach (Func<EventHostImgProxy, string> handler in EventListener.HostImgProxy.GetInvocationList())
            {
                string eventUri = handler.Invoke(em);
                if (eventUri != null)
                    return eventUri;
            }
        }

        if ((width == 0 && height == 0) || (conf.plugin != null && init.rsize_disable != null && init.rsize_disable.Contains(conf.plugin)))
        {
            if (!string.IsNullOrEmpty(init.bypass_host))
            {
                string bypass_host = init.bypass_host.Replace("{sheme}", uri.StartsWith("https:") ? "https" : "http");

                if (bypass_host.Contains("{uri}"))
                    bypass_host = bypass_host.Replace("{uri}", Regex.Replace(uri, "^https?://", ""));

                if (bypass_host.Contains("{encrypt_uri}"))
                    bypass_host = bypass_host.Replace("{encrypt_uri}", ImgProxyToEncryptUri(HttpContext, uri, conf.plugin, requestInfo.IP, headers, null));

                return bypass_host;
            }
            else
            {
                return ImgProxyToEncryptUri(
                    HttpContext,
                    uri,
                    conf.plugin,
                    requestInfo.IP,
                    headers,
                    [host, "/proxyimg/"]
                );
            }
        }

        if (!string.IsNullOrEmpty(init.rsize_host))
        {
            string rsize_host = init.rsize_host.Replace("{sheme}", uri.StartsWith("https:") ? "https" : "http");

            if (rsize_host.Contains("{width}"))
                rsize_host = rsize_host.Replace("{width}", width.ToString());

            if (rsize_host.Contains("{height}"))
                rsize_host = rsize_host.Replace("{height}", height.ToString());

            if (rsize_host.Contains("{uri}"))
                rsize_host = rsize_host.Replace("{uri}", Regex.Replace(uri, "^https?://", ""));

            if (rsize_host.Contains("{encrypt_uri}"))
                rsize_host = rsize_host.Replace("{encrypt_uri}", ImgProxyToEncryptUri(HttpContext, uri, conf.plugin, requestInfo.IP, headers, null));

            return rsize_host;
        }
        else
        {
            return ImgProxyToEncryptUri(
                HttpContext,
                uri,
                conf.plugin,
                requestInfo.IP,
                headers,
                [host, $"/proxyimg:{width}:{height}/"]
            );
        }
    }

    static string ImgProxyToEncryptUri(HttpContext httpContext, string uri, string plugin, string ip, IReadOnlyList<HeadersModel> headers, string[] prefix)
    {
        return ProxyLink.Encrypt(
            uri,
            ip,
            headers != null && headers.Count > 0
                ? headers
                : null,
            plugin: plugin,
            verifyip: false,
            ex: DateTime.Today.AddDays(2),
            IsProxyImg: true,
            prefix: prefix
        );
    }
    #endregion

    #region HostStreamProxy
    public string HostStreamProxy(BaseSettings conf, string uri, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, bool force_streamproxy = false, RchClient rch = null, bool forceMd5 = false, object userdata = null)
    {
        if (!CoreInit.conf.serverproxy.enable || string.IsNullOrEmpty(uri) || conf == null)
        {
            return uri != null && uri.Contains(" ")
                ? uri.Split(" ")[0].Trim()
                : uri?.Trim();
        }

        if (EventListener.HostStreamProxy != null)
        {
            var em = new EventHostStreamProxy(conf, uri, headers, proxy, requestInfo, HttpContext);

            foreach (Func<EventHostStreamProxy, string> handler in EventListener.HostStreamProxy.GetInvocationList())
            {
                string eventUri = handler.Invoke(em);
                if (eventUri != null)
                    return eventUri;
            }
        }

        if (conf.rhub && !conf.rhub_streamproxy)
        {
            return uri.Contains(" ")
                ? uri.Split(" ")[0].Trim()
                : uri.Trim();
        }

        bool streamproxy = conf.streamproxy || conf.apnstream || conf.useproxystream || force_streamproxy;

        #region geostreamproxy
        if (!streamproxy && conf.geostreamproxy != null && conf.geostreamproxy.Length > 0)
        {
            string country = requestInfo.Country;
            if (country != null && country.Length == 2)
            {
                if (conf.geostreamproxy.Contains("ALL") || conf.geostreamproxy.Contains(country))
                    streamproxy = true;
            }
        }
        #endregion

        #region rchstreamproxy
        if (!streamproxy && conf.rchstreamproxy != null && rch != null)
        {
            var rchinfo = rch.InfoConnected();
            if (rchinfo?.rchtype != null)
                streamproxy = conf.rchstreamproxy.Contains(rchinfo.rchtype);
        }
        #endregion

        if (streamproxy)
        {
            if (CoreInit.conf.serverproxy.forced_apn || conf.apnstream)
            {
                if (!string.IsNullOrEmpty(conf.apn?.host) && conf.apn.host.StartsWith("http"))
                    return apnlink(conf, conf.apn, uri, requestInfo.IP, headers);

                if (!string.IsNullOrEmpty(CoreInit.conf?.apn?.host) && CoreInit.conf.apn.host.StartsWith("http"))
                    return apnlink(conf, CoreInit.conf.apn, uri, requestInfo.IP, headers);

                return uri;
            }

            if (headers == null && conf.headers_stream != null && conf.headers_stream.Count > 0)
                headers = HeadersModel.Init(conf.headers_stream);

            return ProxyLink.Encrypt(
                uri,
                requestInfo.IP,
                httpHeaders(conf.host ?? conf.apihost, headers),
                conf != null && conf.useproxystream
                    ? proxy
                    : null,
                conf?.plugin,
                forceMd5: forceMd5,
                userdata: userdata,
                prefix: [host, "/proxy/"]
            );
        }
        else
        {
            if (conf.url_reserve && !uri.Contains(" or ") && !uri.Contains("/proxy/") &&
                !Regex.IsMatch(HttpContext.Request.QueryString.Value, "&play=true", RegexOptions.IgnoreCase))
            {
                return ProxyLink.Encrypt(
                    uri, requestInfo.IP,
                    httpHeaders(conf.host ?? conf.apihost, headers),
                    conf != null && conf.useproxystream
                        ? proxy
                        : null,
                    conf?.plugin,
                    forceMd5: forceMd5,
                    userdata: userdata,
                    prefix: [uri, " or ", host, "/proxy/"]
                );
            }
            else
            {
                return uri;
            }
        }
    }

    string apnlink(BaseSettings conf, ApnConf apn, string uri, string ip, IReadOnlyList<HeadersModel> headers)
    {
        string link = uri.Contains(" ") || uri.Contains("#")
            ? uri.Split(" ")[0].Split("#")[0].Trim()
            : uri.Trim();

        if (apn.secure == "nginx")
        {
            long ex = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds();
            string security = $"{ex}{ip} {apn.secret}";

            Span<byte> data = stackalloc byte[Encoding.UTF8.GetByteCount(security)];
            int bytesWritten = Encoding.UTF8.GetBytes(security, data);

            Span<byte> hashBytes = stackalloc byte[16];
            MD5.HashData(data.Slice(0, bytesWritten), hashBytes);

            return $"{apn.host}/{Base64Url.EncodeToString(hashBytes)}:{ex}/{link}";
        }
        else if (apn.secure == "lampac")
        {
            return ProxyLink.Encrypt(
                uri,
                ip,
                httpHeaders(conf.host, headers),
                plugin: conf?.plugin,
                prefix: [apn.host, "/proxy/"]
            );
        }

        if (apn.host.Contains("{encode_uri}") || apn.host.Contains("{uri}"))
            return apn.host.Replace("{encode_uri}", HttpUtility.UrlEncode(link)).Replace("{uri}", link);

        if (apn.host.Contains("{payload}"))
        {
            var sb = StringBuilderPool.RentSmall();

            try
            {
                CrypTo.Base64(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        u = link,
                        p = conf.plugin,
                        h = httpHeaders(conf.host, headers)?.ToDictionary(),
                        t = "media",
                        i = requestInfo.user_uid,
                    }),
                    base64 => sb.Append(base64)
                );

                if (uri.Contains(".m3u"))
                    sb.Append(".m3u8");

                return apn.host.Replace("{payload}", sb.ToString());
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        return $"{apn.host}/{link}";
    }
    #endregion

    #region InvokeBaseCache
    async public ValueTask<T> InvokeBaseCache<T>(string key, TimeSpan time, RchClient rch, Func<Task<T>> onget, ProxyManager proxyManager = null, bool? memory = null, JsonTypeInfo<T> jsonType = null, bool textJson = false)
    {
        #region cache
        if (hybridCache.ContainsKey(key, out T entryValue, out DateTimeOffset cachEx))
        {
            if (entryValue != null && !entryValue.Equals(default(T)))
            {
                HttpContext.Response.Headers["X-Invoke-Cache"] = "HIT";
                UpdateStatiCacheFeatures(cachEx);
                return entryValue;
            }
            else
            {
                var entry = await hybridCache.EntryAsync(key, jsonType: jsonType, fileCache: true, textJson: textJson);
                if (entry != null && entry.success)
                {
                    HttpContext.Response.Headers["X-Invoke-Cache"] = "HIT";
                    UpdateStatiCacheFeatures(cachEx);
                    return entry.value;
                }
            }
        }
        #endregion

        SemaphorManager semaphore = null;

        try
        {
            if (rch?.enable != true)
            {
                semaphore = new SemaphorManager(key, TimeSpan.FromSeconds(30));
                bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                if (!_acquired)
                    return default;

                if (hybridCache.ContainsKey(key, out entryValue, out cachEx))
                {
                    if (entryValue != null && !entryValue.Equals(default(T)))
                    {
                        HttpContext.Response.Headers["X-Invoke-Cache"] = "HIT";
                        UpdateStatiCacheFeatures(cachEx);
                        return entryValue;
                    }
                }
            }

            HttpContext.Response.Headers["X-Invoke-Cache"] = "MISS";

            var val = await onget.Invoke().ConfigureAwait(false);
            if (val == null || val.Equals(default(T)))
            {
                if (rch?.enable != true)
                    proxyManager?.Refresh();

                return default;
            }

            if (rch?.enable != true)
                proxyManager?.Success();

            hybridCache.Set(key, val, time, memory);
            UpdateStatiCacheFeatures(DateTimeOffset.Now.Add(time));

            return val;
        }
        finally
        {
            semaphore?.Release();
        }
    }
    #endregion

    #region InvokeBaseCacheResult
    async public ValueTask<CacheResult<T>> InvokeBaseCacheResult<T>(string key, TimeSpan time, RchClient rch, ProxyManager proxyManager, Func<CacheResult<T>, Task<CacheResult<T>>> onget, bool? memory = null, JsonTypeInfo<T> jsonType = null, bool textJson = false)
    {
        #region cache
        if (hybridCache.ContainsKey(key, out T entryValue, out DateTimeOffset cachEx))
        {
            if (entryValue != null && !entryValue.Equals(default(T)))
            {
                HttpContext.Response.Headers["X-Invoke-Cache"] = "HIT";
                UpdateStatiCacheFeatures(cachEx);

                return new CacheResult<T>()
                {
                    IsSuccess = true,
                    Value = entryValue
                };
            }
            else
            {
                var entry = await hybridCache.EntryAsync(key, jsonType: jsonType, fileCache: true, textJson: textJson);
                if (entry != null && entry.success)
                {
                    HttpContext.Response.Headers["X-Invoke-Cache"] = "HIT";
                    UpdateStatiCacheFeatures(cachEx);

                    return new CacheResult<T>()
                    {
                        IsSuccess = true,
                        ISingleCache = entry.singleCache,
                        Value = entry.value
                    };
                }
            }
        }
        #endregion

        SemaphorManager semaphore = null;

        try
        {
            if (rch?.enable != true)
            {
                semaphore = new SemaphorManager(key, TimeSpan.FromSeconds(30));
                bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                if (!_acquired)
                    return new CacheResult<T>() { IsSuccess = false, ErrorMsg = "semaphore" };

                if (hybridCache.ContainsKey(key, out entryValue, out cachEx))
                {
                    if (entryValue != null && !entryValue.Equals(default(T)))
                    {
                        HttpContext.Response.Headers["X-Invoke-Cache"] = "HIT";
                        UpdateStatiCacheFeatures(cachEx);

                        return new CacheResult<T>()
                        {
                            IsSuccess = true,
                            Value = entryValue
                        };
                    }
                }
            }

            HttpContext.Response.Headers["X-Invoke-Cache"] = "MISS";

            var val = await onget.Invoke(new CacheResult<T>()).ConfigureAwait(false);

            if (val == null || val.Value == null)
            {
                if (rch?.enable != true)
                    proxyManager?.Refresh();

                return new CacheResult<T>() { IsSuccess = false, ErrorMsg = "null" };
            }

            if (!val.IsSuccess || val.Value.Equals(default(T)))
            {
                if (val.refresh_proxy && rch?.enable != true)
                    proxyManager?.Refresh();

                return val;
            }

            if (rch?.enable != true)
                proxyManager?.Success();

            hybridCache.Set(key, val.Value, time, memory);
            UpdateStatiCacheFeatures(DateTimeOffset.Now.Add(time));

            return new CacheResult<T>() { IsSuccess = true, Value = val.Value };
        }
        finally
        {
            semaphore?.Release();
        }
    }
    #endregion

    #region UpdateStatiCacheFeatures
    StatiCacheEntry _statiCacheEntry;

    void UpdateStatiCacheFeatures(DateTimeOffset ex)
    {
        if (!CoreInit.conf.Staticache.enable)
            return;

        if (_statiCacheEntry == null || _statiCacheEntry.ex > ex)
        {
            _statiCacheEntry = new StatiCacheEntry(ex);
            HttpContext.Features.Set(_statiCacheEntry);
        }
    }
    #endregion

    #region InvkSemaphore
    async public Task<ActionResult> InvkSemaphore(string key, RchClient rch, Func<Task<ActionResult>> func)
    {
        if (rch?.enable == true)
            return await func.Invoke().ConfigureAwait(false);

        var semaphore = new SemaphorManager(key, TimeSpan.FromSeconds(30));

        try
        {
            bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
            if (!_acquired)
                return BadRequest();

            return await func.Invoke().ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
    #endregion

    #region cacheTime
    public TimeSpan cacheTimeBase(int multiaccess, BaseSettings init = null, int rhub = -1)
    {
        if (init != null && init.rhub && rhub != -1)
            return TimeSpan.FromMinutes(rhub);

        int ctime = init != null && init.cache_time > 0 ? init.cache_time : multiaccess;
        if (ctime > multiaccess)
            ctime = multiaccess;

        return TimeSpan.FromMinutes(ctime);
    }
    #endregion

    #region IsCacheError
    public bool IsCacheError(BaseSettings init, RchClient rch)
    {
        if (init.rhub)
            return false;

        if (rch?.enable == true)
            return false;

        if (memoryCache.TryGetValue(ResponseCache.ErrorKey(HttpContext), out object errorCache))
        {
            HttpContext.Response.Headers.TryAdd("X-RCache", "true");

            if (errorCache is OnErrorResult)
            {
                HttpContext.Response.StatusCode = 503;
                badInitMsg = Json(errorCache);
                return true;
            }
            else if (errorCache is string)
            {
                string msg = errorCache.ToString();
            }

            badInitMsg = StatusCode(503);
            return true;
        }

        return false;
    }
    #endregion

    #region IsOverridehost
    public bool IsOverridehost(BaseSettings init)
    {
        if (!string.IsNullOrEmpty(init.overridehost))
            return true;

        if (init.overridehosts != null && init.overridehosts.Length > 0)
            return true;

        return false;
    }

    async public Task<ActionResult> InvokeOverridehost(BaseSettings init)
    {
        string overridehost = null;

        if (!string.IsNullOrEmpty(init.overridehost))
            overridehost = init.overridehost;

        if (string.IsNullOrEmpty(overridehost) && init.overridehosts != null && init.overridehosts.Length > 0)
            overridehost = init.overridehosts[Random.Shared.Next(0, init.overridehosts.Length)];

        if (string.IsNullOrEmpty(overridehost))
            return null;

        if (string.IsNullOrEmpty(init.overridepasswd))
        {
            if (overridehost.Contains("?"))
                overridehost += "&" + HttpContext.Request.QueryString.Value.Remove(0, 1);
            else
                overridehost += HttpContext.Request.QueryString.Value;

            return new RedirectResult(overridehost);
        }

        overridehost = Regex.Replace(overridehost, "^(https?://[^/]+)/.*", "$1");
        string uri = overridehost + HttpContext.Request.Path.Value + HttpContext.Request.QueryString.Value;

        string clientip = requestInfo.IP;
        if (requestInfo.Country == null)
            clientip = await mylocalip();

        string html = await Http.Get(uri, timeoutSeconds: 10, headers: HeadersModel.Init
        (
            ("lcrqpasswd", init.overridepasswd),
            ("x-client-ip", clientip)
        ));

        if (html == null)
            return new ContentResult() { StatusCode = 502, Content = string.Empty };

        html = Regex.Replace(html, "\"(https?://[^/]+/proxy/)", "\"_tmp_ $1");
        html = Regex.Replace(html, $"\"{overridehost}", $"\"{host}");
        html = html.Replace("\"_tmp_ ", "\"");

        return ContentTo(html);
    }
    #endregion

    #region NoAccessGroup
    public bool NoAccessGroup(Igroup init, out string error_msg)
    {
        error_msg = null;

        if (init.group > 0)
        {
            var user = requestInfo.user;
            if (user == null || init.group > user.group)
            {
                error_msg = CoreInit.conf.accsdb.denyGroupMesage
                    .Replace("{account_email}", requestInfo.user_uid)
                    .Replace("{user_uid}", requestInfo.user_uid);

                return true;
            }
        }

        return false;
    }
    #endregion

    #region accsArgs
    public string accsArgs(string uri)
    {
        return AccsDbInvk.Args(uri, HttpContext);
    }
    #endregion

    #region loadKit
    public bool IsKitConf { get; private set; }

    bool IsLoadKitConf()
    {
        var init = CoreInit.conf.kit;
        if (!init.enable || string.IsNullOrEmpty(init.path))
            return false;

        return true;
    }

    public JObject loadKitConf()
    {
        var kit = CoreInit.conf.kit;

        try
        {
            if (string.IsNullOrEmpty(requestInfo.AesGcmKey))
                return null;

            DateTime lastWriteTimeUtc = default;

            string memKey = $"loadFileKit:{requestInfo.AesGcmKey}";

            if (memoryCache.TryGetValue(memKey, out KitCacheEntry _cache))
            {
                if (_cache.lockTime >= DateTime.Now)
                    return _cache.init;

                lastWriteTimeUtc = IO.File.GetLastWriteTimeUtc(_cache.infile);
                if (_cache.lastWriteTimeUtc == lastWriteTimeUtc)
                {
                    _cache.lockTime = DateTime.Now.AddSeconds(Math.Max(5, kit.configCheckIntervalSeconds));
                    return _cache.init;
                }
            }

            _cache = new KitCacheEntry();
            var lockTime = DateTime.Now.AddSeconds(Math.Max(5, kit.configCheckIntervalSeconds));

            string _md5key = CrypTo.md5(requestInfo.AesGcmKey);
            _cache.infile = $"{kit.path}/{_md5key[0]}/{_md5key}";

            if (kit.eval_path != null)
                _cache.infile = CSharpEval.Execute<string>(kit.eval_path, new KitConfEvalPath(kit.path, requestInfo.AesGcmKey));

            if (!IO.File.Exists(_cache.infile))
            {
                _cache.lockTime = lockTime;
                memoryCache.Set(memKey, _cache, _cache.lockTime);
                return null;
            }

            string json = kit.aes
                ? CryptoKit.ReadFile(requestInfo.AesGcmKey, _cache.infile)
                : IO.File.ReadAllText(_cache.infile);

            if (string.IsNullOrWhiteSpace(json))
            {
                _cache.lockTime = lockTime;
                memoryCache.Set(memKey, _cache, _cache.lockTime);
                return null;
            }

            if (lastWriteTimeUtc == default)
                lastWriteTimeUtc = IO.File.GetLastWriteTimeUtc(_cache.infile);

            _cache.lastWriteTimeUtc = lastWriteTimeUtc;
            _cache.lockTime = lockTime;

            if (!json.AsSpan().TrimStart().StartsWith('{'))
                json = "{" + json + "}";

            _cache.init = JsonConvert.DeserializeObject<JObject>(json);

            memoryCache.Set(memKey, _cache, DateTime.Now.AddSeconds(Math.Max(5, kit.cacheToSeconds)));

            return _cache.init;
        }
        catch
        {
            return null;
        }
    }

    public bool IsLoadKit<T>(T _init) where T : BaseSettings
    {
        if (EventListener.LoadKitInit != null || EventListener.LoadKit != null)
            return true;

        if (IsLoadKitConf())
            return _init.kit;

        return false;
    }

    public T loadKit<T>(T _init, Func<JObject, T, T, T> func = null) where T : BaseSettings, ICloneable
    {
        var clone = _init.IsCloneable ? _init : (T)_init.Clone();

        if (_init.kit == false)
        {
            if (EventListener.LoadKitInit != null)
            {
                var em = new EventLoadKit(null, clone, null, requestInfo);
                foreach (Action<EventLoadKit> handler in EventListener.LoadKitInit.GetInvocationList())
                    handler(em);
            }

            return clone;
        }

        JObject appinit = null;

        if (IsLoadKitConf())
            appinit = loadKitConf();

        return loadKit(clone, appinit, func, false);
    }

    public T loadKit<T>(T _init, JObject appinit, Func<JObject, T, T, T> func = null, bool clone = true) where T : BaseSettings, ICloneable
    {
        var kit = KitInvoke.Load(_init, appinit, requestInfo, func, clone);
        if (kit.IsKitConf)
            IsKitConf = true;

        return kit;
    }
    #endregion

    #region RedirectToPlay
    public RedirectResult RedirectToPlay(string url)
    {
        if (!url.Contains(" "))
            return new RedirectResult(url);

        return new RedirectResult(url.Split(" ")[0].Trim());
    }
    #endregion

    #region ContentTo
    public ActionResult ContentTo(string html)
    {
        if (string.IsNullOrEmpty(html))
            return StatusCode(503);

        return Content(html, html.StartsWith("{") || html.StartsWith("[") ? "application/json; charset=utf-8" : "text/html; charset=utf-8");
    }
    #endregion

    #region ipkey
    public string ipkey(string key, ProxyManager proxy, RchClient rch)
    {
        if (rch != null)
            return $"ipkey:{key}:{(rch.enable ? requestInfo.IP : proxy?.CurrentProxyIp)}";

        return $"ipkey:{key}:{proxy?.CurrentProxyIp}";
    }

    public string ipkey(string key, RchClient rch)
        => rch?.enable == true ? $"ipkey:{key}:{requestInfo.IP}" : key;
    #endregion

    #region headerKeys
    public string headerKeys(string key, ProxyManager proxy, RchClient rch, params string[] headersKey)
    {
        if (rch?.enable != true)
            return $"{key}:{proxy?.CurrentProxyIp}";

        const char splitKey = ':';
        var sb = StringBuilderPool.ThreadInstance;

        sb.Append(key);
        sb.Append(splitKey);

        foreach (string hk in headersKey)
        {
            if (HttpContext.Request.Headers.TryGetValue(hk, out StringValues headerValue) && headerValue.Count > 0)
            {
                sb.Append(headerValue[0]);
                sb.Append(splitKey);
            }
        }

        return sb.ToString();
    }
    #endregion

    #region Encrypt/Decrypt Query
    public string EncryptQuery(ReadOnlySpan<char> value)
        => CrypTo.EncryptQuery(value);

    public string DecryptQuery(ReadOnlySpan<char> value)
        => CrypTo.DecryptQuery(value);
    #endregion

    #region JSRuntime
    public Engine JSRuntime(string jsFile)
        => JSRuntime(jsFile, null);

    public Engine JSRuntime(string jsFile, BaseSettings init, HttpHydra httpHydra = null, RchClient rch = null, WebProxy proxy = null)
    {
        var js = new Engine();
        js.Execute(jsFile);

        js.SetValue("log", new Action<object>(Console.WriteLine));

        js.SetValue("host", host);
        js.SetValue("encryptQuery", new Func<string, string>(url => EncryptQuery(url)));
        js.SetValue("decryptQuery", new Func<string, string>(url => DecryptQuery(url)));

        js.SetValue("cacheGet", new Func<string, string>(key =>
        {
            hybridCache.TryGetValue($"{init?.plugin}:{key}", out string value);
            return value;
        }));

        js.SetValue("cacheSet", new Action<string, string, int>((key, value, time) =>
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            hybridCache.Set($"{init?.plugin}:{key}", value, TimeSpan.FromMinutes(time));
        }));

        js.SetValue("req", new
        {
            method = Request.Method,
            path = Request.Path.ToString(),
            headers = Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString()),
            query = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString()),
            ip = requestInfo.IP
        });

        if (init != null)
        {
            js.SetValue("streamProxy", new Func<string, Dictionary<string, string>, string>(
                (url, headers) => HostStreamProxy(init, url, HeadersModel.Init(headers), proxy, rch: rch)));

            js.SetValue("imgProxy", new Func<string, int, string>((url, height) => HostImgProxy(init, url, height)));

            if (httpHydra == null)
                httpHydra = new HttpHydra(init, httpHeaders(init), requestInfo, rch, proxy);
        }

        if (httpHydra == null)
            httpHydra = new HttpHydra(new BaseSettings() { useproxy = proxy != null }, null, requestInfo, rch, proxy);

        js.SetValue("httpGet", new Func<string, Dictionary<string, string>, Dictionary<string, string>, Task<string>>(
            (url, addheaders, newheaders) => httpHydra.Get(url, HeadersModel.Init(addheaders), HeadersModel.Init(newheaders))));

        js.SetValue("httpPost", new Func<string, string, Dictionary<string, string>, Dictionary<string, string>, Task<string>>(
            (url, data, addheaders, newheaders) => httpHydra.Post(url, data, HeadersModel.Init(addheaders), HeadersModel.Init(newheaders))));

        js.SetValue("EpisodeTpl", typeof(EpisodeTpl));
        js.SetValue("MovieTpl", typeof(MovieTpl));
        js.SetValue("SeasonTpl", typeof(SeasonTpl));
        js.SetValue("SegmentTpl", typeof(SegmentTpl));
        js.SetValue("SimilarTpl", typeof(SimilarTpl));
        js.SetValue("StreamQualityTpl", typeof(StreamQualityTpl));
        js.SetValue("SubtitleTpl", typeof(SubtitleTpl));
        js.SetValue("VideoTpl", typeof(VideoTpl));
        js.SetValue("VoiceTpl", typeof(VoiceTpl));

        return js;
    }
    #endregion

    #region SetHeadersNoCache
    public void SetHeadersNoCache()
    {
        HttpContext.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate"; // HTTP 1.1.
        HttpContext.Response.Headers["Pragma"] = "no-cache"; // HTTP 1.0.
        HttpContext.Response.Headers["Expires"] = "0"; // Proxies.
    }
    #endregion
}
