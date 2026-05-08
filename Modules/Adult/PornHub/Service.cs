using Microsoft.Extensions.Caching.Memory;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services.Hybrid;
using Shared.Services.HTML;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace PornHub;

public static class PornHubTo
{
    #region Uri
    public static string Uri(string host, string plugin, string search, string model, string sort, int c, string hd, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        char splitkey = '?';

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrEmpty(search))
        {
            url.Append("video/search?search=");
            url.Append(HttpUtility.UrlEncode(search));
            splitkey = '&';

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append("&o=");
                url.Append(sort);
            }
        }
        else if (!string.IsNullOrEmpty(model))
        {
            if (model.StartsWith("pornstar/"))
            {
                url.Append(model);
                url.Append("/videos/upload");
            }
            else
            {
                url.Append("model/");
                url.Append(model);
                url.Append("/videos");
            }
        }
        else
        {
            switch (plugin ?? "")
            {
                case "phubgay":
                    url.Append("gay/video");
                    break;
                case "phubsml":
                    url.Append("transgender");
                    break;
                default:
                    url.Append("video");
                    break;
            }

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append($"{splitkey}o={sort}");
                splitkey = '&';
            }

            if (!string.IsNullOrEmpty(hd))
            {
                url.Append($"{splitkey}hd={hd}");
                splitkey = '&';
            }

            if (c > 0)
            {
                url.Append($"{splitkey}c={c}");
                splitkey = '&';
            }
        }

        if (pg > 1)
        {
            url.Append(splitkey);
            url.Append("page=");
            url.Append(pg);
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string video_uri, string list_uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null, bool related = false, bool prem = false, bool IsModel_page = false)
    {
        if (html.IsEmpty)
            return null;

        var videoCategory = ReadOnlySpan<char>.Empty;

        if (related)
        {
            videoCategory = HtmlSpan.Node(html, "*", "id", "relatedVideosListing", HtmlSpanTargetType.Exact);
            if (videoCategory.IsEmpty)
                videoCategory = HtmlSpan.Node(html, "*", "id", "relatedVideos", HtmlSpanTargetType.Exact);
        }
        else if (html.Contains("id=\"videoCategory\"", StringComparison.Ordinal))
        {
            videoCategory = HtmlSpan.Node(html, "*", "id", "videoCategory", HtmlSpanTargetType.Exact);
        }
        else if (html.Contains("videoList clearfix browseVideo-tabSplit", StringComparison.Ordinal))
        {
            var ids = Rx.Split("videoList clearfix browseVideo-tabSplit", html);
            if (ids.Count > 1)
            {
                videoCategory = ids[1].Span;

                if (videoCategory.Contains("<h2>Languages</h2>", StringComparison.Ordinal))
                    videoCategory = Rx.Split("<h2>Languages</h2>", videoCategory)[0].Span;

                if (videoCategory.Contains("pageHeader", StringComparison.Ordinal))
                    videoCategory = Rx.Split("pageHeader", videoCategory)[0].Span;
            }
        }
        else if (html.Contains("id=\"profileContent\"", StringComparison.Ordinal))
        {
            videoCategory = Rx.Slice(html, "id=\"profileContent\"", "</section>");
        }
        else
        {
            videoCategory = HtmlSpan.Node(html, "*", "id", "videoSearchResult", HtmlSpanTargetType.Exact);

            if (videoCategory.IsEmpty)
                videoCategory = HtmlSpan.Node(html, "*", "id", "mostRecentVideosSection", HtmlSpanTargetType.Exact);

            if (videoCategory.IsEmpty)
                videoCategory = HtmlSpan.Node(html, "*", "id", "moreData", HtmlSpanTargetType.Exact);

            if (videoCategory.IsEmpty)
                videoCategory = HtmlSpan.Node(html, "*", "id", "content-tv-container", HtmlSpanTargetType.Exact);

            if (videoCategory.IsEmpty)
                videoCategory = HtmlSpan.Node(html, "*", "id", "lazyVids", HtmlSpanTargetType.Exact);
        }

        if (videoCategory.IsEmpty)
            return null;

        ModelItem model = null;

        if (IsModel_page)
        {
            string name = Rx.Match(html, "itemprop=\"name\">([\r\n\t ]+)?([^<]+)</h1>", 2);
            string href = Rx.Match(html, "rel=\"canonical\" href=\"(https?://[^/]+)?/model/([^/]+)/", 2);

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(href))
            {
                model = new ModelItem()
                {
                    name = name.Trim(),
                    uri = list_uri + (list_uri.Contains("?") ? "&" : "?") + $"model={href}",
                };
            }
        }

        string splitkey = videoCategory.Contains("pcVideoListItem ", StringComparison.Ordinal)
            ? "pcVideoListItem " : videoCategory.Contains("data-video-segment", StringComparison.Ordinal)
            ? "data-video-segment" : videoCategory.Contains("<li data-id=", StringComparison.Ordinal)
            ? "<li data-id=" : "<li id=";

        var rx = Rx.Split(splitkey, videoCategory, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (row.Contains("brand__badge") || row.Contains("private-vid-title"))
                continue;

            string vkey = row.Match("(-|_)vkey=\"([^\"]+)\"", 2) ?? row.Match("viewkey=([^\"]+)\"");
            if (vkey == null)
                continue;

            string title = row.Match("href=\"/[^\"]+\" title=\"([^\"]+)\"") ?? row.Match("class=\"videoTitle\">([^<]+)<") ?? row.Match("href=\"/view_[^\"]+\" onclick=[^>]+>([^<]+)<");
            if (title == null)
                continue;

            string img = row.Match("data-mediumthumb=\"(https?://[^\"]+)\"") ?? row.Match("<img( [^>]+)? src=\"([^\"]+)\"", 2);
            if (img == null)
                continue;

            if (!IsModel_page)
            {
                model = null;
                var gmodel = row.Groups("href=\"/model/([^\"]+)\"[^>]+>([^<]+)<");
                if (string.IsNullOrEmpty(gmodel[1].Value))
                    gmodel = row.Groups("href=\"/(pornstar/[^\"]+)\"[^>]+>([^<]+)<");

                if (!string.IsNullOrEmpty(gmodel[1].Value))
                {
                    model = new ModelItem()
                    {
                        name = gmodel[2].Value,
                        uri = list_uri + (list_uri.Contains("?") ? "&" : "?") + $"model={gmodel[1].Value}",
                    };
                }
            }

            var pl = new PlaylistItem()
            {
                name = title,
                video = $"{video_uri}?vkey={vkey}",
                model = model,
                picture = img,
                preview = row.Match("data-mediabook=\"(https?://[^\"]+)\"") ?? row.Match("data-webm=\"(https?://[^\"]+)\""),
                time = row.Match("<var class=\"duration\">([^<]+)</var>") ?? row.Match("class=\"time\">([^<]+)<") ?? row.Match("class=\"videoDuration floatLeft\">([^<]+)<") ?? row.Match("time\">([^<]+)<"),
                json = true,
                related = true,
                bookmark = new Bookmark()
                {
                    site = prem ? "phubprem" : "phub",
                    href = vkey,
                    image = img
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string plugin, string search, string sort, int c, string hd = null)
    {
        #region getSortName
        string getSortName(string sort, string emptyName)
        {
            if (string.IsNullOrWhiteSpace(sort))
                return emptyName;

            switch (sort)
            {
                case "mr":
                case "cm":
                    return "новейшее";

                case "ht":
                    return "самые горячие";

                case "vi":
                case "mv":
                    return "больше просмотров";

                case "ra":
                case "tr":
                    return "лучшие";

                default:
                    return emptyName;
            }
        }
        #endregion

        string url = $"{host}/{plugin}";

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
                    title = $"Сортировка: {getSortName(sort, "Наиболее актуальное")}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(4)
                    {
                        new("Наиболее актуальное", $"{url}?search={encodesearch}"),
                        new("Новейшее", $"{url}?search={encodesearch}&sort=mr"),
                        new("Лучшие", $"{url}?search={encodesearch}&sort=tr"),
                        new("Больше просмотров",$"{url}?search={encodesearch}&sort=mv")
                    }
                }
            };
        }
        #endregion

        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"phub_menu_{host}_{plugin}_{sort}_{c}_{hd}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(4)
        {
            new MenuItem()
            {
                title = "Поиск",
                search_on = "search_on",
                playlist_url = url,
            },
            new MenuItem()
            {
                title = $"Сортировка: {getSortName(sort, "Недавно в избранном")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(4)
                {
                    new("Недавно в избранном", $"{url}?hd={hd}&c={c}"),
                    new("Новейшее", $"{url}?hd={hd}&c={c}&sort=cm"),
                    new("Самые горячие", $"{url}?hd={hd}&c={c}&sort=ht"),
                    new("Лучшие", $"{url}?hd={hd}&c={c}&sort=tr")
                }
            }
        };

        if (plugin == "pornhubpremium" || plugin == "phubprem")
        {
            menu.Insert(1, new MenuItem()
            {
                title = $"Качество: {(hd == "2" ? "1080p" : hd == "3" ? "1440p" : hd == "4" ? "2160p" : "все")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(4)
                {
                    new("Все", $"{url}?sort={sort}&c={c}"),
                    new("2160p", $"{url}?sort={sort}&c={c}&hd=4"),
                    new("1440p", $"{url}?sort={sort}&c={c}&hd=3"),
                    new("1080p", $"{url}?sort={sort}&c={c}&hd=2")
                }
            });
        }
        else
        {
            menu.Add(new MenuItem()
            {
                title = $"Ориентация: {(plugin == "phubgay" ? "Геи" : plugin == "phubsml" ? "Трансы" : "Гетеро")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Гетеро", $"{host}/phub"),
                    new("Геи", $"{host}/phubgay"),
                    new("Трансы", $"{host}/phubsml")
                }
            });
        }

        if (plugin == "phubgay")
        {
            var submenu = new List<MenuItem>(35)
            {
                new("Все", $"{url}?hd={hd}&sort={sort}"),
                new("Азиаты", $"{url}?hd={hd}&sort={sort}&c=48"),
                new("Без презерватива", $"{url}?hd={hd}&sort={sort}&c=40"),
                new("Большие члены", $"{url}?hd={hd}&sort={sort}&c=58"),
                new("Веб-камера", $"{url}?hd={hd}&sort={sort}&c=342"),
                new("Гонзо", $"{url}?hd={hd}&sort={sort}&c=372"),
                new("Грубый секс", $"{url}?hd={hd}&sort={sort}&c=312"),
                new("Дрочит", $"{url}?hd={hd}&sort={sort}&c=262"),
                new("Жеребцы", $"{url}?hd={hd}&sort={sort}&c=70"),
                new("Зрелые", $"{url}?hd={hd}&sort={sort}&c=332"),
                new("Кастинги", $"{url}?hd={hd}&sort={sort}&c=362"),
                new("Качки", $"{url}?hd={hd}&sort={sort}&c=322"),
                new("Колледж", $"{url}?hd={hd}&sort={sort}&c=68"),
                new("Кончают", $"{url}?hd={hd}&sort={sort}&c=352"),
                new("Кремпай", $"{url}?hd={hd}&sort={sort}&c=71"),
                new("Латино", $"{url}?hd={hd}&sort={sort}&c=50"),
                new("Любительское", $"{url}?hd={hd}&sort={sort}&c=252"),
                new("Массаж", $"{url}?hd={hd}&sort={sort}&c=45"),
                new("Медведь", $"{url}?hd={hd}&sort={sort}&c=66"),
                new("Межрассовый Секс", $"{url}?hd={hd}&sort={sort}&c=64"),
                new("Минет", $"{url}?hd={hd}&sort={sort}&c=56"),
                new("Молоденькие геи", $"{url}?hd={hd}&sort={sort}&c=49"),
                new("Мультики", $"{url}?hd={hd}&sort={sort}&c=422"),
                new("Мускулистые", $"{url}?hd={hd}&sort={sort}&c=51"),
                new("На публике", $"{url}?hd={hd}&sort={sort}&c=84"),
                new("Не обрезанные", $"{url}?hd={hd}&sort={sort}&c=272"),
                new("Негры", $"{url}?hd={hd}&sort={sort}&c=44"),
                new("Ноги", $"{url}?hd={hd}&sort={sort}&c=412"),
                new("Папики", $"{url}?hd={hd}&sort={sort}&c=47"),
                new("Парни (соло)", $"{url}?hd={hd}&sort={sort}&c=54"),
                new("Пухленькие", $"{url}?hd={hd}&sort={sort}&c=392"),
                new("Ретро", $"{url}?hd={hd}&sort={sort}&c=77"),
                new("Татуированные Мужчины", $"{url}?hd={hd}&sort={sort}&c=552"),
                new("Фетиш", $"{url}?hd={hd}&sort={sort}&c=52")
            };

            menu.Add(new MenuItem()
            {
                title = $"Категория: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "все"}",
                playlist_url = "submenu",
                submenu = submenu
            });
        }
        else if (plugin == "phub" || plugin == "phubprem")
        {
            var submenu = new List<MenuItem>(90)
            {
                new("Все", $"{url}?hd={hd}&sort={sort}"),
                new("Женский Выбор", $"{url}?hd={hd}&sort={sort}&c=73"),
                new("Русское", $"{url}?hd={hd}&sort={sort}&c=99"),
                new("Немецкое", $"{url}?hd={hd}&sort={sort}&c=95"),
                new("60FPS", $"{url}?hd={hd}&sort={sort}&c=105"),
                new("Азиатки", $"{url}?hd={hd}&sort={sort}&c=1"),
                new("Анальный секс", $"{url}?hd={hd}&sort={sort}&c=35"),
                new("Арабское", $"{url}?hd={hd}&sort={sort}&c=98"),
                new("БДСМ", $"{url}?hd={hd}&sort={sort}&c=10"),
                new("Безобидный контент", $"{url}?hd={hd}&sort={sort}&c=221"),
                new("Бисексуалы", $"{url}?hd={hd}&sort={sort}&c=76"),
                new("Блондинки", $"{url}?hd={hd}&sort={sort}&c=9"),
                new("Большая грудь", $"{url}?hd={hd}&sort={sort}&c=8"),
                new("Большие члены", $"{url}?hd={hd}&sort={sort}&c=7"),
                new("Бразильское", $"{url}?hd={hd}&sort={sort}&c=102"),
                new("Британское", $"{url}?hd={hd}&sort={sort}&c=96"),
                new("Брызги", $"{url}?hd={hd}&sort={sort}&c=69"),
                new("Брюнетки", $"{url}?hd={hd}&sort={sort}&c=11"),
                new("Буккаке", $"{url}?hd={hd}&sort={sort}&c=14"),
                new("В школе", $"{url}?hd={hd}&sort={sort}&c=88"),
                new("Веб-камера", $"{url}?hd={hd}&sort={sort}&c=61"),
                new("Вечеринки", $"{url}?hd={hd}&sort={sort}&c=53"),
                new("Гонзо", $"{url}?hd={hd}&sort={sort}&c=41"),
                new("Грубый секс", $"{url}?hd={hd}&sort={sort}&c=67"),
                new("Групповуха", $"{url}?hd={hd}&sort={sort}&c=80"),
                new("Двойное проникновение", $"{url}?hd={hd}&sort={sort}&c=72"),
                new("Девушки (соло)", $"{url}?hd={hd}&sort={sort}&c=492"),
                new("Дрочит", $"{url}?hd={hd}&sort={sort}&c=20"),
                new("Европейцы", $"{url}?hd={hd}&sort={sort}&c=55"),
                new("Женский оргазм", $"{url}?hd={hd}&sort={sort}&c=502"),
                new("Жесткий секс", $"{url}?hd={hd}&sort={sort}&c=21"),
                new("За кадром", $"{url}?hd={hd}&sort={sort}&c=141"),
                new("Звезды", $"{url}?hd={hd}&sort={sort}&c=12"),
                new("Золотой дождь", $"{url}?hd={hd}&sort={sort}&c=211"),
                new("Зрелые", $"{url}?hd={hd}&sort={sort}&c=28"),
                new("Игрушки", $"{url}?hd={hd}&sort={sort}&c=23"),
                new("Индийское", $"{url}?hd={hd}&sort={sort}&c=101"),
                new("Итальянское", $"{url}?hd={hd}&sort={sort}&c=97"),
                new("Кастинги", $"{url}?hd={hd}&sort={sort}&c=90"),
                new("Колледж", $"{url}?hd={hd}&sort={sort}&c=79"),
                new("Кончают", $"{url}?hd={hd}&sort={sort}&c=16"),
                new("Корейское", $"{url}?hd={hd}&sort={sort}&c=103"),
                new("Косплей", $"{url}?hd={hd}&sort={sort}&c=241"),
                new("Красотки", $"{url}?hd={hd}&sort={sort}&c=5"),
                new("Кремпай", $"{url}?hd={hd}&sort={sort}&c=15"),
                new("Кунилингус", $"{url}?hd={hd}&sort={sort}&c=131"),
                new("Курящие", $"{url}?hd={hd}&sort={sort}&c=91"),
                new("Латинки", $"{url}?hd={hd}&sort={sort}&c=26"),
                new("Любительское", $"{url}?hd={hd}&sort={sort}&c=3"),
                new("Маленькая грудь", $"{url}?hd={hd}&sort={sort}&c=59"),
                new("Мамочки", $"{url}?hd={hd}&sort={sort}&c=29"),
                new("Массаж", $"{url}?hd={hd}&sort={sort}&c=78"),
                new("Мастурбация", $"{url}?hd={hd}&sort={sort}&c=22"),
                new("Межрассовый Секс", $"{url}?hd={hd}&sort={sort}&c=25"),
                new("Минет", $"{url}?hd={hd}&sort={sort}&c=13"),
                new("Мулаты", $"{url}?hd={hd}&sort={sort}&c=17"),
                new("Мультики", $"{url}?hd={hd}&sort={sort}&c=86"),
                new("Мускулистые Мужчины", $"{url}?hd={hd}&sort={sort}&c=512"),
                new("На публике", $"{url}?hd={hd}&sort={sort}&c=24"),
                new("Ноги", $"{url}?hd={hd}&sort={sort}&c=93"),
                new("Няни", $"{url}?hd={hd}&sort={sort}&c=89"),
                new("Пародия", $"{url}?hd={hd}&sort={sort}&c=201"),
                new("Пенсионеры / подростки", $"{url}?hd={hd}&sort={sort}&c=181"),
                new("Подростки", $"{url}?hd={hd}&sort={sort}&c=37"),
                new("Попки", $"{url}?hd={hd}&sort={sort}&c=4"),
                new("Приколы", $"{url}?hd={hd}&sort={sort}&c=32"),
                new("Ретро", $"{url}?hd={hd}&sort={sort}&c=43"),
                new("Рогоносцы", $"{url}?hd={hd}&sort={sort}&c=242"),
                new("Ролевые Игры", $"{url}?hd={hd}&sort={sort}&c=81"),
                new("Романтическое", $"{url}?hd={hd}&sort={sort}&c=522"),
                new("Рыжие", $"{url}?hd={hd}&sort={sort}&c=42"),
                new("Секс втроем", $"{url}?hd={hd}&sort={sort}&c=65"),
                new("Секс-оргия", $"{url}?hd={hd}&sort={sort}&c=2"),
                new("Семейные фантазии", $"{url}?hd={hd}&sort={sort}&c=444"),
                new("Страпон", $"{url}?hd={hd}&sort={sort}&c=542"),
                new("Стриптиз", $"{url}?hd={hd}&sort={sort}&c=33"),
                new("Татуированные Женщины", $"{url}?hd={hd}&sort={sort}&c=562"),
                new("Толстушки", $"{url}?hd={hd}&sort={sort}&c=6"),
                new("Трансвеститы", $"{url}?hd={hd}&sort={sort}&c=83"),
                new("Удовлетворение пальцами", $"{url}?hd={hd}&sort={sort}&c=592"),
                new("Фетиш", $"{url}?hd={hd}&sort={sort}&c=18"),
                new("Фистинг", $"{url}?hd={hd}&sort={sort}&c=19"),
                new("Французское", $"{url}?hd={hd}&sort={sort}&c=94"),
                new("Хентай", $"{url}?hd={hd}&sort={sort}&c=36"),
                new("Чешское", $"{url}?hd={hd}&sort={sort}&c=100"),
                new("Японцы", $"{url}?hd={hd}&sort={sort}&c=111")
            };

            menu.Add(new MenuItem()
            {
                title = $"Категория: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "все"}",
                playlist_url = "submenu",
                submenu = submenu
            });
        }

        memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string vkey)
    {
        if (string.IsNullOrEmpty(vkey))
            return null;

        return $"{host}/view_video.php?viewkey={vkey}";
    }

    public static StreamItem StreamLinks(ReadOnlySpan<char> html, string video_uri, string list_uri)
    {
        if (html.IsEmpty)
            return null;

        var qualitys = new Dictionary<string, string>();

        foreach (string q in new string[] { "1080", "720", "480", "240" })
        {
            string video = Rx.Match(html, $"\"videoUrl\":\"([^\"]+)\",\"quality\":\"{q}\"");

            if (!string.IsNullOrEmpty(video))
                qualitys.TryAdd($"{q}p", video.Replace("\\", "").Replace("///", "//"));
        }

        if (qualitys.Count == 0)
            return null;

        return new StreamItem()
        {
            qualitys = qualitys,
            recomends = Playlist(video_uri, list_uri, html, related: true)
        };
    }
    #endregion

    #region Pages
    public static int Pages(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return 0;

        if (!html.Contains("class=\"page_number\"", StringComparison.Ordinal))
            return 1;

        var rx = Rx.Matches("class=\"page_number\"><a [^>]+>([0-9]+)<", html);
        if (rx.Count == 0)
            return 1;

        int maxpage = 0;
        foreach (var row in rx.Rows())
        {
            string page = row.Match("class=\"page_number\"><a [^>]+>([0-9]+)<");
            if (page != null && int.TryParse(page, out int pg) && pg > maxpage)
                maxpage = pg;
        }

        // модель 6, навигация 5
        if (4 >= maxpage)
            return maxpage;

        return 0;
    }
    #endregion

    #region getDirectLinks
    static string getDirectLinks(string pageCode)
    {
        var vars = new List<(string name, string param)>();

        string mainParamBody = Regex.Match(pageCode, "var player_mp4_seek = \"[^\"]+\";[\n\r\t ]+(// var[^\n\r]+[\n\r\t ]+)?([^\n\r]+)").Groups[2].Value;
        mainParamBody = Regex.Replace(mainParamBody, "/\\*.*?\\*/", "");
        mainParamBody = mainParamBody.Replace("\" + \"", "");

        foreach (Match currVar in Regex.Matches(mainParamBody, "var ([^=]+)=([^;]+);"))
            vars.Add((currVar.Groups[1].Value, currVar.Groups[2].Value.Replace("\"", "").Replace(" + ", "")));

        string mediapattern = /*mainParamBody.Contains("var media_4=") && mainParamBody.Contains("var media_5=") ? "var media_(4)=(.*?);" : */"var media_([0-9]+)=(.*?);";
        foreach (Match m in Regex.Matches(mainParamBody, mediapattern, RegexOptions.Singleline))
        {
            string link = "";
            foreach (string curr in m.Groups[2].Value.Replace(" ", "").Split('+'))
            {
                string param = vars.Find(x => x.name == curr).param;
                if (param == null)
                    continue;

                link += param;
            }

            if (link.Contains("urlset/master.m3u8"))
                return link;
        }

        return null;
    }
    #endregion
}
