using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace KubikVKube;

public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (string.IsNullOrWhiteSpace(args.title) && string.IsNullOrWhiteSpace(args.original_title))
            return null;

        // студия переводит зарубежное — русскоязычные релизы не ищем
        if (string.Equals(args.original_language, "ru", StringComparison.OrdinalIgnoreCase))
            return null;

        return new List<ModuleOnlineItem> { new(conf, "kubikvkube", "Кубик в Кубе") };
    }

    public List<ModuleOnlineSpiderItem> Spider(HttpContext httpContext, RequestModel requestInfo, string host, OnlineSpiderModel args)
        => new List<ModuleOnlineSpiderItem> { new(conf, "kubikvkube") };

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
        Console.WriteLine("[KubikVKube] loaded");
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("KubikVKube", new ModuleConf("KubikVKube", "https://kubikvkube.com", streamproxy: true)
        {
            displayname = "Кубик в Кубе",
            displayindex = 520,
            stream_access = "apk,cors,web",
            httptimeout = 8,
            headers = HeadersModel.Init(Http.defaultFullHeaders).ToDictionary()
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
        => e.balanser == "kubikvkube" ? " ~ 1080p" : null;
}
