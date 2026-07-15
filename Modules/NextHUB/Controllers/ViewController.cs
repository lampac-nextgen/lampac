using HtmlAgilityPack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Shared.Attributes;
using Shared.Models.CSharpGlobals;
using Shared.Models.SISI.NextHUB;
using Shared.PlaywrightCore;

namespace NextHUB;

public class ViewController : BaseSisiController<NxtSettings>
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ViewController>();

    IQueryCollection evalQuery = QueryCollection.Empty;
    string evalQueryCacheKey = string.Empty;

    public ViewController() : base(default) { }

    [HttpGet]
    [Staticache(manually: true)]
    [Route("nexthub/vidosik")]
    async public Task<ActionResult> Index(string uri, bool related)
    {
        uri = DecryptQuery(uri);
        if (string.IsNullOrEmpty(uri))
            return OnError("uri", rcache: false);

        int separator = uri.IndexOf("_-:-_", StringComparison.Ordinal);
        if (separator <= 0 || separator + 5 >= uri.Length)
            return OnError("uri", rcache: false);

        string plugin = uri.Substring(0, separator);
        string url = uri.Substring(separator + 5);

        var _nxtInit = Root.goInit(plugin);
        if (_nxtInit == null)
            return OnError("init not found", rcache: false);

        if (await IsRequestBlocked(_nxtInit, rch: _nxtInit.rch_access != null))
            return badInitMsg;

        (evalQuery, evalQueryCacheKey) = Root.getEvalQuery(HttpContext.Request.Query, init);

        if (init.view.initUrlEval != null)
            url = CSharpEval.Execute<string>(init.view.initUrlEval, new NxtUrlRequest(init.host, plugin, url, evalQuery, related));

        if (!Root.isSafeHttpUrl(url))
            return OnError("url", rcache: false);

        SemaphorManager semaphore = null;
        string semaphoreKey = $"nexthub:InvkSemaphore:{url}{evalQueryCacheKey}";

        try
        {
            VideoModel video = null;

            if ((init.view.priorityBrowser ?? init.priorityBrowser) == "http" && init.view.viewsource &&
                (init.view.nodeFile != null || init.view.eval != null || init.view.regexMatch != null) &&
                 init.view.routeEval == null && init.cookies == null && init.view.evalJS == null)
            {
            reset:
                if (rch?.enable != true)
                {
                    semaphore ??= new SemaphorManager(semaphoreKey, TimeSpan.FromSeconds(30));
                    bool _acquired = await semaphore.WaitAsync();
                    if (!_acquired)
                        return OnError();
                }

                video = await goVideoToHttp(plugin, url);
                if (string.IsNullOrEmpty(video?.file))
                {
                    if (IsRhubFallback())
                        goto reset;

                    return OnError("file", rcache: !init.debug);
                }
            }
            else
            {
                if (rch?.enable == true)
                    return OnError("rch not supported", rcache: false);

                semaphore ??= new SemaphorManager(semaphoreKey, TimeSpan.FromSeconds(30));
                bool _acquired = await semaphore.WaitAsync();
                if (!_acquired)
                    return OnError();

                video = await goVideoToBrowser(plugin, url, init);
                if (string.IsNullOrEmpty(video?.file))
                    return OnError("file", rcache: !init.debug);
            }

            var stream_links = new StreamItem()
            {
                qualitys = new Dictionary<string, string>()
                {
                    ["auto"] = video.file
                },
                recomends = video.recomends
            };

            semaphore?.Release();

            if (related)
                return PlaylistResult(stream_links?.recomends, false, null, total_pages: 1);

            return OnResult(stream_links);
        }
        finally
        {
            semaphore?.Release();
        }
    }


    #region goVideoToBrowser
    async Task<VideoModel> goVideoToBrowser(string plugin, string url, NxtSettings init)
    {
        if (string.IsNullOrEmpty(url))
            return default;

        try
        {
            string memKey = $"nexthub:view18:goVideo:{url}{evalQueryCacheKey}";
            if (init.view.bindingToIP && proxyManager != null)
                memKey += $":{proxyManager.CurrentProxyIp}";

            var entryCache = await hybridCache.EntryAsync<VideoModel>(memKey);
            if (entryCache.success)
                return entryCache.value;

            var headers = httpHeaders(init);
            string targetHost = init.cors(url);
            if (!Root.isSafeHttpUrl(targetHost))
                return default;

            var cache = new VideoModel();

            using (var browser = new PlaywrightBrowser(init.view.priorityBrowser ?? init.priorityBrowser))
            {
                var page = await browser.NewPageAsync(init.plugin, headers?.ToDictionary(), proxy_data, keepopen: init.view.keepopen, deferredDispose: init.view.playbtn != null).ConfigureAwait(false);
                if (page == default)
                    return default;

                if (init.cookies != null)
                    await page.Context.AddCookiesAsync(init.cookies).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(init.view.addInitScript))
                    await page.AddInitScriptAsync(init.view.addInitScript).ConfigureAwait(false);

                string routeEval = init.view.routeEval;
                if (!string.IsNullOrEmpty(routeEval) && routeEval.EndsWith(".cs"))
                    routeEval = FileCache.ReadAllText($"{ModInit.modpath}/sites/{routeEval}");

                #region RouteAsync
                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        if (!Root.isSafeHttpUrl(route.Request.Url))
                        {
                            await route.AbortAsync();
                            return;
                        }

                        if (browser.IsCompleted || (init.view.patternAbort != null && Regex.IsMatch(route.Request.Url, init.view.patternAbort, RegexOptions.IgnoreCase)))
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                            await route.AbortAsync();
                            return;
                        }

                        #region routeEval
                        if (routeEval != null)
                        {
                            bool _next = await CSharpEval.ExecuteAsync<bool>(routeEval, new NxtRoute(route, evalQuery, targetHost, null, null, null, null, 0), Root.routeOptions);
                            if (!_next)
                                return;
                        }
                        #endregion

                        #region patternFile
                        if (init.view.patternFile != null && Regex.IsMatch(route.Request.Url, init.view.patternFile, RegexOptions.IgnoreCase))
                        {
                            if (init.view.waitForResponse)
                            {
                                string result = null;
                                await browser.ClearContinueAsync(route, page);
                                var response = await page.WaitForResponseAsync(route.Request.Url);
                                if (response != null)
                                    result = await response.TextAsync().ConfigureAwait(false);

                                PlaywrightBase.ConsoleLog(() => $"\nPlaywright: {result}\n");
                                browser.SetPageResult(result);
                            }
                            else
                            {
                                void setHeaders(Dictionary<string, string> _headers)
                                {
                                    if (_headers != null && _headers.Count > 0)
                                    {
                                        cache.headers = new List<HeadersModel>(_headers.Count);
                                        foreach (var item in _headers)
                                        {
                                            if (item.Key.ToLower() is "host" or "accept-encoding" or "connection" or "range")
                                                continue;

                                            cache.headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                                        }
                                    }
                                }

                                setHeaders(route.Request.Headers);

                                if (init.view.waitLocationFile)
                                {
                                    await browser.ClearContinueAsync(route, page);
                                    string setUri = route.Request.Url;

                                    var response = await page.WaitForResponseAsync(route.Request.Url);
                                    if (response != null && response.Headers.ContainsKey("location"))
                                    {
                                        setHeaders(response.Request.Headers);
                                        setUri = response.Headers["location"];
                                    }

                                    if (setUri.StartsWith("//"))
                                        setUri = $"{(init.host.StartsWith("https") ? "https" : "http")}:{setUri}";

                                    PlaywrightBase.ConsoleLog(() => $"\nPlaywright: SET {setUri}\n{JsonConvert.SerializeObject(cache.headers.ToDictionary(), Formatting.Indented)}\n");
                                    browser.SetPageResult(setUri);
                                }
                                else
                                {
                                    PlaywrightBase.ConsoleLog(() => $"\nPlaywright: SET {route.Request.Url}\n{JsonConvert.SerializeObject(cache.headers.ToDictionary(), Formatting.Indented)}\n");
                                    browser.SetPageResult(route.Request.Url);
                                    await route.AbortAsync();
                                }
                            }

                            return;
                        }
                        #endregion

                        #region patternAbortEnd
                        if (init.view.patternAbortEnd != null && Regex.IsMatch(route.Request.Url, init.view.patternAbortEnd, RegexOptions.IgnoreCase))
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                            await route.AbortAsync();
                            return;
                        }
                        #endregion

                        #region patternWhiteRequest
                        if (init.view.patternWhiteRequest != null && route.Request.Url != targetHost && !Regex.IsMatch(route.Request.Url, init.view.patternWhiteRequest, RegexOptions.IgnoreCase))
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                            await route.AbortAsync();
                            return;
                        }
                        #endregion

                        #region abortMedia
                        if (init.view.abortMedia || init.view.fullCacheJS)
                        {
                            if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: init.view.abortMedia, fullCacheJS: init.view.fullCacheJS))
                                return;
                        }
                        else
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: {route.Request.Method} {route.Request.Url}");
                        }
                        #endregion

                        await browser.ClearContinueAsync(route, page);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_r9ux5f8f");
                    }
                });
                #endregion

                #region GotoAsync
            resetGotoAsync:
                if (!Root.isSafeHttpUrl(targetHost))
                    return default;

                string html = null;
                var responce = await page.GotoAsync(init.view.viewsource ? $"view-source:{targetHost}" : targetHost, new PageGotoOptions()
                {
                    Timeout = 10_000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                }).ConfigureAwait(false);

                if (responce != null)
                    html = await responce.TextAsync().ConfigureAwait(false);
                #endregion

                if (init.view.waitForResponse)
                    html = await browser.WaitPageResult().ConfigureAwait(false);

                #region WaitForSelector
                if (!string.IsNullOrEmpty(init.view.waitForSelector) || !string.IsNullOrEmpty(init.view.playbtn))
                {
                    try
                    {
                        await page.WaitForSelectorAsync(init.view.waitForSelector ?? init.view.playbtn, new PageWaitForSelectorOptions
                        {
                            Timeout = init.view.waitForSelector_timeout

                        }).ConfigureAwait(false);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_mnr5650j");
                    }

                    html = await page.ContentAsync().ConfigureAwait(false);
                }
                #endregion

                #region iframe
                if (init.view.iframe != null && targetHost.Contains(init.host))
                {
                    string iframeUrl = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.iframe), new NxtRegexMatch(html, init.view.iframe));
                    if (!string.IsNullOrEmpty(iframeUrl) && iframeUrl != targetHost)
                    {
                        targetHost = init.view.iframe.format != null ? init.view.iframe.format.Replace("{value}", iframeUrl) : iframeUrl;
                        goto resetGotoAsync;
                    }
                }
                #endregion

                if (!string.IsNullOrEmpty(init.view.playbtn))
                    await page.ClickAsync(init.view.playbtn).ConfigureAwait(false);

                if (init.view.nodeFile != null)
                {
                    #region nodeFile
                    string goFile(string _content)
                    {
                        if (!string.IsNullOrEmpty(_content))
                        {
                            var doc = new HtmlDocument();
                            doc.LoadHtml(_content);
                            var videoNode = doc.DocumentNode.SelectSingleNode(init.view.nodeFile.node);
                            if (videoNode != null)
                                return (!string.IsNullOrEmpty(init.view.nodeFile.attribute) ? videoNode.GetAttributeValue(init.view.nodeFile.attribute, null) : videoNode.InnerText)?.Trim();
                        }

                        return null;
                    }

                    if (init.view.NetworkIdle)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            cache.file = goFile(await page.ContentAsync().ConfigureAwait(false));
                            if (!string.IsNullOrEmpty(cache.file))
                                break;

                            PlaywrightBase.ConsoleLog(() => "ContentAsync: " + (i + 1));
                            await Task.Delay(800).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        cache.file = goFile(html);
                    }

                    PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                    #endregion
                }
                else if (init.view.regexMatch != null)
                {
                    #region regexMatch
                    if (init.view.NetworkIdle)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            cache.file = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.regexMatch), new NxtRegexMatch(html, init.view.regexMatch));
                            if (!string.IsNullOrEmpty(cache.file) && init.view.regexMatch.format != null)
                                cache.file = init.view.regexMatch.format.Replace("{value}", cache.file).Replace("{host}", init.host);

                            if (!string.IsNullOrEmpty(cache.file))
                                break;

                            PlaywrightBase.ConsoleLog(() => "ContentAsync: " + (i + 1));
                            await Task.Delay(800).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        cache.file = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.regexMatch), new NxtRegexMatch(html, init.view.regexMatch));
                        if (!string.IsNullOrEmpty(cache.file) && init.view.regexMatch.format != null)
                            cache.file = init.view.regexMatch.format.Replace("{value}", cache.file).Replace("{host}", init.host);
                    }

                    PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                    #endregion
                }
                else if (string.IsNullOrEmpty(init.view.eval ?? init.view.evalJS))
                {
                    cache.file = await browser.WaitPageResult().ConfigureAwait(false);
                }

                cache.file = cache.file?.Replace("\\", "")?.Replace("&amp;", "&");

                #region eval
                if (!string.IsNullOrEmpty(init.view.eval ?? init.view.evalJS))
                {
                    Task<string> goFile(string _content)
                    {
                        if (!string.IsNullOrEmpty(_content))
                        {
                            string infile = $"{ModInit.modpath}/sites/{init.view.eval ?? init.view.evalJS}";
                            if ((infile.EndsWith(".cs") || infile.EndsWith(".js")) && System.IO.File.Exists(infile))
                            {
                                string evaluate = FileCache.ReadAllText(infile);

                                if (infile.EndsWith(".js"))
                                    return page.EvaluateAsync<string>($"(html, plugin, url, file) => {{ {evaluate} }}", new { _content, plugin, targetHost, cache.file });

                                var nxt = new NxtEvalView(init, evalQuery, _content, plugin, targetHost, cache.file, cache.headers, proxyManager);
                                return CSharpEval.ExecuteAsync<string>(goEval(evaluate), nxt, Root.evalOptionsFull);
                            }
                            else
                            {
                                if (init.view.evalJS != null)
                                    return page.EvaluateAsync<string>($"(html, plugin, url, file) => {{ {init.view.evalJS} }}", new { _content, plugin, targetHost, cache.file });

                                var nxt = new NxtEvalView(init, evalQuery, _content, plugin, targetHost, cache.file, cache.headers, proxyManager);
                                return CSharpEval.ExecuteAsync<string>(goEval(init.view.eval), nxt, Root.evalOptionsFull);
                            }
                        }

                        return null;
                    }

                    if (init.view.NetworkIdle)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            cache.file = await goFile(await page.ContentAsync().ConfigureAwait(false)).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(cache.file))
                                break;

                            PlaywrightBase.ConsoleLog(() => "ContentAsync: " + (i + 1));
                            await Task.Delay(800).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        cache.file = await goFile(html).ConfigureAwait(false);
                    }

                    PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                }
                #endregion

                if (string.IsNullOrEmpty(cache.file))
                {
                    proxyManager?.Refresh();
                    return default;
                }

                if (cache.file.StartsWith("GotoAsync:"))
                {
                    targetHost = cache.file.Replace("GotoAsync:", "").Trim();
                    goto resetGotoAsync;
                }

                if (!Root.isSafeHttpUrl(cache.file))
                    return default;

                #region related
                if (init.view.related && cache.recomends == null)
                {
                    if (init.view.NetworkIdle)
                    {
                        string contetnt = await page.ContentAsync().ConfigureAwait(false);
                        cache.recomends = ListController.goPlaylist(requestInfo, host, init.view.relatedParse ?? init.contentParse, init, contetnt, null, plugin);
                    }
                    else
                    {
                        cache.recomends = ListController.goPlaylist(requestInfo, host, init.view.relatedParse ?? init.contentParse, init, html, null, plugin);
                    }
                }
                #endregion
            }

            proxyManager?.Success();
            hybridCache.Set(memKey, cache, cacheTime(init.view.cache_time));

            return cache;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_7049566f");
            if (init.debug)
                Console.WriteLine(ex);

            return default;
        }
    }
    #endregion

    #region goVideoToHttp
    async Task<VideoModel> goVideoToHttp(string plugin, string url)
    {
        if (string.IsNullOrEmpty(url))
            return default;

        try
        {
            string memKey = $"nexthub:view18:goVideo:{url}{evalQueryCacheKey}";

            if (init.view.bindingToIP)
                memKey = ipkey(memKey);

            var entryCache = await hybridCache.EntryAsync<VideoModel>(memKey);
            if (entryCache.success)
                return entryCache.value;

        resetGotoAsync:
            if (!Root.isSafeHttpUrl(url))
                return default;

            string html = await httpHydra.Get(url);
            if (string.IsNullOrEmpty(html))
                return default;

            #region iframe
            if (init.view.iframe != null && url.Contains(init.host))
            {
                string iframeUrl = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.iframe), new NxtRegexMatch(html, init.view.iframe));
                if (!string.IsNullOrEmpty(iframeUrl) && iframeUrl != url)
                {
                    url = init.view.iframe.format != null ? init.view.iframe.format.Replace("{value}", iframeUrl) : iframeUrl;
                    goto resetGotoAsync;
                }
            }
            #endregion

            var cache = new VideoModel();

            if (init.view.nodeFile != null)
            {
                #region nodeFile
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var videoNode = doc.DocumentNode.SelectSingleNode(init.view.nodeFile.node);
                if (videoNode != null)
                    cache.file = (!string.IsNullOrEmpty(init.view.nodeFile.attribute) ? videoNode.GetAttributeValue(init.view.nodeFile.attribute, null) : videoNode.InnerText)?.Trim();

                PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                #endregion
            }
            else if (init.view.regexMatch != null)
            {
                #region regexMatch
                cache.file = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.regexMatch), new NxtRegexMatch(html, init.view.regexMatch));
                if (!string.IsNullOrEmpty(cache.file) && init.view.regexMatch.format != null)
                    cache.file = init.view.regexMatch.format.Replace("{value}", cache.file).Replace("{host}", init.host);

                PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                #endregion
            }

            cache.file = cache.file?.Replace("\\", "")?.Replace("&amp;", "&");

            #region eval
            if (!string.IsNullOrEmpty(init.view.eval))
            {
                var nxt = new NxtEvalView(init, evalQuery, html, plugin, url, cache.file, cache.headers, proxyManager);
                cache.file = await CSharpEval.ExecuteAsync<string>(goEval(init.view.eval), nxt, Root.evalOptionsFull).ConfigureAwait(false);

                PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
            }
            #endregion

            if (string.IsNullOrEmpty(cache.file))
            {
                proxyManager?.Refresh();
                return default;
            }

            if (cache.file.StartsWith("GotoAsync:"))
            {
                url = cache.file.Replace("GotoAsync:", "").Trim();
                goto resetGotoAsync;
            }

            if (!Root.isSafeHttpUrl(cache.file))
                return default;

            if (init.view.related && cache.recomends == null)
            {
                string memKeyRelated = $"related:{memKey}";
                var cacheRelated = await hybridCache.EntryAsync<List<PlaylistItem>>(memKeyRelated);
                if (cacheRelated.success)
                    cache.recomends = cacheRelated.value;
                else
                {
                    cache.recomends = ListController.goPlaylist(requestInfo, host, init.view.relatedParse ?? init.contentParse, init, html, null, plugin);
                    if (cache.recomends != null && cache.recomends.Count > 0)
                        hybridCache.Set(memKeyRelated, cache.recomends, TimeSpan.FromHours(36));
                }
            }

            proxyManager?.Success();

            hybridCache.Set(memKey, cache, cacheTime(init.view.cache_time));

            return cache;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_5f5d9c49");
            if (init.debug)
                Console.WriteLine(ex);

            return default;
        }
    }
    #endregion


    #region evalCodeToRegexMatch
    static string evalCodeToRegexMatch(RegexMatchSettings rm)
    {
        return @"if (m.matches != null && m.matches.Length > 0)
            {
                foreach (string q in m.matches)
                {
                    string file = Regex.Match(html, m.pattern.Replace(""{value}"", $""{q}""), RegexOptions.IgnoreCase).Groups[m.index].Value;
                    if (!string.IsNullOrEmpty(file))
                        return file;
                }
                return null;
            }

            return Regex.Match(html, m.pattern, RegexOptions.IgnoreCase).Groups[m.index].Value;";
    }
    #endregion

    #region goEval
    static string goEval(string evalcode)
    {
        string infile = $"{ModInit.modpath}/sites/{evalcode}";
        if (infile.EndsWith(".eval") && System.IO.File.Exists(infile))
            evalcode = FileCache.ReadAllText(infile);

        if (evalcode.Contains("{include:"))
        {
            string includePattern = @"{include:(?<file>[^}]+)}";
            var matches = Regex.Matches(evalcode, includePattern);
            foreach (Match match in matches)
            {
                string file = match.Groups["file"].Value.Trim();
                if (System.IO.File.Exists($"{ModInit.modpath}/utils/{file}"))
                {
                    string includeCode = FileCache.ReadAllText($"{ModInit.modpath}/utils/{file}");
                    evalcode = evalcode.Replace(match.Value, includeCode);
                }
            }
        }

        return evalcode;
    }
    #endregion
}

public class VideoModel
{
    public VideoModel() { }

    public VideoModel(string file, List<HeadersModel> headers, List<PlaylistItem> recomends)
    {
        this.file = file;
        this.headers = headers;
        this.recomends = recomends;
    }

    public string file { get; set; }
    public List<HeadersModel> headers { get; set; }
    public List<PlaylistItem> recomends { get; set; }
}
