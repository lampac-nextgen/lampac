using Microsoft.AspNetCore.Http;
using Shared.Services.RxEnumerate;
using System.Text;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Kodik;

public struct KodikInvoke
{
    #region KodikInvoke
    static readonly ConcurrentDictionary<string, string> psingles = new();
    //static readonly IMemoryCache memoryCache = HybridCache.GetMemory();
    readonly IEnumerable<Result> fallbackDatabase;

    string host;
    string apihost, token, videopath;
    bool usehls, cdn_is_working;
    HttpHydra httpHydra;
    Func<string, string> onstreamfile;
    public KodikInvoke(string host, ModuleConf init, string videopath, IEnumerable<Result> fallbackDatabase, HttpHydra httpHydra, Func<string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.apihost = init.apihost;
        this.token = init.token;
        this.videopath = videopath;
        this.fallbackDatabase = fallbackDatabase;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
        this.usehls = init.hls;
        this.cdn_is_working = init.cdn_is_working;
    }
    #endregion

    #region Embed
    async Task addResult(string url, List<Result> results)
    {
        var root = await httpHydra.Get<RootObject>(url, safety: true, textJson: true);
        if (root?.results != null)
        {
            foreach (var res in root.results)
            {
                if (results.FirstOrDefault(i => i.id == res.id) == null)
                    results.Add(res);
            }
        }
    }

    async public Task<List<Result>> Embed(string imdb_id, long kinopoisk_id, int s)
    {
        if (string.IsNullOrEmpty(imdb_id) && kinopoisk_id == 0)
            return null;

        var results = new List<Result>();

        if (!string.IsNullOrWhiteSpace(token))
        {
            string url = $"{apihost}/search?token={token}&limit=100&with_episodes=true";

            if (s > 0)
                url += $"&season={s}";

            if (kinopoisk_id > 0)
                await addResult($"{url}&kinopoisk_id={kinopoisk_id}", results);

            if (!string.IsNullOrWhiteSpace(imdb_id))
                await addResult($"{url}&imdb_id={imdb_id}", results);
        }
        else
        {
            // results = FallbackByIds(imdb_id, kinopoisk_id, s);
        }

        if (results.Count == 0)
            return null;

        return results;
    }


    public async Task<EmbedModel> Embed(string title, string original_title, int clarification)
    {
        try
        {
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(original_title))
                return null;

            List<Result> results = null;

            if (!string.IsNullOrWhiteSpace(token))
            {
                string url = $"{apihost}/search?token={token}&limit=100&title={HttpUtility.UrlEncode(original_title ?? title)}&with_episodes=true&with_material_data=true";

                var root = await httpHydra.Get<RootObject>(url, safety: true, textJson: true);
                if (root?.results != null)
                    results = root.results;
            }

            //if (results == null)
            //    results = FallbackByTitle(title, original_title);

            if (results == null)
                return null;

            var hash = new HashSet<string>(20);
            var stpl = new SimilarTpl(results.Count);
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            foreach (var similar in results)
            {
                string pick = similar.title?.ToLowerAndTrim();
                if (string.IsNullOrEmpty(pick))
                    continue;

                if (hash.Contains(pick))
                    continue;

                hash.Add(pick);

                string name = !string.IsNullOrEmpty(similar.title) && !string.IsNullOrEmpty(similar.title_orig) ? $"{similar.title} / {similar.title_orig}" : (similar.title ?? similar.title_orig);

                string details = similar.translation.title;
                if (similar.last_season > 0)
                    details += $"{SimilarTpl.OnlineSplit} {similar.last_season}й сезон";

                var matd = similar.material_data;
                string img = PosterApi.Size(matd.anime_poster_url ?? matd.drama_poster_url ?? matd.poster_url);
                stpl.Append(
                    name,
                    similar.year?.ToString(),
                    details,
                    host + $"lite/kodik?title={enc_title}&original_title={enc_original_title}&clarification={clarification}&pick={HttpUtility.UrlEncode(pick)}",
                    img
                );
            }

            return new EmbedModel()
            {
                stpl = stpl,
                result = results
            };
        }
        catch { return null; }
    }

    public List<Result> Embed(List<Result> results, string pick)
    {
        var content = new List<Result>(results.Count);

        foreach (var i in results)
        {
            if (i.title == null || i.title.ToLowerAndTrim() != pick)
                continue;

            content.Add(i);
        }

        return content;
    }
    #endregion

    #region VideoParse
    async public Task<List<StreamModel>> VideoParse(string linkhost, string link)
    {
        List<StreamModel> streams = null;

        string player_single = null;
        string domain = null, d_sign = null, pd = null, pd_sign = null, ref_domain = null, ref_sign = null, type = null, hash = null, id = null;

        if (link.StartsWith("//"))
            link = $"https:{link}";

        await httpHydra.GetSpan(link, iframe =>
        {
            player_single = Rx.Match(iframe, "src=\"/(assets/js/app\\.player_[^\"]+\\.js)\"");

            var advertDebug = Rx.Split("advertDebug", iframe);
            if (advertDebug.Count > 1)
            {
                var preview = Rx.Split("preview-icons", advertDebug[1].Span);
                if (preview.Count > 0)
                {
                    string _frame = Regex.Replace(preview[0].ToString(), "[\n\r\t ]+", "");
                    domain = Regex.Match(_frame, "domain=\"([^\"]+)\"").Groups[1].Value;
                    d_sign = Regex.Match(_frame, "d_sign=\"([^\"]+)\"").Groups[1].Value;
                    pd = Regex.Match(_frame, "pd=\"([^\"]+)\"").Groups[1].Value;
                    pd_sign = Regex.Match(_frame, "pd_sign=\"([^\"]+)\"").Groups[1].Value;
                    ref_domain = Regex.Match(_frame, "ref=\"([^\"]+)\"").Groups[1].Value;
                    ref_sign = Regex.Match(_frame, "ref_sign=\"([^\"]+)\"").Groups[1].Value;
                    type = Regex.Match(_frame, "vInfo.type='([^']+)'").Groups[1].Value;
                    hash = Regex.Match(_frame, "vInfo.hash='([^']+)'").Groups[1].Value;
                    id = Regex.Match(_frame, "vInfo.id='([^']+)'").Groups[1].Value;
                }
            }
        });

        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(player_single))
            return null;

        string uri = null;
        if (!psingles.TryGetValue(player_single, out uri))
        {
            await httpHydra.GetSpan($"{linkhost}/{player_single}", playerjs =>
            {
                uri = DecodeUrlBase64(Rx.Match(playerjs, "type:\"POST\",url:atob\\(\"([^\"]+)\"\\)"));
                if (!string.IsNullOrEmpty(uri))
                    psingles.TryAdd(player_single, uri);
            });
        }

        if (string.IsNullOrEmpty(uri))
            return null;

        bool _usehls = usehls;

        await httpHydra.PostSpan($"{linkhost + uri}", $"d={domain}&d_sign={d_sign}&pd={pd}&pd_sign={pd_sign}&ref={ref_domain}&ref_sign={ref_sign}&bad_user=false&cdn_is_working={cdn_is_working.ToString().ToLower()}&type={type}&hash={hash}&id={id}&info=%7B%7D", json =>
        {
            var rx = Rx.Matches("\"([0-9]+)p?\":\\[\\{\"src\":\"([^\"]+)", json);
            if (rx.Count == 0)
                return;

            streams = new List<StreamModel>(rx.Count);

            foreach (var row in rx.Rows())
            {
                var g = row.Groups();

                if (!string.IsNullOrWhiteSpace(g[2].Value))
                {
                    string m3u = g[2].Value;
                    if (!m3u.Contains("manifest.m3u8"))
                    {
                        int zCharCode = Convert.ToInt32('Z');

                        string src = Regex.Replace(g[2].Value, "[a-zA-Z]", e =>
                        {
                            int eCharCode = Convert.ToInt32(e.Value[0]);
                            return ((eCharCode <= zCharCode ? 90 : 122) >= (eCharCode = eCharCode + 18) ? (char)eCharCode : (char)(eCharCode - 26)).ToString();
                        });

                        m3u = DecodeUrlBase64(src);
                    }

                    if (m3u.StartsWith("//"))
                        m3u = $"https:{m3u}";

                    if (!_usehls && m3u.Contains(".m3u"))
                        m3u = m3u.Replace(":hls:manifest.m3u8", "");

                    streams.Add(new StreamModel() { q = $"{g[1].Value}p", url = m3u });
                }
            }
        });

        if (streams == null || streams.Count == 0)
            return null;

        streams.Reverse();

        return streams;
    }

    public string VideoParse(List<StreamModel> streams, string title, string original_title, int episode, bool play, HttpContext httpContext, VastConf vast = null)
    {
        if (streams == null || streams.Count == 0)
            return string.Empty;

        if (play)
            return onstreamfile(streams[0].url);

        string name = title ?? original_title ?? "auto";
        if (episode > 0)
            name += $" ({episode} серия)";

        var streamquality = new StreamQualityTpl();
        foreach (var l in streams)
            streamquality.Append(onstreamfile(l.url), l.q);

        return VideoTpl.ToJson(
            "play",
            onstreamfile(streams[0].url),
            name,
            streamquality: streamquality,
            vast: vast,
            httpContext: httpContext
        );
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(List<Result> results, string args, string imdb_id, long kinopoisk_id, string title, string original_title, int clarification, string pick, string kid, int s, bool showstream, bool rjson)
    {
        if (results == null || results.Count == 0)
            return default;

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        if (results[0].type is "foreign-movie" or "soviet-cartoon" or "foreign-cartoon" or "russian-cartoon" or "anime" or "russian-movie")
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, results.Count);

            foreach (var data in results)
            {
                string url = host + $"lite/kodik/video?title={enc_title}&original_title={enc_original_title}&link={HttpUtility.UrlEncode(data.link)}";

                string streamlink = null;
                if (showstream)
                {
                    streamlink = usehls ? $"{url.Replace("/video", $"/{videopath}.m3u8")}&play=true" : $"{url.Replace("/video", $"/{videopath}")}&play=true";

                    if (!string.IsNullOrEmpty(args))
                        streamlink += $"&{args.Remove(0, 1)}";
                }

                mtpl.Append(
                    data.translation.title,
                    url,
                    "call",
                    streamlink
                );
            }

            return mtpl;
            #endregion
        }
        else
        {
            #region Сериал
            string enc_pick = HttpUtility.UrlEncode(pick);

            if (s == -1)
            {
                var tpl = new SeasonTpl(results.Count);
                var hash = new HashSet<int>();

                foreach (var item in results.AsEnumerable().Reverse())
                {
                    int season = item.last_season;

                    if (hash.Add(season))
                    {
                        tpl.Append(
                            $"{season} сезон",
                            host + $"lite/kodik?rjson={rjson}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&clarification={clarification}&pick={enc_pick}&s={season}",
                            season
                        );
                    }
                }

                return tpl;
            }
            else
            {
                #region Перевод
                var vtpl = new VoiceTpl();
                HashSet<string> hash = new HashSet<string>();

                foreach (var item in results)
                {
                    string id = item.id;
                    if (string.IsNullOrEmpty(id))
                        continue;

                    string name = item.translation.title ?? "оригинал";
                    if (hash.Contains(name))
                        continue;

                    if (item.last_season != s)
                    {
                        if (item.seasons == null || !item.seasons.ContainsKey(s.ToString()))
                            continue;
                    }

                    hash.Add(name);

                    if (string.IsNullOrEmpty(kid))
                        kid = id;

                    vtpl.Append(
                        name,
                        kid == id,
                        host + $"lite/kodik?rjson={rjson}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&clarification={clarification}&pick={enc_pick}&s={s}&kid={id}"
                    );
                }
                #endregion

                var selected = results.FirstOrDefault(i => i.id == kid);
                if (string.IsNullOrEmpty(selected?.id))
                    selected = results[0];

                string sArhc = s.ToString();
                var series = ResolveEpisodesAsync(selected, sArhc);
                if (series == null || series.Count == 0)
                    return default;

                var etpl = new EpisodeTpl(vtpl, series.Count);

                foreach (var episode in series)
                {
                    string url = host + $"lite/kodik/video?title={enc_title}&original_title={enc_original_title}&link={HttpUtility.UrlEncode(episode.Value)}&episode={episode.Key}";

                    string streamlink = null;
                    if (showstream)
                    {
                        streamlink = usehls ? $"{url.Replace("/video", $"/{videopath}.m3u8")}&play=true" : $"{url.Replace("/video", $"/{videopath}")}&play=true";

                        if (!string.IsNullOrEmpty(args))
                            streamlink += $"&{args.Remove(0, 1)}";
                    }

                    etpl.Append(
                        $"{episode.Key} серия",
                        title ?? original_title,
                        sArhc,
                        episode.Key,
                        url,
                        "call",
                        streamlink: streamlink
                    );
                }

                return etpl;
            }
            #endregion
        }
    }
    #endregion

    #region DecodeUrlBase64
    static string DecodeUrlBase64(string s)
    {
        if (s == null)
            return s;

        return Encoding.UTF8.GetString(Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(4 * ((s.Length + 3) / 4), '=')));
    }
    #endregion


    #region [Codex AI] - db.json
    List<Result> FallbackByIds(string imdb_id, long kinopoisk_id, int season)
    {
        var data = fallbackDatabase;
        if (data == null)
            return null;

        bool requireImdb = !string.IsNullOrEmpty(imdb_id);
        bool requireKinopoisk = kinopoisk_id > 0;

        var matches = data.Where(item =>
        {
            bool imdbMatch = !requireImdb || string.Equals(item.imdb_id, imdb_id, StringComparison.OrdinalIgnoreCase);
            bool kinopoiskMatch = !requireKinopoisk || item.kinopoisk_id == kinopoisk_id.ToString();
            return imdbMatch && kinopoiskMatch;
        }).ToList();

        if (matches.Count == 0)
            return null;

        return matches.Count == 0 ? null : matches;
    }

    List<Result> FallbackByTitle(string title, string originalTitle)
    {
        var data = fallbackDatabase;
        if (data == null)
            return null;

        bool hasTitle = !string.IsNullOrWhiteSpace(title);
        bool hasOriginal = !string.IsNullOrWhiteSpace(originalTitle);

        var strictMatches = new List<Result>();
        List<Result> fallbackMatches = (hasTitle || hasOriginal) ? new List<Result>() : null;

        foreach (var item in data)
        {
            bool titleMatch = !hasTitle || TitleMatches(item.title, title) || TitleMatches(item.title_orig, title);
            bool originalMatch = !hasOriginal || TitleMatches(item.title, originalTitle) || TitleMatches(item.title_orig, originalTitle);

            if (titleMatch && originalMatch)
            {
                strictMatches.Add(item);
                continue;
            }

            if (fallbackMatches == null)
                continue;

            if (TitleMatches(item.title, title) ||
                TitleMatches(item.title_orig, title) ||
                TitleMatches(item.title, originalTitle) ||
                TitleMatches(item.title_orig, originalTitle))
            {
                fallbackMatches.Add(item);
            }
        }

        var matches = strictMatches.Count > 0 ? strictMatches : fallbackMatches;

        return matches == null || matches.Count == 0 ? null : matches;
    }

    static bool TitleMatches(string source, string target)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            return false;

        string normalizedSource = StringConvert.SearchName(source);
        string normalizedTarget = StringConvert.SearchName(target);

        if (string.IsNullOrWhiteSpace(normalizedSource) || string.IsNullOrWhiteSpace(normalizedTarget))
            return false;

        return normalizedSource.Contains(normalizedTarget);
    }

    Dictionary<string, string> ResolveEpisodesAsync(Result selected, string seasonKey)
    {
        if (selected.seasons != null &&
            selected.seasons.TryGetValue(seasonKey, out var seasonInfo) &&
            seasonInfo.episodes != null &&
            seasonInfo.episodes.Count > 0)
        {
            return seasonInfo.episodes;
        }

        //var seasonsFromHtml = await LoadSeasonsFromHtml(selected);
        //if (seasonsFromHtml != null &&
        //    seasonsFromHtml.TryGetValue(seasonKey, out seasonInfo) &&
        //    seasonInfo.episodes != null &&
        //    seasonInfo.episodes.Count > 0)
        //{
        //    return seasonInfo.episodes;
        //}

        return null;
    }

    //async Task<Dictionary<string, Season>> LoadSeasonsFromHtml(Result selected)
    //{
    //    if (string.IsNullOrWhiteSpace(selected.id) || string.IsNullOrWhiteSpace(selected.link) || httpHydra == null)
    //        return null;

    //    string cacheKey = $"kodik:series:{selected.id}";
    //    if (hybridCache.TryGetValue(cacheKey, out Dictionary<string, Season> cached))
    //        return cached;

    //    try
    //    {
    //        string html = await httpHydra.Get($"https:{selected.link}");
    //        if (string.IsNullOrWhiteSpace(html))
    //        {
    //            return null;
    //        }

    //        var doc = new HtmlDocument();
    //        doc.LoadHtml(html);

    //        var optionsRoot = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'series-options')]");
    //        if (optionsRoot == null)
    //            return null;

    //        var seasons = new Dictionary<string, Season>();

    //        var seasonNodes = optionsRoot.SelectNodes(".//div[contains(@class,'season-')]");
    //        if (seasonNodes == null)
    //            return null;

    //        foreach (var seasonNode in seasonNodes)
    //        {
    //            string classes = seasonNode.GetAttributeValue("class", string.Empty);
    //            var match = Regex.Match(classes, "season-([0-9]+)");
    //            if (!match.Success)
    //                continue;

    //            string seasonKey = match.Groups[1].Value;
    //            if (string.IsNullOrEmpty(seasonKey))
    //                continue;

    //            var options = seasonNode.SelectNodes(".//option");
    //            if (options == null || options.Count == 0)
    //                continue;

    //            var episodes = new Dictionary<string, string>();

    //            foreach (var option in options)
    //            {
    //                string episodeNumber = option.GetAttributeValue("value", null) ?? option.InnerText;
    //                episodeNumber = episodeNumber?.Trim();
    //                if (string.IsNullOrEmpty(episodeNumber))
    //                    continue;

    //                string episodeLink = BuildEpisodeLink(option);
    //                if (string.IsNullOrEmpty(episodeLink))
    //                    continue;

    //                if (!episodes.ContainsKey(episodeNumber))
    //                    episodes[episodeNumber] = episodeLink;
    //            }

    //            if (episodes.Count > 0)
    //            {
    //                seasons[seasonKey] = new Season
    //                {
    //                    link = selected.link,
    //                    episodes = episodes
    //                };
    //            }
    //        }

    //        if (seasons.Count == 0)
    //            return null;

    //        hybridCache.Set(cacheKey, seasons, TimeSpan.FromMinutes(20));
    //        return seasons;
    //    }
    //    catch
    //    {
    //        return null;
    //    }
    //}

    //static string BuildEpisodeLink(HtmlNode option)
    //{
    //    string dataId = option.GetAttributeValue("data-id", null);
    //    string dataHash = option.GetAttributeValue("data-hash", null);

    //    if (string.IsNullOrWhiteSpace(dataId) || string.IsNullOrWhiteSpace(dataHash))
    //        return null;

    //    return $"//kodik.info/seria/{dataId}/{dataHash}/720p";
    //}
    #endregion
}
