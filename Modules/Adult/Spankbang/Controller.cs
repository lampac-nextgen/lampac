using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Spankbang;

public class SpankbangController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public SpankbangController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet]
    [Staticache]
    [Route("sbg")]
    async public Task<ActionResult> Index(string search, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"sbg:{search}:{sort}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            string url = SpankbangTo.Uri(init.host, search, sort, pg);

            List<PlaylistItem> playlists = null;

            if (rch?.enable == true || init.priorityBrowser == "http")
            {
                await httpHydra.GetSpan(url, span =>
                {
                    playlists = SpankbangTo.Playlist("sbg/vidosik", span);
                });
            }
            else
            {
                var headers = httpHeaders(init);

                string html = await PlaywrightBrowser.Get(
                    init,
                    init.cors(url, headers, requestInfo),
                    headers,
                    proxy_data
                );

                playlists = SpankbangTo.Playlist("sbg/vidosik", html);
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache,
            string.IsNullOrEmpty(search) ? SpankbangTo.Menu(host, sort) : null
        );
    }

    [HttpGet]
    [Staticache]
    [Route("sbg/vidosik")]
    async public Task<ActionResult> Index(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"spankbang:view:{uri}", 20, jsonContext.StreamItem, async e =>
        {
            string url = SpankbangTo.StreamLinksUri(init.host, uri);
            if (url == null)
                return e.Fail("uri");

            StreamItem stream_links = null;

            if (rch?.enable == true || init.priorityBrowser == "http")
            {
                await httpHydra.GetSpan(url, span =>
                {
                    stream_links = SpankbangTo.StreamLinks("sbg/vidosik", span);
                });
            }
            else
            {
                var headers = httpHeaders(init);

                string html = await PlaywrightBrowser.Get(init,
                    init.cors(url, headers, requestInfo),
                    headers,
                    proxy_data
                );

                stream_links = SpankbangTo.StreamLinks("sbg/vidosik", html);
            }

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
