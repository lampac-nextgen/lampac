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
                    title = "Дорамы",
                    playlist_url = "submenu",
                    submenu = DoramaMenu(host),
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
                    title = "Документалка",
                    playlist_url = $"{host}/fxml/cub?genre=99",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "ТВ Шоу",
                    playlist_url = $"{host}/fxml/cub?cat=tvshow",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Цифр. релизы",
                    playlist_url = $"{host}/fxml/cub?sort=releases",
                    logo_30x30 = Icon.Folder
                },
                new ForkPlaylistItem()
                {
                    title = "Каталог",
                    playlist_url = $"{host}/catalog",
                    logo_30x30 = Icon.CdnSearch
                }
            };

            if (CoreInit.modules?.Exists(i => i?.enable == true && i.name == "SISI") == true)
            {
                channels.Add(new ForkPlaylistItem()
                {
                    title = "Клубничка 18+",
                    playlist_url = $"{host}/sisi",
                    logo_30x30 = Icon.Adult
                });
            }

            return Json(new
            {
                title = "Lampac",
                all_local = "directly",
                //icon = "",
                channels = channels
            });
        }


        static List<ForkPlaylistItem> DoramaMenu(string host)
            => new List<ForkPlaylistItem>()
        {
            new ForkPlaylistItem()
            {
                title = "Сейчас смотрят",
                playlist_url = $"{host}/fxml/tmdb?cat=dorama&sort=now_playing",
                logo_30x30 = Icon.Folder
            },
            new ForkPlaylistItem()
            {
                title = "Новые серии",
                playlist_url = $"{host}/fxml/tmdb?cat=dorama&sort=update",
                logo_30x30 = Icon.Folder
            },
            new ForkPlaylistItem()
            {
                title = "Онгоинги",
                playlist_url = $"{host}/fxml/tmdb?cat=dorama&sort=ongoing",
                logo_30x30 = Icon.Folder
            },
            new ForkPlaylistItem()
            {
                title = "Популярное",
                playlist_url = $"{host}/fxml/tmdb?cat=dorama&sort=top",
                logo_30x30 = Icon.Folder
            },
            new ForkPlaylistItem()
            {
                title = "Последнее добавление",
                playlist_url = $"{host}/fxml/tmdb?cat=dorama&sort=latest",
                logo_30x30 = Icon.Folder
            },
            new ForkPlaylistItem()
            {
                title = "Новинки этого года",
                playlist_url = $"{host}/fxml/tmdb?cat=dorama&sort=now",
                logo_30x30 = Icon.Folder
            },
            new ForkPlaylistItem()
            {
                title = "С высоким рейтингом",
                playlist_url = $"{host}/fxml/tmdb?cat=dorama&sort=rated",
                logo_30x30 = Icon.Folder
            }
        };
    }
}
