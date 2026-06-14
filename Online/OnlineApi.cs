using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Entrys;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using Shared.Services.Utilities;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using IO = System.IO;

namespace Online;

public class OnlineApiController : BaseController
{
    record EventLinkItem(string code, int index, bool work);

    #region online.js
    [HttpGet, AllowAnonymous]
    [Staticache(20, always: true, setHeadersNoCache: true)]
    [Route("online.js")]
    [Route("online/js/{token}")]
    public ActionResult Online(string token)
    {
        var init = ModInit.conf;

        string file = memoryCache.GetOrCreate("online.js", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            string filejs = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "online.js", saveCache: false);

            #region appReplace
            if (init.appReplace != null)
            {
                foreach (var r in init.appReplace)
                {
                    string val = r.Value;
                    if (val.StartsWith("file:"))
                        val = IO.File.ReadAllText(val.Substring(5));

                    filejs = Regex.Replace(filejs, r.Key, val, RegexOptions.IgnoreCase);
                }
            }
            #endregion

            if (!init.version)
            {
                filejs = Regex.Replace(filejs, "version: \\'[^\\']+\\'", "version: ''")
                    .Replace("manifst.name, \" v\"", "manifst.name, \" \"");
            }

            if (init.description != "Плагин для просмотра онлайн сериалов и фильмов")
                filejs = Regex.Replace(filejs, "description: \\'([^\\']+)?\\'", $"description: '{init.description}'");

            if (init.apn != null)
                filejs = Regex.Replace(filejs, "apn: \\'([^\\']+)?\\'", $"apn: '{init.apn}'");

            return filejs;
        });

        var bulder = StringBuilderPool.Rent();

        try
        {
            bulder = bulder.Append(file);
            bulder = bulder.Replace("{rch_websoket}", FileCache.ReadAllText("plugins/rch_nws.js", "rch_nws.js"));

            if (!init.spider)
            {
                bulder = bulder
                    .Replace("addSourceSearch('Spider', 'spider');", "")
                    .Replace("addSourceSearch('Anime', 'spider/anime');", "");
            }

            if (init.component != "lampac")
            {
                bulder = bulder
                    .Replace("component: 'lampac'", $"component: '{init.component}'")
                    .Replace("'lampac', component", $"'{init.component}', component")
                    .Replace("window.lampac_plugin", $"window.{init.component}_plugin");
            }

            if (init.name != "Lampac")
                bulder = bulder.Replace("name: 'Lampac'", $"name: '{init.name}'");

            if (CoreInit.conf.kit.aesgcmkeyName != null)
                bulder = bulder.Replace("aesgcmkey", CoreInit.conf.kit.aesgcmkeyName);

            if (init.spiderName != "Spider")
            {
                bulder = bulder
                    .Replace("addSourceSearch('Spider'", $"addSourceSearch('{init.spiderName}'")
                    .Replace("addSourceSearch('Anime'", $"addSourceSearch('{init.spiderName} - Anime'");
            }

            bulder = bulder
                .Replace("{invc-rch}", FileCache.ReadAllText("plugins/invc-rch.js", "invc-rch.js"))
                .Replace("{invc-rch_nws}", FileCache.ReadAllText("plugins/invc-rch_nws.js", "invc-rch_nws.js"))
                .Replace("{player-inner}", string.Empty)
                .Replace("{localhost}", host)
                .Replace("{token}", HttpUtility.UrlEncode(token));

            if (EventListener.AppReplace != null)
            {
                foreach (Func<string, EventAppReplace, StringBuilder> handler in EventListener.AppReplace.GetInvocationList())
                    bulder = handler.Invoke("online", new EventAppReplace(bulder, token, null, host, requestInfo, HttpContext.Request));
            }

            return ContentTo(bulder, "application/javascript; charset=utf-8");
        }
        finally
        {
            StringBuilderPool.Return(bulder);
        }
    }
    #endregion


    #region externalids
    /// <summary>
    /// imdb_id, kinopoisk_id
    /// </summary>
    static ConcurrentDictionary<string, string> externalids = JsonConvert.DeserializeObject<ConcurrentDictionary<string, string>>(IO.File.ReadAllText("data/externalids.json"));

    [HttpGet]
    [Route("externalids")]
    async public Task<ActionResult> Externalids(string id, string imdb_id, long kinopoisk_id, int serial)
    {
        string memKey = $"OnlineApi:externalids:{id}:{imdb_id}:{kinopoisk_id}:{serial}";
        if (memoryCache.TryGetValue(memKey, out string jsonResult))
            return Content(jsonResult, "application/json; charset=utf-8");

        #region KP_
        if (id != null && id.StartsWith("KP_"))
        {
            string _kp = id.Substring(0, 3);
            foreach (var eid in externalids)
            {
                if (eid.Value == _kp && !string.IsNullOrEmpty(eid.Key))
                {
                    imdb_id = eid.Key;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(imdb_id))
            {
                return Json(new { imdb_id, kinopoisk_id = _kp });
            }
            else
            {
                string mkey = $"externalids:KP_:{_kp}";
                if (!hybridCache.TryGetValue(mkey, out string _imdbid))
                {
                    var bearer = HeadersModel.Init(
                        ("Authorization", $"Bearer 04941a9a3ca3ac16e2b4327347bbc1"),
                        ("Accept", "application/json")
                    );

                    await Http.GetSpan($"https://apbugall.org/v2/movies/search?kp={_kp}", timeoutSeconds: 5, headers: bearer, spanAction: json =>
                    {
                        _imdbid = Rx.Match(json, "\"id_imdb\":\"(tt[^\"]+)\"");
                    });

                    if (string.IsNullOrEmpty(_imdbid))
                        hybridCache.Set(mkey, string.Empty, DateTime.Now.AddHours(1));
                    else
                        hybridCache.Set(mkey, _imdbid, DateTime.Now.AddHours(8));
                }

                return Json(new { imdb_id = _imdbid, kinopoisk_id = _kp });
            }
        }
        #endregion

        #region getAlloha / getVSDN / getTabus
        async Task<string> getAlloha(string imdb)
        {
            string kpid = null;

            var bearer = HeadersModel.Init(
                ("Authorization", $"Bearer 04941a9a3ca3ac16e2b4327347bbc1"),
                ("Accept", "application/json")
            );

            await Http.GetSpan($"https://apbugall.org/v2/movies/search?imdb={imdb}", timeoutSeconds: 5, headers: bearer, spanAction: json =>
            {
                kpid = Rx.Match(json, "\"ids\":{\"kp\":([0-9]+),");
            });

            if (!string.IsNullOrEmpty(kpid) && kpid != "0" && kpid != "null")
                return kpid;

            return null;
        }

        async Task<string> getTabus(string imdb)
        {
            string kpid = null;

            await Http.GetSpan("https://api.bhcesh.me/franchise/details?token=d39edcf2b6219b6421bffe15dde9f1b3&imdb_id=" + imdb.Remove(0, 2), timeoutSeconds: 5, spanAction: json =>
            {
                kpid = Rx.Match(json, "\"kinopoisk_id\":\"?([0-9]+)\"?");
            });

            if (!string.IsNullOrEmpty(kpid) && kpid != "0" && kpid != "null")
                return kpid;

            return null;
        }

        //async Task<string> getVSDN(string imdb)
        //{
        //    //long? res = Lumex.database.FirstOrDefault(i => i.imdb_id == imdb)?.kinopoisk_id;
        //    //if (res > 0)
        //    //    return res.ToString();

        //    if (string.IsNullOrEmpty(ModInit.siteConf.VideoCDN.token) || string.IsNullOrEmpty(ModInit.siteConf.VideoCDN.iframehost))
        //        return null;

        //    ProxyManager proxyManager = ModInit.siteConf.VideoCDN.useproxy
        //        ? new ProxyManager("videocdn", ModInit.siteConf.VideoCDN)
        //        : null;

        //    string kpid = null;

        //    await Http.GetSpan($"{ModInit.siteConf.VideoCDN.iframehost}/api/short?api_token={ModInit.siteConf.VideoCDN.token}&imdb_id={imdb}", json =>
        //    {
        //        string kp = Rx.Groups(json, "\"kp_id\":\"?([0-9]+)\"?")[1].Value;
        //        if (!string.IsNullOrEmpty(kpid) && kpid != "0" && kpid != "null")
        //            kpid = kp;

        //    }, timeoutSeconds: 10, proxy: proxyManager?.Get());

        //    return kpid;
        //}
        #endregion

        #region get imdb_id
        if (string.IsNullOrWhiteSpace(imdb_id))
        {
            if (kinopoisk_id > 0)
            {
                string kinopoisk_id_str = kinopoisk_id.ToString();
                foreach (var eid in externalids)
                {
                    if (eid.Value == kinopoisk_id_str && !string.IsNullOrEmpty(eid.Key))
                    {
                        imdb_id = eid.Key;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(imdb_id) && long.TryParse(id, out long _testid) && _testid > 0)
            {
                await using (var sqlDb = ExternalidsContext.Factory != null
                    ? ExternalidsContext.Factory.CreateDbContext()
                    : new ExternalidsContext())
                {
                    imdb_id = sqlDb.imdb.Find($"{id}_{serial}")?.value;
                }

                if (string.IsNullOrEmpty(imdb_id))
                {
                    string mkey = $"externalids:locktmdb:{serial}:{id}";
                    if (!memoryCache.TryGetValue(mkey, out _))
                    {
                        memoryCache.Set(mkey, 0, DateTime.Now.AddHours(1));

                        string cat = serial == 1 ? "tv" : "movie";
                        await Http.GetSpan($"http://api.themoviedb.org/3/{cat}/{id}?api_key={CoreInit.conf.cub.api_key}&append_to_response=external_ids", timeoutSeconds: 5, spanAction: json =>
                        {
                            imdb_id = Rx.Match(json, "\"imdb_id\":\"(tt[0-9]+)\"");
                        });

                        if (!string.IsNullOrEmpty(imdb_id))
                        {
                            await using (var sqlDb = ExternalidsContext.Factory != null
                                ? ExternalidsContext.Factory.CreateDbContext()
                                : new ExternalidsContext())
                            {
                                sqlDb.Add(new ExternalidsSqlModel()
                                {
                                    Id = $"{id}_{serial}",
                                    value = imdb_id
                                });

                                await sqlDb.SaveChangesLocks();
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region get kinopoisk_id
        string kpid = null;

        if (!string.IsNullOrWhiteSpace(imdb_id))
        {
            externalids.TryGetValue(imdb_id, out kpid);

            if (string.IsNullOrEmpty(kpid) || kpid == "0")
            {
                await using (var sqlDb = ExternalidsContext.Factory != null
                    ? ExternalidsContext.Factory.CreateDbContext()
                    : new ExternalidsContext())
                {
                    kpid = sqlDb.kinopoisk.Find(imdb_id)?.value;

                    if (string.IsNullOrEmpty(kpid) && kinopoisk_id == 0)
                    {
                        string mkey = $"externalids:lockkpid:{imdb_id}";
                        if (!memoryCache.TryGetValue(mkey, out _))
                        {
                            memoryCache.Set(mkey, 0, DateTime.Now.AddDays(1));

                            switch (ModInit.conf.findkp ?? "all")
                            {
                                case "alloha":
                                    kpid = await getAlloha(imdb_id);
                                    break;
                                //case "vsdn":
                                //    kpid = await getVSDN(imdb_id);
                                //    break;
                                case "tabus":
                                    kpid = await getTabus(imdb_id);
                                    break;
                                default:
                                    {
                                        var tasks = new List<Task<string>> { /*getVSDN(imdb_id),*/ getAlloha(imdb_id), getTabus(imdb_id) };

                                        while (tasks.Count > 0)
                                        {
                                            var completedTask = await Task.WhenAny(tasks);
                                            tasks.Remove(completedTask);

                                            var result = completedTask.Result;
                                            if (result != null)
                                            {
                                                kpid = result;
                                                break;
                                            }
                                        }

                                        break;
                                    }
                            }

                            if (!string.IsNullOrEmpty(kpid) && kpid != "0")
                            {
                                sqlDb.Add(new ExternalidsSqlModel()
                                {
                                    Id = imdb_id,
                                    value = kpid
                                });

                                await sqlDb.SaveChangesLocks();
                            }
                        }
                    }
                }
            }
        }
        #endregion

        kpid = kpid != null ? kpid : kinopoisk_id.ToString();

        #region EventListener
        if (EventListener.Externalids != null)
        {
            foreach (Func<EventExternalids, (string imdb_id, string kinopoisk_id)> handler in EventListener.Externalids.GetInvocationList())
            {
                var result = handler(new EventExternalids(id, imdb_id, kpid, serial));

                if (string.IsNullOrWhiteSpace(imdb_id) && !string.IsNullOrWhiteSpace(result.imdb_id))
                    imdb_id = result.imdb_id;

                if ((string.IsNullOrWhiteSpace(kpid) || kpid == "0") && !string.IsNullOrWhiteSpace(result.kinopoisk_id) && result.kinopoisk_id != "0")
                    kpid = result.kinopoisk_id;
            }
        }
        #endregion

        if (CoreInit.conf.lowMemoryMode)
            return Json(new { imdb_id, kinopoisk_id = kpid });

        jsonResult = $"{{\"imdb_id\":\"{imdb_id}\",\"kinopoisk_id\":\"{kpid}\"}}";
        memoryCache.Set(memKey, jsonResult, DateTime.Now.AddHours(1));

        return Content(jsonResult, "application/json; charset=utf-8");
    }
    #endregion

    #region WithSearch
    [HttpGet]
    [AllowAnonymous]
    [Route("lite/withsearch")]
    public ActionResult WithSearch()
    {
        if (CoreInit.conf.online.with_search == null)
            return Content("[]", "application/json; charset=utf-8");

        return Json(CoreInit.conf.online.with_search);
    }
    #endregion

    #region spider
    [HttpGet]
    [Route("lite/spider")]
    [Route("lite/spider/anime")]
    async public Task<ActionResult> Spider(string title)
    {
        if (!ModInit.conf.spider)
            return Content("{}", "application/json; charset=utf-8");

        var rch = new RchClient(HttpContext, host, new BaseSettings() { rhub = true }, requestInfo);
        if (rch.IsNotConnected() || rch.IsRequiredConnected())
            return Content(rch.connectionMsg, "application/json; charset=utf-8");

        var user = requestInfo.user;
        var piders = new List<(string name, string uri, int index)>();

        bool isanime = HttpContext.Request.Path.Value?.EndsWith("/anime") == true;

        #region send
        void send(BaseSettings init, string plugin = null)
        {
            if (init == null || !init.spider || !init.enable || init.rip)
                return;

            if (init.workinghours != null && !init.workinghours.Contains(DateTime.UtcNow.Hour))
                return;

            if (init.geo_hide != null)
            {
                if (requestInfo.Country != null && init.geo_hide.Contains(requestInfo.Country))
                    return;
            }

            if (init.group_hide)
            {
                if (init.group > 0)
                {
                    if (user == null || init.group > user.group)
                        return;
                }
                else if (CoreInit.conf.accsdb.enable)
                {
                    if (user == null)
                        return;
                }
            }

            string url = null;
            string displayname = init.displayname ?? init.plugin;

            if (string.IsNullOrEmpty(init.overridepasswd))
            {
                url = init.overridehost;
                if (string.IsNullOrEmpty(url) && init.overridehosts != null && init.overridehosts.Length > 0)
                    url = init.overridehosts[Random.Shared.Next(0, init.overridehosts.Length)];
            }

            if (string.IsNullOrEmpty(url))
                url = $"{host}/lite/" + (plugin ?? init.plugin).ToLower();

            piders.Add((init.displayname ?? init.plugin, $"{url}?title={HttpUtility.UrlEncode(title)}&clarification=1&rjson=true&similar=true", init.displayindex));
        }
        #endregion

        #region module
        OnlineModuleEntry.EnsureCache();
        var spiderArgs = new OnlineSpiderModel(title, isanime);

        if (OnlineModuleEntry.Spiders != null && OnlineModuleEntry.Spiders.Count > 0)
        {
            foreach (var entry in OnlineModuleEntry.Spiders)
            {
                try
                {
                    var result = entry.Spider(HttpContext, requestInfo, host, spiderArgs);
                    if (result == null || result.Count == 0)
                        continue;

                    foreach (var item in result)
                        send(item.init, item.plugin);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "CatchId={CatchId}", "id_bd1de14c");
                }
            }
        }

        if (OnlineModuleEntry.SpidersAsync != null && OnlineModuleEntry.SpidersAsync.Count > 0)
        {
            foreach (var entry in OnlineModuleEntry.SpidersAsync)
            {
                try
                {
                    var result = await entry.SpiderAsync(HttpContext, requestInfo, host, spiderArgs);
                    if (result == null || result.Count == 0)
                        continue;

                    foreach (var item in result)
                        send(item.init, item.plugin);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "CatchId={CatchId}", "id_tne6mp1q");
                }
            }
        }
        #endregion

        return Json(piders.OrderByDescending(i => i.index).ToDictionary(k => k.name, v => v.uri));
    }
    #endregion


    #region lifeevents
    [HttpGet]
    [AllowAnonymous]
    [Route("lifeevents")]
    public ActionResult LifeEvents(string memkey, long id, string imdb_id, long kinopoisk_id, int serial)
    {
        if (!memoryCache.TryGetValue(memkey, out List<EventLinkItem> links) || links == null)
        {
            Response.ContentType = "application/json; charset=utf-8";
            Response.BodyWriter.Write("{\"ready\":false,\"tasks\":0,\"online\":[]}"u8);
            return _emptyResult;
        }

        var onlineItems = new List<EventLinkItem>(links.Count);

        foreach (var item in links)
        {
            if (item?.code != null)
                onlineItems.Add(item);
        }

        if (onlineItems.Count == 0)
        {
            Response.ContentType = "application/json; charset=utf-8";
            Response.BodyWriter.Write("{\"ready\":false,\"tasks\":0,\"online\":[]}"u8);
            return _emptyResult;
        }

        onlineItems.Sort(static (a, b) =>
        {
            int byWork = b.work.CompareTo(a.work); // OrderByDescending(i => i.work)
            return byWork != 0 ? byWork : a.index.CompareTo(b.index);
        });

        bool show = false;
        bool ready = onlineItems.Count == links.Count;

        using (var writer = new BufferWriterPool<byte>(BufferWriterPoolType.Tiny))
        {
            using (var json = new Utf8JsonWriter(writer))
            {
                json.WriteStartObject();

                json.WriteBoolean("ready", onlineItems.Count == links.Count);
                json.WriteNumber("tasks", links.Count);

                json.WritePropertyName("online");
                json.WriteStartArray();

                for (int i = 0; i < onlineItems.Count; i++)
                {
                    string code = onlineItems[i].code;

                    if (!show && code.Contains("\"show\":true", StringComparison.Ordinal))
                        show = true;

                    if (code.Contains("{localhost}", StringComparison.Ordinal))
                        code = code.Replace("{localhost}", host, StringComparison.Ordinal);

                    json.WriteRawValue(code, skipInputValidation: false);
                }

                json.WriteEndArray();

                json.WriteEndObject();
            }

            #region ошибка
            if (ready && !show)
            {
                string kind = serial == 1 ? "сериала" : "фильма";

                if (string.IsNullOrEmpty(imdb_id) && kinopoisk_id <= 0)
                {
                    return Json(new
                    {
                        accsdb = true,
                        ready = true,
                        online = Array.Empty<string>(),
                        msg = $"Добавьте \"IMDB ID\" {kind} на https://themoviedb.org/{(serial == 1 ? "tv" : "movie")}/{id}/edit?active_nav_item=external_ids"
                    });
                }
                else
                {
                    return Json(new
                    {
                        accsdb = true,
                        ready = true,
                        online = Array.Empty<string>(),
                        msg = $"Не удалось найти онлайн для {kind}"
                    });
                }
            }
            #endregion

            Response.ContentType = "application/json; charset=utf-8";
            BodyWriter.Write(writer.WrittenSpan);

            return _emptyResult;
        }

    }
    #endregion

    #region events
    static readonly Regex chineseRegex = new Regex("[\u4E00-\u9FFF]", RegexOptions.Compiled); // Диапазон для китайских иероглифов
    static readonly Regex japaneseRegex = new Regex("[\u3040-\u30FF\uFF66-\uFF9F]", RegexOptions.Compiled); // Хирагана, катакана и специальные символы
    static readonly Regex koreanRegex = new Regex("[\uAC00-\uD7AF]", RegexOptions.Compiled); // Диапазон для корейских хангыльских символов

    [HttpGet]
    [Route("lite/events")]
    async public Task<ActionResult> Events(string id, string imdb_id, long kinopoisk_id, long tmdb_id, string title, string original_title, string original_language, int year, string source, string rchtype, int serial = -1, int anime = -1, bool life = false, bool islite = false, string account_email = null, string uid = null, string token = null, string nws_id = null, bool external_ids = false)
    {
        var online = new List<(string name, string url, string plugin, int index)>(50);

        string language = original_language != null && original_language.Contains('|')
            ? original_language.Split('|')[0]
            : original_language ?? string.Empty;

        bool isanime = anime != -1 || language is "ja" or "zh";
        if (isanime == false && original_language != null)
            isanime = original_language.EndsWith("|ja") || original_language.EndsWith("|zh");

        #region fix title
        bool fix_title = false;

        if (title != null && language is "ja" or "ko" or "zh" or "cn")
        {
            if (long.TryParse(id, out long tmdbid) && tmdbid > 0)
            {
                if (chineseRegex.IsMatch(title) || japaneseRegex.IsMatch(title) || koreanRegex.IsMatch(title))
                {
                    string memkey = $"themoviedb:fix_title:{serial}:{tmdbid}";
                    if (!memoryCache.TryGetValue(memkey, out string engName))
                    {
                        var result = await Http.Get<JObject>($"http://api.themoviedb.org/3/{(serial == 1 ? "tv" : "movie")}/{tmdbid}?api_key={CoreInit.conf.cub.api_key}&language=en", timeoutSeconds: 5);
                        if (result != null)
                            engName = serial == 1 ? result.Value<string>("name") : result.Value<string>("title");

                        memoryCache.Set(memkey, engName ?? string.Empty, DateTime.Now.AddDays(1));
                    }

                    if (!string.IsNullOrEmpty(engName))
                    {
                        title = engName;
                        fix_title = true;
                    }
                }
            }
        }
        #endregion

        #region external_ids
        if (external_ids && string.IsNullOrEmpty(imdb_id) && (source is "tmdb" or "cub"))
        {
            if (long.TryParse(id, out long tmdbid) && tmdbid > 0)
            {
                string memkey = $"themoviedb:external_ids:{serial}:{tmdbid}";
                if (!memoryCache.TryGetValue(memkey, out string externalImdb))
                {
                    var result = await Http.Get<JObject>($"http://api.themoviedb.org/3/{(serial == 1 ? "tv" : "movie")}/{tmdbid}/external_ids?api_key={CoreInit.conf.cub.api_key}", timeoutSeconds: 5);
                    if (result != null)
                        externalImdb = result.Value<string>("imdb_id");

                    memoryCache.Set(memkey, externalImdb ?? string.Empty, DateTime.Now.AddDays(1));
                }

                if (!string.IsNullOrEmpty(externalImdb))
                {
                    imdb_id = externalImdb;

                    if (kinopoisk_id == 0)
                    {
                        if (externalids.TryGetValue(imdb_id, out string kpid) && int.TryParse(kpid, out int _kpid) && _kpid > 0)
                            kinopoisk_id = _kpid;
                    }
                }
            }
        }
        #endregion

        var user = requestInfo.user;
        JObject kitconf = loadKitConf();

        #region send
        void send(BaseSettings _init, string plugin = null, string name = null, string arg_title = null, string arg_url = null, string myurl = null)
        {
            var init = loadKit(_init, kitconf);

            if (init.workinghours != null && !init.workinghours.Contains(DateTime.UtcNow.Hour))
                return;

            if (rchtype != null)
            {
                if (init.client_type != null && !init.client_type.Contains(rchtype))
                    return;

                string rch_deny = init.RchAccessNotSupport();
                if (rch_deny != null && rch_deny.Contains(rchtype))
                    return;

                string stream_deny = init.StreamAccessNotSupport();
                if (stream_deny != null && stream_deny.Contains(rchtype))
                    return;

                if (init.rhub && !init.rhub_fallback && !init.corseu && string.IsNullOrWhiteSpace(init.webcorshost))
                {
                    if (init.rhub_geo_disable != null &&
                        requestInfo.Country != null &&
                        init.rhub_geo_disable.Contains(requestInfo.Country))
                    {
                        return;
                    }
                }
            }

            if (init.geo_hide != null &&
                requestInfo.Country != null &&
                init.geo_hide.Contains(requestInfo.Country))
            {
                return;
            }

            if (init.group_hide && !requestInfo.IsLocalRequest)
            {
                if (init.group > 0)
                {
                    if (user == null || init.group > user.group)
                        return;
                }
                else if (CoreInit.conf.accsdb.enable)
                {
                    if (user == null)
                        return;
                }
            }

            string url = string.Empty;

            if (string.IsNullOrEmpty(init.overridepasswd))
            {
                url = init.overridehost;
                if (string.IsNullOrEmpty(url) && init.overridehosts != null && init.overridehosts.Length > 0)
                    url = init.overridehosts[Random.Shared.Next(0, init.overridehosts.Length)];
            }

            bool enable = init.enable && !init.rip;
            if (!enable && string.IsNullOrEmpty(url))
                return;

            string displayname = init.displayname ?? name ?? init.plugin;
            string _p = (plugin ?? init.plugin ?? name).ToLowerAndTrim();

            if (string.IsNullOrEmpty(url))
            {
                url = !string.IsNullOrEmpty(myurl)
                    ? url = myurl + arg_url
                    : url = "{localhost}/lite/" + _p + arg_url;
            }

            if (language is "ru" or "ja" or "ko" or "zh" or "cn")
            {
                if (_p is "filmix" or "filmixtv" or "fxapi" or "kinoukr" or "rezka" or "rhsprem" or "kinopub" or "alloha" or "fancdn" or "kinotochka" or "remux" or "kinogo" or "kinobase" or "getstv" or "leproduction") // || (_p == "kodik" && kinopoisk_id == 0 && string.IsNullOrEmpty(imdb_id))
                    url += (url.Contains("?") ? "&" : "?") + "clarification=1";
            }

            online.Add(($"{displayname}{arg_title}", url, _p, init.displayindex > 0 ? init.displayindex : online.Count));
        }
        #endregion

        #region modules
        OnlineModuleEntry.EnsureCache();
        var moduleArgs = new OnlineEventsModel(id, imdb_id, kinopoisk_id, title, original_title, original_language, year, source, rchtype, serial, isanime, life, islite, account_email, uid, token, nws_id, kitconf);

        if (OnlineModuleEntry.Modules != null && OnlineModuleEntry.Modules.Count > 0)
        {
            foreach (var entry in OnlineModuleEntry.Modules)
            {
                try
                {
                    var result = entry.Invoke(HttpContext, requestInfo, host, moduleArgs);
                    if (result != null && result.Count > 0)
                    {
                        foreach (var r in result)
                            send(r.init, r.plugin, r.name, r.arg_title, r.arg_url, r.myurl);
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "CatchId={CatchId}", "id_a73m6i2y");
                }
            }
        }

        if (OnlineModuleEntry.ModulesAsync != null && OnlineModuleEntry.ModulesAsync.Count > 0)
        {
            foreach (var entry in OnlineModuleEntry.ModulesAsync)
            {
                try
                {
                    var result = await entry.InvokeAsync(HttpContext, requestInfo, host, moduleArgs);
                    if (result != null && result.Count > 0)
                    {
                        foreach (var r in result)
                            send(r.init, r.plugin, r.name, r.arg_title, r.arg_url, r.myurl);
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "CatchId={CatchId}", "id_xnfe4pvc");
                }
            }
        }
        #endregion

        #region EventListener
        if (EventListener.OnlineChannels != null)
        {
            var em = new EventOnline(this, online, moduleArgs, kitconf, HttpContext);
            foreach (Func<EventOnline, ActionResult> handler in EventListener.OnlineChannels.GetInvocationList())
            {
                var eventResult = handler(em);
                if (eventResult != null)
                    return eventResult;
            }
        }
        #endregion

        if (ModInit.conf.checkOnlineSearch && !string.IsNullOrEmpty(id))
        {
            #region checkOnlineSearch
            var hash = Fnv1a.Hash("checkOnlineSearch:");
            Fnv1a.Append(ref hash, id);
            Fnv1a.Append(ref hash, ':');
            Fnv1a.Append(ref hash, serial);
            Fnv1a.Append(ref hash, ':');
            Fnv1a.Append(ref hash, source is "tmdb" or "cub" ? string.Empty : (source ?? string.Empty));
            Fnv1a.Append(ref hash, ':');
            Fnv1a.Append(ref hash, online.Count);
            Fnv1a.Append(ref hash, ':');
            Fnv1a.Append(ref hash, IsKitConf ? requestInfo.user_uid : string.Empty);

            string memkey = Fnv1a.Base64Url(hash);

            if (!memoryCache.TryGetValue(memkey, out List<EventLinkItem> links))
            {
                var tasks = new List<Task>(online.Count);
                links = new List<EventLinkItem>(online.Count);
                for (int i = 0; i < online.Count; i++)
                    links.Add(default);

                memoryCache.Set(memkey, links, DateTime.Now.AddMinutes(5));

                foreach (var o in online.OrderBy(i => i.index))
                {
                    tasks.Add(checkSearch(
                        memkey, kitconf, links, tasks.Count, o.index, o.name, o.url, o.plugin,
                        id, imdb_id, kinopoisk_id, tmdb_id, title, original_title,
                        original_language, source, year, serial, anime, life, rchtype
                    ));
                }

                if (life)
                    return Json(new { life = true, memkey, title = (fix_title ? title : null) });

                await Task.WhenAll(tasks);
            }

            if (life)
                return Json(new { life = true, memkey });

            string result = string.Join(",", links
                .Where(i => i.code != null)
                .OrderByDescending(i => i.work)
                .ThenBy(i => i.index)
                .Select(i => i.code));

            return ContentTo($"[{result.Replace("{localhost}", host)}]");
            #endregion
        }
        else
        {
            Response.ContentType = "application/json; charset=utf-8";

            using (var json = new Utf8JsonWriter(BodyWriter))
            {
                json.WriteStartArray();

                foreach (var item in online.OrderBy(i => i.index))
                {
                    json.WriteStartObject();

                    json.WriteString("name", item.name);

                    string url = item.url.Contains("{localhost}", StringComparison.Ordinal)
                        ? item.url.Replace("{localhost}", host, StringComparison.Ordinal)
                        : item.url;

                    json.WriteString("url", url);
                    json.WriteString("balanser", item.plugin);

                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            return _emptyResult;
        }
    }
    #endregion

    #region checkSearch
    static readonly string[] qualityMarks = ["2160", "1080", "720", "480", "360"];

    async Task checkSearch(string memkey, JObject kitconf, List<EventLinkItem> links, int indexList, int index, string name, string uri, string plugin,
                           string id, string imdb_id, long kinopoisk_id, long tmdb_id, string title, string original_title, string original_language, string source, int year, int serial, int anime, bool life, string rchtype)
    {
        try
        {
            string srq = uri.Replace("{localhost}", $"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}");

            var header = uri.Contains("{localhost}")
                ? HeadersModel.Init(("xhost", host), ("xscheme", HttpContext.Request.Scheme), ("lcrqpasswd", CoreInit.rootPasswd))
                : null;

            bool work = false, rch = false;

            string checkuri = $"{srq}{(srq.Contains("?") ? "&" : "?")}id={HttpUtility.UrlEncode(id)}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&tmdb_id={tmdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&original_language={original_language}&source={source}&year={year}&serial={serial}&anime={anime}&rchtype={rchtype}&checksearch=true";

            await Http.GetSpan(AccsDbInvk.Args(checkuri, HttpContext), timeoutSeconds: 10, headers: header, spanAction: res =>
            {
                rch = res.Contains("\"rch\":true", StringComparison.Ordinal);
                work = rch || res.Contains("data-json=", StringComparison.Ordinal)
                    || res.Contains("\"type\":\"movie\"", StringComparison.Ordinal)
                    || res.Contains("\"type\":\"episode\"", StringComparison.Ordinal)
                    || res.Contains("\"type\":\"season\"", StringComparison.Ordinal);

                string quality = string.Empty;

                string balanser = plugin;
                int bl_slash = plugin.IndexOf('/');
                if (bl_slash >= 0 && bl_slash + 1 < plugin.Length)
                    balanser = plugin[(bl_slash + 1)..];

                #region определение качества
                if (work && life)
                {
                    foreach (string q in qualityMarks)
                    {
                        if (res.Contains("<!--q:", StringComparison.Ordinal))
                        {
                            quality = " - " + Rx.Match(res, "<!--q:([^>]+)-->");
                            break;
                        }
                        else if (res.Contains($"\"{q}p\"", StringComparison.Ordinal)
                            || res.Contains($">{q}p<", StringComparison.Ordinal)
                            || res.Contains($"<!--{q}p-->", StringComparison.Ordinal))
                        {
                            quality = $" - {q}p";
                            break;
                        }
                    }

                    if (quality == "2160" || quality == "2160p")
                        quality = res.Contains("HDR", StringComparison.Ordinal) ? " - 4K HDR" : " - 4K";

                    if (quality == string.Empty)
                    {
                        if (EventListener.OnlineApiQuality != null)
                        {
                            var em = new EventOnlineApiQuality(balanser, kitconf);
                            foreach (Func<EventOnlineApiQuality, string> handler in EventListener.OnlineApiQuality.GetInvocationList())
                            {
                                string eventQuality = handler.Invoke(em);
                                if (eventQuality != null)
                                {
                                    quality = eventQuality;
                                    break;
                                }
                            }
                        }

                        if (balanser == "vokino")
                        {
                            quality = res.Contains("4K HDR", StringComparison.Ordinal)
                                ? " - 4K HDR"
                                : res.Contains("4K ", StringComparison.Ordinal)
                                    ? " - 4K"
                                    : quality;
                        }
                    }
                }
                #endregion

                if (!name.Contains(" - ") && ModInit.conf.showquality && !string.IsNullOrEmpty(quality))
                {
                    int cut = name.IndexOf(" ~ ", StringComparison.Ordinal);
                    if (cut >= 0)
                        name = name[..cut];

                    name += quality;
                }
            });

            links[indexList] = new("{" + $"\"name\":\"{name}\",\"url\":\"{uri}\",\"index\":{index},\"show\":{(work ? "true" : "false")},\"balanser\":\"{plugin}\",\"rch\":{(rch ? "true" : "false")}" + "}", index, work);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_effc21fb");
        }
    }
    #endregion
}
