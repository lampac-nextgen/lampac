using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace KinoUkr;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;
    static Timer kinoukrTimer;

    public static Dictionary<string, DbModel> database;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        if (!args.isanime)
            online.Add(new(conf, arg_title: " (Украинский)"));

        return online;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("kinoukr");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;

        if (conf.enable)
        {
            database = JsonConvert.DeserializeObject<Dictionary<string, DbModel>>(File.ReadAllText("data/kinoukr.json"));
            kinoukrTimer = new Timer(CronParse.Kinoukr, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(20));
        }
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;

        database?.Clear();
        database = null;
        kinoukrTimer?.Dispose();
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Kinoukr", new OnlinesSettings("Kinoukr", "https://kinoukr.com")
        {
            enable = false,
            displayindex = 815,
            rch_access = "apk,cors",
            stream_access = "apk,cors",
            rchstreamproxy = "web",
            geo_hide = ["RU", "BY"],
            headers = HeadersModel.Init(Http.defaultFullHeaders,
                ("cookie", "legit_user=1;"),
                ("origin", "https://kinoukr.com"),
                ("referer", "https://kinoukr.com/"),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1")
            ).ToDictionary()
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser == "kinoukr" ? " ~ 1080p" : null;
    }
}
