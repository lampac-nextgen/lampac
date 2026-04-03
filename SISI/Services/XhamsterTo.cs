using Microsoft.Extensions.Caching.Memory;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using SISI.Models.Xhamster;
using System.Text.Json;
using System.Web;

namespace SISI.Services
{
    public static class XhamsterTo
    {
        #region Uri
        public static string Uri(string host, string plugin, string search, string c, string q, string sort, int pg)
        {
            var url = StringBuilderPool.ThreadInstance;

            url.Append(host);

            if (!string.IsNullOrWhiteSpace(search))
            {
                url.Append("/search/");
                url.Append(HttpUtility.UrlEncode(search));
                url.Append("?page=");
                url.Append(pg);
            }
            else
            {
                switch (plugin ?? "")
                {
                    case "xmrsml":
                        url.Append("/shemale");
                        break;
                    case "xmrgay":
                        url.Append("/gay");
                        break;
                    default:
                        break;
                }

                if (!string.IsNullOrEmpty(c))
                {
                    url.Append("/categories/");
                    url.Append(c);
                }

                if (!string.IsNullOrEmpty(q))
                {
                    url.Append("/");
                    url.Append(q);
                }

                switch (sort ?? "")
                {
                    case "newest":
                        url.Append("/newest");
                        break;
                    case "best":
                        url.Append("/best");
                        break;
                    default:
                        break;
                }

                if (pg > 0)
                {
                    url.Append("/");
                    url.Append(pg);
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

            ReadOnlySpan<char> json = Rx.Slice(html, "window.initials=", ";</script>");
            if (json.IsEmpty)
                return null;

            Root root = null;

            try
            {
                root = JsonSerializer.Deserialize<Root>(json, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                });
            }
            catch { }

            var videos = root?.layoutPage?.videoListProps?.videoThumbProps
                ?? root?.searchResult?.videoThumbProps
                ?? root?.pagesCategoryComponent?.trendingVideoListProps?.videoThumbProps;

            if (videos == null || videos.Count == 0)
                return null;

            var playlists = new List<PlaylistItem>(videos.Count);

            foreach (var video in videos)
            {
                if (!string.IsNullOrEmpty(video.title) && !string.IsNullOrWhiteSpace(video.pageURL))
                {
                    int duration = video.duration > 60 ? (video.duration / 60) : 0;

                    var pl = new PlaylistItem()
                    {
                        name = video.title,
                        video = $"{route}?uri={HttpUtility.UrlEncode(Regex.Replace(video.pageURL, "^https?://[^/]+/", ""))}",
                        picture = video.thumbURL,
                        quality = video.isUHD ? "HD" : null,
                        preview = video.trailerURL ?? video.trailerFallbackUrl,
                        time = duration > 0 ? $"{duration.ToString()}m" : null,
                        json = true,
                        related = true,
                        bookmark = new Bookmark()
                        {
                            site = "xmr",
                            href = video.pageURL,
                            image = video.thumbURL
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
        public static List<MenuItem> Menu(string host, string plugin, string c, string q, string sort)
        {
            var memoryCache = HybridCache.GetMemory();
            string menuKey = $"Xhamster_menu_{host}_{plugin}_{sort}_{c}_{q}";

            if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
                return menu;

            menu = new List<MenuItem>(5)
            {
                new MenuItem()
                {
                    title = "Поиск",
                    search_on = "search_on",
                    playlist_url = $"{host}/{plugin}"
                },
                new MenuItem()
                {
                    title = $"Качество: {(q == "4k" ? "2160p" : "Любое")}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(2)
                    {
                        new("Любое", $"{host}/{plugin}?c={c}&sort={sort}"),
                        new("2160p", $"{host}/{plugin}?c={c}&sort={sort}&q=4k")
                    }
                },
                new MenuItem()
                {
                    title = $"Сортировка: {(sort == "newest" ? "Новинки" : sort == "best" ? "Лучшие" :"В тренде")}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(3)
                    {
                        new("В тренде", $"{host}/{plugin}?c={c}&q={q}&sort=trend"),
                        new("Самые новые", $"{host}/{plugin}?c={c}&q={q}&sort=newest"),
                        new("Лучшие видео", $"{host}/{plugin}?c={c}&q={q}&sort=best")
                    }
                },
                new MenuItem()
                {
                    title = $"Ориентация: {(plugin == "xmrgay" ? "Геи" : plugin == "xmrsml" ? "Трансы" :"Гетеро")}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(3)
                    {
                        new("Гетеро", $"{host}/xmr"),
                        new("Геи", $"{host}/xmrgay"),
                        new("Трансы", $"{host}/xmrsml")
                    }
                }
            };

            if (plugin == "xmr")
            {
                var submenu = new List<MenuItem>(75)
                {
                    new("Все", $"{host}/{plugin}?sort={sort}&q={q}"),
                    new("Русское", $"{host}/{plugin}?sort={sort}&q={q}&c=russian"),
                    new("Секс втроем", $"{host}/{plugin}?sort={sort}&q={q}&c=threesome"),
                    new("Азиатское", $"{host}/{plugin}?sort={sort}&q={q}&c=asian"),
                    new("Анал", $"{host}/{plugin}?sort={sort}&q={q}&c=anal"),
                    new("Арабское", $"{host}/{plugin}?sort={sort}&q={q}&c=arab"),
                    new("АСМР", $"{host}/{plugin}?sort={sort}&q={q}&c=asmr"),
                    new("Бабки", $"{host}/{plugin}?sort={sort}&q={q}&c=granny"),
                    new("БДСМ", $"{host}/{plugin}?sort={sort}&q={q}&c=bdsm"),
                    new("Би", $"{host}/{plugin}?sort={sort}&q={q}&c=bisexual"),
                    new("Большие жопы", $"{host}/{plugin}?sort={sort}&q={q}&c=big-ass"),
                    new("Большие задницы", $"{host}/{plugin}?sort={sort}&q={q}&c=pawg"),
                    new("Большие сиськи", $"{host}/{plugin}?sort={sort}&q={q}&c=big-tits"),
                    new("Большой член", $"{host}/{plugin}?sort={sort}&q={q}&c=big-cock"),
                    new("Британское", $"{host}/{plugin}?sort={sort}&q={q}&c=british"),
                    new("В возрасте", $"{host}/{plugin}?sort={sort}&q={q}&c=mature"),
                    new("Вебкамера", $"{host}/{plugin}?sort={sort}&q={q}&c=webcam"),
                    new("Винтаж", $"{host}/{plugin}?sort={sort}&q={q}&c=vintage"),
                    new("Волосатые", $"{host}/{plugin}?sort={sort}&q={q}&c=hairy"),
                    new("Голые мужчины одетые женщины", $"{host}/{plugin}?sort={sort}&q={q}&c=cfnm"),
                    new("Групповой секс", $"{host}/{plugin}?sort={sort}&q={q}&c=group-sex"),
                    new("Гэнгбэнг", $"{host}/{plugin}?sort={sort}&q={q}&c=gangbang"),
                    new("Дилдо", $"{host}/{plugin}?sort={sort}&q={q}&c=dildo"),
                    new("Домашнее порно", $"{host}/{plugin}?sort={sort}&q={q}&c=homemade"),
                    new("Дрочка ступнями", $"{host}/{plugin}?sort={sort}&q={q}&c=footjob"),
                    new("Женское доминирование", $"{host}/{plugin}?sort={sort}&q={q}&c=femdom"),
                    new("Жиробасина", $"{host}/{plugin}?sort={sort}&q={q}&c=ssbbw"),
                    new("Жопа", $"{host}/{plugin}?sort={sort}&q={q}&c=ass"),
                    new("Застряла", $"{host}/{plugin}?sort={sort}&q={q}&c=stuck"),
                    new("Знаменитость", $"{host}/{plugin}?sort={sort}&q={q}&c=celebrity"),
                    new("Игра", $"{host}/{plugin}?sort={sort}&q={q}&c=game"),
                    new("История", $"{host}/{plugin}?sort={sort}&q={q}&c=story"),
                    new("Кастинг", $"{host}/{plugin}?sort={sort}&q={q}&c=casting"),
                    new("Комический", $"{host}/{plugin}?sort={sort}&q={q}&c=comic"),
                    new("Кончина", $"{host}/{plugin}?sort={sort}&q={q}&c=cumshot"),
                    new("Кремовый пирог", $"{host}/{plugin}?sort={sort}&q={q}&c=creampie"),
                    new("Латина", $"{host}/{plugin}?sort={sort}&q={q}&c=latina"),
                    new("Лесбиянка", $"{host}/{plugin}?sort={sort}&q={q}&c=lesbian"),
                    new("Лизать киску", $"{host}/{plugin}?sort={sort}&q={q}&c=eating-pussy"),
                    new("Любительское порно", $"{host}/{plugin}?sort={sort}&q={q}&c=amateur"),
                    new("Массаж", $"{host}/{plugin}?sort={sort}&q={q}&c=massage"),
                    new("Медсестра", $"{host}/{plugin}?sort={sort}&q={q}&c=nurse"),
                    new("Межрасовый секс", $"{host}/{plugin}?sort={sort}&q={q}&c=interracial"),
                    new("МИЛФ", $"{host}/{plugin}?sort={sort}&q={q}&c=milf"),
                    new("Милые", $"{host}/{plugin}?sort={sort}&q={q}&c=cute"),
                    new("Минет", $"{host}/{plugin}?sort={sort}&q={q}&c=blowjob"),
                    new("Миниатюрная", $"{host}/{plugin}?sort={sort}&q={q}&c=petite"),
                    new("Миссионерская поза", $"{host}/{plugin}?sort={sort}&q={q}&c=missionary"),
                    new("Монахиня", $"{host}/{plugin}?sort={sort}&q={q}&c=nun"),
                    new("Мультфильмы", $"{host}/{plugin}?sort={sort}&q={q}&c=cartoon"),
                    new("Негритянки", $"{host}/{plugin}?sort={sort}&q={q}&c=black"),
                    new("Немецкое", $"{host}/{plugin}?sort={sort}&q={q}&c=german"),
                    new("Офис", $"{host}/{plugin}?sort={sort}&q={q}&c=office"),
                    new("Первый раз", $"{host}/{plugin}?sort={sort}&q={q}&c=first-time"),
                    new("Пляж", $"{host}/{plugin}?sort={sort}&q={q}&c=beach"),
                    new("Порно для женщин", $"{host}/{plugin}?sort={sort}&q={q}&c=porn-for-women"),
                    new("Реслинг", $"{host}/{plugin}?sort={sort}&q={q}&c=wrestling"),
                    new("Рогоносцы", $"{host}/{plugin}?sort={sort}&q={q}&c=cuckold"),
                    new("Романтический", $"{host}/{plugin}?sort={sort}&q={q}&c=romantic"),
                    new("Свингеры", $"{host}/{plugin}?sort={sort}&q={q}&c=swingers"),
                    new("Сквирт", $"{host}/{plugin}?sort={sort}&q={q}&c=squirting"),
                    new("Старик", $"{host}/{plugin}?sort={sort}&q={q}&c=old-man"),
                    new("Старые с молодыми", $"{host}/{plugin}?sort={sort}&q={q}&c=old-young"),
                    new("Тинейджеры (18+)", $"{host}/{plugin}?sort={sort}&q={q}&c=teen"),
                    new("Толстушки", $"{host}/{plugin}?sort={sort}&q={q}&c=bbw"),
                    new("Тренажерный зал", $"{host}/{plugin}?sort={sort}&q={q}&c=gym"),
                    new("Узкая киска", $"{host}/{plugin}?sort={sort}&q={q}&c=tight-pussy"),
                    new("Французское", $"{host}/{plugin}?sort={sort}&q={q}&c=french"),
                    new("Футанари", $"{host}/{plugin}?sort={sort}&q={q}&c=futanari"),
                    new("Хардкор", $"{host}/{plugin}?sort={sort}&q={q}&c=hardcore"),
                    new("Хенджоб", $"{host}/{plugin}?sort={sort}&q={q}&c=handjob"),
                    new("Хентай", $"{host}/{plugin}?sort={sort}&q={q}&c=hentai"),
                    new("Японское", $"{host}/{plugin}?sort={sort}&q={q}&c=japanese")
                };

                menu.Add(new MenuItem()
                {
                    title = $"Категория: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "все"}",
                    playlist_url = "submenu",
                    submenu = submenu
                });
            }
            else if (plugin == "xmrgay")
            {
                var submenu = new List<MenuItem>(50)
                {
                    new("Все", $"{host}/{plugin}?sort={sort}&q={q}"),
                    new("Russian", $"{host}/{plugin}?sort={sort}&q={q}&c=russian"),
                    new("Threesome", $"{host}/{plugin}?sort={sort}&q={q}&c=threesome"),
                    new("Азиатское", $"{host}/{plugin}?sort={sort}&q={q}&c=asian"),
                    new("БДСМ", $"{host}/{plugin}?sort={sort}&q={q}&c=bdsm"),
                    new("Без презерватива", $"{host}/{plugin}?sort={sort}&q={q}&c=bareback"),
                    new("Большие дырки", $"{host}/{plugin}?sort={sort}&q={q}&c=gaping"),
                    new("Большой член", $"{host}/{plugin}?sort={sort}&q={q}&c=big-cock"),
                    new("Буккаке", $"{host}/{plugin}?sort={sort}&q={q}&c=bukkake"),
                    new("Вебкамера", $"{host}/{plugin}?sort={sort}&q={q}&c=webcam"),
                    new("Винтаж", $"{host}/{plugin}?sort={sort}&q={q}&c=vintage"),
                    new("Глорихол", $"{host}/{plugin}?sort={sort}&q={q}&c=glory-hole"),
                    new("Групповой секс", $"{host}/{plugin}?sort={sort}&q={q}&c=group-sex"),
                    new("Гэнгбэнг", $"{host}/{plugin}?sort={sort}&q={q}&c=gangbang"),
                    new("Дедушка", $"{host}/{plugin}?sort={sort}&q={q}&c=grandpa"),
                    new("Дилдо", $"{host}/{plugin}?sort={sort}&q={q}&c=dildo"),
                    new("Кончать на фотографии", $"{host}/{plugin}?sort={sort}&q={q}&c=cum-tribute"),
                    new("Кончина", $"{host}/{plugin}?sort={sort}&q={q}&c=cumshot"),
                    new("Красавчик", $"{host}/{plugin}?sort={sort}&q={q}&c=hunk"),
                    new("Кремовый пирог", $"{host}/{plugin}?sort={sort}&q={q}&c=creampie"),
                    new("Маленький член", $"{host}/{plugin}?sort={sort}&q={q}&c=small-cock"),
                    new("Массаж", $"{host}/{plugin}?sort={sort}&q={q}&c=massage"),
                    new("Мастурбация", $"{host}/{plugin}?sort={sort}&q={q}&c=masturbation"),
                    new("Медведь", $"{host}/{plugin}?sort={sort}&q={q}&c=bear"),
                    new("Межрасовый секс", $"{host}/{plugin}?sort={sort}&q={q}&c=interracial"),
                    new("Минет", $"{host}/{plugin}?sort={sort}&q={q}&c=blowjob"),
                    new("Молодые", $"{host}/{plugin}?sort={sort}&q={q}&c=young"),
                    new("На природе", $"{host}/{plugin}?sort={sort}&q={q}&c=outdoor"),
                    new("Негры", $"{host}/{plugin}?sort={sort}&q={q}&c=black"),
                    new("Папочка", $"{host}/{plugin}?sort={sort}&q={q}&c=daddy"),
                    new("Пляж", $"{host}/{plugin}?sort={sort}&q={q}&c=beach"),
                    new("Пухляш", $"{host}/{plugin}?sort={sort}&q={q}&c=chubby"),
                    new("Раздевалка", $"{host}/{plugin}?sort={sort}&q={q}&c=locker-room"),
                    new("Рестлинг", $"{host}/{plugin}?sort={sort}&q={q}&c=wrestling"),
                    new("Секс игрушки", $"{host}/{plugin}?sort={sort}&q={q}&c=sex-toy"),
                    new("Сладкий мальчик", $"{host}/{plugin}?sort={sort}&q={q}&c=twink"),
                    new("Соло", $"{host}/{plugin}?sort={sort}&q={q}&c=solo"),
                    new("Спанкинг", $"{host}/{plugin}?sort={sort}&q={q}&c=spanking"),
                    new("Старые с молодыми", $"{host}/{plugin}?sort={sort}&q={q}&c=old-young"),
                    new("Стриптиз", $"{host}/{plugin}?sort={sort}&q={q}&c=striptease"),
                    new("Толстые", $"{host}/{plugin}?sort={sort}&q={q}&c=fat"),
                    new("Трансвеститы", $"{host}/{plugin}?sort={sort}&q={q}&c=crossdresser"),
                    new("Фистинг", $"{host}/{plugin}?sort={sort}&q={q}&c=fisting"),
                    new("Хенджоб", $"{host}/{plugin}?sort={sort}&q={q}&c=handjob"),
                    new("Хентай", $"{host}/{plugin}?sort={sort}&q={q}&c=hentai"),
                    new("Эмобой", $"{host}/{plugin}?sort={sort}&q={q}&c=emo")
                };

                menu.Add(new MenuItem()
                {
                    title = $"Категория: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "все"}",
                    playlist_url = "submenu",
                    submenu = submenu
                });
            }
            else if (plugin == "xmrsml")
            {
                var submenu = new List<MenuItem>(50)
                {
                    new("Все", $"{host}/{plugin}?sort={sort}&q={q}"),
                    new("Russian", $"{host}/{plugin}?sort={sort}&q={q}&c=russian"),
                    new("Cuckold", $"{host}/{plugin}?sort={sort}&q={q}&c=cuckold"),
                    new("Азиатское", $"{host}/{plugin}?sort={sort}&q={q}&c=asian"),
                    new("БДСМ", $"{host}/{plugin}?sort={sort}&q={q}&c=bdsm"),
                    new("Без презерватива", $"{host}/{plugin}?sort={sort}&q={q}&c=bareback"),
                    new("Блондинки", $"{host}/{plugin}?sort={sort}&q={q}&c=blonde"),
                    new("Большие жопы", $"{host}/{plugin}?sort={sort}&q={q}&c=big-ass"),
                    new("Большой член", $"{host}/{plugin}?sort={sort}&q={q}&c=big-cock"),
                    new("Вебкамера", $"{host}/{plugin}?sort={sort}&q={q}&c=webcam"),
                    new("Винтаж", $"{host}/{plugin}?sort={sort}&q={q}&c=vintage"),
                    new("Групповой секс", $"{host}/{plugin}?sort={sort}&q={q}&c=group-sex"),
                    new("Гэнгбэнг", $"{host}/{plugin}?sort={sort}&q={q}&c=gangbang"),
                    new("Домашнее", $"{host}/{plugin}?sort={sort}&q={q}&c=homemade"),
                    new("Золотой дождь", $"{host}/{plugin}?sort={sort}&q={q}&c=pissing"),
                    new("Кремовый пирог", $"{host}/{plugin}?sort={sort}&q={q}&c=creampie"),
                    new("Латекс", $"{host}/{plugin}?sort={sort}&q={q}&c=latex"),
                    new("Латина", $"{host}/{plugin}?sort={sort}&q={q}&c=latina"),
                    new("Ледибой", $"{host}/{plugin}?sort={sort}&q={q}&c=ladyboy"),
                    new("Ловушка", $"{host}/{plugin}?sort={sort}&q={q}&c=trap"),
                    new("Любительское порно", $"{host}/{plugin}?sort={sort}&q={q}&c=amateur"),
                    new("Маленькие сиськи", $"{host}/{plugin}?sort={sort}&q={q}&c=small-tits"),
                    new("Мастурбация", $"{host}/{plugin}?sort={sort}&q={q}&c=masturbation"),
                    new("Межрасовый секс", $"{host}/{plugin}?sort={sort}&q={q}&c=interracial"),
                    new("Минет", $"{host}/{plugin}?sort={sort}&q={q}&c=blowjob"),
                    new("Миниатюрная", $"{host}/{plugin}?sort={sort}&q={q}&c=petite"),
                    new("На природе", $"{host}/{plugin}?sort={sort}&q={q}&c=outdoor"),
                    new("Нижнее белье", $"{host}/{plugin}?sort={sort}&q={q}&c=lingerie"),
                    new("От первого лица", $"{host}/{plugin}?sort={sort}&q={q}&c=pov"),
                    new("Парень трахает транса", $"{host}/{plugin}?sort={sort}&q={q}&c=guy-fucks-shemale"),
                    new("Подростки", $"{host}/{plugin}?sort={sort}&q={q}&c=teen"),
                    new("Рыжие", $"{host}/{plugin}?sort={sort}&q={q}&c=redhead"),
                    new("Секс втроем", $"{host}/{plugin}?sort={sort}&q={q}&c=threesome"),
                    new("Секс игрушки", $"{host}/{plugin}?sort={sort}&q={q}&c=sex-toy"),
                    new("Соло", $"{host}/{plugin}?sort={sort}&q={q}&c=solo"),
                    new("Татуировки", $"{host}/{plugin}?sort={sort}&q={q}&c=tattoo"),
                    new("Толстушки", $"{host}/{plugin}?sort={sort}&q={q}&c=bbw"),
                    new("Транс трахает девушку", $"{host}/{plugin}?sort={sort}&q={q}&c=shemale-fucks-girl"),
                    new("Транс трахает парня", $"{host}/{plugin}?sort={sort}&q={q}&c=shemale-fucks-guy"),
                    new("Транс трахает транса", $"{host}/{plugin}?sort={sort}&q={q}&c=shemale-fucks-shemale"),
                    new("Трансвестит", $"{host}/{plugin}?sort={sort}&q={q}&c=transgender"),
                    new("Фетиш", $"{host}/{plugin}?sort={sort}&q={q}&c=fetish"),
                    new("Хардкор", $"{host}/{plugin}?sort={sort}&q={q}&c=hardcore"),
                    new("Хенджоб", $"{host}/{plugin}?sort={sort}&q={q}&c=handjob"),
                    new("Хентай", $"{host}/{plugin}?sort={sort}&q={q}&c=hentai"),
                    new("Хорошенькая", $"{host}/{plugin}?sort={sort}&q={q}&c=pretty"),
                    new("Чернокожие", $"{host}/{plugin}?sort={sort}&q={q}&c=black"),
                    new("Чулки", $"{host}/{plugin}?sort={sort}&q={q}&c=stockings"),
                    new("Японское порно", $"{host}/{plugin}?sort={sort}&q={q}&c=japanese")
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
        public static string StreamLinksUri(string host, string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            return $"{host}/{url}";
        }

        public static StreamItem StreamLinks(string host, string route, ReadOnlySpan<char> html)
        {
            if (html.IsEmpty)
                return null;

            string stream_link = Rx.Match(html, "rel=\"preload\" href=\"([^\"]+)\"");
            if (stream_link == null || !stream_link.Contains(".m3u"))
                return null;

            return new StreamItem()
            {
                qualitys = new Dictionary<string, string>()
                {
                    ["auto"] = stream_link.StartsWith("/")
                        ? host + stream_link.Replace("\\", "")
                        : stream_link.Replace("\\", "")
                },
                recomends = Playlist(route, html)
            };
        }
        #endregion
    }
}
