using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using Shared;
using System;
using System.Collections.Generic;

namespace Aladin;

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
            new(conf, "aladin-search")
        };
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("aladin");

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
        conf = ModuleInvoke.Init("Aladin", new ModuleConf("Aladin", "https://apbugall.org/v2", "https://scalp-as.stloadi.live", "22c8122334d050de1bfc97bd08aa5e", true)
        {
            enable = true,
            displayindex = 512,
            httpversion = 2,
            rch_access = "apk,cors,web",
            stream_access = "apk,cors,web",

            tlsProfile = "chrome_133",
            // Public constant from the player bundle; can be overridden in init.conf.
            authorizations = "Bearer pXzvbyDGLYyB6VkwsWZDv3iMKZtsXNzpzRyxZUcsKHXxsSeaYakbo3hw9mBFRc5VQTpqAX6BW8aDEqyLaHYcXSQiV6KHYTVTK6MYR"
                + "phNAy5sBjtrevqkDzKmLqNdfMZGEU9NELjmtKfZy3RNGzCd767sNh1mXEj4tCcvqndHtzmwAbZNkhm4ghDEasodotMBewypNQ56uotJAQGX11cs"
                + "feRfBAPk8DcUWWkkqzxca8vbnEw12vUFbBzT6hz8ZB3F3dzUhUXoL2cr1WM1bXQArRCS1MUNMz3X5WDMMQoZKxj2AMTRqp7QQX4dDB9B7VzEZT"
                + "myFULhm1AcHHMkoMvSVvKYoBoAKLycYAgMHeD4ECJcGEAGpnkJhrV57zQ7",
            // Stable fingerprint for this implementation; must stay constant
            // for the whole stream.
            fingerprintHash = "de80f4dfcbc8eb0d394107a7255229002170b9fb2950a014af8dc3c313853aac",
            referer = "https://kinogo.media/",
            acceptLanguage = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7",
            linkhosts = new List<string>
            {
                "https://scalp-as.stloadi.live",
                "https://quadrillion-as.allarknow.online",
                "https://Aport-as.allarknow.online"
            },
            nativeProxy = "",
            httpRetries = 3,
            resolveTimeout = 8,
            urlTtl = 7200,
            wsReconnectAttempts = 3,
            wsMaxSessions = 12,
            wsHandshakeConcurrency = 2,
            liveMaxStreams = 256,
            liveStreamTtl = 14400
        });

        CurlClient.Profile = string.IsNullOrWhiteSpace(conf.tlsProfile) ? "chrome_133" : conf.tlsProfile;
        AladinNative.Configure(conf);

        if (!CurlClient.Initialize(conf.nativeLibrary))
            Serilog.Log.Warning("Aladin native runtime is unavailable: {Error}", CurlClient.LoadError);
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        bool m4s = conf.m4s;

        if (e.balanser == "aladin" && e.kitconf != null && e.kitconf.TryGetValue("Aladin", out JToken kit))
        {
            if (kit["m4s"] != null)
                m4s = kit.Value<bool>("m4s");
        }

        return e.balanser switch
        {
            "aladin" => (m4s ? " ~ 2160p" : " ~ 1080p"),
            _ => null
        };
    }
}
