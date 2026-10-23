using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Animebesst;

public class AnimebesstController : BaseOnlineController
{
    public AnimebesstController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/animebesst")]
    async public Task<ActionResult> Index(string title, string uri, short s, bool rjson = false, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        if (string.IsNullOrEmpty(uri))
        {
            if (string.IsNullOrWhiteSpace(title))
                return OnError();

            #region Поиск
            var cache = await InvokeCacheResult<List<(string title, string year, string uri, string s, string img)>>($"animebesst:search:{title}", TimeSpan.FromHours(4), async e =>
            {
                bool reqOk = false;
                List<(string title, string year, string uri, string s, string img)> catalog = null;

                string data = $"do=search&subaction=search&search_start=0&full_search=0&result_from=1&story={HttpUtility.UrlEncode(title)}";

                await httpHydra.PostSpan($"{init.host}/index.php?do=search", data, search =>
                {
                    reqOk = search.Contains(">Поиск по сайту<", StringComparison.OrdinalIgnoreCase);

                    var sidebar = Rx.Split("id=\"sidebar\"", search);
                    if (sidebar.Count == 0)
                        return;

                    var rx = Rx.Split("class=\"shortstory-listab\"", sidebar[0].Span, 1);
                    if (rx.Count == 0)
                        return;

                    catalog = new List<(string title, string year, string uri, string s, string img)>(rx.Count);

                    foreach (var row in rx.Rows())
                    {
                        if (row.Contains("Новости"))
                            continue;

                        var g = row.Groups("class=\"shortstory-listab-title\"><a href=\"(https?://[^\"]+\\.html)\">([^<]+)</a>");

                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                        {
                            string season = "0";
                            if (g[2].Value.Contains("сезон"))
                            {
                                season = Regex.Match(g[2].Value, "([0-9]+) сезон").Groups[1].Value;
                                if (string.IsNullOrEmpty(season))
                                    season = "1";
                            }

                            // сайт отдаёт постер относительным путём, без хоста proxyimg его не скачает
                            string img = row.Match("<img class=\"img-fit lozad\" data-src=\"([^\"]+)\"");
                            if (img?.StartsWith("/") == true)
                                img = init.host + img;

                            catalog.Add((
                                g[2].Value,
                                row.Match("\">([0-9]{4})</a>"),
                                g[1].Value,
                                season,
                                img
                            ));
                        }
                    }
                });


                if ((catalog == null || catalog.Count == 0) && !reqOk)
                    return e.Fail("catalog", refresh_proxy: true);

                return e.Success(catalog);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (cache.Value != null && cache.Value.Count == 0)
                return OnError();

            if (!similar && cache.Value != null && cache.Value.Count == 1)
                return LocalRedirect(accsArgs($"/lite/animebesst?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&uri={HttpUtility.UrlEncode(cache.Value[0].uri)}&s={cache.Value[0].s}"));

            return ContentTpl(cache, () =>
            {
                var stpl = new SimilarTpl(cache.Value.Count);

                foreach (var res in cache.Value)
                {
                    stpl.Append(
                        res.title,
                        res.year,
                        string.Empty,
                        $"{host}/lite/animebesst?title={HttpUtility.UrlEncode(title)}&uri={HttpUtility.UrlEncode(res.uri)}&s={res.s}",
                        PosterApi.Size(res.img)
                    );
                }

                return stpl;
            });
            #endregion
        }
        else
        {
            #region Серии
            var cache = await InvokeCacheResult<List<(string episode, string name, string uri)>>($"animebesst:playlist:{uri}", TimeSpan.FromHours(1), async e =>
            {
                var links = new List<(string episode, string name, string uri)>(5);
                bool pageLoaded = false;

                await httpHydra.GetSpan(uri, news =>
                {
                    // страница пришла, но своего плеера у релиза может не быть
                    pageLoaded = true;

                    string videoList = Rx.Match(news, "var videoList ?=([^\n\r]+)");
                    if (videoList == null)
                        return;

                    var match = Regex.Match(videoList, "\"id\":\"([0-9]+)( [^\"]+)?\",\"link\":\"(https?:)?\\\\/\\\\/([^\"]+)\"");
                    while (match.Success)
                    {
                        if (!string.IsNullOrWhiteSpace(match.Groups[1].Value) && !string.IsNullOrWhiteSpace(match.Groups[4].Value))
                            links.Add((match.Groups[1].Value, match.Groups[2].Value.Trim(), match.Groups[4].Value.Replace("\\", "")));

                        match = match.NextMatch();
                    }
                });

                if (links.Count == 0)
                    return pageLoaded ? e.Fail("novideo") : e.Fail("links", refresh_proxy: true);

                return e.Success(links);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (!cache.IsSuccess && cache.ErrorMsg == "novideo")
                return ShowError($"{init.displayname ?? init.plugin}: для этого релиза нет видеоплеера");

            return ContentTpl(cache, () =>
            {
                var etpl = new EpisodeTpl(cache.Value.Count);

                foreach (var l in cache.Value)
                {
                    string name = string.IsNullOrEmpty(l.name) ? $"{l.episode} серия" : $"{l.episode} {l.name}";
                    string voice_name = !string.IsNullOrEmpty(l.name) ? Regex.Replace(l.name, "(^\\(|\\)$)", "") : "";

                    string link = accsArgs($"{host}/lite/animebesst/video.m3u8?uri={HttpUtility.UrlEncode(l.uri)}&title={HttpUtility.UrlEncode(title)}");

                    etpl.Append(
                        name,
                        $"{title} / {name}",
                        s,
                        l.episode,
                        link,
                        "call",
                        streamlink: $"{link}&play=true",
                        voice_name: Regex.Unescape(voice_name)
                    );
                }

                return etpl;
            });
            #endregion
        }
    }

    #region Video
    [HttpGet, Staticache(manually: true)]
    [Route("lite/animebesst/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string title, bool play)
    {
        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        if (rch != null)
        {
            if (rch.IsNotConnected())
            {
                if (init.rhub_fallback && play)
                    rch.Disabled();
                else
                    return Content(rch.connectionMsg, "application/json; charset=utf-8");
            }

            if (!play && rch.IsRequiredConnected())
                return Content(rch.connectionMsg, "application/json; charset=utf-8");

            if (rch.IsNotSupport(out string rch_error))
                return ShowError(rch_error);
        }

    rhubFallback:
        var cache = await InvokeCacheResult<string>($"animebesst:video:{uri}", 30, async e =>
        {
            string hls = null;

            await httpHydra.GetSpan($"https://{uri}", addheaders: HeadersModel.Init("referer", init.host), spanAction: iframe =>
            {
                hls = Rx.Match(iframe, "file:\"(https?://[^\"]+\\.m3u8)\"");
            });

            if (string.IsNullOrEmpty(hls))
                return e.Fail("hls", refresh_proxy: true);

            return e.Success(hls);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string link = HostStreamProxy(cache.Value);

        if (play)
            return RedirectToPlay(link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            link,
            title,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }
    #endregion
}
