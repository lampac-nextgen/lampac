using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;
using Shared;

namespace Aladin;

public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        return new List<ModuleOnlineItem>()
        {
            new(conf)
        };
    }

    public List<ModuleOnlineSpiderItem> Spider(HttpContext httpContext, RequestModel requestInfo, string host, OnlineSpiderModel args)
    {
        return new List<ModuleOnlineSpiderItem>()
        {
            new(conf, "aladin-search")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("aladin");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Aladin", new ModuleConf("Aladin", "https://apbugall.org/v2", "https://scalp-as.stloadi.live", "22c8122334d050de1bfc97bd08aa5e", "", false, true)
        {
            enable = true,
            displayindex = 512,
            httpversion = 2,
            rch_access = "apk,cors,web",
            stream_access = "apk,cors,web",
            streamproxy = true,
            reserve = true
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        bool m4s = conf.m4s;

        if (e.balanser == "aladin" && e.kitconf != null && e.kitconf.TryGetValue("Aladin", out JToken kit))
        {
            if (kit["m4s"] != null)
                m4s = kit.Value<bool>("m4s");
        }

        return e.balanser switch
        {
            "aladin" => (m4s ? " ~ 2160p" : " ~ 1080p"),
            _ => null
        };
    }
}
