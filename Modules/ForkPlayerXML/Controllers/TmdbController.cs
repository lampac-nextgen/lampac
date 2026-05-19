using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;

namespace ForkXML;

public class TmdbController : BaseController
{
    [HttpGet]
    [Route("fxml/tmdb")]
    async public Task<ActionResult> Index(string cat, string sort, int page = 1)
    {
        if (cat != "dorama")
            return BadRequest();

        string uri = $"{host}/fxml/tmdb";
        string additionalArgs = AdditionalArgs();
        string memkey = $"forkxml:tmdb:list:{cat}:{sort}:{page}{additionalArgs}";

        if (!memoryCache.TryGetValue(memkey, out TmdbList cache) || cache == null)
        {
            var root = await Http.Get<TmdbList>(DoramaDiscoverUrl(sort, page));
            if (root?.results == null || root.results.Count == 0)
                return BadRequest();

            cache = root;
            memoryCache.Set(memkey, cache, DateTime.Now.AddMinutes(5));
        }

        var menu = new List<ForkPlaylistItem>()
        {
            new ForkPlaylistItem()
            {
                title = $"Сортировка: {SortTitle(sort)}",
                playlist_url = "submenu",
                submenu = SortMenu(uri, cat, page, additionalArgs),
                logo_30x30 = Icon.Filter
            }
        };
        var playlists = new List<ForkPlaylistItem>();

        foreach (var movie in cache.results)
        {
            string title = movie.title ?? movie.name;
            string original_title = movie.original_title ?? movie.original_name;
            string end_title = string.IsNullOrEmpty(original_title) ? title : $"{title} / {original_title}";
            int serial = string.IsNullOrEmpty(movie.title ?? movie.original_title) ? 1 : 0;

            string args = $"id={movie.id}&source=tmdb&external_ids=true&imdb_id={movie.imdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&original_language={movie.original_language}&year={(movie.release_date ?? movie.first_air_date)?.Split("-")?[0]}";

            playlists.Add(new ForkPlaylistItem()
            {
                title = title ?? original_title,
                description = Utilities.Description(movie, end_title),
                logo_30x30 = Icon.Folder,
                playlist_url = $"{host}/lite/events?{args}",
            });
        }

        return Json(new
        {
            title = "Lampac",
            align = "left",
            menu = menu,
            channels = playlists,
            next_page_url = HasNextPage(page, playlists.Count, cache.total_pages) ? ListUrl(uri, cat, sort, page + 1, additionalArgs) : null
        });
    }

    string AdditionalArgs()
    {
        string additionalArgs = "";

        foreach (var q in Request.Query)
        {
            if (q.Key == "cat" || q.Key == "sort" || q.Key == "page")
                continue;

            foreach (var value in q.Value)
                additionalArgs += $"&{HttpUtility.UrlEncode(q.Key)}={HttpUtility.UrlEncode(value)}";
        }

        return additionalArgs;
    }

    string DoramaDiscoverUrl(string sort, int page)
    {
        var now = DateTime.UtcNow;
        var query = new List<string>()
        {
            $"api_key={HttpUtility.UrlEncode(TmdbApiKey())}",
            "with_original_language=ko",
            "with_genres=18",
            "include_adult=false",
            $"page={page}",
            "language=ru-RU"
        };

        switch (sort)
        {
            case "update":
                query.Add("sort_by=air_date.desc");
                query.Add($"air_date.gte={now.AddDays(-14):yyyy-MM-dd}");
                query.Add($"air_date.lte={now:yyyy-MM-dd}");
                break;
            case "ongoing":
                query.Add("sort_by=popularity.desc");
                query.Add("with_status=0%7C2");
                query.Add($"first_air_date.lte={now:yyyy-MM-dd}");
                query.Add($"air_date.gte={now:yyyy-MM-dd}");
                query.Add($"air_date.lte={now.AddDays(21):yyyy-MM-dd}");
                break;
            case "top":
                query.Add("sort_by=popularity.desc");
                break;
            case "rated":
                query.Add("sort_by=vote_average.desc");
                query.Add("vote_average.gte=7");
                query.Add("vote_count.gte=50");
                break;
            case "latest":
                query.Add("sort_by=first_air_date.desc");
                query.Add($"first_air_date.lte={now:yyyy-MM-dd}");
                break;
            case "now":
                query.Add("sort_by=first_air_date.desc");
                query.Add($"first_air_date_year={now.Year}");
                query.Add($"first_air_date.lte={now:yyyy-MM-dd}");
                break;
            case "now_playing":
            default:
                query.Add("sort_by=popularity.desc");
                query.Add($"air_date.gte={now.AddDays(-14):yyyy-MM-dd}");
                query.Add($"air_date.lte={now.AddDays(14):yyyy-MM-dd}");
                break;
        }

        string vote = Request.Query["vote"].ToString();
        if (!string.IsNullOrWhiteSpace(vote))
            query.Add($"vote_average.gte={HttpUtility.UrlEncode(vote)}");

        return "https://api.themoviedb.org/3/discover/tv?" + string.Join("&", query);
    }

    static string TmdbApiKey()
        => string.IsNullOrEmpty(CoreInit.conf.cub?.api_key)
            ? "4ef0d7355d9ffb5151e987764708ce96"
            : CoreInit.conf.cub.api_key;

    static string ListUrl(string uri, string cat, string sort, int page, string additionalArgs)
    {
        string url = $"{uri}?cat={HttpUtility.UrlEncode(cat)}&sort={HttpUtility.UrlEncode(sort)}&page={page}";
        return url + additionalArgs;
    }

    static string SortTitle(string sort) => sort switch
    {
        "now_playing" => "сейчас смотрят",
        "update" => "новые серии",
        "ongoing" => "онгоинги",
        "top" => "популярное",
        "rated" => "с высоким рейтингом",
        "latest" => "последнее добавление",
        "now" => "новинки этого года",
        _ => "выбрать"
    };

    static List<ForkPlaylistItem> SortMenu(string uri, string cat, int page, string additionalArgs)
        => new List<ForkPlaylistItem>()
    {
        new ForkPlaylistItem()
        {
            title = "Сейчас смотрят",
            playlist_url = ListUrl(uri, cat, "now_playing", page, additionalArgs),
            logo_30x30 = Icon.Folder
        },
        new ForkPlaylistItem()
        {
            title = "Новые серии",
            playlist_url = ListUrl(uri, cat, "update", page, additionalArgs),
            logo_30x30 = Icon.Folder
        },
        new ForkPlaylistItem()
        {
            title = "Онгоинги",
            playlist_url = ListUrl(uri, cat, "ongoing", page, additionalArgs),
            logo_30x30 = Icon.Folder
        },
        new ForkPlaylistItem()
        {
            title = "Популярное",
            playlist_url = ListUrl(uri, cat, "top", page, additionalArgs),
            logo_30x30 = Icon.Folder
        },
        new ForkPlaylistItem()
        {
            title = "Последнее добавление",
            playlist_url = ListUrl(uri, cat, "latest", page, additionalArgs),
            logo_30x30 = Icon.Folder
        },
        new ForkPlaylistItem()
        {
            title = "Новинки этого года",
            playlist_url = ListUrl(uri, cat, "now", page, additionalArgs),
            logo_30x30 = Icon.Folder
        },
        new ForkPlaylistItem()
        {
            title = "С высоким рейтингом",
            playlist_url = ListUrl(uri, cat, "rated", page, additionalArgs),
            logo_30x30 = Icon.Folder
        }
    };

    static bool HasNextPage(int page, int count, int totalPages)
        => totalPages > 0 ? page < totalPages : count == 20;
}
