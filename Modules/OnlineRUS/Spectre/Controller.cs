using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Spectre;

public class SpectreController : BaseOnlineController<ModuleConf>
{
    static readonly ConcurrentDictionary<string, CachedVideoResult> videoCache = new();
    static readonly TimeSpan videoCacheTtl = TimeSpan.FromMinutes(2);
    static readonly TimeSpan videoCacheStaleTtl = TimeSpan.FromMinutes(10);

    public SpectreController() : base(ModInit.conf)
    {
        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("m4s"))
                i.m4s = c.m4s;
            return i;
        };
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/spectre")]
    async public Task<ActionResult> Index(string orid, string imdb_id, long kinopoisk_id, string title, string original_title, byte serial, string original_language, short year, int t = -1, short s = -1, bool origsource = false, bool rjson = false, bool similar = false)
    {
        if (similar)
            return await RouteSpiderSearch(title, origsource, rjson);

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        var result = await search(orid, imdb_id, kinopoisk_id, title, serial, original_language, year);
        if (result.category_id == 0 || result.data == null)
            return OnError();

        JToken data = result.data;
        string tokenMovie = data["token_movie"] != null ? data.Value<string>("token_movie") : null;
        var frame = await iframe(tokenMovie);
        if (frame.all == null)
            return OnError();

        if (result.category_id is 1 or 3)
        {
            #region Фильм
            var videosToken = frame.all["theatrical"];

            if (videosToken == null || videosToken.Type != JTokenType.Object || !videosToken.HasValues)
                videosToken = frame.all["directors"];

            var videos = videosToken?.Type == JTokenType.Object
                ? videosToken.ToObject<Dictionary<string, Dictionary<string, JObject>>>()
                : new Dictionary<string, Dictionary<string, JObject>>();

            var mtpl = new MovieTpl(title, original_title, videos.Count);

            foreach (var i in videos)
            {
                var file = i.Value.First().Value;

                string translation = file.Value<string>("translation");
                string quality = file.Value<string>("quality");
                long id = file.Value<long>("id");

                var uhdToken = file["uhd"];

                bool uhd = init.m4s && (
                    uhdToken?.Type == JTokenType.Boolean && uhdToken.Value<bool>() ||
                    uhdToken?.Type == JTokenType.Integer && uhdToken.Value<int>() == 1
                );

                string link = $"{host}/lite/spectre/video?id_file={id}&token_movie={data.Value<string>("token_movie")}";
                mtpl.Append(
                    translation,
                    link,
                    "call",
                    accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true"),
                    voice_name: uhd ? "2160p" : quality,
                    quality: uhd ? "2160p" : ""
                );
            }

            return ContentTpl(mtpl);
            #endregion
        }
        else
        {
            #region Сериал
            string defaultargs = $"&orid={orid}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&original_language={original_language}";

            if (s == -1)
            {
                #region Сезоны
                string q = null;

                try
                {
                    if (init.m4s)
                        q = frame.active.Value<bool>("uhd") == true ? "2160p" : null;
                }
                catch { }

                Dictionary<string, JToken> seasons;
                if (frame.all["seasons"] != null)
                    seasons = frame.all["seasons"].ToObject<Dictionary<string, JToken>>();
                else
                    seasons = frame.all.ToObject<Dictionary<string, JToken>>();

                if (seasons.First().Key.StartsWith("t"))
                {
                    var tpl = new SeasonTpl(q);

                    var seasonNumbers = new HashSet<int>();

                    foreach (var translation in seasons)
                    {
                        var file = translation.Value["file"];
                        if (file == null)
                            continue;

                        foreach (var season in file.ToObject<Dictionary<string, object>>())
                        {
                            if (int.TryParse(season.Key, out int seasonNumber))
                                seasonNumbers.Add(seasonNumber);
                        }
                    }

                    if (!seasonNumbers.Any())
                        seasonNumbers.Add(frame.active.Value<int>("seasons"));

                    foreach (int i in seasonNumbers.OrderBy(i => i))
                    {
                        tpl.Append(
                            $"{i} сезон",
                            $"{host}/lite/spectre?rjson={rjson}&s={i}{defaultargs}",
                            i.ToString()
                        );
                    }

                    return ContentTpl(tpl);
                }
                else
                {
                    var tpl = new SeasonTpl(q, seasons.Count);

                    foreach (var season in seasons)
                    {
                        tpl.Append(
                            $"{season.Key} сезон",
                            $"{host}/lite/spectre?rjson={rjson}&s={season.Key}{defaultargs}",
                            season.Key
                        );
                    }

                    return ContentTpl(tpl);
                }
                #endregion
            }
            else
            {
                var vtpl = new VoiceTpl();
                var etpl = new EpisodeTpl();
                var voices = new HashSet<int>();

                string sArhc = s.ToString();

                if (frame.all[sArhc] is JArray)
                {
                    #region Перевод
                    foreach (var episode in frame.all[sArhc])
                    {
                        foreach (var voice in episode.ToObject<Dictionary<string, JObject>>().Select(i => i.Value))
                        {
                            int id_translation = voice.Value<int>("id_translation");
                            if (voices.Contains(id_translation))
                                continue;

                            voices.Add(id_translation);

                            if (t == -1)
                                t = id_translation;

                            vtpl.Append(
                                voice.Value<string>("translation"),
                                t == id_translation,
                                $"{host}/lite/spectre?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
                            );
                        }
                    }
                    #endregion

                    foreach (var episode in frame.all[sArhc])
                    {
                        foreach (var voice in episode
                            .ToObject<Dictionary<string, JObject>>()
                            .Select(i => i.Value)
                            .OrderBy(e => e.Value<short>("episode")))
                        {
                            if (voice.Value<int>("id_translation") != t)
                                continue;

                            string translation = voice.Value<string>("translation");
                            short e = voice.Value<short>("episode");

                            string link = $"{host}/lite/spectre/video?id_file={voice.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                            string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                            if (e > 0)
                            {
                                etpl.Append(
                                    $"{e} серия",
                                    title ?? original_title,
                                    s,
                                    e,
                                    link,
                                    "call",
                                    voice_name: translation,
                                    streamlink: streamlink
                                );
                            }
                        }
                    }
                }
                else if (frame.all.ToObject<Dictionary<string, object>>().First().Key.StartsWith("t"))
                {
                    #region Перевод
                    foreach (var node in frame.all)
                    {
                        if (!node.First["file"].ToObject<Dictionary<string, object>>().ContainsKey(sArhc))
                            continue;

                        var voice = node.First["file"].First.First.First.First;
                        int id_translation = voice.Value<int>("id_translation");
                        if (voices.Contains(id_translation))
                            continue;

                        voices.Add(id_translation);

                        if (t == -1)
                            t = id_translation;

                        vtpl.Append(
                            voice.Value<string>("translation"),
                            t == id_translation,
                            $"{host}/lite/spectre?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
                        );
                    }
                    #endregion

                    foreach (var node in frame.all)
                    {
                        foreach (var season in node.First["file"].ToObject<Dictionary<string, object>>())
                        {
                            if (season.Key != sArhc)
                                continue;

                            if (season.Value is JArray sjar)
                            {

                            }
                            else if (season.Value is JObject sjob)
                            {
                                foreach (var episode in sjob
                                    .ToObject<Dictionary<string, JObject>>()
                                    .OrderBy(e => e.Value.Value<short>("episode")))
                                {
                                    if (episode.Value.Value<int>("id_translation") != t)
                                        continue;

                                    string translation = episode.Value.Value<string>("translation");
                                    short e = episode.Value.Value<short>("episode");

                                    string link = $"{host}/lite/spectre/video?id_file={episode.Value.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                                    string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                                    if (e > 0)
                                    {
                                        etpl.Append(
                                            $"{e} серия",
                                            title ?? original_title,
                                            s,
                                            e,
                                            link,
                                            "call",
                                            voice_name: translation,
                                            streamlink: streamlink
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    #region Перевод
                    foreach (var episode in frame.all[sArhc].ToObject<Dictionary<string, Dictionary<string, JObject>>>())
                    {
                        foreach (var voice in episode.Value.Select(i => i.Value))
                        {
                            int id_translation = voice.Value<int>("id_translation");
                            if (voices.Contains(id_translation))
                                continue;

                            voices.Add(id_translation);

                            if (t == -1)
                                t = id_translation;

                            vtpl.Append(
                                voice.Value<string>("translation"),
                                t == id_translation,
                                $"{host}/lite/spectre?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
                            );
                        }
                    }
                    #endregion

                    foreach (var episode in frame.all[sArhc]
                        .ToObject<Dictionary<string, Dictionary<string, JObject>>>()
                        .OrderBy(e => int.TryParse(e.Key, out int _e) ? _e : 0))
                    {
                        foreach (var voice in episode.Value.Select(i => i.Value))
                        {
                            string translation = voice.Value<string>("translation");
                            if (voice.Value<int>("id_translation") != t)
                                continue;

                            string link = $"{host}/lite/spectre/video?id_file={voice.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                            string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                            etpl.Append(
                                $"{episode.Key} серия",
                                title ?? original_title,
                                s,
                                episode.Key,
                                link,
                                "call",
                                voice_name: translation,
                                streamlink: streamlink
                            );
                        }
                    }
                }

                etpl.Append(vtpl);

                return ContentTpl(etpl);
            }
            #endregion
        }
    }


    #region Video
    [HttpGet, Staticache(manually: true)]
    [Route("lite/spectre/video")]
    [Route("lite/spectre/video.m3u8")]
    async public Task<ActionResult> Video(long id_file, string token_movie, bool play)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string streamId = !ModInit.conf.mux ? "muxoff"
            : requestInfo.IsLocalIp || requestInfo.Country == null
                ? token_movie
                : requestInfo.IP;

        string videoCacheKey = cacheKey(token_movie, id_file, streamId);
        var result = await resolveVideo($"{init.linkhost}/?token_movie={token_movie}&token={init.token}", token_movie, id_file, streamId, play);
        if (result.streams?.data == null || result.streams.data.Count == 0)
            return OnError();

        if (!string.IsNullOrEmpty(result.wsUri) || !string.IsNullOrEmpty(result.watch?.edge_hash))
        {
            bool res = false;
            int addOrUpdateAttempts = play ? 5 : 2;

            for (int attempt = 0; attempt < addOrUpdateAttempts; attempt++)
            {
                res = !string.IsNullOrEmpty(result.wsUri)
                    ? await Service.AddOrUpdate(streamId, result.wsUri, result.watch)
                    : Service.AddOrUpdateHeaders(streamId, result.watch);
                if (res)
                    break;

                RemoveCachedVideo(videoCacheKey);

                await Task.Delay(play ? 400 : 200);

                result = await resolveVideo($"{init.linkhost}/?token_movie={token_movie}&token={init.token}", token_movie, id_file, streamId, play);
                if (result.streams?.data == null || result.streams.data.Count == 0)
                    continue;
            }

            if (!res)
                return OnError();
        }

        var first = result.streams.Firts();

        if (play)
            return Redirect(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "auto",
            streamquality: result.streams,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region resolveVideo
    async Task<(WatchMux watch, StreamQualityTpl streams, string wsUri)> resolveVideo(string uri, string token_movie, long id_file, string streamId, bool aggressiveRetry)
    {
        string cacheKey = this.cacheKey(token_movie, id_file, streamId);

        if (TryGetCachedVideo(cacheKey, out var cached))
            return cached;

        int maxAttempts = aggressiveRetry ? 1 : 2;
        int retryDelayMs = aggressiveRetry ? 400 : 250;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var result = await goMovie(uri, id_file, streamId);
            if (result.streams?.data?.Count > 0)
            {
                SetCachedVideo(cacheKey, result);
                return result;
            }

            await Task.Delay(retryDelayMs);
        }

        if (TryGetCachedVideo(cacheKey, out cached, allowStale: true))
            return cached;

        return default;
    }

    string cacheKey(string token_movie, long id_file, string streamId)
        => $"{token_movie}:{id_file}:{streamId}";

    void RemoveCachedVideo(string cacheKey)
        => videoCache.TryRemove(cacheKey, out _);

    bool TryGetCachedVideo(string cacheKey, out (WatchMux watch, StreamQualityTpl streams, string wsUri) result, bool allowStale = false)
    {
        result = default;

        if (!videoCache.TryGetValue(cacheKey, out var cached))
            return false;

        DateTime now = DateTime.UtcNow;
        if (cached.expires < now)
        {
            if (cached.expires.Add(videoCacheStaleTtl) < now)
            {
                videoCache.TryRemove(cacheKey, out _);
                return false;
            }

            if (!allowStale)
                return false;
        }

        var watch = new WatchMux()
        {
            streamId = cached.streamId,
            edge_hash = cached.edge_hash,
            requestOrigin = cached.requestOrigin,
            requestReferer = cached.requestReferer,
            resolution = cached.resolution
        };

        var streams = new StreamQualityTpl(cached.qualities.Count);
        foreach (var item in cached.qualities)
            streams.Append(item.link, item.quality);

        result = (watch, streams, cached.wsUri);
        return true;
    }

    void SetCachedVideo(string cacheKey, (WatchMux watch, StreamQualityTpl streams, string wsUri) result)
    {
        if (result.watch == null || result.streams?.data == null || result.streams.data.Count == 0)
            return;

        videoCache[cacheKey] = new CachedVideoResult()
        {
            expires = DateTime.UtcNow.Add(videoCacheTtl),
            streamId = result.watch.streamId,
            edge_hash = result.watch.edge_hash,
            requestOrigin = result.watch.requestOrigin,
            requestReferer = result.watch.requestReferer,
            resolution = result.watch.resolution,
            wsUri = result.wsUri,
            qualities = result.streams.data
                .Select(i => new CachedVideoQuality(i.link, i.quality))
                .ToList()
        };
    }
    #endregion

    #region SpiderSearch
    [HttpGet, Staticache(manually: true)]
    [Route("lite/spectre-search")]
    async public Task<ActionResult> RouteSpiderSearch(string title, bool origsource = false, bool rjson = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError();

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        var cache = await InvokeCacheResult<JArray>($"mirage:search:{title}", 40, async e =>
        {
            var root = await httpHydra.Get<JObject>($"{init.apihost}/?token={init.token}&name={HttpUtility.UrlEncode(title)}&list", safety: true);
            if (root == null || !root.ContainsKey("data"))
                return e.Fail("data");

            return e.Success(root["data"].ToObject<JArray>());
        });

        return ContentTpl(cache, () =>
        {
            var stpl = new SimilarTpl(cache.Value.Count);

            foreach (var j in cache.Value)
            {
                stpl.Append(
                    j.Value<string>("name") ?? j.Value<string>("original_name"),
                    j.Value<int>("year").ToString(),
                    string.Empty,
                    $"{host}/lite/spectre?orid={j.Value<string>("token_movie")}",
                    PosterApi.Size(j.Value<string>("poster"))
                );
            }

            return stpl;
        });
    }
    #endregion


    #region search
    async ValueTask<(bool refresh_proxy, int category_id, JToken data)> search(string token_movie, string imdb_id, long kinopoisk_id, string title, byte serial, string original_language, short year)
    {
        string memKey = $"mirage:view:{kinopoisk_id}:{imdb_id}";
        if (0 >= kinopoisk_id && string.IsNullOrEmpty(imdb_id))
            memKey = $"mirage:viewsearch:{title}:{serial}:{original_language}:{year}";

        if (!string.IsNullOrEmpty(token_movie))
            memKey = $"mirage:view:{token_movie}";

        JObject root;

        bool cacheHit = hybridCache.TryGetValue(memKey, out (int category_id, JToken data) res);
        bool refreshStaleEmptyCache = cacheHit && res.category_id == 0 && res.data == null && kinopoisk_id > 0 && !string.IsNullOrWhiteSpace(imdb_id);

        if (!cacheHit || refreshStaleEmptyCache)
        {
            string stitle = title.ToLowerAndTrim();

            if (memKey.Contains(":viewsearch:"))
            {
                if (string.IsNullOrWhiteSpace(title) || year == 0)
                    return default;

                root = await httpHydra.Get<JObject>($"{init.apihost}/?token={init.token}&name={HttpUtility.UrlEncode(title)}&list={(serial == 1 ? "serial" : "movie")}", safety: true);
                if (root == null)
                    return (true, 0, null);

                if (root.ContainsKey("data"))
                {
                    foreach (var item in root["data"])
                    {
                        if (item.Value<string>("name")?.ToLowerAndTrim() == stitle)
                        {
                            short y = item.Value<short>("year");
                            if (y > 0 && (y == year || y == (year - 1) || y == (year + 1)))
                            {
                                if (original_language == "ru" && item.Value<string>("country")?.ToLowerAndTrim() != "россия")
                                    continue;

                                res.data = item;
                                res.category_id = item.Value<int>("category_id");
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                root = await loadMovieRoot(kinopoisk_id, imdb_id, token_movie, "kp");
                if (IsNotMovie(root) && kinopoisk_id > 0)
                {
                    root = !string.IsNullOrWhiteSpace(imdb_id)
                        ? await loadMovieRoot(0, imdb_id, token_movie, null)
                        : await loadMovieRoot(kinopoisk_id, null, token_movie, "tmdb");
                }

                if (root == null)
                    return (true, 0, null);

                if (root.ContainsKey("data"))
                {
                    res.data = root.GetValue("data");
                    res.category_id = res.data.Value<int>("category");
                }
            }

            if (res.data != null || (root.ContainsKey("error_info") && root.Value<string>("error_info") == "not movie"))
                hybridCache.Set(memKey, res, cacheTime(res.category_id is 1 or 3 ? 120 : 40));
            else
                hybridCache.Set(memKey, res, cacheTime(2));
        }

        return (false, res.category_id, res.data);
    }

    Task<JObject> loadMovieRoot(long external_id, string imdb_id, string token_movie, string idParam)
    {
        string uri = $"{init.apihost}/?token={init.token}&token_movie={token_movie}";

        if (external_id > 0 && !string.IsNullOrWhiteSpace(idParam))
            uri += $"&{idParam}={external_id}";

        if (!string.IsNullOrWhiteSpace(imdb_id))
            uri += $"&imdb={HttpUtility.UrlEncode(imdb_id)}";

        return httpHydra.Get<JObject>(uri, safety: true);
    }

    bool IsNotMovie(JObject root)
        => root != null && root.Value<string>("status") == "error" && root.Value<string>("error_info") == "not movie";
    #endregion

    #region iframe
    async Task<(JToken all, JToken active)> iframe(string token_movie)
    {
        if (string.IsNullOrEmpty(token_movie))
            return default;

        string memKey = $"mirage:iframe:{token_movie}";
        if (!hybridCache.TryGetValue(memKey, out (JToken all, JToken active) cache))
        {
            string json = null;

            string uri = $"{init.linkhost}/?token_movie={token_movie}&token={init.token}";

            await httpHydra.GetSpan(uri, safety: true, addheaders: HeadersModel.Init(
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                ("referer", "https://kinogo-go.tv/"),
                ("sec-fetch-dest", "iframe"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "cross-site"),
                ("upgrade-insecure-requests", "1")
            ),
            spanAction: html =>
            {
                json = Rx.Match(html, "fileList = JSON.parse\\('([^\n\r]+)'\\);");
            });

            if (string.IsNullOrEmpty(json))
                return default;

            try
            {
                var root = JsonConvert.DeserializeObject<JObject>(json);
                if (root == null || !root.ContainsKey("all"))
                    return default;

                cache = (root["all"], root["active"]);

                hybridCache.Set(memKey, cache, cacheTime(40));
            }
            catch { return default; }
        }

        return cache;
    }
    #endregion

    #region goMovie
    async Task<(WatchMux watch, StreamQualityTpl streams, string wsUri)> goMovie(string uri, long id_file, string streamId)
    {
        try
        {
            string wsUri = null;
            var watch = new WatchMux() { streamId = streamId };

            var streamquality = new StreamQualityTpl();

            using (var browser = new PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data).ConfigureAwait(false);
                if (page == null)
                    return default;

                if (ModInit.conf.debug)
                {
                    page.Console += (_, msg) => Console.WriteLine($"Spectre console {msg.Type}: {msg.Text}");
                    page.PageError += (_, err) => Console.WriteLine("Spectre page error: " + err);
                    page.RequestFailed += (_, req) => Console.WriteLine($"Spectre request failed: {req.Method} {req.Url} - {req.Failure}");
                }

                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        if (ModInit.conf.debug && (route.Request.Url.Contains("kinogo-go.tv") || route.Request.Url.Contains(init.linkhost) || route.Request.Url.Contains("/build/")))
                            Console.WriteLine($"Spectre route: {route.Request.Method} {route.Request.Url}");

                        if (route.Request.Url.Contains("kinogo-go.tv"))
                        {
                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                Body = PlaywrightBase.IframeHtml(WithAutoplay(uri))
                            });
                        }
                        else if (route.Request.Method == "POST" && route.Request.Url.Contains("/movies/"))
                        {
                            if (ModInit.conf.debug)
                                Console.WriteLine("Spectre POST: " + route.Request.Url);

                            string newUrl = Regex.Replace(route.Request.Url, "/[0-9]+$", $"/{id_file}");

                            var fetchHeaders = route.Request.Headers;
                            fetchHeaders.TryAdd("Accept-Encoding", "gzip, deflate, br, zstd");
                            fetchHeaders.TryAdd("Cache-Control", "no-cache");
                            fetchHeaders.TryAdd("DNT", "1");
                            fetchHeaders.TryAdd("Pragma", "no-cache");
                            fetchHeaders.TryAdd("Priority", "u=1, i");
                            fetchHeaders.TryAdd("Sec-Fetch-Dest", "empty");
                            fetchHeaders.TryAdd("Sec-Fetch-Mode", "cors");
                            fetchHeaders.TryAdd("Sec-Fetch-Site", "same-origin");
                            fetchHeaders.TryAdd("Sec-Fetch-Storage-access", "active");

                            var fetchResponse = await route.FetchAsync(new RouteFetchOptions
                            {
                                Url = newUrl,
                                Method = "POST",
                                Headers = fetchHeaders,
                                PostData = route.Request.PostDataBuffer
                            }).ConfigureAwait(false);

                            string json = await fetchResponse.TextAsync().ConfigureAwait(false);
                            if (ModInit.conf.debug)
                                Console.WriteLine($"Spectre POST status: {fetchResponse.Status}, body: {json.Substring(0, Math.Min(json.Length, 500))}");

                            var jo = JsonConvert.DeserializeObject<JObject>(json);
                            if (jo == null || jo["hlsSource"] == null)
                                throw new Exception("invalid player json");

                            route.Request.Headers.TryGetValue("referer", out string requestReferer);
                            route.Request.Headers.TryGetValue("origin", out string requestOrigin);

                            watch.requestReferer = requestReferer;
                            watch.requestOrigin = requestOrigin;

                            string pnr = jo.Value<string>("pnr");
                            string pnk = jo.Value<string>("pnk");
                            bool directHlsFallback = string.IsNullOrEmpty(pnr) || string.IsNullOrEmpty(pnk);

                            if (!directHlsFallback)
                                wsUri = pnr + $"?sid={pnk}&v=2.1&t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

                            var selectedItem =
                                jo["hlsSource"]?
                                    .Children<JObject>()
                                    .FirstOrDefault(x => (bool?)x["default"] == true)
                                ??
                                jo["hlsSource"]?
                                    .FirstOrDefault() as JObject;

                            if (selectedItem?["quality"] == null)
                                throw new Exception("invalid hls source");

                            foreach (var q in selectedItem["quality"].Children<JProperty>())
                            {
                                if (!init.m4s && (q.Name == "2160" || q.Name == "1440"))
                                    continue;

                                string link = (string)q.Value;
                                if (string.IsNullOrWhiteSpace(link))
                                    continue;

                                if (string.IsNullOrEmpty(watch.resolution))
                                    watch.resolution = q.Name;

                                link = link
                                    .Split(new[] { " or " }, StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault()
                                    .Trim();

                                var streamData = new StreamData()
                                {
                                    id = streamId,
                                    resolution = q.Name
                                };

                                streamquality.Append(HostStreamProxy(link, userdata: streamData), $"{q.Name}p");
                            }

                            if (!directHlsFallback)
                                browser.SetPageResult(null);

                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                Status = fetchResponse.Status,
                                Body = json,
                                Headers = fetchResponse.Headers
                            }).ConfigureAwait(false);
                        }
                        else if (route.Request.Url.EndsWith("/master.m3u8"))
                        {
                            if (ModInit.conf.debug)
                                Console.WriteLine("Spectre master: " + route.Request.Url);

                            route.Request.Headers.TryGetValue("referer", out string requestReferer);
                            route.Request.Headers.TryGetValue("origin", out string requestOrigin);
                            route.Request.Headers.TryGetValue("accepts-controls", out string edgeHash);

                            watch.requestReferer = requestReferer;
                            watch.requestOrigin = requestOrigin;
                            watch.edge_hash = edgeHash;

                            if (!string.IsNullOrEmpty(watch.edge_hash))
                                browser.SetPageResult(route.Request.Url);

                            await route.ContinueAsync();
                        }
                        else
                        {
                            if (browser.IsCompleted ||
                                route.Request.Url.Contains("/stat") ||
                                route.Request.Url.Contains("/lists.php") ||
                                route.Request.Url.EndsWith(".cekh8i") ||
                                route.Request.Url.EndsWith(".css") ||
                                route.Request.Url.EndsWith(".svg") ||
                                route.Request.Url.EndsWith("blank.mp4"))
                            {
                                await route.AbortAsync();
                                return;
                            }

                            if (route.Request.Url.StartsWith($"{init.linkhost}/build/") ||
                                route.Request.Url.StartsWith($"{init.linkhost}/js/"))
                            {
                                await route.ContinueAsync();
                                return;
                            }

                            if (await PlaywrightBase.AbortOrCache(page, route))
                                return;

                            await route.ContinueAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ModInit.conf.debug)
                            Console.WriteLine($"Spectre route error: {route.Request.Method} {route.Request.Url} - {ex.Message}");
                    }
                });

                PlaywrightBase.GotoAsync(page, "https://kinogo-go.tv/");

                string pageResult = await browser.WaitPageResult(25);
                if (ModInit.conf.debug)
                    Console.WriteLine($"Spectre page result: {(pageResult ?? "null")}, streams={streamquality.data.Count}, ws={(string.IsNullOrEmpty(wsUri) ? "null" : "yes")}, edge={(string.IsNullOrEmpty(watch.edge_hash) ? "null" : "yes")}");
            }

            return (watch, streamquality, wsUri);
        }
        catch
        {
            return default;
        }
    }

    string WithAutoplay(string uri)
    {
        if (string.IsNullOrEmpty(uri) || uri.IndexOf("autoplay", StringComparison.OrdinalIgnoreCase) >= 0)
            return uri;

        return uri + (uri.Contains("?") ? "&" : "?") + "autoplay=1";
    }
    #endregion

    #region CachedVideo
    sealed class CachedVideoResult
    {
        public DateTime expires { get; set; }
        public string streamId { get; set; }
        public string edge_hash { get; set; }
        public string requestOrigin { get; set; }
        public string requestReferer { get; set; }
        public string resolution { get; set; }
        public string wsUri { get; set; }
        public List<CachedVideoQuality> qualities { get; set; } = new();
    }

    sealed class CachedVideoQuality
    {
        public CachedVideoQuality(string link, string quality)
        {
            this.link = link;
            this.quality = quality;
        }

        public string link { get; }
        public string quality { get; }
    }
    #endregion
}
