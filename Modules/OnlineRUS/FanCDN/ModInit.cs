using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace FanCDN;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled || string.IsNullOrEmpty(conf.cookie))
            return null;

        var online = new List<ModuleOnlineItem>();

        if (args.kinopoisk_id > 0 && (args.serial == -1 || args.serial == 0))
            online.Add(new(conf));

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
        conf = ModuleInvoke.Init("FanCDN", new OnlinesSettings("FanCDN", "https://fanserial.me", streamproxy: true)
        {
            enable = false,
            displayindex = 520,
            imitationHuman = true,
            headers_stream = HeadersModel.Init(Http.defaultFullHeaders,
                ("origin", "https://fanserial.me"),
                ("referer", "https://fanserial.me/"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "cross-site")
            ).ToDictionary()
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "fancdn" ? " ~ 1080p" : null;
    }
}
