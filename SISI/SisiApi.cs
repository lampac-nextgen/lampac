using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Entrys;
using Shared.PlaywrightCore;
using System.Text;
using System.Web;
using IO = System.IO;

namespace SISI
{
    public class SisiApiController : BaseController
    {
        #region sisi.js
        [HttpGet]
        [AllowAnonymous]
        [Route("sisi.js")]
        [Route("sisi/js/{token}")]
        public ContentResult Sisi(string token)
        {
            SetHeadersNoCache();

            var init = ModInit.conf;
            var apr = init.appReplace;

            string memKey = $"sisi.js:{apr?.Count ?? 0}:{init.component}:{init.iconame}:{host}:{init.push_all}:{init.forced_checkRchtype}";
            if (!memoryCache.TryGetValue(memKey, out (string file, string filecleaer) cache))
            {
                cache.file = FileCache.ReadAllText($"{ModInit.modpath}/plugins/sisi.js", "sisi.js", false)
                    .Replace("{rch_websoket}", FileCache.ReadAllText("plugins/rch_nws.js", "rch_nws.js", false));

                #region appReplace
                if (apr != null)
                {
                    foreach (var r in apr)
                    {
                        string val = r.Value;
                        if (val.StartsWith("file:"))
                            val = IO.File.ReadAllText(val.Substring(5));

                        cache.file = Regex.Replace(cache.file, r.Key, val, RegexOptions.IgnoreCase);
                    }
                }
                #endregion

                var bulder = new StringBuilder(cache.file);

                if (!init.spider)
                    bulder = bulder.Replace("Lampa.Search.addSource(Search);", "");

                if (init.component != "sisi")
                {
                    bulder = bulder.Replace("use_api: 'lampac'", $"use_api: '{init.component}'");
                    bulder = bulder.Replace("'plugin_sisi_'", $"'plugin_{init.component}_'");
                }

                if (CoreInit.conf.kit.aesgcmkeyName != null)
                    bulder = bulder.Replace("aesgcmkey", CoreInit.conf.kit.aesgcmkeyName);

                if (!string.IsNullOrEmpty(init.vipcontent))
                    bulder = bulder.Replace("var content = [^\n\r]+", init.vipcontent);

                if (!string.IsNullOrEmpty(init.iconame))
                {
                    bulder = bulder.Replace("Defined.use_api == 'pwa'", "true")
                                   .Replace("'<div>p</div>'", $"'<div>{init.iconame}</div>'");
                }

                bulder = bulder
                    .Replace("{invc-rch}", FileCache.ReadAllText("plugins/invc-rch.js", "invc-rch.js", false))
                    .Replace("{invc-rch_nws}", FileCache.ReadAllText("plugins/invc-rch_nws.js", "invc-rch_nws.js", false))
                    .Replace("{push_all}", init.push_all.ToString().ToLower())
                    .Replace("{localhost}", host)
                    .Replace("{historySave}", ModInit.conf.history.enable.ToString().ToLower());

                if (init.forced_checkRchtype)
                    bulder = bulder.Replace("window.rchtype", "Defined.rchtype");

                cache.file = bulder.ToString();
                cache.filecleaer = cache.file.Replace("{token}", string.Empty);

                memoryCache.Set(memKey, cache, DateTime.Now.AddMinutes(10));
            }

            if (EventListener.AppReplace != null)
            {
                string source = cache.file;

                foreach (Func<string, EventAppReplace, string> handler in EventListener.AppReplace.GetInvocationList())
                    source = handler.Invoke("sisi", new EventAppReplace(source, token, null, host, requestInfo, HttpContext.Request));

                return Content(source.Replace("{token}", HttpUtility.UrlEncode(token)), "application/javascript; charset=utf-8");
            }

            return Content(token != null ? cache.file.Replace("{token}", HttpUtility.UrlEncode(token)) : cache.filecleaer, "application/javascript; charset=utf-8");
        }
        #endregion

        #region startpage.js
        [HttpGet]
        [AllowAnonymous]
        [Route("startpage.js")]
        public ActionResult StartPage()
        {
            SetHeadersNoCache();

            string startpage = FileCache.ReadAllText($"{ModInit.modpath}/plugins/startpage.js", "startpage.js")
                .Replace("{localhost}", host);

            return Content(startpage, "application/javascript; charset=utf-8");
        }
        #endregion


        [HttpGet]
        [Route("sisi")]
        async public Task<JsonResult> Index(string rchtype, string account_email, string uid, string token, bool spder)
        {
            var conf = ModInit.siteConf;
            var appConf = ModInit.conf;
            JObject kitconf = loadKitConf();

            bool lgbt = appConf.lgbt;
            if (kitconf != null && kitconf.Value<bool?>("lgbt") == false)
                lgbt = false;

            var channels = new List<ChannelItem>(50)
            {
                new ChannelItem("Закладки", $"{host}/sisi/bookmarks", 0)
            };

            if (ModInit.conf.history.enable)
                channels.Add(new ChannelItem("История", $"{host}/sisi/historys", 1));

            #region send
            void send(string name, BaseSettings _init, string plugin = null, int displayindex = -1, BaseSettings myinit = null)
            {
                var init = myinit != null ? _init : loadKit(_init, kitconf);
                bool enable = init.enable && !init.rip;
                if (!enable)
                    return;

                if (spder == true && init.spider != true)
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

                if (init.group > 0 && init.group_hide)
                {
                    var user = requestInfo.user;
                    if (user == null || init.group > user.group)
                        return;
                }

                string url = string.Empty;

                if (string.IsNullOrEmpty(init.overridepasswd))
                {
                    url = init.overridehost;
                    if (string.IsNullOrEmpty(url) && init.overridehosts != null && init.overridehosts.Length > 0)
                        url = init.overridehosts[Random.Shared.Next(0, init.overridehosts.Length)];
                }

                string displayname = init.displayname ?? name;

                if (string.IsNullOrEmpty(url))
                    url = $"{host}/{plugin ?? name.ToLower()}";

                if (displayindex == -1)
                {
                    displayindex = init.displayindex;
                    if (displayindex == 0)
                        displayindex = 20 + channels.Count;
                }

                channels.Add(new ChannelItem(init.displayname ?? name, url, displayindex));
            }
            #endregion

            #region modules
            SisiModuleEntry.EnsureCache();
            var args = new SisiEventsModel(rchtype, account_email, uid, token, lgbt);

            if (SisiModuleEntry.Modules != null && SisiModuleEntry.Modules.Count > 0)
            {
                foreach (var entry in SisiModuleEntry.Modules)
                {
                    try
                    {
                        var result = entry.Invoke(HttpContext, requestInfo, host, args);
                        if (result != null && result.Count > 0)
                        {
                            foreach (var item in result)
                                send(item.name, item.init, item.plugin, item.displayindex, item.myinit);
                        }
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex, "CatchId={CatchId}", "id_f9c9ebce");
                    }
                }
            }

            if (SisiModuleEntry.ModulesAsync != null && SisiModuleEntry.ModulesAsync.Count > 0)
            {
                foreach (var entry in SisiModuleEntry.ModulesAsync)
                {
                    try
                    {
                        var result = await entry.InvokeAsync(HttpContext, requestInfo, host, args);
                        if (result != null && result.Count > 0)
                        {
                            foreach (var item in result)
                                send(item.name, item.init, item.plugin, item.displayindex, item.myinit);
                        }
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex, "CatchId={CatchId}", "id_g7b1zx6i");
                    }
                }
            }
            #endregion

            send("pornhubpremium.com", conf.PornHubPremium, "phubprem"); // !rhub
            send("pornhub.com", conf.PornHub, "phub");
            send("xvideos.com", conf.Xvideos, "xds");
            send("xhamster.com", conf.Xhamster, "xmr");
            send("ebalovo.porn", conf.Ebalovo, "elo");
            send("hqporner.com", conf.HQporner, "hqr");

            if (conf.Spankbang.priorityBrowser == "http" || conf.Spankbang.rhub || PlaywrightBrowser.Status != PlaywrightStatus.disabled || !string.IsNullOrEmpty(conf.Spankbang.overridehost) || conf.Spankbang.overridehosts?.Length > 0)
                send("spankbang.com", conf.Spankbang, "sbg");

            send("eporner.com", conf.Eporner, "epr");
            send("porntrex.com", conf.Porntrex, "ptx");
            send("xdsred", conf.XvideosRED, "xdsred");  // !rhub
            send("xnxx.com", conf.Xnxx, "xnx");
            send("tizam.pw", conf.Tizam, "tizam");

            if (conf.BongaCams.priorityBrowser == "http" || conf.BongaCams.rhub || PlaywrightBrowser.Status != PlaywrightStatus.disabled || !string.IsNullOrEmpty(conf.BongaCams.overridehost) || conf.BongaCams.overridehosts?.Length > 0)
                send("bongacams.com", conf.BongaCams, "bgs");

            if (conf.Runetki.priorityBrowser == "http" || conf.Runetki.rhub || PlaywrightBrowser.Status != PlaywrightStatus.disabled || !string.IsNullOrEmpty(conf.Runetki.overridehost) || conf.Runetki.overridehosts?.Length > 0)
                send("runetki.com", conf.Runetki, "runetki");

            send("chaturbate.com", conf.Chaturbate, "chu");

            if (lgbt)
            {
                send("phubgay", conf.PornHub, "phubgay", 10_100);
                send("phubtrans", conf.PornHub, "phubsml", 10_101);
                send("xdsgay", conf.Xvideos, "xdsgay", 10_102);
                send("xdstrans", conf.Xvideos, "xdssml", 10_103);
                send("xmrgay", conf.Xhamster, "xmrgay", 10_104);
                send("xmrtrans", conf.Xhamster, "xmrsml", 10_105);
            }

            return Json(new
            {
                title = "sisi",
                channels = channels.OrderBy(i => i.displayindex)
            });
        }
    }
}
