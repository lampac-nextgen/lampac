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
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Aladin;

public class AladinController : BaseOnlineController<ModuleConf>
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    List<HeadersModel> bearer;

    public AladinController() : base(ModInit.conf)
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

            i.token = c.token;

            return i;
        };
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/aladin")]
    async public Task<ActionResult> Index(string orid, string imdb_id, long kinopoisk_id, string title, string original_title, byte serial, string original_language, short year, int t = -1, short s = -1, bool rjson = false, bool similar = false)
    {
        if (string.IsNullOrEmpty(orid))
        {
            if (similar || (0 >= kinopoisk_id && string.IsNullOrEmpty(imdb_id)))
                return await RouteToSpiderSearch(title);
        }

        if (await IsRequestBlocked())
            return badInitMsg;

        #region search
    rhubFallback:

        string memKey = string.IsNullOrEmpty(orid)
            ? $"aladin:search:{imdb_id}:{kinopoisk_id}"
            : $"aladin:search:{orid}";

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
                string link = $"{host}/lite/aladin/video?t={trId}&token_movie={data.token}" + defaultargs;
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
                        $"{host}/lite/aladin?rjson={rjson}&s={season.season}{defaultargs}",
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
                                    $"{host}/lite/aladin?rjson={rjson}&s={s}&t={translation.id}{defaultargs}"
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
                    string link = $"{host}/lite/aladin/video?t={activTranslate}&s={s}&e={episodeNum}&token_movie={data.token}" + defaultargs;

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
    [Route("lite/aladin/video")]
    [Route("lite/aladin/video.m3u8")]
    async public Task<ActionResult> Video(string token_movie, string title, string original_title, string t, int s, short e, bool play, bool directors_cut, long kinopoisk_id = 0, string imdb_id = null)
    {
        if (await IsRequestBlocked())
            return badInitMsg;

        int translation = int.TryParse(t, out int parsed) ? parsed : 0;

        NativeStream stream;
        try
        {
            stream = await AladinNative.ResolveAsync(init, kinopoisk_id, imdb_id, token_movie, translation, s, e);
        }
        catch
        {
            stream = null;
        }

        if (stream == null)
            return OnError("data");

        var streamquality = new StreamQualityTpl();

        List<KeyValuePair<string, Dictionary<string, List<string>>>> voices;
        lock (stream.Sync)
            voices = stream.Voices.ToList();

        foreach (var voice in voices)
        {
            foreach (var quality in voice.Value)
            {
                if (!init.m4s && (quality.Key == "1440" || quality.Key == "2160"))
                    continue;

                string link = $"{host}/lite/aladin/hls/{stream.Id}/v/{AladinNative.VoiceKey(voice.Key)}/{Uri.EscapeDataString(quality.Key)}.m3u8";
                streamquality.Append(link, quality.Key + "p");
            }
        }

        if (streamquality.IsEmpty)
            return OnError("streams");

        var first = streamquality.Firts();
        if (first == null || string.IsNullOrEmpty(first.link))
            return OnError("streams");

        if (play)
            return RedirectToPlay(first.link);

        var subtitles = new SubtitleTpl();
        foreach (var track in stream.Tracks)
            subtitles.Append(track.label, track.src);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            title ?? original_title,
            streamquality: streamquality,
            subtitles: subtitles,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }

    [HttpGet]
    [Route("lite/aladin/hls/{id}/v/{voice}/{quality}")]
    async public Task<ActionResult> HlsPlaylist(string id, string voice, string quality)
    {
        if (quality.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            quality = quality[..^5];

        var stream = AladinNative.Get(id, init.liveStreamTtl);
        if (stream == null)
            return NotFound();

        string voiceName;
        List<string> mirrors;
        lock (stream.Sync)
        {
            voiceName = stream.Voices.Keys.FirstOrDefault(x => AladinNative.VoiceKey(x) == voice);
            if (voiceName == null ||
                !stream.Voices.TryGetValue(voiceName, out var qualities) ||
                !qualities.TryGetValue(quality, out mirrors) ||
                mirrors.Count == 0)
                return NotFound();
        }

        string url = AladinNative.PickMirror(stream, voiceName, quality);
        if (string.IsNullOrEmpty(url))
            return NotFound();

        stream.Ws?.UpdateProgress(url);

        var response = await Task.Run(() => stream.Browser.Send("GET", url, null, AladinRequestKind.Media, stream.PlayerUrl, stream.LinkHost, 30000, AladinNative.CdnControls(stream, init)));

        if (response.Status is 403 or 404)
        {
            await Task.Delay(500);
            response = await Task.Run(() => stream.Browser.Send("GET", url, null, AladinRequestKind.Media, stream.PlayerUrl, stream.LinkHost, 30000, AladinNative.CdnControls(stream, init)));
        }

        if (response.Status == 403)
        {
            if (response.Header("x-vd") == "client_blocked")
                AladinNative.MarkCdnDead(new Uri(url).Host);

            await Task.Run(() => AladinNative.Refresh(stream, init));
            stream.Ws?.WaitForEdge(1500);
            url = AladinNative.Rebuild(stream, url);
            response = await Task.Run(() => stream.Browser.Send("GET", url, null, AladinRequestKind.Media, stream.PlayerUrl, stream.LinkHost, 30000, AladinNative.CdnControls(stream, init)));
        }

        if (response.Status != 200)
            return StatusCode(response.Status == 0 ? 502 : response.Status);

        if (string.IsNullOrEmpty(response.Body))
            return StatusCode(502);

        string baseUrl = response.Target ?? url;
        string body = AladinHls.Rewrite(response.Body, uri =>
        {
            string absolute = AladinHls.Resolve(baseUrl, uri);
            return $"{host}/lite/aladin/hls/{id}/item/{stream.AddItem(absolute)}";
        }, init.m4s ? 2160 : 1080);

        Response.Headers["Cache-Control"] = "private, no-store";
        return Content(body, "application/vnd.apple.mpegurl");
    }

    [HttpGet]
    [Route("lite/aladin/hls/{id}/item/{key}")]
    async public Task<ActionResult> HlsItem(string id, string key)
    {
        var stream = AladinNative.Get(id, init.liveStreamTtl);
        if (stream == null)
            return NotFound();

        string url = stream.GetItem(key);
        if (string.IsNullOrEmpty(url))
            return NotFound();

        url = AladinNative.Rebuild(stream, url);
        stream.Ws?.UpdateProgress(url);

        byte[] bytes = null;
        var response = await Task.Run(() => stream.Browser.FetchBytes(url, AladinRequestKind.Media, stream.PlayerUrl, stream.LinkHost, AladinNative.CdnControls(stream, init), out bytes));

        if (response.Status == 403)
        {
            if (response.Header("x-vd") == "client_blocked")
                AladinNative.MarkCdnDead(new Uri(url).Host);

            await Task.Run(() => AladinNative.Refresh(stream, init));
            stream.Ws?.WaitForEdge(1500);
            url = AladinNative.Rebuild(stream, url);
            response = await Task.Run(() => stream.Browser.FetchBytes(url, AladinRequestKind.Media, stream.PlayerUrl, stream.LinkHost, AladinNative.CdnControls(stream, init), out bytes));
        }

        if (bytes == null || response.Status < 200 || response.Status >= 300)
            return StatusCode(response.Status == 0 ? 502 : response.Status);

        if (AladinHls.IsPlaylist(bytes, response))
        {
            string baseUrl = response.Target ?? url;
            string body = AladinHls.Rewrite(Encoding.UTF8.GetString(bytes), uri =>
            {
                string absolute = AladinHls.Resolve(baseUrl, uri);
                return $"{host}/lite/aladin/hls/{id}/item/{stream.AddItem(absolute)}";
            }, init.m4s ? 2160 : 1080);

            Response.Headers["Cache-Control"] = "private, no-store";
            return Content(body, "application/vnd.apple.mpegurl");
        }

        return File(bytes, AladinHls.ContentType(response, false));
    }
    #endregion

    #region RouteToSpiderSearch
    [HttpGet, Staticache(manually: true)]
    [Route("lite/aladin-search")]
    async public Task<ActionResult> RouteToSpiderSearch(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError("title", gbcache: false);

        if (await IsRequestBlocked())
            return badInitMsg;

        var cache = await InvokeCacheResult<List<MediaItem>>($"aladin:search:{title}", TimeSpan.FromHours(4), async e =>
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
                    $"{host}/lite/aladin?orid={j.token}",
                    PosterApi.Size(j.poster)
                );
            }

            return stpl;
        });
    }
    #endregion
}
