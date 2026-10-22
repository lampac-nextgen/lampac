using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace LordFilm;

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
            new(conf, "lordfilm-search")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("lordfilm");

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
        conf = ModuleInvoke.Init("LordFilm", new ModuleConf("LordFilm", "https://gw.lordfilm5.pro", streamproxy: true)
        {
            displayindex = 575,
            httpversion = 2,
            rch_access = "apk,cors,web",
            stream_access = "apk,cors,web"
        });

        // Хост из init.conf — только точка входа: если сайт редиректит на другое
        // зеркало (gw, we и т.д.), LordFilmSession сам переключится и решит челлендж там.
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        if (e.balanser != "lordfilm")
            return null;

        return (conf.dash || conf.both) ? " ~ 1080p" : " ~ 720p";
    }
}
