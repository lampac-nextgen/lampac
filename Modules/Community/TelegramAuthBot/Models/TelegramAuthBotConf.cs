namespace TelegramAuthBot.Models
{
    public class TelegramAuthBotConf
    {
        public bool enable { get; set; } = true;

        public string bot_token { get; set; } = "";

        public string lampac_base_url { get; set; } = "http://127.0.0.1:9118";

        public int request_timeout_sec { get; set; } = 10;

        public string service_display_name { get; set; } = "Lampac NextGen Bot";

        /// <summary>Cookie <c>accspasswd</c> для HTTP к Lampac — то же содержимое, что файл <c>passwd</c> на сервере (<c>CoreInit.rootPasswd</c>).</summary>
        public string lampac_accspasswd { get; set; } = "";

        public long[] admin_chat_ids { get; set; }

        public long[] owner_telegram_ids { get; set; }

        public bool notify_admins_on_pending_provision { get; set; } = true;
    }
}
