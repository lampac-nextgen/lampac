using Microsoft.AspNetCore.Http;
using Shared.Models.Events;
using Shared.Models.Base;
using System.Collections.Generic;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using Shared;

namespace Kodik;

public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
{
    public static ModuleConf conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        string lang = args.original_language?.Split("|")?[0];
        if (args.isanime || (lang is "ja" or "ko" or "zh" or "cn" or "th" or "vi" or "tl"))
        {

            return new List<ModuleOnlineItem>()
            {
                new(conf)
            };
        }

        return null;
    }

    public List<ModuleOnlineSpiderItem> Spider(HttpContext httpContext, RequestModel requestInfo, string host, OnlineSpiderModel args)
    {
        if (!args.isanime)
            return null;

        return new List<ModuleOnlineSpiderItem>()
        {
            new(conf)
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("kodik");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;

        //KodikController.database = JsonHelper.ListReader<Result>("data/kodik.json", 100_000);
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Kodik", new ModuleConf("Kodik", "41dd95f84c21719b09d6c71182237a25", true)
        {
            displayindex = 100,
            rch_access = "apk",
            stream_access = "apk,cors,web",
            apihost = "https://kodik-api.com",
            playerhost = "https://kodikplayer.com",
            linkhost = "https://kodikres.com",
            auto_proxy = true,
            cdn_is_working = true,
            headers = HeadersModel.Init(("referer", "https://anilib.me/")).ToDictionary()
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "kodik" ? " ~ 720p" : null;
    }
}
