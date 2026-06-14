using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Shared.Attributes;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Entrys;
using Shared.Services.Pools;
using System.Text;
using System.Web;
using IO = System.IO;

namespace SISI;

public class SisiApiController : BaseController
{
    #region sisi.js
    [HttpGet, AllowAnonymous]
    [Staticache(20, always: true, setHeadersNoCache: true)]
    [Route("sisi.js")]
    [Route("sisi/js/{token}")]
    public ActionResult Sisi(string token)
    {
        var init = ModInit.conf;

        string file = memoryCache.GetOrCreate("sisi.js", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            string filejs = FileCache.ReadAllText($"{ModInit.modpath}/plugins/sisi.js", "sisi.js", saveCache: false);

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

            return filejs;
        });

        var bulder = StringBuilderPool.Rent();

        try
        {
            bulder = bulder.Append(file);
            bulder = bulder.Replace("{rch_websoket}", FileCache.ReadAllText("plugins/rch_nws.js", "rch_nws.js"));

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
                bulder = bulder
                    .Replace("Defined.use_api == 'pwa'", "true")
                    .Replace("'<div>p</div>'", $"'<div>{init.iconame}</div>'");
            }

            bulder = bulder
                .Replace("{invc-rch}", FileCache.ReadAllText("plugins/invc-rch.js", "invc-rch.js"))
                .Replace("{invc-rch_nws}", FileCache.ReadAllText("plugins/invc-rch_nws.js", "invc-rch_nws.js"))
                .Replace("{push_all}", init.push_all ? "true" : "false")
                .Replace("{localhost}", host)
                .Replace("{historySave}", ModInit.conf.history.enable ? "true" : "false")
                .Replace("{token}", HttpUtility.UrlEncode(token));

            if (init.forced_checkRchtype)
                bulder = bulder.Replace("window.rchtype", "Defined.rchtype");

            if (EventListener.AppReplace != null)
            {
                foreach (Func<string, EventAppReplace, StringBuilder> handler in EventListener.AppReplace.GetInvocationList())
                    bulder = handler.Invoke("sisi", new EventAppReplace(bulder, token, null, host, requestInfo, HttpContext.Request));
            }

            return ContentTo(bulder, "application/javascript; charset=utf-8");
        }
        finally
        {
            StringBuilderPool.Return(bulder);
        }
    }
    #endregion

    #region startpage.js
    [HttpGet, AllowAnonymous]
    [Staticache(10, always: true, setHeadersNoCache: true)]
    [Route("startpage.js")]
    public ActionResult StartPage()
    {
        string startpage = FileCache.ReadAllText($"{ModInit.modpath}/plugins/startpage.js", "startpage.js", saveCache: false)
            .Replace("{localhost}", host);

        return ContentTo(startpage, "application/javascript; charset=utf-8");
    }
    #endregion


    [HttpGet]
    [Route("sisi")]
    async public Task<ActionResult> Index(string rchtype, string account_email, string uid, string token, bool spder)
    {
        var appConf = ModInit.conf;
        JObject kitconf = loadKitConf();

        bool lgbt = appConf.lgbt;
        if (kitconf != null && kitconf.Value<bool?>("lgbt") == false)
            lgbt = false;

        var channels = new List<ChannelItem>(50)
        {
            new("Закладки", $"{host}/sisi/bookmarks", 0)
        };

        if (ModInit.conf.history.enable)
            channels.Add(new("История", $"{host}/sisi/historys", 1));

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

                if (init.rhub && !init.rhub_fallback && !init.corseu && string.IsNullOrEmpty(init.webcorshost))
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

            if (string.IsNullOrEmpty(url))
                url = $"{host}/{plugin ?? name.ToLowerAndTrim()}";

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
        var args = new SisiEventsModel(rchtype, account_email, uid, token, lgbt, kitconf);

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

        #region EventListener
        if (EventListener.SisiChannels != null)
        {
            var em = new EventSisiChannels(this, HttpContext, channels);

            foreach (Func<EventSisiChannels, ActionResult> handler in EventListener.SisiChannels.GetInvocationList())
            {
                var eventResult = handler(em);
                if (eventResult != null)
                    return eventResult;
            }
        }
        #endregion

        return Json(new
        {
            title = "sisi",
            channels = channels.OrderBy(i => i.displayindex)
        });
    }
}
