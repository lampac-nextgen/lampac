using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Phantom;

public class PhantomController : BaseOnlineController<ModuleConf>
{
    public PhantomController() : base(ModInit.conf)
    {
        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("m4s"))
                i.m4s = c.m4s;
            return i;
        };
    }

    [HttpGet]
    [Route("lite/phantom")]
    async public Task<ActionResult> Index(string orid, string imdb_id, long kinopoisk_id, string title, string original_title, int serial, string original_language, int year, int t = -1, int s = -1, bool origsource = false, bool rjson = false, bool similar = false)
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
            var videos = frame.all["theatrical"].ToObject<Dictionary<string, Dictionary<string, JObject>>>();

            var mtpl = new MovieTpl(title, original_title, videos.Count);

            foreach (var i in videos)
            {
                var file = i.Value.First().Value;

                string translation = file.Value<string>("translation");
                string quality = file.Value<string>("quality");
                long id = file.Value<long>("id");
                bool uhd = init.m4s ? file.Value<bool>("uhd") : false;

                string link = $"{host}/lite/phantom/video?id_file={id}&token_movie={data.Value<string>("token_movie")}";
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
                            $"{host}/lite/phantom?rjson={rjson}&s={i}{defaultargs}",
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
                            $"{host}/lite/phantom?rjson={rjson}&s={season.Key}{defaultargs}",
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
                                $"{host}/lite/phantom?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
                            );
                        }
                    }
                    #endregion

                    foreach (var episode in frame.all[sArhc])
                    {
                        foreach (var voice in episode
                            .ToObject<Dictionary<string, JObject>>()
                            .Select(i => i.Value)
                            .OrderBy(e => e.Value<int>("episode")))
                        {
                            if (voice.Value<int>("id_translation") != t)
                                continue;

                            string translation = voice.Value<string>("translation");
                            int e = voice.Value<int>("episode");

                            string link = $"{host}/lite/phantom/video?id_file={voice.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                            string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                            if (e > 0)
                            {
                                etpl.Append(
                                    $"{e} серия",
                                    title ?? original_title,
                                    sArhc,
                                    e.ToString(),
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
                            $"{host}/lite/phantom?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
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
                                    .OrderBy(e => e.Value.Value<int>("episode")))
                                {
                                    if (episode.Value.Value<int>("id_translation") != t)
                                        continue;

                                    string translation = episode.Value.Value<string>("translation");
                                    int e = episode.Value.Value<int>("episode");

                                    string link = $"{host}/lite/phantom/video?id_file={episode.Value.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                                    string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                                    if (e > 0)
                                    {
                                        etpl.Append(
                                            $"{e} серия",
                                            title ?? original_title,
                                            sArhc,
                                            e.ToString(),
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
                                $"{host}/lite/phantom?rjson={rjson}&s={s}&t={id_translation}{defaultargs}"
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

                            string link = $"{host}/lite/phantom/video?id_file={voice.Value<long>("id")}&token_movie={data.Value<string>("token_movie")}";
                            string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                            etpl.Append(
                                $"{episode.Key} серия",
                                title ?? original_title,
                                sArhc,
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
    [HttpGet]
    [Route("lite/phantom/video")]
    [Route("lite/phantom/video.m3u8")]
    async public Task<ActionResult> Video(long id_file, string token_movie, bool play)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string memkey = $"phantom:{id_file}:{token_movie}";

        if (!hybridCache.TryGetValue(memkey, out (StreamData watch, List<StreamQualityDto> streams) cache))
        {
            cache = await goMovie($"{init.linkhost}/?token_movie={token_movie}&token={init.token}", id_file);
            if (cache.streams.Count == 0)
                return OnError();

            hybridCache.Set(memkey, cache, DateTime.Now.AddMinutes(10));
        }

        var streamquality = new StreamQualityTpl();
        foreach (var item in cache.streams)
            streamquality.Append(HostStreamProxy(item.link, userdata: cache.watch), item.quality);

        var first = streamquality.Firts();

        if (play)
            return Redirect(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "auto",
            streamquality: streamquality,
            vast: init.vast,
            hls_manifest_timeout: (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region SpiderSearch
    [HttpGet]
    [Route("lite/phantom-search")]
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
                    $"{host}/lite/phantom?orid={j.Value<string>("token_movie")}",
                    PosterApi.Size(j.Value<string>("poster"))
                );
            }

            return stpl;
        });
    }
    #endregion


    #region search
    async ValueTask<(bool refresh_proxy, int category_id, JToken data)> search(string token_movie, string imdb_id, long kinopoisk_id, string title, int serial, string original_language, int year)
    {
        string memKey = $"mirage:view:{kinopoisk_id}:{imdb_id}";
        if (0 >= kinopoisk_id && string.IsNullOrEmpty(imdb_id))
            memKey = $"mirage:viewsearch:{title}:{serial}:{original_language}:{year}";

        if (!string.IsNullOrEmpty(token_movie))
            memKey = $"mirage:view:{token_movie}";

        JObject root;

        if (!hybridCache.TryGetValue(memKey, out (int category_id, JToken data) res))
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
                            int y = item.Value<int>("year");
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
                root = await httpHydra.Get<JObject>($"{init.apihost}/?token={init.token}&kp={kinopoisk_id}&imdb={imdb_id}&token_movie={token_movie}", safety: true);
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
    async Task<(StreamData watch, List<StreamQualityDto> streams)> goMovie(string uri, long id_file)
    {
        try
        {
            var watch = new StreamData();
            var streams = new List<StreamQualityDto>();

            using (var browser = new PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data).ConfigureAwait(false);
                if (page == null)
                    return default;

                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        if (route.Request.Url.Contains("kinogo-go.tv"))
                        {
                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                Body = PlaywrightBase.IframeHtml(uri)
                            });
                        }
                        else if (route.Request.Method == "POST" && route.Request.Url.Contains("/movies/"))
                        {
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
                            var jo = JsonConvert.DeserializeObject<JObject>(json);

                            var selectedItem =
                                jo["hlsSource"]
                                    .Children<JObject>()
                                    .FirstOrDefault(x => (bool?)x["default"] == true)
                                ??
                                jo["hlsSource"]
                                    .FirstOrDefault() as JObject;

                            foreach (var q in selectedItem["quality"].Children<JProperty>())
                            {
                                if (!init.m4s && (q.Name == "2160" || q.Name == "1440"))
                                    continue;

                                string link = (string)q.Value;
                                if (string.IsNullOrWhiteSpace(link))
                                    continue;

                                link = link
                                    .Split(new[] { " or " }, StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault()
                                    .Trim();

                                streams.Add(new StreamQualityDto(link, $"{q.Name}p"));
                            }

                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                Status = fetchResponse.Status,
                                Body = json,
                                Headers = fetchResponse.Headers
                            }).ConfigureAwait(false);
                        }
                        else if (route.Request.Url.EndsWith("/master.m3u8"))
                        {
                            watch.requestReferer = route.Request.Headers["referer"];
                            watch.requestOrigin = route.Request.Headers["origin"];
                            watch.edge_hash = route.Request.Headers["accepts-controls"];

                            if (init.debug)
                                Console.WriteLine(JsonConvert.SerializeObject(watch, Formatting.Indented));

                            browser.SetPageResult(route.Request.Url);
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

                            if (await PlaywrightBase.AbortOrCache(page, route))
                                return;

                            await route.ContinueAsync();
                        }
                    }
                    catch { }
                });

                PlaywrightBase.GotoAsync(page, "https://kinogo-go.tv/");

                await browser.WaitPageResult(15);
            }

            return (watch, streams);
        }
        catch
        {
            return default;
        }
    }
    #endregion
}
