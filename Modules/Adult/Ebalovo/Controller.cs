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
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Ebalovo;

public class EbalovoController : BaseSisiController
{
    public EbalovoController() : base(ModInit.conf) { }

    [HttpGet]
    [Staticache]
    [Route("elo")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"elo:{search}:{sort}:{c}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            string ehost = await goHost(init.host, proxy);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(EbalovoTo.Uri(ehost, search, sort, c, pg), span =>
            {
                playlists = EbalovoTo.Playlist("elo/vidosik", span);
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
            string.IsNullOrEmpty(search) ? EbalovoTo.Menu(host, sort, c) : null
        );
    }


    [HttpGet]
    [Route("elo/vidosik")]
    async public Task<ActionResult> Index(string uri, bool related)
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


    async public static ValueTask<string> goHost(string host, WebProxy proxy = null)
    {
        if (!Regex.IsMatch(host, "^https?://www\\."))
            return host;

        var memoryCache = HybridCache.GetMemory();
        string backhost = "https://web.epalovo.com";

        string memkey = $"ebalovo:gohost:{host}";
        if (memoryCache.TryGetValue(memkey, out string _host))
            return _host;

        _host = await Http.GetLocation(host, timeoutSeconds: 5, proxy: proxy, allowAutoRedirect: true);
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
}
