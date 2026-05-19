using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PlayEmbed;

public class PlayEmbedController : BaseENGController
{
    public PlayEmbedController() : base(ModInit.conf)
    {
    }

    [HttpGet]
    [Route("lite/playembed")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet]
    [Route("lite/playembed/video")]
    [Route("lite/playembed/video.m3u8")]
    public async Task<ActionResult> Video(long id, int s = -1, int e = -1, bool play = false)
    {
        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
            return OnError();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        string embed = $"{init.host}/movie/{id}?colour=e1216d&autoplay=true&autonextepisode=false&pausescreen=true";
        if (s > 0)
            embed = $"{init.host}/tv/{id}/{s}/{e}?colour=e1216d&autoplay=true&autonextepisode=false&pausescreen=true";

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
            string memKey = $"playembed:black_magic:{uri}";
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
                            if (browser.IsCompleted || Regex.IsMatch(route.Request.Url, "(/ads/)"))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                return;

                            if (route.Request.Url.Contains(".m3u8") || route.Request.Url.Contains("/playlist/"))
                            {
                                cache.headers = HeadersModel.Init(
                                    ("sec-fetch-dest", "empty"),
                                    ("sec-fetch-mode", "cors"),
                                    ("sec-fetch-site", "cross-site")
                                );

                                foreach (var item in route.Request.Headers)
                                {
                                    if (item.Key.ToLower() is "host" or "accept-encoding" or "connection" or "range")
                                        continue;

                                    if (cache.headers.FirstOrDefault(k => k.name == item.Key) == null)
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
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "PlayEmbed", "id_kh28v1oh");
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
}
