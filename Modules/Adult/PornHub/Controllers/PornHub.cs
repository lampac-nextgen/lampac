using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PornHub;

public class PornHubController : BaseSisiController
{
    const int StreamCacheMinutes = 1;
    const int StreamProbeAttempts = 3;

    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client(useCookies: false);
    static readonly IReadOnlyList<HeadersModel> streamPageHeaders = HeadersModel.Init(
        ("user-agent", Http.UserAgent),
        ("cookie", "platform=pc; accessAgeDisclaimerPH=1"),
        ("sec-fetch-dest", "document"),
        ("sec-fetch-site", "same-origin"),
        ("sec-fetch-mode", "navigate")
    );

    public PornHubController() : base(ModInit.conf.PornHub)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache(11)]
    [Route("phub"), Route("phubgay"), Route("phubsml")]
    async public Task<ActionResult> Index(string search, string model, string sort, int c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string plugin = Regex.Match(HttpContext.Request.Path.Value, "^/([a-z]+)").Groups[1].Value;

        SemaphorManager semaphore = null;
        string semaphoreKey = $"{plugin}:list:v2:{search}:{model}:{sort}:{c}:{pg}";

        PlaylistAndPage cache = null;
        HybridCacheEntry<PlaylistAndPage> entryCache;

        try
        {
        reset: // http запросы последовательно
            if (rch?.enable != true)
            {
                semaphore ??= new SemaphorManager(semaphoreKey, TimeSpan.FromSeconds(30));
                bool _acquired = await semaphore.WaitAsync();
                if (!_acquired)
                    return OnError();
            }

            entryCache = await hybridCache.EntryAsync(semaphoreKey, jsonContext.PlaylistAndPage);

            // fallback cache
            if (!entryCache.success)
            {
                string userKey = headerKeys(semaphoreKey, "accept");

                bool next = rch == null;
                if (!next)
                {
                    // user cache разделенный по ip
                    entryCache = await hybridCache.EntryAsync(userKey, jsonContext.PlaylistAndPage);
                    if (entryCache.success)
                        StatiCacheDisabled = true;

                    next = !entryCache.success;
                }

                if (next)
                {
                    string uri = PornHubTo.Uri(init.host, plugin, search, model, sort, c, null, pg);

                    await httpHydra.GetSpan(uri, span =>
                    {
                        cache = new PlaylistAndPage(
                            PornHubTo.Pages(span),
                            PornHubTo.Playlist("phub/vidosik", "phub", span, IsModel_page: !string.IsNullOrEmpty(model))
                        );
                    });

                    if (cache?.playlists == null || cache.playlists.Count == 0)
                    {
                        if (IsRhubFallback())
                            goto reset;

                        return OnError("playlists", refresh_proxy: string.IsNullOrEmpty(search));
                    }

                    string memKey = semaphoreKey;

                    if (rch?.enable == true)
                    {
                        memKey = userKey;
                        StatiCacheDisabled = true;
                    }
                    else
                    {
                        proxyManager?.Success();
                    }

                    hybridCache.Set(memKey, cache, cacheTime(10));
                }
            }
        }
        finally
        {
            semaphore?.Release();
        }

        if (cache == null)
            cache = entryCache.value;

        return PlaylistResult(
            cache.playlists,
            entryCache.singleCache,
            string.IsNullOrEmpty(model) ? PornHubTo.Menu(host, plugin, search, sort, c) : null,
            total_pages: cache.total_pages
        );
    }


    [HttpGet]
    [Route("phub/model")]
    async public Task<ActionResult> Model(string vkey, string current)
    {
        if (await IsRequestBlocked(rch_check: false))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(vkey))
            return Json(new { model = default(ModelItem) });

        string memKey = $"phub:model:v1:{vkey}";
        if (!hybridCache.TryGetValue(memKey, out List<ModelItem> models))
        {
            string url = PornHubTo.StreamLinksUri(init.host, vkey);
            if (url == null)
                return Json(new { model = default(ModelItem) });

            var headers = httpHeaders(init, HeadersModel.Init(
                ("user-agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Safari/604.1"),
                ("cookie", "platform=mobile; accessAgeDisclaimerPH=1"),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-mode", "navigate")
            ));

            string html = await Http.Get(init.cors(url, headers, requestInfo), timeoutSeconds: 8, proxy: proxy, httpversion: init.httpversion, headers: headers);
            models = PornHubTo.Models("phub", html);

            if (models is not { Count: > 0 })
                return Json(new { model = default(ModelItem) });

            hybridCache.Set(memKey, models, cacheTime(24 * 60));
        }

        models = PornHubTo.FilterCurrentModels(current, models);
        return Json(new { model = models is { Count: > 0 } ? models[0] : default, models });
    }


    [HttpGet, Staticache(manually: true)]
    [Route("phub/vidosik")]
    async public Task<ActionResult> Vidosik(string vkey, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"phub:vidosik:v5:{vkey}", StreamCacheMinutes, jsonContext.StreamItem, async e =>
        {
            string url = PornHubTo.StreamLinksUri(init.host, vkey);
            if (url == null)
                return e.Fail("vkey");

            StreamItem stream_links = null;

            for (int i = 0; i < StreamProbeAttempts; i++)
            {
                await Http.GetSpan(
                    init.cors(url, streamPageHeaders, requestInfo),
                    span => stream_links = PornHubTo.StreamLinks(span, "phub/vidosik", "phub"),
                    timeoutSeconds: init.httptimeout,
                    headers: streamPageHeaders,
                    proxy: proxy,
                    statusCodeOK: true,
                    httpversion: init.httpversion,
                    useDefaultHeaders: false,
                    httpClient: init.httpversion == 2 && !init.useproxy ? http2Client : null
                );

                if (stream_links?.qualitys != null && stream_links.qualitys.Count > 0 && !HasLegacySignedUrls(stream_links) && await IsPlayableStream(stream_links))
                    return e.Success(stream_links);
            }

            if (stream_links?.qualitys == null || stream_links.qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Fail("stream_probe", refresh_proxy: true);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        return OnResult(cache);
    }

    async Task<bool> IsPlayableStream(StreamItem streamLinks)
    {
        string streamUrl = null;

        foreach (var quality in streamLinks.qualitys)
        {
            streamUrl = quality.Value;
            break;
        }

        if (string.IsNullOrEmpty(streamUrl))
            return false;

        var headers = HeadersModel.InitOrNull(init.headers_stream);
        string requestUri = init.cors(streamUrl, headers, requestInfo);

        var client = FriendlyHttp.MessageClient(
            init.httpversion == 2 ? "http2" : "base",
            Http.HandlerOrNull(requestUri, proxy),
            out bool disposeHttpClient,
            httpClient: init.httpversion == 2 && !init.useproxy ? http2Client : null
        );

        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Head, requestUri))
            {
                Http.DefaultRequestHeaders(requestUri, request, null, null, headers);

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                    {
                        return (int)response.StatusCode is 200 or 206;
                    }
                }
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }

    static bool HasLegacySignedUrls(StreamItem streamLinks)
    {
        foreach (var quality in streamLinks.qualitys)
        {
            string url = quality.Value;

            if (string.IsNullOrEmpty(url) || url.Contains("validto=", StringComparison.Ordinal))
                continue;

            if (url.Contains("?e=", StringComparison.Ordinal) || url.Contains("&e=", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
