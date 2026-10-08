using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Redheadsound;

public class QualityLink
{
    public string link { get; set; }

    public string quality { get; set; }
}

public class VideoResult
{
    public StreamData watch { get; set; }

    public List<QualityLink> streams { get; set; } = new List<QualityLink>();
}

/// <summary>
/// RHS — только собственный плеер сайта RedHeadSound (ladoni).
/// Ни API, ни плееров общих балансеров: список сезонов и серий берётся с карточек сайта,
/// поток — из плеера сайта через Chromium. Поэтому источник показывает ровно то, что выложено у RHS.
/// </summary>
public partial class RedheadsoundController : BaseOnlineController<ModuleConf>
{
    public RedheadsoundController() : base(ModInit.conf) { }

    #region Index
    [HttpGet, Staticache(manually: true)]
    [Route("lite/redheadsound")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int serial = -1, short year = 0, int t = -1, int s = -1, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (!HttpMode && Chromium.Status == PlaywrightStatus.disabled)
            return OnError("chromium disabled");

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))
            return OnError("title");

        if (serial == -1 && s > 0)
            serial = 1;

        var cards = await SiteCards(title, original_title, year);

        if ((init.ladonimode ?? "card") == "fake")
            cards = cards.Where(c => !string.IsNullOrEmpty(c.lat)).ToList();

        if (cards.Count == 0)
            return OnError("site", refresh_proxy: true);

        if (HttpMode)
        {
            string hargs = $"&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1";
            return await IndexHttp(cards, title, original_title, serial, t, s, rjson, hargs);
        }

        bool movie = serial == 0 || (serial != 1 && cards.All(c => c.season == 0));

        if (movie)
        {
            #region Фильм
            var mcard = cards.FirstOrDefault(c => c.season == 0) ?? cards.First();
            var mtpl = new MovieTpl(title, original_title, 1);

            var (link, stream) = LadoniLink(mcard.cid, 0, 0);
            mtpl.Append(VoiceName, link, "call", stream, voice_name: "плеер сайта");

            return ContentTpl(mtpl);
            #endregion
        }

        #region Сериал
        string defaultargs = $"&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1";

        static int cardSeason(SiteCard c) => c.season > 0 ? c.season : 1;

        if (s == -1)
        {
            var seasons = cards.Select(cardSeason).Distinct().OrderBy(i => i).ToList();

            var stpl = new SeasonTpl(seasons.Count);
            foreach (int i in seasons)
                stpl.Append($"{i} сезон", $"{host}/lite/redheadsound?rjson={rjson}&s={i}{defaultargs}", i.ToString());

            return ContentTpl(stpl);
        }

        // карточка с плеером (у постов-анонсов серии его может не быть), серий — максимум по всем карточкам сезона
        var inSeason = cards.Where(c => cardSeason(c) == s).ToList();
        var scard = inSeason.Where(c => !string.IsNullOrEmpty(c.lat)).OrderByDescending(c => c.episodes).FirstOrDefault()
                    ?? inSeason.OrderByDescending(c => c.episodes).FirstOrDefault();
        if (scard == null)
            return OnError("season");

        int count = inSeason.Max(c => c.episodes);
        if (count <= 0)
            count = 1;

        // одна озвучка — плеер сайта; список озвучек нужен TelegramBot (ищет озвучку по имени и t=)
        var vtpl = new VoiceTpl(1);
        vtpl.Append(VoiceName, true, $"{host}/lite/redheadsound?rjson={rjson}&s={s}&t=1{defaultargs}");

        var etpl = new EpisodeTpl(vtpl);
        for (int e = 1; e <= count; e++)
        {
            var (link, stream) = LadoniLink(scard.cid, s, e);

            etpl.Append(
                $"{e} серия",
                title ?? original_title,
                s.ToString(),
                e.ToString(),
                link,
                "call",
                voice_name: VoiceSite,
                streamlink: stream
            );
        }

        return ContentTpl(etpl);
        #endregion
    }
    #endregion

    (string link, string stream) LadoniLink(string cid, int s, int e)
    {
        string q = $"?cid={cid}&s={s}&e={e}";
        return ($"{host}/lite/redheadsound/ladoni{q}", accsArgs($"{host}/lite/redheadsound/ladoni.m3u8{q}&play=true"));
    }

    /// <summary>Ответ Lampa: список качеств через stream-proxy с заголовками плеера.</summary>
    ActionResult Reply(VideoResult cache, bool play)
    {
        var streamquality = new StreamQualityTpl();
        foreach (var item in cache.streams)
            streamquality.Append(HostStreamProxy(item.link, userdata: cache.watch), item.quality);

        var first = streamquality.Firts();
        if (first == null)
            return OnError();

        if (play)
            return Redirect(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "auto",
            streamquality: streamquality,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }

    internal static string Norm(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.ToLowerInvariant().Replace('ё', 'е');
        return Regex.Replace(s, "[^\\p{L}\\p{Nd}]+", "");
    }
}
