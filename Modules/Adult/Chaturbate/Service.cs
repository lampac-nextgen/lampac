using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Chaturbate;

public static class ChaturbateTo
{
    #region Uri
    public static string Uri(string host, string sort, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/api/ts/roomlist/room-list/?enable_recommendations=false&limit=90");

        if (!string.IsNullOrWhiteSpace(sort))
        {
            url.Append("&genders=");
            url.Append(sort);
        }

        if (pg > 1)
        {
            url.Append("&offset=");
            url.Append(pg * 90);
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("display_age", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (!row.Contains("\"current_show\":\"public\""))
                continue;

            string baba = row.Match("\"username\":\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(baba))
                continue;

            string img = row.Match("\"img\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(img))
                continue;

            var pl = new PlaylistItem()
            {
                name = baba.Trim(),
                video = $"{route}?baba={baba}",
                picture = img.Replace("\\", ""),
                json = true
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string sort)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Chaturbate_menu_{host}_{sort}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        var sortmenu = new List<MenuItem>(5)
        {
            new("Лучшие", $"{host}/chu"),
            new("Девушки", $"{host}/chu?sort=f"),
            new("Пары", $"{host}/chu?sort=c"),
            new("Парни", $"{host}/chu?sort=m"),
            new("Транссексуалы", $"{host}/chu?sort=t")
        };

        menu = new List<MenuItem>(1)
        {
            new MenuItem()
            {
                title = $"Сортировка: {sortmenu.FirstOrDefault(i => i.playlist_url.EndsWith($"={sort}"))?.title ?? "Лучшие" }",
                playlist_url = "submenu",
                submenu = sortmenu
            }
        };

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string baba)
    {
        if (string.IsNullOrWhiteSpace(baba))
            return null;

        return $"{host}/{baba}/";
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        string hls =
            Rx.Match(html, "(https?://[^ ]+/playlist\\.m3u8)") ??
            Rx.Match(html, @"\\u0022hls_source\\u0022: \\u0022([^, ]+)\\u0022,");

        if (hls == null)
            return null;

        return new Dictionary<string, string>()
        {
            ["auto"] = Regex.Unescape(hls).Replace("\\", "")
        };
    }
    #endregion
}
