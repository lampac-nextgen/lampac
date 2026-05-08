using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace LampaWeb;

public class ModInit : IModuleLoaded
{
    public static string modpath;

    public static ModuleConf conf;

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.Accsdb += accsdbEvent;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);

        LampaCron.Start();
    }

    public void Dispose()
    {
        LampaCron.Stop();
        EventListener.UpdateInitFile -= updateConf;
        EventListener.Accsdb -= accsdbEvent;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("LampaWeb", new ModuleConf()
        {
            autoupdate = true,
            intervalupdate = 90, // minute
            basetag = true,
            index = "lampa-main/index.html",
            git = "yumata/lampa",
            tree = "d96b1849da8a03c4d9d029ab2fec5a02c5fa7923",
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/(extensions|testaccsdb|msx/)", new WafLimitMap { limit = 10, second = 1 })
            }
        });
    }

    void accsdbEvent(EventAccsdb e)
    {
        var accsdb = CoreInit.conf.accsdb;

        if (accsdb.enable &&
            accsdb.shared_passwd != null &&
            e.httpContext.Request.Path.Value.Equals("/testaccsdb", StringComparison.OrdinalIgnoreCase) &&
            e.requestInfo.user_uid == accsdb.shared_passwd)
        {
            e.requestInfo.IsAnonymousRequest = true;
        }
    }
}
