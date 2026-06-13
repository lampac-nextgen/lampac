using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.IO;
using Microsoft.Playwright;
using Shared.Attributes;
using Shared.Models.CSharpGlobals;
using Shared.Models.SISI.NextHUB;
using Shared.PlaywrightCore;
using Shared.Services.HTTP;
using Shared.Services.Pools;
using System.Net.Http;
using System.Text;
using System.Web;

namespace NextHUB;

public partial class ListController : BaseSisiController<NxtSettings>
{
    public ListController() : base(default) { }

    [HttpGet]
    [Route("nexthub/model")]
    async public Task<ActionResult> Model(string plugin, string href, string current)
    {
        plugin = DecryptQuery(plugin);
        href = DecryptQuery(href);
        current = string.IsNullOrEmpty(current) ? null : DecryptQuery(current);

        var _nxtInit = Root.goInit(plugin);
        if (_nxtInit == null)
            return Json(new { model = default(ModelItem) });

        if (string.IsNullOrWhiteSpace(href))
            return Json(new { model = default(ModelItem) });

        if (await IsRequestBlocked(_nxtInit, rch: _nxtInit.rch_access != null))
            return badInitMsg;

        var models = await new ModelProbeResolver(init.host).Resolve(plugin, href, current);

        return Json(new { model = models?.FirstOrDefault(), models });
    }

    [HttpGet, Staticache]
    [Route("nexthub")]
    async public Task<ActionResult> Index(string plugin, string search, string sort, string cat, string model, int pg = 1)
    {
        plugin = DecryptQuery(plugin);
        sort = DecryptQuery(sort);
        cat = DecryptQuery(cat);
        model = DecryptQuery(model);

        var _nxtInit = Root.goInit(plugin);
        if (_nxtInit == null)
            return OnError($"init {plugin} not found", rcache: false);

        if (!string.IsNullOrEmpty(search) && string.IsNullOrEmpty(_nxtInit.search?.uri))
            return OnError("search disable", rcache: false);

        if (await IsRequestBlocked(_nxtInit, rch: _nxtInit.rch_access != null))
            return badInitMsg;

        string semaphoreKey = $"nexthub:{plugin}:{search}:{sort}:{cat}:{model}:{pg}";
        if (init.menu?.customs != null)
        {
            foreach (var item in init.menu.customs)
            {
                string argvalue = CustomQueryValue(item.arg);
                if (!string.IsNullOrEmpty(argvalue))
                    semaphoreKey += $":{item.arg}:{argvalue}";
            }
        }

    rhubFallback:
        var cache = await InvokeCacheResult(semaphoreKey, init.cache_time, jsonContext.ListPlaylistItem, async e =>
        {
            #region contentParse
            var contentParse = init.list.contentParse ?? init.contentParse;

            if (!string.IsNullOrEmpty(search) && init.search?.contentParse != null)
                contentParse = init.search.contentParse;

            if (!string.IsNullOrEmpty(model) && init.model?.contentParse != null)
                contentParse = init.model.contentParse;
            #endregion

            List<PlaylistItem> playlists = null;

            using (var msm = PoolInvk.msm.GetStream())
            {
                string html = await HttpRequest(plugin, pg, search, sort, cat, model, msm);

                playlists = goPlaylist(requestInfo, host, contentParse, init, html, msm, plugin);
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        bool nomenu = HttpContext.Request.Query.ContainsKey("nomenu");
        var menu = nomenu ? null : new List<MenuItem>(3);
        bool usedRoute = init.menu?.route != null || init.route?.eval != null;

        #region search
        if (!nomenu && string.IsNullOrEmpty(model) && init.search?.uri != null)
        {
            menu.Add(new MenuItem()
            {
                title = "Поиск",
                search_on = "search_on",
                playlist_url = $"{host}/nexthub?plugin={EncryptQuery(plugin)}",
            });
        }
        #endregion

        #region sort
        if (!nomenu && string.IsNullOrEmpty(search) && init.menu?.sort != null)
        {
            var msort = new MenuItem()
            {
                title = $"Сортировка: {init.menu.sort.FirstOrDefault(i => i.Value.Equals(sort, StringComparison.OrdinalIgnoreCase)).Key ?? init.menu.sort.First().Key}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
            };

            string arg = usedRoute && init.menu.bind ? $"&cat={EncryptQuery(cat)}&model={EncryptQuery(model)}" : string.Empty;

            foreach (var s in init.menu.sort)
            {
                msort.submenu.Add(new MenuItem()
                {
                    title = s.Key,
                    playlist_url = $"{host}/nexthub?plugin={EncryptQuery(plugin)}&sort={EncryptQuery(s.Value)}" + arg,
                });
            }

            if (msort.submenu.Count > 0)
                menu.Add(msort);
        }
        #endregion

        #region categories
        if (!nomenu && string.IsNullOrEmpty(search) && string.IsNullOrEmpty(model) && init.menu?.categories != null)
        {
            var categories = init.menu.categories.Where(i => i.Key != "format");

            var mcat = new MenuItem()
            {
                title = $"Категории: {categories.FirstOrDefault(i => i.Value.Equals(cat, StringComparison.OrdinalIgnoreCase)).Key ?? "Выбрать"}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
            };

            string arg = usedRoute && init.menu.bind ? $"&sort={EncryptQuery(sort)}" : string.Empty;

            foreach (var s in categories)
            {
                mcat.submenu.Add(new MenuItem()
                {
                    title = s.Key,
                    playlist_url = $"{host}/nexthub?plugin={EncryptQuery(plugin)}&cat={EncryptQuery(s.Value)}" + arg,
                });
            }

            if (mcat.submenu.Count > 0)
                menu.Add(mcat);
        }
        #endregion

        #region custom categories
        if (!nomenu && string.IsNullOrEmpty(search) && init.menu?.customs != null)
        {
            foreach (var custom in init.menu.customs)
            {
                if (!ShouldShowCustomMenu(custom, model))
                    continue;

                string argvalue = string.Equals(custom.arg, "model", StringComparison.OrdinalIgnoreCase)
                    ? model
                    : CustomQueryValue(custom.arg);

                var mcat = new MenuItem()
                {
                    title = $"{custom.name}: {custom.submenu.FirstOrDefault(i => i.Value.Equals(argvalue, StringComparison.OrdinalIgnoreCase)).Key ?? "Выбрать"}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>()
                };

                foreach (var s in custom.submenu)
                {
                    mcat.submenu.Add(new MenuItem()
                    {
                        title = s.Key,
                        playlist_url = $"{host}/nexthub?plugin={EncryptQuery(plugin)}&{custom.arg}={EncryptQuery(s.Value)}",
                    });
                }

                if (mcat.submenu.Count > 0)
                    menu.Add(mcat);
            }
        }
        #endregion

        #region total_pages
        int total_pages = init.list.total_pages;

        if (search != null && init.search != null)
            total_pages = init.search.total_pages;

        if (model != null && init.model != null)
            total_pages = init.model.total_pages;
        #endregion

        return PlaylistResult(cache,
            menu?.Count == 0 ? null : menu,
            total_pages: total_pages
        );
    }

    static bool ShouldShowCustomMenu(CustomCategories custom, string model)
    {
        if (custom?.submenu == null)
            return false;

        if (string.IsNullOrEmpty(model))
            return true;

        return string.Equals(custom.arg, "model", StringComparison.OrdinalIgnoreCase) &&
               custom.submenu.Values.Any(i => string.Equals(i, model, StringComparison.OrdinalIgnoreCase));
    }

    string CustomQueryValue(string arg)
    {
        if (string.IsNullOrEmpty(arg) || !HttpContext.Request.Query.ContainsKey(arg))
            return null;

        string value = HttpContext.Request.Query[arg];
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DecryptQuery(value) ?? value;
    }

    #region ContentAsync
    async Task<string> ContentAsync(NxtSettings init, string url, IReadOnlyList<HeadersModel> headers, (string ip, string username, string password) proxy, string search, string sort, string cat, string model, int pg)
    {
        try
        {
            var conf = string.IsNullOrEmpty(search) ? init.list : init.search;

            using (var browser = new PlaywrightBrowser(init.priorityBrowser))
            {
                var page = await browser.NewPageAsync(init.plugin, headers?.ToDictionary(), proxy: proxy, keepopen: init.keepopen).ConfigureAwait(false);
                if (page == default)
                    return null;

                if (init.cookies != null)
                    await page.Context.AddCookiesAsync(init.cookies).ConfigureAwait(false);

                string routeEval = conf.routeEval;
                if (!string.IsNullOrEmpty(routeEval) && routeEval.EndsWith(".cs"))
                    routeEval = FileCache.ReadAllText($"{ModInit.modpath}/sites/{routeEval}");

                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        #region routeEval
                        if (routeEval != null)
                        {
                            bool _next = await CSharpEval.ExecuteAsync<bool>(routeEval, new NxtRoute(route, HttpContext.Request.Query, url, search, sort, cat, model, pg), Root.routeOptions);
                            if (!_next)
                                return;
                        }
                        #endregion

                        if (conf.patternAbort != null && Regex.IsMatch(route.Request.Url, conf.patternAbort, RegexOptions.IgnoreCase))
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                            await route.AbortAsync();
                            return;
                        }

                        if (init.abortMedia || init.fullCacheJS)
                        {
                            if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: init.abortMedia, fullCacheJS: init.fullCacheJS))
                                return;
                        }
                        else
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: {route.Request.Method} {route.Request.Url}");
                        }

                        await browser.ClearContinueAsync(route, page);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex, "CatchId={CatchId}", "id_e8da804c"); PlaywrightBase.ConsoleLog(() => ex.Message);
                    }
                });

                string content = null;
                PlaywrightBase.GotoAsync(page, url);

                if (!string.IsNullOrEmpty(conf.waitForSelector))
                {
                    try
                    {
                        await page.WaitForSelectorAsync(conf.waitForSelector, new PageWaitForSelectorOptions
                        {
                            Timeout = conf.waitForSelector_timeout

                        }).ConfigureAwait(false);
                    }
                    catch (System.Exception ex)
                    {
                        Serilog.Log.Error(ex, "{Class} {CatchId}", "ListController", "id_fi6mwf4q");
                    }

                    content = await page.ContentAsync().ConfigureAwait(false);
                }
                else
                {
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions() { Timeout = 20_000 }).ConfigureAwait(false);
                    content = await page.ContentAsync().ConfigureAwait(false);
                }

                return content;
            }
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region HttpRequest
    async Task<string> HttpRequest(string plugin, int pg, string search, string sort, string cat, string model, RecyclableMemoryStream msm)
    {
        string data = !string.IsNullOrEmpty(search) ? (init.search?.data ?? init.list.data) : init.list.data;

        #region encoding
        Encoding encodingRequest = default, encodingResponse = default;

        if (!string.IsNullOrEmpty(search))
        {
            if (init.search?.encodingRequest != null)
                encodingRequest = Encoding.GetEncoding(init.search.encodingRequest);

            if (init.search?.encodingResponse != null)
                encodingResponse = Encoding.GetEncoding(init.search.encodingResponse);
        }

        if (encodingRequest == default && init.list?.encodingRequest != null)
            encodingRequest = Encoding.GetEncoding(init.list.encodingRequest);

        if (encodingResponse == default && init.list?.encodingResponse != null)
            encodingResponse = Encoding.GetEncoding(init.list.encodingResponse);
        #endregion

        #region формируем url
        string url = $"{init.host}/{(pg == 1 && init.list.firstpage != null ? init.list.firstpage : init.list.uri)}";
        if (!string.IsNullOrEmpty(search))
        {
            string uri = pg == 1 && init.search?.firstpage != null ? init.search.firstpage : init.search?.uri;
            string _s = encodingRequest != default ? HttpUtility.UrlEncode(search, encodingRequest) : HttpUtility.UrlEncode(search);
            url = $"{init.host}/{uri}".Replace("{search}", _s);
        }
        else
        {
            if (!string.IsNullOrEmpty(sort))
                url = $"{init.host}/{sort}";
            else if (!string.IsNullOrEmpty(cat))
                url = $"{init.host}/{init.menu.formatcat(cat)}";
            else if (!string.IsNullOrEmpty(model))
            {
                url = $"{init.host}/{model}";
                if (init.model?.uri != null)
                    url = init.model.uri.Replace("{host}", init.host).Replace("{model}", model);
                else if (init.model?.format != null)
                {
                    string eval = $"return $\"{init.model.format}\";";
                    url = CSharpEval.BaseExecute<string>(eval, new NxtMenuRoute(init.host, plugin, url, search, cat, sort, model, HttpContext.Request.Query, pg));
                }
            }
            else if (init.menu?.customs != null)
            {
                foreach (var c in init.menu.customs)
                {
                    string argvalue = CustomQueryValue(c.arg);
                    if (!string.IsNullOrWhiteSpace(argvalue))
                        url = $"{init.host}/{c.format.Replace("{value}", argvalue)}";
                }
            }

            if (init.menu?.route != null)
            {
                string goroute(string name)
                {
                    if (init.menu.route.TryGetValue(name, out string value))
                        return value;

                    if (init.menu.route.TryGetValue("-", out value))
                        return value;

                    return string.Empty;
                }

                string eval = $"return (cat != null && sort != null) ? $\"{goroute("catsort")}\" : (model != null && sort != null) ? $\"{goroute("modelsort")}\" : model != null ? $\"{goroute("model")}\" : cat != null ? $\"{goroute("cat")}\" : sort != null ? $\"{goroute("sort")}\" : \"{url}\";";
                url = CSharpEval.BaseExecute<string>(eval, new NxtMenuRoute(init.host, plugin, url, search, cat, sort, model, HttpContext.Request.Query, pg));
            }
        }

        if (init.route?.eval != null)
            url = CSharpEval.Execute<string>(init.route.eval, new NxtMenuRoute(init.host, plugin, url, search, cat, sort, model, HttpContext.Request.Query, pg));
        #endregion

        var headers = httpHeaders(init);
        string targetHost = init.cors(url.Replace("{page}", pg.ToString()), headers, requestInfo);

        if (!string.IsNullOrEmpty(data))
        {
            #region POST
            if (!string.IsNullOrEmpty(search))
            {
                string _s = encodingRequest != default ? HttpUtility.UrlEncode(search, encodingRequest) : HttpUtility.UrlEncode(search);
                data = data.Replace("{search}", _s);
            }

            data = data.Replace("{page}", pg.ToString());

            if (init.rhub == true)
            {
                await rch.SendHub(targetHost, data, headers, true, msAction: result => { result.CopyTo(msm); });
                msm.Position = 0;
                return null;
            }
            else
            {
                using (var dataContent = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"))
                {
                    await Http.BasePostReaderAsync(async e =>
                    {
                        using (var byteBuf = new BufferPool())
                        {
                            int bytesRead;
                            var memBuf = byteBuf.Memory;

                            while ((bytesRead = await e.stream.ReadAsync(memBuf, e.ct).ConfigureAwait(false)) > 0)
                                msm.Write(memBuf.Span.Slice(0, bytesRead));
                        }

                    }, targetHost, dataContent, headers: headers, proxy: proxy, timeoutSeconds: init.timeout, httpversion: init.httpversion);
                }

                msm.Position = 0;
                return null;
            }
            #endregion
        }
        else
        {
            #region GET
            if (init.rhub == true)
            {
                await rch.SendHub(targetHost, null, headers, true, msAction: result => { result.CopyTo(msm); });
                msm.Position = 0;
                return null;
            }
            else if (init.priorityBrowser == "http")
            {
                if (encodingResponse != default)
                    return await Http.Get(targetHost, encoding: encodingResponse, headers: headers, proxy: proxy, timeoutSeconds: init.timeout, httpversion: init.httpversion);

                await Http.BaseGetReaderAsync(async e =>
                {
                    using (var byteBuf = new BufferPool())
                    {
                        int bytesRead;
                        var memBuf = byteBuf.Memory;

                        while ((bytesRead = await e.stream.ReadAsync(memBuf, e.ct).ConfigureAwait(false)) > 0)
                            msm.Write(memBuf.Span.Slice(0, bytesRead));
                    }

                }, targetHost, headers: headers, proxy: proxy, timeoutSeconds: init.timeout, httpversion: init.httpversion);

                msm.Position = 0;
                return null;
            }
            else if (init.list.viewsource)
            {
                await PlaywrightHttp.GetReaderAsync(init.plugin, targetHost, msm.Write, headers, proxy_data, init.cookies);
                msm.Position = 0;
                return null;
            }
            else
            {
                return await ContentAsync(init, targetHost, headers, proxy_data, search, sort, cat, model, pg);
            }
            #endregion
        }
    }
    #endregion
}
