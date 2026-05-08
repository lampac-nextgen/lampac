using Microsoft.AspNetCore.Mvc;
using Shared.Models.Events;
using Shared.PlaywrightCore;
using Shared.Services.Pools.Json;
using System.Net.Http;

namespace Catalog;

public class CardController : BaseController
{
    [HttpGet]
    [Route("catalog/card")]
    public async Task<ActionResult> Index(string plugin, string uri, string type)
    {
        var init = ModInit.goInit(plugin)?.Clone();
        if (init == null || !init.enable)
            return BadRequest("init not found");

        var rch = new RchClient(HttpContext, host, init, requestInfo);
        if (rch.IsNotConnected())
            rch.Disabled();

        var proxyManager = new ProxyManager(init, rch);
        var proxy = proxyManager.BaseGet();

        string memKey = $"catalog:card:{plugin}:{uri}";

        return await InvkSemaphore(memKey, rch, async () =>
        {
            if (!hybridCache.TryGetValue(memKey, out JObject jo))
            {
                string url = $"{init.host}/{uri}";
                var headers = httpHeaders(init);

                if (init.args != null)
                    url = url.Contains("?") ? $"{url}&{init.args}" : $"{url}?{init.args}";

                if (init.card_parse.initUrl != null)
                    url = CSharpEval.Execute<string>(init.card_parse.initUrl, new CatalogInitUrlCard(init.host, init.args, uri, HttpContext.Request.Query, type));

                if (init.card_parse.initHeader != null)
                    headers = CSharpEval.Execute<List<HeadersModel>>(init.card_parse.initHeader, new CatalogInitHeader(url, headers));

                reset:

                string html = null;

                if (!string.IsNullOrEmpty(init.card_parse.postData))
                {
                    string mediaType = init.card_parse.postData.StartsWith("{") || init.card_parse.postData.StartsWith("[") ? "application/json" : "application/x-www-form-urlencoded";
                    var httpdata = new StringContent(init.card_parse.postData, Encoding.UTF8, mediaType);

                    html = rch.enable
                        ? await rch.Post(url, init.card_parse.postData, headers, useDefaultHeaders: init.useDefaultHeaders)
                        : await Http.Post(url, httpdata, headers: headers, proxy: proxy.proxy, timeoutSeconds: init.timeout, httpversion: init.httpversion, useDefaultHeaders: init.useDefaultHeaders);
                }
                else
                {
                    html = rch.enable
                        ? await rch.Get(url, headers, useDefaultHeaders: init.useDefaultHeaders)
                        : init.priorityBrowser == "playwright" ? await PlaywrightBrowser.Get(init, url, headers, proxy.data, cookies: init.cookies)
                        : await Http.Get(url, headers: headers, proxy: proxy.proxy, timeoutSeconds: init.timeout, httpversion: init.httpversion, useDefaultHeaders: init.useDefaultHeaders);
                }

                if (html == null)
                {
                    if (ModInit.IsRhubFallback(init))
                        goto reset;

                    proxyManager.Refresh();

                    return BadRequest("html");
                }

                proxyManager.Success();

                var parse = init.card_parse;
                bool? jsonPath = parse.jsonPath;
                if (jsonPath == null)
                    jsonPath = init.jsonPath;

                #region parse doc/json
                HtmlNode node = null;
                JToken json = null;

                if (jsonPath == true)
                {
                    try
                    {
                        json = JToken.Parse(html);

                        if (!string.IsNullOrEmpty(parse.node))
                        {
                            json = json.SelectToken(parse.node);
                            if (json == null)
                                return BadRequest("parse.node");
                        }
                    }
                    catch
                    {
                        json = null;
                        return BadRequest("json");
                    }
                }
                else
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    node = doc.DocumentNode;
                }
                #endregion

                #region name / original_name / year
                string name;
                string original_name;
                string year;

                if (jsonPath == true)
                {
                    name = ModInit.nodeValue(json, parse.name, host)?.ToString();
                    original_name = ModInit.nodeValue(json, parse.original_name, host)?.ToString();
                    year = ModInit.nodeValue(json, parse.year, host)?.ToString();
                }
                else
                {
                    name = ModInit.nodeValue(node, parse.name, host)?.ToString();
                    original_name = ModInit.nodeValue(node, parse.original_name, host)?.ToString();
                    year = ModInit.nodeValue(node, parse.year, host)?.ToString();
                }
                #endregion

                #region img
                string img = jsonPath == true
                    ? ModInit.nodeValue(json, parse.image, host)?.ToString()
                    : ModInit.nodeValue(node, parse.image, host)?.ToString();

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

                jo = new JObject()
                {
                    ["id"] = uri.Trim(),
                    ["img"] = PosterApi.Size(host, img),

                    ["vote_average"] = 0,
                    ["genres"] = new JArray(),
                    ["production_countries"] = new JArray(),
                    ["production_companies"] = new JArray()
                };

                string overview = jsonPath == true
                    ? ModInit.nodeValue(json, parse.description, host)?.ToString()
                    : ModInit.nodeValue(node, parse.description, host)?.ToString();

                if (!string.IsNullOrEmpty(overview))
                    jo["overview"] = overview;

                if (type == "tv")
                {
                    jo["first_air_date"] = year;
                    jo["name"] = name;

                    if (!string.IsNullOrEmpty(original_name))
                        jo["original_name"] = original_name;
                }
                else
                {
                    jo["release_date"] = year;
                    jo["title"] = name;

                    if (!string.IsNullOrEmpty(original_name))
                        jo["original_title"] = original_name;
                }

                #region card_args
                if (init.card_args != null)
                {
                    foreach (var arg in init.card_args)
                    {
                        object val = jsonPath == true
                            ? ModInit.nodeValue(json, arg, host)
                            : ModInit.nodeValue(node, arg, host);

                        ModInit.setArgsValue(arg, val, jo);
                    }
                }
                #endregion

                if (init.tmdb_injects != null && init.tmdb_injects.Length > 0)
                    await Injects(year, jo, init.tmdb_injects);

                if (!jo.ContainsKey("tagline") && !string.IsNullOrEmpty(original_name))
                    jo["tagline"] = original_name;

                hybridCache.Set(memKey, jo, cacheTimeBase(init.cache_time, init: init));
            }

            if (EventListener.CatalogCard != null)
            {
                var em = new EventCatalogCard(this, HttpContext, jo, plugin, uri, type);

                foreach (Func<EventCatalogCard, ActionResult> handler in EventListener.CatalogCard.GetInvocationList())
                {
                    var eventResult = handler(em);
                    if (eventResult != null)
                        return eventResult;
                }
            }

            return ContentTo(JsonConvertPool.SerializeObject(jo));
        });
    }


    #region TMDB Injects
    static readonly string[] defaultInjectskeys =
    [
        "imdb_id",
        "external_ids",
        "backdrop_path",
        "created_by",
        "genres",
        "production_companies",
        "production_countries",
        "content_ratings",
        "episode_run_time",
        "languages",
        "number_of_episodes",
        "number_of_seasons",
        "last_episode_to_air",
        "origin_country",
        "original_language",
        "status",
        "networks",
        "seasons",
        "type",
        "budget",
        "spoken_languages",
        "alternative_titles",
        "keywords",

        // &append_to_response=
        "videos",
        "credits",
        "recommendations",
        "similar",
    ];

    static readonly string[] addEmptykeys =
    [
        "tagline",
        "overview",
        "first_air_date",
        "last_air_date",
        "release_date",
        "runtime"
    ];

    async Task Injects(string year, JObject jo, string[] keys)
    {
        if (!jo.ContainsKey("imdb_id") && !jo.ContainsKey("original_title") && !jo.ContainsKey("original_name"))
            return;

        if (keys.Length == 1 && keys[0] == "default")
            keys = defaultInjectskeys;

        #region Поиск карточки в TMDB
        string imdbId = null;
        if (jo.ContainsKey("imdb_id"))
            imdbId = jo["imdb_id"]?.ToString();

        var header = HeadersModel.Init(("lcrqpasswd", CoreInit.rootPasswd));

        long id = 0;
        string cat = string.Empty;

        if (!string.IsNullOrWhiteSpace(imdbId) && imdbId.StartsWith("tt"))
        {
            var find = await Http.Get<JObject>($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/tmdb/api/3/find/{imdbId}?external_source=imdb_id&api_key={CoreInit.conf.cub.api_key}", timeoutSeconds: 5, headers: header);
            if (find != null)
            {
                foreach (string key in new string[] { "movie_results", "tv_results" })
                {
                    if (find.ContainsKey(key))
                    {
                        var movies = find[key] as JArray;
                        if (movies != null && movies.Count > 0)
                        {
                            id = movies[0].Value<long>("id");
                            cat = key == "movie_results" ? "movie" : "tv";
                            break;
                        }
                    }
                }
            }
        }
        else if (jo.ContainsKey("original_title") || jo.ContainsKey("original_name"))
        {
            string type = jo.ContainsKey("original_title") ? "movie" : "tv";
            string originalTitle = jo.Value<string>(type == "movie" ? "original_title" : "original_name");

            if (!string.IsNullOrEmpty(originalTitle) && int.TryParse(year.Split("-")[0], out int _year) && _year > 0)
            {
                var searchMovie = await Http.Get<JObject>($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/tmdb/api/3/search/{type}?query={HttpUtility.UrlEncode(originalTitle)}&api_key={CoreInit.conf.cub.api_key}", timeoutSeconds: 5, headers: header);
                if (searchMovie != null && searchMovie.ContainsKey("results"))
                {
                    var results = searchMovie["results"] as JArray;
                    if (results != null && results.Count > 0)
                    {
                        long foundId = 0;
                        for (int i = 0; i < results.Count; i++)
                        {
                            var item = results[i] as JObject;
                            if (item == null)
                                continue;

                            string date = item.Value<string>("release_date") ?? item.Value<string>("first_air_date");
                            if (string.IsNullOrEmpty(date))
                                continue;

                            // date is usually in format YYYY-MM-DD, take first 4 chars
                            string yearStr = date.Length >= 4 ? date.Substring(0, 4) : date;
                            if (int.TryParse(yearStr, out int itemYear) && itemYear == _year)
                            {
                                string _s1 = StringConvert.SearchName(originalTitle);
                                string _s2 = StringConvert.SearchName(item.Value<string>(type == "movie" ? "original_title" : "original_name"));

                                if (!string.IsNullOrEmpty(_s1) && !string.IsNullOrEmpty(_s2) && _s1 == _s2)
                                {
                                    foundId = item.Value<long>("id");
                                    break;
                                }
                            }
                        }

                        if (foundId != 0)
                        {
                            id = foundId;
                            cat = type;
                        }
                    }
                }
            }
        }
        #endregion

        if (id == 0)
            return;

        string append = "content_ratings,release_dates,external_ids,keywords,alternative_titles,videos,credits,recommendations,similar";
        var result = await Http.Get<JObject>($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/tmdb/api/3/{cat}/{id}?api_key={CoreInit.conf.cub.api_key}&append_to_response={append}&language=ru", timeoutSeconds: 5, headers: header);
        if (result == null)
            return;

        foreach (string key in keys)
        {
            if (key is "videos" or "recommendations" or "similar")
            {
                if (result.ContainsKey(key) && result[key] is JObject _jo && _jo.ContainsKey("results"))
                    jo[key] = _jo["results"];
            }
            else if (result.ContainsKey(key))
            {
                jo[key] = result[key];
            }
        }

        if (result.ContainsKey("id"))
            jo["tmdb_id"] = result["id"];

        if (!jo.ContainsKey("imdb_id") && result.ContainsKey("external_ids") && result["external_ids"] is JObject extIds && extIds.ContainsKey("imdb_id"))
            jo["imdb_id"] = extIds["imdb_id"];

        foreach (string key in addEmptykeys)
        {
            if (!jo.ContainsKey(key) && result.ContainsKey(key))
            {
                var tok = result[key];
                if (tok == null)
                    continue;

                if (tok.Type == JTokenType.String)
                {
                    var str = tok.Value<string>();
                    if (!string.IsNullOrWhiteSpace(str))
                        jo[key] = str;
                }
                else
                {
                    jo[key] = tok;
                }
            }
        }
    }
    #endregion
}
