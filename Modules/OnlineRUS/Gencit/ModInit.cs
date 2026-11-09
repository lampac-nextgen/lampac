using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace Gencit;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        if (args.serial > 0 && (args.kinopoisk_id > 0 || !string.IsNullOrWhiteSpace(args.imdb_id)))
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

    private void updateConf()
    {
        conf = ModuleInvoke.Init("Gencit", new ModuleConf("Gencit", "https://ylitron.pro", streamproxy: true)
        {
            enable = true,
            displayindex = 535,
            stream_access = "apk,cors,web",
            api_host = "https://malsed.site",
            headers = HeadersModel.Init(
                Http.defaultFullHeaders,
                ("referer", GencitService.Referer)
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(
                Http.defaultFullHeaders,
                ("accept", "*/*"),
                ("origin", "https://ylitron.pro"),
                ("referer", "https://ylitron.pro/"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-site")
            ).ToDictionary()
        });
    }

    private string onlineApiQuality(EventOnlineApiQuality e)
        => e.balanser == "gencit" ? " ~ 1080p" : null;
}
