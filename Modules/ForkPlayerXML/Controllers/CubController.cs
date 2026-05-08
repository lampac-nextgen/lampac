using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
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
    async public Task<ActionResult> Index(string search, string cat, string sort, int page = 1)
    {
        string uri = $"{host}/fxml/cub";

        string memkey = $"forkxml:list:{search}:{cat}:{sort}:{page}";

        if (!memoryCache.TryGetValue(memkey, out List<TmdbMovie> movies) || movies == null)
        {
            var root = await Http.Get<JObject>("http://tmdb.cub.red/" + $"?query={HttpUtility.UrlEncode(search)}&cat={cat}&sort={sort}&page={page}&results=60");
            if (root == null || !root.ContainsKey("results"))
                return BadRequest();

            movies = root.Value<JArray>("results")?.ToObject<List<TmdbMovie>>();
            if (movies == null || movies.Count == 0)
                return BadRequest();

            memoryCache.Set(memkey, movies, DateTime.Now.AddMinutes(5));
        }

        var menu = new List<ForkPlaylistItem>();
        var playlists = new List<ForkPlaylistItem>();

        foreach (var movie in movies)
        {
            string title = movie.title ?? movie.name;
            string original_title = movie.original_title ?? movie.original_name;
            string end_title = string.IsNullOrEmpty(original_title) ? title : $"{title} / {original_title}";
            int serial = string.IsNullOrEmpty(movie.title ?? movie.original_title) ? 1 : 0;

            string args = $"imdb_id={movie.imdb_id}&kinopoisk_id={movie.kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&original_language={movie.original_language}&year={(movie.release_date ?? movie.first_air_date)?.Split("-")?[0]}";

            playlists.Add(new ForkPlaylistItem()
            {
                title = title ?? original_title,
                description = Description(movie, end_title),
                logo_30x30 = Icon.Folder,
                playlist_url = $"{host}/lite/events?{args}",
            });
        }

        if (playlists.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(search))
                return BadRequest();
        }

        if (string.IsNullOrEmpty(search) && sort != "releases")
        {
            menu.Add(new ForkPlaylistItem()
            {
                title = $"Сортировка: {(string.IsNullOrEmpty(sort) ? "выбрать" : sort.Replace("now_playing", "сейчас смотрят").Replace("now", "новинки").Replace("top", "популярное"))}",
                playlist_url = "submenu",
                submenu = new List<ForkPlaylistItem>()
                {
                    new ForkPlaylistItem()
                    {
                        title = "Новинки",
                        playlist_url = $"{uri}?cat={cat}&page={page}&sort=now",
                        logo_30x30 = Icon.Folder
                    },
                    new ForkPlaylistItem()
                    {
                        title = "Популярное",
                        playlist_url = $"{uri}?cat={cat}&page={page}&sort=top",
                        logo_30x30 = Icon.Folder
                    },
                    new ForkPlaylistItem()
                    {
                        title = "Cейчас смотрят",
                        playlist_url = $"{uri}?cat={cat}&page={page}&sort=now_playing",
                        logo_30x30 = Icon.Folder
                    }
                },
                logo_30x30 = Icon.Filter
            });
        }

        return Json(new
        {
            title = "Lampac",
            align = "left",
            menu = menu,
            channels = playlists,
            next_page_url = playlists.Count == 60 ? $"{uri}?query={HttpUtility.UrlEncode(search)}&cat={cat}&sort={sort}&page={page + 1}" : null
        });
    }


    static string Description(TmdbMovie movie, string end_title)
        => $@"<div class=""description"" style=""display: block; top: 38px; max-height: 1042px;""><div id=""title"" style=""color: #699bbb;""><strong>{end_title}</strong></div><br><div id=""cover_div"" style=""float: left; margin: 0px 1.8% 0px 0px;""><img id=""cover_img"" style=""width: 184px; "" src=""http://image.tmdb.org/t/p/w200/{movie.poster_path}""></div><div><strong><span style=""color: #3974d0;"">Выход:</span></strong> {(movie.release_date ?? movie.first_air_date).Split("-")[0]}<br><strong><span style=""color: #339966;"">Качество:</span></strong> {movie.release_quality}<br><div id=""footer"" style=""clear: both;  ""><br>{movie.overview}</div></div></div>";
}
