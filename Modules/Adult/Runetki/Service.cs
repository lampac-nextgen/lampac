using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services.Hybrid;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;

namespace Runetki;

public static class RunetkiTo
{
    public static string Uri(string host, string sort, int pg)
    {
        return $"{host}/tools/listing_v3.php?livetab={sort ?? "all"}&offset={(pg > 1 ? ((pg - 1) * 72) : 0)}&limit=72";
    }

    public static List<PlaylistItem> Playlist(ReadOnlySpan<char> html, out int total_pages, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        total_pages = 0;

        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("\"gender\"", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            string baba = row.Match("\"username\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(baba))
                continue;

            string esid = row.Match("\"esid\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(esid))
                continue;

            string img = row.Match("\"thumb_image\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(img))
                continue;

            string title = row.Match("\"display_name\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(title))
                title = baba;

            string cleanImg = img.Replace("\\", "").Replace("{ext}", "jpg");

            var pl = new PlaylistItem()
            {
                name = title,
                quality = row.Match("\"vq\":\"([^\"]+)\""),
                video = $"https://{esid}.bcvcdn.com/hls/stream_{baba}/playlist.m3u8",
                picture = cleanImg.StartsWith("http") ? cleanImg : $"https:{cleanImg}"
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        string total_count = Rx.Match(html, "\"total_count\":([0-9]+),");
        if (total_count != null && int.TryParse(total_count, out int total) && total > 0)
        {
            if (72 >= total)
                total_pages = 1;
            else
                total_pages = (total / 72) + 1;
        }

        return playlists;
    }

    public static List<MenuItem> Menu(string host, string sort)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Runetki_menu_{host}_{sort}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(1)
        {
            new MenuItem()
            {
                title = $"Сортировка: {(string.IsNullOrWhiteSpace(sort) ? "выбрать" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(5)
                {
                    new("Новые", $"{host}/runetki?sort=new"),
                    new("Пары", $"{host}/runetki?sort=couples"),
                    new("Девушки", $"{host}/runetki?sort=female"),
                    new("Парни", $"{host}/runetki?sort=male"),
                    new("Транссексуалы", $"{host}/runetki?sort=transsexual")
                }
            }
        };

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
}
