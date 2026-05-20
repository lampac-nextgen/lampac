using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Ebalovo;

public static class EbalovoTo
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
        }
        else
        {
            if (!string.IsNullOrEmpty(c))
            {
                url.Append("porno/");
                url.Append(c);

                if (sort is "porno-online" or "xxx-top")
                    url.Append("-rating");

                url.Append("/");
            }
            else
            {
                if (!string.IsNullOrEmpty(sort))
                {
                    url.Append(sort);
                    url.Append("/");
                }
            }
        }

        if (pg > 1)
        {
            url.Append(pg);
            url.Append("/");
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("<div class=\"item\">", html);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (!row.Contains("<div class=\"item-info\">"))
                continue;

            string link = row.Match("<a href=\"https?://[^/]+/(video/[^\"]+)\"");
            string title = row.Match("<div class=\"item-title\">([^<]+)</div>");

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
            {
                var img = row.Groups("( )src=\"(([^\"]+)/[0-9]+.jpg)\"");
                if (string.IsNullOrWhiteSpace(img[3].Value) || img[2].Value.Contains("load.png"))
                    img = row.Groups("(data-srcset|data-src|srcset)=\"([^\"]+/[0-9]+.jpg)\"");

                var pl = new PlaylistItem()
                {
                    name = title.Trim(),
                    video = $"{uri}?uri={link}",
                    picture = img[2].Value,
                    time = row.Match(" data-eb=\"([^;\"]+);", trim: true),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "elo",
                        href = link,
                        image = img[2].Value
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
        string menuKey = $"Ebalovo_menu_{host}_{sort}_{c}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        string url = $"{host}/elo";

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
                submenu = new List<MenuItem>(3)
                {
                    new("Новинки", $"{url}?c={c}"),
                    new("Лучшее", $"{url}?c={c}&sort=porno-online"),
                    new("Популярное", $"{url}?c={c}&sort=xxx-top")
                }
            }
        };

        var catmenu = new List<MenuItem>(140)
        {
            new("Все", $"{url}?sort={sort}"),
            new("CFNM", $"{url}?sort={sort}&c=cfnm"),
            new("pov", $"{url}?sort={sort}&c=pov"),
            new("Анал", $"{url}?sort={sort}&c=anal-videos"),
            new("Анальная дыра", $"{url}?sort={sort}&c=gape"),
            new("Анальная пробка", $"{url}?sort={sort}&c=butt-plug-porn"),
            new("БДСМ", $"{url}?sort={sort}&c=bdsm-porn"),
            new("Блондинки", $"{url}?sort={sort}&c=blonde"),
            new("Большие жопы", $"{url}?sort={sort}&c=big-ass"),
            new("Большие сиськи", $"{url}?sort={sort}&c=big-tits"),
            new("Большие члены", $"{url}?sort={sort}&c=big-cock"),
            new("Большой чёрный член", $"{url}?sort={sort}&c=bbc"),
            new("Бондаж", $"{url}?sort={sort}&c=bondage"),
            new("Босс", $"{url}?sort={sort}&c=boss"),
            new("Бритые письки", $"{url}?sort={sort}&c=shaved-pussy"),
            new("Брюнетки", $"{url}?sort={sort}&c=a1-brunette"),
            new("Буккаке", $"{url}?sort={sort}&c=bukkake"),
            new("В гольфах", $"{url}?sort={sort}&c=knee-socks"),
            new("В клубе", $"{url}?sort={sort}&c=club"),
            new("В красивом белье", $"{url}?sort={sort}&c=lingerie"),
            new("В майке", $"{url}?sort={sort}&c=shirt"),
            new("В масле", $"{url}?sort={sort}&c=oiled"),
            new("В машине", $"{url}?sort={sort}&c=car-porn"),
            new("В очках", $"{url}?sort={sort}&c=glasses"),
            new("В презервативе", $"{url}?sort={sort}&c=condom"),
            new("В спальне", $"{url}?sort={sort}&c=bedroom"),
            new("В спортзале", $"{url}?sort={sort}&c=gym-porn"),
            new("В чулках", $"{url}?sort={sort}&c=stockings"),
            new("Вебкамера", $"{url}?sort={sort}&c=webcam"),
            new("Волосатая пизда", $"{url}?sort={sort}&c=hairy"),
            new("Гибкие", $"{url}?sort={sort}&c=flexible"),
            new("Глотает сперму", $"{url}?sort={sort}&c=cum-swallow"),
            new("Горничная", $"{url}?sort={sort}&c=maid"),
            new("Госпожа", $"{url}?sort={sort}&c=mistress"),
            new("Групповуха", $"{url}?sort={sort}&c=group-porno"),
            new("Дилдо", $"{url}?sort={sort}&c=dildo"),
            new("Длинные волосы", $"{url}?sort={sort}&c=long-hair"),
            new("Доктор", $"{url}?sort={sort}&c=doctor"),
            new("Домашнее порно", $"{url}?sort={sort}&c=amateur"),
            new("Дрочит парню", $"{url}?sort={sort}&c=handjob"),
            new("Евро", $"{url}?sort={sort}&c=a1-europe"),
            new("Жесть", $"{url}?sort={sort}&c=fun"),
            new("ЖМЖ", $"{url}?sort={sort}&c=a1-threesome"),
            new("Измена", $"{url}?sort={sort}&c=cheating"),
            new("Интимные стрижки", $"{url}?sort={sort}&c=intimate-haircut"),
            new("Кляп в рот", $"{url}?sort={sort}&c=gag"),
            new("Короткие волосы", $"{url}?sort={sort}&c=short-hair"),
            new("Косички", $"{url}?sort={sort}&c=braids"),
            new("Красивая грудь", $"{url}?sort={sort}&c=nice-tits-porn"),
            new("Красивые", $"{url}?sort={sort}&c=a1-babe"),
            new("Красивые попки", $"{url}?sort={sort}&c=ass"),
            new("Красивый секс", $"{url}?sort={sort}&c=beautiful"),
            new("Крупным планом", $"{url}?sort={sort}&c=closeup"),
            new("Куколд", $"{url}?sort={sort}&c=cuckold"),
            new("Куни", $"{url}?sort={sort}&c=cunni"),
            new("Лесби", $"{url}?sort={sort}&c=lesbi-porno"),
            new("Лижет попу", $"{url}?sort={sort}&c=ass-licking-porn"),
            new("Массаж", $"{url}?sort={sort}&c=massage"),
            new("Мастурбация", $"{url}?sort={sort}&c=a1-masturbation"),
            new("Мачеха", $"{url}?sort={sort}&c=a1-stepmom"),
            new("Медсестра", $"{url}?sort={sort}&c=nurse"),
            new("Между сисек", $"{url}?sort={sort}&c=tits-fuck"),
            new("Межрассовое", $"{url}?sort={sort}&c=interracial"),
            new("МЖМ", $"{url}?sort={sort}&c=2man-woman"),
            new("Минет", $"{url}?sort={sort}&c=blowjob"),
            new("Молодые", $"{url}?sort={sort}&c=teen"),
            new("На каблуках", $"{url}?sort={sort}&c=heels"),
            new("На пляже", $"{url}?sort={sort}&c=beach"),
            new("На природе", $"{url}?sort={sort}&c=outdoor-sex"),
            new("На публике", $"{url}?sort={sort}&c=a1-public"),
            new("На столе", $"{url}?sort={sort}&c=table"),
            new("Наездница", $"{url}?sort={sort}&c=cowgirl"),
            new("Наручники", $"{url}?sort={sort}&c=handcuffs"),
            new("Натуральные сиськи", $"{url}?sort={sort}&c=a1-natural-tits"),
            new("Негритянки", $"{url}?sort={sort}&c=black-girl"),
            new("Негры", $"{url}?sort={sort}&c=black"),
            new("Негры с блондинками", $"{url}?sort={sort}&c=blacks-on-blondes"),
            new("Некрасивая грудь", $"{url}?sort={sort}&c=ugly-tits"),
            new("Няня", $"{url}?sort={sort}&c=babysitter"),
            new("Писает", $"{url}?sort={sort}&c=pissing"),
            new("Плётка", $"{url}?sort={sort}&c=whip"),
            new("Под водой", $"{url}?sort={sort}&c=underwater"),
            new("Подчинение", $"{url}?sort={sort}&c=submission"),
            new("Поза 69", $"{url}?sort={sort}&c=69"),
            new("Порно зрелых", $"{url}?sort={sort}&c=milfs"),
            new("Реслинг", $"{url}?sort={sort}&c=wrestling"),
            new("Русское домашнее порно", $"{url}?sort={sort}&c=russian-amateur"),
            new("Русское порно", $"{url}?sort={sort}&c=ruporn"),
            new("Рыжие", $"{url}?sort={sort}&c=redhead"),
            new("С латинками", $"{url}?sort={sort}&c=latina-sex"),
            new("С невестой", $"{url}?sort={sort}&c=bride"),
            new("С тренером", $"{url}?sort={sort}&c=couch-porn"),
            new("Свингеры", $"{url}?sort={sort}&c=swingers"),
            new("Секретарша", $"{url}?sort={sort}&c=secretary-porn"),
            new("Секс в общаге", $"{url}?sort={sort}&c=dorm-porn"),
            new("Секс в офисе", $"{url}?sort={sort}&c=office-sex"),
            new("Секс на кухне", $"{url}?sort={sort}&c=kitchen"),
            new("Секс с бывшей", $"{url}?sort={sort}&c=exgfs"),
            new("Секс-игрушки", $"{url}?sort={sort}&c=sex-toys"),
            new("Секс-машина", $"{url}?sort={sort}&c=sex-machines"),
            new("Секс-рабыня", $"{url}?sort={sort}&c=slave"),
            new("Силиконовые сиськи", $"{url}?sort={sort}&c=silicone-tits"),
            new("Сквирт", $"{url}?sort={sort}&c=squirting"),
            new("Соло", $"{url}?sort={sort}&c=a1-solo"),
            new("Сперма вытекает", $"{url}?sort={sort}&c=creampie"),
            new("Сперма на груди", $"{url}?sort={sort}&c=cum-on-tits"),
            new("Сперма на лице", $"{url}?sort={sort}&c=facial"),
            new("Сперма на ногах", $"{url}?sort={sort}&c=sperma-na-nogah"),
            new("Сперма на пизде", $"{url}?sort={sort}&c=cum-on-pussy"),
            new("Сперма на попе", $"{url}?sort={sort}&c=cum-on-ass"),
            new("Старые с молодыми", $"{url}?sort={sort}&c=old-and-young"),
            new("Страпон", $"{url}?sort={sort}&c=strapon"),
            new("Стриптиз", $"{url}?sort={sort}&c=strip"),
            new("Студентка", $"{url}?sort={sort}&c=schoolgirls"),
            new("Студенты", $"{url}?sort={sort}&c=students"),
            new("Стюардесса", $"{url}?sort={sort}&c=styuardessa"),
            new("Трах", $"{url}?sort={sort}&c=trah"),
            new("Учит трахаться", $"{url}?sort={sort}&c=teaching"),
            new("Учитель", $"{url}?sort={sort}&c=teacher"),
            new("Учительница", $"{url}?sort={sort}&c=teacher-milf"),
            new("Футфетиш", $"{url}?sort={sort}&c=foot-fetish"),
            new("Худые", $"{url}?sort={sort}&c=skinny-porn"),
            new("Чешское порно", $"{url}?sort={sort}&c=czech-porn"),
            new("Член из дырки", $"{url}?sort={sort}&c=gloryhole-porn"),
            new("Эротика", $"{url}?sort={sort}&c=erotic")
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
    async public static Task<StreamItem> StreamLinks(HttpHydra http, string uri, string host, string url, Func<string, Task<string>> onlocation = null)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        string stream_link = null;
        List<PlaylistItem> recomends = null;

        await http.GetSpan($"{host}/{url}", html =>
        {
            foreach (string q in new string[] { "video_alt_url", "video_url" })
            {
                stream_link = Rx.Groups(html, $"{q}:([\t ]+)?('|\")(?<link>[^\"']+)")["link"].Value;
                if (!string.IsNullOrEmpty(stream_link))
                    break;
            }

            if (!string.IsNullOrEmpty(stream_link))
                recomends = Playlist(uri, html);
        },
        addheaders: HeadersModel.Init(
            ("sec-fetch-dest", "document"),
            ("sec-fetch-mode", "navigate"),
            ("sec-fetch-site", "same-origin"),
            ("sec-fetch-user", "?1"),
            ("upgrade-insecure-requests", "1")
        ));

        if (string.IsNullOrEmpty(stream_link))
            return null;

        if (onlocation != null)
        {
            string location = await onlocation.Invoke(stream_link);
            if (location == null || stream_link == location || location.Contains("_file/"))
                return null;

            stream_link = location;
        }

        return new StreamItem()
        {
            qualitys = new Dictionary<string, string>()
            {
                ["auto"] = stream_link
            },
            recomends = recomends
        };
    }
    #endregion
}
