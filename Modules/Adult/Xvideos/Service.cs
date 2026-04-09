using Microsoft.Extensions.Caching.Memory;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using Shared.Services.Hybrid;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;
using System.Web;

namespace Xvideos
{
    public static class XvideosTo
    {
        #region Uri
        public static string Uri(string host, string plugin, string search, string sort, string c, int pg)
        {
            var url = StringBuilderPool.ThreadInstance;

            if (!string.IsNullOrWhiteSpace(search))
            {
                url.Append(host);
                url.Append("/?k=");
                url.Append(HttpUtility.UrlEncode(search));
                url.Append("&p=");
                url.Append(pg);
            }
            else
            {
                if (!string.IsNullOrEmpty(c))
                {
                    url.Append($"{host}/c/s:{(sort == "top" ? "rating" : "uploaddate")}/{c}/{pg}");
                }
                else
                {
                    if (sort == "top")
                    {
                        url.Append($"{host}/{(plugin == "xdsgay" ? "best-of-gay" : plugin == "xdssml" ? "best-of-shemale" : "best")}/{DateTime.Today.AddMonths(-1):yyyy-MM}");
                    }
                    else
                    {
                        url.Append(plugin == "xdsgay" ? $"{host}/gay" : plugin == "xdssml" ? $"{host}/shemale" : $"{host}/new");
                    }

                    url.Append($"/{pg}");
                }
            }

            return url.ToString();
        }
        #endregion

        #region Playlist
        public static List<PlaylistItem> Playlist(string route, string uri_star, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null, string site = "xds")
        {
            if (html.IsEmpty)
                return null;

            var rx = Rx.Split("<div id=\"video_", html, 1);
            if (rx.Count == 0)
                return null;

            var playlists = new List<PlaylistItem>(rx.Count);

            foreach (var row in rx.Rows())
            {
                // <a href="/video.ucmdacd450a/_" title="Горничная приходит на работу в коротком платье">
                var g = row.Groups("<a href=\"/(video[^\"]+|search-video/[^\"]+)\" title=\"([^\"]+)\"");
                if (string.IsNullOrEmpty(g[1].Value) || string.IsNullOrEmpty(g[2].Value))
                {
                    // <a href="/video.ohpbioo5118/_." target="_blank">Я думал, что не переживу его наказания.</a>
                    g = row.Groups("<a href=\"\\/(video[^\"]+)\"[^>]+>([^<]+)");
                }

                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    string img = row.Match("data-src=\"([^\"]+)\"") ?? string.Empty;
                    img = Regex.Replace(img, "/videos/thumbs([0-9]+)/", "/videos/thumbs$1lll/");
                    img = Regex.Replace(img, "THUMBNUM", "1", RegexOptions.IgnoreCase);

                    string preview = Regex.Replace(img, "/thumbs[^/]+/", "/") ?? string.Empty;  
                    preview = Regex.Replace(preview, "/[^/]+$", "/3/preview.mp4");

                    img = img.Replace("thumbs169l/", "thumbs169lll/").Replace("thumbs169ll/", "thumbs169lll/");

                    var gm = row.Groups("href=\"/([^\"]+)\"><span class=\"name\">([^<]+)<");
                    var model = string.IsNullOrEmpty(gm[1].Value) || string.IsNullOrEmpty(gm[2].Value) ? default : new ModelItem()
                    {
                        name = gm[2].Value,
                        uri = $"{uri_star}?uri=" + (gm[1].Value.Contains("/") ? gm[1].Value : $"channels/{gm[1].Value}"),
                    };

                    var pl = new PlaylistItem()
                    {
                        name = g[2].Value,
                        video = $"{route}?uri={g[1].Value}",
                        picture = img,
                        preview = preview,
                        quality = row.Match("<span class=\"video-hd-mark\">([^<]+)</span>"),
                        time = row.Match("<span class=\"duration\">([^<]+)</span>", trim: true),
                        json = true,
                        related = true,
                        model = model,
                        bookmark = new Bookmark()
                        {
                            site = site,
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

        #region Pornstars
        async public static Task<List<PlaylistItem>> Pornstars(string uri_video, string uri_star, string host, string plugin, string uri, string sort, int pg, Func<string, Task<PornstarsRoot>> onresult)
        {
            if (string.IsNullOrEmpty(uri))
                return null;

            sort = string.IsNullOrEmpty(sort) ? "new" : sort;
            string url = plugin == "xdsgay" ? $"{host}/{uri}/videos/{sort}/gay" : plugin == "xdssml" ? $"{host}/{uri}/videos/{sort}/shemale" : $"{host}/{uri}/videos/{sort}";

            url += $"/{pg}";

            PornstarsRoot root = await onresult.Invoke(url);
            if (root?.videos == null)
                return null;

            try
            {
                var videos = root.videos;
                if (videos == null)
                    return null;

                var playlists = new List<PlaylistItem>(videos.Count);

                foreach (var r in videos)
                {
                    if (string.IsNullOrEmpty(r.tf) || string.IsNullOrEmpty(r.u))
                        continue;

                    string p = r.p?.ToString();
                    string pn = r.pn?.ToString();
                    ModelItem model = null;

                    if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(pn))
                    {
                        if (!p.StartsWith("false", StringComparison.OrdinalIgnoreCase) &&
                            !pn.StartsWith("false", StringComparison.OrdinalIgnoreCase))
                        {
                            model = new ModelItem()
                            {
                                name = pn,
                                uri = $"{uri_star}?uri=" + (r?.ch == true ? "channels/" : "pornstars/") + r.p,
                            };
                        }
                    }

                    playlists.Add(new PlaylistItem()
                    {
                        name = r.tf,
                        video = $"{uri_video}?uri={r.u.Remove(0, 1)}",
                        picture = r.i,
                        preview = r.ipu,
                        time = r.d,
                        json = true,
                        related = true,
                        model = model,
                        bookmark = new Bookmark()
                        {
                            site = "xds",
                            href = r.u.Remove(0, 1),
                            image = r.i
                        }
                    });
                }

                return playlists;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Menu
        public static List<MenuItem> Menu(string host, string plugin, string sort, string c)
        {
            var memoryCache = HybridCache.GetMemory();
            string menuKey = $"Xvideos_menu_{host}_{plugin}_{sort}_{c}";

            if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
                return menu;

            string url = $"{host}/{plugin}";

            menu = new List<MenuItem>(5)
            {
                new MenuItem()
                {
                    title = "Поиск",
                    search_on = "search_on",
                    playlist_url = url,
                }
            };

            var menusort = new MenuItem()
            {
                title = $"Сортировка: {(sort == "like" ? "Понравившиеся" : sort == "top" ? "Лучшие" : "Новое")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(2)
                {
                    new("Новое", $"{url}?c={c}"),
                    new("Лучшие", $"{url}?c={c}&sort=top")
                }
            };

            if (plugin == "xdsred" && string.IsNullOrEmpty(c))
            {
                menusort.submenu.Add(new MenuItem()
                {
                    title = "Понравившиеся",
                    playlist_url = $"{url}?c={c}&sort=like"
                });
            }

            if (plugin != "xdsred" && sort != "like")
            {
                menu.Add(new MenuItem()
                {
                    title = $"Ориентация: {(plugin == "xdsgay" ? "Геи" : plugin == "xdssml" ? "Трансы" : "Гетеро")}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(3)
                    {
                        new("Гетеро", $"{host}/xds"),
                        new("Геи", $"{host}/xdsgay"),
                        new("Трансы", $"{host}/xdssml")
                    }
                });
            }

            if (sort != "like" && (plugin == "xds" || plugin == "xdsred"))
            {
                var submenu = new List<MenuItem>(40)
                {
                    new("Все", url),
                    new("Азиат", $"{url}?sort={sort}&c=Asian_Woman-32"),
                    new("Анал", $"{url}?sort={sort}&c=Anal-12"),
                    new("Арабки", $"{url}?sort={sort}&c=Arab-159"),
                    new("Бисексуалы", $"{url}?sort={sort}&c=Bi_Sexual-62"),
                    new("Блондинки", $"{url}?sort={sort}&c=Blonde-20"),
                    new("Большие Попы", $"{url}?sort={sort}&c=Big_Ass-24"),
                    new("Большие Сиськи", $"{url}?sort={sort}&c=Big_Tits-23"),
                    new("Большие яйца", $"{url}?sort={sort}&c=Big_Cock-34"),
                    new("Брюнетки", $"{url}?sort={sort}&c=Brunette-25"),
                    new("В масле", $"{url}?sort={sort}&c=Oiled-22"),
                    new("Веб камеры", $"{url}?sort={sort}&c=Cam_Porn-58"),
                    new("Гэнгбэнг", $"{url}?sort={sort}&c=Gangbang-69"),
                    new("Зияющие отверстия", $"{url}?sort={sort}&c=Gapes-167"),
                    new("Зрелые", $"{url}?sort={sort}&c=Mature-38"),
                    new("Индийский", $"{url}?sort={sort}&c=Indian-89"),
                    new("Испорченная семья", $"{url}?sort={sort}&c=Fucked_Up_Family-81"),
                    new("Кончает внутрь", $"{url}?sort={sort}&c=Creampie-40"),
                    new("Куколд / Горячая Жена", $"{url}?sort={sort}&c=Cuckold-237"),
                    new("Латинки", $"{url}?sort={sort}&c=Latina-16"),
                    new("Лесбиянки", $"{url}?sort={sort}&c=Lesbian-26"),
                    new("Любительское порно", $"{url}?sort={sort}&c=Amateur-65"),
                    new("Мамочки. МИЛФ", $"{url}?sort={sort}&c=Milf-19"),
                    new("Межрассовые", $"{url}?sort={sort}&c=Interracial-27"),
                    new("Минет", $"{url}?sort={sort}&c=Blowjob-15"),
                    new("Нижнее бельё", $"{url}?sort={sort}&c=Lingerie-83"),
                    new("Попки", $"{url}?sort={sort}&c=Ass-14"),
                    new("Рыжие", $"{url}?sort={sort}&c=Redhead-31"),
                    new("Сквиртинг", $"{url}?sort={sort}&c=Squirting-56"),
                    new("Соло", $"{url}?sort={sort}&c=Solo_and_Masturbation-33"),
                    new("Сперма", $"{url}?sort={sort}&c=Cumshot-18"),
                    new("Тинейджеры", $"{url}?sort={sort}&c=Teen-13"),
                    new("Фемдом", $"{url}?sort={sort}&c=Femdom-235"),
                    new("Фистинг", $"{url}?sort={sort}&c=Fisting-165"),
                    new("Черные Женщины", $"{url}?sort={sort}&c=bbw-51"),
                    new("Черный", $"{url}?sort={sort}&c=Black_Woman-30"),
                    new("Чулки,колготки", $"{url}?sort={sort}&c=Stockings-28"),
                    new("ASMR", $"{url}?sort={sort}&c=ASMR-229")
                };

                menu.Add(new MenuItem()
                {
                    title = $"Категория: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"c={c}"))?.title ?? "все"}",
                    playlist_url = "submenu",
                    submenu = submenu
                });
            }

            menu.Insert(1, menusort);

            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

            return menu;
        }
        #endregion

        #region StreamLinks
        public static string StreamLinksUri(string uri_star, string host, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            return $"{host}/{url}";
        }

        public static StreamItem StreamLinks(ReadOnlySpan<char> html, string route, string uri_star, Func<string, Task<string>> onm3u = null)
        {
            if (html.IsEmpty)
                return null;

            string stream_link = Rx.Match(html, "html5player\\.setVideoHLS\\('([^']+)'\\);");
            if (string.IsNullOrWhiteSpace(stream_link))
                return null;

            #region getRelated
            List<PlaylistItem> getRelated(ReadOnlySpan<char> html)
            {
                string json = Rx.Match(html, @"video_related\s*=\s*(\[[\s\S]*?\])\s*;", options: RegexOptions.Singleline | RegexOptions.CultureInvariant);
                if (string.IsNullOrWhiteSpace(json) || !json.StartsWith("[") || !json.EndsWith("]"))
                    return new List<PlaylistItem>();

                var related = new List<PlaylistItem>(40);

                try
                {
                    foreach (var r in JsonSerializer.Deserialize<List<Related>>(json, new JsonSerializerOptions
                    {
                        AllowTrailingCommas = true
                    }))
                    {
                        if (string.IsNullOrEmpty(r.tf) || string.IsNullOrEmpty(r.u))
                            continue;

                        string p = r.p?.ToString();
                        string pn = r.pn?.ToString();
                        ModelItem model = null;

                        if (!string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(pn))
                        {
                            if (!p.StartsWith("false", StringComparison.OrdinalIgnoreCase) &&
                                !pn.StartsWith("false", StringComparison.OrdinalIgnoreCase))
                            {
                                model = new ModelItem()
                                {
                                    name = pn,
                                    uri = $"{uri_star}?uri=" + (r?.ch == true ? "channels/" : "pornstars/") + r.p,
                                };
                            }
                        }

                        related.Add(new PlaylistItem()
                        {
                            name = r.tf,
                            video = $"{route}?uri={r.u.Remove(0, 1)}",
                            picture = r.i,
                            preview = r.ipu,
                            time = r.d,
                            json = true,
                            related = true,
                            model = model,
                            bookmark = new Bookmark()
                            {
                                site = "xds",
                                href = r.u.Remove(0, 1),
                                image = r.i
                            }
                        });
                    }
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId}", "XvideosTo", "id_wqrdlrn9");
                }

                return related;
            }

            #endregion

            return new StreamItem()
            {
                qualitys = new Dictionary<string, string>()
                {
                    ["auto"] = stream_link
                },
                recomends = getRelated(html)
            };

            //string m3u8 = onm3u == null ? null : await onm3u.Invoke(stream_link);
            //if (m3u8 == null)
            //{
            //    return new StreamItem()
            //    {
            //        qualitys = new Dictionary<string, string>()
            //        {
            //            ["auto"] = stream_link
            //        },
            //        recomends = getRelated()
            //    };
            //}

            //var stream_links = new Dictionary<int, string>();

            //foreach (string line in m3u8.Split('\n'))
            //{
            //    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("hls-"))
            //        continue;

            //    string _q = new Regex("hls-([0-9]+)p").Match(line).Groups[1].Value;

            //    if (int.TryParse(_q, out int q) && q > 0)
            //        stream_links.TryAdd(q, $"{Regex.Replace(stream_link, "/hls.m3u8.*", "")}/{line}");
            //}

            //return new StreamItem()
            //{
            //    qualitys = stream_links.OrderByDescending(i => i.Key).ToDictionary(k => $"{k.Key}p", v => v.Value),
            //    recomends = getRelated()
            //};
        }
        #endregion
    }
}
