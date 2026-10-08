using Shared;
using System.Collections.Generic;
using System.Web;

namespace LampaWeb;

public static class LampaPluginBuilder
{
    public static List<LampaPlugin> BuildInitPlugins(InitPlugins initPlugins, List<LampaPlugin> customPlugins, bool adult = true)
    {
        var plugins = new List<LampaPlugin>(20);
        AppendEnabledPlugins(plugins, initPlugins, customPlugins, adult, useTokenRoutes: false, routeToken: null);
        return plugins;
    }

    public static List<string> BuildOnPluginUrls(InitPlugins initPlugins, List<LampaPlugin> customPlugins, string routeToken, bool adult = true)
    {
        var urlStrings = new List<string>(20);
        AppendEnabledPlugins(urlStrings, initPlugins, customPlugins, adult, useTokenRoutes: true, routeToken: routeToken);
        return urlStrings;
    }

    static void AppendEnabledPlugins<T>(
        List<T> target,
        InitPlugins initPlugins,
        List<LampaPlugin> customPlugins,
        bool adult,
        bool useTokenRoutes,
        string routeToken)
    {
        if (initPlugins.dlna && ModuleLoaded("DLNA"))
            AddPlugin(target, "dlna", "DLNA", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.tracks && ModuleLoaded("Tracks"))
            AddPlugin(target, "tracks", "Tracks.js", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.transcoding && ModuleLoaded("Transcoding"))
            AddPlugin(target, "transcoding", "Transcoding video", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.tmdbProxy && ModuleLoaded("TmdbProxy"))
            AddPlugin(target, "tmdbproxy", "TMDB Proxy", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.cubProxy && ModuleLoaded("CubProxy"))
            AddPlugin(target, "cubproxy", "CUB Proxy", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.online && ModuleLoaded("Online"))
            AddPlugin(target, "online", "Онлайн", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.watch_together && ModuleLoaded("WatchTogether"))
            AddPlugin(target, "watchtogether", "Watch Together", useTokenRoutes, routeToken, worktoken: false);

        if (initPlugins.catalog && ModuleLoaded("Catalog"))
            AddPlugin(target, "catalog", "Альтернативные источники каталога", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.dorama)
            AddPlugin(target, "dorama", "Дорамы", useTokenRoutes, routeToken, worktoken: true);

        if (adult && initPlugins.sisi && ModuleLoaded("SISI"))
        {
            AddPlugin(target, "sisi", "Клубничка", useTokenRoutes, routeToken, worktoken: true);
            AddPlugin(target, "startpage", "Стартовая страница", useTokenRoutes, routeToken, worktoken: false);
        }

        bool sync = initPlugins.sync && ModuleLoaded("Sync");

        if (sync)
            AddPlugin(target, "sync", "Синхронизация", useTokenRoutes, routeToken, worktoken: true);

        if (!sync && initPlugins.timecode && ModuleLoaded("TimeCode"))
            AddPlugin(target, "timecode", "Синхронизация тайм-кодов", useTokenRoutes, routeToken, worktoken: true);

        if (!sync && initPlugins.bookmark && ModuleLoaded("Sync"))
            AddPlugin(target, "bookmark", "Синхронизация закладок", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.torrserver && ModuleLoaded("TorrServer"))
            AddPlugin(target, "ts", "TorrServer", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.backup && ModuleLoaded("Storage"))
            AddPlugin(target, "backup", "Backup", useTokenRoutes, routeToken, worktoken: true);

        if (customPlugins == null)
            return;

        foreach (var p in customPlugins)
        {
            if (p.status != 1)
                continue;

            if (target is List<LampaPlugin> pluginList)
                pluginList.Add(p);
            else if (target is List<string> urlList)
                urlList.Add($"\"{p.url}\"");
        }
    }

    // Precompiled modules from mods/*.dll are registered as "Name.dll" without enable set.
    internal static bool ModuleLoaded(string name)
        => CoreInit.modules?.Exists(m => m?.assembly != null && (m.name == name || m.name == name + ".dll")) == true;

    static void AddPlugin<T>(
        List<T> target,
        string name,
        string title,
        bool useTokenRoutes,
        string routeToken,
        bool worktoken)
    {
        if (target is List<LampaPlugin> pluginList)
        {
            pluginList.Add(new LampaPlugin($"{{localhost}}/{name}.js", 1, title, "lampac"));
            return;
        }

        if (target is List<string> urlList)
        {
            if (useTokenRoutes && worktoken && !string.IsNullOrEmpty(routeToken))
                urlList.Add($"\"{{localhost}}/{name}/js/{HttpUtility.UrlEncode(routeToken)}\"");
            else
                urlList.Add($"\"{{localhost}}/{name}.js\"");
        }
    }
}
