using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Shared.Services.HTML;

namespace Music;

internal sealed class SefonMatchPayload
{
    public string stream_url { get; set; }
    public string stream_direct_url { get; set; }
    public string track_url { get; set; }
    public string referer { get; set; }
}

internal static class SefonSupport
{
    public const string ProviderId = "sefonaudio";
    public const string BaseUrl = "https://sefon.org";
    const int MaxSearchQueries = 3;

    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);

    static readonly string[] SoftPenaltyWords =
    {
        "nightcore", "remix", "cover", "karaoke", "instrumental", "sped up", "slowed", "speed up",
        "bass boosted", "deep", "car and bass", "edit", "mashup", "bootleg", "live", "demo"
    };

    static readonly string[] HardPenaltyWords =
    {
        "рингтон", "ringtone", "припев", "pripev", "припiв", "snippet", "тизер", "teaser",
        "минус", "acapella", "акапелла"
    };

    static readonly string[] AlbumNoiseWords =
    {
        "www.", ".ru", ".com", "muzmix", "mfm", "freshmp3music", "not entered tracks"
    };

    static SefonSupport()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(3);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", $"{BaseUrl}/");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", BaseUrl);
    }

    public static string BuildTrackQuery(MusicTrack track)
    {
        return BuildTrackQueries(track).FirstOrDefault() ?? string.Empty;
    }

    public static async Task<List<MusicAudioMatch>> SearchAsync(MusicTrack track, CancellationToken cancellationToken = default)
    {
        var queries = BuildTrackQueries(track).Take(MaxSearchQueries).ToList();
        if (queries.Count == 0)
            return new List<MusicAudioMatch>();

        var merged = new Dictionary<string, MusicAudioMatch>(StringComparer.OrdinalIgnoreCase);
        var tasks = queries
            .Select(query => SearchQuerySafeAsync(query, cancellationToken))
            .ToList();

        var batches = await Task.WhenAll(tasks);
        foreach (var batch in batches)
        {
            foreach (var match in batch)
            {
                if (match == null || string.IsNullOrWhiteSpace(match.id))
                    continue;

                merged[match.id] = match;
            }
        }

        return RankMatches(track, merged.Values)
            .Where(match => IsRelevantMatch(track, match))
            .Take(5)
            .ToList();
    }

    public static async Task<List<MusicTrack>> SearchTracksByQueryAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicTrack>();

        var matches = await SearchQuerySafeAsync(query.Trim(), cancellationToken);
        if (matches.Count == 0)
            return new List<MusicTrack>();

        return matches
            .Select(match => new { match, score = ScoreLooseQueryMatch(query, match) })
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.match.title)
            .Take(Math.Max(1, limit))
            .Select(item => MapTrack(item.match))
            .Where(track => track != null)
            .ToList();
    }

    public static bool IsRelevantMatch(MusicTrack query, MusicAudioMatch match)
    {
        if (query == null || match == null)
            return false;

        if (ScoreMatch(query, match) < 10)
            return false;

        if (!HasArtistOverlap(query, match))
            return false;

        if (query.duration_ms.HasValue && match.duration_ms.HasValue)
        {
            int diff = Math.Abs(query.duration_ms.Value - match.duration_ms.Value);
            if (diff > 90000)
                return false;
        }

        return true;
    }

    static async Task<List<MusicAudioMatch>> SearchQueryAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicAudioMatch>();

        string url = $"{BaseUrl}/song/{Uri.EscapeDataString(query)}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new List<MusicAudioMatch>();

        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
            return new List<MusicAudioMatch>();

        var nodes = HtmlParse.Nodes(html, "//div[contains(concat(' ', normalize-space(@class), ' '), ' muzmo-track ') and contains(concat(' ', normalize-space(@class), ' '), ' track-item ')]");
        if (nodes.Count == 0)
            return new List<MusicAudioMatch>();

        var matches = new List<MusicAudioMatch>();

        foreach (var node in nodes)
        {
            string streamPath = node.SelectText(string.Empty, "data-file");
            string title = WebUtility.HtmlDecode(node.SelectText(string.Empty, "data-track-title") ?? string.Empty).Trim();
            string artist = WebUtility.HtmlDecode(node.SelectText(string.Empty, "data-artist") ?? string.Empty).Trim();
            string trackId = node.SelectText(string.Empty, "data-tid");
            string trackUrl = node.SelectText(".//a[contains(concat(' ', normalize-space(@class), ' '), ' muzmo-track__title ')]", "href");
            string albumTitle = CleanAlbumTitle(node.SelectText(".//span[contains(concat(' ', normalize-space(@class), ' '), ' short-track__info-album ')]"));
            int? durationMs = ParseDurationMs(node.SelectText(".//span[contains(concat(' ', normalize-space(@class), ' '), ' short-track__time ')]"));

            if (string.IsNullOrWhiteSpace(streamPath) || string.IsNullOrWhiteSpace(title))
                continue;

            string streamUrl = BuildAbsoluteUrl(streamPath);
            if (string.IsNullOrWhiteSpace(streamUrl))
                continue;

            string directStreamUrl = TryDecodeDirectStreamUrl(streamUrl);

            matches.Add(new MusicAudioMatch
            {
                provider_id = ProviderId,
                id = !string.IsNullOrWhiteSpace(trackId) ? trackId : streamUrl,
                title = NormalizeMatchTitle(title, artist),
                artists = string.IsNullOrWhiteSpace(artist) ? new List<string>() : new List<string> { artist },
                album_title = albumTitle,
                duration_ms = durationMs,
                payload = MusicJson.Serialize(new SefonMatchPayload
                {
                    stream_url = streamUrl,
                    stream_direct_url = directStreamUrl,
                    track_url = BuildAbsoluteUrl(trackUrl),
                    referer = $"{BaseUrl}/"
                })
            });
        }

        return matches;
    }

    static async Task<List<MusicAudioMatch>> SearchQuerySafeAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            return await SearchQueryAsync(query, cancellationToken);
        }
        catch
        {
            return new List<MusicAudioMatch>();
        }
    }

    public static IReadOnlyList<MusicPlaybackSource> BuildSources(MusicAudioMatch match)
    {
        var payload = string.IsNullOrWhiteSpace(match?.payload)
            ? null
            : MusicJson.Deserialize<SefonMatchPayload>(match.payload);

        string sourceUrl = !string.IsNullOrWhiteSpace(payload?.stream_direct_url)
            ? payload.stream_direct_url
            : payload?.stream_url;

        if (string.IsNullOrWhiteSpace(sourceUrl))
            return Array.Empty<MusicPlaybackSource>();

        var headers = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(payload?.stream_direct_url))
        {
            headers["Referer"] = string.IsNullOrWhiteSpace(payload.referer) ? $"{BaseUrl}/" : payload.referer;
            headers["Origin"] = BaseUrl;
        }

        return new List<MusicPlaybackSource>
        {
            new()
            {
                provider_id = ProviderId,
                url = sourceUrl,
                mime_type = "audio/mpeg",
                quality = "MP3",
                headers = headers
            }
        };
    }

    public static List<MusicAudioMatch> RankMatches(MusicTrack query, IEnumerable<MusicAudioMatch> matches)
    {
        return matches
            .Select(match => new { match, score = ScoreMatch(query, match) })
            .Where(i => i.score > -18)
            .OrderByDescending(i => i.score)
            .ThenBy(i => i.match.title)
            .Select(i => i.match)
            .ToList();
    }

    static int ScoreMatch(MusicTrack query, MusicAudioMatch match)
    {
        string title = NormalizeText(match?.title);
        string album = NormalizeText(match?.album_title);
        string artist = NormalizeText(match?.artists.FirstOrDefault());
        string lowered = $"{title} {artist} {album}";
        string queryAlbum = NormalizeText(query?.album_title);
        var queryTitles = BuildNormalizedTitleVariants(query?.title);
        var queryArtists = NormalizeArtistFragments(query?.artist_name);
        string queryPrimaryArtist = queryArtists.FirstOrDefault() ?? string.Empty;
        var matchArtists = NormalizeArtistFragments(match?.artists.FirstOrDefault());
        int score = 0;

        if (queryTitles.Count > 0)
        {
            if (queryTitles.Any(i => title == i))
                score += 16;
            else if (queryTitles.Any(i => !string.IsNullOrWhiteSpace(i) && title.Contains(i)))
                score += 10;
            else if (queryTitles.Any(i => AllTokensPresent(i, title)))
                score += 5;
            else
                score -= 18;
        }

        if (!string.IsNullOrWhiteSpace(queryPrimaryArtist))
        {
            if (matchArtists.Contains(queryPrimaryArtist) || artist == queryPrimaryArtist)
                score += 12;
            else if (artist.Contains(queryPrimaryArtist))
                score += 7;
            else
                score -= 12;

            if (matchArtists.FirstOrDefault() == queryPrimaryArtist)
                score += 4;
        }

        foreach (var queryArtist in queryArtists.Skip(1).Take(3))
        {
            if (matchArtists.Contains(queryArtist) || artist.Contains(queryArtist))
                score += 3;
        }

        if (!string.IsNullOrWhiteSpace(queryAlbum) && !string.IsNullOrWhiteSpace(album))
        {
            if (album == queryAlbum)
                score += 3;
            else if (album.Contains(queryAlbum) || queryAlbum.Contains(album))
                score += 1;
        }

        if (query?.duration_ms.HasValue == true && query.duration_ms.Value > 0 && match?.duration_ms.HasValue == true)
        {
            int diff = Math.Abs(query.duration_ms.Value - match.duration_ms.Value);
            if (diff <= 3000)
                score += 8;
            else if (diff <= 10000)
                score += 5;
            else if (diff <= 20000)
                score += 2;
            else if (diff > 120000)
                score -= 20;
            else if (diff > 60000)
                score -= 12;
            else if (diff > 30000)
                score -= 6;
        }
        else if (match?.duration_ms.HasValue == true && match.duration_ms.Value > 0 && !IsShortTrackQuery(queryTitles))
        {
            if (match.duration_ms.Value < 90000)
                score -= 14;
            else if (match.duration_ms.Value < 120000)
                score -= 8;
        }

        if (HardPenaltyWords.Any(i => lowered.Contains(i)))
            score -= 18;

        if (SoftPenaltyWords.Any(i => lowered.Contains(i)))
            score -= 6;

        if (AlbumNoiseWords.Any(i => album.Contains(i)))
            score -= 4;

        if (artist.Contains("www") || artist.Contains(".ru") || artist.Contains(".com"))
            score -= 4;

        if (title.Length > 140)
            score -= 3;

        return score;
    }

    static int ScoreLooseQueryMatch(string query, MusicAudioMatch match)
    {
        string expected = NormalizeText(query);
        if (string.IsNullOrWhiteSpace(expected) || match == null)
            return 0;

        string title = NormalizeText(match.title);
        string artist = NormalizeText(match.artists.FirstOrDefault());
        string album = NormalizeText(match.album_title);
        string joined = $"{artist} {title} {album}".Trim();
        int score = 0;

        if (joined == expected)
            score += 80;
        else if (!string.IsNullOrWhiteSpace(title) && title == expected)
            score += 70;
        else if (!string.IsNullOrWhiteSpace(joined) && joined.Contains(expected))
            score += 50;
        else if (AllTokensPresent(expected, joined))
            score += 35;

        if (!string.IsNullOrWhiteSpace(title) && title.Contains(expected))
            score += 10;

        if (HardPenaltyWords.Any(i => title.Contains(i)) || HardPenaltyWords.Any(i => album.Contains(i)))
            score -= 40;

        if (SoftPenaltyWords.Any(i => title.Contains(i)) || SoftPenaltyWords.Any(i => album.Contains(i)))
            score -= 18;

        return score;
    }

    static MusicTrack MapTrack(MusicAudioMatch match)
    {
        if (match == null || string.IsNullOrWhiteSpace(match.title))
            return null;

        string artistName = match.artists?.FirstOrDefault() ?? string.Empty;

        return new MusicTrack
        {
            id = $"sefon:search:{match.id}",
            title = match.title,
            artist_name = artistName,
            artists = string.IsNullOrWhiteSpace(artistName) ? new List<string>() : new List<string> { artistName },
            album_title = match.album_title,
            duration_ms = match.duration_ms
        };
    }

    static bool HasArtistOverlap(MusicTrack query, MusicAudioMatch match)
    {
        var queryArtists = NormalizeArtistFragments(query?.artist_name);
        if (queryArtists.Count == 0)
            return true;

        var matchArtists = NormalizeArtistFragments(match?.artists.FirstOrDefault());
        string normalizedArtist = NormalizeText(match?.artists.FirstOrDefault());

        foreach (var queryArtist in queryArtists)
        {
            if (string.IsNullOrWhiteSpace(queryArtist))
                continue;

            if (matchArtists.Contains(queryArtist) || normalizedArtist.Contains(queryArtist))
                return true;
        }

        return false;
    }

    static string BuildAbsoluteUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim());
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (!value.StartsWith("/"))
            value = "/" + value;

        return BaseUrl + value;
    }

    static string TryDecodeDirectStreamUrl(string streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
            return null;

        const string marker = "/stream/mym/";
        int markerIndex = streamUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        string encoded = streamUrl[(markerIndex + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(encoded))
            return null;

        try
        {
            string padded = encoded.Replace('-', '+').Replace('_', '/');
            int mod = padded.Length % 4;
            if (mod > 0)
                padded = padded.PadRight(padded.Length + (4 - mod), '=');

            string decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            if (Uri.TryCreate(decoded, UriKind.Absolute, out var uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return uri.ToString();
            }
        }
        catch
        {
        }

        return null;
    }

    static string CleanAlbumTitle(string value)
    {
        value = WebUtility.HtmlDecode(value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (string.Equals(value, "Not entered tracks", StringComparison.OrdinalIgnoreCase))
            return null;

        return value.Trim().Trim('/');
    }

    static string NormalizeMatchTitle(string title, string artist)
    {
        title = WebUtility.HtmlDecode(title ?? string.Empty).Trim();
        artist = WebUtility.HtmlDecode(artist ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(title))
            return title;

        if (string.IsNullOrWhiteSpace(artist))
            return title;

        string normalizedTitle = NormalizeText(title);
        string normalizedArtist = NormalizeText(artist);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(normalizedArtist))
            return title;

        if (normalizedTitle == normalizedArtist)
            return title;

        var prefixPattern = $"^{Regex.Escape(artist)}\\s*[-–—:]\\s*";
        return Regex.Replace(title, prefixPattern, string.Empty, RegexOptions.IgnoreCase).Trim();
    }

    static int? ParseDurationMs(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int minutes) &&
            int.TryParse(parts[1], out int seconds))
        {
            return ((minutes * 60) + seconds) * 1000;
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int hours) &&
            int.TryParse(parts[1], out minutes) &&
            int.TryParse(parts[2], out seconds))
        {
            return ((hours * 3600) + (minutes * 60) + seconds) * 1000;
        }

        return null;
    }

    static bool AllTokensPresent(string query, string value)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(value))
            return false;

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(value.Contains);
    }

    static List<string> BuildTrackQueries(MusicTrack track)
    {
        var queries = new List<string>();
        string title = CleanQueryValue(track?.title);
        string titlePrimary = CleanQueryValue(StripTitleDecorations(track?.title));
        string artistPrimary = CleanQueryValue(ExtractPrimaryArtist(track?.artist_name));
        string artistFull = CleanQueryValue(track?.artist_name);

        void AddQuery(string artist, string trackTitle)
        {
            var parts = new[] { artist, trackTitle }
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToArray();

            if (parts.Length == 0)
                return;

            string value = string.Join(" ", parts).Trim();
            if (!string.IsNullOrWhiteSpace(value) && queries.All(i => !string.Equals(i, value, StringComparison.OrdinalIgnoreCase)))
                queries.Add(value);
        }

        AddQuery(artistPrimary, titlePrimary);
        AddQuery(artistFull, titlePrimary);

        if (!string.Equals(titlePrimary, title, StringComparison.OrdinalIgnoreCase))
        {
            AddQuery(artistPrimary, title);
            AddQuery(artistFull, title);
        }

        AddQuery(string.Empty, titlePrimary);
        return queries;
    }

    static List<string> BuildNormalizedTitleVariants(string value)
    {
        var variants = new List<string>();

        void Add(string next)
        {
            next = NormalizeText(next);
            if (!string.IsNullOrWhiteSpace(next) && variants.All(i => i != next))
                variants.Add(next);
        }

        Add(value);
        Add(StripTitleDecorations(value));

        return variants;
    }

    static bool IsShortTrackQuery(List<string> normalizedTitles)
    {
        return normalizedTitles.Any(i =>
            i.Contains("intro")
            || i.Contains("outro")
            || i.Contains("interlude")
            || i.Contains("skit")
            || i.Contains("snippet"));
    }

    static List<string> NormalizeArtistFragments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        string normalized = NormalizeText(value);
        normalized = Regex.Replace(normalized, @"\b(feat|ft|featuring|prod|with|and|x|vs|и)\b", "|");
        normalized = Regex.Replace(normalized, @"\s+-\s+", "|");
        normalized = normalized.Replace("&", "|").Replace(",", "|");

        return normalized
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanQueryValue)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string ExtractPrimaryArtist(string value)
    {
        return NormalizeArtistFragments(value).FirstOrDefault() ?? value;
    }

    static string StripTitleDecorations(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = WebUtility.HtmlDecode(value).Trim();
        value = Regex.Replace(value, @"\(([^)]*(feat|ft|live|remix|version|edit|explicit|clean|radio)[^)]*)\)", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\[([^\]]*(feat|ft|live|remix|version|edit|explicit|clean|radio)[^\]]*)\]", string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    static string CleanQueryValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = WebUtility.HtmlDecode(value).Trim();
        value = Regex.Replace(value, @"\s+", " ");
        return value.Trim(' ', '-', '–', '—', ',', ';', ':');
    }

    static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = WebUtility.HtmlDecode(value).ToLowerInvariant();
        value = Regex.Replace(value, @"[\(\)\[\]\{\}\-_]+", " ");
        value = Regex.Replace(value, @"[^0-9a-zа-яёіїєґ\s]+", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}
