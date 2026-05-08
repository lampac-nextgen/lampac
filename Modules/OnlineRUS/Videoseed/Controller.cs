using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services.Utilities;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Videoseed;

public class VideoseedController : BaseOnlineController
{
    public VideoseedController() : base(ModInit.conf) { }

    [HttpGet]
    [Staticache]
    [Route("lite/videoseed")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int s = -1, bool rjson = false, int serial = -1)
    {
        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
            return OnError();

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (string.IsNullOrEmpty(init.token))
            return OnError();

        var cache = await InvokeCacheResult<Data>($"videoseed:view:{kinopoisk_id}:{imdb_id}:{original_title}", TimeSpan.FromHours(4), async e =>
        {
            var data =
                await goSearch(serial, kinopoisk_id > 0, $"&kp={kinopoisk_id}") ??
                await goSearch(serial, !string.IsNullOrEmpty(imdb_id), $"&tmdb={imdb_id}") ??
                await goSearch(serial, !string.IsNullOrEmpty(original_title), $"&q={HttpUtility.UrlEncode(original_title)}&release_year_from={year - 1}&release_year_to={year + 1}");

            if (data == null)
                return e.Fail("search_data", refresh_proxy: true);

            if (data?.seasons == null && string.IsNullOrEmpty(data?.iframe))
                return e.Fail("empty_embed", refresh_proxy: true);

            return e.Success(data);
        });

        return ContentTpl(cache, () =>
        {
            if (cache.Value.seasons != null)
            {
                #region Сериал
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);

                if (s == -1)
                {
                    var tpl = new SeasonTpl(cache.Value.seasons.Count);

                    foreach (var season in cache.Value.seasons)
                    {
                        tpl.Append(
                            $"{season.Key} сезон",
                            $"{host}/lite/videoseed?rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={season.Key}",
                            season.Key
                        );
                    }

                    return tpl;
                }
                else
                {
                    string sArhc = s.ToString();
                    var videos = cache.Value.seasons.First(i => i.Key == sArhc).Value.videos;

                    var etpl = new EpisodeTpl(videos.Count);

                    foreach (var video in videos)
                    {
                        string iframe = video.Value.iframe;
                        etpl.Append(
                            $"{video.Key} серия",
                            title ?? original_title,
                            sArhc,
                            video.Key,
                            accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(iframe)}") + "#.m3u8",
                            "call",
                            vast: init.vast
                        );
                    }

                    return etpl;
                }
                #endregion
            }
            else
            {
                #region Фильм
                var mtpl = new MovieTpl(title, original_title, 1);

                if (cache.Value.translation_iframe?.Count > 0)
                {
                    foreach (var translation in cache.Value.translation_iframe)
                    {
                        string voice = translation.Value.short_name;

                        mtpl.Append(
                            voice ?? translation.Value.name ?? translation.Key,
                            accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(cache.Value.iframe)}") + $"&voice={HttpUtility.UrlEncode(voice)}" + "#.m3u8",
                            "call",
                            vast: init.vast
                        );
                    }
                }
                else
                {
                    mtpl.Append(
                        "По-умолчанию",
                        accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(cache.Value.iframe)}") + "#.m3u8",
                        "call",
                        vast: init.vast
                    );
                }

                return mtpl;
                #endregion
            }
        });
    }

    #region Video
    [HttpGet]
    [Route("lite/videoseed/video/{*iframe}")]
    async public Task<ActionResult> Video(string iframe, string voice)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        iframe = AesTo.Decrypt(iframe);
        if (string.IsNullOrEmpty(iframe))
            return OnError();

        var cache = await InvokeCacheResult<string>($"videoseed:video:{iframe}:{proxyManager?.CurrentProxyIp}", 20, async e =>
        {
            var headers = httpHeaders(init);

            try
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data, headers: headers?.ToDictionary()).ConfigureAwait(false);
                    if (page == null)
                        return e.Fail("page");

                    //await page.AddInitScriptAsync("localStorage.setItem('pljsquality', '1080p');").ConfigureAwait(false);

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (route.Request.Url.Contains("videoseed.tv"))
                            {
                                await route.FulfillAsync(new RouteFulfillOptions
                                {
                                    Body = PlaywrightBase.IframeHtml(iframe)
                                });
                            }
                            else if (route.Request.Url == iframe)
                            {
                                string html = null;
                                await route.ContinueAsync();

                                var response = await page.WaitForResponseAsync(route.Request.Url);
                                if (response != null)
                                    html = await response.TextAsync();

                                browser.SetPageResult(html);
                                return;
                            }
                            else
                            {
                                //if (browser.IsCompleted || route.Request.Url.Contains(".xml") || route.Request.Url.Contains(".php"))
                                //{
                                //    await route.AbortAsync();
                                //    return;
                                //}

                                //if (route.Request.Url.Contains("/hls.m3u8"))
                                //{
                                //    browser.SetPageResult(route.Request.Url);
                                //    await route.AbortAsync();
                                //    return;
                                //}

                                //if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                //    return;

                                await route.ContinueAsync();
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "Videoseed", "id_m1o18qjn");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, "https://videoseed.tv");

                    string html = await browser.WaitPageResult().ConfigureAwait(false);
                    if (html == null)
                        return e.Fail("wait_page_result", refresh_proxy: true);

                    string file = Regex.Match(html, "Playerjs\\(\"([^\"]+)").Groups[1].Value;
                    if (string.IsNullOrEmpty(file) || file.Length <= 2)
                        return e.Fail("playerjs_file", refresh_proxy: true);

                    string cleaned = Regex.Replace(file.Substring(2), @"\|\|\|[^=\|]+==", string.Empty);
                    if (cleaned.Contains("|||"))
                        cleaned = Regex.Replace(cleaned, @"\|\|\|[^=\|]+==", string.Empty);

                    string json = CrypTo.DecodeBase64(cleaned);
                    if (string.IsNullOrEmpty(json) || !json.Contains(".m3u8"))
                        return e.Fail("json");

                    return e.Success(json);
                }
            }
            catch
            {
                return e.Fail("exception");
            }
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string location = null;

        if (voice != null)
            location = Regex.Match(cache.Value, "\\{" + voice + "\\} ?(https?://[^\\;\\{\"\n\r\t ]+\\.m3u8)").Groups[1].Value;

        if (string.IsNullOrEmpty(location))
            location = Regex.Match(cache.Value, "(https?://[^\\;\\{\"\n\r\t ]+\\.m3u8)").Groups[1].Value;

        if (string.IsNullOrEmpty(location))
            return OnError("location");

        string referer = Regex.Match(iframe, "(^https?://[^/]+)").Groups[1].Value;
        var headers_stream = httpHeaders(init.host, HeadersModel.Join(HeadersModel.Init("referer", referer), init.headers_stream));

        return ContentTo(VideoTpl.ToJson(
            "play",
            HostStreamProxy(location, headers: headers_stream),
            "auto",
            vast: init.vast,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region goSearch
    async Task<Data> goSearch(int serial, bool isOk, string arg)
    {
        if (!isOk)
            return null;

        var root = await httpHydra.Get<Root>($"{init.apihost}/apiv2.php?item={(serial == 1 ? "serial" : "movie")}&token={init.token}" + arg, safety: true);

        if (root?.data == null || root.status == "error")
        {
            proxyManager?.Refresh();
            return null;
        }

        return root.data.FirstOrDefault();
    }
    #endregion
}
