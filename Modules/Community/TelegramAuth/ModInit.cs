using System;
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
            UpdateConfFromInit();
            EventListener.UpdateInitFile += UpdateConfFromInit;
        }

        void UpdateConfFromInit()
        {
            conf = ModuleInvoke.Init("TelegramAuth", new TelegramAuthConf
            {
                enable = false,
                data_dir = null,
                legacy_import_path = "",
                enable_import = true,
                enable_cleanup = true,
                max_active_devices_per_user = 0,
                owner_telegram_ids = null,
                auto_provision_users = false,
                auto_provision_role = "user",
                auto_provision_lang = "ru",
                auto_provision_expires_days = 0,
                auto_provision_activate_immediately = false,
                accsdb_sync_group_admin = 100,
                accsdb_sync_group_user = 0,
                limit_map = new List<WafLimitRootMap>
                {
                    new("^/tg/auth", new WafLimitMap { limit = 5, second = 1 })
                }
            });

            if (CoreInit.conf != null && conf.enable)
                CoreInit.conf.accsdb.enable = true;

            Store = new TelegramAuthStore(conf);
            Store.EnsureStorage();
            Store.EnsureOwnerUsersAtStartup();

            if (conf.enable)
                ApplyWafLimitMapFromConf();
        }

        void ApplyWafLimitMapFromConf()
        {
            var waf = CoreInit.conf?.WAF?.limit_map;
            if (waf == null)
                return;

            var ours = conf.limit_map ?? new List<WafLimitRootMap>();
            var patterns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in ours)
            {
                if (!string.IsNullOrEmpty(m?.pattern))
                    patterns.Add(m.pattern);
            }

            if (patterns.Count == 0)
                return;

            waf.RemoveAll(x => x != null && !string.IsNullOrEmpty(x.pattern) && patterns.Contains(x.pattern));
            foreach (var m in ours)
                waf.Insert(0, m);
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= UpdateConfFromInit;
        }
    }
}
