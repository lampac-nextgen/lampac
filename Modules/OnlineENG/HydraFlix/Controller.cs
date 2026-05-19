using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HydraFlix;

public class HydraFlixController : BaseENGController
{
    public HydraFlixController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/hydraflix")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call", extension: "m3u8");
    }

    #region Video
    [HttpGet]
    [Route("lite/hydraflix/video")]
    [Route("lite/hydraflix/video.mpd")]
    [Route("lite/hydraflix/video.m3u8")]
    async public Task<ActionResult> Video(long id, int s = -1, int e = -1, bool play = false)
    {
        if (id == 0)
            return OnError();

        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
            return OnError();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        string embed = $"{init.host}/movie/{id}?autoPlay=true&theme=e1216d";
        if (s > 0)
            embed = $"{init.host}/tv/{id}/{s}/{e}?autoPlay=true&theme=e1216d";

        var result = await black_magic(embed);
        if (result.m3u8 == null)
            return OnError("m3u8", 502);

        string file = HostStreamProxy(result.m3u8, headers: result.headers);

        if (play)
            return RedirectToPlay(file);

        return ContentTo(VideoTpl.ToJson(
            "play",
            file,
            "English",
            vast: init.vast,
            headers: init.streamproxy ? null : result.headers,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region black_magic
    async Task<(string m3u8, List<HeadersModel> headers)> black_magic(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            string memKey = $"Hydraflix:black_magic:{uri}";
            if (!hybridCache.TryGetValue(memKey, out (string m3u8, List<HeadersModel> headers) cache))
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data);
                    if (page == null)
                        return default;

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                return;

                            if (browser.IsCompleted || route.Request.Url.Contains("adsco."))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (Regex.IsMatch(route.Request.Url, "\\.(mpd|m3u|mp4)"))
                            {
                                cache.headers = new List<HeadersModel>();
                                foreach (var item in route.Request.Headers)
                                {
                                    if (item.Key.ToLower() is "host" or "accept-encoding" or "connection" or "range")
                                        continue;

                                    cache.headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                                }

                                PlaywrightBase.ConsoleLog(() => ($"Playwright: SET {route.Request.Url}", cache.headers));
                                browser.SetPageResult(route.Request.Url);
                                await route.AbortAsync();
                                return;
                            }

                            await route.ContinueAsync();
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "HydraFlix", "id_2wp0b2w1");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, uri);
                    cache.m3u8 = await browser.WaitPageResult(20);
                }

                if (cache.m3u8 == null)
                {
                    proxyManager?.Refresh();
                    return default;
                }

                proxyManager?.Success();
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return cache;
        }
        catch
        {
            return default;
        }
    }
    #endregion
}
