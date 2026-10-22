using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AdminPanel;

public static class ConfigSectionGroups
{
    public sealed record GroupSpec(string Id, string Title, string Hint, string[] Keys);

    public static readonly GroupSpec[] Catalog =
    {
        new("runtime", "Рантайм", "Поля снимка; обычно приходят из current, в init не обязательны.",
            new[] { "guid", "freeDiskSpace" }),
        new("listen", "HTTP-сервер (listen)", "Адрес, порт, схема, таймауты, compression.",
            new[] { "listen" }),
        new("security", "Безопасность и доступ", "WAF, accsdb, список модулей и middleware ядра.",
            new[] { "WAF", "accsdb", "BaseModule" }),
        new("network", "Сеть и прокси", "Прокси исходящих запросов, CORS, доверенные сети.",
            new[] { "serverproxy", "proxy", "globalproxy", "corsehost", "KnownProxies" }),

        new("pools", "Пулы и служебное", "Буферы, apn, kit.",
            new[] { "pool", "apn", "kit" }),
        new("cache-gc", "Кэш и память", "Гибридный кэш, Staticache, настройки GC.",
            new[] { "cache", "Staticache", "GC" }),
        new("media", "Изображения и постеры", "Движок картинок, Poster API.",
            new[] { "imagelibrary", "posterApi" }),

        new("realtime", "WebSocket и RCH", "Нативные сокеты и удалённый хаб.",
            new[] { "WebSocket", "rch" }),
        new("browser", "Браузеры (Playwright)", "Chromium / Firefox для автоматизации.",
            new[] { "chromium", "firefox" }),
        new("diagnostics", "Логи и диагностика", "Serilog, обработчик исключений, openstat.",
            new[] { "serilog", "useDeveloperExceptionPage", "exceptionHandlerLogTarget", "exceptionHandlerLogFile", "watcherInit", "openstat" }),

        new("app", "Приложение и оболочка", "online, cub, sisi, реклама, дефолты, omdb.",
            new[] { "online", "cub", "sisi", "vast", "disableEng", "defaultOn", "omdbapi_key", "overrideResponse" }),
        new("client", "Клиент Lampa и API", "Оболочка Lampa, cookie, PidTor, TMDB.",
            new[] { "tmdb", "LampaWeb", "Cookie", "PidTor" }),
        new("modules", "Модули расширения", "Секции подключаемых модулей в корне конфига.",
            new[] { "Catalog", "DLNA", "JacRed", "Sync", "TimeCode", "TorrServer", "Tracks", "transcoding", "TmdbProxy", "CubProxy", "WebLog" }),

        new("src-anime", "Источники · аниме", "Онлайн-балансеры аниме и смежные (в т.ч. Kodik).",
            new[] { "AniLiberty", "AniLibria", "Animebesst", "AnimeGo", "AnimeLib", "AnimeON", "Animevost", "AniMedia", "Dreamerscast", "Kodik", "Mikai", "MoonAnime" }),
        new("src-embed", "Источники · встраиваемые плееры", "Embed и агрегаторы сторонних плееров.",
            new[] { "Autoembed", "Hydraflix", "MovPI", "Playembed", "Rgshows", "Smashystream", "Twoembed", "VidLink", "Videasy", "Vidsrc" }),
        new("src-vod", "Источники · VOD и CDN", "Кино, сериалы, региональные и CDN-провайдеры.",
            new[]
            {
                "Aladin", "Alloha", "Ashdi", "AsiaGe", "BamBoo", "CDNvideohub", "Collaps", "Eneyida", "FanCDN", "Filmix", "FilmixPartner", "FilmixTV", "FlixCDN",
                "Geosaitebi", "GetsTV", "HDVB", "HdvbUA", "IptvOnline", "iRemux", "Kinobase", "Kinoflix", "Kinogo", "Kinotochka", "Kinoukr", "KinoPub",
                "LeProduction", "Mirage", "Rezka", "RezkaPrem", "RutubeMovie", "Tortuga", "UaKino", "VideoDB", "Videoseed", "VeoVeo", "Vibix", "VkMovie", "VoKino"
            }),
        new("src-adult", "Источники · 18+", "SISI / взрослые сайты.",
            new[]
            {
                "BongaCams", "Chaturbate", "Ebalovo", "Eporner", "HQporner", "PornHub", "PornHubPremium", "Porntrex", "Runetki", "Spankbang", "Tizam",
                "Xhamster", "Xnxx", "Xvideos", "XvideosRED"
            }),
    };

    public static List<GroupDto> Build(JObject currentRoot)
    {
        if (currentRoot == null)
            currentRoot = new JObject();

        var inFile = new HashSet<string>(
            currentRoot.Properties().Select(p => p.Name),
            StringComparer.Ordinal);

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GroupDto>();

        foreach (var g in Catalog)
        {
            var keys = g.Keys.Where(inFile.Contains).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            foreach (var k in keys)
                assigned.Add(k);

            if (keys.Length == 0)
                continue;

            result.Add(new GroupDto(g.Id, g.Title, g.Hint, keys));
        }

        var orphans = inFile.Where(k => !assigned.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (orphans.Length > 0)
            result.Add(new GroupDto("other", "Прочее", "Ключи из current.conf, не попавшие в каталог (новые модули).", orphans));

        return result;
    }

    public static List<GroupDto> BuildCatalog()
    {
        var list = new List<GroupDto>(Catalog.Length);
        foreach (var g in Catalog)
            list.Add(new GroupDto(g.Id, g.Title, g.Hint, g.Keys.ToArray()));
        return list;
    }

    public static HashSet<string> CatalogRootKeys { get; } = new(
        Catalog.SelectMany(g => g.Keys),
        StringComparer.Ordinal);
}

public sealed record GroupDto(string Id, string Title, string Hint, string[] Keys);
