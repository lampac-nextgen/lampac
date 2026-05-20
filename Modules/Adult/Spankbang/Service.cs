using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services.HTML;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Spankbang;

public static class SpankbangTo
{
    #region Uri
    public static string Uri(string host, string search, string sort, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("s/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");
            url.Append(pg);
            url.Append("/");
        }
        else
        {
            url.Append($"{sort ?? "new_videos"}/{pg}/");

            if (sort == "most_popular")
                url.Append("?p=m");
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        ReadOnlySpan<char> container = html.Contains("class=\"main-container\"", StringComparison.Ordinal)
            ? Rx.Split("class=\"main-container\"", html)[1].Span
            : html;

        var playlists = new List<PlaylistItem>(80);

        foreach (ReadOnlySpan<char> row in HtmlSpan.Nodes(container, "div", "data-testid", "video-item", HtmlSpanTargetType.Exact))
        {
            var g = Rx.Groups(row, "<a href=\"/(?<link>[^\"]+)\" title=\"(?<title>[^\"]+)\"");
            if (!string.IsNullOrWhiteSpace(g["link"].Value) && !string.IsNullOrWhiteSpace(g["title"].Value))
            {
                string img = Rx.Match(row, "([\n\r\t ]+)src=\"([^\"]+)\"", 2) ?? string.Empty;
                if (!img.Contains("/w:"))
                    img = Rx.Match(row, "data-src=\"([^\"]+)\"");

                if (img == null)
                    continue;

                img = Regex.Replace(img, "/w:[0-9]00/", "/w:300/").Replace("http://", "https://");

                string preview = Rx.Match(row, "data-preview=\"([^\"]+)\"");
                if (preview == null)
                    preview = Rx.Match(row, "<source data-src=\"([^\"]+)\"");

                var pl = new PlaylistItem()
                {
                    name = g["title"].Value,
                    video = $"{route}?uri={g["link"].Value}",
                    quality = Rx.Match(row, "\"video-item-resolution\">([^<]+)</span>"),
                    picture = img,
                    preview = preview,
                    time = Rx.Match(row, "\"video-item-length\">([^<]+)</span>"),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "sbg",
                        href = g["link"].Value,
                        image = img
                    }
                };

                if (onplaylist != null)
                    pl = onplaylist.Invoke(pl);

                playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string sort)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Spankbang_menu_{host}_{sort}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(2)
        {
            new MenuItem()
            {
                title = "Поиск",
                search_on = "search_on",
                playlist_url = $"{host}/sbg",
            },
            new MenuItem()
            {
                title = $"Сортировка: {(string.IsNullOrWhiteSpace(sort) ? "новое" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Новое", $"{host}/sbg"),
                    new("Трендовое", $"{host}/sbg?sort=trending_videos"),
                    new("Популярное", $"{host}/sbg?sort=most_popular")
                }
            }
        };

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        return $"{host}/{url}";
    }

    public static StreamItem StreamLinks(string route, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Matches("'([0-9]+)(p|k)': ?\\[\'(https?://[^']+)\'", html);
        if (rx.Count == 0)
            return null;

        var stream_links = new Dictionary<int, string>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups();
            if (string.IsNullOrEmpty(g[1].Value))
                continue;

            int q = $"{g[1].Value}{g[2].Value}" == "4k" ? 2160 : -1;
            if (q == -1)
                int.TryParse(g[1].Value, out q);

            stream_links.TryAdd(q, g[3].Value);
        }

        return new StreamItem()
        {
            qualitys = stream_links.OrderByDescending(i => i.Key).ToDictionary(k => $"{k.Key}p", v => v.Value),
            recomends = Playlist(route, html)
        };
    }
    #endregion
}
