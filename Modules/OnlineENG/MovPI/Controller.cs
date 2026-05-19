using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MovPI;

public class MovPIController : BaseENGController
{
    public MovPIController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/movpi")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet]
    [Route("lite/movpi/video")]
    [Route("lite/movpi/video.m3u8")]
    public async Task<ActionResult> Video(long id, int s = -1, int e = -1, bool play = false)
    {
        if (id == 0)
            return OnError();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        string embed = $"{init.host}/movie/{id}?autoPlay=true&poster=false";
        if (s > 0)
            embed = $"{init.host}/tv/{id}-{s}-{e}?autoPlay=true&poster=false";

        var result = await black_magic(embed);
        if (result.m3u8 == null)
            return OnError("m3u8", 502);

        string hls = HostStreamProxy(result.m3u8, headers: result.headers);

        if (play)
            return RedirectToPlay(hls);

        return ContentTo(VideoTpl.ToJson(
            "play",
            hls,
            "English",
            vast: init.vast,
            headers: init.streamproxy ? null : result.headers,
            httpContext: HttpContext
        ));
    }


    async Task<(string m3u8, List<HeadersModel> headers)> black_magic(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            string memKey = $"movpi:black_magic:{uri}:{proxy_data.ip}";
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
                            if (route.Request.Url.StartsWith("https://www.hydraflix.cc"))
                            {
                                await route.FulfillAsync(new RouteFulfillOptions
                                {
                                    Body = PlaywrightBase.IframeHtml(uri)
                                });
                            }
                            else
                            {
                                if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                    return;

                                if (route.Request.Url.Contains(".m3u8"))
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
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "MovPI", "id_9hg2mb85");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, "https://www.hydraflix.cc");

                    cache.m3u8 = await browser.WaitPageResult();
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
}
