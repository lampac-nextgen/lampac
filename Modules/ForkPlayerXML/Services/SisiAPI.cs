using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Events;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ForkXML;

public static class SisiAPI
{
    #region Channels
    public static ActionResult Channels(EventSisiChannels e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        var forklist = new List<ForkPlaylistItem>();

        foreach (var ch in e.channels.Where(i => i.displayindex > 1).OrderBy(i => i.displayindex))
        {
            forklist.Add(new ForkPlaylistItem()
            {
                title = ch.title,
                playlist_url = ch.playlist_url,
                logo_30x30 = Icon.Folder
            });
        }

        return new JsonResult(new
        {
            title = "Lampac",
            all_local = "directly",
            channels = forklist
        });
    }
    #endregion

    #region PlaylistResult
    public static ActionResult PlaylistResult(EventSisiPlaylistResult e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        string box_mac = e.httpContext.Request.Query["box_mac"];
        string host = CoreInit.Host(e.httpContext);

        #region playlists
        var forklist = new List<ForkPlaylistItem>();

        foreach (var pl in e.playlists)
        {
            string video = pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? pl.video
                : $"{host}/{pl.video}";

            if (!video.Contains(host, StringComparison.OrdinalIgnoreCase))
                video = e.controller.HostStreamProxy(e.init, video, e.headers_stream);

            string picture = e.controller.HostImgProxy(e.init, pl.picture, headers: e.headers_image);

            var htmlpl = new ForkPlaylistItem()
            {
                title = pl.name,
                stream_url = pl.json ? null : video,
                playlist_url = pl.json ? video : null,
                position = "html",
                template = $"<div class=\"grid\"><small>{pl.time}</small><img class=\"gridmage\" src=\"{picture}\"><strong>{pl.name}</strong></div>"
            };

            if ((forklist.Count % 4) == 0)
                htmlpl.br = 1;

            forklist.Add(htmlpl);
        }
        #endregion

        #region pages
        if (!int.TryParse(e.httpContext.Request.Query["pg"], out int page))
            page = 1;

        int total_pages = e.total_pages;
        if (total_pages == 0)
            total_pages = page + 10;

        for (int pg = page + 1; pg < total_pages; pg++)
        {
            string args = Utilities.ClearArgs(e.httpContext.Request.Query);

            forklist.Add(new ForkPlaylistItem()
            {
                title = pg.ToString(),
                playlist_url = host + e.httpContext.Request.Path + $"?pg={pg}" + (!string.IsNullOrEmpty(args) ? $"&{args}" : string.Empty),
                position = "html",
                template = $"<div class=\"page\">{pg}</div>",
            });

            if (pg == (page + 1))
            {
                forklist[forklist.Count - 1].br = 1;
                forklist[forklist.Count - 1].before = "<div class=\"navigate\" style=\"text-align: center;\">";
            }
        }

        forklist[forklist.Count - 1].after = "</div>";
        #endregion

        #region menu
        var menu = new List<ForkPlaylistItem>();

        if (e.menu != null)
        {
            foreach (var item in e.menu)
            {
                if (item.title.Equals("Поиск", StringComparison.OrdinalIgnoreCase))
                {
                    menu.Add(new ForkPlaylistItem()
                    {
                        title = item.title,
                        search_on = "search_on",
                        playlist_url = item.playlist_url,
                        logo_30x30 = Icon.Search
                    });
                }
                else
                {
                    menu.Add(new ForkPlaylistItem()
                    {
                        title = item.title,
                        playlist_url = item.playlist_url,
                        submenu = item.submenu?.Select(i => new ForkPlaylistItem()
                        {
                            title = i.title,
                            playlist_url = i.playlist_url
                        }).ToList(),
                        logo_30x30 = Icon.Filter
                    });
                }
            }
        }
        #endregion

        return new JsonResult(new
        {
            title = "Lampac",
            css = ".grid{height:247px; border-radius:4px; background-image: linear-gradient(#2D1F3A, #23182D);overflow:hidden;margin: 11px;text-align:center;}" +
                  "small{font-size: 17px;background:#4a3042;color:#fff;margin:4px;padding:4px;border-radius:4px;float:left;position:absolute;font-weight:lighter;}" +
                  ".gridmage{width:100%; height: 165px; object-fit:cover; overflow:hidden;border-bottom:3px solid #b33939;}" +
                  "strong{font-size:22px; line-height:23px; display: -webkit-flex;display: flex;font-weight:lighter; justify-content: center; padding: 0px 4px 0px 4px;}" +
                  ".htmlselected {background: #b15f76; color: #edcdcd; border-radius: 12px;}" +
                  ".page {box-sizing: border-box; display: inline-block; color: #fff; font-weight: 500; border-radius: 6px; padding: 6px 20px; background-color: #b33939; margin: 20px; font-size: 30px;}" +
                  "#content {background: #383a3a;}" +
                  "#content .ch {width: 25%;}" + "#content .navigate .ch {width: 10%;}" +
                  "ul>li>img[onerror]{width:0px}" +
                  "@media screen and (min-width: 1900px){ .grid{height:310px;} .gridmage{height: 230px;} small{font-size: 19px;} strong{font-size:24px;} }",
            align = "left",
            menu = menu,
            channels = forklist
        });
    }
    #endregion

    #region OnResult
    public static ActionResult OnResult(EventSisiOnResult e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        var forklist = new List<ForkPlaylistItem>();
        string host = CoreInit.Host(e.httpContext);

        foreach (var pl in e.stream_links.qualitys)
        {
            forklist.Add(new ForkPlaylistItem()
            {
                title = pl.Key == "auto" ? "смотреть" : pl.Key,
                stream_url = e.controller.HostStreamProxy(e.init, pl.Value, e.headers_stream),
                logo_30x30 = Icon.Play
            });
        }

        if (e.stream_links.recomends != null && e.stream_links.recomends.Count > 0)
        {
            string desc(string title, string img, string duration = null)
            {
                string _img = $"<img src=\"{e.controller.HostImgProxy(e.init, img, headers: e.headers_image)}\" width=\"85%\"/>";
                return $"<div style=\"text-align: center; font-size: 1.8vw\"><span style=\"color: #699bbb;\"><strong>{title}</strong></span><br /><br />{_img}{(duration != null ? $"<br />Продолжительность: {duration}" : "")}</div>";
            }

            foreach (var pl in e.stream_links.recomends)
            {
                string playlist_url = pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? pl.video
                    : $"{host}/{pl.video}";

                forklist.Add(new ForkPlaylistItem()
                {
                    title = pl.name,
                    description = desc(pl.name, pl.picture, pl.time),
                    playlist_url = playlist_url,
                    ident = CrypTo.md5(pl.video),
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
}
