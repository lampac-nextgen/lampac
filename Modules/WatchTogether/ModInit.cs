using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Collections.Generic;
using Shared.Services;

namespace WatchTogether
{
    public class WatchTogetherConf
    {
        public bool enable { get; set; } = true;
        public int max_rooms_total { get; set; } = 500;
        public int max_rooms_per_user { get; set; } = 3;
        public int gc_empty_timeout_minutes { get; set; } = 60;
        public int gc_max_lifetime_hours { get; set; } = 12;
        public int ws_ping_interval { get; set; } = 20;
        public bool allow_anonymous { get; set; } = true;
        public bool web_guests_interactive { get; set; } = true;

        public List<WafLimitRootMap> limit_map { get; set; } = new()
        {
            new("^/watchtogether/", new WafLimitMap { limit = 30, second = 1 })
        };
    }

    public class ModInit : IModuleLoaded, IModuleConfigure
    {
        public static string modpath;
        public static WatchTogetherConf conf;

        public void Configure(ConfigureModel app) { }

        public void Loaded(InitspaceModel baseconf)
        {
            modpath = baseconf.path;
            UpdateConf();
            EventListener.UpdateInitFile += UpdateConf;
            WsEvents.Start();
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= UpdateConf;
            WsEvents.Stop();
        }

        private static bool isWafInjected = false;

        private void UpdateConf()
        {
            conf = ModuleInvoke.Init("WatchTogether", new WatchTogetherConf());

            if (!isWafInjected && conf.limit_map != null)
            {
                foreach (var m in conf.limit_map)
                {
                    CoreInit.conf.WAF.limit_map.Insert(0, m);
                }
                isWafInjected = true;
            }
        }
    }
}
