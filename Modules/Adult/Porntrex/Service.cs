using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Porntrex;

public static class PorntrexTo
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

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append(sort);
                url.Append("/");
            }

            url.Append("?from_videos=");
            url.Append(pg);
        }
        else
        {
            if (!string.IsNullOrEmpty(c))
            {
                url.Append("categories/");
                url.Append(c);
                url.Append("/");

                if (sort == "most-popular")
                    url.Append("top-rated/");

                url.Append("?from4=");
                url.Append(pg);
            }
            else
            {
                if (string.IsNullOrEmpty(sort))
                {
                    url.Append("latest-updates/");
                    url.Append(pg);
                    url.Append("/");
                }
                else
                {
                    url.Append(sort);
                    url.Append("/weekly/?from4=");
                    url.Append(pg);
                }
            }
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("<div class=\"video-preview-screen", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (row.Contains("<span class=\"line-private\">"))
                continue;

            var g = row.Groups("<a href=\"https?://[^/]+/(video/[^\"]+)\" title=\"([^\"]+)\"");

            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                var img = row.Groups("data-src=\"(https?:)?//(((ptx|statics)\\.cdntrex\\.com/contents/videos_screenshots/[0-9]+/[0-9]+)[^\"]+)");

                var pl = new PlaylistItem()
                {
                    video = $"{uri}?uri={g[1].Value}",
                    name = g[2].Value,
                    picture = $"https://{img[2].Value}",
                    quality = row.Match("<span class=\"quality\">([^<]+)</span>"),
                    time = row.Match("<i class=\"fa fa-clock-o\"></i>([^<]+)</div>", trim: true),
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "ptx",
                        href = g[1].Value,
                        image = $"https://{img[2].Value}"
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
        string url = $"{host}/ptx";

        #region search menu
        if (!string.IsNullOrEmpty(search))
        {
            string encodesearch = HttpUtility.UrlEncode(search);

            return new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Поиск",
                    search_on = "search_on",
                    playlist_url = url,
                },
                new MenuItem()
                {
                    title = $"Сортировка: {(string.IsNullOrEmpty(sort) ? "Most Relevant" : sort)}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>()
                    {
                        new MenuItem()
                        {
                            title = "Most Relevant",
                            playlist_url = $"{url}?c={c}&search={encodesearch}"
                        },
                        new MenuItem()
                        {
                            title = "Новинки",
                            playlist_url = $"{url}?c={c}&sort=latest-updates&search={encodesearch}"
                        },
                        new MenuItem()
                        {
                            title = "Топ просмотров",
                            playlist_url = $"{url}?c={c}&sort=most-popular&search={encodesearch}"
                        }
                    }
                }
            };
        }
        #endregion

        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Porntrex_menu_{host}_{sort}_{c}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(3)
        {
            new MenuItem()
            {
                title = "Поиск",
                search_on = "search_on",
                playlist_url = url,
            },
            new MenuItem()
            {
                title = $"Сортировка: {(string.IsNullOrEmpty(sort) ? "новинки" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(2)
                {
                    new("Новинки", $"{url}?c={c}"),
                    new("Топ просмотров", $"{url}?c={c}&sort=most-popular")
                }
            }
        };

        var catmenu = new List<MenuItem>(90)
        {
            new("Все", $"{url}?sort={sort}"),
            new("4K UHD", $"{url}?sort={sort}&c=4k-porn"),
            new("Anal", $"{url}?sort={sort}&c=anal"),
            new("Arab", $"{url}?sort={sort}&c=arab"),
            new("Asian", $"{url}?sort={sort}&c=asian"),
            new("Ass licking", $"{url}?sort={sort}&c=ass-licking"),
            new("Ass to mouth (ATM)", $"{url}?sort={sort}&c=ass-to-mouth"),
            new("Babe", $"{url}?sort={sort}&c=babe"),
            new("Babysitter", $"{url}?sort={sort}&c=babysitter"),
            new("BBW", $"{url}?sort={sort}&c=bbw"),
            new("Big Ass", $"{url}?sort={sort}&c=big-ass"),
            new("Big Tits", $"{url}?sort={sort}&c=big-tits"),
            new("Black", $"{url}?sort={sort}&c=black"),
            new("Blonde", $"{url}?sort={sort}&c=blonde"),
            new("Blowjob", $"{url}?sort={sort}&c=blowjob"),
            new("Bondage", $"{url}?sort={sort}&c=bondage"),
            new("Brunette", $"{url}?sort={sort}&c=brunette"),
            new("Bukkake", $"{url}?sort={sort}&c=bukkake"),
            new("Busty", $"{url}?sort={sort}&c=busty"),
            new("Casting", $"{url}?sort={sort}&c=casting"),
            new("Celebrities", $"{url}?sort={sort}&c=celebrities"),
            new("College", $"{url}?sort={sort}&c=college"),
            new("Compilation", $"{url}?sort={sort}&c=compilation"),
            new("Creampie", $"{url}?sort={sort}&c=creampie"),
            new("Cuckold", $"{url}?sort={sort}&c=cuckold"),
            new("Cum-swap", $"{url}?sort={sort}&c=cum-swapping"),
            new("Cumshots", $"{url}?sort={sort}&c=cumshots"),
            new("Czech", $"{url}?sort={sort}&c=czech"),
            new("Czech Massage", $"{url}?sort={sort}&c=czech-massage"),
            new("Deepthroat", $"{url}?sort={sort}&c=deepthroat"),
            new("Doggystyle", $"{url}?sort={sort}&c=doggystyle"),
            new("Double Penetration (DP)", $"{url}?sort={sort}&c=double-penetration"),
            new("Ebony", $"{url}?sort={sort}&c=ebony"),
            new("Fantasy", $"{url}?sort={sort}&c=fantasy"),
            new("Fetish", $"{url}?sort={sort}&c=fetish"),
            new("Fingering", $"{url}?sort={sort}&c=fingering"),
            new("Fisting", $"{url}?sort={sort}&c=fisting"),
            new("Footjob", $"{url}?sort={sort}&c=footjob"),
            new("Foursome", $"{url}?sort={sort}&c=foursome"),
            new("Gangbang", $"{url}?sort={sort}&c=gangbang"),
            new("Gangbang Creampie", $"{url}?sort={sort}&c=gangbang-creampie"),
            new("Gaping", $"{url}?sort={sort}&c=gaping"),
            new("Gay", $"{url}?sort={sort}&c=gay"),
            new("German", $"{url}?sort={sort}&c=german"),
            new("Gloryhole", $"{url}?sort={sort}&c=gloryhole"),
            new("Hairy", $"{url}?sort={sort}&c=hairy"),
            new("Handjob", $"{url}?sort={sort}&c=handjob"),
            new("Hardcore", $"{url}?sort={sort}&c=hardcore"),
            new("Hentai", $"{url}?sort={sort}&c=hentai"),
            new("Homemade", $"{url}?sort={sort}&c=homemade"),
            new("Hungarian", $"{url}?sort={sort}&c=hungarian"),
            new("Indian", $"{url}?sort={sort}&c=indian"),
            new("Interracial", $"{url}?sort={sort}&c=interracial"),
            new("Japanese", $"{url}?sort={sort}&c=japanese"),
            new("Latina", $"{url}?sort={sort}&c=latina"),
            new("Lesbian", $"{url}?sort={sort}&c=lesbian"),
            new("Lingerie", $"{url}?sort={sort}&c=lingerie"),
            new("Massage", $"{url}?sort={sort}&c=massage"),
            new("Masturbation", $"{url}?sort={sort}&c=masturbation"),
            new("Mature", $"{url}?sort={sort}&c=mature"),
            new("Milf", $"{url}?sort={sort}&c=milf"),
            new("Office", $"{url}?sort={sort}&c=office"),
            new("Old and Young", $"{url}?sort={sort}&c=old-and-young"),
            new("Orgy", $"{url}?sort={sort}&c=orgy"),
            new("Outdoor", $"{url}?sort={sort}&c=outdoor"),
            new("Petite", $"{url}?sort={sort}&c=petite"),
            new("POV", $"{url}?sort={sort}&c=pov"),
            new("Public", $"{url}?sort={sort}&c=public"),
            new("Pussy licking", $"{url}?sort={sort}&c=pussy-licking"),
            new("Red Head", $"{url}?sort={sort}&c=red-head"),
            new("Riding", $"{url}?sort={sort}&c=riding"),
            new("Russian", $"{url}?sort={sort}&c=russian"),
            new("School Girl", $"{url}?sort={sort}&c=school-girl"),
            new("Shemale", $"{url}?sort={sort}&c=shemale"),
            new("Skinny", $"{url}?sort={sort}&c=skinny"),
            new("Small tits", $"{url}?sort={sort}&c=small-tits"),
            new("Solo", $"{url}?sort={sort}&c=solo"),
            new("Squirt", $"{url}?sort={sort}&c=squirt"),
            new("Strap-on", $"{url}?sort={sort}&c=strap-on"),
            new("Swallow", $"{url}?sort={sort}&c=swallow"),
            new("Teen", $"{url}?sort={sort}&c=teen"),
            new("Threesome", $"{url}?sort={sort}&c=threesome"),
            new("Titfuck", $"{url}?sort={sort}&c=titfuck"),
            new("Toys", $"{url}?sort={sort}&c=toys"),
            new("Uniform", $"{url}?sort={sort}&c=uniform"),
            new("Vintage", $"{url}?sort={sort}&c=vintage"),
            new("Webcam", $"{url}?sort={sort}&c=webcam"),
            new("Wife", $"{url}?sort={sort}&c=wife")
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
    public static string StreamLinksUri(string host, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        return $"{host}/{uri}";
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Matches("(https?://[^/]+/get_file/[^\\.]+_([0-9]+p)\\.mp4)", html);

        var stream_links = new Dictionary<string, string>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups();
            if (!string.IsNullOrEmpty(g[1].Value))
                stream_links.TryAdd(g[2].Value, g[1].Value);
        }

        if (stream_links.Count == 0)
        {
            string link = Rx.Match(html, "(https?://[^/]+/get_file/[^\\.]+\\.mp4)");
            if (link != null)
                stream_links.TryAdd("auto", link);
        }

        return stream_links.Reverse().ToDictionary(k => k.Key, v => v.Value);
    }
    #endregion
}
