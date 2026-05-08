using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace FlixCDN;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (Firefox.Status == PlaywrightStatus.disabled)
            return null;

        var online = new List<ModuleOnlineItem>();

        if (args.kinopoisk_id > 0 && !args.isanime)
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
        conf = ModuleInvoke.Init("FlixCDN", new OnlinesSettings("FlixCDN", "https://player0.flixcdn.space", "https://api0.flixcdn.biz/api", streamproxy: true)
        {
            enable = false,
            displayindex = 525,
            stream_access = "apk,cors,web",
            headers_stream = HeadersModel.Init(
                ("accept", "*/*"),
                ("origin", "https://player0.flixcdn.space"),
                ("referer", "https://player0.flixcdn.space/"),
                ("sec-fetch-dest", "video"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "cross-site")
            ).ToDictionary()
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "flixcdn" ? " ~ 1080p" : null;
    }
}
