using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace HQporner;

public static class HQpornerTo
{
    #region Uri
    public static string Uri(string host, string search, string sort, string c, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("?q=");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("&p=");
            url.Append(pg);
        }
        else
        {
            if (!string.IsNullOrEmpty(c))
            {
                url.Append("category/");
                url.Append(c);
            }
            else
            {
                if (!string.IsNullOrEmpty(sort))
                {
                    url.Append("top/");
                    url.Append(sort);
                }
                else
                    url.Append("hdporn");
            }

            url.Append("/");
            url.Append(pg);
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("<div class=\"img-container\">", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups("href=\"/([^\"]+)\" class=\"atfi[^\"]+\"><img src=\"//([^\"]+)\"[^>]+ alt=\"([^\"]+)\"");
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                var pl = new PlaylistItem()
                {
                    name = g[3].Value.Trim(),
                    video = $"{uri}?uri={g[1].Value}",
                    picture = "https://" + g[2].Value,
                    time = row.Match("class=\"fa fa-clock-o\" [^>]+></i>([\n\r\t ]+)?([^<]+)<", 2, trim: true),
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "hqr",
                        href = g[1].Value,
                        image = "https://" + g[2].Value
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
    public static List<MenuItem> Menu(string host, string sort, string c)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"HQporner_menu_{host}_{sort}_{c}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        string url = $"{host}/hqr";

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
                title = $"Сортировка: {(string.IsNullOrEmpty(sort) ? "новинки" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Самые новые", $"{url}?c={c}"),
                    new("Топ недели", $"{url}?c={c}&sort=week"),
                    new("Топ месяца", $"{url}?c={c}&sort=month")
                }
            });
        }

        var catmenu = new List<MenuItem>(65)
        {
            new("Все", $"{url}?sort={sort}"),
            new("1080p porn", $"{url}?sort={sort}&c=1080p-porn"),
            new("anal", $"{url}?sort={sort}&c=anal-sex-hd"),
            new("4k porn", $"{url}?sort={sort}&c=4k-porn"),
            new("milf", $"{url}?sort={sort}&c=milf"),
            new("lesbian", $"{url}?sort={sort}&c=lesbian"),
            new("60fps", $"{url}?sort={sort}&c=60fps-porn"),
            new("creampie", $"{url}?sort={sort}&c=creampie"),
            new("big tits", $"{url}?sort={sort}&c=big-tits"),
            new("teen porn", $"{url}?sort={sort}&c=teen-porn"),
            new("pov", $"{url}?sort={sort}&c=pov"),
            new("threesome", $"{url}?sort={sort}&c=threesome"),
            new("asian", $"{url}?sort={sort}&c=asian"),
            new("old and young", $"{url}?sort={sort}&c=old-and-young"),
            new("ebony", $"{url}?sort={sort}&c=ebony"),
            new("big ass", $"{url}?sort={sort}&c=big-ass"),
            new("interracial", $"{url}?sort={sort}&c=interracial"),
            new("squirt", $"{url}?sort={sort}&c=squirt"),
            new("mature", $"{url}?sort={sort}&c=mature"),
            new("sex massage", $"{url}?sort={sort}&c=porn-massage"),
            new("amateur", $"{url}?sort={sort}&c=amateur"),
            new("casting", $"{url}?sort={sort}&c=casting"),
            new("gangbang", $"{url}?sort={sort}&c=gangbang"),
            new("stockings", $"{url}?sort={sort}&c=stockings"),
            new("big dick", $"{url}?sort={sort}&c=big-dick"),
            new("babe", $"{url}?sort={sort}&c=babe"),
            new("latina", $"{url}?sort={sort}&c=latina"),
            new("group sex", $"{url}?sort={sort}&c=group-sex"),
            new("russian", $"{url}?sort={sort}&c=russian"),
            new("masturbation", $"{url}?sort={sort}&c=masturbation"),
            new("hairy pussy", $"{url}?sort={sort}&c=hairy-pussy"),
            new("uniforms", $"{url}?sort={sort}&c=uniforms"),
            new("shemale", $"{url}?sort={sort}&c=shemale"),
            new("blonde", $"{url}?sort={sort}&c=blonde"),
            new("orgasm", $"{url}?sort={sort}&c=orgasm"),
            new("pickup", $"{url}?sort={sort}&c=pickup"),
            new("sex party", $"{url}?sort={sort}&c=sex-parties"),
            new("bdsm", $"{url}?sort={sort}&c=bdsm"),
            new("public", $"{url}?sort={sort}&c=public"),
            new("japanese", $"{url}?sort={sort}&c=japanese-girls-porn"),
            new("redhead", $"{url}?sort={sort}&c=redhead"),
            new("orgy", $"{url}?sort={sort}&c=orgy"),
            new("blowjob", $"{url}?sort={sort}&c=blowjob"),
            new("fetish", $"{url}?sort={sort}&c=fetish"),
            new("brunette", $"{url}?sort={sort}&c=brunette"),
            new("small tits", $"{url}?sort={sort}&c=small-tits"),
            new("undressing", $"{url}?sort={sort}&c=undressing"),
            new("cumshot", $"{url}?sort={sort}&c=cumshot"),
            new("outdoor", $"{url}?sort={sort}&c=outdoor"),
            new("deepthroat", $"{url}?sort={sort}&c=deepthroat"),
            new("bondage", $"{url}?sort={sort}&c=bondage"),
            new("shaved pussy", $"{url}?sort={sort}&c=shaved-pussy"),
            new("bisexual", $"{url}?sort={sort}&c=bisexual"),
            new("hentai", $"{url}?sort={sort}&c=hentai"),
            new("handjob", $"{url}?sort={sort}&c=handjob"),
            new("pussy licking", $"{url}?sort={sort}&c=pussy-licking"),
            new("moaning", $"{url}?sort={sort}&c=moaning"),
            new("fisting", $"{url}?sort={sort}&c=fisting"),
            new("vintage", $"{url}?sort={sort}&c=vintage"),
            new("tattooed", $"{url}?sort={sort}&c=tattooed"),
            new("beach", $"{url}?sort={sort}&c=beach-porn"),
            new("vibrator", $"{url}?sort={sort}&c=vibrator"),
            new("fingering", $"{url}?sort={sort}&c=fingering"),
            new("squeezing tits", $"{url}?sort={sort}&c=squeezing-tits"),
            new("long hair", $"{url}?sort={sort}&c=long-hair")
        };

        menu.Add(new MenuItem()
        {
            title = $"Категория: {catmenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "все"}",
            playlist_url = "submenu",
            submenu = catmenu
        });

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    async public static Task<Dictionary<string, string>> StreamLinks(HttpHydra http, string host, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string uriframe = null;

        await http.GetSpan($"{host}/{uri}", html =>
        {
            uriframe = Rx.Match(html, "<iframe src=\"//([^/]+/video/[^/]+/)\"");
        });

        if (uriframe == null)
            return null;

        var stream_links = new Dictionary<string, string>(5);

        await http.GetSpan($"https://{uriframe}", iframeHtml =>
        {
            foreach (var row in Rx.Matches("src=.\"([^\"]+)\" title=.\"([^\"]+)\"", iframeHtml).Rows())
            {
                var g = row.Groups("src=.\"([^\"]+)\" title=.\"([^\"]+)\"");

                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !g[2].Value.Contains("Default"))
                    stream_links.TryAdd(g[2].Value.Replace("\\", ""), $"https:{g[1].Value.Replace("\\", "")}");
            }

            if (stream_links.Count == 0)
            {
                string jw = Rx.Match(iframeHtml, "\\$\\(\"#jw\"\\)([^;]+)");
                if (jw != null && jw.Contains("replaceAll"))
                {
                    var grpal = Rx.Groups(iframeHtml, "replaceAll\\(\"([^\"]+)\",([^\\+]+)\\+\"pubs/\"\\+([^\\+]+)");

                    string cdn = Rx.Match(iframeHtml, grpal[2].Value + "=\"([^\"]+)\"");
                    string hash = Rx.Match(iframeHtml, grpal[3].Value + "=\"([^\"]+)\"");

                    if (!string.IsNullOrEmpty(cdn) && !string.IsNullOrEmpty(hash))
                    {
                        foreach (var row in Rx.Matches("src=.?\"([^\"]+[0-9]+\\.mp4)\" title=.?\"([^\"]+)\"", iframeHtml).Rows())
                        {
                            var g = row.Groups("src=.?\"([^\"]+[0-9]+\\.mp4)\" title=.?\"([^\"]+)\"");

                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !g[2].Value.Contains("Default"))
                            {
                                string hls = g[1].Value.Replace(grpal[1].Value, $"https:{cdn}pubs/{hash}/");

                                if (hls.StartsWith("https:"))
                                    stream_links.TryAdd(g[2].Value.Replace("\\", ""), hls.Replace("\\", ""));
                            }
                        }
                    }
                }
            }
        });

        if (stream_links.Count == 0)
            return null;

        return stream_links.Reverse().ToDictionary(k => k.Key, v => v.Value);
    }
    #endregion
}
