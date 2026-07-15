using System;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services.RxEnumerate;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web;

namespace Xnxx;

public static class XnxxTo
{
    public static string Uri(string host, string search, int pg)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return $"{host}/search/{HttpUtility.UrlEncode(search)}/{pg}";

        return $"{host}/best/{DateTime.Today.AddMonths(-1):yyyy-MM}/{pg}";
    }

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("[\t ]id=\"video(-|_)", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups(" href=\"/(video-[^\"]+)\" title=\"([^\"]+)\"");

            if (!string.IsNullOrEmpty(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                string img = row.Match("data-src=\"([^\"]+)\"").Replace(".THUMBNUM.", ".1.") ?? string.Empty;

                string preview =
                    row.Match("\"previewVideo\":\"([^\"]+)\"")?.Replace("\\", "") ??
                    row.Match("data-pvv=\"([^\"]+)\"");

                if (string.IsNullOrEmpty(preview))
                    preview = Regex.Replace(img, "/[^/]+$", "/preview.mp4");

                var pl = new PlaylistItem()
                {
                    name = g[2].Value,
                    video = $"{route}?uri={g[1].Value}",
                    picture = img,
                    preview = preview,
                    time = row.Match("</span>([^<]+)<span class=\"video-hd\">", trim: true),
                    quality = row.Match("<span class=\"superfluous\"> - </span>([^<]+)</span>"),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "xnx",
                        href = g[1].Value,
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
    public static List<MenuItem> Menu(string host)
    {
        return new List<MenuItem>(1)
        {
            new MenuItem()
            {
                title = "Поиск",
                search_on = "search_on",
                playlist_url = $"{host}/xnx"
            }
        };
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return $"{host}/{Regex.Replace(url ?? string.Empty, "^([^/]+)/.*", "$1/_")}";
    }

    public static StreamItem StreamLinks(ReadOnlySpan<char> html, string route, Func<string, Task<string>> onm3u = null)
    {
        if (html.IsEmpty)
            return null;

        string stream_link = Rx.Match(html, "html5player\\.setVideoHLS\\('([^']+)'\\);");
        if (string.IsNullOrWhiteSpace(stream_link))
            return null;

        #region getRelated
        List<PlaylistItem> getRelated(ReadOnlySpan<char> html)
        {
            string json = Rx.Match(html, "video_related=([^\n\r]+);window");
            if (string.IsNullOrWhiteSpace(json) || !json.StartsWith("[") || !json.EndsWith("]"))
                return new List<PlaylistItem>();

            var related = new List<PlaylistItem>(40);

            try
            {
                foreach (var r in JsonSerializer.Deserialize<List<Related>>(json, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                }))
                {
                    if (string.IsNullOrEmpty(r.tf) || string.IsNullOrEmpty(r.u) || string.IsNullOrEmpty(r.i))
                        continue;

                    related.Add(new PlaylistItem()
                    {
                        name = r.tf,
                        video = $"{route}?uri={r.u.Remove(0, 1)}",
                        picture = r.i,
                        json = true,
                        related = true,
                        bookmark = new Bookmark()
                        {
                            site = "xnx",
                            href = r.u.Remove(0, 1),
                            image = r.i
                        }
                    });
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "XnxxTo", "id_h1jfd3x2");
            }

            return related;
        }
        #endregion

        return new StreamItem()
        {
            qualitys = new Dictionary<string, string>()
            {
                ["auto"] = stream_link
            },
            recomends = getRelated(html)
        };

        //string m3u8 = onm3u == null ? null : await onm3u.Invoke(stream_link);
        //if (m3u8 == null)
        //{
        //    return new StreamItem()
        //    {
        //        qualitys = new Dictionary<string, string>()
        //        {
        //            ["auto"] = stream_link
        //        },
        //        recomends = getRelated()
        //    };
        //}

        //var stream_links = new Dictionary<int, string>();
        //foreach (Match m in Regex.Matches(m3u8, "(hls-(2160|1440|1080|720|480|360)p[^\n\r\t ]+)"))
        //{
        //    string hls = m.Groups[1].Value;
        //    if (string.IsNullOrEmpty(hls))
        //        continue;

        //    hls = $"{Regex.Replace(stream_link, "/hls\\.m3u.*", "")}/{hls}".Replace("https:", "http:");
        //    stream_links.Add(int.Parse(m.Groups[2].Value), hls);
        //}

        //return new StreamItem()
        //{
        //    qualitys = stream_links.OrderByDescending(i => i.Key).ToDictionary(k => $"{k.Key}p", v => v.Value),
        //    recomends = getRelated()
        //};
    }
    #endregion
}
