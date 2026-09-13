using Microsoft.Extensions.DependencyInjection;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using SyncEvents;
using System.Collections.Generic;
using System.IO;

namespace Sync;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    public static string modpath;
    public static ModuleConf conf;

    public void Configure(ConfigureModel app)
    {
        app.services.AddDbContextFactory<SqlContext>(SqlContext.ConfiguringDbBuilder);
    }

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        NwsEvents.Start(onlyreg: true);

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);

        Directory.CreateDirectory("database/storage");
        Directory.CreateDirectory("database/storage/temp");

        SqlContext.Initialization(baseconf.app.ApplicationServices);

        // 1 = закладки одним JSON-блобом на пользователя, без ревизий и надгробий.
        ModuleCapabilities.Set("bookmarks", 1);
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        ModuleCapabilities.Remove("bookmarks");
        NwsEvents.Stop();
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Sync", new ModuleConf()
        {
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/bookmark", new WafLimitMap { limit = 10, second = 1 })
            }
        });
    }
}
