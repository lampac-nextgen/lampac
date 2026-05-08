using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System.Collections.Generic;

namespace PidTor;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static PidTorSettings conf;
    public static int tsport;

    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        bool enable = (conf.torrs != null && conf.torrs.Length > 0) || (conf.auth_torrs != null && conf.auth_torrs.Count > 0);

        if (enable || CoreInit.CurrentConf.ContainsKey("TorrServer"))
        {
            var md = new BaseSettings()
            {
                plugin = "PidTor",
                enable = conf.enable,
                enabled = conf.enable,
                displayname = conf.displayname,
                displayindex = conf.displayindex,
                group = conf.group,
                group_hide = conf.group_hide
            };

            online.Add(new(md, "pidtor", "Pid<s>T</s>or"));
        }

        return online;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("pidtor");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.UpdateCurrentConf += updateCurrentConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.UpdateCurrentConf -= updateCurrentConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("PidTor", new PidTorSettings()
        {
            enable = true,
            displayindex = 551,
            min_sid = 15,
            emptyVoice = true,
            redapi = "http://jac.red"
        });
    }

    void updateCurrentConf()
    {
        if (CoreInit.CurrentConf.TryGetValue("TorrServer", out var torrServerConf))
            tsport = torrServerConf.Value<int>("tsport");
        else
            tsport = 9085;
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser switch
        {
            "pidtor" => " ~ 2160p",
            _ => null
        };
    }
}
