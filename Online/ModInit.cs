using Microsoft.Extensions.DependencyInjection;
using Online.SQL;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System.Collections.Generic;

namespace Online
{
    public class ModInit : IModuleLoaded, IModuleConfigure
    {
        public static string modpath;
        public static ModuleConf conf;

        public void Configure(ConfigureModel app)
        {
            app.services.AddDbContextFactory<ExternalidsContext>(ExternalidsContext.ConfiguringDbBuilder);
        }

        public void Loaded(InitspaceModel baseconf)
        {
            modpath = baseconf.path;

            updateConf();
            EventListener.UpdateInitFile += updateConf;


            ExternalidsContext.Initialization(baseconf.app.ApplicationServices);

            foreach (var m in conf.limit_map)
                CoreInit.conf.WAF.limit_map.Insert(0, m);
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= updateConf;
        }


        void updateConf()
        {
            conf = ModuleInvoke.Init("online", new ModuleConf()
            {
                findkp = "all",
                checkOnlineSearch = CoreInit.conf.online.checkOnlineSearch,
                spider = true,
                spiderName = "Spider",
                component = "lampac",
                name = CoreInit.conf.online.name,
                description = "Плагин для просмотра онлайн сериалов и фильмов",
                version = CoreInit.conf.online.version,
                btn_priority_forced = CoreInit.conf.online.btn_priority_forced,
                showquality = true,
                showExtraTitle = true,
                limit_map = new List<WafLimitRootMap>
                {
                    new("^/lite/", new WafLimitMap { limit = 10, second = 1 }),
                    new("^/(externalids|lifeevents)", new WafLimitMap { limit = 10, second = 1 })
                }
            });
        }
    }
}
