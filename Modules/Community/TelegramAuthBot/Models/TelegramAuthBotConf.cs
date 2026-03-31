namespace TelegramAuthBot.Models
{
    public class TelegramAuthBotConf
    {
        public bool enable { get; set; } = true;

        public string bot_token { get; set; } = "";

        public string lampac_base_url { get; set; } = "http://127.0.0.1:9118";

        public int request_timeout_sec { get; set; } = 10;

        public string service_display_name { get; set; } = "Lampac NextGen Bot";

        public string mutations_api_secret { get; set; } = "";

        public long[] admin_chat_ids { get; set; }
    }
}
