using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Music;

// Audio capability: поиск и ранжирование матчей под трек, скоринг,
// выбор transcoding и построение stream-источников.
public static partial class SoundCloudSupport
{
    public static async Task<IReadOnlyList<MusicAudioMatch>> SearchAudioAsync(MusicTrack track, CancellationToken cancellationToken = default)
    {
        if (track == null)
            return Array.Empty<MusicAudioMatch>();

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return Array.Empty<MusicAudioMatch>();

        var results = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string exactTrackId = ParseTrackApiId(track.id);
        if (!string.IsNullOrWhiteSpace(exactTrackId))
        {
            var exact = await LoadTrackElementAsync(exactTrackId, clientId, cancellationToken);
            if (exact.HasValue && IsPlayableAudioTrack(exact.Value))
            {
                var exactMatch = MapAudioMatch(exact.Value);
                if (exactMatch != null)
                    return new[] { exactMatch };
            }

            return Array.Empty<MusicAudioMatch>();
        }

        foreach (string query in BuildAudioQueries(track))
        {
            var elements = await SearchTrackElementsAsync(query, clientId, 10, cancellationToken);
            if (elements == null)
                continue;

            foreach (var item in elements)
            {
                string id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id) || !IsPlayableAudioTrack(item))
                    continue;

                results.Add(item);
                if (results.Count >= 16)
                    break;
            }

            if (results.Count >= 16)
                break;
        }

        return RankAudioMatches(track, results);
    }

    public static async Task<IReadOnlyList<MusicPlaybackSource>> BuildAudioSourcesAsync(MusicAudioMatch match, CancellationToken cancellationToken = default)
    {
        if (match == null)
            return Array.Empty<MusicPlaybackSource>();

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return Array.Empty<MusicPlaybackSource>();

        var payload = ParseMatchPayload(match);
        string trackId = NormalizeValue(payload?.track_id) ?? ParseTrackApiId(match.id);
        if (string.IsNullOrWhiteSpace(trackId))
            return Array.Empty<MusicPlaybackSource>();

        var track = await LoadTrackElementAsync(trackId, clientId, cancellationToken);
        if (!track.HasValue || !track.Value.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
            return Array.Empty<MusicPlaybackSource>();

        if (!media.TryGetProperty("transcodings", out var transcodings) || transcodings.ValueKind != JsonValueKind.Array)
            return Array.Empty<MusicPlaybackSource>();

        var sources = new List<MusicPlaybackSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transcoding in OrderPlayableTranscodings(transcodings))
        {
            string streamUrl = await ResolveTranscodingUrlAsync(transcoding, clientId, payload?.track_authorization, cancellationToken);
            if (string.IsNullOrWhiteSpace(streamUrl) || IsPreviewStreamUrl(streamUrl) || !seen.Add(streamUrl))
                continue;

            string mime = GetFormatMime(transcoding);
            string protocol = GetFormatProtocol(transcoding);

            sources.Add(new MusicPlaybackSource
            {
                provider_id = AudioProviderId,
                url = streamUrl,
                mime_type = NormalizeSourceMime(protocol, mime, streamUrl),
                quality = BuildQualityLabel(protocol, mime),
                bitrate = EstimateBitrate(streamUrl, mime)
            });
        }

        return sources;
    }

    public static async Task<MusicPlaybackSource> BuildPreferredAudioSourceAsync(MusicAudioMatch match, CancellationToken cancellationToken = default)
    {
        if (match == null)
            return null;

        string cacheKey = BuildPreferredSourceCacheKey(match);
        if (!string.IsNullOrWhiteSpace(cacheKey))
        {
            var cached = await MusicMetadataCacheService.GetAsync<MusicPlaybackSource>(AudioProviderId, "preferred_source", cacheKey, cancellationToken);
            if (cached != null && !string.IsNullOrWhiteSpace(cached.url))
                return cached;
        }

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var payload = ParseMatchPayload(match);
        string trackId = NormalizeValue(payload?.track_id) ?? ParseTrackApiId(match.id);
        if (string.IsNullOrWhiteSpace(trackId))
            return null;

        var track = await LoadTrackElementAsync(trackId, clientId, cancellationToken);
        if (!track.HasValue || !track.Value.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
            return null;

        if (!media.TryGetProperty("transcodings", out var transcodings) || transcodings.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var transcoding in OrderPlayableTranscodings(transcodings))
        {
            string streamUrl = await ResolveTranscodingUrlAsync(transcoding, clientId, payload?.track_authorization, cancellationToken);
            if (string.IsNullOrWhiteSpace(streamUrl) || IsPreviewStreamUrl(streamUrl))
                continue;

            string mime = GetFormatMime(transcoding);
            string protocol = GetFormatProtocol(transcoding);

            var source = new MusicPlaybackSource
            {
                provider_id = AudioProviderId,
                url = streamUrl,
                mime_type = NormalizeSourceMime(protocol, mime, streamUrl),
                quality = BuildQualityLabel(protocol, mime),
                bitrate = EstimateBitrate(streamUrl, mime)
            };

            if (!string.IsNullOrWhiteSpace(cacheKey))
                await MusicMetadataCacheService.SaveAsync(AudioProviderId, "preferred_source", cacheKey, source, preferredSourceCacheTtl, cancellationToken);

            return source;
        }

        return null;
    }

    public static async Task<MusicAudioMatch> GetExactAudioMatchAsync(MusicTrack track, CancellationToken cancellationToken = default)
    {
        if (track == null)
            return null;

        string exactTrackId = ParseTrackApiId(track.id);
        if (string.IsNullOrWhiteSpace(exactTrackId))
            return null;

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var exact = await LoadTrackElementAsync(exactTrackId, clientId, cancellationToken);
        if (!exact.HasValue || !IsPlayableAudioTrack(exact.Value))
            return null;

        return MapAudioMatch(exact.Value);
    }

    static bool IsPreviewStreamUrl(string streamUrl)
    {
        streamUrl = NormalizeValue(streamUrl);
        if (string.IsNullOrWhiteSpace(streamUrl))
            return false;

        if (streamUrl.Contains("cf-preview-media.sndcdn.com", StringComparison.OrdinalIgnoreCase))
            return true;

        if (streamUrl.Contains("/preview/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (streamUrl.Contains("cf-hls-media.sndcdn.com/playlist/0/30/", StringComparison.OrdinalIgnoreCase))
            return true;

        if (streamUrl.Contains("cf-hls-media.sndcdn.com/media/0/30/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static bool IsRelevantAudioMatch(MusicTrack track, MusicAudioMatch match)
    {
        if (track == null || match == null)
            return false;

        var payload = ParseMatchPayload(match);
        string expectedTrackId = ParseTrackApiId(track.id);
        string actualTrackId = NormalizeValue(payload?.track_id) ?? ParseTrackApiId(match.id);

        if (!string.IsNullOrWhiteSpace(expectedTrackId))
            return string.Equals(expectedTrackId, actualTrackId, StringComparison.OrdinalIgnoreCase);

        return ScoreAudioMatch(track, match) >= 40;
    }

    public static bool HasExactTrackId(MusicTrack track)
        => !string.IsNullOrWhiteSpace(ParseTrackApiId(track?.id));

    static List<string> BuildAudioQueries(MusicTrack track)
    {
        var queries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string value)
        {
            string normalized = NormalizeValue(value);
            if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                queries.Add(normalized);
        }

        string title = NormalizeValue(track?.title);
        string primaryArtist = NormalizeValue(track?.artist_name);
        string allArtists = track?.artists == null ? null : string.Join(", ", track.artists.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase));

        Add($"{primaryArtist} {title}");
        Add($"{allArtists} {title}");
        Add(title);

        return queries;
    }

    static IReadOnlyList<MusicAudioMatch> RankAudioMatches(MusicTrack expectedTrack, IEnumerable<JsonElement> candidates)
    {
        var ranked = new List<(MusicAudioMatch match, int score)>();

        foreach (var candidate in candidates)
        {
            var match = MapAudioMatch(candidate);
            if (match == null)
                continue;

            int score = ScoreAudioMatch(expectedTrack, match);
            if (score < 35)
                continue;

            match.duration_ms ??= GetInt(candidate, "full_duration") ?? GetInt(candidate, "duration");
            ranked.Add((match, score));
        }

        return ranked
            .OrderByDescending(x => x.score)
            .ThenBy(x => Math.Abs((x.match.duration_ms ?? 0) - (expectedTrack?.duration_ms ?? 0)))
            .ThenBy(x => x.match.title, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.match)
            .ToList();
    }

    static MusicAudioMatch MapAudioMatch(JsonElement track)
    {
        string trackId = GetString(track, "id");
        string rawTitle = NormalizeValue(GetString(track, "title"));
        var titleParts = SplitArtistTitleFromSoundCloudTitle(rawTitle);
        string title = NormalizeValue(titleParts.title) ?? rawTitle;
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(title))
            return null;

        string artistName = null;
        string permalinkUrl = GetString(track, "permalink_url");
        string urn = GetString(track, "urn");
        var artists = ExtractCandidateArtists(track);
        artistName = artists.FirstOrDefault();

        var payload = new SoundCloudMatchPayload
        {
            track_id = trackId,
            urn = urn,
            permalink_url = permalinkUrl,
            title = title,
            artist_name = artistName,
            duration_ms = GetInt(track, "full_duration") ?? GetInt(track, "duration"),
            track_authorization = NormalizeValue(GetString(track, "track_authorization"))
        };

        return new MusicAudioMatch
        {
            provider_id = AudioProviderId,
            id = !string.IsNullOrWhiteSpace(urn) ? urn : $"soundcloud:tracks:{trackId}",
            title = title,
            artists = artists,
            duration_ms = payload.duration_ms,
            payload = MusicJson.Serialize(payload)
        };
    }

    static int ScoreAudioMatch(MusicTrack expectedTrack, MusicAudioMatch candidate)
    {
        if (expectedTrack == null || candidate == null)
            return 0;

        string expectedTitle = NormalizeSearchText(expectedTrack.title);
        string candidateTitle = NormalizeSearchText(candidate.title);
        string expectedArtist = NormalizeSearchText(expectedTrack.artist_name);
        var expectedArtists = BuildArtistFragments(new[] { expectedTrack.artist_name }.Concat(expectedTrack.artists ?? Enumerable.Empty<string>()));
        var candidateArtists = BuildArtistFragments(candidate.artists);
        string candidateArtist = candidateArtists.FirstOrDefault() ?? NormalizeSearchText(candidate.artists.FirstOrDefault());
        string candidateText = NormalizeSearchText($"{candidate.title} {string.Join(' ', candidate.artists ?? new List<string>())}");

        int score = 0;

        if (!string.IsNullOrWhiteSpace(expectedTitle) && !string.IsNullOrWhiteSpace(candidateTitle))
        {
            if (string.Equals(expectedTitle, candidateTitle, StringComparison.Ordinal))
                score += 60;
            else if (candidateTitle.Contains(expectedTitle, StringComparison.Ordinal) || expectedTitle.Contains(candidateTitle, StringComparison.Ordinal))
                score += 35;
            else
                score += CountSharedTokens(expectedTitle, candidateTitle) * 8;
        }

        if (expectedArtists.Count > 0 && candidateArtists.Count > 0)
        {
            if (candidateArtists.Any(a => expectedArtists.Contains(a)))
                score += 30;
            else if (candidateArtists.Any(a => expectedArtists.Any(e => a.Contains(e, StringComparison.Ordinal) || e.Contains(a, StringComparison.Ordinal))))
                score += 16;
            else
                score += expectedArtists.Max(e => candidateArtists.Max(a => CountSharedTokens(e, a))) * 6;
        }
        else if (!string.IsNullOrWhiteSpace(expectedArtist) && !string.IsNullOrWhiteSpace(candidateArtist))
        {
            if (string.Equals(expectedArtist, candidateArtist, StringComparison.Ordinal))
                score += 30;
            else if (candidateArtist.Contains(expectedArtist, StringComparison.Ordinal) || expectedArtist.Contains(candidateArtist, StringComparison.Ordinal))
                score += 16;
            else
                score += CountSharedTokens(expectedArtist, candidateArtist) * 6;
        }

        if (expectedArtists.Count > 1 && candidateArtists.Count > 0)
        {
            foreach (string artist in expectedArtists.Skip(1).Take(3))
            {
                if (candidateArtists.Any(a => a == artist || a.Contains(artist, StringComparison.Ordinal)))
                    score += 4;
            }
        }

        int expectedDuration = expectedTrack.duration_ms ?? 0;
        int candidateDuration = candidate.duration_ms ?? 0;
        if (expectedDuration > 0 && candidateDuration > 0)
        {
            int delta = Math.Abs(expectedDuration - candidateDuration);
            if (delta <= 2_000) score += 18;
            else if (delta <= 5_000) score += 12;
            else if (delta <= 10_000) score += 6;
            else if (delta >= 30_000) score -= 12;
        }

        score -= ComputeDescriptorPenalty(expectedTitle, candidateText);
        return score;
    }

    static int ComputeDescriptorPenalty(string expectedText, string candidateText)
    {
        int penalty = 0;
        foreach (var descriptor in hardDescriptorPenalties)
        {
            bool expectedHas = HasDescriptor(expectedText, descriptor.term);
            bool candidateHas = HasDescriptor(candidateText, descriptor.term);
            if (!expectedHas && candidateHas)
                penalty += descriptor.penalty;
        }

        return penalty;
    }

    static readonly (string term, int penalty)[] hardDescriptorPenalties =
    [
        ("fast", 42),
        ("sped", 42),
        ("sped up", 42),
        ("slowed", 42),
        ("slowed reverb", 54),
        ("nightcore", 42),
        ("mp3 wav", 54),
        ("mp3", 28),
        ("wav", 28),
        ("reverb", 18),
        ("cover", 28),
        ("karaoke", 28),
        ("instrumental", 28),
        ("snippet", 36),
        ("looped", 28),
        ("8d", 24),
        ("bass boosted", 24),
        ("live", 18),
        ("remix", 18),
        ("remastered", 12)
    ];

    static bool HasDescriptor(string text, string descriptor)
    {
        string normalizedText = NormalizeSearchText(text);
        string normalizedDescriptor = NormalizeSearchText(descriptor);
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedDescriptor))
            return false;

        if (normalizedDescriptor.Contains(' ', StringComparison.Ordinal))
            return normalizedText.Contains(normalizedDescriptor, StringComparison.Ordinal);

        var tokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Contains(normalizedDescriptor, StringComparer.Ordinal);
    }

    static List<string> ExtractCandidateArtists(JsonElement track)
    {
        var artists = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddMany(string value)
        {
            foreach (string artist in SplitArtistNames(value))
            {
                if (!string.IsNullOrWhiteSpace(artist) && seen.Add(artist))
                    artists.Add(artist);
            }
        }

        if (track.TryGetProperty("publisher_metadata", out var publisher) && publisher.ValueKind == JsonValueKind.Object)
            AddMany(GetString(publisher, "artist"));

        var titleParts = SplitArtistTitleFromSoundCloudTitle(GetString(track, "title"));
        AddMany(titleParts.artist);

        if (track.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            AddMany(GetString(user, "username"));

        return artists;
    }

    static List<string> SplitArtistNames(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        return value
            .Split(new[] { ",", "&", " x ", " feat. ", " feat ", " ft. ", " ft ", " and " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => NormalizeValue(i))
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static List<string> BuildArtistFragments(IEnumerable<string> artists)
    {
        var fragments = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string artist in artists ?? Enumerable.Empty<string>())
        {
            foreach (string part in SplitArtistNames(artist))
            {
                string normalized = NormalizeSearchText(part);
                if (!string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized))
                    fragments.Add(normalized);
            }
        }

        return fragments;
    }

    static string NormalizeSearchText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    static int CountSharedTokens(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return 0;

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightSet = right.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        int shared = 0;
        foreach (string token in leftTokens)
        {
            if (rightSet.Contains(token))
                shared++;
        }

        return shared;
    }

    static bool IsPlayableAudioTrack(JsonElement track)
    {
        if (!track.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Object)
            return false;

        if (!media.TryGetProperty("transcodings", out var transcodings) || transcodings.ValueKind != JsonValueKind.Array)
            return false;

        return OrderPlayableTranscodings(transcodings).Any();
    }

    static IEnumerable<JsonElement> OrderPlayableTranscodings(JsonElement transcodings)
    {
        if (transcodings.ValueKind != JsonValueKind.Array)
            return Array.Empty<JsonElement>();

        return transcodings.EnumerateArray()
            .Where(IsPlainPlayableTranscoding)
            .Select(t => t.Clone())
            .OrderByDescending(GetTranscodingPriority)
            .ToList();
    }

    static bool IsPlainPlayableTranscoding(JsonElement transcoding)
    {
        string protocol = GetFormatProtocol(transcoding);
        string mime = GetFormatMime(transcoding);
        if (string.IsNullOrWhiteSpace(protocol) || protocol.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            return false;

        return protocol.Equals("progressive", StringComparison.OrdinalIgnoreCase)
            || protocol.Equals("hls", StringComparison.OrdinalIgnoreCase)
            || mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase)
            || mime.Contains("mp4", StringComparison.OrdinalIgnoreCase);
    }

    static int GetTranscodingPriority(JsonElement transcoding)
    {
        string protocol = GetFormatProtocol(transcoding);
        string mime = GetFormatMime(transcoding);

        if (protocol.Equals("progressive", StringComparison.OrdinalIgnoreCase) && mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
            return 400;
        if (protocol.Equals("progressive", StringComparison.OrdinalIgnoreCase))
            return 300;
        if (protocol.Equals("hls", StringComparison.OrdinalIgnoreCase) && mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
            return 200;
        if (protocol.Equals("hls", StringComparison.OrdinalIgnoreCase))
            return 100;

        return 0;
    }

    static string GetFormatProtocol(JsonElement transcoding)
    {
        if (transcoding.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
            return NormalizeValue(GetString(format, "protocol")) ?? string.Empty;

        return string.Empty;
    }

    static string GetFormatMime(JsonElement transcoding)
    {
        if (transcoding.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
            return NormalizeValue(GetString(format, "mime_type")) ?? "audio/mpeg";

        return "audio/mpeg";
    }

    static async Task<string> ResolveTranscodingUrlAsync(JsonElement transcoding, string clientId, string trackAuthorization, CancellationToken cancellationToken)
    {
        string transcodingUrl = NormalizeValue(GetString(transcoding, "url"));
        if (string.IsNullOrWhiteSpace(transcodingUrl))
            return null;

        var query = new List<string>
        {
            $"client_id={Uri.EscapeDataString(clientId)}"
        };

        if (!string.IsNullOrWhiteSpace(trackAuthorization))
            query.Add($"track_authorization={Uri.EscapeDataString(trackAuthorization)}");

        string url = transcodingUrl.Contains('?')
            ? $"{transcodingUrl}&{string.Join("&", query)}"
            : $"{transcodingUrl}?{string.Join("&", query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var header in CreatePublicHeaders())
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            InvalidatePublicClientIdOnUnauthorized(response.StatusCode, clientId);
            return null;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty("licenseAuthToken", out _))
            return null;

        return NormalizeValue(GetString(document.RootElement, "url"));
    }

    static SoundCloudMatchPayload ParseMatchPayload(MusicAudioMatch match)
    {
        if (string.IsNullOrWhiteSpace(match?.payload))
            return null;

        try
        {
            return MusicJson.Deserialize<SoundCloudMatchPayload>(match.payload);
        }
        catch
        {
            return null;
        }
    }

    static string BuildQualityLabel(string protocol, string mime)
    {
        if (protocol.Equals("progressive", StringComparison.OrdinalIgnoreCase) && mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
            return "MP3";
        if (protocol.Equals("hls", StringComparison.OrdinalIgnoreCase) && mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase))
            return "HLS MP3";
        if (protocol.Equals("hls", StringComparison.OrdinalIgnoreCase) && mime.Contains("mp4", StringComparison.OrdinalIgnoreCase))
            return "HLS AAC";
        if (protocol.Equals("progressive", StringComparison.OrdinalIgnoreCase))
            return "Direct";

        return "SoundCloud";
    }

    static string NormalizeSourceMime(string protocol, string mime, string streamUrl)
    {
        if (!string.IsNullOrWhiteSpace(protocol) && protocol.Contains("hls", StringComparison.OrdinalIgnoreCase))
            return "application/vnd.apple.mpegurl";

        if (!string.IsNullOrWhiteSpace(mime))
            return mime;

        return streamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ? "application/vnd.apple.mpegurl" : "audio/mpeg";
    }

    static int? EstimateBitrate(string streamUrl, string mime)
    {
        if (!string.IsNullOrWhiteSpace(streamUrl))
        {
            var match = Regex.Match(streamUrl, @"[._-](\d{2,3})k(?:\.|/)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int bitrate))
                return bitrate;
        }

        return mime.Contains("mpeg", StringComparison.OrdinalIgnoreCase) ? 128 : null;
    }

    static string BuildPreferredSourceCacheKey(MusicAudioMatch match)
    {
        var payload = ParseMatchPayload(match);
        string trackId = NormalizeValue(payload?.track_id) ?? ParseTrackApiId(match.id);
        return string.IsNullOrWhiteSpace(trackId) ? null : $"preferred:{trackId}";
    }
}
