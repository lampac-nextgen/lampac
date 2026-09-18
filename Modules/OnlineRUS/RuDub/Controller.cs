using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Threading.Tasks;
using System.Web;

namespace RuDub;

public class RuDubController : BaseOnlineController
{
    RuDubInvoke oninvk;

    public RuDubController() : base(ModInit.conf)
    {
        requestInitialization = () =>
        {
            oninvk = new RuDubInvoke(init, httpHydra);
        };
    }

    #region Index
    [HttpGet, Staticache(manually: true)]
    [Route("lite/rudub")]
    async public Task<ActionResult> Index(string title, string original_title, byte serial, short s = -1, bool checksearch = false, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (serial != 1)
            return OnError();

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        if (s == -1)
        {
            #region Сезоны
            var seasons = await oninvk.Seasons(title, original_title, checksearch);
            if (seasons == null || seasons.Count == 0)
                return OnError();

            var tpl = new SeasonTpl(seasons.Count);

            foreach (var season in seasons)
            {
                tpl.Append(
                    $"{season.num} сезон",
                    $"{host}/lite/rudub?serial=1&rjson={rjson}&title={enc_title}&original_title={enc_original_title}&s={season.num}",
                    season.num
                );
            }

            return ContentTpl(tpl);
            #endregion
        }
        else
        {
            #region Серии
            var season = await oninvk.Season(title, original_title, s);
            if (season == null || season.episodes.Count == 0)
                return OnError();

            var etpl = new EpisodeTpl();

            string quality = string.IsNullOrEmpty(season.quality) ? null : $"&q={HttpUtility.UrlEncode(season.quality)}";

            foreach (var episode in season.episodes)
            {
                string hash = HttpUtility.UrlEncode(episode.hash);

                string link = $"{host}/lite/rudub/play?h={hash}{quality}&title={enc_title}";
                string streamlink = $"{host}/lite/rudub/play.m3u8?h={hash}{quality}&title={enc_title}&play=true";

                etpl.Append(
                    $"{episode.num} серия",
                    title ?? original_title,
                    s,
                    (short)episode.num,
                    link,
                    "call",
                    voice_name: "RuDub",
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
    [Route("lite/rudub/play")]
    [Route("lite/rudub/play.m3u8")]
    async public Task<ActionResult> Play(string h, string q, string title, bool play = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string ua = Request.Headers["user-agent"].ToString();
        if (string.IsNullOrWhiteSpace(ua))
            ua = RudubConf.UA;

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
            string.IsNullOrWhiteSpace(title) ? "RuDub" : title,
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
