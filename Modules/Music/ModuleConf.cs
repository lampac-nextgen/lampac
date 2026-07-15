using Shared.Models.AppConf;

namespace Music;

public class ModuleConf
{
    public string default_metadata_provider { get; set; }

    public string default_audio_provider { get; set; }

    public string default_auth_provider { get; set; }

    public bool client_debug_enabled { get; set; }

    public bool youtube_audio_enabled { get; set; }

    public bool sefon_audio_enabled { get; set; }

    public bool soundcloud_enabled { get; set; }

    public bool soundcloud_discovery_enabled { get; set; }

    public bool soundcloud_audio_enabled { get; set; }

    public bool soundcloud_auth_enabled { get; set; }

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
