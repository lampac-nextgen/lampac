using Shared.Services.RxEnumerate;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace FanCDN;

public struct FanCDNInvoke
{
    #region FanCDNInvoke
    string host;
    string apihost;
    HttpHydra httpHydra;
    Func<string, Task<string>> onget;
    Func<string, string> onstreamfile;

    public FanCDNInvoke(string host, string apihost, HttpHydra httpHydra, Func<string, Task<string>> onget, Func<string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null; this.apihost = apihost;
        this.onget = onget;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region EmbedSearch
    async public Task<EmbedModel> EmbedSearch(string title, string original_title, int year, int serial)
    {
        if (serial == 1)
        {
            return null;
        }
        else
        {
            if (string.IsNullOrEmpty(title) || year == 0)
                return null;

            string search = await onget($"{apihost}/?do=search&subaction=search&story={HttpUtility.UrlEncode(title)}");
            if (string.IsNullOrEmpty(search))
                return null;

            string href = null;

            var rx = Rx.Split("item-search-serial", search);

            foreach (var row in rx.Rows())
            {
                var item_search = Rx.Split("item-search-serial", row.Span);
                if (item_search.Count == 0)
                    continue;

                string info = item_search[0].ToString();
                if (!string.IsNullOrEmpty(info) && (info.Contains($"({year - 1}") || info.Contains($"({year}") || info.Contains($"({year + 1}")))
                {
                    string _info = StringConvert.SearchName(info);
                    if (_info.Contains(StringConvert.SearchName(title)) || (!string.IsNullOrEmpty(original_title) && _info.Contains(StringConvert.SearchName(original_title))))
                    {
                        href = Regex.Match(info, "<a href=\"(https?://[^\"]+\\.html)\"").Groups[1].Value;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(href))
                return null;

            string html = await onget(href);
            if (string.IsNullOrEmpty(html))
                return null;


            string iframe_url = null;

            foreach (Match match in Regex.Matches(html, "(https?://fancdn\\.[^\"\n\r\t ]+)\""))
            {
                string cdn = match.Groups[1].Value;
                if (cdn.Contains("kinopoisk=") && cdn.Contains("key="))
                    iframe_url = cdn;
            }


            if (string.IsNullOrEmpty(iframe_url))
                return null;

            return await Embed(iframe_url);
        }
    }
    #endregion

    #region EmbedToken
    async public Task<EmbedModel> EmbedToken(long kinopoisk_id, string token)
    {
        if (kinopoisk_id == 0)
            return null;

        return await Embed($"https://fancdn.net/iframe/?kinopoisk={kinopoisk_id}&key={token}");
    }
    #endregion

    #region EmbedFilms
    async public Task<EmbedModel> EmbedFilms(string host, long kinopoisk_id)
    {
        if (kinopoisk_id == 0)
            return null;

        string iframe_url = $"{host}/films.php?kp={kinopoisk_id}";

        var movies = await httpHydra.Get<Episode[]>(iframe_url, textJson: true);
        if (movies == null || movies.Length == 0)
            return null;

        return new EmbedModel() { movies = movies };
    }
    #endregion

    #region Embed
    async public Task<EmbedModel> Embed(string iframe_url)
    {
        if (string.IsNullOrEmpty(iframe_url))
            return null;

        string iframe = await onget(iframe_url);
        if (string.IsNullOrEmpty(iframe))
            return null;

        iframe = Regex.Replace(iframe, "[\n\r\t]+", "").Replace("var ", "\n");

        string playlist = Regex.Match(iframe, "playlist ?= ?(\\[[^\n\r]+\\]);").Groups[1].Value;
        if (string.IsNullOrEmpty(playlist))
            return null;

        try
        {
            if (iframe.Contains("\"folder\""))
            {
                var serial = JsonSerializer.Deserialize<Voice[]>(playlist, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                });

                if (serial == null || serial.Length == 0)
                    return null;

                return new EmbedModel() { serial = serial };
            }
            else
            {
                var movies = JsonSerializer.Deserialize<Episode[]>(playlist, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                });

                if (movies == null || movies.Length == 0)
                    return null;

                return new EmbedModel() { movies = movies };
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "FanCDN", "id_2888z5nz");
        }

        return null;
    }
    #endregion

    #region Html
    public ITplResult Tpl(EmbedModel root, string imdb_id, long kinopoisk_id, string title, string original_title, int t = -1, int s = -1, bool rjson = false, VastConf vast = null, List<HeadersModel> headers = null)
    {
        if (root == null)
            return default;

        if (root.movies != null)
        {
            var mtpl = new MovieTpl(title, original_title, root.movies.Length);

            foreach (var m in root.movies)
            {
                if (string.IsNullOrEmpty(m.file))
                    continue;

                #region subtitle
                var subtitles = new SubtitleTpl();

                if (!string.IsNullOrEmpty(m.subtitles))
                {
                    // [rus]rus1.srt,[eng]eng2.srt,[eng]eng3.srt
                    var match = new Regex("\\[([^\\]]+)\\]([^\\,]+)").Match(m.subtitles);
                    while (match.Success)
                    {
                        string srt = m.file.Replace("/hls.m3u8", "/") + match.Groups[2].Value;
                        subtitles.Append(match.Groups[1].Value, onstreamfile.Invoke(srt));
                        match = match.NextMatch();
                    }
                }
                #endregion

                mtpl.Append(
                    m.title,
                    onstreamfile.Invoke(m.file),
                    subtitles: subtitles,
                    vast: vast,
                    headers: headers
                );
            }

            return mtpl;
        }
        else
        {
            #region Сериал
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (s == -1)
            {
                #region Сезоны
                var tpl = new SeasonTpl();
                var hash = new HashSet<int>(20);

                foreach (var voice in root.serial.OrderBy(i => i.seasons))
                {
                    if (hash.Add(voice.seasons))
                    {
                        tpl.Append(
                            $"{voice.seasons} сезон",
                            host + $"lite/fancdn?rjson={rjson}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&s={voice.seasons}",
                            voice.seasons
                        );
                    }
                }

                return tpl;
                #endregion
            }
            else
            {
                #region Перевод
                var vtpl = new VoiceTpl();

                foreach (var voice in root.serial)
                {
                    if (s != voice.seasons)
                        continue;

                    if (t == -1)
                        t = voice.id;

                    vtpl.Append(
                        voice.title,
                        t == voice.id,
                        host + $"lite/fancdn?rjson={rjson}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&s={s}&t={voice.id}"
                    );
                }
                #endregion

                var episodes = root.serial.First(i => i.id == t).folder[s.ToString()].folder;

                var etpl = new EpisodeTpl(vtpl, episodes.Count);
                string sArhc = s.ToString();

                foreach (var episode in episodes)
                {
                    etpl.Append(
                        $"{episode.Key} серия",
                        title ?? original_title,
                        sArhc,
                        episode.Key,
                        onstreamfile.Invoke(episode.Value.file),
                        vast: vast,
                        headers: headers
                    );
                }

                return etpl;
            }
            #endregion
        }
    }
    #endregion
}
