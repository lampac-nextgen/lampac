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
        if (initPlugins.dlna)
            AddPlugin(target, "dlna", "DLNA", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.tracks)
            AddPlugin(target, "tracks", "Tracks.js", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.transcoding)
            AddPlugin(target, "transcoding", "Transcoding video", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.tmdbProxy)
            AddPlugin(target, "tmdbproxy", "TMDB Proxy", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.cubProxy)
            AddPlugin(target, "cubproxy", "CUB Proxy", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.online)
            AddPlugin(target, "online", "Онлайн", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.watch_together)
            AddPlugin(target, "watchtogether", "Watch Together", useTokenRoutes, routeToken, worktoken: false);

        if (initPlugins.catalog)
            AddPlugin(target, "catalog", "Альтернативные источники каталога", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.dorama)
            AddPlugin(target, "dorama", "Дорамы", useTokenRoutes, routeToken, worktoken: true);

        if (adult && initPlugins.sisi)
        {
            AddPlugin(target, "sisi", "Клубничка", useTokenRoutes, routeToken, worktoken: true);
            AddPlugin(target, "startpage", "Стартовая страница", useTokenRoutes, routeToken, worktoken: false);
        }

        if (initPlugins.sync)
            AddPlugin(target, "sync", "Синхронизация", useTokenRoutes, routeToken, worktoken: true);

        if (!initPlugins.sync && initPlugins.timecode)
            AddPlugin(target, "timecode", "Синхронизация тайм-кодов", useTokenRoutes, routeToken, worktoken: true);

        if (!initPlugins.sync && initPlugins.bookmark)
            AddPlugin(target, "bookmark", "Синхронизация закладок", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.torrserver)
            AddPlugin(target, "ts", "TorrServer", useTokenRoutes, routeToken, worktoken: true);

        if (initPlugins.backup)
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
