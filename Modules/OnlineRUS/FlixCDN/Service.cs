using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace FlixCDN;

public struct FlixCDNInvoke
{
    #region FlixCDNInvoke
    string host;
    OnlinesSettings init;
    HttpHydra httpHydra;
    Func<string, string> onstreamfile;

    public FlixCDNInvoke(string host, OnlinesSettings init, HttpHydra httpHydra, Func<string, string> onstreamfile)
    {
        this.host = host;
        this.init = init;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region StreamQuality
    public StreamQualityTpl GetStreamQualityTpl(string file)
    {
        var streamquality = new StreamQualityTpl();

        foreach (Match m in Regex.Matches(file, "\\[(?<q>\\d{3,4})\\](?<url>https?://[^,\"\\[\\s]+)"))
        {
            string q = m.Groups["q"].Value;
            string link = m.Groups["url"].Value;

            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(q))
                continue;

            streamquality.Insert(onstreamfile.Invoke(link), $"{q}p");
        }

        if (streamquality.Any())
            return streamquality;

        foreach (Match m in Regex.Matches(file, "(https?://[^,\"\\[\\s]+\\.(m3u8|mp4)(:hls:manifest\\.m3u8)?)"))
        {
            string link = m.Groups[1].Value;
            if (string.IsNullOrEmpty(link))
                continue;

            streamquality.Append(onstreamfile.Invoke(link), "auto");
            break;
        }

        return streamquality;
    }
    #endregion

    #region BuildIframeUrl
    public string BuildIframeUrl(string iframe, int t, int s, int e)
    {
        var args = new List<string>(3);

        if (t > 0)
            args.Add("translation=" + t);

        if (s > 0)
            args.Add("season=" + s);

        if (e > 0)
            args.Add("episode=" + e);

        if (args.Count == 0)
            return iframe;

        return iframe + (iframe.Contains("?") ? "&" : "?") + string.Join("&", args);
    }
    #endregion

    #region SearchByTitle
    async public Task<SearchItem> SearchByTitle(string imdb_id, long kinopoisk_id, string title, string original_title, bool forceSimilar)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))
            return null;

        var root = await ApiSearch($"title={HttpUtility.UrlEncode(title ?? original_title)}");
        if (root == null || root.Length == 0)
            return null;

        var stpl = new SimilarTpl(root.Length);
        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        string stitle = StringConvert.SearchName(title);
        string sorig = StringConvert.SearchName(original_title);

        SearchItem exact = null;

        foreach (var item in root)
        {
            string name = item.title_rus ?? item.title_orig;
            string details = item.year > 0 ? item.year.ToString() : string.Empty;

            stpl.Append(
                name,
                details,
                string.Empty,
                $"{host}/lite/flixcdn?kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={HttpUtility.UrlEncode(item.title_rus)}&original_title={HttpUtility.UrlEncode(item.title_orig)}&year={item.year}",
                PosterApi.Size(item.poster)
            );

            if (exact == null && !string.IsNullOrEmpty(name))
            {
                string sname = StringConvert.SearchName(name);
                if (!string.IsNullOrEmpty(stitle) && sname.Contains(stitle))
                    exact = item;
                else if (!string.IsNullOrEmpty(sorig) && sname.Contains(sorig))
                    exact = item;
            }
        }

        if (forceSimilar)
            return new SearchItem() { similar = stpl };

        if (exact != null)
            return exact;

        if (root.Length == 1)
            return root[0];

        if (stpl.Length > 0)
            return new SearchItem() { similar = stpl };

        return null;
    }

    async Task<SearchItem[]> ApiSearch(string query)
    {
        string uri = $"{init.apihost}/search?token={init.token}&{query}";
        var root = await httpHydra.Get<SearchRoot>(uri, safety: true);

        return root?.result;
    }
    #endregion
}
