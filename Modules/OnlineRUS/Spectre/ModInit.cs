using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace Spectre;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (Chromium.Status == PlaywrightStatus.disabled)
            return null;

        return new List<ModuleOnlineItem>()
        {
            new(conf)
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
        EventListener.ProxyApiCreateHttpRequest += Service.ProxyApiCreateHttpRequest;
        Service.Start();
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
        EventListener.ProxyApiCreateHttpRequest -= Service.ProxyApiCreateHttpRequest;
        Service.Stop();
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Spectre", new ModuleConf("Spectre", "https://api.apbugall.org", "https://aport-as.allarknow.online", "22c8122334d050de1bfc97bd08aa5e", "", true)
        {
            enable = true,
            mux = true, // multi stream
            m4s = true, // 4k
            displayindex = 510,
            streamproxy = true,
            httpversion = 2,
            headers = Http.defaultFullHeaders
        });

        conf.streamproxy = true;
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "spectre" ? " ~ 2160p" : null;
    }
}
