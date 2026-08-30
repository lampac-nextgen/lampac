using Shared;
using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace HydraFlix;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        if ((args.original_language == null || args.original_language == "en") && CoreInit.conf.disableEng == false)
        {
            if (args.source != null && (args.source is "tmdb" or "cub") && long.TryParse(args.id, out long _id) && _id > 0)
            {
                if (Firefox.Status != PlaywrightStatus.disabled)
                    online.Add(new(conf, "hydraflix", "HydraFlix", " (ENG)"));
            }
        }

        return online;
    }

    public void Loaded(InitspaceModel baseconf)
    {
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
        /// <summary>
        /// https://www.hydraflix.cc
        /// </summary>
        conf = ModuleInvoke.Init("Hydraflix", new OnlinesSettings("Hydraflix", "https://vidfast.vc")
        {
            displayindex = 1000,
            streamproxy = true,
            priorityBrowser = "firefox"
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "hydraflix" ? " ~ 1080p" : null;
    }
}
