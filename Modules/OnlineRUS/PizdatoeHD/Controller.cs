using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Playwright;
using Shared;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using BrowserCookie = Microsoft.Playwright.Cookie;

namespace PizdatoeHD;

public class PizdatoeHDController : BaseOnlineController<ModuleConf>
{
    PizdaInvoke oninvk;

    public PizdatoeHDController() : base(ModInit.conf)
    {
        requestInitializationAsync = async () =>
        {
            oninvk = new PizdaInvoke
            (
                host,
                "lite/pizdatoehd",
                init,
                streamfile =>
                {
                    if (init.cdn != null && !streamfile.Contains(".vtt"))
                        return HostStreamProxy(Regex.Replace(streamfile, "https?://[^/]+", init.cdn));

                    return HostStreamProxy(streamfile);
                }
            );
        };
    }

    [HttpGet]
    [Route("lite/pizdatoehd")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int clarification, int year, string href, string t, int s = -1, bool rjson = false, bool similar = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(href) && string.IsNullOrWhiteSpace(title))
            return OnError();

        using (var browser = new PlaywrightBrowser(init.priorityBrowser))
        {
            IPage page = null;

            #region search
            if (string.IsNullOrEmpty(href))
            {
                CacheResult<SearchModel> search;

                string _kp = kinopoisk_id.ToString();
                var dbEntry = ModInit.database.Where(e => (imdb_id != null && e.Value.imdb == imdb_id) || e.Value.kp == _kp);
                if (dbEntry.Any())
                {
                    var model = new SearchModel()
                    {
                        similar = new List<SimilarModel>()
                    };

                    foreach (var entry in dbEntry)
                    {
                        model.similar.Add(new SimilarModel()
                        {
                            title = entry.Value.title,
                            year = entry.Value.year,
                            href = entry.Value.href,
                            img = entry.Value.img
                        });
                    }

                    if (model.similar.Count == 1)
                        model.href = model.similar[0].href;

                    search = new CacheResult<SearchModel>()
                    {
                        IsSuccess = true,
                        Value = model
                    };
                }
                else
                {
                    search = await InvokeCacheResult<SearchModel>($"pizdatoehd:search:{title}:{original_title}:{clarification}:{year}", 240, textJson: true, onget: async e =>
                    {
                        try
                        {
                            string search_uri = $"{init.host}/search/?do=search&subaction=search&q={HttpUtility.UrlEncode(clarification == 1 ? title : (original_title ?? title))}";

                            page = await browser.NewPageAsync(init.plugin, init.headers, proxy: proxy_data, imitationHuman: true).ConfigureAwait(false);
                            if (page == null)
                                return e.Fail("page");

                            await AdsBlockRouteAsync(page);

                            var result = await page.GotoAsync(search_uri, new PageGotoOptions()
                            {
                                WaitUntil = WaitUntilState.DOMContentLoaded,
                                Timeout = 10_000
                            });

                            if (result == null)
                                return e.Fail("не удалось загрузить страницу", refresh_proxy: true);

                            string html = await result.TextAsync();
                            if (string.IsNullOrEmpty(html))
                                return e.Fail("не удалось получить содержимое страницы");

                            var content = oninvk.Search(html, title, original_title, year);
                            if (content == null || content.IsError)
                                return e.Fail(string.Empty, refresh_proxy: true);

                            if (content.IsEmpty)
                            {
                                if (rch.enable || content.content != null)
                                    return e.Fail(content.content ?? "content");
                            }

                            return e.Success(content);

                        }
                        catch
                        {
                            return e.Fail("catch");
                        }
                    });
                }

                if (search.ErrorMsg != null)
                    return ShowError(string.IsNullOrEmpty(search.ErrorMsg) ? "поиск не дал результатов" : search.ErrorMsg);

                if (similar || string.IsNullOrEmpty(search.Value?.href))
                {
                    if (search.Value?.IsEmpty == true)
                        return ShowError(search.Value.content ?? "поиск не дал результатов");

                    return ContentTpl(search, () =>
                    {
                        if (search.Value.similar == null)
                            return default;

                        var stpl = new SimilarTpl(search.Value.similar.Count);
                        string enc_title = HttpUtility.UrlEncode(title);
                        string enc_original_title = HttpUtility.UrlEncode(original_title);

                        foreach (var similar in search.Value.similar)
                        {
                            string link = $"{host}/lite/pizdatoehd?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&href={HttpUtility.UrlEncode(similar.href)}";

                            stpl.Append(similar.title, similar.year, string.Empty, link, PosterApi.Size(similar.img));
                        }

                        return stpl;
                    });
                }

                href = search.Value.href;
            }
            #endregion

            #region news
            var cache = await InvokeCacheResult<RootObject>($"pizdatoehd:{href}", 15, async e =>
            {
                try
                {
                    string html = null;

                    if (page != null && init.imitationHuman)
                    {
                        if (await GotoLinkAsync(page, href))
                            html = await page.ContentAsync();
                    }

                    if (html == null || !html.Contains("b-sidecover"))
                    {
                        if (page == null)
                        {
                            page = await browser.NewPageAsync(init.plugin, init.headers, proxy: proxy_data, imitationHuman: true).ConfigureAwait(false);
                            if (page == null)
                                return e.Fail("page");

                            await AdsBlockRouteAsync(page);
                        }

                        var result = await page.GotoAsync($"{init.host}/{href}", new PageGotoOptions()
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 10_000
                        });

                        if (result == null)
                            return e.Fail("не удалось загрузить страницу", refresh_proxy: true);

                        html = await result.TextAsync();
                    }

                    if (string.IsNullOrEmpty(html))
                        return e.Fail("не удалось получить содержимое страницы");

                    var content = oninvk.Embed(href, html);
                    if (content == null)
                        return e.Fail("не удалось распарсить страницу");

                    return e.Success(content);
                }
                catch
                {
                    return e.Fail("catch");
                }
            });
            #endregion

            if (cache.Value?.IsEmpty == true)
                return ShowError(cache.Value.content);

            return ContentTpl(cache,
                () => oninvk.Tpl(cache.Value, accsArgs(string.Empty), title, original_title, href, t, s, rjson)
            );
        }
    }

    #region Movie
    [HttpGet]
    [Route("lite/pizdatoehd/movie")]
    [Route("lite/pizdatoehd/movie.m3u8")]
    async public Task<ActionResult> Movie(string title, string original_title, string href, string voice, int t, int s = -1, int e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        var cache = await InvokeCacheResult<MovieModel>(ipkey($"pizdatoehd:movie:{voice}:{href}:{t}:{s}:{e}"), 20, async result =>
        {
            using (var browser = new PlaywrightBrowser(init.priorityBrowser))
            {
                try
                {
                    var page = await browser.NewPageAsync(init.plugin, init.headers, proxy: proxy_data, imitationHuman: true).ConfigureAwait(false);
                    if (page == null)
                        return result.Fail("page");

                    await AdsBlockRouteAsync(page);

                    if (!string.IsNullOrEmpty(init.cookie))
                    {
                        var cookies = new List<BrowserCookie>();
                        var excookie = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

                        foreach (string line in init.cookie.Split(";"))
                        {
                            if (line.Contains("dle_user_id") || line.Contains("dle_password"))
                            {
                                cookies.Add(new BrowserCookie()
                                {
                                    Domain = "." + Regex.Replace(init.host, "^https?://", ""),
                                    Expires = excookie,
                                    Path = "/",
                                    HttpOnly = true,
                                    Name = line.Split("=")[0].Trim(),
                                    Value = line.Split("=")[1].Trim()
                                });
                            }
                        }

                        if (cookies.Count > 0)
                            await page.Context.AddCookiesAsync(cookies);
                    }

                    if (string.IsNullOrEmpty(voice))
                    {
                        page.Response += async (s, e) =>
                        {
                            if (e.Request.Method == "POST" && e.Request.Url.Contains("/get_cdn_series/"))
                            {
                                string json = await e.TextAsync();
                                browser.SetPageResult(json);
                            }
                        };

                        PlaywrightBase.GotoAsync(page, $"{init.host}/{href}#t:{t}-s:{s}-e:{e}");

                        string json = await browser.WaitPageResult(10);
                        if (string.IsNullOrEmpty(json))
                            return result.Fail("не удалось получить содержимое страницы");

                        var content = oninvk.AjaxMovie(json);
                        if (content == null)
                            return result.Fail("не удалось распарсить страницу");

                        return result.Success(content);
                    }
                    else
                    {
                        var page_result = await page.GotoAsync($"{init.host}/{voice}", new PageGotoOptions()
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 10_000
                        });

                        if (page_result == null)
                            return result.Fail("не удалось загрузить страницу", refresh_proxy: true);

                        string html = await page_result.TextAsync();
                        if (string.IsNullOrEmpty(html))
                            return result.Fail("не удалось получить содержимое страницы");

                        var content = oninvk.Movie(html);
                        if (content == null)
                            return result.Fail("не удалось распарсить страницу");

                        return result.Success(content);
                    }
                }
                catch
                {
                    return result.Fail("catch");
                }
            }
        });

        if (cache.Value?.links == null || cache.Value.links.Count == 0)
            return OnError();

        string result = oninvk.Movie(cache.Value, title, original_title, play, vast: init.vast);
        if (result == null)
            return OnError();

        if (play)
            return RedirectToPlay(result);

        return ContentTo(result);
    }
    #endregion

    #region GotoLinkAsync
    async public Task<bool> GotoLinkAsync(IPage page, string href)
    {
        try
        {
            var container = page.Locator("div.b-content__inline_item-link").Filter(new()
            {
                Has = page.Locator($"a[href*='{href}']")
            });

            if (container == null || await container.CountAsync() != 1)
                return false;

            var link = container.Locator("a");
            if (link == null)
                return false;

            await link.ClickAsync();

            await page.WaitForURLAsync($"**/{href}", new PageWaitForURLOptions()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 8_000
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region AdsBlockRouteAsync
    async public Task AdsBlockRouteAsync(IPage page)
    {
        const string adspattern = "(vk.com|ad2the.net|schulist.link|clarity.ms|frane[a-z]ki.net|cdn.jsdelivr.net/npm/yandex-metrica-watch/tag.js)";

        await page.RouteAsync("**/*", async route =>
        {
            try
            {
                if (Regex.IsMatch(route.Request.Url, adspattern, RegexOptions.IgnoreCase))
                    await route.AbortAsync();
                else
                    await route.ContinueAsync();
            }
            catch { }
        });
    }
    #endregion
}
