using Shared.Models.Module;

namespace TelegramBot.Models
{
    public class TelegramBotConf : ModuleBaseConf
    {
        public bool enable { get; set; } = true;

        public string bot_token { get; set; } = "YOUR_BOT_TOKEN";

        public string tmdb_api_key { get; set; } = "";

        public string trakt_client_id { get; set; } = "";

        public string lampac_host { get; set; } = "http://127.0.0.1:9118";

        public string lampac_token { get; set; } = "";

        // Ключ kinopoiskapiunofficial.tech — резолвинг tmdb→kinopoisk_id по названию для источника VideoHub.
        // Пусто → kp_id резолвится только через мост Lampac /externalids (покрытие ~40%).
        public string kp_api_key { get; set; } = "";

        public int check_interval_minutes { get; set; } = 60;

        public string tmdb_lang { get; set; } = "ru-RU";

        // Каталог для users.json / subscriptions.json. Пусто → database/tgnotify
        public string data_dir { get; set; } = "database/tgnotify";
    }
}
