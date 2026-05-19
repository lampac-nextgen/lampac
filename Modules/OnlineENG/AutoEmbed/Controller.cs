using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoEmbed;

public class AutoEmbedController : BaseENGController
{
    public AutoEmbedController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/autoembed")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, mp4: true, method: "call");
    }

    [HttpGet]
    [Route("lite/autoembed/video")]
    public async Task<ActionResult> Video(long id, int s = -1, int e = -1, bool play = false)
    {
        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
            return OnError();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        string embed = $"{init.host}/embed/movie/{id}";
        if (s > 0)
            embed = $"{init.host}/embed/tv/{id}/{s}/{e}";

        var result = await black_magic(embed);
        if (result.file == null)
            return OnError("m3u8", 502);

        string file = HostStreamProxy(result.file, headers: result.headers);

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


    async Task<(string file, List<HeadersModel> headers)> black_magic(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            string memKey = $"autoembed:black_magic:{uri}";
            if (!hybridCache.TryGetValue(memKey, out (string file, List<HeadersModel> headers) cache))
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
                            if (cache.file != null || await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                return;

                            if ((Regex.IsMatch(route.Request.Url, "(hakunaymatata|kphimplayer)") && route.Request.Url.Contains(".mp4"))
                                || route.Request.Url.Contains("/embed-proxy?url=")
                                || route.Request.Url.Contains(".m3u8"))
                            {
                                cache.headers = HeadersModel.Init(
                                    ("sec-fetch-dest", "empty"),
                                    ("sec-fetch-mode", "cors"),
                                    ("sec-fetch-site", "cross-site"),
                                    ("referer", $"{init.host}/"),
                                    ("origin", init.host)
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

                            if (browser.IsCompleted || Regex.IsMatch(route.Request.Url, "(/ads/|vast.xml|ping.gif|silent.mp4)"))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            await route.ContinueAsync();
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "AutoEmbed", "id_a4f7fygg");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, uri);
                    cache.file = await browser.WaitPageResult(20);
                }

                if (cache.file == null)
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
