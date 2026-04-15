using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.PlaywrightCore;
using Shared.Services;
using System.Collections.Generic;

namespace Zetflix
{
    public class ModInit : IModuleLoaded, IModuleOnline
    {
        public static ModuleConf conf;

        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled || args.kinopoisk_id <= 0)
                return null;

            return new List<ModuleOnlineItem>()
            {
                new(conf)
            };
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
            conf = ModuleInvoke.Init("Zetflix", new ModuleConf("Zetflix", "kwwsv=22jr1}hw0iol{1rqolqh", enable: true, streamproxy: true)
            {
                displayindex = 510,
                stream_access = "apk,cors,web",
                httpversion = 2,
                headers = Http.defaultFullHeaders,
                geostreamproxy = new string[] { "ALL" }
            });
        }

        string onlineApiQuality(EventOnlineApiQuality e)
        {
            return e.balanser switch
            {
                "zetflix" => " ~ 1080p",
                _ => null
            };
        }
    }
}
