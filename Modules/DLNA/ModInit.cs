using Shared;
using Shared.Services;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;

namespace DLNA;

public class ModInit : IModuleLoaded
{
    public static string modpath;
    public static ModuleConf conf;

    public void Loaded(InitspaceModel baseconf)
    {
        modpath = baseconf.path;

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);

        DLNAController.Initialization();
        TrackersCron.Start();
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        TrackersCron.Stop();
        DLNAController.Shutdown();
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("DLNA", new ModuleConf()
        {
            path = "data/dlna",
            uploadSpeed = 125000 * 10,
            autoupdatetrackers = true,
            intervalUpdateTrackers = 90,
            addTrackersToMagnet = true,
            mediaPattern = "^\\.(aac|flac|mpga|mpega|mp2|mp3|m4a|oga|ogg|opus|spx|opus|weba|wav|dif|dv|fli|mp4|mpeg|mpg|mpe|mpv|mkv|ts|m4s|m2ts|mts|ogv|webm|avi|qt|mov)$",
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/dlna/", new WafLimitMap { limit = 50, second = 1 })
            }
        });

        DLNAController.UpdateEngineSettings();
    }
}
