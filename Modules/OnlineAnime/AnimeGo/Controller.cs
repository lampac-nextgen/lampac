using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared.Attributes;
using Shared.Services.RxEnumerate;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace AnimeGo;

public class AnimeGoController : BaseOnlineController
{
    public AnimeGoController() : base(ModInit.conf) { }

    [HttpGet]
    [Staticache]
    [Route("lite/animego")]
    async public Task<ActionResult> Index(string title, int year, int pid, int s, string t, bool similar = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError();

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (pid == 0)
        {
            #region Поиск
            var cache = await InvokeCacheResult<List<(string title, string year, string pid, string s, string img)>>($"animego:search:{title}", TimeSpan.FromHours(4), async e =>
            {
                string search = await httpHydra.Get($"{init.host}/search/anime?q={HttpUtility.UrlEncode(title)}");
                if (search == null)
                    return e.Fail("search", refresh_proxy: true);

                var rx = Rx.Split("class=\"p-poster__stack\"", search, 1);

                string stitle = SearchNameTo.Convert(title);
                var catalog = new List<(string title, string year, string pid, string s, string img)>(rx.Count);

                foreach (var row in rx.Rows())
                {
                    string player_id = row.Match("data-ajax-url=\"/[^\"]+-([0-9]+)\"");
                    string name = row.Match("card-title text-truncate\"><a [^>]+>([^<]+)<");
                    string animeyear = row.Match("class=\"anime-year\"><a [^>]+>([0-9]{4})<");
                    string img = row.Match("data-original=\"([^\"]+)\"");
                    if (string.IsNullOrEmpty(img))
                        img = null;

                    if (!string.IsNullOrWhiteSpace(player_id) && SearchNameTo.Contains(name, stitle))
                    {
                        string season = "0";
                        if (animeyear == year.ToString() && SearchNameTo.Equals(name, stitle))
                            season = "1";

                        catalog.Add((name, row.Match(">([0-9]{4})</a>"), player_id, season, img));
                    }
                }

                if (catalog.Count == 0)
                    return e.Fail("catalog");

                return e.Success(catalog);
            });

            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            if (!similar && cache.Value.Count == 1)
                return LocalRedirect(accsArgs($"/lite/animego?title={HttpUtility.UrlEncode(title)}&pid={cache.Value[0].pid}&s={cache.Value[0].s}"));

            var stpl = new SimilarTpl(cache.Value.Count);

            foreach (var res in cache.Value)
            {
                stpl.Append(
                    res.title,
                    res.year,
                    string.Empty,
                    $"{host}/lite/animego?title={HttpUtility.UrlEncode(title)}&pid={res.pid}&s={res.s}",
                    PosterApi.Size(res.img)
                );
            }

            return ContentTpl(stpl);
            #endregion
        }
        else
        {
            #region Серии
            var cache = await InvokeCacheResult<(string translation, List<(string episode, string uri)> links, List<(string name, string id)> translations)>($"animego:playlist:{pid}", 30, async e =>
            {
                #region content
                var player = await httpHydra.Get<JObject>($"{init.host}/anime/{pid}/player?_allow=true", addheaders: HeadersModel.Init(
                    ("cache-control", "no-cache"),
                    ("dnt", "1"),
                    ("pragma", "no-cache"),
                    ("referer", $"{init.host}/"),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "same-origin"),
                    ("x-requested-with", "XMLHttpRequest")
                ));

                string content = player?.Value<string>("content");
                if (string.IsNullOrWhiteSpace(content))
                    return e.Fail("content", refresh_proxy: true);
                #endregion

                var g = Regex.Match(content, "data-player=\"(https?:)?//(aniboom\\.[^/]+)/embed/([^\"\\?&]+)\\?episode=1\\&amp;translation=([0-9]+)\"").Groups;
                if (string.IsNullOrWhiteSpace(g[2].Value) || string.IsNullOrWhiteSpace(g[3].Value) || string.IsNullOrWhiteSpace(g[4].Value))
                    return e.Fail("player");

                (string translation, List<(string episode, string uri)> links, List<(string name, string id)> translations) result = default;

                #region links
                var match = Regex.Match(content, "data-episode=\"([0-9]+)\"");
                result.links = new List<(string episode, string uri)>(match.Length);

                while (match.Success)
                {
                    if (!string.IsNullOrWhiteSpace(match.Groups[1].Value))
                        result.links.Add((match.Groups[1].Value, $"video.m3u8?host={g[2].Value}&token={g[3].Value}&e={match.Groups[1].Value}"));

                    match = match.NextMatch();
                }

                if (result.links.Count == 0)
                    return e.Fail("links");
                #endregion

                #region translation / translations
                match = Regex.Match(content, "data-player=\"(https?:)?//aniboom\\.[^/]+/embed/[^\"\\?&]+\\?episode=[0-9]+\\&amp;translation=([0-9]+)\"[\n\r\t ]+data-provider=\"[0-9]+\"[\n\r\t ]+data-provide-dubbing=\"([0-9]+)\"");

                result.translation = g[4].Value;
                result.translations = new List<(string name, string id)>(match.Length);

                while (match.Success)
                {
                    if (!string.IsNullOrWhiteSpace(match.Groups[2].Value) && !string.IsNullOrWhiteSpace(match.Groups[3].Value))
                    {
                        string name = Regex.Match(content, $"data-dubbing=\"{match.Groups[3].Value}\"><span [^>]+>[\n\r\t ]+([^\n\r<]+)").Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            result.translations.Add((name, match.Groups[2].Value));
                    }

                    match = match.NextMatch();
                }
                #endregion

                return e.Success(result);
            });

            return ContentTpl(cache, () =>
            {
                #region Перевод
                var vtpl = new VoiceTpl(cache.Value.translations.Count);
                if (string.IsNullOrWhiteSpace(t))
                    t = cache.Value.translation;

                foreach (var translation in cache.Value.translations)
                {
                    vtpl.Append(
                        translation.name,
                        t == translation.id,
                        $"{host}/lite/animego?pid={pid}&title={HttpUtility.UrlEncode(title)}&s={s}&t={translation.id}"
                    );
                }
                #endregion

                var etpl = new EpisodeTpl(vtpl, cache.Value.links.Count);
                var headers_stream = httpHeaders(init.host, init.headers_stream);

                foreach (var l in cache.Value.links)
                {
                    string hls = accsArgs($"{host}/lite/animego/{l.uri}&t={t ?? cache.Value.translation}");

                    etpl.Append(
                        $"{l.episode} серия",
                        title,
                        s.ToString(),
                        l.episode,
                        hls,
                        "play",
                        headers: headers_stream
                    );
                }

                return etpl;
            });
            #endregion
        }
    }

    #region Video
    [HttpGet]
    [Route("lite/animego/video.m3u8")]
    async public Task<ActionResult> Video(string host, string token, string t, int e)
    {
        if (await IsRequestBlocked(rch: false, rch_check: false))
            return badInitMsg;

        var cache = await InvokeCacheResult<string>($"animego:video:{token}:{t}:{e}", 30, async result =>
        {
            string embed = await httpHydra.Get($"https://{host}/embed/{token}?episode={e}&translation={t}", addheaders: HeadersModel.Init(
                ("cache-control", "no-cache"),
                ("dnt", "1"),
                ("pragma", "no-cache"),
                ("referer", $"{init.host}/"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-origin"),
                ("x-requested-with", "XMLHttpRequest")
            ));

            if (string.IsNullOrWhiteSpace(embed))
                return result.Fail("embed", refresh_proxy: true);

            embed = embed.Replace("&quot;", "\"").Replace("\\", "");

            string hls = Regex.Match(embed, "\"hls\":\"\\{\"src\":\"(https?:)?(//[^\"]+\\.m3u8)\"").Groups[2].Value;
            if (string.IsNullOrWhiteSpace(hls))
                return result.Fail("hls", refresh_proxy: true);

            hls = "https:" + hls;

            return result.Success(hls);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return Redirect(HostStreamProxy(cache.Value));
    }
    #endregion
}
