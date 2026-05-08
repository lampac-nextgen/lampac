using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MsxNative;

public static class SisiAPI
{
    #region Channels
    public static ActionResult Channels(EventSisiChannels e)
    {
        if (!Utilities.IsMsxPlayer(e.httpContext))
            return null;

        string host = CoreInit.Host(e.httpContext);
        var items = new List<MsxItem>(e.channels.Count);

        foreach (var ch in e.channels.Where(i => i.displayindex > 1).OrderBy(i => i.displayindex))
        {
            items.Add(new MsxItem()
            {
                title = ch.title,
                icon = "#ff9900:movie",
                iconSize = "large",
                action = "content:request:interaction:"
                    + Utilities.Uri(ch.playlist_url, e.httpContext.Request.Query)
                    + $"&uid={{UID}}&pg={{PAGE}}&limit={{LIMIT}}|30@{host}/msx/paging.html"
            });
        }

        return new JsonResult(new
        {
            type = "list",
            headline = "Клубничка",
            template = new
            {
                type = "separate",
                layout = "0,0,2,3",
                color = "msx-glass"
            },
            items = items
        });
    }
    #endregion

    #region PlaylistResult
    public static ActionResult PlaylistResult(EventSisiPlaylistResult e)
    {
        if (!Utilities.IsMsxPlayer(e.httpContext))
            return null;

        string host = CoreInit.Host(e.httpContext);
        var items = new List<MsxItem>(e.playlists.Count + 1);

        foreach (var pl in e.playlists)
        {
            string video = pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? pl.video
                : $"{host}/{pl.video}";

            if (!video.Contains(host, StringComparison.OrdinalIgnoreCase))
                video = e.controller.HostStreamProxy(e.init, video, e.headers_stream);

            if (pl.json)
                video = Utilities.Uri(video, e.httpContext.Request.Query);

            items.Add(new MsxItem
            {
                title = pl.name,
                image = e.controller.HostImgProxy(e.init, pl.picture, headers: e.headers_image),
                action = (pl.json ? "content:" : "video:") + video
            });
        }

        return new JsonResult(new
        {
            type = "list",
            template = new
            {
                type = "separate",
                layout = "0,0,3,3",
            },
            items = items
        });
    }
    #endregion

    #region OnResult
    public static ActionResult OnResult(EventSisiOnResult e)
    {
        if (!Utilities.IsMsxPlayer(e.httpContext))
            return null;

        var items = new List<MsxItem>();
        string host = CoreInit.Host(e.httpContext);

        foreach (var pl in e.stream_links.qualitys)
        {
            items.Add(new MsxItem
            {
                title = pl.Key == "auto" ? "смотреть" : pl.Key,
                icon = "#e50914:play-circle-outline",
                iconSize = "large",
                action = "video:" + e.controller.HostStreamProxy(e.init, pl.Value, e.headers_stream)
            });
        }

        if (e.stream_links.recomends != null)
        {
            foreach (var pl in e.stream_links.recomends)
            {
                string playlistUrl = pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? pl.video
                    : $"{CoreInit.Host(e.httpContext)}/{pl.video}";

                string video = pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? pl.video
                    : $"{host}/{pl.video}";

                items.Add(new MsxItem
                {
                    title = pl.name,
                    image = e.controller.HostImgProxy(e.init, pl.picture, headers: e.headers_image),
                    action = "content:" + Utilities.Uri(video, e.httpContext.Request.Query)
                });
            }
        }

        return new JsonResult(new
        {
            type = "list",
            template = new
            {
                type = "separate",
                layout = "0,0,3,3",
            },
            items = items
        });
    }
    #endregion
}
