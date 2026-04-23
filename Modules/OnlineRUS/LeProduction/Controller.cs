using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services.RxEnumerate;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace LeProduction
{
    public class LeProductionController : BaseOnlineController
    {
        public LeProductionController() : base(ModInit.conf) { }

        [HttpGet]
        [Staticache]
        [Route("lite/leproduction")]
        async public Task<ActionResult> Index(string title, string original_title, int clarification, int serial = 0, int s = -1, bool rjson = false, string href = null)
        {
            if (await IsRequestBlocked(rch: true))
                return badInitMsg;

            rhubFallback:

            #region search
            if (string.IsNullOrEmpty(href))
            {
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))
                    return OnError();

                var search = await InvokeCacheResult<(string href, SimilarTpl similar, List<LeProductionSearchItem> seasons)>($"leproduction:search:{serial}:{clarification}:{title}:{original_title}", TimeSpan.FromHours(4), async e =>
                {
                    string newsHref = null;
                    var similar = new SimilarTpl();
                    var seasons = new List<LeProductionSearchItem>();
                    string contentType = serial == 1 ? "serial" : "film";
                    var seenHrefs = new HashSet<string>();
                    var searchTitles = new List<string>();

                    void addSearchTitle(string value)
                    {
                        if (!string.IsNullOrWhiteSpace(value) && !searchTitles.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                            searchTitles.Add(value);
                    }

                    if (clarification == 1)
                    {
                        addSearchTitle(title);
                        addSearchTitle(original_title);
                    }
                    else
                    {
                        addSearchTitle(original_title);
                        addSearchTitle(title);
                    }

                    if (searchTitles.Count == 0)
                        return e.Fail("searchTitle");

                    foreach (var searchTitle in searchTitles)
                    {
                        string queryFirstHref = null;
                        int queryResultCount = 0;
                        string searchUrl = $"{init.host}/index.php?do=search&subaction=search&search_start=0&full_search=0&result_from=1&story={HttpUtility.UrlEncode(searchTitle)}";
                        await httpHydra.GetSpan(searchUrl, spanAction: html =>
                        {
                            string stitle = StringConvert.SearchName(title);
                            string soriginal = StringConvert.SearchName(original_title);

                            var rx = Rx.Matches($"<a\\s+href=\"https?://[^/]+/({contentType}/[0-9]+-[^\"]+\\.html)\"[^>]*>([^<]+)</a>", html, options: RegexOptions.IgnoreCase);

                            foreach (var row in rx.Rows())
                            {
                                var g = row.Groups();
                                string itemHref = g[1].Value;
                                string itemTitle = Regex.Replace(g[2].Value, "<[^>]*>", string.Empty).Trim();

                                if (string.IsNullOrWhiteSpace(itemHref) || string.IsNullOrWhiteSpace(itemTitle))
                                    continue;

                                queryResultCount++;
                                queryFirstHref ??= itemHref;

                                if (seenHrefs.Add(itemHref))
                                {
                                    string encTitle = HttpUtility.UrlEncode(title);
                                    string encOriginalTitle = HttpUtility.UrlEncode(original_title);
                                    string encHref = HttpUtility.UrlEncode(itemHref);
                                    string link = $"{host}/lite/leproduction?title={encTitle}&original_title={encOriginalTitle}&clarification={clarification}&serial={serial}&href={encHref}";
                                    similar.Append(itemTitle, string.Empty, string.Empty, link, string.Empty);
                                }

                                string normalized = StringConvert.SearchName(itemTitle);
                                if (newsHref == null && (normalized.Contains(stitle) || (!string.IsNullOrWhiteSpace(soriginal) && normalized.Contains(soriginal))))
                                    newsHref = itemHref;

                                if (serial == 1 && normalized.Contains(stitle))
                                {
                                    int season = 1;
                                    var seasonMatch = Regex.Match(itemTitle, "(?<season>\\d+)\\s*сезон", RegexOptions.IgnoreCase);
                                    if (seasonMatch.Success)
                                        season = int.Parse(seasonMatch.Groups["season"].Value);

                                    if (!seasons.Any(i => i.season == season || i.href == itemHref))
                                        seasons.Add(new LeProductionSearchItem { season = season, href = itemHref, title = itemTitle });
                                }
                            }
                        });

                        if (newsHref == null
                            && serial != 1
                            && !string.IsNullOrWhiteSpace(original_title)
                            && string.Equals(searchTitle, original_title, StringComparison.OrdinalIgnoreCase)
                            && queryResultCount == 1
                            && !string.IsNullOrWhiteSpace(queryFirstHref))
                        {
                            newsHref = queryFirstHref;
                        }
                    }

                    if (newsHref == null && similar.Length > 0)
                        return e.Success((null, similar, seasons));

                    if (string.IsNullOrWhiteSpace(newsHref))
                        return e.Fail("search", refresh_proxy: true);

                    return e.Success((newsHref, similar, seasons));
                });

                if (!search.IsSuccess)
                {
                    if (IsRhubFallback(search))
                        goto rhubFallback;

                    return OnError(search.ErrorMsg);
                }

                if (search.Value.href == null)
                    return ContentTpl(search.Value.similar);

                if (serial == 1 && s == -1 && search.Value.seasons?.Count > 1)
                {
                    var tpl = new SeasonTpl(search.Value.seasons.Count);
                    string encTitle = HttpUtility.UrlEncode(title);
                    string encOriginalTitle = HttpUtility.UrlEncode(original_title);

                    foreach (var seasonItem in search.Value.seasons.OrderBy(i => i.season))
                    {
                        string encHref = HttpUtility.UrlEncode(seasonItem.href);
                        tpl.Append($"{seasonItem.season} сезон", $"{host}/lite/leproduction?rjson={rjson}&serial=1&title={encTitle}&original_title={encOriginalTitle}&clarification={clarification}&href={encHref}", seasonItem.season);
                    }

                    return ContentTpl(tpl);
                }

                href = search.Value.href;
            }
            #endregion

            var cache = await InvokeCacheResult<string>($"leproduction:view:{href}", 20, async e =>
            {
                string iframe = null;
                await httpHydra.GetSpan($"{init.host}/{href}", spanAction: html =>
                {
                    iframe = Rx.Match(html, "<iframe[^>]+id=\"omfg[0-9]*\"[^>]+src=\"([^\"]+)\"", options: RegexOptions.IgnoreCase);
                    if (string.IsNullOrWhiteSpace(iframe))
                        iframe = Rx.Match(html, "<iframe[^>]+src=\"(https?://[^\"]+)\"", options: RegexOptions.IgnoreCase);
                    if (string.IsNullOrWhiteSpace(iframe))
                        iframe = Rx.Match(html, "<select[^>]+id=\"selectFilm[0-9]*\"[\\s\\S]*?<option[^>]+value=\"([^\"]+)\"", options: RegexOptions.IgnoreCase);
                });

                if (string.IsNullOrWhiteSpace(iframe))
                    return e.Fail("iframe", refresh_proxy: true);

                string fileBlock = null;

                await httpHydra.GetSpan(iframe, addheaders: HeadersModel.Init("referer", $"{init.host}/{href}"), spanAction: html =>
                {
                    fileBlock = Rx.Match(html, "file\\s*:\\s*\"([^\"]*\\[[0-9]+p\\][^\"]+)\"", options: RegexOptions.IgnoreCase);
                    if (string.IsNullOrWhiteSpace(fileBlock))
                        fileBlock = Rx.Match(html, "file\\s*:\\s*(\\[[\\s\\S]*?\\])\\s*,\\s*(?:embed|url|default_quality)\\s*:", options: RegexOptions.IgnoreCase);
                });

                if (string.IsNullOrWhiteSpace(fileBlock))
                    return e.Fail("fileBlock", refresh_proxy: true);

                return e.Success(fileBlock);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            return ContentTpl(cache, () =>
            {
                if (serial == 1)
                {
                    var episodes = ParseEpisodes(cache.Value);
                    if (episodes.Count == 0)
                        return BuildMovie(title, original_title, cache.Value);

                    if (s == -1)
                    {
                        var seasons = episodes
                            .Select(i => i.season <= 0 ? 1 : i.season)
                            .Distinct()
                            .OrderBy(i => i)
                            .ToList();

                        if (seasons.Count == 1)
                            return BuildEpisodeTpl(title, original_title, episodes, seasons[0]);

                        var tpl = new SeasonTpl(seasons.Count);
                        string encTitle = HttpUtility.UrlEncode(title);
                        string encOriginalTitle = HttpUtility.UrlEncode(original_title);
                        string encHref = HttpUtility.UrlEncode(href);

                        foreach (int season in seasons)
                            tpl.Append($"{season} сезон", $"{host}/lite/leproduction?rjson={rjson}&serial=1&title={encTitle}&original_title={encOriginalTitle}&clarification={clarification}&href={encHref}&s={season}", season);

                        return tpl;
                    }

                    return BuildEpisodeTpl(title, original_title, episodes, s);
                }

                return BuildMovie(title, original_title, cache.Value);
            });
        }

        MovieTpl BuildMovie(string title, string original_title, string fileBlock)
        {
            var mtpl = new MovieTpl(title, original_title, 1);
            var streamQuality = ParseStreamQuality(fileBlock);
            if (!streamQuality.Any())
                return null;

            var first = streamQuality.Firts();
            mtpl.Append("По умолчанию", first.link, quality: first.quality, streamquality: streamQuality, vast: init.vast);
            return mtpl;
        }

        EpisodeTpl BuildEpisodeTpl(string title, string original_title, List<LeProductionEpisode> episodes, int season)
        {
            var selected = episodes
                .Where(i => (i.season <= 0 ? 1 : i.season) == season)
                .OrderBy(i => i.episode)
                .ThenBy(i => i.comment)
                .ToList();

            if (selected.Count == 0)
                return null;

            var etpl = new EpisodeTpl(selected.Count);
            string seasonNum = season.ToString();

            foreach (var item in selected)
            {
                var streamQuality = ParseStreamQuality(item.file);
                if (!streamQuality.Any())
                    continue;

                var first = streamQuality.Firts();
                string episodeNum = (item.episode <= 0 ? etpl.Length + 1 : item.episode).ToString();
                string episodeTitle = item.episode > 0 ? $"{item.episode} серия" : item.comment;
                etpl.Append(episodeTitle, title ?? original_title, seasonNum, episodeNum, first.link, streamquality: streamQuality, vast: init.vast);
            }

            return etpl.IsEmpty ? null : etpl;
        }

        List<LeProductionEpisode> ParseEpisodes(string fileBlock)
        {
            var episodes = new List<LeProductionEpisode>();

            foreach (Match m in Regex.Matches(fileBlock, "\"comment\"\\s*:\\s*\"(?<comment>[^\"]+)\"\\s*,\\s*\"file\"\\s*:\\s*\"(?<file>[^\"]+)\"", RegexOptions.IgnoreCase))
            {
                string comment = HttpUtility.HtmlDecode(m.Groups["comment"].Value)?.Trim();
                string file = m.Groups["file"].Value?.Trim();

                if (string.IsNullOrWhiteSpace(comment) || string.IsNullOrWhiteSpace(file))
                    continue;

                int season = 1;
                int episode = 0;

                var match = Regex.Match(comment, "(?<season>\\d+)\\s*сезон\\s*(?<episode>\\d+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    season = int.Parse(match.Groups["season"].Value);
                    episode = int.Parse(match.Groups["episode"].Value);
                }
                else
                {
                    match = Regex.Match(comment, "(?<episode>\\d+)\\s*(?:серия|эпизод)", RegexOptions.IgnoreCase);
                    if (match.Success)
                        episode = int.Parse(match.Groups["episode"].Value);
                }

                episodes.Add(new LeProductionEpisode
                {
                    season = season,
                    episode = episode,
                    comment = comment,
                    file = file
                });
            }

            return episodes;
        }

        StreamQualityTpl ParseStreamQuality(string fileBlock)
        {
            var streamQuality = new StreamQualityTpl();
            var parsed = new List<(string link, string quality)>(8);

            foreach (Match m in Regex.Matches(fileBlock, "\\[([0-9]+p)\\](https?://[^,\\[\"\t\n ]+)", RegexOptions.IgnoreCase))
            {
                string link = HostStreamProxy(m.Groups[2].Value);
                string quality = m.Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(link) && !string.IsNullOrWhiteSpace(quality))
                    parsed.Add((link, quality));
            }

            if (parsed.Count > 0)
            {
                foreach (var item in parsed
                    .GroupBy(i => i.quality, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(i => QualityPriority(i.quality))
                    .ThenByDescending(i => QualityNumeric(i.quality)))
                {
                    streamQuality.Append(item.link, item.quality);
                }
            }

            if (!streamQuality.Any())
            {
                string single = Regex.Match(fileBlock, "(https?://[^,\\[\"\t\n ]+)", RegexOptions.IgnoreCase).Value;
                if (!string.IsNullOrWhiteSpace(single))
                    streamQuality.Append(HostStreamProxy(single), "1080p");
            }

            return streamQuality;
        }

        static int QualityPriority(string quality)
        {
            return quality?.ToLowerInvariant() switch
            {
                "1080p" => 0,
                "2160p" => 1,
                "1440p" => 2,
                "720p" => 3,
                "480p" => 4,
                "360p" => 5,
                "240p" => 6,
                _ => 10
            };
        }

        static int QualityNumeric(string quality)
        {
            var match = Regex.Match(quality ?? string.Empty, "(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        class LeProductionEpisode
        {
            public int season { get; set; }
            public int episode { get; set; }
            public string comment { get; set; }
            public string file { get; set; }
        }

        class LeProductionSearchItem
        {
            public int season { get; set; }
            public string href { get; set; }
            public string title { get; set; }
        }
    }
}
