using Microsoft.AspNetCore.Http;
using Shared.Models.Events;
using Shared.Models.Base;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Shared;

namespace Collaps
{
    public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
    {
        public static ModuleConf conf;

        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            return new List<ModuleOnlineItem>()
            {
                new(conf)
            };
        }

        public List<ModuleOnlineSpiderItem> Spider(HttpContext httpContext, RequestModel requestInfo, string host, OnlineSpiderModel args)
        {
            return new List<ModuleOnlineSpiderItem>()
            {
                new(conf, "collaps-search")
            };
        }

        public void Loaded(InitspaceModel baseconf)
        {
            CoreInit.conf.online.with_search.Add("collaps");
            CoreInit.conf.online.with_search.Add("collaps-dash");

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
            conf = ModuleInvoke.Init("Collaps", new ModuleConf("Collaps", "https://api.luxembd.ws", streamproxy: true)
            {
                displayindex = 555,
                rch_access = "apk",
                stream_access = "apk,cors,web",
                apihost = "https://api.bhcesh.me",
                token = "eedefb541aeba871dcfc756e6b31c02e",
                headers = HeadersModel.Init(Http.defaultFullHeaders,
                    ("Origin", "https://kinokrad.my")
                ).ToDictionary(),
                headers_stream = HeadersModel.Init(Http.defaultFullHeaders,
                    ("Origin", "https://kinokrad.my"),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "cross-site"),
                    ("accept", "*/*")
                ).ToDictionary()
            });
        }

        string onlineApiQuality(EventOnlineApiQuality e)
        {
            bool dash = conf.dash;

            if (e.balanser == "collaps" && e.kitconf != null && e.kitconf.TryGetValue("Collaps", out JToken kit))
            {
                if (kit["dash"] != null)
                    dash = kit.Value<bool>("dash");
            }

            return e.balanser switch
            {
                "collaps-dash" => " ~ 1080p",
                "collaps" => (dash ? " ~ 1080p" : " ~ 720p"),
                _ => null
            };
        }
    }
}
