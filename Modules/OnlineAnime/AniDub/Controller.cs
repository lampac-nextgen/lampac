using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Threading.Tasks;
using System.Web;

namespace AniDub;

public class AniDubController : BaseOnlineController
{
    AniDubInvoke oninvk;

    public AniDubController() : base(ModInit.conf)
    {
        requestInitialization = () =>
        {
            oninvk = new AniDubInvoke(init, httpHydra);
        };
    }

    // Ссылка на серию: у общего плеера это хеш, у sibnet — videoid с приметным
    // префиксом, чтобы Streams развёл их не заглядывая в кэш.
    static string EpisodeRef(AnidubEpisode episode)
    {
        return episode.sib != null
            ? AnidubConf.SibnetPrefix + episode.sib
            : HttpUtility.UrlEncode(episode.hash);
    }

    #region Index
    [HttpGet, Staticache(manually: true)]
    [Route("lite/anidub")]
    async public Task<ActionResult> Index(string title, string original_title, short year = 0, byte serial = 0, short s = -1, string u = null, bool checksearch = false, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        if (serial != 1)
        {
            #region Фильм
            var movie = await oninvk.Movie(title, original_title, year);
            if (movie == null || movie.episodes.Count == 0)
                return OnError();

            var mtpl = new MovieTpl(title, original_title, 1);

            string quality = string.IsNullOrEmpty(movie.quality) ? null : $"&q={HttpUtility.UrlEncode(movie.quality)}";
            string link = $"{host}/lite/anidub/play?h={EpisodeRef(movie.episodes[0])}{quality}&title={enc_title}";

            mtpl.Append("AniDUB", link, "call", voice_name: "AniDUB");
            return ContentTpl(mtpl);
            #endregion
        }

        if (s == -1)
        {
            #region Сезоны
            var seasons = await oninvk.Seasons(title, original_title, year, checksearch);
            if (seasons == null || seasons.Count == 0)
                return OnError();

            var tpl = new SeasonTpl(seasons.Count);

            foreach (var season in seasons)
            {
                tpl.Append(
                    $"{season.num} сезон",
                    $"{host}/lite/anidub?serial=1&rjson={rjson}&title={enc_title}&original_title={enc_original_title}&year={year}&u={HttpUtility.UrlEncode(season.url)}&s={season.num}",
                    season.num
                );
            }

            return ContentTpl(tpl);
            #endregion
        }
        else
        {
            #region Серии
            var season = await oninvk.Season(u, s);
            if (season == null || season.episodes.Count == 0)
                return OnError();

            var etpl = new EpisodeTpl();

            string quality = string.IsNullOrEmpty(season.quality) ? null : $"&q={HttpUtility.UrlEncode(season.quality)}";

            foreach (var episode in season.episodes)
            {
                string hash = EpisodeRef(episode);

                string link = $"{host}/lite/anidub/play?h={hash}{quality}&title={enc_title}";
                string streamlink = $"{host}/lite/anidub/play.m3u8?h={hash}{quality}&title={enc_title}&play=true";

                etpl.Append(
                    $"{episode.num} серия",
                    title ?? original_title,
                    s,
                    (short)episode.num,
                    link,
                    "call",
                    voice_name: "AniDUB",
                    streamlink: accsArgs(streamlink)
                );
            }

            return ContentTpl(etpl);
            #endregion
        }
    }
    #endregion

    #region Play
    [HttpGet, Staticache(manually: true)]
    [Route("lite/anidub/play")]
    [Route("lite/anidub/play.m3u8")]
    async public Task<ActionResult> Play(string h, string q, string title, bool play = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string ua = Request.Headers["user-agent"].ToString();
        if (string.IsNullOrWhiteSpace(ua))
            ua = AnidubConf.UA;

        var result = await oninvk.Streams(h, q, ua);
        if (result == null || result.variants.Count == 0)
            return OnError();

        var headers_stream = HeadersModel.Init(
            ("user-agent", ua),
            ("accept", "*/*"),
            ("referer", $"{result.player}/"),
            ("origin", result.player)
        );

        var streamquality = new StreamQualityTpl();

        foreach (var variant in result.variants)
            streamquality.Append(HostStreamProxy(variant.url, headers: headers_stream), variant.label);

        string stream = HostStreamProxy(result.variants[0].url, headers: headers_stream);

        if (play)
            return RedirectToPlay(stream);

        return ContentTo(VideoTpl.ToJson(
            "play",
            stream,
            string.IsNullOrWhiteSpace(title) ? "AniDUB" : title,
            streamquality: streamquality,
            quality: result.variants[0].label,
            vast: init.vast,
            hls_manifest_timeout: 30000,
            headers: init.streamproxy ? null : headers_stream,
            httpContext: HttpContext
        ));
    }
    #endregion
}
