using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace Redheadsound;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (conf == null || !conf.enable)
            return null;

        // браузер нужен только в старом режиме ladonimode=card/fake
        if (!(conf.ladonimode ?? "http").Equals("http", StringComparison.OrdinalIgnoreCase) && Chromium.Status == PlaywrightStatus.disabled)
            return null;

        if (string.IsNullOrEmpty(args.title) && string.IsNullOrEmpty(args.original_title))
            return null;

        return new List<ModuleOnlineItem>()
        {
            new(conf, "redheadsound", "RHS")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
        EventListener.ProxyApiCreateHttpRequest += Service.ProxyApiCreateHttpRequest;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
        EventListener.ProxyApiCreateHttpRequest -= Service.ProxyApiCreateHttpRequest;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Redheadsound", new ModuleConf("Redheadsound", "https://redheadsound3.top")
        {
            enable = true,
            displayname = "RHS",
            displayindex = 511,
            streamproxy = true,
            httpversion = 2,
            httptimeout = 40,
            headers = Http.defaultFullHeaders
        });

        conf.streamproxy = true;
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "redheadsound" ? " ~ 1080p" : null;
    }
}
