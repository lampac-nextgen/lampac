using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;

namespace WatchTogether
{
    public class ModInit : IModuleLoaded, IModuleConfigure
    {
        public static string modpath;

        public void Configure(ConfigureModel app) {}

        public void Loaded(InitspaceModel baseconf)
        {
            modpath = baseconf.path;
            WsEvents.Start();
        }

        public void Dispose()
        {
            WsEvents.Stop();
        }
    }
}