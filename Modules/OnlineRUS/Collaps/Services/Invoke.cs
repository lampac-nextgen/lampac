using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Collaps;

public struct CollapsInvoke
{
    #region CollapsInvoke
    string host, route;
    string apihost;
    bool dash;
    Func<string, string> onstreamfile;
    HttpHydra httpHydra;

    public CollapsInvoke(string host, string route, HttpHydra httpHydra, string apihost, bool dash, Func<string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.route = route;
        this.apihost = apihost;
        this.dash = dash;
        this.onstreamfile = onstreamfile;
        this.httpHydra = httpHydra;
    }
    #endregion

    #region Embed
    async public Task<EmbedModel> Embed(string imdb_id, long kinopoisk_id, long orid)
    {
        string url = $"{apihost}/embed/imdb/{imdb_id}";

        if (kinopoisk_id > 0)
            url = $"{apihost}/embed/kp/{kinopoisk_id}";

        if (orid > 0)
            url = $"{apihost}/embed/movie/{orid}";

        EmbedModel embed = null;

        await httpHydra.GetSpan(url, content =>
        {
            if (!content.Contains("seasons:", StringComparison.Ordinal))
            {
                var rx = Rx.Split("makePlayer\\(\\{", content);
                if (1 > rx.Count)
                    return;

                embed = new EmbedModel()
                {
                    content = rx[1].ToString()
                };
            }
            else
            {
                try
                {
                    var root = JsonSerializer.Deserialize<RootObject[]>(Rx.Match(content, "seasons:([^\n\r]+)"), new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true
                    });

                    if (root != null && root.Length > 0)
                        embed = new EmbedModel() { serial = root };
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId}", "Collaps", "id_acefv40j");
                }
            }
        });

        return embed;
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(EmbedModel md, string imdb_id, long kinopoisk_id, long orid, string title, string original_title, int s, bool rjson = false, List<HeadersModel> headers = null, VastConf vast = null)
    {
        if (md == null)
            return default;

        if (md.content != null)
        {
            #region Фильм
            string stream = Regex.Match(md.content, "hls: +\"(https?://[^\"]+\\.m3u[^\"]+)\"").Groups[1].Value;

            if (dash)
            {
                string _dash = Regex.Match(md.content, "dasha?: +\"(https?://[^\"]+\\.mp[^\"]+)\"").Groups[1].Value;
                if (!string.IsNullOrEmpty(_dash))
                    stream = _dash;
            }

            if (string.IsNullOrEmpty(stream))
                return default;

            var mtpl = new MovieTpl(title, original_title, 1);

            string name = Regex.Match(md.content, "audio: +\\{\"names\":\\[\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(name))
                name = "По умолчанию";

            #region subtitle
            SubtitleTpl subtitles = null;

            try
            {
                string subplain = Regex.Match(md.content, "cc: +(\\[[^\n\r]+\\]),").Groups[1].Value;
                if (!string.IsNullOrEmpty(subplain))
                {
                    var subs = JsonSerializer.Deserialize<List<Cc>>(subplain.Trim(), new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true
                    });

                    if (subs != null)
                    {
                        subtitles = new SubtitleTpl(subs.Count);
                        foreach (var cc in subs)
                        {
                            if (cc.url != null)
                                subtitles.Append(cc.name, onstreamfile.Invoke(cc.url));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "Collaps", "id_d922pta7");
            }
            #endregion

            string voicename = Regex.Match(md.content, "audio: +\\{\"names\":\\[\"([^\\]]+)\\]").Groups[1].Value;
            voicename = voicename.Replace("\"", "").Replace("delete", "").Replace(",", ", ");
            voicename = Regex.Replace(voicename, "[, ]+$", "");

            mtpl.Append(
                name,
                onstreamfile.Invoke(stream.Replace("\u0026", "&")),
                subtitles: subtitles,
                voice_name: voicename,
                headers: headers,
                vast: vast
            );

            return mtpl;
            #endregion
        }
        else
        {
            #region Сериал
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            try
            {
                if (s == -1)
                {
                    var tpl = new SeasonTpl(md.serial.Length);

                    foreach (var season in md.serial.OrderBy(i => i.season))
                    {
                        tpl.Append(
                            $"{season.season} сезон",
                            host + $"{route}?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&orid={orid}&title={enc_title}&original_title={enc_original_title}&s={season.season}",
                            season.season
                        );
                    }

                    return tpl;
                }
                else
                {
                    var episodes = md.serial.FirstOrDefault(i => i.season == s)?.episodes;
                    if (episodes == null)
                        return default;

                    var etpl = new EpisodeTpl(episodes.Length);
                    string sArch = s.ToString();

                    foreach (var episode in episodes)
                    {
                        string stream = episode.hls ?? episode.dasha ?? episode.dash;
                        if (dash && (episode.dasha ?? episode.dash) != null)
                            stream = episode.dasha ?? episode.dash;

                        if (string.IsNullOrEmpty(stream) || string.IsNullOrEmpty(episode.episode))
                            continue;

                        #region voicename
                        string voicename = string.Empty;

                        if (episode.audio.names != null)
                            voicename = Regex.Replace(string.Join(", ", episode.audio.names), "[, ]+$", "");
                        #endregion

                        #region subtitle
                        var subtitles = new SubtitleTpl(episode.cc?.Length ?? 0);

                        if (episode.cc != null && episode.cc.Length > 0)
                        {
                            foreach (var cc in episode.cc)
                            {
                                if (cc.url != null)
                                    subtitles.Append(cc.name, onstreamfile.Invoke(cc.url));
                            }
                        }
                        #endregion

                        etpl.Append(
                            $"{episode.episode} серия",
                            title ?? original_title,
                            sArch,
                            episode.episode,
                            onstreamfile.Invoke(stream.Replace("\u0026", "&")),
                            subtitles: subtitles,
                            voice_name: voicename,
                            headers: headers,
                            vast: vast
                        );
                    }

                    return etpl;
                }
            }
            catch
            {
                return default;
            }
            #endregion
        }
    }
    #endregion
}
