using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;
using Shared;

namespace VkMovie;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static OnlinesSettings conf;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        if (args.serial == -1 || args.serial == 0)
        {
            return new List<ModuleOnlineItem>()
            {
                new(conf, plugin: "vkmovie", name: "VK Видео")
            };
        }

        return null;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("vkmovie");

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
        conf = ModuleInvoke.Init("VkMovie", new OnlinesSettings("VkMovie", "https://api.vkvideo.ru")
        {
            displayindex = 570,
            streamproxy = true,
            rch_access = "apk,cors",
            stream_access = "apk,cors",
            rchstreamproxy = "web",
            headers = HeadersModel.Init(Http.defaultFullHeaders,
                ("origin", "https://vkvideo.ru"),
                ("referer", "https://vkvideo.ru/")
            ).ToDictionary()
        });
    }

    private string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser switch
        {
            "vkmovie" => " ~ 2160p",
            _ => null
        };
    }
}
