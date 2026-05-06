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

namespace Ashdi;

public struct AshdiInvoke
{
    #region AshdiInvoke
    string host;
    HttpHydra httpHydra;
    Func<string, string> onstreamfile;
    public AshdiInvoke(string host, HttpHydra httpHydra, Func<string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region Embed
    public async Task<EmbedModel> Embed(string iframeUri)
    {
        EmbedModel result = null;

        await httpHydra.GetSpan(iframeUri, content =>
        {
            if (!content.Contains("new Playerjs", StringComparison.Ordinal))
                return;

            if (!Regex.IsMatch(content, "file:([\t ]+)?'\\[\\{"))
            {
                var rx = Rx.Split("new Playerjs", content);
                if (1 > rx.Count)
                    return;

                result = new EmbedModel()
                {
                    content = rx[1].ToString()
                };

                return;
            }
            else
            {
                try
                {
                    var root = JsonSerializer.Deserialize<Voice[]>(Rx.Match(content, "file:([\t ]+)?'([^\n\r]+)',", 2), new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true
                    });

                    if (root != null && root.Length > 0)
                        result = new EmbedModel() { serial = root };
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId}", "Ashdi", "id_4egjgt5u");
                }
            }
        });

        return result;
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(EmbedModel md, string uri, string title, string original_title, int t, int s, VastConf vast = null, bool rjson = false)
    {
        if (md == null || md.IsEmpty || (string.IsNullOrEmpty(md.content) && md.serial == null))
            return default;

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);
        string enc_uri = HttpUtility.UrlEncode(uri);

        string fixStream(string _l) => _l.Replace("0yql3tj", "oyql3tj");

        if (md.content != null)
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, 1);

            string hls = Regex.Match(md.content, "file:([\t ]+)?(\"|')([\t ]+)?(?<hls>https?://[^\"'\n\r\t ]+/index.m3u8)").Groups["hls"].Value;
            if (string.IsNullOrEmpty(hls))
                return default;

            #region subtitle
            SubtitleTpl subtitles = null;
            string subtitle = new Regex("subtitle(\")?:\"([^\"]+)\"").Match(md.content).Groups[2].Value;

            if (!string.IsNullOrEmpty(subtitle))
            {
                var match = new Regex("\\[([^\\]]+)\\](https?://[^\\,]+)").Match(subtitle);
                subtitles = new SubtitleTpl(match.Length);

                while (match.Success)
                {
                    subtitles.Append(match.Groups[1].Value, onstreamfile.Invoke(fixStream(match.Groups[2].Value)));
                    match = match.NextMatch();
                }
            }
            #endregion

            mtpl.Append(
                "По умолчанию",
                onstreamfile.Invoke(fixStream(hls)),
                subtitles: subtitles,
                vast: vast
            );

            return mtpl;
            #endregion
        }
        else
        {
            #region Сериал
            try
            {
                if (s == -1)
                {
                    var tpl = new SeasonTpl();
                    var hashseason = new HashSet<string>(20);

                    foreach (var voice in md.serial)
                    {
                        foreach (var season in voice.folder)
                        {
                            if (hashseason.Add(season.title))
                            {
                                string numberseason = Regex.Match(season.title, "([0-9]+)$").Groups[1].Value;
                                if (string.IsNullOrEmpty(numberseason))
                                    continue;

                                tpl.Append(
                                    season.title,
                                    host + $"lite/ashdi?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&uri={enc_uri}&s={numberseason}",
                                    numberseason
                                );
                            }
                        }
                    }

                    return tpl;
                }
                else
                {
                    #region Перевод
                    var vtpl = new VoiceTpl();

                    for (int i = 0; i < md.serial.Length; i++)
                    {
                        if (md.serial[i].folder?.FirstOrDefault(i => i.title.EndsWith($" {s}")) == null)
                            continue;

                        if (t == -1)
                            t = i;

                        vtpl.Append(
                            md.serial[i].title,
                            t == i,
                            host + $"lite/ashdi?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&uri={enc_uri}&s={s}&t={i}"
                        );
                    }
                    #endregion

                    string sArch = s.ToString();
                    var episodes = md.serial[t].folder.First(i => i.title.EndsWith($" {s}")).folder;

                    var etpl = new EpisodeTpl(vtpl, episodes.Length);

                    foreach (var episode in episodes)
                    {
                        #region subtitle
                        SubtitleTpl subtitles = null;

                        if (!string.IsNullOrEmpty(episode.subtitle))
                        {
                            var match = new Regex("\\[([^\\]]+)\\](https?://[^\\,]+)").Match(episode.subtitle);
                            subtitles = new SubtitleTpl(match.Length);

                            while (match.Success)
                            {
                                subtitles.Append(match.Groups[1].Value, onstreamfile.Invoke(fixStream(match.Groups[2].Value)));
                                match = match.NextMatch();
                            }
                        }
                        #endregion

                        string file = onstreamfile.Invoke(fixStream(episode.file));
                        etpl.Append(
                            episode.title,
                            title ?? original_title,
                            sArch,
                            Regex.Match(episode.title,
                            "([0-9]+)$").Groups[1].Value,
                            file,
                            subtitles: subtitles,
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
