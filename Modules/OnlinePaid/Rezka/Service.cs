using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Rezka
{
    public class RezkaInvoke
    {
        static readonly Serilog.ILogger Log = Serilog.Log.ForContext<RezkaInvoke>();

        #region RezkaInvoke
        RezkaSettings init;
        string host, scheme, route;
        string apihost;
        bool usehls, userprem, usereserve;
        HttpHydra httpHydra;
        List<HeadersModel> defaultHeaders;
        Func<string, string> onstreamfile;
        bool safety = false;
        CookieContainer cookieContainer;

        public string requestlog = string.Empty;

        static readonly ConcurrentDictionary<long, string> basereferer = new();

        public RezkaInvoke(string host, string route, RezkaSettings init, CookieContainer cookieContainer, bool safety, List<HeadersModel> defaultHeaders, HttpHydra httpHydra, Func<string, string> onstreamfile)
        {
            this.host = host != null ? $"{host}/" : null;
            this.route = route;
            this.init = init;
            this.cookieContainer = cookieContainer;
            this.safety = safety;
            apihost = init.host;
            scheme = init.scheme;
            this.httpHydra = httpHydra;
            this.defaultHeaders = defaultHeaders;
            this.onstreamfile = onstreamfile;
            usehls = init.hls;
            usereserve = init.reserve;
            userprem = init.premium;
        }
        #endregion

        #region Search
        async public Task<SearchModel> Search(string title, string original_title, int clarification, int year)
        {
            var result = new SearchModel();
            string reservedlink = null;

            var base_headers = HeadersModel.Init(init.headers,
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                ("cache-control", "no-cache"),
                ("dnt", "1"),
                ("pragma", "no-cache"),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1"),
                ("referer", $"{apihost}/")
            );

            var newheaders = HeadersModel.Join(base_headers, defaultHeaders);

            result.search_uri = $"{apihost}/search/?do=search&subaction=search&q={HttpUtility.UrlEncode(clarification == 1 ? title : (original_title ?? title))}";

            await httpHydra.GetSpan(result.search_uri, statusCodeOK: false, safety: safety, newheaders: newheaders, cookieContainer: cookieContainer, spanAction: search =>
            {
                if (search.Contains("class=\"error-code\"", StringComparison.OrdinalIgnoreCase) && search.Contains("ошибка доступа", StringComparison.OrdinalIgnoreCase))
                {
                    if (search.Contains("(105)", StringComparison.Ordinal) ||
                        search.Contains(">105<", StringComparison.Ordinal) ||
                        search.Contains("(403)", StringComparison.Ordinal) ||
                        search.Contains(">403<", StringComparison.Ordinal))
                    {
                        result = new SearchModel() { IsEmpty = true, content = "Ошибка доступа (105)<br>IP-адрес заблокирован<br><br>" };
                        return;
                    }

                    if (search.Contains("(101)", StringComparison.Ordinal) ||
                        search.Contains(">101<", StringComparison.Ordinal))
                    {
                        result = new SearchModel() { IsEmpty = true, content = "Ошибка доступа (101)<br>Аккаунт заблокирован<br><br>" };
                        return;
                    }

                    var accessError = Rx.Groups(search, "Ошибка доступа[ \t]+\\(([0-9]+)\\)", RegexOptions.IgnoreCase);
                    result = new SearchModel() { IsEmpty = true, content = $"Ошибка доступа ({accessError[1].Value})" };
                    return;
                }

                string stitle = StringConvert.SearchName(title);
                string sorigtitle = StringConvert.SearchName(original_title);

                var rx = Rx.Split("\"b-content__inline_item\"", search, 1);
                if (rx.Count == 0)
                {
                    result.IsError = true;
                    return;
                }

                foreach (var row in rx.Rows())
                {
                    var g = row.Groups("href=\"https?://[^/]+/([^\"]+)\">([^<]+)</a> ?<div>([0-9]{4})");

                    if (string.IsNullOrEmpty(g[1].Value))
                        continue;

                    string name = g[2].Value.Trim();
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (result.similar == null)
                        result.similar = new List<SimilarModel>(rx.Count);

                    string img = row.Match("<img src=\"([^\"]+)\"");
                    result.similar.Add(new SimilarModel(name, g[3].Value, g[1].Value, img));

                    string _sname = StringConvert.SearchName(name);

                    if ((stitle != null && (name.Contains(" / ") && _sname.Contains(stitle) || _sname == stitle)) ||
                        (sorigtitle != null && (name.Contains(" / ") && _sname.Contains(sorigtitle) || _sname == sorigtitle)))
                    {
                        reservedlink = g[1].Value;

                        if (string.IsNullOrEmpty(result.href) && year > 0 && g[3].Value == year.ToString())
                            result.href = reservedlink;
                    }
                }

                if (string.IsNullOrEmpty(result.href))
                {
                    if (string.IsNullOrEmpty(reservedlink))
                    {
                        if (result.similar.Count > 0)
                            return;

                        if (search.Contains("Результаты поиска", StringComparison.Ordinal))
                        {
                            result = new SearchModel() { IsEmpty = true };
                            return;
                        }

                        result.IsError = true;
                        return;
                    }

                    result.href = reservedlink;
                }
            });

            return result;
        }
        #endregion

        #region Embed
        async public Task<EmbedModel> Embed(string href, string search_uri)
        {
            if (!href.StartsWith("http"))
                href = $"{apihost}/{href}";

            var result = new EmbedModel();

            var base_headers = HeadersModel.Init(init.headers,
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                ("cache-control", "no-cache"),
                ("dnt", "1"),
                ("pragma", "no-cache"),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1")
            );

            if (!string.IsNullOrEmpty(search_uri))
                base_headers.Add(new HeadersModel("referer", search_uri));

            var newheaders = HeadersModel.Join(base_headers, defaultHeaders);

            result.id = Regex.Match(href, "/([0-9]+)-[^/]+\\.html").Groups[1].Value;
            if (long.TryParse(result.id, out long id) && id > 0)
                basereferer.TryAdd(id, href);

            bool IsTrailer = false;

            await httpHydra.GetSpan(href, safety: safety, cookieContainer: cookieContainer, newheaders: newheaders, spanAction: html =>
            {
                IsTrailer = html.Contains("Ожидаем фильм в хорошем качестве", StringComparison.OrdinalIgnoreCase);

                if (!html.Contains("data-season_id=", StringComparison.Ordinal))
                {
                    var rx = Rx.Split("ctrl_token_id", html);
                    if (rx.Count > 0)
                    {
                        var row = Rx.Split("translators-list", rx[0].Span);
                        if (row.Count > 1)
                            result.content = row[1].ToString(); // фильм
                    }
                }
                else
                {
                    string trs = Rx.Match(html, "\\.initCDNSeriesEvents\\([0-9]+, ([0-9]+),");
                    if (string.IsNullOrEmpty(trs))
                        return;

                    result.trs = trs;

                    var rx = Rx.Split("user-network-issues", html);
                    if (rx.Count > 0)
                    {
                        var row = Rx.Split("b-post__lastepisodeout", rx[0].Span);
                        if (row.Count > 1)
                            result.content = row[1].ToString(); // сериал
                        else if (!IsTrailer)
                            result.content = rx[0].Span.ToString(); // что то пошло не так
                    }
                }
            });

            if (string.IsNullOrEmpty(result.content))
            {
                return IsTrailer
                    ? new EmbedModel() { IsEmpty = true, content = "Ожидаем фильм в хорошем качестве..." }
                    : null;
            }

            return result;
        }
        #endregion

        #region Tpl
        public ITplResult Tpl(EmbedModel result, string args, string title, string original_title, int s, string href, bool showstream, bool rjson = false)
        {
            if (result == null || result.IsEmpty || result.content == null)
                return default;

            if (!string.IsNullOrEmpty(args))
                args = $"&{args.Remove(0, 1)}";

            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);
            string enc_href = HttpUtility.UrlEncode(href);

            if (!result.content.Contains("data-season_id="))
            {
                #region Фильм
                var match = new Regex("<[^>]+ data-translator_id=\"([0-9]+)\"([^>]+)?>(?<voice>[^<]+)(<img title=\"(?<imgname>[^\"]+)\" [^>]+/>)?").Match(result.content);

                var mtpl = new MovieTpl(title, original_title, match.Length);

                if (match.Success)
                {
                    while (match.Success)
                    {
                        if (!string.IsNullOrEmpty(match.Groups[1].Value) && !string.IsNullOrEmpty(match.Groups["voice"].Value))
                        {
                            if (!userprem && match.Groups[0].Value.Contains("prem_translator"))
                            {
                                match = match.NextMatch();
                                continue;
                            }

                            string favs = Regex.Match(result.content, "id=\"ctrl_favs\" value=\"([^\"]+)\"").Groups[1].Value;
                            string link = host + $"{route}/movie?title={enc_title}&original_title={enc_original_title}&id={result.id}&t={match.Groups[1].Value}&favs={favs}";

                            string voice_href = Regex.Match(match.Groups[0].Value, "href=\"(https?://[^/]+)?/([^\"]+)\"").Groups[2].Value;
                            if (!string.IsNullOrEmpty(voice_href))
                                link += $"&voice={HttpUtility.UrlEncode(voice_href)}";

                            #region voice
                            string voice = match.Groups["voice"].Value.Trim();

                            if (!string.IsNullOrEmpty(match.Groups["imgname"].Value) && !voice.ToLower().Contains(match.Groups["imgname"].Value.ToLowerAndTrim()))
                                voice += $" ({match.Groups["imgname"].Value.Trim()})";

                            if (voice == "-" || string.IsNullOrEmpty(voice))
                                voice = "Оригинал";
                            #endregion

                            if (match.Groups[2].Value.Contains("data-director=\"1\""))
                                link += "&director=1";

                            string stream = null;
                            if (showstream)
                            {
                                stream = usehls ? $"{link.Replace("/movie", "/movie.m3u8")}&play=true" : $"{link}&play=true";
                                stream += args;
                            }

                            mtpl.Append(voice, link, "call", stream);
                        }

                        match = match.NextMatch();
                    }
                }
                else
                {
                    var links = getStreamLink(Regex.Match(result.content, "\"id\":\"cdnplayer\",\"streams\":\"([^\"]+)\"").Groups[1].Value.Replace("\\", ""));
                    if (links.Count == 0)
                        return default;

                    var streamquality = new StreamQualityTpl(links.Select(l => (onstreamfile(l.stream_url!), l.title!)));

                    var first = streamquality.Firts();
                    if (first != null)
                        mtpl.Append(first.quality, onstreamfile(first.link), streamquality: streamquality);
                }

                return mtpl;
                #endregion
            }
            else
            {
                #region Перевод
                var vtpl = new VoiceTpl();

                if (result.content.Contains("data-translator_id="))
                {
                    var match = new Regex("<[a-z]+ [^>]+ data-translator_id=\"(?<translator>[0-9]+)\"([^>]+)?>(?<name>[^<]+)(<img title=\"(?<imgname>[^\"]+)\" [^>]+/>)?").Match(result.content);
                    while (match.Success)
                    {
                        if (!userprem && match.Groups[0].Value.Contains("prem_translator"))
                        {
                            match = match.NextMatch();
                            continue;
                        }

                        string name = match.Groups["name"].Value.Trim();
                        if (!string.IsNullOrEmpty(match.Groups["imgname"].Value) && !name.ToLower().Contains(match.Groups["imgname"].Value.ToLowerAndTrim()))
                            name += $" ({match.Groups["imgname"].Value})";

                        string link = host + $"{route}/serial?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&href={enc_href}&id={result.id}&t={match.Groups["translator"].Value}";

                        string voice_href = Regex.Match(match.Groups[0].Value, "href=\"(https?://[^/]+)?/([^\"]+)\"").Groups[2].Value;
                        if (!string.IsNullOrEmpty(voice_href) && init.ajax != null && init.ajax.Value == false)
                        {
                            string voice = HttpUtility.UrlEncode(voice_href);
                            link = host + $"{route}?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&href={voice}&id={result.id}&t={match.Groups["translator"].Value}";
                        }

                        vtpl.Append(name, match.Groups["translator"].Value == result.trs, link);

                        match = match.NextMatch();
                    }
                }
                #endregion

                #region Сериал
                var tpl = new SeasonTpl(vtpl);
                var etpl = new EpisodeTpl(vtpl);
                HashSet<string> eshash = new HashSet<string>();

                string sArhc = s.ToString();

                var m = Regex.Match(result.content, "data-cdn_url=\"(?<cdn>[^\"]+)\" [^>]+ data-season_id=\"(?<season>[0-9]+)\" data-episode_id=\"(?<episode>[0-9]+)\"([^>]+)?>(?<name>[^>]+)</[a-z]+>");
                while (m.Success)
                {
                    if (s == -1)
                    {
                        #region Сезоны
                        string sname = $"{m.Groups["season"].Value} сезон";
                        if (!string.IsNullOrEmpty(m.Groups["season"].Value) && !eshash.Contains(sname))
                        {
                            eshash.Add(sname);
                            string link = host + $"{route}?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&href={enc_href}&t={result.trs}&s={m.Groups["season"].Value}";

                            tpl.Append(sname, link, m.Groups["season"].Value);
                        }
                        #endregion
                    }
                    else
                    {
                        #region Серии
                        if (m.Groups["season"].Value == sArhc && !eshash.Contains(m.Groups["name"].Value))
                        {
                            eshash.Add(m.Groups["name"].Value);
                            string link = host + $"{route}/movie?title={enc_title}&original_title={enc_original_title}&id={result.id}&t={result.trs}&s={s}&e={m.Groups["episode"].Value}";

                            string voice_href = Regex.Match(m.Groups[0].Value, "href=\"(https?://[^/]+)?/([^\"]+)\"").Groups[2].Value;
                            if (!string.IsNullOrEmpty(voice_href))
                                link += $"&voice={HttpUtility.UrlEncode(voice_href)}";

                            string stream = null;
                            if (showstream)
                            {
                                stream = usehls ? $"{link.Replace("/movie", "/movie.m3u8")}&play=true" : $"{link}&play=true";
                                stream += args;
                            }

                            etpl.Append(m.Groups["name"].Value, title ?? original_title, sArhc, m.Groups["episode"].Value, link, "call", streamlink: stream);
                        }
                        #endregion
                    }

                    m = m.NextMatch();
                }

                if (s == -1)
                    return tpl;

                return etpl;
                #endregion
            }
        }
        #endregion


        #region Serial
        async public Task<Episodes> SerialEmbed(long id, int t)
        {
            var base_headers = HeadersModel.Init(init.headers,
                ("accept", "application/json, text/javascript, */*; q=0.01"),
                ("cache-control", "no-cache"),
                ("dnt", "1"),
                ("origin", apihost),
                ("pragma", "no-cache"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-origin"),
                ("x-requested-with", "XMLHttpRequest")
            );

            if (basereferer.TryGetValue(id, out string referer) && !string.IsNullOrEmpty(referer))
                base_headers.Add(new HeadersModel("referer", referer));

            var newheaders = HeadersModel.Join(base_headers, defaultHeaders);

            string uri = $"{apihost}/ajax/get_cdn_series/?t={((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds()}{Random.Shared.Next(101, 999)}";
            string data = $"id={id}&translator_id={t}&action=get_episodes";

            var root = await httpHydra.Post<Episodes>(uri, data, safety: safety, cookieContainer: cookieContainer, newheaders: newheaders, textJson: true);
            if (root == null)
                return null;

            string episodes = root.episodes;
            if (string.IsNullOrWhiteSpace(episodes) || episodes.Contains("false", StringComparison.OrdinalIgnoreCase))
                return null;

            return root;
        }

        public ITplResult Serial(Episodes root, EmbedModel result, string args, string title, string original_title, string href, long id, int t, int s, bool showstream, bool rjson = false)
        {
            if (root == null || result == null)
                return default;

            if (!string.IsNullOrEmpty(args))
                args = $"&{args.Remove(0, 1)}";

            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);
            string enc_href = HttpUtility.UrlEncode(href);

            #region Перевод
            var vtpl = new VoiceTpl();

            {
                if (string.IsNullOrWhiteSpace(href))
                    return default;

                if (result?.content != null)
                {
                    if (result.content.Contains("data-translator_id="))
                    {
                        var match = new Regex("<[a-z]+ [^>]+ data-translator_id=\"(?<translator>[0-9]+)\"([^>]+)?>(?<name>[^<]+)(<img title=\"(?<imgname>[^\"]+)\" [^>]+/>)?").Match(result.content);
                        while (match.Success)
                        {
                            if (!userprem && match.Groups[0].Value.Contains("prem_translator"))
                            {
                                match = match.NextMatch();
                                continue;
                            }

                            string name = match.Groups["name"].Value.Trim() + (string.IsNullOrWhiteSpace(match.Groups["imgname"].Value) ? "" : $" ({match.Groups["imgname"].Value})");
                            string link = host + $"{route}/serial?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&href={enc_href}&id={id}&t={match.Groups["translator"].Value}";

                            vtpl.Append(name, match.Groups["translator"].Value == t.ToString(), link);

                            match = match.NextMatch();
                        }
                    }
                }
            }
            #endregion

            if (s == -1)
            {
                #region Сезоны
                var tpl = new SeasonTpl(vtpl, root.seasons.Length);

                var match = new Regex("data-tab_id=\"(?<season>[0-9]+)\"([^>]+)?>(?<name>[^<]+)</[a-z]+>").Match(root.seasons);
                while (match.Success)
                {
                    string link = host + $"{route}/serial?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&href={enc_href}&id={id}&t={t}&s={match.Groups["season"].Value}";

                    tpl.Append($"{match.Groups["season"].Value} сезон", link, match.Groups["season"].Value);

                    match = match.NextMatch();
                }

                return tpl;
                #endregion
            }
            else
            {
                #region Серии
                var etpl = new EpisodeTpl(vtpl);

                var m = new Regex($"data-season_id=\"{s}\" data-episode_id=\"(?<episode>[0-9]+)\"([^>]+)?>(?<name>[^<]+)</li>").Match(root.episodes);
                while (m.Success)
                {
                    if (!string.IsNullOrEmpty(m.Groups["episode"].Value) && !string.IsNullOrEmpty(m.Groups["name"].Value))
                    {
                        string link = host + $"{route}/movie?title={enc_title}&original_title={enc_original_title}&id={id}&t={t}&s={s}&e={m.Groups["episode"].Value}";

                        string voice_href = Regex.Match(m.Groups[0].Value, "href=\"(https?://[^/]+)?/([^\"]+)\"").Groups[2].Value;
                        if (!string.IsNullOrEmpty(voice_href))
                            link += $"&voice={HttpUtility.UrlEncode(voice_href)}";

                        string stream = usehls ? $"{link.Replace("/movie", "/movie.m3u8")}&play=true" : $"{link}&play=true";

                        etpl.Append(m.Groups["name"].Value, title ?? original_title, s.ToString(), m.Groups["episode"].Value, link, "call", streamlink: (showstream ? $"{stream}{args}" : null));
                    }

                    m = m.NextMatch();
                }

                return etpl;
                #endregion
            }
        }
        #endregion

        #region Movie
        async public Task<MovieModel> Movie(long id, int t, int director, int s, int e, string favs)
        {
            string data = null;
            string uri = $"{apihost}/ajax/get_cdn_series/?t={((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds()}{Random.Shared.Next(101, 999)}";

            if (s == -1)
            {
                data = $"id={id}&translator_id={t}&is_camrip=0&is_ads=0&is_director={director}&favs={favs}&action=get_movie";
            }
            else
            {
                data = $"id={id}&translator_id={t}&season={s}&episode={e}&favs={favs}&action=get_stream";
            }

            var base_headers = HeadersModel.Init(init.headers,
                ("accept", "application/json, text/javascript, */*; q=0.01"),
                ("cache-control", "no-cache"),
                ("dnt", "1"),
                ("origin", apihost),
                ("pragma", "no-cache"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-origin"),
                ("x-requested-with", "XMLHttpRequest")
            );

            if (basereferer.TryGetValue(id, out string referer) && !string.IsNullOrEmpty(referer))
                base_headers.Add(new HeadersModel("referer", referer));

            var newheaders = HeadersModel.Join(base_headers, defaultHeaders);

            var root = await httpHydra.Post<Dictionary<string, object>>(uri, data, safety: safety, cookieContainer: cookieContainer, newheaders: newheaders);

            if (root == null)
                return null;

            string url = root.ContainsKey("url") ? root["url"]?.ToString() : null;
            if (string.IsNullOrEmpty(url) || url.Contains("false", StringComparison.OrdinalIgnoreCase))
                return null;

            var links = getStreamLink(url);
            if (links.Count == 0)
                return null;

            string subtitlehtml = null;

            try
            {
                subtitlehtml = root?["subtitle"]?.ToString();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_0jizyv93");
            }

            return new MovieModel() { links = links, subtitlehtml = subtitlehtml };
        }

        async public Task<MovieModel> Movie(string href)
        {
            var base_headers = HeadersModel.Init(init.headers,
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                ("cache-control", "no-cache"),
                ("dnt", "1"),
                ("pragma", "no-cache"),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1")
            );

            var newheaders = HeadersModel.Join(base_headers, defaultHeaders);

            List<ApiModel> links = null;
            string subtitlehtml = null;

            await httpHydra.GetSpan($"{apihost}/{href}", safety: safety, cookieContainer: cookieContainer, newheaders: newheaders, spanAction: html =>
            {
                string url = Rx.Match(html, "\"streams\"\\s*:\\s*\"(.*?)\"\\s*,");
                if (string.IsNullOrEmpty(url) || url.Contains("false", StringComparison.OrdinalIgnoreCase))
                    return;

                links = getStreamLink(url.Replace("\\", ""));

                if (links.Count > 0)
                    subtitlehtml = Rx.Match(html, "\"subtitle\":\"([^\"]+)\"");
            });

            if (links == null || links.Count == 0)
            {
                return null;
            }

            return new MovieModel()
            {
                links = links,
                subtitlehtml = subtitlehtml
            };
        }

        public string Movie(MovieModel md, string title, string original_title, bool play, VastConf vast = null)
        {
            if (play)
                return onstreamfile(md.links[0].stream_url!);

            #region subtitles
            var subtitles = new SubtitleTpl();

            try
            {
                if (!string.IsNullOrWhiteSpace(md.subtitlehtml))
                {
                    var m = Regex.Match(md.subtitlehtml, "\\[([^\\]]+)\\](https?://[^\n\r,']+\\.vtt)");
                    while (m.Success)
                    {
                        if (!string.IsNullOrEmpty(m.Groups[1].Value) && !string.IsNullOrEmpty(m.Groups[2].Value))
                            subtitles.Append(m.Groups[1].Value, onstreamfile(m.Groups[2].Value));

                        m = m.NextMatch();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_gu3or11x");
            }
            #endregion

            var streamquality = new StreamQualityTpl();
            foreach (var l in md.links)
                streamquality.Append(onstreamfile(l.stream_url!), l.title);

            return VideoTpl.ToJson("play", onstreamfile(md.links[0].stream_url!), (title ?? original_title ?? "auto"),
                streamquality: streamquality,
                subtitles: subtitles,
                vast: vast,
                hls_manifest_timeout: (int)TimeSpan.FromSeconds(20).TotalMilliseconds
            );
        }
        #endregion


        #region decodeBase64
        static string[] trashListBase = ["JCQhIUAkJEBeIUAjJCRA", "QEBAQEAhIyMhXl5e", "IyMjI14hISMjIUBA", "Xl5eIUAjIyEhIyM=", "JCQjISFAIyFAIyM="];
        static string[] trashListOld = ["QEA=", "QCM=", "QCE=", "QF4=", "QCQ=", "I0A=", "IyM=", "IyE=", "I14=", "IyQ=", "IUA=", "ISM=", "ISE=", "IV4=", "ISQ=", "XkA=", "XiM=", "XiE=", "Xl4=", "XiQ=", "JEA=", "JCM=", "JCE=", "JF4=", "JCQ=", "QEBA", "QEAj", "QEAh", "QEBe", "QEAk", "QCNA", "QCMj", "QCMh", "QCNe", "QCMk", "QCFA", "QCEj", "QCEh", "QCFe", "QCEk", "QF5A", "QF4j", "QF4h", "QF5e", "QF4k", "QCRA", "QCQj", "QCQh", "QCRe", "QCQk", "I0BA", "I0Aj", "I0Ah", "I0Be", "I0Ak", "IyNA", "IyMj", "IyMh", "IyNe", "IyMk", "IyFA", "IyEj", "IyEh", "IyFe", "IyEk", "I15A", "I14j", "I14h", "I15e", "I14k", "IyRA", "IyQj", "IyQh", "IyRe", "IyQk", "IUBA", "IUAj", "IUAh", "IUBe", "IUAk", "ISNA", "ISMj", "ISMh", "ISNe", "ISMk", "ISFA", "ISEj", "ISEh", "ISFe", "ISEk", "IV5A", "IV4j", "IV4h", "IV5e", "IV4k", "ISRA", "ISQj", "ISQh", "ISRe", "ISQk", "XkBA", "XkAj", "XkAh", "XkBe", "XkAk", "XiNA", "XiMj", "XiMh", "XiNe", "XiMk", "XiFA", "XiEj", "XiEh", "XiFe", "XiEk", "Xl5A", "Xl4j", "Xl4h", "Xl5e", "Xl4k", "XiRA", "XiQj", "XiQh", "XiRe", "XiQk", "JEBA", "JEAj", "JEAh", "JEBe", "JEAk", "JCNA", "JCMj", "JCMh", "JCNe", "JCMk", "JCFA", "JCEj", "JCEh", "JCFe", "JCEk", "JF5A", "JF4j", "JF4h", "JF5e", "JF4k", "JCRA", "JCQj", "JCQh", "JCRe", "JCQk"];

        static string decodeBase64(string data)
        {
            if (data.StartsWith("#"))
            {
                try
                {
                    string _data = data.Remove(0, 2);

                    foreach (string trash in trashListBase)
                    {
                        if (_data.Contains($"//_//{trash}"))
                            _data = _data.Replace($"//_//{trash}", "");
                    }

                    using (var nbuf = new BufferBytePool(Encoding.UTF8.GetMaxByteCount(data.Length)))
                    {
                        Span<byte> buffer = nbuf.Span;

                        if (Convert.TryFromBase64String(_data, buffer, out int bytesWritten))
                        {
                            return Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
                        }
                        else
                        {
                            if (Convert.TryFromBase64String(Regex.Replace(_data, "//[^/]+_//", "").Replace("//_//", ""), buffer, out bytesWritten))
                            {
                                return Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
                            }
                            else
                            {
                                _data = data.Remove(0, 2).Replace("//_//", "");

                                foreach (string trash in trashListOld)
                                {
                                    if (_data.Contains(trash))
                                        _data = _data.Replace(trash, "");
                                }

                                _data = Regex.Replace(_data, "//[^/]+_//", "").Replace("//_//", "");

                                if (Convert.TryFromBase64String(_data, buffer, out bytesWritten))
                                    return Encoding.UTF8.GetString(buffer.Slice(0, bytesWritten));
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error(ex, "CatchId={CatchId}", "id_oll157hd");
                }
            }

            return data;
        }
        #endregion

        #region getStreamLink
        List<ApiModel> getStreamLink(string _data)
        {
            string data = decodeBase64(_data);
            var links = new List<ApiModel>(6);

            #region getLink
            string getLink(string _q)
            {
                string qline = Regex.Match(data, $"\\[({_q}|[^\\]]+{_q}[^\\]]+)\\]([^,\\[]+)").Groups[2].Value;
                if (!qline.Contains(".mp4") && !qline.Contains(".m3u8"))
                    return null;

                if (usereserve && qline.Contains(" or "))
                {
                    return string.Join(" or ", qline.Split(" or ").Select(i =>
                    {
                        string l = Regex.Match(i, "(https?://[^\\[\n\r, ]+)").Groups[1].Value;
                        if (usehls)
                        {
                            if (l.EndsWith(".m3u8"))
                                return l;

                            return l + ":hls:manifest.m3u8";
                        }

                        return l.Replace(":hls:manifest.m3u8", "");
                    }));
                }
                else
                {
                    string link = Regex.Match(qline, "(https?://[^\\[\n\r, ]+)").Groups[1].Value;
                    if (string.IsNullOrEmpty(link))
                        return null;

                    if (usehls)
                    {
                        if (link.EndsWith(".m3u8"))
                            return link;

                        return link + ":hls:manifest.m3u8";
                    }

                    return link.Replace(":hls:manifest.m3u8", "");
                }
            }
            #endregion

            #region Максимально доступное
            var qualities = new List<string> { "2160p", "1440p", "1080p", "720p", "480p" };
            if (userprem)
                qualities.InsertRange(2, new List<string> { "1080p Ultra" });

            foreach (string q in qualities)
            {
                string link = null;

                switch (q)
                {
                    case "2160p":
                        link = getLink("4K") ?? getLink(q);
                        break;
                    case "1440p":
                        link = getLink("2K") ?? getLink(q);
                        break;
                    case "1080p":
                        link = userprem ? getLink(q) : (getLink(q) ?? getLink("1080p Ultra"));
                        break;
                    default:
                        link = getLink(q);
                        break;
                }

                if (string.IsNullOrEmpty(link))
                    continue;

                if (scheme == "http")
                    link = link.Replace("https:", "http:");

                string realq = q;

                switch (q)
                {
                    case "1080p Ultra":
                        realq = "1080p";
                        break;
                    case "1080p":
                        realq = "720p";
                        break;
                    case "720p":
                        realq = "480p";
                        break;
                    case "480p":
                        realq = "360p";
                        break;
                }

                links.Add(new ApiModel()
                {
                    title = realq,
                    stream_url = link
                });
            }
            #endregion

            return links;
        }
        #endregion


        #region fixcdn
        public static string fixcdn(string country, string uacdn, string link)
        {
            if (uacdn != null && country == "UA" && !link.Contains(".vtt"))
                return Regex.Replace(link, "https?://[^/]+", uacdn);

            return link;
        }
        #endregion

        #region StreamProxyHeaders
        public static List<HeadersModel> StreamProxyHeaders(RezkaSettings init) => HeadersModel.Init(init.headers,
            ("accept", "*/*"),
            ("cache-control", "no-cache"),
            ("dnt", "1"),
            ("origin", init.host),
            ("pragma", "no-cache"),
            ("referer", $"{init.host}/"),
            ("sec-fetch-dest", "empty"),
            ("sec-fetch-mode", "cors"),
            ("sec-fetch-site", "cross-site")
        );
        #endregion
    }
}
