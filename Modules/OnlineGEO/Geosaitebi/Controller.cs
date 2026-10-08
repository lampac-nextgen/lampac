using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using Shared;
using Shared.Models.Templates;
using Shared.Services.HTML;
using Microsoft.AspNetCore.Mvc;
using Shared.Attributes;
using Shared.Services.RxEnumerate;

namespace Geosaitebi;

public class GeosaitebiController : BaseOnlineController
{
    public GeosaitebiController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/geosaitebi")]
    async public Task<ActionResult> Index(string title, string original_title, short year, byte serial, string href = null, bool similar = false)
    {
        if (serial == 1)
            return OnError();

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        #region search
        if (string.IsNullOrEmpty(href))
        {
            string searchTitle = similar ? title : (original_title ?? title);
            if (string.IsNullOrWhiteSpace(searchTitle))
                return OnError();

        rhubSearchFallback:

            // В кэше только данные сайта, без host: host свой у каждого клиента
            // (http://ip:9118 или https://домен за прокси) и подставляется ниже.
            var cache = await InvokeCacheResult<List<SearchItem>>($"geosaitebi:search:v2:{searchTitle}:{year}", TimeSpan.FromHours(4), async e =>
            {
                var results = new List<SearchItem>();

                await httpHydra.GetSpan($"{init.host}/index.php?do=search&subaction=search&search_start=0&full_search=0&story={HttpUtility.UrlEncode(searchTitle)}", search =>
                {
                    foreach (var row in HtmlSpan.Nodes(search, "div", "class", "tu-cat-6", HtmlSpanTargetType.Exact))
                    {
                        var g = Rx.Groups(row, "class=\"tu-cat-16\"><a href=\"https?://[^/]+/([^\"]+)\">([^\n\r<]+)");

                        string href = g[1].Value;
                        string name = g[2].Value.Trim();
                        if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(name))
                            continue;

                        results.Add(new SearchItem()
                        {
                            name = name,
                            year = Rx.Match(row, "<span class=\"w-v-d-2\">([0-9]+)</span>") ?? string.Empty,
                            href = href
                        });
                    }
                });

                if (results.Count == 0)
                    return e.Fail("search", refresh_proxy: true);

                return e.Success(results);
            });

            if (IsRhubFallback(cache))
                goto rhubSearchFallback;

            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            if (string.IsNullOrWhiteSpace(href))
            {
                var stpl = new SimilarTpl(cache.Value.Count);

                foreach (var item in cache.Value)
                {
                    stpl.Append(
                        item.name,
                        item.year,
                        string.Empty,
                        $"{host}/lite/geosaitebi?title={HttpUtility.UrlEncode(item.name ?? title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(item.href)}"
                    );
                }

                return ContentTpl(stpl);
            }
        }
        #endregion

    rhubFallback:

        var movie = await InvokeCacheResult<string>($"geosaitebi:movie:{href}", 60, async e =>
        {
            string hls = null;

            await httpHydra.GetSpan($"{init.host}/{href}", html =>
            {
                hls = Rx.Match(html, "file: ?\"([^\"]+)\"");
            });

            if (hls == null)
                return e.Fail("hls", refresh_proxy: true);

            return e.Success(hls);
        });

        if (IsRhubFallback(movie))
            goto rhubFallback;

        return ContentTpl(movie, () =>
        {
            var mtpl = new MovieTpl(title, original_title);

            mtpl.Append(
                title ?? "ქართული",
                HostStreamProxy(movie.Value),
                voice_name: "ქართული",
                headers: httpHeaders(init.host, init.headers_stream),
                vast: init.vast
            );

            return mtpl;
        });
    }
}
