using System;

namespace QRAuth.Models
{
    public class TelegramBotConf
    {
        public bool enable { get; set; } = true;

        public string bot_token { get; set; } = "";

        public string users_file_path { get; set; } = "users.json";

        /// <summary>Where this module appends its own log (grants, denies, errors) — separate
        /// from the host's own logging so it survives even if the host doesn't persist console output.</summary>
        public string log_path { get; set; } = "tgbot.log";

        /// <summary>Telegram ids allowed to grant/deny access requests.</summary>
        public long[] admin_ids { get; set; } = Array.Empty<long>();
    }
}
