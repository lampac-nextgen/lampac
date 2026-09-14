using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kinoteatrkg;

public class KinoteatrkgController : BaseOnlineController
{
    const int MaxResolveCandidates = 6;

    sealed class SearchCandidate
    {
        public string id;
        public string title;
        public short year;
        public int order;
    }

    static readonly Regex PlayerSourceRegex = new(
        """class=["'][^"']*\bkt-view-player-box\b[^"']*["'][\s\S]{0,12000}?<(?:source|video)\b[^>]*\bsrc=["'](?<src>[^"']+\.mp4(?:\?[^"']*)?)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex Mp4AttributeRegex = new(
        """(?:src|data-src)=["'](?<src>[^"']+\.mp4(?:\?[^"']*)?)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex Mp4QuotedRegex = new(
        """["'](?<src>(?:(?:https?:)?//|/)[^"']+?\.mp4(?:\?[^"']*)?)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex PageTitleRegex = new(
        """<h1[^>]*>\s*(?<title>[^<]+?)(?:\s*<span[^>]*>\s*(?<original>[^<]*?)\s*</span>)?\s*</h1>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex YearRegex = new(
        """<span>\s*Год\s*</span>\s*<strong>\s*(?<year>(?:19|20)\d{2})\s*</strong>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex QualityRegex = new(
        """<span>\s*Качество\s*</span>\s*<strong>\s*(?<quality>[^<]+?)\s*</strong>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex SearchCardRegex = new(
        """<article\b[^>]*>(?<card>[\s\S]*?)</article>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex SearchViewLinkRegex = new(
        """<a\b(?<attrs>[^>]*\bhref=["'][^"']*/site/view\?id=(?<id>\d+)[^"']*["'][^>]*)>(?<body>[\s\S]*?)</a>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex CardMovieTitleRegex = new(
        """<h3\b[^>]*class=["'][^"']*\bkt-movie-title\b[^"']*["'][^>]*>(?<value>[\s\S]*?)</h3>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex CardHeadingRegex = new(
        """<h[1-4]\b[^>]*>(?<value>[\s\S]*?)</h[1-4]>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex AriaLabelRegex = new(
        """\baria-label=["'](?<value>[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex ImageAltRegex = new(
        """<img\b[^>]*\balt=["'](?<value>[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    static readonly Regex AnyYearRegex = new(
        """(?<!\d)(?<year>(?:19|20)\d{2})(?!\d)""",
        RegexOptions.CultureInvariant
    );

    public KinoteatrkgController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/kinoteatrkg")]
    async public Task<ActionResult> Index(string title, string original_title, short year = 0, string source = null, string id = null, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string providerId = !string.IsNullOrEmpty(source) && source.Equals("kinoteatrkg", StringComparison.OrdinalIgnoreCase)
            ? id
            : null;

        string cacheKey = $"kinoteatrkg:v8:view:{providerId}:{NormalizeTitle(title)}:{NormalizeTitle(original_title)}:{year}";
        var cache = await InvokeCacheResult<string>(cacheKey, 40, async e =>
        {
            var movie = await ResolveMovie(title, original_title, year, providerId);
            if (string.IsNullOrEmpty(movie.pageUrl) || string.IsNullOrEmpty(movie.streamUrl))
                return e.Fail("search");

            return e.Success($"{movie.pageUrl}\n{movie.streamUrl}\n{movie.quality}");
        });

        if (!cache.IsSuccess || string.IsNullOrEmpty(cache.Value))
            return OnError(cache.ErrorMsg);

        string[] data = cache.Value.Split(new[] { '\n' }, 3, StringSplitOptions.None);
        if (data.Length < 2 || string.IsNullOrWhiteSpace(data[0]) || string.IsNullOrWhiteSpace(data[1]))
            return OnError("cache");

        string pageUrl = data[0];
        string streamUrl = data[1];
        string quality = data.Length > 2 && !string.IsNullOrWhiteSpace(data[2])
            ? data[2]
            : "Kinoteatr.kg";

        var streamHeaders = HeadersModel.Init("referer", pageUrl);
        string playUrl = HostStreamProxy(streamUrl, streamHeaders, force_streamproxy: true);

        var mtpl = new MovieTpl(title, original_title);
        mtpl.Append(
            quality,
            playUrl,
            vast: init.vast
        );

        return ContentTpl(mtpl);
    }

    async Task<(string pageUrl, string streamUrl, string quality)> ResolveMovie(string title, string originalTitle, short year, string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            return await ResolveById(id.Trim(), year);

        var triedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (string query in SearchQueries(title, originalTitle))
        {
            var movie = await ResolveCandidates(await SearchSuggest(query), query, year, triedIds);
            if (!string.IsNullOrEmpty(movie.streamUrl))
                return movie;

            movie = await ResolveCandidates(await SearchFull(query, year), query, year, triedIds);
            if (!string.IsNullOrEmpty(movie.streamUrl))
                return movie;
        }

        return default;
    }

    async Task<(string pageUrl, string streamUrl, string quality)> ResolveCandidates(
        List<SearchCandidate> candidates,
        string query,
        short year,
        HashSet<string> triedIds)
    {
        if (candidates == null || candidates.Count == 0)
            return default;

        string normalizedQuery = NormalizeTitle(query);

        var ordered = candidates
            .Where(i => year <= 0 || i.year <= 0 || i.year == year)
            .OrderByDescending(i => NormalizeTitle(i.title) == normalizedQuery)
            .ThenByDescending(i => year > 0 && i.year == year)
            .ThenByDescending(i => IsTitleMatch(i.title, query))
            .ThenBy(i => i.order)
            .Take(MaxResolveCandidates);

        foreach (var candidate in ordered)
        {
            if (string.IsNullOrWhiteSpace(candidate.id) || !triedIds.Add(candidate.id))
                continue;

            var movie = await ResolveById(candidate.id, year, query);
            if (!string.IsNullOrEmpty(movie.streamUrl))
                return movie;
        }

        return default;
    }

    async Task<List<SearchCandidate>> SearchSuggest(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        string url = $"{init.host.TrimEnd('/')}/site/suggest?term={Uri.EscapeDataString(query.Trim())}";
        string json = await httpHydra.Get(url, addheaders: HeadersModel.Init("referer", $"{init.host.TrimEnd('/')}/"));
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("films", out JsonElement films) || films.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<SearchCandidate>();
            int order = 0;

            foreach (JsonElement item in films.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out JsonElement idElement) || !item.TryGetProperty("title", out JsonElement titleElement))
                    continue;

                string movieId = idElement.ValueKind switch
                {
                    JsonValueKind.String => idElement.GetString(),
                    JsonValueKind.Number => idElement.GetRawText(),
                    _ => null
                };

                string movieTitle = titleElement.ValueKind == JsonValueKind.String
                    ? titleElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(movieId) && !string.IsNullOrWhiteSpace(movieTitle))
                {
                    result.Add(new SearchCandidate
                    {
                        id = movieId,
                        title = movieTitle.Trim(),
                        year = 0,
                        order = order++
                    });
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    async Task<List<SearchCandidate>> SearchFull(string query, short expectedYear)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        string host = init.host.TrimEnd('/');
        string url = $"{host}/search?query={Uri.EscapeDataString(query.Trim())}";
        string html = await httpHydra.Get(url, addheaders: HeadersModel.Init("referer", $"{host}/"));
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var candidates = new List<SearchCandidate>();
        var byId = new Dictionary<string, SearchCandidate>(StringComparer.Ordinal);
        int order = 0;

        foreach (Match linkMatch in SearchViewLinkRegex.Matches(html))
        {
            string body = linkMatch.Groups["body"].Value;
            string attrs = linkMatch.Groups["attrs"].Value;

            AddSearchCandidate(
                candidates,
                byId,
                linkMatch.Groups["id"].Value,
                ExtractLinkTitle(attrs, body),
                ExtractCandidateYear(body),
                order++,
                query
            );
        }

        foreach (Match cardMatch in SearchCardRegex.Matches(html))
        {
            string card = cardMatch.Groups["card"].Value;
            Match linkMatch = SearchViewLinkRegex.Match(card);
            if (!linkMatch.Success)
                continue;

            AddSearchCandidate(
                candidates,
                byId,
                linkMatch.Groups["id"].Value,
                ExtractSearchTitle(card, linkMatch),
                ExtractCandidateYear(card),
                order++,
                query
            );
        }

        return RankFullCandidates(candidates, query, expectedYear);
    }

    static void AddSearchCandidate(
        List<SearchCandidate> candidates,
        Dictionary<string, SearchCandidate> byId,
        string id,
        string title,
        short year,
        int order,
        string query)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        title = CleanText(title);

        if (!byId.TryGetValue(id, out SearchCandidate existing))
        {
            var candidate = new SearchCandidate
            {
                id = id,
                title = title,
                year = year,
                order = order
            };

            byId[id] = candidate;
            candidates.Add(candidate);
            return;
        }

        if (TitleScore(title, query) > TitleScore(existing.title, query))
            existing.title = title;

        if (existing.year == 0 && year > 0)
            existing.year = year;
    }

    static List<SearchCandidate> RankFullCandidates(List<SearchCandidate> candidates, string query, short expectedYear)
    {
        if (candidates == null || candidates.Count == 0)
            return candidates;

        string normalizedQuery = NormalizeTitle(query);

        var exact = candidates
            .Where(i => !string.IsNullOrEmpty(normalizedQuery) && NormalizeTitle(i.title) == normalizedQuery)
            .Where(i => expectedYear <= 0 || i.year <= 0 || i.year == expectedYear)
            .OrderByDescending(i => expectedYear > 0 && i.year == expectedYear)
            .ThenBy(i => i.order)
            .ToList();

        if (exact.Count > 0)
            return exact;

        var titleMatches = candidates
            .Where(i => IsTitleMatch(i.title, query))
            .Where(i => expectedYear <= 0 || i.year <= 0 || i.year == expectedYear)
            .OrderByDescending(i => expectedYear > 0 && i.year == expectedYear)
            .ThenBy(i => i.order)
            .ToList();

        if (titleMatches.Count > 0)
            return titleMatches;

        return candidates
            .Where(i => expectedYear <= 0 || i.year <= 0 || i.year == expectedYear)
            .OrderByDescending(i => expectedYear > 0 && i.year == expectedYear)
            .ThenBy(i => i.order)
            .Take(MaxResolveCandidates)
            .ToList();
    }

    static string ExtractSearchTitle(string card, Match linkMatch)
    {
        Match titleMatch = CardMovieTitleRegex.Match(card);
        if (titleMatch.Success)
        {
            string value = CleanText(titleMatch.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        Match headingMatch = CardHeadingRegex.Match(card);
        if (headingMatch.Success)
        {
            string value = CleanText(headingMatch.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return ExtractLinkTitle(linkMatch.Groups["attrs"].Value, linkMatch.Groups["body"].Value);
    }

    static string ExtractLinkTitle(string attrs, string body)
    {
        Match ariaMatch = AriaLabelRegex.Match(attrs ?? string.Empty);
        if (ariaMatch.Success)
        {
            string value = CleanText(ariaMatch.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        Match headingMatch = CardHeadingRegex.Match(body ?? string.Empty);
        if (headingMatch.Success)
        {
            string value = CleanText(headingMatch.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        Match altMatch = ImageAltRegex.Match(body ?? string.Empty);
        if (altMatch.Success)
        {
            string value = CleanText(altMatch.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return CleanText(body);
    }

    static short ExtractCandidateYear(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;

        string text = CleanText(html.Length > 1400 ? html.Substring(0, 1400) : html);
        Match match = AnyYearRegex.Match(text);

        return match.Success && short.TryParse(match.Groups["year"].Value, out short year)
            ? year
            : (short)0;
    }

    static int TitleScore(string title, string query)
    {
        if (string.IsNullOrWhiteSpace(title))
            return 0;

        string left = NormalizeTitle(title);
        string right = NormalizeTitle(query);

        if (!string.IsNullOrEmpty(right) && left == right)
            return 1000;

        if (IsTitleMatch(title, query))
            return 800;

        return Math.Max(1, 300 - Math.Min(title.Length, 299));
    }

    async Task<(string pageUrl, string streamUrl, string quality)> ResolveById(string id, short expectedYear, string expectedTitle = null)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsDigit))
            return default;

        string host = init.host.TrimEnd('/');
        string pageUrl = $"{host}/site/view?id={id}";
        string html = await httpHydra.Get(pageUrl, addheaders: HeadersModel.Init("referer", $"{host}/"));
        if (string.IsNullOrWhiteSpace(html))
            return default;

        Match titleMatch = PageTitleRegex.Match(html);
        string pageTitle = titleMatch.Success
            ? WebUtility.HtmlDecode(titleMatch.Groups["title"].Value)?.Trim()
            : null;
        string pageOriginalTitle = titleMatch.Success
            ? WebUtility.HtmlDecode(titleMatch.Groups["original"].Value)?.Trim()
            : null;

        if (!string.IsNullOrWhiteSpace(expectedTitle))
        {
            if (!titleMatch.Success || (!IsTitleMatch(pageTitle, expectedTitle) && !IsTitleMatch(pageOriginalTitle, expectedTitle)))
                return default;
        }

        short pageYear = 0;
        Match yearMatch = YearRegex.Match(html);
        if (yearMatch.Success)
            short.TryParse(yearMatch.Groups["year"].Value, out pageYear);

        if (expectedYear > 0 && pageYear != expectedYear)
            return default;

        string streamUrl = ExtractStreamUrl(html, host);
        if (string.IsNullOrWhiteSpace(streamUrl))
            return default;

        string quality = null;
        Match qualityMatch = QualityRegex.Match(html);
        if (qualityMatch.Success)
        {
            quality = WebUtility.HtmlDecode(qualityMatch.Groups["quality"].Value);
            if (!string.IsNullOrWhiteSpace(quality))
                quality = Regex.Replace(quality, """\s+""", " ").Trim();
        }

        return (pageUrl, streamUrl, quality);
    }

    static string ExtractStreamUrl(string html, string host)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(host))
            return null;

        Uri baseUri;
        try
        {
            baseUri = new Uri($"{host.TrimEnd('/')}/");
        }
        catch (UriFormatException)
        {
            return null;
        }

        Match playerMatch = PlayerSourceRegex.Match(html);
        if (playerMatch.Success)
        {
            string playerUrl = ResolveMp4Source(playerMatch.Groups["src"].Value, baseUri, requireVideoPath: false);
            if (!string.IsNullOrWhiteSpace(playerUrl))
                return playerUrl;
        }

        foreach (Match match in Mp4AttributeRegex.Matches(html))
        {
            string url = ResolveMp4Source(match.Groups["src"].Value, baseUri, requireVideoPath: true);
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        foreach (Match match in Mp4QuotedRegex.Matches(html))
        {
            string url = ResolveMp4Source(match.Groups["src"].Value, baseUri, requireVideoPath: true);
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    static string ResolveMp4Source(string rawSource, Uri baseUri, bool requireVideoPath)
    {
        string source = WebUtility.HtmlDecode(rawSource)?.Trim();
        if (string.IsNullOrWhiteSpace(source))
            return null;

        try
        {
            Uri resolved;
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri absolute) &&
                (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                resolved = absolute;
            }
            else
            {
                resolved = new Uri(baseUri, source);
            }

            if (!resolved.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !resolved.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return null;

            string path = resolved.AbsolutePath;
            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                return null;

            if (path.Contains("trailer", StringComparison.OrdinalIgnoreCase))
                return null;

            if (requireVideoPath && !path.Contains("/video", StringComparison.OrdinalIgnoreCase))
                return null;

            return resolved.AbsoluteUri;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    static IEnumerable<string> SearchQueries(string title, string originalTitle)
    {
        if (!string.IsNullOrWhiteSpace(title))
            yield return title.Trim();

        if (!string.IsNullOrWhiteSpace(originalTitle) && !NormalizeTitle(originalTitle).Equals(NormalizeTitle(title), StringComparison.Ordinal))
            yield return originalTitle.Trim();
    }

    static bool IsTitleMatch(string candidate, string query)
    {
        string left = NormalizeTitle(candidate);
        string right = NormalizeTitle(query);

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        return left == right || left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal);
    }

    static string CleanText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string text = Regex.Replace(value, """<[^>]+>""", " ", RegexOptions.CultureInvariant);
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, """\s+""", " ").Trim();
    }

    static string NormalizeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string decoded = WebUtility.HtmlDecode(value).ToLowerInvariant();
        decoded = Regex.Replace(
            decoded,
            """\b(?:full\s*hd|uhd|4k|60\s*fps|hd)\b""",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        return Regex.Replace(decoded, """[\W_]+""", string.Empty, RegexOptions.CultureInvariant);
    }
}
