using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace VideoDB;

public class VideoDBController : BaseOnlineController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<VideoDBController>();

    public VideoDBController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/videodb")]
    async public Task<ActionResult> Index(string uri, string title, string original_title, string t, int s = -1, int sid = -1, bool rjson = false)
    {
        string href = DecryptQuery(uri);

        if (string.IsNullOrWhiteSpace(href))
            return OnError("href");

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var oninvk = new VideoDBInvoke(host);

    rhubFallback:
        var cache = await InvokeCacheResult<EmbedModel>(ipkey($"videodb:{href}"), 20, textJson: true, onget: async e =>
        {
            EmbedModel embed = null;

            if (rch?.enable == true || init.priorityBrowser == "http")
            {
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "iframe"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "cross-site"),
                    ("referer", "{host}/")
                ));

                if (init.httpversion == 2)
                    httpHydra.RegisterHttp(http2Client);

                await httpHydra.GetSpan(href, newheaders: headers, spanAction: html =>
                {
                    embed = oninvk.Embed(html);
                });
            }
            else
            {
                ReadOnlySpan<char> html = await black_magic(href);
                embed = oninvk.Embed(html);
            }

            if (embed == null)
                return e.Fail("embed", refresh_proxy: true);

            return e.Success(embed);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return ContentTpl(cache,
            () => oninvk.Tpl(cache.Value, accsArgs(string.Empty), uri, title, original_title, t, s, sid, rjson, rch?.enable == true)
        );
    }


    #region Manifest
    [HttpGet]
    [Route("lite/videodb/manifest")]
    [Route("lite/videodb/manifest.m3u8")]
    async public Task<ActionResult> Manifest(string link, bool serial)
    {
        link = DecryptQuery(link);

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        bool play = HttpContext.Request.Path.Value.Contains(".m3u8");

        if (rch != null)
        {
            if (rch.IsNotConnected())
            {
                if (init.rhub_fallback && play)
                    rch.Disabled();
                else
                    return ContentTo(rch.connectionMsg);
            }

            if (!play && rch.IsRequiredConnected())
                return ContentTo(rch.connectionMsg);

            if (rch.IsNotSupport(out string rch_error))
                return ShowError(rch_error);
        }

        var cache = await InvokeCacheResult<string>(ipkey($"videodb:video:{link}"), 20, async e =>
        {
            string location = null;

        reset:
            try
            {
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "same-site"),
                    ("origin", "{host}"),
                    ("referer", "{host}/")
                ));

                if (rch?.enable == true)
                {
                    var res = await rch.Headers(link, null, headers);
                    location = res.currentUrl;
                }
                else if (init.priorityBrowser == "http")
                {
                    location = await Http.GetLocation(link, httpversion: init.httpversion, timeoutSeconds: init.httptimeout, proxy: proxy, headers: headers);
                }
                else
                {
                    using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                    {
                        var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data);
                        if (page == null)
                            return e.Fail("page");

                        browser.SetFailedUrl(link);

                        await page.RouteAsync("**/*", async route =>
                        {
                            try
                            {
                                if (route.Request.Url.Contains("api/chromium/iframe"))
                                {
                                    await route.ContinueAsync();
                                    return;
                                }

                                if (route.Request.Url == link)
                                {
                                    await route.ContinueAsync(new RouteContinueOptions { Headers = headers.ToDictionary() });

                                    var response = await page.WaitForResponseAsync(route.Request.Url);
                                    if (response != null)
                                        response.Headers.TryGetValue("location", out location);

                                    browser.SetPageResult(location);
                                    return;
                                }

                                await route.AbortAsync();
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "CatchId={CatchId}", "id_of0azh8k");
                            }
                        });

                        PlaywrightBase.GotoAsync(page, PlaywrightBase.IframeUrl(link));

                        location = await browser.WaitPageResult();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_az6nc5cm");
            }

            if (string.IsNullOrEmpty(location) || link == location)
            {
                if (init.rhub && init.rhub_fallback)
                {
                    init.rhub = false;
                    goto reset;
                }

                return e.Fail("location", refresh_proxy: true);
            }

            return e.Success(location);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        proxyManager?.Success();

        string hls = HostStreamProxy(cache.Value);

        if (play)
            return RedirectToPlay(hls);

        return ContentTo(VideoTpl.ToJson(
            "play",
            hls,
            "auto",
            vast: init.vast,
            headers: init.streamproxy
                ? null
                : httpHeaders(init.host, init.headers_stream),
            httpContext: HttpContext
        ));
    }
    #endregion

    #region black_magic
    async Task<string> black_magic(string iframe_uri)
    {
        try
        {
            using (var browser = new PlaywrightBrowser(init.priorityBrowser))
            {
                var page = await browser.NewPageAsync(init.plugin, init.headers, proxy: proxy_data, imitationHuman: init.imitationHuman).ConfigureAwait(false);
                if (page == null)
                    return null;

                browser.SetFailedUrl(iframe_uri);

                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        if (route.Request.Url.StartsWith(init.host))
                        {
                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                Body = PlaywrightBase.IframeHtml(iframe_uri)
                            });
                        }
                        else if (route.Request.Url == iframe_uri)
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
                            if (!init.imitationHuman || route.Request.Url.EndsWith(".m3u8") || route.Request.Url.Contains("/cdn-cgi/challenge-platform/"))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                            }
                            else
                            {
                                if (await PlaywrightBase.AbortOrCache(page, route))
                                    return;

                                await route.ContinueAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_o4wf3qjk");
                    }
                });

                PlaywrightBase.GotoAsync(page, init.host);

                return await browser.WaitPageResult().ConfigureAwait(false);
            }
        }
        catch { return null; }
    }
    #endregion
}
