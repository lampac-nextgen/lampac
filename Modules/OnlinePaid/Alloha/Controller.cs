using Microsoft.AspNetCore.Mvc;
using Shared.Attributes;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace Alloha;

public class AllohaController : BaseOnlineController<ModuleConf>
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    List<HeadersModel> bearer;

    public AllohaController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);

            bearer = HeadersModel.Init(
                ("Authorization", $"Bearer {init.token}"),
                ("Accept", "application/json")
            );
        };

        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("m4s"))
                i.m4s = c.m4s;

            if (j.ContainsKey("reserve"))
                i.reserve = c.reserve;

            i.secret_token = c.secret_token;
            i.token = c.token;

            return i;
        };
    }

    [HttpGet]
    [Staticache]
    [Route("lite/alloha")]
    async public Task<ActionResult> Index(string orid, string imdb_id, long kinopoisk_id, string title, string original_title, int serial, string original_language, int year, int t = -1, int s = -1, bool rjson = false, bool similar = false)
    {
        if (string.IsNullOrEmpty(orid))
        {
            if (similar || (0 >= kinopoisk_id && string.IsNullOrEmpty(imdb_id)))
                return await RouteToSpiderSearch(title);
        }

        if (await IsRequestBlocked(rch: !string.IsNullOrEmpty(init.secret_token)))
            return badInitMsg;

        #region search
        rhubFallback:

        string memKey = string.IsNullOrEmpty(orid)
            ? $"alloha:search:{imdb_id}:{kinopoisk_id}"
            : $"alloha:search:{orid}";

        var cache = await InvokeCacheResult<ContentRoot>(memKey, TimeSpan.FromHours(4), async e =>
        {
            ContentRoot root = !string.IsNullOrEmpty(orid)
                ? await httpHydra.Get<ContentRoot>($"{init.apihost}/movies/token/{orid}", safety: true, newheaders: bearer)
                : await httpHydra.Get<ContentRoot>($"{init.apihost}/movies/search?imdb={imdb_id}&kp={kinopoisk_id}", safety: true, newheaders: bearer);

            if (root?.data?.category?.slug != null)
                return e.Success(root);

            return e.Fail("root", refresh_proxy: true);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);
        #endregion

        MediaItem data = cache.Value.data;
        string defaultargs = $"&orid={orid}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&year={year}&original_language={original_language}";

        if (cache.Value.data.category.slug is "movie" or "anime")
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title);
            bool directors_cut = data.flags.directors_cut;

            foreach (var translation in data.translations)
            {
                int trId = translation.id;
                string link = $"{host}/lite/alloha/video?t={trId}&token_movie={data.token}" + defaultargs;
                string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                bool uhd = translation.uhd && init.m4s;
                string quality = uhd ? "2160p" : translation.quality;

                if (directors_cut && trId == 66)
                {
                    mtpl.Append(
                        "Режиссерская версия",
                        $"{link}&directors_cut=true",
                        "call",
                        $"{streamlink}&directors_cut=true",
                        voice_name: quality,
                        quality: uhd ? "2160p" : ""
                    );
                }

                mtpl.Append(
                    translation.name,
                    link,
                    "call",
                    streamlink,
                    voice_name: quality,
                    quality: uhd ? "2160p" : ""
                );
            }

            return ContentTpl(mtpl);
            #endregion
        }
        else
        {
            #region Сериал
            if (s == -1)
            {
                var tpl = new SeasonTpl(data.flags.uhd && init.m4s ? "2160p" : null);

                foreach (var season in data.seasons.OrderBy(x => x.season))
                {
                    tpl.Append(
                        $"{season.season} сезон",
                        $"{host}/lite/alloha?rjson={rjson}&s={season.season}{defaultargs}",
                        season.season.ToString()
                    );
                }

                return ContentTpl(tpl);
            }
            else
            {
                #region Перевод
                var vtpl = new VoiceTpl();
                var temp_translation = new HashSet<int>();

                int activTranslate = t;
                var selectedSeason = data.seasons?.FirstOrDefault(x => x.season == s);
                if (selectedSeason == null)
                    return OnError("selectedSeason");

                foreach (var episode in selectedSeason.episodes)
                {
                    foreach (var translation in episode.translations)
                    {
                        if (translation?.id > 0)
                        {
                            if (temp_translation.Add(translation.id))
                            {
                                if (activTranslate == -1)
                                    activTranslate = translation.id;

                                vtpl.Append(
                                    translation.name,
                                    activTranslate == translation.id,
                                    $"{host}/lite/alloha?rjson={rjson}&s={s}&t={translation.id}{defaultargs}"
                                );
                            }
                        }
                    }
                }
                #endregion

                var etpl = new EpisodeTpl(vtpl);
                string sArhc = s.ToString();

                foreach (var episode in selectedSeason.episodes)
                {
                    if (episode.translations != null && !episode.translations.Any(x => x.id == activTranslate))
                        continue;

                    string episodeNum = episode.episode.ToString();
                    string link = $"{host}/lite/alloha/video?t={activTranslate}&s={s}&e={episodeNum}&token_movie={data.token}" + defaultargs;
                    string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                    etpl.Append(
                        $"{episodeNum} серия",
                        title ?? original_title,
                        sArhc,
                        episodeNum,
                        link,
                        "call",
                        streamlink: streamlink
                    );
                }

                return ContentTpl(etpl);
            }
            #endregion
        }
    }


    #region Video
    [HttpGet]
    [Route("lite/alloha/video")]
    [Route("lite/alloha/video.m3u8")]
    async public Task<ActionResult> Video(string token_movie, string title, string original_title, string t, int s, int e, bool play, bool directors_cut)
    {
        if (await IsRequestBlocked(rch: !string.IsNullOrEmpty(init.secret_token), rch_check: !play))
            return badInitMsg;

        var cache = await InvokeCacheResult<DirectData>($"alloha:view:stream:{init.secret_token}:{token_movie}:{t}:{s}:{e}:{init.m4s}:{directors_cut}", 20, async cacheEntry =>
        {
            string userIp = requestInfo.IP;
            if (init.localip || init.streamproxy)
            {
                userIp = await mylocalip();
                if (userIp == null)
                    return cacheEntry.Fail("userIp");
            }

            #region url запроса
            string uri = $"{init.linkhost}/direct?secret_token={init.secret_token}&token_movie={token_movie}";

            uri += $"&ip={userIp}&translation={t}";

            if (s > 0)
                uri += $"&season={s}";

            if (e > 0)
                uri += $"&episode={e}";

            if (init.m4s)
                uri += "&av1=true";

            if (directors_cut)
                uri += "&directors_cut";
            #endregion

            var root = await httpHydra.Get<DirectRoot>(uri, safety: true);
            if (root?.data == null)
                return cacheEntry.Fail("data", refresh_proxy: true);

            return cacheEntry.Success(root.data);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var data = cache.Value;

        #region subtitle
        var subtitles = new SubtitleTpl();

        foreach (var sub in data.file?.tracks ?? new List<Track>())
            subtitles.Append(sub.label, sub.src);
        #endregion

        List<StreamQualityDto> streams = null;

        foreach (var hlsSource in data.file?.hlsSource ?? new List<HlsSource>())
        {
            // first or default
            if (streams == null || hlsSource.@default)
            {
                streams = new List<StreamQualityDto>(6);

                foreach (var q in hlsSource.quality ?? new Dictionary<string, string>())
                {
                    string file = q.Value;
                    if (init.reserve && hlsSource.reserve != null && hlsSource.reserve.TryGetValue(q.Key, out string reserve))
                        file += " or " + reserve;

                    streams.Add(new StreamQualityDto(HostStreamProxy(file), $"{q.Key}p"));
                }
            }
        }

        if (streams == null || streams.Count == 0)
            return OnError("streams");

        var streamquality = new StreamQualityTpl(streams);

        var first = streamquality.Firts();
        if (first == null)
            return OnError();

        if (play)
            return RedirectToPlay(first.link);

        #region segments
        var segments = new SegmentTpl();

        var dfile = data.file;
        string skipTime = dfile?.skipTime;
        string removeTime = dfile?.removeTime;

        if (skipTime != null && skipTime.Contains("-"))
        {
            foreach (string skp in skipTime.Split(","))
            {
                var range = skp.Trim().Split('-');
                if (range.Length >= 2 && int.TryParse(range[0].Trim(), out int start) && int.TryParse(range[1].Trim(), out int end))
                    segments.skip(start, end);
            }
        }

        if (removeTime != null && removeTime.Contains("-"))
        {
            foreach (string skp in removeTime.Split(","))
            {
                var range = skp.Trim().Split('-');
                if (range.Length >= 2 && int.TryParse(range[0].Trim(), out int start) && int.TryParse(range[1].Trim(), out int end))
                    segments.ad(start, end);
            }
        }
        #endregion

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            (title ?? original_title),
            streamquality: streamquality,
            vast: init.vast,
            subtitles: subtitles,
            segments: segments,
            hls_manifest_timeout: (int)TimeSpan.FromSeconds(20).TotalMilliseconds,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region RouteToSpiderSearch
    [HttpGet]
    [Route("lite/alloha-search")]
    async public Task<ActionResult> RouteToSpiderSearch(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError("title", gbcache: false);

        if (await IsRequestBlocked(rch: !string.IsNullOrEmpty(init.token)))
            return badInitMsg;

        var cache = await InvokeCacheResult<List<MediaItem>>($"alloha:search:{title}", TimeSpan.FromHours(4), async e =>
        {
            var root = await httpHydra.Get<SearchListRoot>($"{init.apihost}/movies/name/list?name={HttpUtility.UrlEncode(title)}", safety: true, newheaders: bearer);
            if (root?.data == null)
                return e.Fail("data", refresh_proxy: true);

            return e.Success(root.data);
        });

        return ContentTpl(cache, () =>
        {
            var stpl = new SimilarTpl(cache.Value.Count);

            foreach (var j in cache.Value)
            {
                stpl.Append(
                    j.name ?? j.original_name,
                    j.year.ToString(),
                    string.Empty,
                    $"{host}/lite/alloha?orid={j.token}",
                    PosterApi.Size(j.poster)
                );
            }

            return stpl;
        });
    }
    #endregion
}
