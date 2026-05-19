using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services.HTML;
using Shared.Services.Utilities;
using Microsoft.AspNetCore.Mvc;
using Shared.Services.RxEnumerate;

namespace Kinoflix;

public class KinoflixController : BaseOnlineController
{
    public KinoflixController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/kinoflix")]
    async public Task<ActionResult> Index(string title, string original_title, int clarification, int year, string href, string t = null, int s = -1)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:

        #region search
        if (string.IsNullOrEmpty(href))
        {
            string searchTitle = clarification == 1 ? title : (original_title ?? title);
            if (string.IsNullOrWhiteSpace(searchTitle))
                return OnError();

            var search = await InvokeCacheResult<SearchModel>($"kinoflix:search:{searchTitle}", TimeSpan.FromHours(4), async e =>
            {
                var card = await Search(searchTitle, title, original_title, year);
                if (card == null)
                    return e.Fail("search", refresh_proxy: true);

                return e.Success(card);
            });

            if (!search.IsSuccess)
            {
                if (IsRhubFallback(search))
                    goto rhubFallback;

                return OnError(search.ErrorMsg);
            }

            if (search.Value.link == null)
                return ContentTpl(search.Value.similar);

            href = search.Value.link;
        }
        #endregion

        #region news
        var news = await InvokeCacheResult<string>($"kinoflix:{href}", TimeSpan.FromHours(4), async e =>
        {
            string iframeUri = null;
            await httpHydra.GetSpan($"{init.host}/{href}", spanAction: html =>
            {
                iframeUri = Rx.Match(html, "<iframe src=\"([^\"]+)\"");
            });

            if (string.IsNullOrWhiteSpace(iframeUri))
                return e.Fail("iframeUri", refresh_proxy: true);

            return e.Success(iframeUri);
        });

        if (IsRhubFallback(news))
            goto rhubFallback;

        if (!news.IsSuccess)
            return OnError(news.ErrorMsg);
        #endregion

        #region embed
        var cache = await InvokeCacheResult<EmbedModel>(ipkey($"kinoflix:{news.Value}"), 20, textJson: true, onget: async e =>
        {
            string embedUri = null;
            await httpHydra.GetSpan(news.Value, spanAction: iframeHtml =>
            {
                embedUri = Rx.Match(iframeHtml, "<iframe id=\"[a-z]+_embed\" src=\"([^\"]+)\"");
            });

            if (embedUri != null)
            {
                string playlistUri = null;
                await httpHydra.GetSpan(embedUri, spanAction: iframeHtml =>
                {
                    playlistUri = Rx.Match(iframeHtml, "file: ?\"([^\"]+)\"");
                });

                if (playlistUri != null)
                {
                    var root = await httpHydra.Get<List<PlayerModel>>(playlistUri);
                    if (root != null && root.Count > 0)
                    {
                        return e.Success(new EmbedModel()
                        {
                            referer = embedUri,
                            items = root
                        });
                    }
                }
            }

            return e.Fail("iframe", refresh_proxy: true);
        });


        if (IsRhubFallback(cache))
            goto rhubFallback;
        #endregion

        return ContentTpl(cache, () =>
        {
            var items = cache.Value.items;

            var headers_stream = httpHeaders(
                init.host,
                HeadersModel.Init(
                    init.headers_stream,
                    ("referer", cache.Value.referer)
                )
            );

            if (items[0].folder != null)
            {
                #region Сериал
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);
                string enc_href = HttpUtility.UrlEncode(href);

                if (s == -1)
                {
                    var tpl = new SeasonTpl();

                    foreach (var season in items)
                    {
                        string seasonName = season.title;
                        string seasonNum = Regex.Match(seasonName, "([0-9]+)").Groups[1].Value;

                        tpl.Append(
                            seasonName ?? $"{seasonNum} сезон",
                            $"{host}/lite/kinoflix?title={enc_title}&original_title={enc_original_title}&href={enc_href}&s={seasonNum}",
                            seasonNum
                        );
                    }

                    return tpl;
                }
                else
                {
                    string sArhc = s.ToString();

                    var season = items.FirstOrDefault(x => x.title.EndsWith($" {sArhc}"));
                    if (season?.folder == null)
                        return null;

                    var etpl = new EpisodeTpl();

                    foreach (var episode in season.folder)
                    {
                        #region subtitle
                        var subtitles = new SubtitleTpl();

                        if (episode.subtitles != null)
                        {
                            foreach (var sub in episode.subtitles)
                                subtitles.Append(sub.label, HostStreamProxy(sub.file));
                        }
                        #endregion

                        etpl.Append(
                            episode.title,
                            title ?? original_title,
                            sArhc,
                            Regex.Match(episode.title, "([0-9]+)").Groups[1].Value,
                            HostStreamProxy(episode.file + "#.m3u8", headers: headers_stream),
                            headers: headers_stream,
                            subtitles: subtitles,
                            vast: init.vast
                        );
                    }

                    return etpl;
                }
                #endregion
            }
            else
            {
                #region Фильм
                var mtpl = new MovieTpl(title, original_title);

                foreach (var m in items)
                {
                    #region subtitle
                    var subtitles = new SubtitleTpl();

                    if (m.subtitles != null)
                    {
                        foreach (var sub in m.subtitles)
                            subtitles.Append(sub.label, HostStreamProxy(sub.file));
                    }
                    #endregion

                    mtpl.Append(
                        title ?? original_title,
                        HostStreamProxy(m.file + "#.m3u8", headers: headers_stream),
                        headers: headers_stream,
                        subtitles: subtitles,
                        vast: init.vast
                    );
                }

                return mtpl;
                #endregion
            }
        });
    }


    #region Search
    async Task<SearchModel> Search(string query, string title, string original_title, int year)
    {
        SearchModel smd = null;

        await httpHydra.GetSpan($"{init.host}/filter-movies?type=search&search={HttpUtility.UrlEncode(query)}", html =>
        {
            string link = null;
            var similar = new SimilarTpl();

            string stitle = SearchNameTo.Convert(title);
            string soriginal_title = SearchNameTo.Convert(original_title);

            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            foreach (ReadOnlySpan<char> row in HtmlSpan.Nodes(html, "div", "class", "filter_cols", HtmlSpanTargetType.Exact))
            {
                ReadOnlySpan<char> card = HtmlSpan.Node(row, "div", "class", "popular-card__title", HtmlSpanTargetType.Exact);
                if (card.IsEmpty)
                    continue;

                string href = Rx.Match(card, " href=\"https?://[^/]+/([^\"#]+)");
                if (href == null)
                    continue;

                string name = Rx.Match(card, "<p>([^<]+)</p>");
                if (name == null)
                    continue;

                string orig_name = Rx.Match(card, "<span>([^<]+)</span>");
                string _year = Rx.Match(row, "class=\"year\">([0-9]{4})");

                string img = Rx.Match(row, "<img\\s+data-src=\"([^\"]+)\"");
                if (!string.IsNullOrEmpty(img))
                    img = Rx.Match(row, "<img src=\"([^\"]+)\"");

                string _l = $"{host}/lite/kinoflix?title={enc_title}&original_title={enc_original_title}&year={year}&href={HttpUtility.UrlEncode(href)}";
                similar.Append(orig_name != null ? $"{name} / {orig_name}" : name, _year, string.Empty, _l, PosterApi.Size(img));

                bool match = SearchNameTo.Contains(name, stitle)
                    || SearchNameTo.Contains(orig_name, soriginal_title);

                if (match && _year == year.ToString())
                    link = href;
            }

            if (similar.Length > 0)
            {
                smd = new SearchModel()
                {
                    link = link,
                    similar = similar
                };
            }
        });

        return smd;
    }
    #endregion
}
