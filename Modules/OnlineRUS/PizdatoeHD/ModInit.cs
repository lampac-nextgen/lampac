using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PizdatoeHD
{
    public class ModInit : IModuleLoaded, IModuleOnline, IModuleOnlineSpider
    {
        public static ConcurrentDictionary<string, DbModel> PizdatoeDb = null;

        public static ModuleConf conf;
        static Timer timer;

        public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
        {
            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                return null;

            return new List<ModuleOnlineItem>()
            {
                new(conf)
            };
        }

        public List<ModuleOnlineSpiderItem> Spider(HttpContext httpContext, RequestModel requestInfo, string host, OnlineSpiderModel args)
        {
            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                return null;

            return new List<ModuleOnlineSpiderItem>()
            {
                new(conf)
            };
        }

        public void Loaded(InitspaceModel baseconf)
        {
            CoreInit.conf.online.with_search.Add("pizdatoehd");

            updateConf();
            EventListener.UpdateInitFile += updateConf;
            EventListener.OnlineApiQuality += onlineApiQuality;

            PizdatoeDb = JsonConvert.DeserializeObject<ConcurrentDictionary<string, DbModel>>(File.ReadAllText("data/PizdatoeDb.json"));
            timer = new Timer(CronParse.Pizda, null, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(Random.Shared.Next(10, 30)));
            
            //CronParse.PizdaBobra();
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= updateConf;
            EventListener.OnlineApiQuality -= onlineApiQuality;

            PizdatoeDb?.Clear();
            timer?.Dispose();
        }

        void updateConf()
        {
            conf = ModuleInvoke.Init("PizdatoeHD", new ModuleConf("pizdatoehd", "https://rezka.ag")
            {
                kit = false,
                displayindex = 331,
                hls = true,
                streamproxy = true,
                stream_access = "apk,cors,web",
                headers_stream = HeadersModel.Init(
                    ("accept-encoding", "gzip, deflate, br, zstd"),
                    ("connection", "keep-alive"),
                    ("origin", "https://rezka.ag"),
                    ("referer", "https://rezka.ag/"),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "cross-site")
                ).ToDictionary()
            });
        }

        string onlineApiQuality(EventOnlineApiQuality e)
        {
            return e.balanser switch
            {
                "pizdatoehd" => " ~ 720p",
                _ => null
            };
        }
    }
}
