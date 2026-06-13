using Microsoft.AspNetCore.Mvc;
using System;
using Shared.Models.Base;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ebalovo;

public class EbalovoController : BaseSisiController
{
    public EbalovoController() : base(ModInit.conf) { }

    [HttpGet, Staticache]
    [Route("elo")]
    async public Task<ActionResult> Index(string search, string sort, string c, string model, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"elo:{search}:{sort}:{c}:{model}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            string ehost = await goHost(init.host, proxy);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(EbalovoTo.Uri(ehost, search, sort, c, model, pg), span =>
            {
                playlists = EbalovoTo.Playlist("elo/vidosik", span, pl =>
                {
                    if (!string.IsNullOrWhiteSpace(pl?.bookmark?.href))
                    {
                        string modelProbe = $"elo/model?uri={WebUtility.UrlEncode(pl.bookmark.href)}";

                        if (!string.IsNullOrWhiteSpace(model))
                            modelProbe += $"&current={WebUtility.UrlEncode(model.Trim('/'))}";

                        pl.myarg = $"model_probe:{modelProbe}";
                    }

                    return pl;
                });
            },
            addheaders: HeadersModel.Init(
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1")
            ));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache,
            string.IsNullOrEmpty(search) && string.IsNullOrEmpty(model) ? EbalovoTo.Menu(host, sort, c) : null,
            total_pages: string.IsNullOrEmpty(model) ? 0 : 1
        );
    }


    [HttpGet]
    [Route("elo/model")]
    async public Task<ActionResult> Model(string uri, string current)
    {
        if (await IsRequestBlocked(rch_check: false))
            return badInitMsg;

        uri = NormalizeSitePath(uri);
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { model = default(ModelItem) });

        var memoryCache = HybridCache.GetMemory();
        string memKey = $"ebalovo:model:v1:{uri}";

        if (memoryCache.TryGetValue(memKey, out List<ModelItem> cachedModels))
        {
            cachedModels = FilterCurrentModels(current, cachedModels);
            return Json(new { model = cachedModels?.FirstOrDefault(), models = cachedModels });
        }

        string ehost = await goHost(init.host, proxy);

        var html = await httpHydra.Get($"{ehost}/{uri}",
            addheaders: HeadersModel.Init(
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1")
            )
        );

        var models = EbalovoTo.Models("elo", html);
        if (models is not { Count: > 0 })
            return Json(new { model = default(ModelItem) });

        memoryCache.Set(memKey, models, DateTimeOffset.Now.AddHours(24));

        models = FilterCurrentModels(current, models);
        return Json(new { model = models?.FirstOrDefault(), models });
    }


    [HttpGet, Staticache(manually: true)]
    [Route("elo/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (rch?.enable == true && 484 > rch.InfoConnected()?.apkVersion)
        {
            rch.Disabled(); // на версиях ниже java.lang.OutOfMemoryError
            if (!init.rhub_fallback)
                return OnError("apkVersion", false);
        }

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"ebalovo:view:{uri}"), 20, jsonContext.StreamItem, async e =>
        {
            string ehost = await goHost(init.host);

            var stream_links = await EbalovoTo.StreamLinks(httpHydra, "elo/vidosik", ehost, uri,
                async location =>
                {
                    var headers = httpHeaders(init, HeadersModel.Init(
                        ("referer", $"{ehost}/"),
                        ("sec-fetch-dest", "video"),
                        ("sec-fetch-mode", "no-cors"),
                        ("sec-fetch-site", "same-origin")
                    ));

                    if (rch?.enable == true)
                    {
                        var res = await rch.Headers(init.cors(location, headers, requestInfo), null, headers);
                        return res.currentUrl;
                    }

                    return await Http.GetLocation(init.cors(location, headers, requestInfo), timeoutSeconds: init.httptimeout, httpversion: init.httpversion, proxy: proxy, headers: headers);
                }
            );

            if (stream_links?.qualitys == null || stream_links.qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(stream_links);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        return OnResult(cache);
    }


    public static ValueTask<string> goHost(string host, WebProxy proxy = null)
    {
        if (!Regex.IsMatch(host, "^https?://www\\."))
            return ValueTask.FromResult(host);

        var memoryCache = HybridCache.GetMemory();

        string memkey = $"ebalovo:gohost:{host}";
        if (memoryCache.TryGetValue(memkey, out string _host))
            return ValueTask.FromResult(_host);

        return goHostAsync(memoryCache, memkey, host, proxy);
    }

    async static ValueTask<string> goHostAsync(IMemoryCache memoryCache, string memkey, string host, WebProxy proxy)
    {
        const string backhost = "https://web.epalovo.com";

        string _host = await Http.GetLocation(host, timeoutSeconds: 5, proxy: proxy, allowAutoRedirect: true);
        if (_host != null && !Regex.IsMatch(_host, "^https?://www\\."))
        {
            _host = Regex.Replace(_host, "/$", "");
            memoryCache.Set(memkey, _host, DateTime.Now.AddHours(1));
            return _host;
        }
        else
        {
            memoryCache.Set(memkey, backhost, DateTime.Now.AddMinutes(20));
            return backhost;
        }
    }

    static List<ModelItem> FilterCurrentModels(string current, IEnumerable<ModelItem> models)
    {
        if (models == null)
            return null;

        var filtered = new List<ModelItem>();
        foreach (var model in models)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.name) || string.IsNullOrWhiteSpace(model.uri))
                continue;

            if (!IsSameCurrentModel(current, model) && !filtered.Any(i => string.Equals(ExtractModelHref(i), ExtractModelHref(model), StringComparison.OrdinalIgnoreCase)))
                filtered.Add(model);
        }

        return filtered.Count == 0 ? null : filtered;
    }

    static bool IsSameCurrentModel(string current, ModelItem item)
    {
        if (string.IsNullOrWhiteSpace(current) || item == null)
            return false;

        string modelHref = ExtractModelHref(item)?.Trim('/');
        return !string.IsNullOrEmpty(modelHref) && modelHref.Equals(current.Trim('/'), StringComparison.OrdinalIgnoreCase);
    }

    static string ExtractModelHref(ModelItem item)
    {
        string value = Regex.Match(item?.uri ?? string.Empty, "model=([^&]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        return string.IsNullOrEmpty(value) ? null : WebUtility.UrlDecode(value);
    }

    static string NormalizeSitePath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        return Regex.Replace(uri, "^https?://[^/]+/", string.Empty, RegexOptions.IgnoreCase).TrimStart('/');
    }
}
