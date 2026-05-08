using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace FanCDN;

public struct FanCDNInvoke
{
    #region FanCDNInvoke
    OnlinesSettings init;
    List<Microsoft.Playwright.Cookie> cookies;
    Func<string, string> onstreamfile;

    public FanCDNInvoke(OnlinesSettings init, List<Microsoft.Playwright.Cookie> cookies, Func<string, string> onstreamfile)
    {
        this.init = init;
        this.cookies = cookies;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region Search
    async public Task<(string kp, string key)> Search(string title, string original_title, int year)
    {
        if (string.IsNullOrEmpty(title) || year == 0)
            return default;

        string search = await PlaywrightBrowser.Get(
            init,
            $"{init.host}/engine/ajax/msearch.php?q={HttpUtility.UrlEncode(title)}",
            cookies: cookies,
            headers: HeadersModel.Init(
                ("referer", $"{init.host}/"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-origin")
            )
        );

        if (string.IsNullOrEmpty(search))
            return default;

        JArray root = null;

        try
        {
            root = JsonConvert.DeserializeObject<JArray>(search);
        }
        catch { }

        if (root == null || root.Count == 0)
            return default;

        string newsUrl = null;

        string stitle = StringConvert.SearchName(title, string.Empty);
        string soriginal = StringConvert.SearchName(original_title, string.Empty);

        foreach (var item in root)
        {
            string _title = item.Value<string>("title");
            string _original_title = item.Value<string>("original_title");
            string _year = item.Value<string>("year");

            if (year.ToString() == _year || (year - 1).ToString() == _year || (year + 1).ToString() == _year)
            {
                if (stitle == StringConvert.SearchName(_title) ||
                    soriginal == StringConvert.SearchName(_original_title))
                {
                    newsUrl = item.Value<string>("url");
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(newsUrl))
            return default;

        string news = await PlaywrightBrowser.Get(init,
            init.host + newsUrl,
            cookies: cookies
        );

        if (string.IsNullOrEmpty(news))
            return default;

        var g = Regex.Match(news, "src=\"/movies/([0-9]+)\\?key=([^\"]+)\"").Groups;
        if (string.IsNullOrEmpty(g[1].Value) || string.IsNullOrEmpty(g[2].Value))
            return default;

        return (g[1].Value, g[2].Value);
    }
    #endregion

    #region Embed
    async public Task<EmbedModel> Embed(string kp, string key)
    {
        string json = await PlaywrightBrowser.Get(init,
            $"{init.host}/film.php?kp={kp}&key={key}",
            cookies: cookies,
            headers: HeadersModel.Init(
                ("referer", $"{init.host}/movies/{kp}?key={key}"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-origin")
            )
        );

        if (string.IsNullOrEmpty(json))
            return null;

        Episode[] movies = null;

        try
        {
            movies = JsonConvert.DeserializeObject<Episode[]>(json);
        }
        catch { }

        if (movies == null || movies.Length == 0)
            return null;

        return new EmbedModel() { movies = movies };
    }
    #endregion

    #region Html
    public ITplResult Tpl(EmbedModel root, string imdb_id, long kinopoisk_id, string title, string original_title, VastConf vast = null, List<HeadersModel> headers = null)
    {
        if (root == null)
            return default;

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
    #endregion
}
