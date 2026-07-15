using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Music;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    public static string modpath;
    public static ModuleConf conf;

    public void Configure(ConfigureModel app)
    {
        app.services.AddDbContextFactory<MusicContext>(MusicContext.ConfiguringDbBuilder);
    }

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);

        Directory.CreateDirectory("database/music");
        MusicContext.Initialization(initspace.app.ApplicationServices);
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Music", new ModuleConf()
        {
            default_metadata_provider = "musicbrainz",
            default_audio_provider = "youtubeaudio",
            default_auth_provider = "",
            client_debug_enabled = false,
            youtube_audio_enabled = true,
            sefon_audio_enabled = true,
            soundcloud_enabled = true,
            soundcloud_discovery_enabled = true,
            soundcloud_audio_enabled = true,
            soundcloud_auth_enabled = false,
            soundcloud_client_id = "",
            soundcloud_client_secret = "",
            soundcloud_redirect_uri = "",
            soundcloud_country = "US",
            z3fm_enabled = false,
            z3fm_audio_enabled = false,
            z3fm_proxy_enabled = false,
            z3fm_proxy_url = "",
            z3fm_proxy_username = "",
            z3fm_proxy_password = "",
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/music", new WafLimitMap { limit = 15, second = 1 })
            }
        });
    }
}
