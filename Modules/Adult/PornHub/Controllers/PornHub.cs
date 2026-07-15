using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PornHub;

public class PornHubController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client(useCookies: false);

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
        string semaphoreKey = $"{plugin}:list:{search}:{model}:{sort}:{c}:{pg}";

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


    [HttpGet, Staticache(manually: true)]
    [Route("phub/vidosik")]
    async public Task<ActionResult> Vidosik(string vkey, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"phub:vidosik:{vkey}"), 20, jsonContext.StreamItem, async e =>
        {
            string url = PornHubTo.StreamLinksUri(init.host, vkey);
            if (url == null)
                return e.Fail("vkey");

            StreamItem stream_links = null;

            await httpHydra.GetSpan(url, span =>
            {
                stream_links = PornHubTo.StreamLinks(span, "phub/vidosik", "phub");
            });

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
}
