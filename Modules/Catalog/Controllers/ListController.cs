using Microsoft.AspNetCore.Mvc;
using Shared.Models.Events;
using Shared.PlaywrightCore;
using System.Net.Http;

namespace Catalog;

public class ListController : BaseController
{
    [HttpGet]
    [Route("catalog/list")]
    async public Task<ActionResult> Index(string query, string plugin, string cat, string sort, int page = 1)
    {
        var init = ModInit.goInit(plugin)?.Clone();
        if (init == null || !init.enable)
            return BadRequest("init not found");

        if (!string.IsNullOrEmpty(query) && string.IsNullOrEmpty(init.search?.uri))
            return BadRequest("search disable");

        var rch = new RchClient(HttpContext, host, init, requestInfo);
        if (rch.IsNotConnected())
            rch.Disabled();

        var proxyManager = new ProxyManager(init, rch);
        var proxy = proxyManager.BaseGet();

        string search = query;
        string memKey = $"catalog:{plugin}:{search}:{sort}:{cat}:{page}";

        return await InvkSemaphore(memKey, rch, async () =>
        {
            if (!hybridCache.TryGetValue(memKey, out (List<PlaylistItem> playlists, int total_pages) cache))
            {
                #region contentParse
                var contentParse = init.list?.contentParse ?? init.content;

                if (!string.IsNullOrEmpty(search) && init.search?.contentParse != null)
                    contentParse = init.search.contentParse;
                #endregion

                #region html
                var headers = httpHeaders(init);
                var parse = init.list;

                string url = $"{init.host}/{(page == 1 && init.list?.firstpage != null ? init.list?.firstpage : init.list?.uri)}";
                string data = init.list?.postData;

                if (!string.IsNullOrEmpty(search))
                {
                    string uri = page == 1 && init.search?.firstpage != null ? init.search.firstpage : init.search?.uri;
                    url = $"{init.host}/{uri}".Replace("{search}", HttpUtility.UrlEncode(search));

                    data = init.search?.postData?.Replace("{search}", HttpUtility.UrlEncode(search));
                    parse = init.search;
                }
                else if (!string.IsNullOrEmpty(cat))
                {
                    var menu = init.menu.FirstOrDefault(i => i.categories.Values.Contains(cat));
                    if (menu == null)
                        return BadRequest("menu");

                    string getFormat(string key)
                    {
                        if (menu.format.TryGetValue(key, out string _f))
                            return _f;

                        return string.Empty;
                    }

                    string eval = (cat != null && sort != null) ? getFormat("sort") : getFormat("-");
                    if (!string.IsNullOrEmpty(eval))
                    {
                        if (!eval.Contains("$\"") && eval.Contains("{") && eval.Contains("}"))
                            eval = $"return $\"{eval}\";";

                        url = CSharpEval.BaseExecute<string>(eval, new CatalogGlobalsMenuRoute(init.host, plugin, init.args, url, search, cat, sort, HttpContext.Request.Query, page));
                    }

                    if (!url.StartsWith("http"))
                        url = $"{init.host}/{url}";
                }

                if (init.args != null)
                    url = url.Contains("?") ? $"{url}&{init.args}" : $"{url}?{init.args}";

                if (parse?.initUrl != null)
                    url = CSharpEval.Execute<string>(parse.initUrl, new CatalogGlobalsMenuRoute(init.host, plugin, init.args, url, search, cat, sort, HttpContext.Request.Query, page));

                if (parse?.initHeader != null)
                    headers = CSharpEval.Execute<List<HeadersModel>>(parse.initHeader, new CatalogInitHeader(url, headers));

                reset:
                string html = null;

                if (!string.IsNullOrEmpty(data))
                {
                    string mediaType = data.StartsWith("{") || data.StartsWith("[") ? "application/json" : "application/x-www-form-urlencoded";
                    var httpdata = new StringContent(data, Encoding.UTF8, mediaType);

                    html = rch.enable
                        ? await rch.Post(url.Replace("{page}", page.ToString()), data, headers, useDefaultHeaders: init.useDefaultHeaders)
                        : await Http.Post(url.Replace("{page}", page.ToString()), httpdata, headers: headers, proxy: proxy.proxy, timeoutSeconds: init.timeout, httpversion: init.httpversion, useDefaultHeaders: init.useDefaultHeaders);
                }
                else
                {
                    html = rch.enable
                        ? await rch.Get(url.Replace("{page}", page.ToString()), headers, useDefaultHeaders: init.useDefaultHeaders)
                        : init.priorityBrowser == "playwright" ? await PlaywrightBrowser.Get(init, url.Replace("{page}", page.ToString()), headers, proxy.data, cookies: init.cookies)
                        : await Http.Get(url.Replace("{page}", page.ToString()), headers: headers, proxy: proxy.proxy, timeoutSeconds: init.timeout, httpversion: init.httpversion, useDefaultHeaders: init.useDefaultHeaders);
                }
                #endregion

                bool? jsonPath = contentParse.jsonPath;
                if (jsonPath == null)
                    jsonPath = init.jsonPath;

                #region parse doc/json
                HtmlDocument doc = null;
                JToken json = null;

                if (jsonPath == true)
                {
                    try
                    {
                        json = JToken.Parse(html);
                    }
                    catch
                    {
                        json = null;
                    }
                }
                else
                {
                    doc = new HtmlDocument();

                    if (html != null)
                        doc.LoadHtml(html);
                }
                #endregion

                cache.playlists = jsonPath == true
                    ? goPlaylistJson(cat, json, requestInfo, host, contentParse, init, html, plugin)
                    : goPlaylist(cat, doc, requestInfo, host, contentParse, init, html, plugin);

                if (cache.playlists == null || cache.playlists.Count == 0)
                {
                    if (ModInit.IsRhubFallback(init))
                        goto reset;

                    proxyManager.Refresh();

                    return BadRequest("playlists");
                }

                if (contentParse.total_pages != null)
                {
                    string _p = jsonPath == true
                        ? ModInit.nodeValue(json, contentParse.total_pages, host)?.ToString() ?? ""
                        : ModInit.nodeValue(doc.DocumentNode, contentParse.total_pages, host)?.ToString() ?? "";

                    if (int.TryParse(_p, out int _pages) && _pages > 0)
                        cache.total_pages = _pages;
                }

                proxyManager.Success();

                hybridCache.Set(memKey, cache, cacheTimeBase(init.cache_time, init: init), inmemory: false);
            }

            #region total_pages
            int? total_pages = init.list?.total_pages ?? 0;

            if (search != null && init.search != null)
                total_pages = init.search.total_pages;

            if (total_pages == 0)
                total_pages = cache.total_pages;
            #endregion

            #region next_page
            bool? next_page = null;

            if (search != null)
            {
                if (init.search != null && init.search.count_page > 0 && cache.playlists.Count >= init.search.count_page)
                    next_page = true;
            }
            else
            {
                if (init.list != null && init.list.count_page > 0 && cache.playlists.Count >= init.list.count_page)
                    next_page = true;
            }

            if (next_page == true && total_pages == 0)
                total_pages = null;
            #endregion

            #region results
            var results = new JArray();

            foreach (var pl in cache.playlists)
            {
                var jo = new JObject()
                {
                    ["id"] = pl.id,
                    ["img"] = pl.img
                };

                if (pl.is_serial)
                {
                    jo["first_air_date"] = pl.year;
                    jo["name"] = pl.title;
                    jo["original_name"] = string.IsNullOrWhiteSpace(pl.original_title) ? pl.title : pl.original_title;
                }
                else
                {
                    jo["release_date"] = pl.year;
                    jo["title"] = pl.title;
                    jo["original_title"] = string.IsNullOrWhiteSpace(pl.original_title) ? pl.title : pl.original_title;
                }

                if (pl.args != null)
                {
                    foreach (var a in pl.args)
                        jo[a.Key] = JToken.FromObject(a.Value);
                }

                results.Add(jo);
            }
            #endregion

            if (EventListener.CatalogList != null)
            {
                var em = new EventCatalogList(this, HttpContext, cache.playlists, query, plugin, cat, sort, page, total_pages, next_page);

                foreach (Func<EventCatalogList, ActionResult> handler in EventListener.CatalogList.GetInvocationList())
                {
                    var eventResult = handler(em);
                    if (eventResult != null)
                        return eventResult;
                }
            }

            return ContentTo(JsonConvert.SerializeObject(new
            {
                page,
                results,
                total_pages,
                next_page

            }, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            }));
        });
    }


    #region goPlaylistJson
    static List<PlaylistItem> goPlaylistJson(string cat, JToken json, RequestModel requestInfo, string host, ContentParseSettings parse, CatalogSettings init, string html, string plugin)
    {
        if (parse == null || json == null)
            return null;

        if (init.debug)
            Console.WriteLine(html);

        string eval = parse.eval;
        if (!string.IsNullOrEmpty(eval) && eval.EndsWith(".cs"))
            eval = FileCache.ReadAllText($"{ModInit.modpath}/sites/{eval}");

        if (string.IsNullOrEmpty(parse.nodes))
        {
            if (string.IsNullOrEmpty(eval))
                return null;

            var options = ScriptOptions.Default
                .AddReferences(typeof(Shared.Startup).Assembly)
                .AddImports("Shared")
                .AddImports("Shared.Models")
                .AddImports("Shared.Services")
                .AddImports("Shared.Services.Utilities")
                .AddReferences(typeof(Newtonsoft.Json.JsonConvert).Assembly)
                .AddImports("Newtonsoft.Json")
                .AddImports("Newtonsoft.Json.Linq");

            return CSharpEval.Execute<List<PlaylistItem>>(eval, new CatalogPlaylistJson(init, plugin, host, html, json, new List<PlaylistItem>()), options);
        }

        var nodes = json.SelectTokens(parse.nodes)?.ToList();
        if (nodes == null || nodes.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(nodes.Count);

        foreach (var node in nodes)
        {
            string name = ModInit.nodeValue(node, parse.name, host)?.ToString();
            string original_name = ModInit.nodeValue(node, parse.original_name, host)?.ToString();
            string href = ModInit.nodeValue(node, parse.href, host)?.ToString();
            string img = ModInit.nodeValue(node, parse.image, host)?.ToString();
            string year = ModInit.nodeValue(node, parse.year, host)?.ToString();

            if (init.debug)
                Console.WriteLine($"\n\nname: {name}\noriginal_name: {original_name}\nhref: {href}\nimg: {img}\nyear: {year}\n\n{node.ToString(Formatting.None)}");

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(href))
            {
                #region href
                if (href.StartsWith("../"))
                    href = href.Replace("../", "");
                else if (href.StartsWith("//"))
                    href = Regex.Replace(href, "//[^/]+/", "");
                else if (href.StartsWith("http"))
                    href = Regex.Replace(href, "https?://[^/]+/", "");
                else if (href.StartsWith("/"))
                    href = href.Substring(1);
                #endregion

                #region img
                if (img != null)
                {
                    img = img.Replace("&amp;", "&").Replace("\\", "");

                    if (img.StartsWith("../"))
                        img = $"{init.host}/{img.Replace("../", "")}";
                    else if (img.StartsWith("//"))
                        img = $"https:{img}";
                    else if (img.StartsWith("/"))
                        img = init.host + img;
                    else if (!img.StartsWith("http"))
                        img = $"{init.host}/{img}";
                }
                #endregion

                if (!init.ignore_no_picture && string.IsNullOrEmpty(img))
                    continue;

                string clearText(string text)
                {
                    if (string.IsNullOrEmpty(text))
                        return text;

                    text = text.Replace("&nbsp;", "");
                    text = Regex.Replace(text, "<[^>]+>", "");
                    text = HttpUtility.HtmlDecode(text);
                    return text.Trim();
                }

                #region is_serial
                bool? is_serial = null;

                if (cat != null)
                {
                    if (init.movie_cats != null && init.movie_cats.Contains(cat))
                        is_serial = false;
                    else if (init.serial_cats != null && init.serial_cats.Contains(cat))
                        is_serial = true;
                }

                if (is_serial == null && parse.serial_regex != null)
                    is_serial = Regex.IsMatch(node.ToString(Formatting.None), parse.serial_regex, RegexOptions.IgnoreCase);

                if (is_serial == null && parse.serial_key != null)
                {
                    if (ModInit.nodeValue(node, parse.serial_key, host) != null)
                        is_serial = true;
                }
                #endregion

                var pl = new PlaylistItem()
                {
                    id = href,
                    title = clearText(name),
                    original_title = clearText(original_name),
                    img = PosterApi.Size(host, img),
                    year = clearText(year),
                    is_serial = is_serial == true
                };

                if (parse.args != null)
                {
                    foreach (var arg in parse.args)
                    {
                        if (pl.args == null)
                            pl.args = new JObject();

                        object val = ModInit.nodeValue(node, arg, host);
                        ModInit.setArgsValue(arg, val, pl.args);
                    }
                }

                if (eval != null)
                {
                    var options = ScriptOptions.Default
                        .AddReferences(typeof(Shared.Startup).Assembly)
                        .AddImports("Shared")
                        .AddImports("Shared.Models")
                        .AddImports("Shared.Services")
                        .AddImports("Shared.Services.Utilities")
                        .AddReferences(typeof(Newtonsoft.Json.JsonConvert).Assembly)
                        .AddImports("Newtonsoft.Json")
                        .AddImports("Newtonsoft.Json.Linq");

                    pl = CSharpEval.Execute<PlaylistItem>(eval, new CatalogChangePlaylisJson(init, plugin, host, html, nodes, pl, node), options);
                }

                if (pl != null)
                    playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

    #region goPlaylist
    static List<PlaylistItem> goPlaylist(string cat, HtmlDocument doc, RequestModel requestInfo, string host, ContentParseSettings parse, CatalogSettings init, string html, string plugin)
    {
        if (parse == null || string.IsNullOrEmpty(html))
            return null;

        if (init.debug)
            Console.WriteLine(html);

        string eval = parse.eval;
        if (!string.IsNullOrEmpty(eval) && eval.EndsWith(".cs"))
            eval = FileCache.ReadAllText($"{ModInit.modpath}/sites/{eval}");

        if (string.IsNullOrEmpty(parse.nodes))
        {
            if (string.IsNullOrEmpty(eval))
                return null;

            var options = ScriptOptions.Default
                .AddReferences(typeof(Shared.Startup).Assembly)
                .AddImports("Shared")
                .AddImports("Shared.Models")
                .AddImports("Shared.Services")
                .AddImports("Shared.Services.Utilities")
                .AddReferences(typeof(HtmlAgilityPack.HtmlNode).Assembly)
                .AddImports("HtmlAgilityPack");

            return CSharpEval.Execute<List<PlaylistItem>>(eval, new CatalogPlaylist(init, plugin, host, html, doc, new List<PlaylistItem>()), options);
        }

        var nodes = doc.DocumentNode.SelectNodes(parse.nodes);
        if (nodes == null || nodes.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(nodes.Count);

        foreach (var node in nodes)
        {
            string name = ModInit.nodeValue(node, parse.name, host)?.ToString();
            string original_name = ModInit.nodeValue(node, parse.original_name, host)?.ToString();
            string href = ModInit.nodeValue(node, parse.href, host)?.ToString();
            string img = ModInit.nodeValue(node, parse.image, host)?.ToString();
            string year = ModInit.nodeValue(node, parse.year, host)?.ToString();

            if (init.debug)
                Console.WriteLine($"\n\nname: {name}\noriginal_name: {original_name}\nhref: {href}\nimg: {img}\nyear: {year}\n\n{node.OuterHtml}");

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(href))
            {
                #region href
                if (href.StartsWith("../"))
                    href = href.Replace("../", "");
                else if (href.StartsWith("//"))
                    href = Regex.Replace(href, "//[^/]+/", "");
                else if (href.StartsWith("http"))
                    href = Regex.Replace(href, "https?://[^/]+/", "");
                else if (href.StartsWith("/"))
                    href = href.Substring(1);
                #endregion

                #region img
                if (img != null)
                {
                    img = img.Replace("&amp;", "&").Replace("\\", "");

                    if (img.StartsWith("../"))
                        img = $"{init.host}/{img.Replace("../", "")}";
                    else if (img.StartsWith("//"))
                        img = $"https:{img}";
                    else if (img.StartsWith("/"))
                        img = init.host + img;
                    else if (!img.StartsWith("http"))
                        img = $"{init.host}/{img}";
                }
                #endregion

                if (!init.ignore_no_picture && string.IsNullOrEmpty(img))
                    continue;

                #region is_serial
                bool? is_serial = null;

                if (cat != null)
                {
                    if (init.movie_cats != null && init.movie_cats.Contains(cat))
                        is_serial = false;
                    else if (init.serial_cats != null && init.serial_cats.Contains(cat))
                        is_serial = true;
                }

                if (is_serial == null && parse.serial_regex != null)
                    is_serial = Regex.IsMatch(node.OuterHtml, parse.serial_regex, RegexOptions.IgnoreCase);

                if (is_serial == null && parse.serial_key != null)
                {
                    if (ModInit.nodeValue(node, parse.serial_key, host) != null)
                        is_serial = true;
                }
                #endregion

                var pl = new PlaylistItem()
                {
                    id = href,
                    title = ModInit.clearText(name),
                    original_title = ModInit.clearText(original_name),
                    img = PosterApi.Size(host, img),
                    year = ModInit.clearText(year),
                    is_serial = is_serial == true
                };

                if (parse.args != null)
                {
                    foreach (var arg in parse.args)
                    {
                        if (pl.args == null)
                            pl.args = new JObject();

                        object val = ModInit.nodeValue(node, arg, host);
                        ModInit.setArgsValue(arg, val, pl.args);
                    }
                }

                if (eval != null)
                {
                    var options = ScriptOptions.Default
                        .AddReferences(typeof(Shared.Startup).Assembly)
                        .AddImports("Shared")
                        .AddImports("Shared.Models")
                        .AddImports("Shared.Services")
                        .AddImports("Shared.Services.Utilities")
                        .AddReferences(typeof(HtmlAgilityPack.HtmlNode).Assembly)
                        .AddImports("HtmlAgilityPack");

                    pl = CSharpEval.Execute<PlaylistItem>(eval, new CatalogChangePlaylis(init, plugin, host, html, nodes, pl, node), options);
                }

                if (pl != null)
                    playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion
}
