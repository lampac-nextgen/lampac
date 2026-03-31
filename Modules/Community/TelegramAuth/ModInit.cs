using System.Collections.Generic;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using TelegramAuth.Models;
using TelegramAuth.Services;

namespace TelegramAuth
{
    public class ModInit : IModuleLoaded
    {
        public static TelegramAuthConf conf = new();

        public static TelegramAuthStore Store { get; private set; } = null!;

        public void Loaded(InitspaceModel initspace)
        {
            updateConf();
            EventListener.UpdateInitFile += updateConf;

            foreach (var m in conf.limit_map ?? new List<WafLimitRootMap>())
                CoreInit.conf.WAF.limit_map.Insert(0, m);
        }

        void updateConf()
        {
            conf = ModuleInvoke.Init("TelegramAuth", new TelegramAuthConf
            {
                data_dir = null,
                legacy_import_path = "",
                enable_import = true,
                enable_cleanup = true,
                max_active_devices_per_user = 0,
                mutations_api_secret = "",
                owner_telegram_ids = null,
                auto_provision_users = false,
                auto_provision_role = "user",
                auto_provision_lang = "ru",
                auto_provision_expires_days = 0,
                auto_provision_activate_immediately = false,
                limit_map = new List<WafLimitRootMap>
                {
                    new("^/tg/auth", new WafLimitMap { limit = 25, second = 1 })
                }
            });

            Store = new TelegramAuthStore(conf);
            Store.EnsureStorage();
            Store.EnsureOwnerUsersAtStartup();
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= updateConf;
        }
    }
}
