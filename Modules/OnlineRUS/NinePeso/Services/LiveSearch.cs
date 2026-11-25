using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.HTML;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NinePeso;

/// <summary>
/// On-demand lookup of a title through the live site and provider API.
/// tartugi uses a DLE GET search, then /CDN/ChangeCDN.php returns "Плеер 9"
/// with a videobalanser embed. Only verified metadata IDs are returned.
/// </summary>
internal sealed class LiveLookupResult
{
    public LiveRecord Record { get; set; }
    public bool Serial { get; set; }
    public JObject Metadata { get; set; }
    public List<LiveSearchCandidate> Candidates { get; set; }
}

internal sealed class LiveSearchCandidate
{
    public string PagePath { get; set; }
    public string Title { get; set; }
    public string Year { get; set; }
    public string Poster { get; set; }
}

internal sealed class LiveMetadataException : Exception
{
    public LiveMetadataException(string message, Exception inner = null)
        : base(message, inner) { }
}

internal static class LiveSearch
{
    static readonly Regex player = new(
        @"https://videobalanser\.cc/embed/[A-Za-z0-9_-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex cdn = new(
        @"/CDN/ChangeCDN\.php\?[^""'<>\s]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string PageMeta(string html, string property)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var meta = HtmlParse.Nodes(html, "//meta[@property]")
            .FirstOrDefault(node => string.Equals(node.SelectText(null, "property"),
                property, StringComparison.OrdinalIgnoreCase));
        return WebUtility.HtmlDecode(meta?.SelectText(null, "content"));
    }

    static string PageTitle(string html) => PageMeta(html, "og:title");

    static string PagePoster(string html, Uri pageUri)
    {
        string image = PageMeta(html, "og:image");
        if (string.IsNullOrWhiteSpace(image) ||
            !Uri.TryCreate(pageUri, image, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Host != "tartugi.name" &&
             !uri.Host.EndsWith(".tartugi.name", StringComparison.OrdinalIgnoreCase)))
            return null;

        return uri.AbsoluteUri;
    }

    // DLE appends a single year or a comma-separated list of season years.
    // Strip only this suffix; numbers that belong to the title must survive.
    static (string Title, int[] Years) ParsePageTitle(string name)
    {
        name = (name ?? "").Trim();
        Match suffix = Regex.Match(name, @"\s*\((\d{4}(?:\s*,\s*\d{4})*)\)\s*$");
        if (!suffix.Success)
            return (name, Array.Empty<int>());

        int[] years = suffix.Groups[1].Value.Split(',')
            .Select(value => int.Parse(value.Trim())).ToArray();
        return (name.Substring(0, suffix.Index).Trim(), years);
    }

    // When neither external ID can be used, the player can only be found
    // through a slower title search. Status checks defer that validation
    // until the source is opened; a positive probe is only provisional.
    internal static bool HasTitleOnlyFallback(
        string imdb, long kp, string title, string originalTitle)
        => kp <= 0 && !IsImdb(imdb) &&
           (!string.IsNullOrWhiteSpace(title) ||
            !string.IsNullOrWhiteSpace(originalTitle));

    // Lampac's source-status probe has a hard 10-second timeout.
    // Only check the direct "Плеер 9" listing; defer iframe/API loading
    // and expensive title search until the source is actually opened.
    internal static async Task<bool> HasPlayer(
        string site, string imdb, long kp, HttpHydra siteHttp)
    {
        if (!Uri.TryCreate(site, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Port != 443
            || (uri.Host != "tartugi.name"
                && !uri.Host.EndsWith(".tartugi.name", StringComparison.OrdinalIgnoreCase))
            || (kp <= 0 && !IsImdb(imdb)))
            return false;

        string url = CdnUrl(uri.GetLeftPart(UriPartial.Authority), imdb, kp);
        string response = await SiteGet(url, uri.GetLeftPart(UriPartial.Authority) + "/",
            true, siteHttp);
        return PlayerEmbed(response) != null;
    }

    static string CdnUrl(string siteBase, string imdb, long kp)
        => siteBase + "/CDN/ChangeCDN.php"
            + $"?kpid={Math.Max(kp, 0)}&imdbid={WebUtility.UrlEncode(IsImdb(imdb) ? imdb : "")}"
            + "&tmdbid=&all=yes&ok=&ok2=&youtube=&youtube-ost=&youtube-list=&youtube-fakti=";

    // HttpHydra selects HTTP/RCH and applies standard proxy/CORS settings.
    static Task<string> SiteGet(string url, string referer, bool ajax, HttpHydra siteHttp)
        => siteHttp.Get(url, newheaders: HeadersModel.Init(
            ("referer", referer),
            ("x-requested-with", ajax ? "XMLHttpRequest" : null)));

    // Live CI confirmed that POST returns the same unrelated cards for
    // different titles, while this GET query returns matching results.
    static string SearchUrl(string siteBase, string title)
        => siteBase + "/index.php?do=search&subaction=search&sortby=title&story="
            + WebUtility.UrlEncode(title)
            + "&search_start=1&full_search=0&result_from=1";

    static Task<string> SearchSite(string siteBase, string title, HttpHydra siteHttp)
        => SiteGet(SearchUrl(siteBase, title), siteBase + "/", false, siteHttp);

    public static async Task<LiveLookupResult> Find(
        string site, string api, HttpHydra siteHttp, HttpHydra apiHttp,
        string imdb, long kp, string title, string originalTitle,
        int year, bool serial)
    {
        if (!Uri.TryCreate(site, UriKind.Absolute, out var siteUri)
            || siteUri.Scheme != Uri.UriSchemeHttps
            || (siteUri.Host != "tartugi.name"
                && !siteUri.Host.EndsWith(".tartugi.name", StringComparison.OrdinalIgnoreCase))
            || siteUri.Port != 443)
            return null;

        // The exact ChangeCDN endpoint and its ID parameters were observed in
        // the supplied HAR. Try this cheap lookup before searching site HTML.
        if (kp > 0 || IsImdb(imdb))
        {
            string url = CdnUrl(siteUri.GetLeftPart(UriPartial.Authority), imdb, kp);
            return await FindFromCdn(url, api, siteHttp, apiHttp,
                imdb, kp, title, originalTitle, year, serial,
                siteUri.GetLeftPart(UriPartial.Authority) + "/");
        }

        // Use the same verified live search and ranked links as Spider.
        string baseUrl = siteUri.GetLeftPart(UriPartial.Authority);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<LiveSearchCandidate>();
        int checkedPages = 0;
        const int maxPages = 2;
        foreach (string query in new[] { title, originalTitle })
        {
            if (checkedPages >= maxPages)
                break;
            if (string.IsNullOrWhiteSpace(query) || query.Length > 150)
                continue;

            string html = await SearchSite(baseUrl, query, siteHttp);

            if (string.IsNullOrWhiteSpace(html))
                continue;

            foreach (Uri pageUri in SpiderPages(siteUri, html, query))
            {
                if (checkedPages >= maxPages)
                    break;

                if (!visited.Add(pageUri.AbsoluteUri))
                    continue;

                checkedPages++;
                string pageHtml = await SiteGet(pageUri.AbsoluteUri, baseUrl + "/", false, siteHttp);
                if (string.IsNullOrWhiteSpace(pageHtml))
                    continue;

                string foundTitle = PageTitle(pageHtml);
                if (string.IsNullOrWhiteSpace(foundTitle))
                    continue;

                if (!SameTitle(foundTitle, title, allowPrefix: true) &&
                    !SameTitle(foundTitle, originalTitle, allowPrefix: true))
                    continue;

                Match ajax = cdn.Match(pageHtml);
                if (!ajax.Success || !IsSafePagePath(pageUri.AbsolutePath))
                    continue;

                // Candidates are suggestions only. Never auto-select a work
                // whose external ID, title or year is not confirmed.
                var pageTitle = ParsePageTitle(foundTitle);
                candidates.Add(new LiveSearchCandidate
                {
                    PagePath = pageUri.AbsolutePath,
                    Title = pageTitle.Title,
                    Year = pageTitle.Years.Length > 0 ? pageTitle.Years[0].ToString() : null,
                    Poster = PagePoster(pageHtml, pageUri)
                });
            }
        }

        return candidates.Count > 0
            ? new LiveLookupResult { Candidates = candidates }
            : null;
    }

    // Global Spider searches films and series together. Check that each result
    // advertises player 9, but defer iframe and full metadata until selection.
    internal static async Task<List<LiveSearchCandidate>> FindSpider(
        string site, HttpHydra siteHttp, bool useRhub, string title)
    {
        var results = new List<LiveSearchCandidate>();
        string wanted = SearchNameTo.Convert(title);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 150 ||
            string.IsNullOrEmpty(wanted) ||
            !Uri.TryCreate(site, UriKind.Absolute, out var siteUri) ||
            siteUri.Scheme != Uri.UriSchemeHttps || siteUri.Port != 443 ||
            (siteUri.Host != "tartugi.name" &&
             !siteUri.Host.EndsWith(".tartugi.name", StringComparison.OrdinalIgnoreCase)))
            return results;

        string baseUrl = siteUri.GetLeftPart(UriPartial.Authority);
        string html = await SearchSite(baseUrl, title, siteHttp);
        var pages = SpiderPages(siteUri, html, title);

        async Task<LiveSearchCandidate> CheckPage(Uri pageUri)
        {
            try
            {
                string pageHtml = await SiteGet(pageUri.AbsoluteUri, baseUrl + "/", false, siteHttp);
                if (string.IsNullOrEmpty(pageHtml))
                    return null;

                string foundTitle = PageTitle(pageHtml);
                if (!SearchNameTo.Contains(foundTitle, wanted))
                    return null;

                Match ajax = cdn.Match(pageHtml);
                if (!ajax.Success)
                    return null;

                string ajaxPath = WebUtility.HtmlDecode(ajax.Value);
                string response = await SiteGet(new Uri(siteUri, ajaxPath).AbsoluteUri,
                    pageUri.AbsoluteUri, true, siteHttp);
                if (string.IsNullOrEmpty(PlayerEmbed(response)))
                    return null;

                var pageTitle = ParsePageTitle(foundTitle);
                return new LiveSearchCandidate
                {
                    PagePath = pageUri.AbsolutePath,
                    Title = pageTitle.Title,
                    Year = pageTitle.Years.Length > 0 ? pageTitle.Years[0].ToString() : null,
                    Poster = PagePoster(pageHtml, pageUri)
                };
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "NinePeso Spider candidate unavailable");
                return null;
            }
        }

        // Direct HTTP has bounded fan-out; avoid simultaneous calls on one RCH.
        int batchSize = useRhub ? 1 : 4;
        int count = useRhub ? Math.Min(pages.Count, 4) : pages.Count;
        for (int offset = 0; offset < count; offset += batchSize)
        {
            var batch = await Task.WhenAll(pages.Skip(offset)
                .Take(Math.Min(batchSize, count - offset)).Select(CheckPage));
            foreach (var item in batch)
                if (item != null)
                    results.Add(item);
        }

        return results;
    }

    static List<Uri> SpiderPages(Uri siteUri, string html, string title)
    {
        var pages = new Dictionary<string, (Uri Page, int Rank, int Order)>(
            StringComparer.OrdinalIgnoreCase);
        string wanted = SearchNameTo.Convert(title);

        void Add(string raw, int rank)
        {
            raw = WebUtility.HtmlDecode(raw);
            if (string.IsNullOrWhiteSpace(raw) ||
                !(raw.StartsWith("/") && !raw.StartsWith("//") ||
                  raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ||
                !Uri.TryCreate(siteUri, raw, out var pageUri) ||
                pageUri.Scheme != Uri.UriSchemeHttps || pageUri.Port != 443 ||
                !pageUri.Host.Equals(siteUri.Host, StringComparison.OrdinalIgnoreCase) ||
                !IsSafePagePath(pageUri.AbsolutePath) ||
                !string.IsNullOrEmpty(pageUri.Query) || !string.IsNullOrEmpty(pageUri.Fragment))
                return;

            if (pages.TryGetValue(pageUri.AbsolutePath, out var previous))
            {
                if (rank > previous.Rank)
                    pages[pageUri.AbsolutePath] = (pageUri, rank, previous.Order);
                return;
            }

            pages.Add(pageUri.AbsolutePath, (pageUri, rank, pages.Count));
        }

        int Rank(string value)
        {
            string clean = WebUtility.HtmlDecode(value);
            if (SearchNameTo.Equals(clean, wanted))
                return 2;
            return SearchNameTo.Contains(clean, wanted) ? 1 : 0;
        }

        // Rank actual link labels and nested title/alt attributes before
        // applying the network budget. Retain other safe hrefs as fallback.
        if (string.IsNullOrWhiteSpace(html))
            return new List<Uri>();

        foreach (var link in HtmlParse.Nodes(html, "//*[@href]")
            .OrderByDescending(node => node.row.Name == "a"))
        {
            string path = link.SelectText(null, "href");
            int rank = 0;
            if (link.row.Name == "a")
            {
                rank = Rank(link.row.InnerText);
                rank = Math.Max(rank, Rank(link.SelectText(null, "title")));
                rank = Math.Max(rank, Rank(link.SelectText(null, "alt")));
                foreach (var node in link.row.Descendants())
                {
                    rank = Math.Max(rank, Rank(node.GetAttributeValue("title", null)));
                    rank = Math.Max(rank, Rank(node.GetAttributeValue("alt", null)));
                }
            }
            Add(path, rank);
        }

        return pages.Values.OrderByDescending(x => x.Rank).ThenBy(x => x.Order)
            .Take(8).Select(x => x.Page).ToList();
    }

    static string PlayerEmbed(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;
        try
        {
            string html = JObject.Parse(response)["data"]?.ToString();
            if (string.IsNullOrEmpty(html) ||
                !html.Contains("Плеер 9", StringComparison.OrdinalIgnoreCase))
                return null;
            Match match = player.Match(html);
            return match.Success ? match.Value : null;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    // Explicit selection from SimilarTpl: re-read the page and verify that
    // its player metadata actually corresponds to the selected site title.
    internal static async Task<LiveLookupResult> FindSelectedPage(
        string site, string api, HttpHydra siteHttp, HttpHydra apiHttp,
        string pagePath, bool? serial)
    {
        if (!IsSafePagePath(pagePath) ||
            !Uri.TryCreate(site, UriKind.Absolute, out var siteUri) ||
            siteUri.Scheme != Uri.UriSchemeHttps || siteUri.Port != 443 ||
            (siteUri.Host != "tartugi.name" &&
             !siteUri.Host.EndsWith(".tartugi.name", StringComparison.OrdinalIgnoreCase)))
            return null;

        var pageUri = new Uri(siteUri.GetLeftPart(UriPartial.Authority) + pagePath);
        string html = await SiteGet(pageUri.AbsoluteUri,
            siteUri.GetLeftPart(UriPartial.Authority) + "/", false, siteHttp);
        if (string.IsNullOrEmpty(html))
            return null;

        string pageName = PageTitle(html);
        Match playerLink = cdn.Match(html);
        if (string.IsNullOrWhiteSpace(pageName) || !playerLink.Success)
            return null;

        var pageTitle = ParsePageTitle(pageName);
        string ajaxPath = WebUtility.HtmlDecode(playerLink.Value);
        return await FindFromCdn(
            new Uri(siteUri, ajaxPath).AbsoluteUri, api, siteHttp, apiHttp,
            null, 0, pageTitle.Title, null, pageTitle.Years.FirstOrDefault(),
            serial, pageUri.AbsoluteUri, selectedYears: pageTitle.Years);
    }

    static bool IsSafePagePath(string path)
        => !string.IsNullOrEmpty(path) && path.Length <= 300 &&
           Regex.IsMatch(path, @"^/\d+-[^/?#\\]+\.html$",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
           !path.Contains("%2f", StringComparison.OrdinalIgnoreCase) &&
           !path.Contains("%5c", StringComparison.OrdinalIgnoreCase);

    static async Task<LiveLookupResult> FindFromCdn(
        string url, string api, HttpHydra siteHttp, HttpHydra apiHttp,
        string imdb, long kp, string title,
        string originalTitle, int year, bool? expectedSerial, string referer,
        int[] selectedYears = null)
    {
        string response = await SiteGet(url, referer, true, siteHttp);
        string embed = PlayerEmbed(response);
        if (string.IsNullOrEmpty(embed))
            return null;

        string embedHtml = await SiteGet(embed, referer, false, siteHttp);
        if (!TryGetEmbeddedIdentity(embedHtml, expectedSerial, out int id, out bool serial))
            return null;

        string endpoint = serial ? "tv-series" : "movies";
        // Site/iframe loaded successfully; metadata failure is not "no player".
        // Two bounded attempts fit Lampa's 60-second source-opening deadline.
        JObject data = await LoadMetadata(
            $"{api.TrimEnd('/')}/api/{endpoint}/{id}/",
            embed, apiHttp, id);

        if (data.Value<int?>("id") != id)
        {
            Serilog.Log.Warning("NinePeso metadata ID mismatch for {Kind} {Id}",
                endpoint, id);
            return null;
        }

        if (!MatchesMetadata(data, imdb, kp, title, originalTitle, year, selectedYears))
            return null;

        if (serial && data["seasons"] is not JArray)
            throw new LiveMetadataException($"NinePeso series {id} has no seasons");
        if (!serial && data["media"] is not JArray)
            throw new LiveMetadataException($"NinePeso movie {id} has no media");

        return new LiveLookupResult
        {
            Record = new LiveRecord
            {
                id = id,
                imdb_id = data.Value<string>("imdb_id"),
                kinopoisk_id = data["kinopoisk_id"]?.ToString(),
                ru_title = data.Value<string>("ru_title"),
                orig_title = data.Value<string>("orig_title"),
                released = data["released"]?.ToString()
            },
            Metadata = data,
            Serial = serial
        };
    }

    // Spider does not guess the content type from the search card.
    static bool TryGetEmbeddedIdentity(string html, bool? expectedSerial,
        out int id, out bool serial)
    {
        id = 0;
        serial = false;
        if (string.IsNullOrEmpty(html))
            return false;

        Match movie = Regex.Match(html, @"\bmovieId=(\d+)");
        Match series = Regex.Match(html, @"\btvSeriesId=(\d+)");
        bool hasMovie = int.TryParse(movie.Groups[1].Value, out int movieId) && movieId > 0;
        bool hasSeries = int.TryParse(series.Groups[1].Value, out int seriesId) && seriesId > 0;

        if (expectedSerial == true || (expectedSerial == null && hasSeries && !hasMovie))
        {
            serial = true;
            id = seriesId;
            return hasSeries;
        }
        if (expectedSerial == false || (expectedSerial == null && hasMovie && !hasSeries))
        {
            id = movieId;
            return hasMovie;
        }
        return false;
    }

    // An exact external ID match wins even if the provider uses another title
    // or release year. If no ID matches, reject conflicting known IDs and use
    // an exact normalized title (optionally ending in a release year).
    static bool MatchesMetadata(JObject data, string imdb, long kp,
        string title, string originalTitle, int year, int[] selectedYears = null)
    {
        string siteKp = data["kinopoisk_id"]?.ToString();
        string siteImdb = data.Value<string>("imdb_id");
        bool matchedKp = kp > 0 && siteKp == kp.ToString();
        bool matchedImdb = IsImdb(imdb) &&
            string.Equals(siteImdb, imdb, StringComparison.OrdinalIgnoreCase);

        if (matchedKp || matchedImdb)
            return true;

        // Do not substitute another work merely because its title is similar.
        if ((kp > 0 && !string.IsNullOrWhiteSpace(siteKp) && siteKp != kp.ToString()) ||
            (IsImdb(imdb) && IsImdb(siteImdb) &&
             !string.Equals(siteImdb, imdb, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!SameTitle(data.Value<string>("ru_title"), title) &&
            !SameTitle(data.Value<string>("orig_title"), originalTitle))
            return false;

        if (year <= 0 ||
            !int.TryParse(LiveRecord.ReleaseYear(data["released"]?.ToString()), out int released))
            return true;

        // The selected page already supplies a specific player and exact title.
        // Accept its listed season years and a one-year release-date discrepancy.
        // Ordinary ID/title lookups retain their existing strict year check.
        if (selectedYears != null && selectedYears.Length > 0)
            return selectedYears.Contains(released) ||
                Math.Abs((long)released - selectedYears[0]) == 1;

        return released == year;
    }

    private static async Task<JObject> LoadMetadata(
        string url, string referer, HttpHydra httpHydra, int id)
    {
        Exception lastError = null;
        const int attempts = 2;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                JObject data = await httpHydra.Get<JObject>(url,
                    newheaders: HeadersModel.Init("referer", referer));
                if (data != null)
                    return data;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Serilog.Log.Warning(ex,
                    "NinePeso metadata request failed: id={Id}, attempt={Attempt}",
                    id, attempt + 1);
            }
        }

        throw new LiveMetadataException(
            $"NinePeso API metadata unavailable for id {id} after {attempts} requests",
            lastError);
    }

    static bool IsImdb(string id)
        => !string.IsNullOrWhiteSpace(id) &&
           Regex.IsMatch(id, @"^tt\d{4,12}$", RegexOptions.IgnoreCase);

    static bool SameTitle(string found, string wanted, bool allowPrefix = false)
    {
        string b = SearchNameTo.Convert(wanted);
        if (string.IsNullOrEmpty(b) || b.Length < 4 || string.IsNullOrWhiteSpace(found))
            return false;

        if (SearchNameTo.Equals(found, b))
            return true;

        // Site search pages can have extra text after the requested title.
        // Final metadata validation must reject sequels, spin-offs and remakes.
        if (allowPrefix)
            return SearchNameTo.StartsWith(found, b);

        var pageTitle = ParsePageTitle(found);
        return pageTitle.Years.Length > 0 &&
            SearchNameTo.Equals(pageTitle.Title, b);
    }
}
