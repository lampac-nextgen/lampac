using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;
using Shared;

namespace Rezka
{
    public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
    {
        public static RezkaSettings conf;

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
                new(conf)
            };
        }

        public void Loaded(InitspaceModel baseconf)
        {
            CoreInit.conf.online.with_search.Add("rezka");

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
            conf = ModuleInvoke.Init("Rezka", new RezkaSettings("Rezka", "https://hdrezka.me")
            {
                displayindex = 330,
                streamproxy = true,
                stream_access = "apk,cors,web",
                ajax = true,
                reserve = true,
                hls = true,
                scheme = "http",
                headers = Http.defaultUaHeaders
            });
        }

        string onlineApiQuality(EventOnlineApiQuality e)
        {
            if (e.balanser == "rezka" && e.kitconf != null)
            {
                bool premium = conf.premium;

                if (e.kitconf.TryGetValue("Rezka", out JToken kit))
                {
                    if (kit["premium"] != null)
                        premium = kit.Value<bool>("premium");
                }

                return premium ? " ~ 2160p" : " ~ 720p";
            }
            else
            {
                return e.balanser switch
                {
                    "rezka" => conf.premium ? " ~ 2160p" : " ~ 720p",
                    _ => null
                };
            }
        }
    }
}
