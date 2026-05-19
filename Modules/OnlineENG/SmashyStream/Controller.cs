using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmashyStream;

public class SmashyStreamController : BaseENGController
{
    public SmashyStreamController() : base(ModInit.conf)
    {
    }

    [HttpGet]
    [Route("lite/smashystream")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet]
    [Route("lite/smashystream/video")]
    [Route("lite/smashystream/video.mp4")]
    public async Task<ActionResult> Video(long id, int s = -1, int e = -1, bool play = false)
    {
        if (id == 0)
            return OnError();

        if (await IsRequestBlocked(rch: false, rch_check: false))
            return badInitMsg;

        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
            return OnError();

        string embed = $"{init.host}/embed/tmdb-movie-{id}";
        if (s > 0)
            embed = $"{init.host}/embed/tmdb-tv-{id}-{s}-{e}";

        var cache = await InvokeCacheResult<(string stream, List<HeadersModel> headers)>(embed, 20, async e =>
        {
            var result = await black_magic(embed);
            if (result.stream == null)
                return e.Fail("stream", refresh_proxy: true);

            return e.Success(result);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string hls = HostStreamProxy(cache.Value.stream, headers: cache.Value.headers);

        if (play)
            return RedirectToPlay(hls);

        return ContentTo(VideoTpl.ToJson(
            "play",
            hls,
            "English",
            vast: init.vast,
            headers: init.streamproxy ? null : cache.Value.headers,
            httpContext: HttpContext
        ));
    }


    async Task<(string stream, List<HeadersModel> headers)> black_magic(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            (string stream, List<HeadersModel> headers) result = default;

            using (var browser = new PlaywrightBrowser(init.priorityBrowser))
            {
                var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data, deferredDispose: true).ConfigureAwait(false);
                if (page == null)
                    return default;

                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        if (await PlaywrightBase.AbortOrCache(page, route, fullCacheJS: true))
                            return;

                        if (route.Request.Url.Contains("/api/proxy"))
                        {
                            result.headers = new List<HeadersModel>();
                            foreach (var item in route.Request.Headers)
                            {
                                if (item.Key.ToLower() is "host" or "accept-encoding" or "connection" or "range")
                                    continue;

                                result.headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                            }

                            PlaywrightBase.ConsoleLog(() => ($"Playwright: SET {route.Request.Url}", result.headers));
                            browser.SetPageResult(route.Request.Url);
                            await route.AbortAsync();
                            return;
                        }

                        await route.ContinueAsync();
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex, "CatchId={CatchId}", "id_qmeb0rj5");
                    }
                });

                PlaywrightBase.GotoAsync(page, uri);

                result.stream = await browser.WaitPageResult();
            }

            return result;
        }
        catch
        {
            return default;
        }
    }
}
