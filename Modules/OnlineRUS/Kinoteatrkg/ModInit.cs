using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace Kinoteatrkg;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (args.serial == -1 || args.serial == 0)
        {
            return new List<ModuleOnlineItem>()
            {
                new(conf, plugin: "kinoteatrkg", name: "Kinoteatr.kg")
            };
        }

        return null;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        if (!CoreInit.conf.online.with_search.Contains("kinoteatrkg"))
            CoreInit.conf.online.with_search.Add("kinoteatrkg");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    private void updateConf()
    {
        conf = ModuleInvoke.Init("Kinoteatrkg", new OnlinesSettings("Kinoteatrkg", "https://kinoteatr.kg", enable: true));
    }

    private string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser switch
        {
            "kinoteatrkg" => " ~ 1080p",
            _ => null
        };
    }
}
