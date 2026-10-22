using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace SmashyStream;

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
                if (PlaywrightBrowser.Status != PlaywrightStatus.disabled)
                    online.Add(new(conf, "smashystream", "SmashyStream", " (ENG)"));
            }
        }

        return online;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        UpdateConf();
        EventListener.UpdateInitFile += UpdateConf;
        EventListener.OnlineApiQuality += OnlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= UpdateConf;
        EventListener.OnlineApiQuality -= OnlineApiQuality;
    }

    private void UpdateConf()
    {
        // https://smashystream.xyz
        // https://anyembed.xyz/
        conf = ModuleInvoke.Init("Smashystream", new OnlinesSettings("Smashystream", "https://anyembed.xyz")
        {
            // anyembed.xyz returns 451 (DMCA); no working drop-in mirror — disabled by default
            enable = false,
            displayindex = 1030,
            streamproxy = true
        });
    }

    private string OnlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "smashystream" ? " ~ 1080p" : null;
    }
}
