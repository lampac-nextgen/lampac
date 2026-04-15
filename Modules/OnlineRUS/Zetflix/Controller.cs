using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Zetflix
{
    public class ZetflixController : BaseOnlineController<ModuleConf>
    {
        public ZetflixController() : base(ModInit.conf) { }

        static string PHPSESSID = null;

        sealed class LiveMovieSource
        {
            public string title { get; set; }
            public Dictionary<int, string> qualitys { get; set; }
            public List<HeadersModel> headers { get; set; }
        }

        [HttpGet]
        [Route("lite/zetflix")]
        public async Task<ActionResult> Index(bool checksearch, long id, int serial, long kinopoisk_id, string title, string original_title, string t, int s = -1, bool orightml = false, bool origsource = false, bool rjson = false)
        {
            if (kinopoisk_id == 0)
                return OnError();

            if (checksearch)
            {
                if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                    return OnError();

                return Content("data-json=");
            }

            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                return OnError();

            if (await IsRequestBlocked(rch: false))
                return badInitMsg;

            string ztfhost = await goHost(init.host);
            string log = $"{HttpContext.Request.Path.Value}\n\nstart init\n";

            if (serial != 1)
            {
                var liveMovie = await CaptureLiveMovie(ztfhost, kinopoisk_id, s, title, original_title);
                if (!liveMovie.IsEmpty)
                {
                    if (origsource)
                        return Content(liveMovie.ToJson(), "application/json; charset=utf-8");

                    return ContentTpl(liveMovie);
                }
            }

            var oninvk = new ZetflixInvoke
            (
                host,
                ztfhost,
                init.hls,
                (url, head) => httpHydra.Get(url, addheaders: head),
                onstreamtofile => HostStreamProxy(onstreamtofile)
            );

            int rs = serial == 1 ? (s == -1 ? 1 : s) : s;

            string html = await InvokeCache($"zetfix:view:{kinopoisk_id}:{rs}:{proxyManager?.CurrentProxyIp}", 20, async () =>
            {
                string uri = $"{ztfhost}/iplayer/videodb.php?kp={kinopoisk_id}" + (rs > 0 ? $"&season={rs}" : "");

                var headers = HeadersModel.Init(
                    Chromium.baseContextOptions.ExtraHTTPHeaders.ToDictionary(kv => kv.Key, kv => kv.Value),
                    ("Referer", "https://www.google.com/")
                );

                string result = string.IsNullOrEmpty(PHPSESSID) ? null : await Http.Get(uri, proxy: proxy, cookie: $"PHPSESSID={PHPSESSID}", headers: headers);
                if (result != null && !result.StartsWith("<script>(function"))
                {
                    if (!result.Contains("new Playerjs"))
                        return null;

                    proxyManager?.Success();
                    return result;
                }

                try
                {
                    using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                    {
                        log += "browser init\n";

                        var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>()
                        {
                            ["Referer"] = "https://www.google.com/"
                        }, proxy: proxy_data, keepopen: init.browser_keepopen).ConfigureAwait(false);

                        if (page == null)
                            return null;

                        if (init.browser_keepopen)
                        {
                            await page.Context.ClearCookiesAsync(new BrowserContextClearCookiesOptions
                            {
                                Domain = Regex.Replace(ztfhost, "^https?://", ""),
                                Name = "PHPSESSID"
                            }).ConfigureAwait(false);
                        }

                        log += "page init\n";

                        await page.RouteAsync("**/*", async route =>
                        {
                            try
                            {
                                if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                    return;

                                await route.ContinueAsync();
                            }
                            catch { }
                        });

                        await page.GotoAsync(uri, new PageGotoOptions()
                        {
                            Timeout = 15_000,
                            WaitUntil = WaitUntilState.NetworkIdle
                        }).ConfigureAwait(false);

                        result = await page.ContentAsync().ConfigureAwait(false);

                        log += $"{result}\n\n";

                        if (result == null || result.StartsWith("<script>(function"))
                        {
                            proxyManager?.Refresh();
                            return null;
                        }

                        var cook = await page.Context.CookiesAsync().ConfigureAwait(false);
                        PHPSESSID = cook?.FirstOrDefault(i => i.Name == "PHPSESSID")?.Value;

                        if (!result.Contains("new Playerjs"))
                            return null;

                        return result;
                    }
                }
                catch (Exception ex)
                {
                    log += $"\nex: {ex}\n";
                    return null;
                }
            });

            if (html == null)
            {
                var liveMovie = await CaptureLiveMovie(ztfhost, kinopoisk_id, rs, title, original_title);
                if (!liveMovie.IsEmpty)
                {
                    if (origsource)
                        return Content(liveMovie.ToJson(), "application/json; charset=utf-8");

                    return ContentTpl(liveMovie);
                }

                return OnError();
            }

            if (orightml)
                return Content(html, "text/plain; charset=utf-8");

            var content = oninvk.Embed(html);

            if ((content?.movie == true && NeedLiveMovieFallback(content)) || content?.pl == null)
            {
                var liveMovie = await CaptureLiveMovie(ztfhost, kinopoisk_id, rs, title, original_title);
                if (!liveMovie.IsEmpty)
                {
                    if (origsource)
                        return Content(liveMovie.ToJson(), "application/json; charset=utf-8");

                    return ContentTpl(liveMovie);
                }
            }

            if (content?.pl == null)
                return OnError();

            if (origsource)
                return Json(content);

            int number_of_seasons = 1;
            if (!content.movie && s == -1)
            {
                number_of_seasons = await InvokeCache($"zetfix:number_of_seasons:v7:{kinopoisk_id}:{id}", 1800, async () =>
                {
                    if (id > 0)
                    {
                        int tmdbSeasons = await oninvk.number_of_seasons(id);
                        if (tmdbSeasons > 1)
                            return tmdbSeasons;
                    }

                    return await NumberOfSeasons(kinopoisk_id, id, title, original_title, HttpContext.Request.Query["uid"].ToString());
                });
            }

            return ContentTpl(oninvk.Tpl(content, number_of_seasons, id, kinopoisk_id, title, original_title, t, s, vast: init.vast));
        }

        async ValueTask<int> NumberOfSeasons(long kinopoisk_id, long id, string title, string original_title, string uid)
        {
            int detected = 0;
            string encTitle = HttpUtility.UrlEncode(title ?? string.Empty);
            string encOriginalTitle = HttpUtility.UrlEncode(original_title ?? string.Empty);
            string encUid = HttpUtility.UrlEncode(uid ?? string.Empty);

            for (int season = 1; season <= 20; season++)
            {
                try
                {
                    string uri = $"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/lite/zetflix?serial=1&rjson=true&kinopoisk_id={kinopoisk_id}&s={season}&title={encTitle}&original_title={encOriginalTitle}";
                    if (id > 0)
                        uri += $"&id={id}";
                    if (!string.IsNullOrEmpty(encUid))
                        uri += $"&uid={encUid}";

                    string json = await Http.Get(uri, timeoutSeconds: 8, statusCodeOK: false);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var root = JsonNode.Parse(json);
                        if (root?["type"]?.GetValue<string>() == "episode" && (root["data"] as JsonArray)?.Count > 0)
                        {
                            detected = season;
                            continue;
                        }
                    }

                    if (season > 1 && detected > 0)
                        break;
                }
                catch
                {
                    if (season > 1 && detected > 0)
                        break;
                }
            }

            return detected > 0 ? detected : 1;
        }

        bool NeedLiveMovieFallback(EmbedModel content)
        {
            if (content?.pl == null || content.pl.Count == 0)
                return true;

            foreach (var item in content.pl)
            {
                if (string.IsNullOrEmpty(item.file))
                    continue;

                if (item.file.Contains("hdvideobox.me", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        async ValueTask<MovieTpl> CaptureLiveMovie(string ztfhost, long kinopoisk_id, int season, string title, string original_title)
        {
            var mtpl = new MovieTpl(title, original_title, 1);

            string memKey = $"zetflix:live_movie:v4:{kinopoisk_id}:{season}:{proxyManager?.CurrentProxyIp}";
            if (!hybridCache.TryGetValue(memKey, out List<LiveMovieSource> cache))
            {
                cache = await CaptureLiveMovieInternal(ztfhost, kinopoisk_id, season);
                if (cache?.Count > 0)
                    hybridCache.Set(memKey, cache, cacheTime(600));
            }

            if (cache == null || cache.Count == 0)
                return mtpl;

            mtpl = new MovieTpl(title, original_title, cache.Count);

            foreach (var source in cache)
            {
                if (source?.qualitys == null || source.qualitys.Count == 0)
                    continue;

                var streamquality = new StreamQualityTpl();
                foreach (var item in source.qualitys.OrderByDescending(i => i.Key))
                    streamquality.Append(HostStreamProxy(item.Value, headers: source.headers), $"{item.Key}p");

                var selected = streamquality.Firts();
                if (string.IsNullOrEmpty(selected.link))
                    continue;

                string voiceTitle = string.IsNullOrWhiteSpace(source.title) ? "По умолчанию" : source.title;
                mtpl.Append(voiceTitle, selected.link, streamquality: streamquality, vast: init.vast, hls_manifest_timeout: 30000);
            }

            return mtpl;
        }

        async ValueTask<List<LiveMovieSource>> CaptureLiveMovieInternal(string ztfhost, long kinopoisk_id, int season)
        {
            if (string.IsNullOrWhiteSpace(ztfhost) || kinopoisk_id == 0)
                return default;

            string source = $"/iplayer/videodb.php?kp={kinopoisk_id}" + (season > 0 ? $"&season={season}" : string.Empty);
            string playerId = Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
            string playerUri = $"{ztfhost}/iplayer/player.php?id={playerId}";
            string iframeUri = null;

            try
            {
                string playerHtml = await Http.Get(playerUri, headers: HeadersModel.Init(
                    ("Referer", $"{ztfhost}/"),
                    ("Origin", ztfhost)
                ));

                var fotpro = await TryFotproMovie(playerHtml);
                var validFotpro = await ValidateMovieManifests(fotpro);
                if (validFotpro?.Count > 0)
                    return validFotpro;

                string iframeSrc = Regex.Match(playerHtml ?? string.Empty, "<iframe[^>]*?\\ssrc=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(iframeSrc))
                {
                    iframeUri = iframeSrc.StartsWith("//") ? $"https:{iframeSrc}" :
                               iframeSrc.StartsWith("/") ? $"{ztfhost}{iframeSrc}" :
                               iframeSrc;

                    if (iframeUri.Contains(".obrut.show/", StringComparison.OrdinalIgnoreCase))
                    {
                        var obrut = await TryObrutMovie(iframeUri, playerUri, ztfhost);
                        var validObrut = await ValidateMovieManifests(obrut);
                        if (validObrut?.Count > 0)
                            return validObrut;
                    }
                }
            }
            catch { }

            try
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>()
                    {
                        ["Referer"] = "https://www.google.com/"
                    }, proxy: proxy_data, keepopen: init.browser_keepopen).ConfigureAwait(false);

                    if (page == null)
                        return default;

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            string url = route.Request.Url.Split("?")[0];
                            if (Regex.IsMatch(url, "\\.(woff2?|vtt|srt|css|svg|jpe?g|png|gif|webp|ico)$", RegexOptions.IgnoreCase))
                            {
                                await route.AbortAsync();
                                return;
                            }

                            await route.ContinueAsync();
                        }
                        catch { }
                    });

                    var qualitys = new Dictionary<int, string>();
                    List<HeadersModel> headers = null;

                    bool captured = await CaptureMovieWithBrowser(page, playerUri, qualitys, () => headers = FilterStreamHeaders(page));
                    if (!captured && !string.IsNullOrWhiteSpace(iframeUri))
                        captured = await CaptureMovieWithBrowser(page, iframeUri, qualitys, () => headers = FilterStreamHeaders(page));

                    if (!captured || qualitys.Count == 0)
                    {
                        proxyManager?.Refresh();
                        return default;
                    }

                    proxyManager?.Success();
                    return new List<LiveMovieSource>()
                    {
                        new LiveMovieSource() { title = "По умолчанию", qualitys = qualitys, headers = headers }
                    };
                }
            }
            catch
            {
                return default;
            }
        }

        [HttpGet]
        [Route("lite/zetflix/video")]
        [Route("lite/zetflix/video.m3u8")]
        public async ValueTask<ActionResult> Video(long kinopoisk_id, string title, string original_title, string t, int s, int e, bool play)
        {
            if (kinopoisk_id == 0 || s <= 0 || e <= 0 || string.IsNullOrWhiteSpace(t))
                return OnError();

            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                return OnError();

            if (await IsRequestBlocked(rch: false))
                return badInitMsg;

            string ztfhost = await goHost(init.host);
            var cache = await InvokeCache($"zetflix:live_serial:v8:{kinopoisk_id}:{s}:{e}:{t}:{proxyManager?.CurrentProxyIp}", 300,
                async () => await CaptureLiveSerialEpisodeInternal(ztfhost, kinopoisk_id, s, t, e)
            );

            if (cache.qualitys == null || cache.qualitys.Count == 0)
                return OnError();

            var streamquality = new StreamQualityTpl();
            foreach (var item in cache.qualitys.OrderByDescending(i => i.Key))
                streamquality.Append(HostStreamProxy(item.Value, headers: cache.headers), $"{item.Key}p");

            var selected = streamquality.Firts();
            if (string.IsNullOrWhiteSpace(selected.link))
                return OnError();

            if (play)
                return Redirect(selected.link);

            return ContentTo(VideoTpl.ToJson("play", selected.link, $"{title ?? original_title} ({e} серия)", streamquality: streamquality, quality: selected.quality, vast: init.vast, hls_manifest_timeout: 30000));
        }

        async ValueTask<(Dictionary<int, string> qualitys, List<HeadersModel> headers)> CaptureLiveSerialEpisodeInternal(string ztfhost, long kinopoisk_id, int season, string voice, int episode)
        {
            if (string.IsNullOrWhiteSpace(ztfhost) || string.IsNullOrWhiteSpace(voice) || season <= 0 || episode <= 0)
                return default;

            string source = $"/iplayer/videodb.php?kp={kinopoisk_id}&season={season}";
            string playerId = Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
            string pageUri = $"{ztfhost}/iplayer/player.php?id={playerId}";

            try
            {
                string playerHtml = await Http.Get(pageUri, headers: HeadersModel.Init(
                    ("Referer", $"{ztfhost}/"),
                    ("Origin", ztfhost)
                ));

                string iframeSrc = Regex.Match(playerHtml ?? string.Empty, "<iframe[^>]*?\\ssrc=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrWhiteSpace(iframeSrc))
                    return default;

                string iframeUri = iframeSrc.StartsWith("//") ? $"https:{iframeSrc}" :
                                   iframeSrc.StartsWith("/") ? $"{ztfhost}{iframeSrc}" :
                                   iframeSrc;

                if (!iframeUri.Contains(".obrut.show/", StringComparison.OrdinalIgnoreCase))
                    return await TryPlayerjsSerial(ztfhost, kinopoisk_id, season, voice, episode);

                return await TryObrutSerial(iframeUri, pageUri, ztfhost, season, episode, voice);
            }
            catch
            {
                return default;
            }
        }

        async ValueTask<(Dictionary<int, string> qualitys, List<HeadersModel> headers)> TryPlayerjsSerial(string ztfhost, long kinopoisk_id, int season, string voice, int episode)
        {
            try
            {
                string uri = $"{ztfhost}/iplayer/videodb.php?kp={kinopoisk_id}&season={season}";
                var reqHeaders = HeadersModel.Init(
                    ("dnt", "1"),
                    ("pragma", "no-cache"),
                    ("referer", "https://www.google.com/"),
                    ("upgrade-insecure-requests", "1")
                );

                string html = string.IsNullOrEmpty(PHPSESSID) ? null :
                    await Http.Get(uri, proxy: proxy, cookie: $"PHPSESSID={PHPSESSID}", headers: reqHeaders);

                if (string.IsNullOrWhiteSpace(html) || html.StartsWith("<script>(function") || !html.Contains("new Playerjs"))
                    return default;

                var oninvk = new ZetflixInvoke(null, ztfhost, init.hls,
                    (url, head) => httpHydra.Get(url, addheaders: head),
                    link => link);

                var embed = oninvk.Embed(html);
                if (embed?.pl == null || embed.pl.Count == 0 || embed.movie)
                    return default;

                string normalizedVoice = NormalizeObrutNodeText(voice);

                foreach (var pl in embed.pl)
                {
                    string plVoice = NormalizeObrutNodeText(pl.title);
                    bool voiceMatch = string.IsNullOrEmpty(normalizedVoice) ||
                                      plVoice.Contains(normalizedVoice, StringComparison.OrdinalIgnoreCase) ||
                                      normalizedVoice.Contains(plVoice, StringComparison.OrdinalIgnoreCase);
                    if (!voiceMatch)
                        continue;

                    if (pl.folder == null)
                        continue;

                    foreach (var ep in pl.folder)
                    {
                        string epNum = Regex.Match(ep.comment ?? string.Empty, "^([0-9]+)").Groups[1].Value;
                        if (epNum != episode.ToString())
                            continue;

                        var qualitys = ParsePlayerjsQualityMap(ep.file);
                        if (qualitys.Count > 0)
                            return (qualitys, null);
                    }
                }

                return default;
            }
            catch
            {
                return default;
            }
        }

        static Dictionary<int, string> ParsePlayerjsQualityMap(string file)
        {
            var qualitys = new Dictionary<int, string>();
            if (string.IsNullOrWhiteSpace(file))
                return qualitys;

            foreach (Match m in Regex.Matches(file, @"\[(1080|720|480|360)p?\](https?://[^\[|,\n\r\t ]+)", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out int q))
                    continue;

                qualitys[q] = m.Groups[2].Value.TrimEnd();
            }

            return qualitys;
        }

        async ValueTask<bool> CaptureMovieWithBrowser(IPage page, string targetUri, Dictionary<int, string> qualitys, Action onHeaders)
        {
            if (string.IsNullOrWhiteSpace(targetUri))
                return false;

            DateTime lastHit = default;

            try
            {
                await page.GotoAsync(targetUri, new PageGotoOptions()
                {
                    Timeout = 8_000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                }).ConfigureAwait(false);
            }
            catch { }

            for (int i = 0; i < 60; i++)
            {
                try
                {
                    var resources = await page.EvaluateAsync<string[]>("() => performance.getEntriesByType('resource').map(r => r.name)");
                    if (resources != null)
                    {
                        foreach (string url in resources)
                        {
                            var quality = Regex.Match(url ?? string.Empty, @"/(240|360|480|720|1080|1440|2160)\\.mp4:hls:manifest\\.m3u8(?:\\?|$)", RegexOptions.IgnoreCase);
                            if (!quality.Success)
                                continue;

                            qualitys[int.Parse(quality.Groups[1].Value)] = url;
                            lastHit = DateTime.Now;
                            onHeaders?.Invoke();
                        }
                    }
                }
                catch { }

                if (qualitys.Count > 0 && (DateTime.Now - lastHit).TotalMilliseconds > 700)
                    return true;

                if (i % 8 == 0)
                {
                    try
                    {
                        await page.Mouse.ClickAsync(640, 360);
                    }
                    catch { }

                    try
                    {
                        foreach (var frame in page.Frames)
                        {
                            await frame.EvaluateAsync(@"() => {
                                const button = document.querySelector('button, .play, .vjs-big-play-button, .jw-icon-display, .jw-display-icon-container');
                                if (button) button.click();
                                const video = document.querySelector('video');
                                if (video) {
                                    video.muted = true;
                                    video.play().catch(() => {});
                                }
                            }");
                        }
                    }
                    catch { }
                }

                await Task.Delay(100);
            }

            return qualitys.Count > 0;
        }

        List<HeadersModel> FilterStreamHeaders(IPage page)
        {
            return HeadersModel.Init(
                ("accept", "*/*"),
                ("origin", page.Url is string current && current.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Regex.Match(current, "^(https?://[^/]+)", RegexOptions.IgnoreCase).Groups[1].Value : string.Empty),
                ("referer", page.Url)
            ).Where(h => !string.IsNullOrWhiteSpace(h.val)).ToList();
        }

        async ValueTask<List<LiveMovieSource>> ValidateMovieManifests(List<LiveMovieSource> streams)
        {
            if (streams == null || streams.Count == 0)
                return null;

            var valid = new List<LiveMovieSource>(streams.Count);
            foreach (var stream in streams)
            {
                if (stream?.qualitys == null || stream.qualitys.Count == 0)
                    continue;

                if (await ValidateMovieManifest(stream.qualitys, stream.headers))
                    valid.Add(stream);
            }

            return valid.Count > 0 ? valid : null;
        }

        async ValueTask<bool> ValidateMovieManifest(Dictionary<int, string> qualitys, List<HeadersModel> headers)
        {
            if (qualitys == null || qualitys.Count == 0)
                return false;

            string probeUrl = qualitys.OrderByDescending(i => i.Key).First().Value?.Split("#.")[0];
            if (string.IsNullOrWhiteSpace(probeUrl))
                return false;

            try
            {
                string manifest = await Http.Get(probeUrl, headers: headers, timeoutSeconds: 5, statusCodeOK: false);
                return !string.IsNullOrWhiteSpace(manifest) && manifest.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        async ValueTask<List<LiveMovieSource>> TryFotproMovie(string playerHtml)
        {
            if (string.IsNullOrWhiteSpace(playerHtml))
                return default;

            string fallbackSrc = Regex.Match(playerHtml, "data-fallback-src=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(fallbackSrc))
                return default;

            string fallbackOrigin = Regex.Match(fallbackSrc, "^(https?://[^/]+)").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(fallbackOrigin))
                return default;

            string iframeHtml = await Http.Get(fallbackSrc, headers: HeadersModel.Init(
                ("Referer", $"{fallbackOrigin}/"),
                ("Origin", fallbackOrigin)
            ));

            if (string.IsNullOrWhiteSpace(iframeHtml))
                return default;

            string href = Regex.Match(iframeHtml, "\"href\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
            string key = Regex.Match(iframeHtml, "\"key\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value.Replace("\\", "");
            string file = Regex.Match(iframeHtml, "\"file\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value.Replace("\\", "");

            if (string.IsNullOrWhiteSpace(file))
            {
                href = Regex.Match(iframeHtml, "\"href\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                key = Regex.Match(iframeHtml, "\"key\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value.Replace("\\", "");
                file = Regex.Match(iframeHtml, "\"file\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value.Replace("\\", "");
            }

            if (string.IsNullOrWhiteSpace(file))
            {
                href = Regex.Match(iframeHtml, "\"href\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                key = Regex.Match(iframeHtml, "\"key\":\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value.Replace("\\", "");
                file = Regex.Match(iframeHtml, "playerConfigs\\s*=\\s*\\{.*?\"file\":\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value.Replace("\\", "");

                if (string.IsNullOrWhiteSpace(href))
                    href = Regex.Match(iframeHtml, "playerConfigs\\s*=\\s*\\{.*?\"href\":\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value.Replace("\\", "");

                if (string.IsNullOrWhiteSpace(key))
                    key = Regex.Match(iframeHtml, "playerConfigs\\s*=\\s*\\{.*?\"key\":\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value.Replace("\\", "");
            }

            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(key))
                return default;

            var playlistHeaders = HeadersModel.Init(
                ("accept", "*/*"),
                ("origin", fallbackOrigin),
                ("referer", $"{fallbackOrigin}/"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-site"),
                ("x-csrf-token", key)
            );

            string playlist = await Http.Post($"https://vid11.{href}/playlist/{file}.txt", "", timeoutSeconds: 8, removeContentType: true, statusCodeOK: false, headers: playlistHeaders);
            if (string.IsNullOrWhiteSpace(playlist))
                playlist = await httpHydra.Post($"https://vid11.{href}/playlist/{file}.txt", "", addheaders: playlistHeaders);

            if (string.IsNullOrWhiteSpace(playlist))
                return default;

            string masterUrl = playlist.Trim();
            if (!masterUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return default;

            string master = await Http.Get(masterUrl, headers: HeadersModel.Init(
                ("origin", fallbackOrigin),
                ("referer", $"{fallbackOrigin}/")
            ), timeoutSeconds: 8, statusCodeOK: false);

            var headers = HeadersModel.Init(
                ("origin", fallbackOrigin),
                ("referer", $"{fallbackOrigin}/"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "cross-site")
            );

            var qualitys = new Dictionary<int, string>();
            foreach (var variant in ParseVariants(master, masterUrl))
            {
                if (variant.height > 0)
                    qualitys[variant.height] = $"{variant.url}#.m3u8";
            }

            if (qualitys.Count == 0)
                qualitys[1080] = $"{masterUrl}#.m3u8";

            return new List<LiveMovieSource>()
            {
                new LiveMovieSource() { title = "По умолчанию", qualitys = qualitys, headers = headers }
            };
        }

        async ValueTask<List<LiveMovieSource>> TryObrutMovie(string iframeUri, string playerUri, string ztfhost)
        {
            if (string.IsNullOrWhiteSpace(iframeUri))
                return default;

            string iframeOrigin = Regex.Match(iframeUri, "^(https?://[^/]+)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(iframeOrigin))
                return default;

            var cookieContainer = new CookieContainer();
            var iframeResult = await Http.BaseGet(iframeUri,
                headers: HeadersModel.Init(
                    ("Referer", playerUri),
                    ("Origin", ztfhost),
                    ("Sec-Fetch-Dest", "iframe"),
                    ("Sec-Fetch-Mode", "navigate"),
                    ("Sec-Fetch-Site", "cross-site")
                ),
                timeoutSeconds: 15,
                statusCodeOK: false,
                cookieContainer: cookieContainer
            );

            string iframeHtml = iframeResult.content;
            if (string.IsNullOrWhiteSpace(iframeHtml))
                return default;

            if (TryParseObrutPlayerRoot(iframeHtml, out JsonObject root))
            {
                var movieVoices = FindObrutMovieVoices(root);
                if (movieVoices.Count > 0)
                {
                    var result = new List<LiveMovieSource>(movieVoices.Count);

                    foreach (var voiceNode in movieVoices)
                    {
                        var qualitys = ParseObrutQualityMap(voiceNode.file);
                        if (qualitys.Count == 0)
                            continue;

                        string cookie = BuildCookieHeader(cookieContainer, new Uri(qualitys.First().Value.Replace("#.m3u8", "")));
                        var headers = HeadersModel.Init(
                            ("accept", "*/*"),
                            ("origin", iframeOrigin),
                            ("referer", iframeUri),
                            ("sec-fetch-dest", "empty"),
                            ("sec-fetch-mode", "cors"),
                            ("sec-fetch-site", "same-site")
                        );

                        if (!string.IsNullOrEmpty(cookie))
                            headers.Add(new HeadersModel("cookie", cookie));

                        result.Add(new LiveMovieSource()
                        {
                            title = voiceNode.title,
                            qualitys = qualitys,
                            headers = headers
                        });
                    }

                    if (result.Count > 0)
                        return result;
                }
            }

            string playerData = Regex.Match(iframeHtml, "new\\s+Player\\(\"([^\"]+)\"\\)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(playerData))
                return default;

            int payloadIndex = playerData.IndexOf("eyJ", StringComparison.Ordinal);
            if (payloadIndex == -1)
                return default;

            try
            {
                string payload = playerData[payloadIndex..];
                payload = payload
                    .Replace("//dC90L3Q=", string.Empty)
                    .Replace("//Yi9iL2I=", string.Empty)
                    .Replace("//dS91L3U=", string.Empty)
                    .Replace("//ci9yL3I=", string.Empty)
                    .Replace("//by9vL28=", string.Empty);

                byte[] bytes = Convert.FromBase64String(payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '='));
                string decoded = Encoding.Latin1.GetString(bytes);

                var qualitys = new Dictionary<int, string>();
                foreach (Match m in Regex.Matches(decoded, "\\[(240|360|480|720|1080)p\\](https?:\\\\/\\\\/[^,\\\"]+)", RegexOptions.IgnoreCase))
                {
                    if (!int.TryParse(m.Groups[1].Value, out int q))
                        continue;

                    string url = m.Groups[2].Value.Replace("\\/", "/");
                    qualitys[q] = $"{url}#.m3u8";
                }

                if (qualitys.Count == 0)
                {
                    string auto = Regex.Match(decoded, "\\[[^\\]]*Авто[^\\]]*\\](https?:\\\\/\\\\/[^,\\\"]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (!string.IsNullOrWhiteSpace(auto))
                        qualitys[1080] = $"{auto.Replace("\\/", "/")}#.m3u8";
                }

                if (qualitys.Count == 0)
                    return default;

                string cookie = BuildCookieHeader(cookieContainer, new Uri(qualitys.First().Value.Replace("#.m3u8", "")));
                var headers = HeadersModel.Init(
                    ("accept", "*/*"),
                    ("origin", iframeOrigin),
                    ("referer", iframeUri),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "same-site")
                );

                if (!string.IsNullOrEmpty(cookie))
                    headers.Add(new HeadersModel("cookie", cookie));

                return new List<LiveMovieSource>()
                {
                    new LiveMovieSource() { title = "По умолчанию", qualitys = qualitys, headers = headers }
                };
            }
            catch
            {
                return default;
            }
        }

        async ValueTask<(Dictionary<int, string> qualitys, List<HeadersModel> headers)> TryObrutSerial(string iframeUri, string playerUri, string ztfhost, int season, int episode, string voice)
        {
            if (string.IsNullOrWhiteSpace(iframeUri) || season <= 0 || episode <= 0)
                return default;

            string iframeOrigin = Regex.Match(iframeUri, "^(https?://[^/]+)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(iframeOrigin))
                return default;

            var cookieContainer = new CookieContainer();
            var iframeResult = await Http.BaseGet(iframeUri,
                headers: HeadersModel.Init(
                    ("Referer", playerUri),
                    ("Origin", ztfhost),
                    ("Sec-Fetch-Dest", "iframe"),
                    ("Sec-Fetch-Mode", "navigate"),
                    ("Sec-Fetch-Site", "cross-site")
                ),
                timeoutSeconds: 15,
                statusCodeOK: false,
                cookieContainer: cookieContainer
            );

            string iframeHtml = iframeResult.content;
            if (string.IsNullOrWhiteSpace(iframeHtml))
                return default;

            if (!TryParseObrutPlayerRoot(iframeHtml, out JsonObject root))
                return default;

            string file = FindObrutSerialFile(root, season, episode, voice);
            if (string.IsNullOrWhiteSpace(file))
                return default;

            var qualitys = ParseObrutQualityMap(file);
            if (qualitys.Count == 0)
                return default;

            string cookie = BuildCookieHeader(cookieContainer, new Uri(qualitys.First().Value.Replace("#.m3u8", "")));
            var headers = HeadersModel.Init(
                ("accept", "*/*"),
                ("origin", iframeOrigin),
                ("referer", iframeUri),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-site")
            );

            if (!string.IsNullOrEmpty(cookie))
                headers.Add(new HeadersModel("cookie", cookie));

            return (qualitys, headers);
        }

        static bool TryParseObrutPlayerRoot(string iframeHtml, out JsonObject root)
        {
            root = null;

            string playerData = Regex.Match(iframeHtml ?? string.Empty, "new\\s+Player\\(\"([^\"]+)\"\\)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(playerData))
                return false;

            string decoded = DecodeObrutPlayerPayload(playerData);
            if (string.IsNullOrWhiteSpace(decoded) || !TryExtractBalancedJsonObject(decoded, out string jsonText))
                return false;

            try
            {
                root = JsonNode.Parse(jsonText) as JsonObject;
                return root != null;
            }
            catch
            {
                return false;
            }
        }

        static string DecodeObrutPlayerPayload(string playerData)
        {
            if (string.IsNullOrWhiteSpace(playerData))
                return null;

            int payloadIndex = playerData.IndexOf("eyJ", StringComparison.Ordinal);
            if (payloadIndex == -1)
                return null;

            try
            {
                string payload = playerData[payloadIndex..];
                payload = payload
                    .Replace("//dC90L3Q=", string.Empty)
                    .Replace("//Yi9iL2I=", string.Empty)
                    .Replace("//dS91L3U=", string.Empty)
                    .Replace("//ci9yL3I=", string.Empty)
                    .Replace("//by9vL28=", string.Empty);

                int padding = (4 - payload.Length % 4) % 4;
                byte[] bytes = Convert.FromBase64String(payload.PadRight(payload.Length + padding, '='));
                return Encoding.Latin1.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        static bool TryExtractBalancedJsonObject(string text, out string jsonText)
        {
            jsonText = null;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            int start = text.IndexOf('{');
            if (start == -1)
                return false;

            bool inString = false;
            bool escaped = false;
            int depth = 0;

            for (int i = start; i < text.Length; i++)
            {
                char ch = text[i];

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                    continue;
                }

                if (ch != '}')
                    continue;

                depth--;
                if (depth == 0)
                {
                    jsonText = text[start..(i + 1)];
                    return true;
                }
            }

            return false;
        }

        static string FindObrutSerialFile(JsonObject root, int season, int episode, string voice)
        {
            if (root?["file"] is not JsonArray seasons)
                return null;

            JsonObject seasonNode = FindObrutFolderByNumber(seasons, season);
            if (seasonNode == null && seasons.Count == 1 && season == 1)
                seasonNode = seasons[0] as JsonObject;

            if (seasonNode == null || seasonNode["folder"] is not JsonArray episodes)
                return null;

            JsonObject episodeNode = FindObrutFolderByNumber(episodes, episode);
            if (episodeNode == null)
                return null;

            if (episodeNode["folder"] is JsonArray voices && voices.Count > 0)
            {
                JsonObject voiceNode = FindObrutVoiceNode(voices, voice) ?? voices[0] as JsonObject;
                return voiceNode?["file"]?.GetValue<string>();
            }

            return episodeNode["file"]?.GetValue<string>();
        }

        static List<(string title, string file)> FindObrutMovieVoices(JsonObject root)
        {
            var result = new List<(string title, string file)>();
            if (root?["file"] == null)
                return result;

            if (root["file"] is JsonArray voices)
            {
                foreach (var item in voices)
                {
                    if (item is not JsonObject obj)
                        continue;

                    string file = obj["file"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(file))
                        continue;

                    result.Add((NormalizeObrutMovieVoiceTitle(obj["title"]?.GetValue<string>(), result.Count + 1), file));
                }

                return result;
            }

            string single = root["file"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(single))
                result.Add(("По умолчанию", single));

            return result;
        }

        static JsonObject FindObrutFolderByNumber(JsonArray items, int number)
        {
            foreach (var item in items)
            {
                if (item is not JsonObject obj)
                    continue;

                if (!TryExtractNodeNumber(obj["title"]?.GetValue<string>(), out int current))
                    continue;

                if (current == number)
                    return obj;
            }

            return null;
        }

        static JsonObject FindObrutVoiceNode(JsonArray items, string voice)
        {
            if (items == null || items.Count == 0)
                return null;

            string wanted = NormalizeObrutNodeText(voice);
            JsonObject partial = null;

            foreach (var item in items)
            {
                if (item is not JsonObject obj)
                    continue;

                string current = NormalizeObrutNodeText(obj["title"]?.GetValue<string>());
                if (string.IsNullOrWhiteSpace(current))
                    continue;

                if (string.IsNullOrWhiteSpace(wanted))
                    return obj;

                if (current == wanted || current.Contains(wanted, StringComparison.OrdinalIgnoreCase) || wanted.Contains(current, StringComparison.OrdinalIgnoreCase))
                    return obj;

                if (partial == null && current.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
                    partial = obj;
            }

            return partial;
        }

        static bool TryExtractNodeNumber(string text, out int number)
        {
            number = 0;
            var match = Regex.Match(text ?? string.Empty, "(\\d+)");
            return match.Success && int.TryParse(match.Groups[1].Value, out number);
        }

        static string NormalizeObrutNodeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return Regex.Replace(text, "\\s+", " ").Trim().ToLowerInvariant();
        }

        static string NormalizeObrutMovieVoiceTitle(string text, int index)
        {
            text = Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            return index <= 1 ? "По умолчанию" : $"Озвучка {index}";
        }

        static Dictionary<int, string> ParseObrutQualityMap(string file)
        {
            var qualitys = new Dictionary<int, string>();

            foreach (Match m in Regex.Matches(file ?? string.Empty, "\\[(240|360|480|720|1080)p\\](https?:\\\\/\\\\/[^,\\\"]+|https?://[^,\\\"]+)", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out int q))
                    continue;

                string url = m.Groups[2].Value.Replace("\\/", "/");
                qualitys[q] = $"{url}#.m3u8";
            }

            if (qualitys.Count > 0)
                return qualitys;

            string auto = Regex.Match(file ?? string.Empty, "\\[(?:auto|авто)\\](https?:\\\\/\\\\/[^,\\\"]+|https?://[^,\\\"]+)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(auto))
                qualitys[1080] = $"{auto.Replace("\\/", "/")}#.m3u8";

            if (qualitys.Count > 0)
                return qualitys;

            string direct = Regex.Match(file ?? string.Empty, "(https?:\\\\/\\\\/[^,\\\"]+|https?://[^,\\\"]+)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(direct))
                qualitys[1080] = $"{direct.Replace("\\/", "/")}#.m3u8";

            return qualitys;
        }

        static List<(string url, int height)> ParseVariants(string master, string masterUrl)
        {
            var result = new List<(string url, int height)>();
            if (string.IsNullOrWhiteSpace(master))
                return result;

            var lines = master.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length - 1; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
                    continue;

                int height = 0;
                var resolution = Regex.Match(line, @"RESOLUTION=\d+x(?<h>\d+)", RegexOptions.IgnoreCase);
                if (resolution.Success)
                    int.TryParse(resolution.Groups["h"].Value, out height);

                string next = lines[i + 1].Trim();
                if (string.IsNullOrEmpty(next) || next.StartsWith("#"))
                    continue;

                try
                {
                    result.Add((new Uri(new Uri(masterUrl), next).ToString(), height));
                }
                catch { }
            }

            return result;
        }

        static List<HeadersModel> FilterStreamHeaders(IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null || headers.Count == 0)
                return null;

            var result = new List<HeadersModel>(headers.Count);

            foreach (var item in headers)
            {
                string key = item.Key.ToLowerInvariant();
                if (key is "host" or "accept-encoding" or "connection" or "range" or "content-length")
                    continue;

                result.Add(new HeadersModel(item.Key, item.Value));
            }

            return result.Count > 0 ? result : null;
        }

        static string BuildCookieHeader(CookieContainer cookieContainer, Uri uri)
        {
            if (cookieContainer == null || uri == null)
                return null;

            var parts = new List<string>();
            foreach (System.Net.Cookie cookie in cookieContainer.GetCookies(uri))
                parts.Add($"{cookie.Name}={cookie.Value}");

            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }

        async ValueTask<string> goHost(string host)
        {
            if (!Regex.IsMatch(host ?? string.Empty, "^https?://go\\."))
                return host;

            string backhost = "https://7apr.zet-flix.online";

            string memkey = $"zeflix:gohost:v4:{host}";
            if (hybridCache.TryGetValue(memkey, out string ztfhost))
            {
                if (string.IsNullOrEmpty(ztfhost))
                    return backhost;

                return ztfhost;
            }

            string html = await httpHydra.Get(host);
            if (html != null)
            {
                ztfhost = Regex.Match(html, "\"([^\"]+)\"\\);</script>").Groups[1].Value;
                if (!string.IsNullOrEmpty(ztfhost))
                {
                    ztfhost = $"https://{ztfhost}";
                    hybridCache.Set(memkey, ztfhost, DateTime.Now.AddMinutes(20));
                    return ztfhost;
                }
            }

            try
            {
                using var browser = new PlaywrightBrowser(init.priorityBrowser);
                var page = await browser.NewPageAsync(init.plugin, headers: Http.defaultFullHeaders, proxy: proxy_data).ConfigureAwait(false);
                if (page != null)
                {
                    await page.GotoAsync(host, new PageGotoOptions()
                    {
                        Timeout = 20_000,
                        WaitUntil = WaitUntilState.DOMContentLoaded
                    }).ConfigureAwait(false);

                    ztfhost = page.Url;
                    if (!string.IsNullOrWhiteSpace(ztfhost) && Uri.TryCreate(ztfhost, UriKind.Absolute, out var uri) && Regex.IsMatch(uri.Host, @"^\d+apr\.zet-flix\.online$", RegexOptions.IgnoreCase))
                    {
                        ztfhost = $"{uri.Scheme}://{uri.Host}";
                        hybridCache.Set(memkey, ztfhost, DateTime.Now.AddMinutes(20));
                        return ztfhost;
                    }
                }
            }
            catch { }

            hybridCache.Set(memkey, string.Empty, DateTime.Now.AddMinutes(1));

            return backhost;
        }
    }
}
