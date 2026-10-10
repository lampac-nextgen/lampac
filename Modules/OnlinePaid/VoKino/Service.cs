using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace VoKino;

public struct VoKinoInvoke
{
    #region VoKinoInvoke
    string host;
    string apihost;
    string token;
    HttpHydra httpHydra;
    Func<string, string> onstreamfile;

    static readonly Regex SeasonOrEpisodeNum = new(@"\d+", RegexOptions.Compiled);
    static readonly Regex EpisodeIdent = new(@"\d+$", RegexOptions.Compiled);

    public VoKinoInvoke(string host, string apihost, string token, HttpHydra httpHydra, Func<string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.apihost = apihost;
        this.token = token;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region Embed
    public async Task<EmbedModel> Embed(string origid, long kinopoisk_id, string balancer, string t)
    {
        try
        {
            if (string.IsNullOrEmpty(balancer))
            {
                var json = await httpHydra.Get<JsonElement>($"{apihost}/v2/view/{origid ?? kinopoisk_id.ToString()}?token={token}", safety: true, textJson: true);

                if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("online", out var onlineProp) || onlineProp.ValueKind != JsonValueKind.Object)
                    return new EmbedModel() { IsEmpty = true };

                var online = onlineProp.EnumerateObject();
                if (!online.Any())
                    return new EmbedModel() { IsEmpty = true };

                var similars = new List<Similar>(10);

                foreach (var item in online)
                {
                    if (item.Value.ValueKind != JsonValueKind.Object || !item.Value.TryGetProperty("playlist_url", out var playlistProp))
                        continue;

                    string playlistUrl = playlistProp.GetString();
                    if (string.IsNullOrEmpty(playlistUrl))
                        continue;

                    var model = new Similar()
                    {
                        title = item.Name,
                        balancer = item.Name.ToLowerInvariant()
                    };

                    if (item.Name == "Vokino")
                        similars.Insert(0, model);
                    else
                        similars.Add(model);
                }

                return new EmbedModel() { similars = similars };
            }
            else
            {
                string uri = $"{apihost}/v2/online/{balancer}/{origid ?? kinopoisk_id.ToString()}?token={token}";
                if (!string.IsNullOrEmpty(t))
                {
                    try
                    {
                        string safeBase64 = t.Replace(" ", "+");
                        string decodedQuery = Encoding.UTF8.GetString(Convert.FromBase64String(safeBase64));
                        uri += $"&{decodedQuery}";
                    }
                    catch
                    {
                        uri += $"&{t}";
                    }
                }

                RootObject root = null;

                // До 2 попыток с небольшой паузой при сбое 503 / 429 или пустом ответе
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    root = await httpHydra.Get<RootObject>(uri, safety: true, textJson: true);

                    if (root?.channels != null && root.channels.Length > 0)
                        break;

                    if (attempt == 0)
                        await Task.Delay(300);
                }

                if (root?.channels == null || root.channels.Length == 0)
                    return new EmbedModel() { IsEmpty = true };

                return new EmbedModel() { menu = root.menu, channels = root.channels };
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "VoKino", "id_tfj170h1");
        }

        return new EmbedModel() { IsEmpty = true };
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(EmbedModel result, string origid, long kinopoisk_id, string title, string original_title, string balancer, string t, short s, VastConf vast = null, bool rjson = false)
    {
        if (result == null || result.IsEmpty)
            return default;

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        #region similar
        if (result.similars != null)
        {
            var stpl = new SimilarTpl(result.similars.Count);

            foreach (var similar in result.similars)
            {
                string link = host + $"lite/vokino?rjson={rjson}&origid={origid}&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&balancer={similar.balancer}";

                stpl.Append(
                    similar.title,
                    string.Empty,
                    string.Empty,
                    link
                );
            }

            return stpl;
        }
        #endregion

        if (result.channels == null || result.channels.Length == 0)
            return default;

        #region Переводы
        var voices = result.menu?.FirstOrDefault(i => i.title is "Перевод" or "Переводы" or "Озвучка" or "Озвучки")?.submenu;
        var vtpl = new VoiceTpl(voices != null ? voices.Length : 0);

        string voice_s = (result.channels.First().playlist_url is "submenu" or "sumbenu") ? "-1" : s.ToString();

        if (voices != null && voices.Length > 0)
        {
            foreach (var translation in voices)
            {
                string _t = string.Empty;

                if (!string.IsNullOrEmpty(translation.playlist_url) && translation.playlist_url.Contains("?"))
                {
                    string queryPart = translation.playlist_url.Split("?")[1];
                    _t = HttpUtility.UrlEncode(Convert.ToBase64String(Encoding.UTF8.GetBytes(queryPart)));
                }

                vtpl.Append(
                    translation.title,
                    translation.selected,
                    host + $"lite/vokino?rjson={rjson}&origid={origid}&kinopoisk_id={kinopoisk_id}&balancer={balancer}&title={enc_title}&original_title={enc_original_title}&t={_t}&s={voice_s}"
                );
            }
        }
        #endregion

        if (result.channels.First().playlist_url is "submenu" or "sumbenu")
        {
            if (s == -1)
            {
                var tpl = new SeasonTpl(quality: result.channels[0].quality_full?.Replace("2160p.", "4K "), result.channels.Length);
                string encoded_t = HttpUtility.UrlEncode(t ?? string.Empty);

                for (int i = 0; i < result.channels.Length; i++)
                {
                    var ch = result.channels[i];
                    string chTitle = ch.title ?? string.Empty;

                    string sname = SeasonOrEpisodeNum.Match(chTitle).Value;
                    if (string.IsNullOrEmpty(sname))
                        sname = (i + 1).ToString();

                    tpl.Append(
                        ch.title,
                        host + $"lite/vokino?rjson={rjson}&origid={origid}&kinopoisk_id={kinopoisk_id}&balancer={balancer}&title={enc_title}&original_title={enc_original_title}&t={encoded_t}&s={sname}",
                        sname
                    );
                }

                return tpl;
            }
            else
            {
                var seasonMatch = result.channels.FirstOrDefault(i => i.title != null && SeasonOrEpisodeNum.Match(i.title).Value == s.ToString());
                if (seasonMatch == null && result.channels.Length > s - 1 && s > 0)
                    seasonMatch = result.channels[s - 1];

                if (seasonMatch?.submenu == null)
                    return default;

                var series = seasonMatch.submenu;
                var etpl = new EpisodeTpl(vtpl, series.Length);

                for (int i = 0; i < series.Length; i++)
                {
                    var e = series[i];
                    string epNum = EpisodeIdent.Match(e.ident ?? string.Empty).Value;
                    if (string.IsNullOrEmpty(epNum))
                        epNum = SeasonOrEpisodeNum.Match(e.title ?? string.Empty).Value;
                    if (string.IsNullOrEmpty(epNum))
                        epNum = (i + 1).ToString();

                    string playUrl = null;

                    if (!string.IsNullOrEmpty(e.stream_url))
                    {
                        playUrl = onstreamfile(e.stream_url);
                    }
                    else if (!string.IsNullOrEmpty(e.data_url))
                    {
                        playUrl = host + $"lite/vokino/stream?url={HttpUtility.UrlEncode(e.data_url)}";
                    }

                    if (string.IsNullOrEmpty(playUrl))
                        continue;

                    etpl.Append(
                        e.title,
                        title ?? original_title,
                        s,
                        epNum,
                        playUrl,
                        vast: vast
                    );
                }

                return etpl;
            }
        }
        else
        {
            var mtpl = new MovieTpl(title, original_title, vtpl, result.channels.Length);

            foreach (var ch in result.channels)
            {
                string streamUrl = ch.stream_url;
                if (string.IsNullOrEmpty(streamUrl) && !string.IsNullOrEmpty(ch.data_url))
                    streamUrl = host + $"lite/vokino/stream?url={HttpUtility.UrlEncode(ch.data_url)}";

                if (string.IsNullOrEmpty(streamUrl))
                    continue;

                string name = !string.IsNullOrWhiteSpace(ch.title) ? ch.title : (ch.quality_full ?? "AUTO");
                name = name.Replace("2160p.", "4K ");

                if (!string.IsNullOrEmpty(ch.quality_full) && !name.Contains(ch.quality_full) && !name.Contains("4K"))
                    name += $" ({ch.quality_full.Replace("2160p.", "4K ")})";

                if (ch.extra != null && ch.extra.TryGetValue("size", out string size) && !string.IsNullOrEmpty(size))
                    name += $" - {size}";

                mtpl.Append(
                    name,
                    onstreamfile(streamUrl),
                    vast: vast
                );
            }

            return mtpl;
        }
    }
    #endregion
}
