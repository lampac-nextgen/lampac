using Shared.Models.AppConf;
using Shared.Models.Module;

namespace TelegramAuth.Models
{
    public class TelegramAuthConf : ModuleBaseConf
    {
        public bool enable { get; set; }

        public string? data_dir { get; set; }

        public string legacy_import_path { get; set; } = "";

        public bool enable_import { get; set; } = true;

        public bool enable_cleanup { get; set; } = true;

        public int max_active_devices_per_user { get; set; }

        public long[]? owner_telegram_ids { get; set; }

        public bool auto_provision_users { get; set; }

        public string auto_provision_role { get; set; } = "user";

        public string auto_provision_lang { get; set; } = "ru";

        public int auto_provision_expires_days { get; set; }

        public bool auto_provision_activate_immediately { get; set; }

        public int accsdb_sync_group_admin { get; set; } = 100;

        public int accsdb_sync_group_user { get; set; }
    }
}
