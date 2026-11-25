using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace NinePeso;

public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(
        HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        // Only advertise requests that can be resolved through live search.
        if (string.IsNullOrWhiteSpace(args.title) &&
            string.IsNullOrWhiteSpace(args.original_title) &&
            args.kinopoisk_id <= 0 && string.IsNullOrWhiteSpace(args.imdb_id))
            return null;

        return new List<ModuleOnlineItem> { new(conf) };
    }

    public List<ModuleOnlineSpiderItem> Spider(
        HttpContext httpContext, RequestModel requestInfo, string host, OnlineSpiderModel args)
    {
        return new List<ModuleOnlineSpiderItem> { new(conf, "ninepeso-spider") };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("ninepeso");

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
        conf = ModuleInvoke.Init("NinePeso", new OnlinesSettings(
            "NinePeso", "https://231126.tartugi.name", "https://videobalanser.cc", streamproxy: true)
        {
            displayindex = 550,
            // TV responses include all episodes/translations and often exceed
            // the BaseSettings default 8-second HTTP timeout.
            httptimeout = 30,
            httpversion = 2,
            rch_access = "apk",
            stream_access = "apk,cors,web",
            headers = HeadersModel.Init(Http.defaultFullHeaders,
                ("referer", "https://videobalanser.cc/")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(Http.defaultFullHeaders,
                ("origin", "https://videobalanser.cc"),
                ("referer", "https://videobalanser.cc/")
            ).ToDictionary()
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
        => e.balanser == "ninepeso" ? " ~ 1080p" : null;
}
