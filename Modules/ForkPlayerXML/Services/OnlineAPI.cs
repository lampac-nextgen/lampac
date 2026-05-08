using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Events;
using Shared.Models.Templates;
using System.Collections.Generic;
using System.Linq;

namespace ForkXML;

public static class OnlineAPI
{
    #region Channels
    public static ActionResult Channels(EventOnline e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        var forklist = new List<ForkPlaylistItem>();
        string host = CoreInit.Host(e.httpContext);

        foreach (var item in e.online.OrderBy(i => i.index))
        {
            string uri = item.url.Replace("{localhost}", host);
            uri += (uri.Contains("?") ? "&" : "?") + e.moduleArgs.ToArgs();

            forklist.Add(new ForkPlaylistItem()
            {
                title = item.name,
                playlist_url = uri,
                logo_30x30 = Icon.CdnSearch
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

    #region ContentTpl
    public static ActionResult ContentTpl(EventOnlineTpl e)
    {
        if (!Utilities.IsForkPlayer(e.httpContext))
            return null;

        List<ForkPlaylistItem> menu = null;
        var forklist = new List<ForkPlaylistItem>();

        var obj = e.tpl.ToObject();

        if (obj is SimilarTpl similarTpl)
        {
            string desc(string title, string img)
            {
                string _img = $"<img src=\"{e.controller.HostImgProxy(e.init, img)}\" width=\"85%\"/>";
                return $"<div style=\"text-align: center; font-size: 1.8vw\"><span style=\"color: #699bbb;\"><strong>{title}</strong></span><br /><br />{_img}</div>";
            }

            foreach (var item in similarTpl.data)
            {
                forklist.Add(new ForkPlaylistItem()
                {
                    title = item.title,
                    playlist_url = item.url,
                    logo_30x30 = Icon.Folder,
                    description = desc(item.title, item.img),
                });
            }
        }
        else if (obj is MovieTpl movieTpl)
        {
            foreach (var item in movieTpl.data)
            {
                var md = new ForkPlaylistItem()
                {
                    title = item.voiceOrQuality,
                    ident = $"lampac:{e.httpContext.Request.Query["imdb_id"]}"
                };

                if (!string.IsNullOrEmpty(item.stream) || item.method == "play")
                {
                    md.stream_url = (item.stream ?? item.link).Split(" ")[0];
                    md.logo_30x30 = Icon.Play;
                }
                else
                {
                    md.playlist_url = item.link;
                    md.logo_30x30 = Icon.Folder;
                }

                forklist.Add(md);
            }
        }
        else if (obj is SeasonTpl seasonTpl)
        {
            foreach (var item in seasonTpl.data)
            {
                forklist.Add(new ForkPlaylistItem()
                {
                    title = item.name,
                    playlist_url = item.url,
                    logo_30x30 = Icon.Folder
                });
            }
        }
        else if (obj is EpisodeTpl episodeTpl)
        {
            foreach (var item in episodeTpl.data)
            {
                var md = new ForkPlaylistItem()
                {
                    title = item.name,
                    ident = $"lampac:{e.httpContext.Request.Query["imdb_id"]}:{item.s}:{item.e}"
                };

                if (!string.IsNullOrEmpty(item.stream) || item.method == "play")
                {
                    md.stream_url = (item.stream ?? item.url).Split(" ")[0];
                    md.logo_30x30 = Icon.Play;
                }
                else
                {
                    md.playlist_url = item.url;
                    md.logo_30x30 = Icon.Folder;
                }

                forklist.Add(md);
            }

            if (episodeTpl?.vtpl?.data != null)
            {
                string active = null;
                var submenu = new List<ForkPlaylistItem>();

                foreach (var voice in episodeTpl.vtpl.data)
                {
                    if (voice.active)
                        active = voice.name;

                    submenu.Add(new ForkPlaylistItem()
                    {
                        title = voice.name,
                        playlist_url = voice.url
                    });
                }

                menu = new List<ForkPlaylistItem>()
                {
                    new ForkPlaylistItem()
                    {
                        title = $"Перевод: {active ?? "выбрать"}",
                        playlist_url = "submenu",
                        submenu = submenu,
                        logo_30x30 = Icon.Filter
                    }
                };
            }
        }

        return new JsonResult(new
        {
            title = "Lampac",
            all_local = "directly",
            menu = menu,
            channels = forklist
        });
    }
    #endregion

    #region VideoTpl
    public static string VideoTpl(EventVideoTpl e)
    {
        if (e.httpContext == null || !Utilities.IsForkPlayer(e.httpContext))
            return null;

        var forklist = new List<ForkPlaylistItem>();

        foreach (var item in e.video.quality)
        {
            forklist.Add(new ForkPlaylistItem()
            {
                title = item.Key,
                stream_url = item.Value,
                logo_30x30 = Icon.Play
            });
        }

        if (forklist.Count == 0)
        {
            forklist.Add(new ForkPlaylistItem()
            {
                title = e.video.title,
                stream_url = e.video.url,
                logo_30x30 = Icon.Play
            });
        }

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "Lampac",
            all_local = "directly",
            channels = forklist
        });
    }
    #endregion
}
