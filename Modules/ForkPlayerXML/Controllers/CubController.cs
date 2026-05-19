using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;

namespace ForkXML;

public class CubController : BaseController
{
    [HttpGet]
    [Route("fxml/cub")]
    async public Task<ActionResult> Index(string search, string cat, string sort, int without_genres, int genre, int page = 1)
    {
        string memkey = $"forkxml:list:{search}:{cat}:{sort}:{without_genres}:{genre}:{page}";

        if (!memoryCache.TryGetValue(memkey, out TmdbList cache))
        {
            var root = await Http.Get<TmdbList>("http://tmdb.cub.red/" + $"?query={HttpUtility.UrlEncode(search)}&cat={cat}&sort={sort}&without_genres={without_genres}&genre={genre}&page={page}&results=60");
            if (root?.results == null || root.results.Count == 0)
                return BadRequest();

            cache = root;
            memoryCache.Set(memkey, root, DateTime.Now.AddMinutes(5));
        }

        var playlists = new List<ForkPlaylistItem>();

        foreach (var movie in cache.results)
        {
            string title = movie.title ?? movie.name;
            string original_title = movie.original_title ?? movie.original_name;
            string end_title = string.IsNullOrEmpty(original_title) ? title : $"{title} / {original_title}";
            int serial = string.IsNullOrEmpty(movie.title ?? movie.original_title) ? 1 : 0;

            string release_date = movie.release_date ?? movie.first_air_date;
            if (release_date != null)
                release_date = release_date.Split("-")[0];

            playlists.Add(new ForkPlaylistItem()
            {
                title = title ?? original_title,
                description = Utilities.Description(movie, end_title),
                logo_30x30 = Icon.Folder,
                playlist_url = $"{host}/lite/events?id={movie.id}&imdb_id={movie.imdb_id}&kinopoisk_id={movie.kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&original_language={movie.original_language}&year={release_date}",
            });
        }

        string uri = $"{host}/fxml/cub";

        return Json(new
        {
            title = "Lampac",
            align = "left",
            menu = BuilderMenu(uri, search, cat, sort, without_genres, genre, page),
            channels = playlists,
            next_page_url = cache.total_pages > cache.page
                ? $"{uri}?query={HttpUtility.UrlEncode(search)}&cat={cat}&sort={sort}&without_genres={without_genres}&genre={genre}&page={page + 1}"
                : null
        });
    }


    static List<ForkPlaylistItem> BuilderMenu(string uri, string search, string cat, string sort, int without_genres, int genre, int page)
    {
        var menu = new List<ForkPlaylistItem>();

        if (string.IsNullOrEmpty(search) && sort != "releases")
        {
            #region Сортировка
            var search_menu = new List<ForkPlaylistItem>()
            {
                new ForkPlaylistItem()
                {
                    title = "Новинки",
                    playlist_url = $"{uri}?cat={cat}&without_genres={without_genres}&genre={genre}&page={page}&sort=now",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Популярное",
                    playlist_url = $"{uri}?cat={cat}&without_genres={without_genres}&genre={genre}&page={page}&sort=top",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Cейчас смотрят",
                    playlist_url = $"{uri}?cat={cat}&without_genres={without_genres}&genre={genre}&page={page}&sort=now_playing",
                    logo_30x30 = Icon.Folder
                }
            };

            if (cat == "tv")
            {
                search_menu.Add(new ForkPlaylistItem()
                {
                    title = "Онгоинги",
                    playlist_url = $"{uri}?cat={cat}&without_genres={without_genres}&genre={genre}&page={page}&sort=airing",
                    logo_30x30 = Icon.Folder
                });
            }

            menu.Add(new ForkPlaylistItem()
            {
                title = $"Сортировка: {SortName(sort)}",
                playlist_url = "submenu",
                submenu = search_menu,
                logo_30x30 = Icon.Filter
            });
            #endregion

            #region Жанр
            var genres_menu = new List<ForkPlaylistItem>();

            foreach (var g in genre_db)
            {
                if (g.Key == 16 || g.Key == 99)
                    continue;

                genres_menu.Add(new ForkPlaylistItem()
                {
                    title = g.Value,
                    playlist_url = $"{uri}?cat={cat}&without_genres={without_genres}&sort={sort}&page={page}&genre={g.Key}",
                    logo_30x30 = Icon.Folder
                });
            }

            menu.Add(new ForkPlaylistItem()
            {
                title = $"Жанр: {(genre == 0 ? "выбрать" : genre_db[genre])}",
                playlist_url = "submenu",
                submenu = genres_menu,
                logo_30x30 = Icon.Filter
            });
            #endregion
        }

        return menu;
    }

    static string SortName(string sort) => sort switch
    {
        "now_playing" => "сейчас смотрят",
        "airing" => "онгоинги",
        "top" => "популярное",
        "now" => "новинки",
        _ => "выбрать"
    };

    static IReadOnlyDictionary<int, string> genre_db = new Dictionary<int, string>()
    {
        [28] = "боевик",
        [12] = "приключения",
        [16] = "мультфильм",
        [35] = "комедия",
        [80] = "криминал",
        [99] = "документальный",
        [18] = "драма",
        [10751] = "семейный",
        [14] = "фэнтези",
        [36] = "история",
        [27] = "ужасы",
        [10402] = "музыка",
        [9648] = "детектив",
        [10749] = "мелодрама",
        [878] = "фантастика",
        [53] = "триллер",
        [10752] = "военный",
        [37] = "вестерн"
    };
}
