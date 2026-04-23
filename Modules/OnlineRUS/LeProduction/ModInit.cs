using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace LeProduction
{
    public class ModInit : IModuleLoaded, IModuleOnline
    {
        public static OnlinesSettings conf;

        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            var online = new List<ModuleOnlineItem>();

            if (!args.isanime)
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
            conf = ModuleInvoke.Init("LeProduction", new OnlinesSettings("LeProduction", "https://www.le-production.tv")
            {
                displayindex = 545,
                rch_access = "apk,cors",
                stream_access = "apk,cors",
                rchstreamproxy = "web"
            });
        }

        string onlineApiQuality(EventOnlineApiQuality e)
        {
            return e.balanser == "leproduction" ? " ~ 1080p" : null;
        }
    }
}
