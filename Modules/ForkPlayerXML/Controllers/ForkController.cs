using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using System.Collections.Generic;

namespace ForkXML;

public class ForkController : BaseController
{
    [HttpGet]
    [AllowAnonymous]
    [Route("fxml")]
    public ActionResult Index(string box_mac)
    {
        if (CoreInit.conf.accsdb.enable && requestInfo.user == null)
        {
            return new JsonResult(new
            {
                title = "Lampac",
                all_local = "directly",
                channels = new List<ForkPlaylistItem>
                {
                    new ForkPlaylistItem()
                    {
                        title = "Ошибка доступа",
                        description = $"Добавьте {box_mac}",
                        playlist_url = $"{host}/fxml",
                        logo_30x30 = Icon.Error
                    }
                }
            });
        }
        else
        {
            var channels = new List<ForkPlaylistItem>()
            {
                new ForkPlaylistItem()
                {
                    search_on = "search_on",
                    title = "Поиск",
                    playlist_url = $"{host}/fxml/cub",
                    logo_30x30 = Icon.Search
                },
                new ForkPlaylistItem()
                {
                    title = "Сейчас смотрят",
                    playlist_url = $"{host}/fxml/cub?sort=now_playing",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Фильмы",
                    playlist_url = $"{host}/fxml/cub?cat=movie&without_genres=16",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Сериалы",
                    playlist_url = $"{host}/fxml/cub?cat=tv&without_genres=16",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Мультфильмы",
                    playlist_url = $"{host}/fxml/cub?cat=movie&genre=16",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Мультсериалы",
                    playlist_url = $"{host}/fxml/cub?cat=tv&genre=16",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Аниме",
                    playlist_url = $"{host}/fxml/cub?cat=anime",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Каталог",
                    playlist_url = $"{host}/catalog",
                    logo_30x30 = Icon.CdnSearch
                },
                new ForkPlaylistItem()
                {
                    title = "Клубничка 18+",
                    playlist_url = $"{host}/sisi",
                    logo_30x30 = Icon.Adult
                }
            };

            return Json(new
            {
                title = "Lampac",
                all_local = "directly",
                //icon = "",
                channels = channels
            });
        }
    }
}
