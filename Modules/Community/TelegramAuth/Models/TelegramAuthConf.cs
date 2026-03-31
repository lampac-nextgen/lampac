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
    }
}
