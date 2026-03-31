using Shared.Models.AppConf;
using Shared.Models.Module;

namespace TelegramAuth.Models
{
    public class TelegramAuthConf : ModuleBaseConf
    {
        public string? data_dir { get; set; }

        public string legacy_import_path { get; set; } = "";

        public bool enable_import { get; set; } = true;

        public bool enable_cleanup { get; set; } = true;

        public int max_active_devices_per_user { get; set; }

        public string mutations_api_secret { get; set; } = "";

        /// <summary>
        /// If true, the first successful <c>POST /tg/auth/bind/complete</c> for an unknown Telegram id
        /// creates a new row in users.json (closed registration when false).
        /// </summary>
        public bool auto_provision_users { get; set; }

        public string auto_provision_role { get; set; } = "user";

        public string auto_provision_lang { get; set; } = "ru";

        /// <summary>Access length for new users; 0 or unset = no expiry.</summary>
        public int auto_provision_expires_days { get; set; }
    }
}
