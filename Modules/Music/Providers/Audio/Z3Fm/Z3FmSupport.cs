using Microsoft.Extensions.Caching.Memory;
using Microsoft.Playwright;
using Shared.Models.Base;
using Shared.PlaywrightCore;
using Shared.Services.Utilities;
using System.Net;
using System.Text.RegularExpressions;

namespace Music;

public static class Z3FmSupport
{
    public const string AudioProviderId = "z3fmaudio";
    public const string BaseUrl = "https://z3.fm";
    public const string SearchPath = "/mp3/search";

    static readonly MemoryCache cookieCache = new(new MemoryCacheOptions());
    static readonly TimeSpan cookieLifetime = TimeSpan.FromMinutes(20);
    static readonly Regex resultBlockRegex = new(
        "<div class=\"song song-wrap[^\"]*\">(?<block>.*?)<span[^>]*data-time=\"(?<seconds>\\d+)\"[^>]*data-sid=\"(?<sid>\\d+)\"[^>]*data-url=\"(?<download>/download/\\d+)\"[^>]*data-title=\"(?<fulltitle>[^\"]+)\"[^>]*class=\"song-play[^\"]*\"",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
    );
    static readonly Regex artistRegex = new(
        "<div class=\"song-artist\">.*?<a[^>]*href=\"(?<href>/artist/\\d+)\"[^>]*>(?<inner>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
    );
    static readonly Regex titleRegex = new(
        "<div class=\"song-name\">.*?<a[^>]*href=\"(?<href>/song/\\d+)\"[^>]*>(?<inner>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
    );
    static readonly Regex songPageDownloadRegex = new(
        "data-sid=\"(?<sid>\\d+)\"\\s+data-url=\"(?<download>/download/\\d+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
    );
    static readonly string[] noisyTerms =
    {
        "remix", "mix", "instrumental", "karaoke", "spedup", "slowed", "nightcore", "8d", "cover", "live", "edit", "extended"
    };

    public static bool IsModuleEnabled => ModInit.conf?.z3fm_enabled == true;
    public static bool IsAudioEnabled => IsModuleEnabled && ModInit.conf?.z3fm_audio_enabled == true;
    public static bool IsProxyEnabled => IsModuleEnabled && ModInit.conf?.z3fm_proxy_enabled == true;

    public static string BuildSearchUrl(string query)
    {
        query = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
        return $"{BaseUrl.TrimEnd('/')}{SearchPath}?keywords={Uri.EscapeDataString(query)}";
    }

    public static Dictionary<string, string> CreateBrowserHeaders()
    {
        return new Dictionary<string, string>
        {
            ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            ["Accept-Language"] = "ru,en-US;q=0.9,en;q=0.8",
            ["Cache-Control"] = "no-cache",
            ["Pragma"] = "no-cache",
            ["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7; rv:139.0) Gecko/20100101 Firefox/139.0"
        };
    }

    public static async Task<IReadOnlyList<MusicAudioMatch>> SearchAsync(MusicTrack track, CancellationToken cancellationToken = default)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.title))
            return Array.Empty<MusicAudioMatch>();

        var matches = new Dictionary<string, MusicAudioMatch>(StringComparer.Ordinal);
        foreach (var query in BuildQueryVariants(track))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await FetchPageAsync(BuildSearchUrl(query), cancellationToken);
            if (page == null || string.IsNullOrWhiteSpace(page.Html))
                continue;

            foreach (var item in ParseSearchResults(page.Html))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(page.CookieHeader))
                {
                    cookieCache.Set(BuildCookieCacheKey(item.sid), new Z3FmCookieContext
                    {
                        CookieHeader = page.CookieHeader,
                        Referer = string.IsNullOrWhiteSpace(item.trackUrl) ? page.Url : item.trackUrl
                    }, cookieLifetime);
                }

                if (matches.ContainsKey(item.sid))
                    continue;

                matches[item.sid] = new MusicAudioMatch
                {
                    provider_id = AudioProviderId,
                    id = item.sid,
                    title = item.title,
                    artists = string.IsNullOrWhiteSpace(item.artist) ? new List<string>() : new List<string> { item.artist },
                    duration_ms = item.durationMs,
                    payload = MusicJson.Serialize(new Z3FmMatchPayload
                    {
                        sid = item.sid,
                        download_url = item.downloadUrl,
                        track_url = item.trackUrl
                    })
                };
            }

            if (matches.Count > 0)
                break;
        }

        return RankMatches(track, matches.Values).Take(20).ToList();
    }

    public static async Task<IReadOnlyList<MusicPlaybackSource>> BuildSourcesAsync(MusicAudioMatch match, CancellationToken cancellationToken = default)
    {
        var payload = string.IsNullOrWhiteSpace(match?.payload)
            ? null
            : MusicJson.Deserialize<Z3FmMatchPayload>(match.payload);

        if (payload == null || string.IsNullOrWhiteSpace(payload.sid))
            return Array.Empty<MusicPlaybackSource>();

        var context = await EnsureCookieContextAsync(payload, cancellationToken);
        string url = BuildAbsoluteUrl(payload.download_url);
        if (string.IsNullOrWhiteSpace(url))
            return Array.Empty<MusicPlaybackSource>();

        var headers = new Dictionary<string, string>
        {
            ["Referer"] = context?.Referer ?? BuildAbsoluteUrl(payload.track_url) ?? $"{BaseUrl}/",
            ["Origin"] = BaseUrl,
            ["User-Agent"] = CreateBrowserHeaders()["User-Agent"]
        };

        if (!string.IsNullOrWhiteSpace(context?.CookieHeader))
            headers["Cookie"] = context.CookieHeader;

        var source = new MusicPlaybackSource
        {
            provider_id = AudioProviderId,
            url = url,
            mime_type = "audio/mpeg",
            quality = "MP3",
            headers = headers
        };

        ApplyConfiguredProxy(source);
        return new List<MusicPlaybackSource> { source };
    }

    public static List<MusicAudioMatch> RankMatches(MusicTrack query, IEnumerable<MusicAudioMatch> matches)
    {
        return matches
            .Select(match => new { match, score = ScoreMatch(query, match) })
            .Where(i => i.score >= 4)
            .OrderByDescending(i => i.score)
            .ThenBy(i => i.match.title)
            .Select(i => i.match)
            .ToList();
    }

    public static bool IsRelevantMatch(MusicTrack query, MusicAudioMatch match)
    {
        return ScoreMatch(query, match) >= 6;
    }

    public static void ApplyConfiguredProxy(MusicPlaybackSource source)
    {
        if (source == null)
            return;

        var configured = ParseConfiguredProxy();
        if (configured == null)
            return;

        source.proxy_url = configured.Server;
        source.proxy_username = configured.Username;
        source.proxy_password = configured.Password;
    }

    public static WebProxy GetHttpProxy()
    {
        var configured = ParseConfiguredProxy();
        if (configured == null)
            return null;

        var credentials = string.IsNullOrWhiteSpace(configured.Username)
            ? null
            : new NetworkCredential(configured.Username, configured.Password ?? string.Empty);

        return new WebProxy(configured.Server, false, null, credentials);
    }

    public static (string ip, string username, string password) GetBrowserProxyData()
    {
        var configured = ParseConfiguredProxy();
        if (configured == null)
            return default;

        return (configured.Server, configured.Username, configured.Password);
    }

    public static bool HasConfiguredProxy() => ParseConfiguredProxy() != null;

    static async Task<Z3FmCookieContext> EnsureCookieContextAsync(Z3FmMatchPayload payload, CancellationToken cancellationToken)
    {
        if (payload == null || string.IsNullOrWhiteSpace(payload.sid))
            return null;

        if (cookieCache.TryGetValue(BuildCookieCacheKey(payload.sid), out Z3FmCookieContext cached) && cached != null)
            return cached;

        string trackUrl = BuildAbsoluteUrl(payload.track_url);
        if (string.IsNullOrWhiteSpace(trackUrl))
            return null;

        var page = await FetchPageAsync(trackUrl, cancellationToken);
        if (page == null)
            return null;

        if (string.IsNullOrWhiteSpace(payload.download_url))
        {
            var songPage = ParseSongPage(page.Html);
            if (songPage != null && songPage.sid == payload.sid && !string.IsNullOrWhiteSpace(songPage.downloadUrl))
                payload.download_url = songPage.downloadUrl;
        }

        if (string.IsNullOrWhiteSpace(page.CookieHeader))
            return null;

        var context = new Z3FmCookieContext
        {
            CookieHeader = page.CookieHeader,
            Referer = trackUrl
        };

        cookieCache.Set(BuildCookieCacheKey(payload.sid), context, cookieLifetime);
        return context;
    }

    static IEnumerable<string> BuildQueryVariants(MusicTrack track)
    {
        var variants = new List<string>();

        void add(string value)
        {
            value = value?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (variants.All(existing => !string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                variants.Add(value);
        }

        add($"{track.artist_name} {track.title}");

        if (track.artists != null && track.artists.Count > 0)
        {
            string credit = string.Join(' ', track.artists.Where(i => !string.IsNullOrWhiteSpace(i)));
            add($"{credit} {track.title}");
        }

        add(track.title);
        return variants;
    }

    static async Task<Z3FmBrowserPage> FetchPageAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var headers = CreateBrowserHeaders();
        var init = new BaseSettings
        {
            plugin = "music.z3fm",
            priorityBrowser = "firefox"
        };

        try
        {
            using var browser = new PlaywrightBrowser(init.priorityBrowser);
            var page = await browser.NewPageAsync(init.plugin, headers, GetBrowserProxyData(), keepopen: true, imitationHuman: false, deferredDispose: false).ConfigureAwait(false);
            if (page == null)
                return null;

            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                Timeout = 15_000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            }).ConfigureAwait(false);

            string html = null;
            if (response != null)
                html = await response.TextAsync().ConfigureAwait(false);

            html ??= await page.ContentAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html) || IsBlockedHtml(html))
                return null;

            var cookies = await page.Context.CookiesAsync().ConfigureAwait(false);
            return new Z3FmBrowserPage
            {
                Url = url,
                Html = html,
                CookieHeader = BuildCookieHeader(cookies)
            };
        }
        catch
        {
            return null;
        }
    }

    static IEnumerable<Z3FmParsedEntry> ParseSearchResults(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            yield break;

        foreach (Match match in resultBlockRegex.Matches(html))
        {
            string block = match.Groups["block"].Value;
            string sid = match.Groups["sid"].Value;
            string downloadUrl = match.Groups["download"].Value;
            string fullTitle = HtmlDecode(match.Groups["fulltitle"].Value);

            if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(downloadUrl))
                continue;

            string title = ExtractTitle(block);
            string artist = ExtractArtist(block);
            string trackUrl = ExtractTrackUrl(block);

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
                (artist, title) = SplitFullTitle(fullTitle);

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
                continue;

            int? durationMs = null;
            if (int.TryParse(match.Groups["seconds"].Value, out int seconds) && seconds > 0)
                durationMs = seconds * 1000;

            yield return new Z3FmParsedEntry
            {
                sid = sid,
                artist = artist,
                title = title,
                downloadUrl = BuildAbsoluteUrl(downloadUrl),
                trackUrl = BuildAbsoluteUrl(trackUrl),
                durationMs = durationMs
            };
        }
    }

    static Z3FmParsedEntry ParseSongPage(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var match = songPageDownloadRegex.Match(html);
        if (!match.Success)
            return null;

        return new Z3FmParsedEntry
        {
            sid = match.Groups["sid"].Value,
            downloadUrl = BuildAbsoluteUrl(match.Groups["download"].Value)
        };
    }

    static string ExtractArtist(string block)
    {
        var match = artistRegex.Match(block ?? string.Empty);
        if (!match.Success)
            return null;

        return CleanInlineText(match.Groups["inner"].Value);
    }

    static string ExtractTitle(string block)
    {
        var match = titleRegex.Match(block ?? string.Empty);
        if (!match.Success)
            return null;

        return CleanInlineText(match.Groups["inner"].Value);
    }

    static string ExtractTrackUrl(string block)
    {
        var match = titleRegex.Match(block ?? string.Empty);
        return match.Success ? match.Groups["href"].Value : null;
    }

    static (string artist, string title) SplitFullTitle(string fullTitle)
    {
        fullTitle = CleanInlineText(fullTitle);
        if (string.IsNullOrWhiteSpace(fullTitle))
            return (null, null);

        int delimiter = fullTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (delimiter <= 0 || delimiter >= fullTitle.Length - 3)
            return (null, fullTitle);

        return (fullTitle[..delimiter].Trim(), fullTitle[(delimiter + 3)..].Trim());
    }

    static int ScoreMatch(MusicTrack query, MusicAudioMatch match)
    {
        string queryTitle = SearchNameTo.Convert(query?.title, string.Empty);
        string matchTitle = SearchNameTo.Convert(match?.title, string.Empty);
        string fullMatchTitle = SearchNameTo.Convert($"{match?.artists?.FirstOrDefault()} {match?.title}", string.Empty);
        string queryPrimaryArtist = BuildArtistFragments(query?.artist_name).FirstOrDefault() ?? string.Empty;
        var matchArtists = BuildArtistFragments(match?.artists?.FirstOrDefault());

        int score = 0;

        if (!string.IsNullOrWhiteSpace(queryTitle))
        {
            if (string.Equals(matchTitle, queryTitle, StringComparison.Ordinal))
                score += 18;
            else if (!string.IsNullOrWhiteSpace(matchTitle) && matchTitle.Contains(queryTitle, StringComparison.Ordinal))
                score += 10;
            else if (AllTokensPresent(queryTitle, matchTitle))
                score += 5;
            else
                score -= 14;
        }

        if (!string.IsNullOrWhiteSpace(queryPrimaryArtist))
        {
            if (matchArtists.Contains(queryPrimaryArtist) || string.Equals(SearchNameTo.Convert(match?.artists?.FirstOrDefault(), string.Empty), queryPrimaryArtist, StringComparison.Ordinal))
                score += 12;
            else if (matchArtists.Any(a => a.Contains(queryPrimaryArtist, StringComparison.Ordinal)))
                score += 6;
            else
                score -= 8;
        }

        if (query?.duration_ms.HasValue == true && match?.duration_ms.HasValue == true)
        {
            int delta = Math.Abs(query.duration_ms.Value - match.duration_ms.Value);
            if (delta <= 2_000) score += 8;
            else if (delta <= 5_000) score += 6;
            else if (delta <= 10_000) score += 3;
            else if (delta <= 20_000) score += 1;
            else score -= 2;
        }

        string queryNoiseBase = $"{SearchNameTo.Convert(query?.title, string.Empty)} {SearchNameTo.Convert(query?.artist_name, string.Empty)}";
        foreach (var term in noisyTerms)
        {
            if (!queryNoiseBase.Contains(term, StringComparison.Ordinal) && fullMatchTitle.Contains(term, StringComparison.Ordinal))
                score -= 6;
        }

        return score;
    }

    static List<string> BuildArtistFragments(string artist)
    {
        artist = artist?.Trim();
        if (string.IsNullOrWhiteSpace(artist))
            return new List<string>();

        return artist
            .Split(new[] { ",", "&", " x ", " feat. ", " feat ", " ft. ", " ft ", " and " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => SearchNameTo.Convert(i, string.Empty))
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    static bool AllTokensPresent(string query, string target)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(target))
            return false;

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(token => target.Contains(token, StringComparison.Ordinal));
    }

    static string CleanInlineText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
        value = HtmlDecode(value);
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return Regex.Replace(value, "\\s{2,}", " ").Trim();
    }

    static string HtmlDecode(string value) => WebUtility.HtmlDecode(value ?? string.Empty);

    static string BuildAbsoluteUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (!value.StartsWith('/'))
            value = "/" + value;

        return BaseUrl + value;
    }

    static string BuildCookieHeader(IReadOnlyList<BrowserContextCookiesResult> cookies)
    {
        if (cookies == null || cookies.Count == 0)
            return null;

        var parts = cookies
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Value != null)
            .Select(c => $"{c.Name}={c.Value}")
            .ToArray();

        return parts.Length == 0 ? null : string.Join("; ", parts);
    }

    static bool IsBlockedHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return true;

        return html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
            || html.Contains("cf-challenge", StringComparison.OrdinalIgnoreCase)
            || html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Attention Required", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Access denied", StringComparison.OrdinalIgnoreCase);
    }

    static string BuildCookieCacheKey(string sid) => $"z3fm:cookie:{sid}";

    sealed class ProxyConfiguration
    {
        public string Server { get; init; }
        public string Username { get; init; }
        public string Password { get; init; }
    }

    sealed class Z3FmBrowserPage
    {
        public string Url { get; init; }
        public string Html { get; init; }
        public string CookieHeader { get; init; }
    }

    sealed class Z3FmCookieContext
    {
        public string CookieHeader { get; init; }
        public string Referer { get; init; }
    }

    sealed class Z3FmParsedEntry
    {
        public string sid { get; init; }
        public string artist { get; init; }
        public string title { get; init; }
        public string trackUrl { get; set; }
        public string downloadUrl { get; set; }
        public int? durationMs { get; init; }
    }

    sealed class Z3FmMatchPayload
    {
        public string sid { get; set; }
        public string track_url { get; set; }
        public string download_url { get; set; }
    }

    static ProxyConfiguration ParseConfiguredProxy()
    {
        if (!IsProxyEnabled)
            return null;

        string raw = ModInit.conf?.z3fm_proxy_url?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!raw.Contains("://", StringComparison.Ordinal))
            raw = "http://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return null;

        string username = ModInit.conf?.z3fm_proxy_username?.Trim();
        string password = ModInit.conf?.z3fm_proxy_password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
            username = Uri.UnescapeDataString(parts[0]);
            password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty
        };

        return new ProxyConfiguration
        {
            Server = builder.Uri.GetLeftPart(UriPartial.Authority),
            Username = string.IsNullOrWhiteSpace(username) ? null : username,
            Password = string.IsNullOrWhiteSpace(username) ? null : password
        };
    }
}
