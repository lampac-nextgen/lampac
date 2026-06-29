using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PornHub;

public class PornHubPremium : BaseSisiController
{
    const int StreamCacheMinutes = 1;

    public PornHubPremium() : base(ModInit.conf.PornHubPremium) { }

    [HttpGet]
    [Route("phubprem")]
    async public Task<ActionResult> Prem(string search, string model, string sort, string hd, int c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string memKey = $"phubprem:list:v2:{search}:{model}:{sort}:{hd}:{pg}";
        if (!hybridCache.TryGetValue(memKey, out (int total_pages, List<PlaylistItem> playlists) cache))
        {
            string uri = PornHubTo.Uri(init.host, "phubprem", search, model, sort, c, hd, pg);

            var headers = httpHeaders(init, HeadersModel.Init("cookie", init.cookie));

            string html = await Http.Get(init.cors(uri, headers, requestInfo), timeoutSeconds: 14, proxy: proxy, httpversion: 2, headers: headers);
            if (html == null)
                return OnError("html", refresh_proxy: string.IsNullOrEmpty(search));

            cache.total_pages = PornHubTo.Pages(html);
            cache.playlists = PornHubTo.Playlist("phubprem/vidosik", "phubprem", html, prem: true);

            if (cache.playlists.Count == 0)
                return OnError("playlists", refresh_proxy: pg > 1 && string.IsNullOrEmpty(search));

            proxyManager?.Success();
            hybridCache.Set(memKey, cache, cacheTime(10));
        }

        return PlaylistResult(
            cache.playlists,
            false,
            string.IsNullOrEmpty(model) ? PornHubTo.Menu(host, "phubprem", search, sort, c, hd) : null,
            total_pages: cache.total_pages
        );
    }

    [HttpGet]
    [Route("phubprem/vidosik")]
    async public Task<ActionResult> Prem(string vkey, bool related)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string memKey = $"phubprem:vidosik:v4:{vkey}";
        if (!hybridCache.TryGetValue(memKey, out StreamItem stream_links))
        {
            string url = PornHubTo.StreamLinksUri(init.host, vkey);
            if (url == null)
                return OnError("vkey", refresh_proxy: false);

            var headers = httpHeaders(init, HeadersModel.Init("cookie", init.cookie));
            string html = await Http.Get(init.cors(url, headers, requestInfo), httpversion: 2, timeoutSeconds: 8, proxy: proxy, headers: headers);

            stream_links = PornHubTo.StreamLinks(html, "phubprem/vidosik", "phubprem");

            if (stream_links?.qualitys == null || stream_links.qualitys.Count == 0)
                return OnError("stream_links", refresh_proxy: true);

            proxyManager?.Success();
            hybridCache.Set(memKey, stream_links, cacheTime(StreamCacheMinutes));
        }

        if (related)
            return PlaylistResult(stream_links?.recomends, false, null, total_pages: 1);

        return OnResult(stream_links);
    }
}
