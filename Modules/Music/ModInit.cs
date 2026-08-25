using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Music;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    static readonly object wafRulesLock = new();

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

        Directory.CreateDirectory("database/music");
        MusicContext.Initialization(initspace.app.ApplicationServices);
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        var previousLimitMap = conf?.limit_map?.ToList() ?? new List<WafLimitRootMap>();

        conf = ModuleInvoke.Init("Music", new ModuleConf()
        {
            useproxy = false,
            useproxystream = false,
            globalnameproxy = null,
            proxy = null,
            default_metadata_provider = "musicbrainz",
            default_audio_provider = "youtubeaudio",
            default_auth_provider = "",
            client_debug_enabled = false,
            stats_clear_enabled = false,
            daily_reset_enabled = false,
            youtube_audio_enabled = true,
            spotify_search_fallback_enabled = false,
            spotify_discovery_enabled = true,
            spotify_country = "us",
            sefon_audio_enabled = true,
            soundcloud_enabled = true,
            soundcloud_discovery_enabled = true,
            soundcloud_audio_enabled = true,
            soundcloud_auth_enabled = false,
            applemusic_country = "us",
            applemusic_album_resolver = "auto",
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

        ApplyWafRules(previousLimitMap, conf.limit_map);
        MusicProxyService.ConfigurationChanged();
    }

    static void ApplyWafRules(IReadOnlyCollection<WafLimitRootMap> previousRules, IReadOnlyCollection<WafLimitRootMap> currentRules)
    {
        var waf = CoreInit.conf?.WAF;
        if (waf == null)
            return;

        lock (wafRulesLock)
        {
            var limitMap = waf.limit_map?.ToList() ?? new List<WafLimitRootMap>();

            foreach (var rule in previousRules ?? Array.Empty<WafLimitRootMap>())
                limitMap.RemoveAll(existing => SameWafRoot(existing, rule));

            foreach (var rule in currentRules ?? Array.Empty<WafLimitRootMap>())
            {
                if (rule == null || (string.IsNullOrWhiteSpace(rule.path) && string.IsNullOrWhiteSpace(rule.pattern)))
                    continue;

                limitMap.RemoveAll(existing => SameWafRoot(existing, rule));
                limitMap.Insert(0, rule);
            }

            // WAF может одновременно обслуживать запросы: публикуем новый снимок
            // списка одной записью, не меняя коллекцию, которую он перечисляет.
            waf.limit_map = limitMap;
        }
    }

    static bool SameWafRoot(WafLimitRootMap left, WafLimitRootMap right)
    {
        if (left == null || right == null)
            return false;

        if (!string.IsNullOrWhiteSpace(right.path))
            return !string.IsNullOrWhiteSpace(left.path)
                && string.Equals(left.path, right.path, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrWhiteSpace(right.pattern)
            && !string.IsNullOrWhiteSpace(left.pattern)
            && string.Equals(left.pattern, right.pattern, StringComparison.OrdinalIgnoreCase);
    }
}
