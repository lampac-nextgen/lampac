using Microsoft.Extensions.DependencyInjection;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.IO;

namespace WatchTogether
{
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

            // Init Database
            SqlContext.Initialization(baseconf.app.ApplicationServices);

            WsEvents.Start();
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= updateConf;
            WsEvents.Stop();
        }

        void updateConf()
        {
            conf = ModuleInvoke.Init("WatchTogether", new ModuleConf());
        }
    }
}
