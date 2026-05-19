using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VidSrc;

public class VidSrcController : BaseENGController
{
    public VidSrcController() : base(ModInit.conf)
    {
    }

    [HttpGet]
    [Route("lite/vidsrc")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet]
    [Route("lite/vidsrc/video")]
    [Route("lite/vidsrc/video.m3u8")]
    public async Task<ActionResult> Video(long id, string imdb_id, int s = -1, int e = -1, bool play = false)
    {
        if (id == 0)
            return OnError();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        string embed = $"{init.host}/v2/embed/movie/{id}?autoPlay=true&poster=false";
        if (s > 0)
            embed = $"{init.host}/v2/embed/tv/{id}/{s}/{e}?autoPlay=true&poster=false";

        var result = await black_magic(id, embed);
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


    async Task<(string m3u8, List<HeadersModel> headers)> black_magic(long id, string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            string memKey = $"vidsrc:black_magic:{uri}";
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
                            if (browser.IsCompleted || Regex.IsMatch(route.Request.Url.Split("?")[0], "\\.(woff2?|vtt|srt|css|ico)$"))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (await PlaywrightBase.AbortOrCache(page, route, fullCacheJS: true))
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
                        catch (Exception ex)
                        {
                            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_zp5in04r");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, uri);

                    var playBtn = page.Locator("button:has(svg)");

                    await playBtn.ClickAsync(new LocatorClickOptions
                    {
                        Timeout = 15000
                    });

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
