using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Music;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    static readonly object wafRulesLock = new();
    static List<WafLimitRootMap> appliedWafRules = new();

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
        RemoveWafRules();
    }

    void updateConf()
    {
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

        ApplyWafRules(conf.limit_map);
        MusicProxyService.ConfigurationChanged();
    }

    static void ApplyWafRules(IReadOnlyCollection<WafLimitRootMap> currentRules)
    {
        var waf = CoreInit.conf?.WAF;
        if (waf == null)
            return;

        lock (wafRulesLock)
        {
            var limitMap = waf.limit_map?.ToList() ?? new List<WafLimitRootMap>();
            RemoveAppliedWafRules(limitMap);

            var nextAppliedRules = new List<WafLimitRootMap>();

            foreach (var rule in currentRules ?? Array.Empty<WafLimitRootMap>())
            {
                if (rule == null || (string.IsNullOrWhiteSpace(rule.path) && string.IsNullOrWhiteSpace(rule.pattern)))
                    continue;

                limitMap.Insert(0, rule);
                nextAppliedRules.Add(rule);
            }

            // WAF может одновременно обслуживать запросы: публикуем новый снимок
            // списка одной записью, не меняя коллекцию, которую он перечисляет.
            waf.limit_map = limitMap;
            appliedWafRules = nextAppliedRules;
        }
    }

    static void RemoveWafRules()
    {
        lock (wafRulesLock)
        {
            var waf = CoreInit.conf?.WAF;
            if (waf != null)
            {
                var limitMap = waf.limit_map?.ToList() ?? new List<WafLimitRootMap>();
                RemoveAppliedWafRules(limitMap);
                waf.limit_map = limitMap;
            }

            appliedWafRules = new List<WafLimitRootMap>();
        }
    }

    static void RemoveAppliedWafRules(List<WafLimitRootMap> limitMap)
    {
        if (limitMap == null || appliedWafRules.Count == 0)
            return;

        limitMap.RemoveAll(existing => appliedWafRules.Any(applied => ReferenceEquals(existing, applied)));
    }
}
