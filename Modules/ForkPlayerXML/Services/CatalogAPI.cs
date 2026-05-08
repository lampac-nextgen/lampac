using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Events;
using System.Collections.Generic;
using System.Web;

namespace ForkXML;

public static class CatalogAPI
{
    #region Channels
    public static ActionResult Channels(EventCatalogChannels e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        var forklist = new List<ForkPlaylistItem>();
        string host = CoreInit.Host(e.httpContext);

        foreach (var ch in e.channels)
        {
            var submenu = new List<ForkPlaylistItem>();

            foreach (var sub in ch.Value.ToObject<Dictionary<string, JToken>>())
            {
                if (sub.Key == "search")
                {
                    submenu.Add(new ForkPlaylistItem()
                    {
                        title = "Поиск",
                        search_on = "search_on",
                        playlist_url = host + sub.Value.ToString(),
                        logo_30x30 = Icon.Search
                    });
                }
                else if (sub.Key is "movie" or "tv" or "anime" or "cartoons")
                {
                    var cats = new List<ForkPlaylistItem>();

                    foreach (var cat in sub.Value.ToObject<Dictionary<string, string>>())
                    {
                        cats.Add(new ForkPlaylistItem()
                        {
                            title = cat.Key,
                            playlist_url = host + cat.Value,
                            logo_30x30 = Icon.Folder
                        });
                    }

                    if (cats.Count > 0)
                    {
                        if (cats.Count == 1)
                        {
                            submenu.Add(new ForkPlaylistItem()
                            {
                                title = sub.Key,
                                playlist_url = cats[0].playlist_url,
                                logo_30x30 = Icon.Folder
                            });
                        }
                        else
                        {
                            submenu.Add(new ForkPlaylistItem()
                            {
                                title = sub.Key,
                                playlist_url = "submenu",
                                submenu = cats,
                                logo_30x30 = Icon.Folder
                            });
                        }
                    }
                }
            }

            if (submenu.Count > 0)
            {
                forklist.Add(new ForkPlaylistItem()
                {
                    title = ch.Key,
                    playlist_url = "submenu",
                    submenu = submenu,
                    logo_30x30 = Icon.Folder
                });
            }
        }

        return new JsonResult(new
        {
            title = "Lampac",
            all_local = "directly",
            channels = forklist
        });
    }
    #endregion

    #region List
    public static ActionResult List(EventCatalogList e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        var forklist = new List<ForkPlaylistItem>();
        string host = CoreInit.Host(e.httpContext);

        foreach (var pl in e.playlists)
        {
            forklist.Add(new ForkPlaylistItem()
            {
                title = pl.title,
                playlist_url = $"{host}/catalog/card?uri={HttpUtility.UrlEncode(pl.id)}&plugin={e.plugin}&type={(pl.is_serial ? "tv" : "movie")}",
                logo_30x30 = Icon.Folder,
                description = Description(pl),
            });
        }

        return new JsonResult(new
        {
            title = "Lampac",
            align = "left",
            channels = forklist,
            next_page_url = e.total_pages > e.page
                ? $"{host}{e.httpContext.Request.Path}?page={e.page + 1}&{Utilities.ClearArgs(e.httpContext.Request.Query)}"
                : null
        });
    }
    #endregion

    #region Card
    public static ActionResult Card(EventCatalogCard e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        string imdb_id = e.card.Value<string>("imdb_id");
        string title = e.card.Value<string>("title") ?? e.card.Value<string>("name");
        string original_title = e.card.Value<string>("original_title") ?? e.card.Value<string>("original_name");
        string original_language = e.card.Value<string>("original_language");
        string year = (e.card.Value<string>("release_date") ?? e.card.Value<string>("first_air_date"))?.Split("-")?[0];
        int serial = e.type == "tv" ? 1 : 0;

        string args = $"source={e.type}&id={HttpUtility.UrlEncode(e.uri)}&imdb_id={imdb_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&original_language={original_language}&year={year}";

        return new LocalRedirectResult($"/lite/events?{args}" + Utilities.ForkArgs(e.httpContext.Request.Query));
    }
    #endregion


    static string Description(Shared.Models.Catalog.PlaylistItem pl)
    {
        string title = pl.title;
        string original_title = pl.original_title;
        string img = pl.img;
        string name = string.IsNullOrEmpty(original_title) || original_title == title
            ? title
            : $"{title} / {original_title}";

        return $@"<div class=""description"" style=""display: block; top: 38px; max-height: 1042px;""><div id=""title"" style=""color: #699bbb;""><strong>{name}</strong></div><br><div id=""cover_div"" style=""float: left; margin: 0px 1.8% 0px 0px;""><img id=""cover_img"" style=""width: 184px; "" src=""{img}""></div><div><strong><span style=""color: #3974d0;"">Выход:</span></strong> {pl.year}</div></div></div>";
    }
}
