using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.HTML;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Eporner;

public static class EpornerTo
{
    #region Uri
    public static string Uri(string host, string search, string sort, string c, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");

            if (pg > 1)
            {
                url.Append(pg);
                url.Append("/");
            }

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append(sort);
                url.Append("/");
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(c))
            {
                url.Append("cat/");
                url.Append(c);
                url.Append("/");

                if (pg > 1)
                {
                    url.Append(pg);
                    url.Append("/");
                }
            }
            else
            {
                if (pg > 1)
                {
                    url.Append(pg);
                    url.Append("/");
                }

                if (!string.IsNullOrEmpty(sort))
                {
                    url.Append(sort);
                    url.Append("/");
                }
            }
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var single = ReadOnlySpan<char>.Empty;

        if (html.Contains("id=\"relateddiv\"", StringComparison.Ordinal))
            single = HtmlSpan.Node(html, "*", "id", "relateddiv", HtmlSpanTargetType.Exact);

        else if (html.Contains("id=\"vidresults\"", StringComparison.Ordinal))
            single = HtmlSpan.Node(html, "*", "id", "vidresults", HtmlSpanTargetType.Exact);

        else if (html.Contains("class=\"toptopbelinset\"", StringComparison.Ordinal))
            single = Rx.Split("class=\"toptopbelinset\"", html)[1].Span;

        else if (html.Contains("class=\"relatedtext\"", StringComparison.Ordinal))
            single = Rx.Split("class=\"relatedtext\"", html)[1].Span;

        if (single.IsEmpty)
            return null;

        var rx = Rx.Split("<div class=\"mb( hdy)?\"", single, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups("<p class=\"mbtit\"><a href=\"/([^\"]+)\">([^<]+)</a>");

            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                string img = row.Match(" data-src=\"([^\"]+)\"");
                if (img == null)
                    img = row.Match("<img src=\"([^\"]+)\"");

                if (img == null)
                    img = string.Empty;

                var pl = new PlaylistItem()
                {
                    name = g[2].Value,
                    video = $"{route}?uri={g[1].Value}",
                    picture = img,
                    preview = Regex.Replace(img, "/[^/]+$", "") + $"/{row.Match("data-id=\"([^\"]+)\"")}-preview.webm",
                    quality = row.Match("<div class=\"mvhdico\"([^>]+)?><span>([^\"<]+)", 2),
                    time = row.Match("<span class=\"mbtim\"([^>]+)?>([^<]+)</span>", 2, trim: true),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "epr",
                        href = g[1].Value,
                        image = img
                    }
                };

                if (onplaylist != null)
                    pl = onplaylist.Invoke(pl);

                playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string search, string sort, string c)
    {
        host = string.IsNullOrWhiteSpace(host) ? string.Empty : $"{host}/";
        string url = host + "epr";

        #region search menu
        if (!string.IsNullOrEmpty(search))
        {
            string encodesearch = HttpUtility.UrlEncode(search);

            return new List<MenuItem>(2)
            {
                new MenuItem()
                {
                    title = "Поиск",
                    search_on = "search_on",
                    playlist_url = url,
                },
                new MenuItem()
                {
                    title = $"Сортировка: {(string.IsNullOrWhiteSpace(sort) ? "новинки" : sort)}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(5)
                    {
                        new("Новинки", $"{url}?search={encodesearch}"),
                        new("Топ просмотра", $"{url}?sort=most-viewed&search={encodesearch}"),
                        new("Топ рейтинга", $"{url}?sort=top-rated&search={encodesearch}"),
                        new("Длинные ролики", $"{url}?sort=longest&search={encodesearch}"),
                        new("Короткие ролики", $"{url}?sort=shortest&search={encodesearch}")
                    }
                }
            };
        }
        #endregion

        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Eporner_menu_{host}_{sort}_{c}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(3)
        {
            new MenuItem()
            {
                title = "Поиск",
                search_on = "search_on",
                playlist_url = url,
            }
        };

        if (string.IsNullOrEmpty(c))
        {
            menu.Add(new MenuItem()
            {
                title = $"Сортировка: {(string.IsNullOrWhiteSpace(sort) ? "новинки" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(5)
                {
                    new("Новинки", url),
                    new("Топ просмотра", $"{url}?sort=most-viewed"),
                    new("Топ рейтинга", $"{url}?sort=top-rated"),
                    new("Длинные ролики", $"{url}?sort=longest"),
                    new("Короткие ролики", $"{url}?sort=shortest")
                }
            });
        }

        var catmenu = new List<MenuItem>(70)
        {
            new("Все", url),
            new("4K UHD", $"{url}?c=4k-porn"),
            new("60 FPS", $"{url}?c=60fps"),
            new("Amateur", $"{url}?c=amateur"),
            new("Anal", $"{url}?c=anal"),
            new("Asian", $"{url}?c=asian"),
            new("ASMR", $"{url}?c=asmr"),
            new("BBW", $"{url}?c=bbw"),
            new("BDSM", $"{url}?c=bdsm"),
            new("Big Ass", $"{url}?c=big-ass"),
            new("Big Dick", $"{url}?c=big-dick"),
            new("Big Tits", $"{url}?c=big-tits"),
            new("Bisexual", $"{url}?c=bisexual"),
            new("Blonde", $"{url}?c=blonde"),
            new("Blowjob", $"{url}?c=blowjob"),
            new("Bondage", $"{url}?c=bondage"),
            new("Brunette", $"{url}?c=brunette"),
            new("Bukkake", $"{url}?c=bukkake"),
            new("Creampie", $"{url}?c=creampie"),
            new("Cumshot", $"{url}?c=cumshot"),
            new("Double Penetration", $"{url}?c=double-penetration"),
            new("Ebony", $"{url}?c=ebony"),
            new("Fat", $"{url}?c=fat"),
            new("Fetish", $"{url}?c=fetish"),
            new("Fisting", $"{url}?c=fisting"),
            new("Footjob", $"{url}?c=footjob"),
            new("For Women", $"{url}?c=for-women"),
            new("Gay", $"{url}?c=gay"),
            new("Group Sex", $"{url}?c=group-sex"),
            new("Handjob", $"{url}?c=handjob"),
            new("Hardcore", $"{url}?c=hardcore"),
            new("Hentai", $"{url}?c=hentai"),
            new("Homemade", $"{url}?c=homemade"),
            new("Hotel", $"{url}?c=hotel"),
            new("Housewives", $"{url}?c=housewives"),
            new("Indian", $"{url}?c=indian"),
            new("Interracial", $"{url}?c=interracial"),
            new("Japanese", $"{url}?c=japanese"),
            new("Latina", $"{url}?c=latina"),
            new("Lesbian", $"{url}?c=lesbians"),
            new("Lingerie", $"{url}?c=lingerie"),
            new("Massage", $"{url}?c=massage"),
            new("Masturbation", $"{url}?c=masturbation"),
            new("Mature", $"{url}?c=mature"),
            new("MILF", $"{url}?c=milf"),
            new("Nurses", $"{url}?c=nurse"),
            new("Office", $"{url}?c=office"),
            new("Older Men", $"{url}?c=old-man"),
            new("Orgy", $"{url}?c=orgy"),
            new("Outdoor", $"{url}?c=outdoor"),
            new("Petite", $"{url}?c=petite"),
            new("Pornstar", $"{url}?c=pornstar"),
            new("POV", $"{url}?c=pov-porn"),
            new("Public", $"{url}?c=public"),
            new("Redhead", $"{url}?c=redhead"),
            new("Shemale", $"{url}?c=shemale"),
            new("Sleep", $"{url}?c=sleep"),
            new("Small Tits", $"{url}?c=small-tits"),
            new("Squirt", $"{url}?c=squirt"),
            new("Striptease", $"{url}?c=striptease"),
            new("Students", $"{url}?c=students"),
            new("Swinger", $"{url}?c=swingers"),
            new("Teen", $"{url}?c=teens"),
            new("Threesome", $"{url}?c=threesome"),
            new("Toys", $"{url}?c=toys"),
            new("Uncategorized", $"{url}?c=uncategorized"),
            new("Uniform", $"{url}?c=uniform"),
            new("Vintage", $"{url}?c=vintage"),
            new("Webcam", $"{url}?c=webcam")
        };

        menu.Add(new MenuItem()
        {
            title = $"Категория: {catmenu.FirstOrDefault(i => i.playlist_url.EndsWith($"c={c}"))?.title ?? "все"}",
            playlist_url = "submenu",
            submenu = catmenu
        });

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    async public static Task<StreamItem> StreamLinks(HttpHydra http, string route, string host, string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        string vid = null, hash = null;
        List<PlaylistItem> recomends = null;

        await http.GetSpan($"{host}/{url}", html =>
        {
            vid = Rx.Match(html, "vid ?= ?'([^']+)'");
            hash = Rx.Match(html, "hash ?= ?'([^']+)'");

            if (vid != null && hash != null)
                recomends = Playlist(route, html);
        });

        if (vid == null || hash == null)
            return null;

        var stream_links = new Dictionary<string, string>(5);

        await http.GetSpan($"{host}/xhr/video/{vid}?hash={convertHash(hash)}&domain={Regex.Replace(host, "^https?://", "")}&fallback=false&embed=false&supportedFormats=dash,mp4&_={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", json =>
        {
            foreach (var row in Rx.Matches("\"src\":( +)?\"(https?://[^/]+/[^\"]+-([0-9]+p).mp4)\",", json).Rows())
            {
                var g = row.Groups("\"src\":( +)?\"(https?://[^/]+/[^\"]+-([0-9]+p).mp4)\",");

                if (!string.IsNullOrEmpty(g[3].Value))
                    stream_links.TryAdd(g[3].Value, g[2].Value);
            }
        });

        return new StreamItem()
        {
            qualitys = stream_links,
            recomends = recomends
        };
    }
    #endregion


    #region convertHash
    static string convertHash(string h)
    {
        var builder = StringBuilderPool.ThreadInstance;

        Base36(h.AsSpan(0, 8), builder);
        Base36(h.AsSpan(8, 8), builder);
        Base36(h.AsSpan(16, 8), builder);
        Base36(h.AsSpan(24, 8), builder);

        return builder.ToString();
    }
    #endregion

    #region Base36
    static void Base36(ReadOnlySpan<char> hex, StringBuilder builder)
    {
        // Парсинг 8 hex-символов без Substring и без аллокаций
        ulong value = ulong.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

        const int Base = 36;
        const string Chars = "0123456789abcdefghijklmnopqrstuvwxyz";

        int start = builder.Length;

        if (value == 0)
        {
            builder.Append('0');
            return;
        }

        while (value > 0)
        {
            builder.Append(Chars[(int)(value % Base)]);
            value /= Base;
        }

        // Разворот добавленного участка (т.к. цифры добавлялись в обратном порядке)
        int i = start;
        int j = builder.Length - 1;
        while (i < j)
        {
            (builder[i], builder[j]) = (builder[j], builder[i]);
            i++;
            j--;
        }
    }
    #endregion
}
