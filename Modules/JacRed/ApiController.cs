using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Services.Pools.Json;

namespace JacRed.Controllers
{
    public class ApiController : JacBaseController
    {
        #region Conf
        [HttpGet]
        [AllowAnonymous]
        [Route("api/v1.0/conf")]
        public JsonResult JacConf(string apikey)
        {
            return Json(new
            {
                apikey = ModInit.conf.apikey == null || apikey == ModInit.conf.apikey
            });
        }
        #endregion

        #region Indexers
        [HttpGet]
        [AllowAnonymous]
        [Route("/api/v2.0/indexers/{status}/results")]
        async public Task<ActionResult> Indexers(string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial = -1)
        {
            if (string.IsNullOrEmpty(ModInit.conf.typesearch))
                return Content("typesearch == null");

            if (!string.IsNullOrEmpty(ModInit.conf.apikey) && ModInit.conf.apikey != apikey)
                return Content("apikey");

            #region Запрос с NUM
            bool rqnum = !HttpContext.Request.QueryString.Value.Contains("&is_serial=") && HttpContext.Request.Headers.UserAgent.ToString() == "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

            if (rqnum && query != null)
            {
                var mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+) ([0-9]{4})$");

                if (mNum.Success)
                {
                    if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
                    {
                        var g = mNum.Groups;
                        title = g[1].Value;
                        title_original = g[2].Value;
                        year = int.Parse(g[3].Value);
                    }
                }
                else
                {
                    if (Regex.IsMatch(query, "^([^a-z-A-Z]+) ((19|20)[0-9]{2})$"))
                        return Content(JsonConvertPool.SerializeObject(new { Results = new List<object>(), jacred = ModInit.conf.typesearch == "red" }), "application/json; charset=utf-8");

                    mNum = Regex.Match(query, "^([^a-z-A-Z]+) ([^а-я-А-Я]+)$");

                    if (mNum.Success)
                    {
                        if (Regex.IsMatch(mNum.Groups[2].Value, "[a-zA-Z0-9]{2}"))
                        {
                            var g = mNum.Groups;
                            title = g[1].Value;
                            title_original = g[2].Value;
                        }
                    }
                }
            }
            #endregion

            if (!HttpContext.Request.QueryString.Value.ToLower().Contains("&category[]="))
                category = null;

            IEnumerable<TorrentDetails> torrents = null;

            if (ModInit.conf.typesearch == "red")
            {
                #region red
                string memoryKey = $"{ModInit.conf.typesearch}:{query}:{rqnum}:{title}:{title_original}:{year}:{is_serial}";
                if (!hybridCache.TryGetValue(memoryKey, out List<TorrentDetails> _redCache))
                {
                    var res = RedApi.Indexers(rqnum, apikey, query, title, title_original, year, is_serial, category);

                    _redCache = res.torrents.ToList();

                    if (res.setcache && !red.evercache.enable)
                        hybridCache.Set(memoryKey, _redCache, DateTime.Now.AddMinutes(5), inmemory: false);
                }

                if (ModInit.conf.merge == "jackett")
                {
                    torrents = mergeTorrents
                    (
                        _redCache,
                        await JackettApi.Indexers(host, query, title, title_original, year, is_serial, category)
                    );
                }
                else
                {
                    torrents = _redCache;
                }
                #endregion
            }
            else if (ModInit.conf.typesearch == "webapi")
            {
                #region webapi
                if (ModInit.conf.merge == "jackett")
                {
                    var t1 = WebApi.Indexers(query, title, title_original, year, is_serial, category);
                    var t2 = JackettApi.Indexers(host, query, title, title_original, year, is_serial, category);

                    await Task.WhenAll(t1, t2);

                    torrents = t1.Result == null && t2.Result.Count == 0 ? null : mergeTorrents(t1.Result, t2.Result);
                }
                else
                {
                    torrents = await WebApi.Indexers(query, title, title_original, year, is_serial, category);
                }

                if (torrents == null)
                    return StatusCode(502);
                #endregion
            }
            else if (ModInit.conf.typesearch == "jackett")
            {
                torrents = await JackettApi.Indexers(host, query, title, title_original, year, is_serial, category);
            }

            return Content(JsonConvert.SerializeObject(new
            {
                Results = torrents.OrderByDescending(i => i.createTime).Take(2_000).Select(i => new
                {
                    Tracker = i.trackerName,
                    Details = i.url != null && i.url.StartsWith("http") ? i.url : null,
                    Title = i.title,
                    Size = (long)(0 >= i.size ? getSizeInfo(i.sizeName) : i.size),
                    PublishDate = i.createTime,
                    Category = getCategoryIds(i, out string categoryDesc),
                    CategoryDesc = categoryDesc,
                    Seeders = i.sid,
                    Peers = i.pir,
                    MagnetUri = i.magnet,
                    Link = i.parselink != null ? $"{i.parselink}&apikey={apikey}" : null,
                    Info = ModInit.conf.typesearch != "red" || rqnum ? null : new
                    {
                        i.name,
                        i.originalname,
                        i.relased,
                        i.quality,
                        i.videotype,
                        i.sizeName,
                        i.voices,
                        seasons = i.seasons != null && i.seasons.Count > 0 ? i.seasons : null,
                        i.types
                    },
                    languages = !rqnum && i.languages != null && i.languages.Count > 0 ? i.languages : null,
                    ffprobe = rqnum ? null : i.ffprobe
                }),
                jacred = ModInit.conf.typesearch == "red"

            }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), "application/json; charset=utf-8");
        }
        #endregion

        #region Api
        [HttpGet]
        [AllowAnonymous]
        [Route("/api/v1.0/torrents")]
        async public Task<ActionResult> Api(string apikey, string search, string altname, bool exact, string type, string sort, string tracker, string voice, string videotype, long relased, long quality, long season)
        {
            if (string.IsNullOrEmpty(ModInit.conf.typesearch))
                return Content("typesearch == null");

            if (!string.IsNullOrEmpty(ModInit.conf.apikey) && ModInit.conf.apikey != apikey)
                return Content("apikey");

            #region search kp/imdb
            if (!string.IsNullOrWhiteSpace(search) && Regex.IsMatch(search.Trim(), "^(tt|kp)[0-9]+$"))
            {
                string memkey = $"api/v1.0/torrents:{search}";
                if (!hybridCache.TryGetValue(memkey, out (string original_name, string name) cache))
                {
                    search = search.Trim();
                    string uri = $"&imdb={search}";
                    if (search.StartsWith("kp"))
                        uri = $"&kp={search.Remove(0, 2)}";

                    var root = await Http.Get<JObject>("https://api.alloha.tv/?token=04941a9a3ca3ac16e2b4327347bbc1" + uri, timeoutSeconds: 10);
                    cache.original_name = root?.Value<JObject>("data")?.Value<string>("original_name");
                    cache.name = root?.Value<JObject>("data")?.Value<string>("name");

                    hybridCache.Set(memkey, cache, DateTime.Now.AddDays(1), inmemory: false);
                }

                if (!string.IsNullOrWhiteSpace(cache.name) && !string.IsNullOrWhiteSpace(cache.original_name))
                {
                    search = cache.original_name;
                    altname = cache.name;
                }
                else
                {
                    search = cache.original_name ?? cache.name;
                }
            }
            #endregion

            IEnumerable<TorrentDetails> torrents = null;

            if (ModInit.conf.typesearch == "red")
            {
                #region red
                torrents = RedApi.Api(search, altname, exact, type, sort, tracker, voice, videotype, relased, quality, season);

                if (ModInit.conf.merge == "jackett")
                {
                    torrents = mergeTorrents
                    (
                        torrents,
                        await JackettApi.Api(host, search)
                    );
                }
                #endregion
            }
            else if (ModInit.conf.typesearch == "webapi")
            {
                #region webapi
                if (ModInit.conf.merge == "jackett")
                {
                    var t1 = WebApi.Api(search);
                    var t2 = JackettApi.Api(host, search);

                    await Task.WhenAll(t1, t2);

                    torrents = t1.Result == null && t2.Result.Count == 0 ? null : mergeTorrents(t1.Result, t2.Result);
                }
                else
                {
                    torrents = await WebApi.Api(search);
                }

                if (torrents == null)
                    return StatusCode(502);
                #endregion
            }
            else if (ModInit.conf.typesearch == "jackett")
            {
                torrents = await JackettApi.Api(host, search);
            }

            return Content(JsonConvert.SerializeObject(torrents.Take(2_000).Select(i => new
            {
                tracker = i.trackerName,
                url = i.url != null && i.url.StartsWith("http") ? i.url : null,
                i.title,
                size = 0 > i.size ? getSizeInfo(i.sizeName) : i.size,
                i.sizeName,
                i.createTime,
                i.sid,
                i.pir,
                magnet = i.magnet ?? $"{i.parselink}&apikey={apikey}",
                i.name,
                i.originalname,
                i.relased,
                i.videotype,
                i.quality,
                i.voices,
                i.seasons,
                i.types

            }), new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), "application/json; charset=utf-8");
        }
        #endregion


        #region getSizeInfo
        long getSizeInfo(string sizeName)
        {
            if (string.IsNullOrWhiteSpace(sizeName))
                return 0;

            try
            {
                double size = 0.1;
                var gsize = Regex.Match(sizeName, "([0-9\\.,]+) (Mb|МБ|GB|ГБ|TB|ТБ)", RegexOptions.IgnoreCase).Groups;
                if (!string.IsNullOrWhiteSpace(gsize[2].Value))
                {
                    if (double.TryParse(gsize[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out size) && size != 0)
                    {
                        if (gsize[2].Value.ToLower() is "gb" or "гб")
                            size *= 1024;

                        if (gsize[2].Value.ToLower() is "tb" or "тб")
                            size *= 1048576;

                        return (long)(size * 1048576);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "ApiController", "id_0rhzrhyu");
            }

            return 0;
        }
        #endregion

        #region getCategoryIds
        HashSet<int> getCategoryIds(TorrentDetails t, out string categoryDesc)
        {
            categoryDesc = null;
            HashSet<int> categoryIds = new HashSet<int>();

            if (t.types == null)
                return categoryIds;

            foreach (string type in t.types)
            {
                switch (type)
                {
                    case "movie":
                        categoryDesc = "Movies";
                        categoryIds.Add(2000);
                        break;

                    case "serial":
                        categoryDesc = "TV";
                        categoryIds.Add(5000);
                        break;

                    case "documovie":
                    case "docuserial":
                        categoryDesc = "TV/Documentary";
                        categoryIds.Add(5080);
                        break;

                    case "tvshow":
                        categoryDesc = "TV/Foreign";
                        categoryIds.Add(5020);
                        categoryIds.Add(2010);
                        break;

                    case "anime":
                        categoryDesc = "TV/Anime";
                        categoryIds.Add(5070);
                        break;
                }
            }

            return categoryIds;
        }
        #endregion

        #region mergeTorrents
        static IEnumerable<TorrentDetails> mergeTorrents(IEnumerable<TorrentDetails> red, IEnumerable<TorrentDetails> jac)
        {
            if (red == null && jac == null)
                return new List<TorrentDetails>();

            if (red == null || !red.Any())
                return jac;

            if (jac == null || !jac.Any())
                return red;

            var torrents = new Dictionary<string, TorrentDetails>();

            foreach (var i in red.Concat(jac))
            {
                if (string.IsNullOrEmpty(i.url) || !i.url.StartsWith("http"))
                    continue;

                void add(string url) { torrents.TryAdd(Regex.Replace(url, "^https?://[^/]+/", ""), (TorrentDetails)i.Clone()); }

                if (i.urls != null && i.urls.Count > 0)
                {
                    foreach (string u in i.urls)
                        add(u);
                }
                else
                {
                    add(i.url);
                }
            }

            return torrents.Values;
        }
        #endregion
    }
}
