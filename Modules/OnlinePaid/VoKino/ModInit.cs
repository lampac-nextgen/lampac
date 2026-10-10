using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Kit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoKino;

public class ModInit : IModuleLoaded, IModuleOnlineAsync
{
    public static ModuleConf conf;

    public async Task<List<ModuleOnlineItem>> InvokeAsync(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        var user = requestInfo.user;
        var memoryCache = HybridCache.GetMemory();

        if (args.kinopoisk_id > 0 || (args.source != null && args.source.Equals("vokino", StringComparison.OrdinalIgnoreCase)))
        {
            string vid = args.kinopoisk_id.ToString();
            if ((args.source != null && args.source.Equals("vokino", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(args.id))
                vid = args.id;

            var myinit = KitInvoke.Load(conf, args.kitconf, requestInfo, (j, i, c) =>
            {
                if (j.ContainsKey("online"))
                    i.online = c.online;

                return i;
            });

            if (myinit.enable && myinit.HasToken())
            {
                void SendOnline(JObject view)
                {
                    var on = myinit.online;

                    void send(string name)
                    {
                        string url = "{localhost}/lite/vokino?balancer=" + name.ToLower();

                        string displayname = $"{myinit.displayname ?? "VoKino"}";
                        if (name != "VoKino")
                            displayname = $"{name} ({myinit.displayname ?? "VoKino"})";

                        if (myinit.onlyBalancerName)
                            displayname = name;

                        online.Add(new(myinit, (name == "VoKino" ? "vokino" : $"vokino-{name.ToLower()}"), displayname, myurl: url));
                    }

                    if (on.vokino && (view == null || view.ContainsKey("Vokino")))
                        send("VoKino");

                    if (on.videobase && (view == null || view.ContainsKey("VideoBase") || view.ContainsKey("Videobase")))
                        send("VideoBase");

                    if (on.filmix && (view == null || view.ContainsKey("Filmix")))
                        send("Filmix");

                    if (on.zetflix && (view == null || view.ContainsKey("Zetflix")))
                        send("Zetflix");

                    if (on.mango && (view == null || view.ContainsKey("Mango")))
                        send("Mango");

                    if (on.alloha && (view == null || view.ContainsKey("Alloha")))
                        send("Alloha");

                    if (on.vibix && (view == null || view.ContainsKey("Vibix")))
                        send("Vibix");

                    if (on.coconut && (view == null || view.ContainsKey("Coconut")))
                        send("Coconut");

                    if (on.monframe && (view == null || view.ContainsKey("MonFrame")))
                        send("MonFrame");

                    if (on.remux && (view == null || view.ContainsKey("Remux")))
                        send("Remux");

                    if (on.ashdi && (view == null || view.ContainsKey("Ashdi")))
                        send("Ashdi");

                    if (on.hdvb && (view == null || view.ContainsKey("Hdvb")))
                        send("HDVB");
                }

                ValueTask vkino()
                {
                    SendOnline(null);
                    return ValueTask.CompletedTask;
                }

                if (CoreInit.conf.accsdb.enable)
                {
                    if (user != null)
                    {
                        if (myinit.group > user.group && myinit.group_hide) { }
                        else
                            await vkino();
                    }
                }
                else
                {
                    if (myinit.group > 0 && myinit.group_hide && (user == null || myinit.group > user.group)) { }
                    else
                        await vkino();
                }
            }
        }

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
        conf = ModuleInvoke.Init("VoKino", new ModuleConf("VoKino", "http://api.vokino.org")
        {
            displayindex = 300,
            streamproxy = false,
            rchstreamproxy = "web",
            rhub_safety = false
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        switch (e.balanser)
        {
            case "vokino":
            case "vokino-videobase":
            case "vokino-alloha":
            case "vokino-filmix":
                return " ~ 2160p";
            case "vokino-vibix":
            case "vokino-monframe":
            case "vokino-remux":
            case "vokino-ashdi":
            case "vokino-zetflix":
            case "vokino-hdvb":
            case "vokino-coconut":
                return " ~ 1080p";
            case "vokino-mango":
                return " ~ 720p";
        }

        return null;
    }
}
