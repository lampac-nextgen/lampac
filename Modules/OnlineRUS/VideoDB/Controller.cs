using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services;
using Shared.Services.HTTP;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace VideoDB;

public class VideoDBController : BaseOnlineController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<VideoDBController>();

    public VideoDBController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/videodb")]
    async public Task<ActionResult> Index(string uri, string title, string original_title, string t, short s = -1, short sid = -1, bool rjson = false)
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
                if (init.httpversion == 2)
                    httpHydra.RegisterHttp(http2Client);

                string cookie = ChromeCookie.Value;

            cookieRetry:
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "iframe"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "cross-site"),
                    ("referer", "{host}/"),
                    ("cookie", cookie)
                ));

                await httpHydra.GetSpan(href, newheaders: headers, spanAction: html =>
                {
                    embed = oninvk.Embed(html);
                });

                if (embed == null && rch?.enable != true && !string.IsNullOrEmpty(ModInit.cdp))
                {
                    string fresh = await ChromeCookie.Refresh(ModInit.cdp, href);
                    if (fresh != null && fresh != cookie)
                    {
                        cookie = fresh;
                        goto cookieRetry;
                    }
                }
            }
            else
            {
                await PlaywrightHttp.GetSpan(init.plugin, href, html =>
                {
                    embed = oninvk.Embed(html);
                }, init.headersList, proxy_data);
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
    [HttpGet, Staticache(manually: true)]
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
                    return Content(rch.connectionMsg, "application/json; charset=utf-8");
            }

            if (!play && rch.IsRequiredConnected())
                return Content(rch.connectionMsg, "application/json; charset=utf-8");

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
                    ("referer", "{host}/"),
                    ("cookie", ChromeCookie.Value)
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
}
