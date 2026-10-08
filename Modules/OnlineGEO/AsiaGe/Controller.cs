using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.HTML;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace AsiaGe;

public class AsiaGeController : BaseOnlineController
{
    public AsiaGeController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/asiage")]
    async public Task<ActionResult> Index(long id, string title, short year, byte serial = 0, string href = null, bool similar = false)
    {
        if (serial == 0)
            return OnError();

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        #region search
        if (string.IsNullOrEmpty(href))
        {
            string searchTitle = similar ? title : null;

            if (searchTitle == null)
            {
                var tmdbName = await InvokeCacheResult<string>($"asiage:tmdb:{id}", 60 * 24, async e =>
                {
                    var result = await Http.Get<JObject>($"http://api.themoviedb.org/3/tv/{id}?api_key={CoreInit.conf.cub.api_key}&language=en", timeoutSeconds: 5);
                    if (result != null)
                        return e.Success(result.Value<string>("name"));

                    return e.Fail("tmdb");
                });

                searchTitle = tmdbName.Value;
            }

            if (string.IsNullOrWhiteSpace(searchTitle))
                return OnError();

        rhubSearchFallback:

            // В кэше только данные сайта, без host: host свой у каждого клиента
            // (http://ip:9118 или https://домен за прокси) и подставляется ниже.
            var search = await InvokeCacheResult<List<SearchItem>>($"asiage:search:v2:{searchTitle}:{year}", TimeSpan.FromHours(4), async e =>
            {
                var results = new List<SearchItem>();

                await httpHydra.GetSpan($"{init.host}/index.php?do=search&subaction=search&search_start=0&full_search=0&story={HttpUtility.UrlEncode(searchTitle)}", html =>
                {
                    foreach (ReadOnlySpan<char> row in HtmlSpan.Nodes(html, "div", "class", "movie-item", HtmlSpanTargetType.Exact))
                    {
                        string link = Rx.Match(row, "href=\"https?://[^/]+/([^\"]+\\.html)\"");

                        // Название: прежняя вёрстка (mob-titl-3) или нынешняя (gb-filmisvidi-3,
                        // после названия год ссылкой «(<a …>2025</a>)» или пустые «()»).
                        string name = Rx.Match(row, "<div class=\"mob-titl-3\">([^<]+)</div>")
                            ?? Rx.Match(row, "<div class=\"gb-filmisvidi-3\">\\s*([^<]+?)\\s*(?:\\(<a|\\(\\s*\\)|</div>)");

                        if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(name))
                            continue;

                        results.Add(new SearchItem()
                        {
                            name = name,
                            year = Rx.Match(row, "/year/([0-9]+)/\"") ?? string.Empty,
                            href = link
                        });
                    }
                });

                if (results.Count == 0)
                    return e.Fail("search", refresh_proxy: true);

                return e.Success(results);
            });

            if (IsRhubFallback(search))
                goto rhubSearchFallback;

            if (!search.IsSuccess)
                return OnError(search.ErrorMsg);

            if (string.IsNullOrWhiteSpace(href))
            {
                var stpl = new SimilarTpl(search.Value.Count);

                foreach (var item in search.Value)
                    stpl.Append(item.name, item.year, string.Empty, $"{host}/lite/asiage?title={HttpUtility.UrlEncode(title)}&year={year}&serial={serial}&href={HttpUtility.UrlEncode(item.href)}");

                return ContentTpl(stpl);
            }
        }
        #endregion

    rhubFallback:

        var cache = await InvokeCacheResult<List<(string title, string s, string ep, string file)>>($"asiage:view:{href}", 40, async e =>
        {
            List<(string title, string s, string ep, string file)> episodes = null;

            await httpHydra.GetSpan($"{init.host}/{href}", html =>
            {
                ReadOnlySpan<char> json = Rx.Slice(html, "file:", "\"vast\"").Trim()[..^1];

                var root = JsonSerializer.Deserialize<SerialModel[]>(json, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                });

                if (root != null && root.Length > 0)
                {
                    episodes = new();

                    foreach (var item in root)
                    {
                        string epFile = item.download;
                        if (string.IsNullOrEmpty(epFile))
                            epFile = Regex.Match(item.file, "\\{[^}]+\\}([^;,]+)").Groups[1].Value;

                        if (!string.IsNullOrEmpty(epFile))
                        {
                            string ep = Regex.Match(item.title, "([0-9]+)").Groups[1].Value;
                            string s = Rx.Match(html, ">სეზონების რაოდენობა:</b>([^<]+)<")?.Trim() ?? "1";

                            episodes.Add((item.title, s, ep, epFile));
                        }
                    }
                }
            });

            if (episodes == null)
                return e.Fail("episodes", refresh_proxy: true);

            return e.Success(episodes);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return ContentTpl(cache, () =>
        {
            var etpl = new EpisodeTpl();

            foreach (var episode in cache.Value)
            {
                etpl.Append(
                    episode.title,
                    title,
                    episode.s,
                    episode.ep,
                    HostStreamProxy(episode.file),
                    vast: init.vast
                );
            }

            return etpl;
        });
    }
}
