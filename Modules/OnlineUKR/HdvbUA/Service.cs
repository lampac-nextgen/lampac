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

namespace HdvbUA;

public struct HdvbUAInvoke
{
    #region HdvbUAInvoke
    string host;
    HttpHydra httpHydra;
    Func<string, string> onstreamfile;
    public HdvbUAInvoke(string host, HttpHydra httpHydra, Func<string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region Embed
    public async Task<EmbedModel> Embed(string iframeUri)
    {
        var result = new EmbedModel();

        await httpHydra.GetSpan(iframeUri, content =>
        {
            if (!content.Contains("file:", StringComparison.Ordinal))
                return;

            if (Regex.IsMatch(content, "file: ?'\\["))
            {
                try
                {
                    var root = JsonSerializer.Deserialize<List<Voice>>(Rx.Match(content, "file: ?'([^\n\r]+)',"), new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true
                    });

                    if (root != null && root.Count > 0)
                        result.serial = root;
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId}", "HdvbUA", "id_vq7wnuwy");
                }
            }
            else
            {
                result.content = content.ToString();
            }
        });

        if (result.serial == null && string.IsNullOrEmpty(result.content))
            return null;

        return result;
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(EmbedModel result, string title, string original_title, int t, int s, string uri, VastConf vast = null, bool rjson = false)
    {
        if (result == null || result.IsEmpty)
            return default;

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        if (result.content != null)
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title);

            string hls = Regex.Match(result.content, "file: ?\"(https?://[^\"]+/index.m3u8)\"").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(hls))
                return default;

            #region subtitle
            SubtitleTpl subtitles = new SubtitleTpl();
            string subtitle = new Regex("subtitle: ?\"([^\"]+)\"").Match(result.content).Groups[1].Value;

            if (!string.IsNullOrEmpty(subtitle))
            {
                var match = new Regex("\\[([^\\]]+)\\](https?://[^\\,]+)").Match(subtitle);
                while (match.Success)
                {
                    subtitles.Append(match.Groups[1].Value, onstreamfile.Invoke(match.Groups[2].Value));
                    match = match.NextMatch();
                }
            }
            #endregion

            mtpl.Append(
                "По умолчанию",
                onstreamfile.Invoke(hls),
                subtitles: subtitles,
                vast: vast
            );

            return mtpl;
            #endregion
        }
        else
        {
            #region Сериал
            string enc_uri = HttpUtility.UrlEncode(uri);

            try
            {
                if (s == -1)
                {
                    #region Сезоны
                    var tpl = new SeasonTpl();

                    foreach (var season in result.serial)
                    {
                        string numberseason = Regex.Match(season.title, "^([0-9]+)").Groups[1].Value;
                        if (string.IsNullOrEmpty(numberseason))
                            continue;

                        tpl.Append(
                            season.title,
                            host + $"lite/hdvbua?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&uri={enc_uri}&s={numberseason}",
                            numberseason
                        );
                    }

                    return tpl;
                    #endregion
                }
                else
                {
                    var season = result.serial.First(i => i.title.StartsWith($"{s} "));

                    #region Перевод
                    var vtpl = new VoiceTpl();
                    var hash = new HashSet<string>();

                    for (int i = 0; i < season.folder.Length; i++)
                    {
                        if (hash.Add(season.folder[i].title))
                        {
                            if (t == -1)
                                t = i;

                            vtpl.Append(
                                season.folder[i].title,
                                t == i,
                                host + $"lite/hdvbua?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&uri={enc_uri}&s={s}&t={i}"
                            );
                        }
                    }
                    #endregion

                    string sArch = s.ToString();
                    var episodes = season.folder[t].folder;

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
                                subtitles.Append(match.Groups[1].Value, onstreamfile.Invoke(match.Groups[2].Value));
                                match = match.NextMatch();
                            }
                        }
                        #endregion

                        string file = onstreamfile.Invoke(episode.file);
                        etpl.Append(
                            episode.title,
                            title ?? original_title,
                            sArch,
                            Regex.Match(episode.title,
                            "^([0-9]+)").Groups[1].Value,
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
