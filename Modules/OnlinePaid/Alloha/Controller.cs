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
using Shared.Services.Pools;

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

    [HttpGet, Staticache(manually: true)]
    [Route("lite/alloha")]
    async public Task<ActionResult> Index(string orid, string imdb_id, long kinopoisk_id, string title, string original_title, byte serial, string original_language, short year, int t = -1, short s = -1, bool rjson = false, bool similar = false)
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

                foreach (var episode in selectedSeason.episodes)
                {
                    if (episode.translations != null && !episode.translations.Any(x => x.id == activTranslate))
                        continue;

                    string episodeNum = episode.episode.ToString();
                    string link = $"{host}/lite/alloha/video?t={activTranslate}&s={s}&e={episodeNum}&token_movie={data.token}" + defaultargs;

                    etpl.Append(
                        $"{episodeNum} серия",
                        title ?? original_title,
                        s,
                        episodeNum,
                        link,
                        "call",
                        streamlink: accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true")
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
    async public Task<ActionResult> Video(string token_movie, string title, string original_title, string t, int s, short e, bool play, bool directors_cut, long kinopoisk_id = 0, string imdb_id = null)
    {
        if (await IsRequestBlocked(rch: !string.IsNullOrEmpty(init.secret_token), rch_check: !play))
            return badInitMsg;

        var cache = await InvokeCacheResult<DirectData>($"alloha:view:stream:{init.secret_token}:{init.token}:{token_movie}:{t}:{s}:{e}:{init.m4s}:{directors_cut}", 20, async cacheEntry =>
        {
            DirectData data = null;

            if (!string.IsNullOrEmpty(init.secret_token))
            {
                string userIp = requestInfo.IP;
                if (init.localip || init.streamproxy)
                {
                    userIp = await mylocalip();
                    if (userIp == null)
                        return cacheEntry.Fail("userIp");
                }

                #region url запроса
                var uri = StringBuilderPool.ThreadInstance;

                uri.Append($"{init.linkhost}/direct?secret_token={init.secret_token}&token_movie={token_movie}")
                   .Append($"&ip={userIp}&translation={t}");

                if (s > 0)
                    uri.Append($"&season={s}");

                if (e > 0)
                    uri.Append($"&episode={e}");

                if (init.m4s)
                    uri.Append("&av1=true");

                if (directors_cut)
                    uri.Append("&directors_cut");
                #endregion

                var root = await httpHydra.Get<DirectRoot>(uri.ToString(), safety: true);
                if (root?.data?.file?.hlsSource != null && root.data.file.hlsSource.Count > 0)
                    data = root.data;
            }

            if (data == null)
            {
                data = await ResolveBnsiStream(token_movie, t, s, e, kinopoisk_id, imdb_id);
            }

            if (data?.file?.hlsSource == null || data.file.hlsSource.Count == 0)
                return cacheEntry.Fail("data", refresh_proxy: true);

            return cacheEntry.Success(data);
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

        string streamOrigin = string.IsNullOrEmpty(init.linkhost) ? "https://scalp-as.stloadi.live" : init.linkhost.TrimEnd('/');
        string edgeHash = AllohaGuard.GetLiveToken(streamOrigin) ?? AllohaSessionManager.GetGlobalEdgeHash();

        var streamHeaders = HeadersModel.Init(
            ("Origin", streamOrigin),
            ("Referer", streamOrigin + "/"),
            ("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36")
        );
        if (!string.IsNullOrEmpty(edgeHash))
            streamHeaders.Add(new HeadersModel("Accepts-Controls", edgeHash));

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

                    streams.Add(new StreamQualityDto(HostStreamProxy(file, headers: streamHeaders, force_streamproxy: true), $"{q.Key}p"));
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
            httpContext: HttpContext
        ));
    }
    #endregion

    #region ResolveBnsiStream
    async Task<DirectData> ResolveBnsiStream(string token_movie, string t, int s, short e, long kinopoisk_id, string imdb_id)
    {
        string linkHost = string.IsNullOrEmpty(init.linkhost) ? "https://scalp-as.stloadi.live" : init.linkhost.TrimEnd('/');
        string token = string.IsNullOrEmpty(init.token) ? init.secret_token : init.token;
        if (string.IsNullOrEmpty(token))
            return null;

        var pUri = new System.Text.StringBuilder();
        pUri.Append($"{linkHost}/?token={token}");
        if (!string.IsNullOrEmpty(token_movie))
            pUri.Append($"&token_movie={token_movie}");
        else if (kinopoisk_id > 0)
            pUri.Append($"&kp={kinopoisk_id}");
        else if (!string.IsNullOrEmpty(imdb_id))
            pUri.Append($"&imdb={imdb_id}");

        if (s > 0) pUri.Append($"&season={s}");
        if (e > 0) pUri.Append($"&episode={e}");
        if (!string.IsNullOrEmpty(t)) pUri.Append($"&translation={t}");

        string playerUrl = pUri.ToString();
        var playerHeaders = HeadersModel.Init(
            ("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"),
            ("Referer", $"{linkHost}/")
        );

        string html = await httpHydra.Get(playerUrl, safety: true, addheaders: playerHeaders);
        if (string.IsNullOrEmpty(html))
            return null;

        var vpMatch = System.Text.RegularExpressions.Regex.Match(html, @"name=[""']viewporti[""']\s+content=[""']([^""']+)[""']");
        if (!vpMatch.Success)
            return null;
        string viewporti = vpMatch.Groups[1].Value;

        var flMatch = System.Text.RegularExpressions.Regex.Match(html, @"fileList\s*=\s*JSON\.parse\('([^']+)'\);");
        if (!flMatch.Success)
            flMatch = System.Text.RegularExpressions.Regex.Match(html, @"fileList\s*=\s*JSON\.parse\(""([^""]+)""\);");
        if (!flMatch.Success)
            return null;

        string actId = null;
        try
        {
            string rawJson = flMatch.Groups[1].Value;
            var fl = Newtonsoft.Json.Linq.JObject.Parse(rawJson);

            if (fl["active"]?["id"] != null)
                actId = fl["active"]["id"].ToString();

            if (fl["all"] != null && !string.IsNullOrEmpty(t))
            {
                string trKey = "t" + t;
                if (s > 0 && e > 0)
                {
                    var epToken = fl["all"]?[s.ToString()]?[e.ToString()]?[trKey];
                    if (epToken?["id"] != null)
                        actId = epToken["id"].ToString();
                }
                else
                {
                    var theatrical = fl["all"]?["theatrical"]?[trKey];
                    if (theatrical is Newtonsoft.Json.Linq.JObject thObj)
                    {
                        foreach (var prop in thObj.Properties())
                        {
                            if (prop.Value?["id"] != null)
                            {
                                actId = prop.Value["id"].ToString();
                                break;
                            }
                        }
                    }
                    if (actId == null && fl["all"]?["directors"]?[trKey] is Newtonsoft.Json.Linq.JObject dirObj)
                    {
                        foreach (var prop in dirObj.Properties())
                        {
                            if (prop.Value?["id"] != null)
                            {
                                actId = prop.Value["id"].ToString();
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        if (string.IsNullOrEmpty(actId))
            return null;

        string borth = AllohaBorth.Compute(viewporti);
        string postUrl = $"{linkHost}/bnsi/movies/{actId}";

        var bnsiHeaders = HeadersModel.Init(
            ("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"),
            ("Origin", linkHost),
            ("Referer", playerUrl),
            ("Borth", borth),
            ("Accept", "application/json, text/plain, */*"),
            ("X-Requested-With", "XMLHttpRequest"),
            ("Content-Type", "application/x-www-form-urlencoded")
        );

        string audioParam = string.IsNullOrEmpty(t) ? "66" : t;
        string postData = $"token={token}&av1={(init.m4s ? "true" : "false")}&autoplay=0&audio={audioParam}";

        var bnsiResp = await httpHydra.Post<BnsiResponse>(postUrl, postData, safety: true, addheaders: bnsiHeaders);
        if (bnsiResp?.hlsSource == null || bnsiResp.hlsSource.Count == 0)
        {
            string dynamicBorth = await AllohaBorth.ComputeFromHtmlAsync(html, viewporti, linkHost);
            if (!string.IsNullOrEmpty(dynamicBorth) && dynamicBorth != borth)
            {
                bnsiHeaders.RemoveAll(x => x.name == "Borth");
                bnsiHeaders.Add(new HeadersModel("Borth", dynamicBorth));
                bnsiResp = await httpHydra.Post<BnsiResponse>(postUrl, postData, safety: true, addheaders: bnsiHeaders);
            }
        }

        if (bnsiResp?.hlsSource == null || bnsiResp.hlsSource.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(bnsiResp.pnr) && !string.IsNullOrEmpty(bnsiResp.pnk))
        {
            AllohaSessionManager.GetOrCreate(bnsiResp.pnr, bnsiResp.pnk, linkHost);
        }
        _ = Task.Run(() => AllohaGuard.ForHost(linkHost).GetTokenAsync());

        return new DirectData
        {
            file = new FileData
            {
                hlsSource = bnsiResp.hlsSource,
                tracks = bnsiResp.tracks
            }
        };
    }
    #endregion

    #region RouteToSpiderSearch
    [HttpGet, Staticache(manually: true)]
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
