using Shared.Models.AppConf;
using Shared.Models.Base;

namespace Music;

public class ModuleConf : Iproxy
{
    public bool useproxy { get; set; }

    public bool useproxystream { get; set; }

    public string globalnameproxy { get; set; }

    public ProxySettings proxy { get; set; }

    public string default_metadata_provider { get; set; }

    public string default_audio_provider { get; set; }

    public string default_auth_provider { get; set; }

    public bool client_debug_enabled { get; set; }

    public bool stats_clear_enabled { get; set; }

    // dev-кнопка «Обновить миксы недели» (пересборка раскладки по salt);
    // в обычном UX неделя обновляется сама по понедельникам
    public bool daily_reset_enabled { get; set; }

    public bool youtube_audio_enabled { get; set; }

    // Spotify-корректор поиска: когда MusicBrainz не понял «человеческий» ввод
    // (кириллица западного артиста, опечатка), спросить у Spotify каноническое
    // имя артиста и переспросить MB им. Хрупко к ротации persisted-хэша Spotify,
    // потому default off; деградирует в «как без него» без падений.
    public bool spotify_search_fallback_enabled { get; set; }

    public bool spotify_discovery_enabled { get; set; }

    public string spotify_country { get; set; }

    public bool sefon_audio_enabled { get; set; }

    public bool soundcloud_enabled { get; set; }

    public bool soundcloud_discovery_enabled { get; set; }

    public bool soundcloud_audio_enabled { get; set; }

    public bool soundcloud_auth_enabled { get; set; }

    public string applemusic_country { get; set; }

    // auto | applemusic | spotify | soundcloud | musicbrainz
    public string applemusic_album_resolver { get; set; }

    public string soundcloud_client_id { get; set; }

    public string soundcloud_client_secret { get; set; }

    public string soundcloud_redirect_uri { get; set; }

    public string soundcloud_country { get; set; }

    public bool z3fm_enabled { get; set; }

    public bool z3fm_audio_enabled { get; set; }

    public bool z3fm_proxy_enabled { get; set; }

    public string z3fm_proxy_url { get; set; }

    public string z3fm_proxy_username { get; set; }

    public string z3fm_proxy_password { get; set; }

    public List<WafLimitRootMap> limit_map { get; set; } = new();
}
