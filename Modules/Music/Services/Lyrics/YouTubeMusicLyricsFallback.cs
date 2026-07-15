using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Music;

internal static class YouTubeMusicLyricsFallback
{
    const string HomeUrl = "https://music.youtube.com/";
    const string InnertubeBaseUrl = "https://music.youtube.com/youtubei/v1/";
    const string SongsSearchParams = "EgWKAQIIAWoMEA4QChADEAQQCRAF";
    const int MaxCandidates = 4;

    static readonly HttpClient httpClient = CreateClient();
    static readonly SemaphoreSlim configLock = new(1, 1);
    static YouTubeMusicClientConfig cachedConfig;
    static DateTimeOffset configExpiresAt;

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", HomeUrl.TrimEnd('/'));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", HomeUrl);
        return client;
    }

    public static async Task<MusicLyricsResponse> GetAsync(string title, string artistName, int? durationMs, string youtubeId = null, bool syncedOnly = false, CancellationToken cancellationToken = default)
    {
        if (!YouTubeMusicSearchSupport.IsSearchEnabled || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artistName))
            return null;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(12));

        var config = await GetConfigAsync(budget.Token);

        youtubeId = NormalizeVideoId(youtubeId);
        MusicLyricsResponse plainFallback = null;

        if (!string.IsNullOrWhiteSpace(youtubeId))
        {
            // Прямой YouTube id может прийти с грязной карточки, поэтому доверяем
            // ему только если он отдаёт настоящие тайминги. Plain ищем через
            // канонический поиск по title/artist ниже.
            var directLyrics = await FetchLyricsAsync(config, youtubeId, durationMs, budget.Token, syncedOnly: true);
            if (directLyrics != null && directLyrics.synced)
                return directLyrics;
        }

        var candidates = await SearchSongCandidatesAsync(config, title, artistName, durationMs, budget.Token);

        foreach (var candidate in candidates)
        {
            var lyrics = await FetchLyricsAsync(config, candidate.VideoId, durationMs, budget.Token, syncedOnly);
            if (lyrics != null && lyrics.synced)
                return lyrics;

            if (!syncedOnly)
                plainFallback ??= lyrics;
        }

        return plainFallback;
    }

    static MusicLyricsResponse BuildPlainResponse(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
            return null;

        return new MusicLyricsResponse
        {
            available = true,
            message = "ok",
            source = "youtube_music",
            source_mode = "plain",
            synced = false,
            plain = plain.Trim()
        };
    }

    static string NormalizeVideoId(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        videoId = videoId.Trim();
        if (videoId.StartsWith("youtube:", StringComparison.OrdinalIgnoreCase))
            videoId = videoId["youtube:".Length..].Trim();

        return Regex.IsMatch(videoId, @"^[A-Za-z0-9_-]{6,32}$") ? videoId : null;
    }

    static async Task<YouTubeMusicClientConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        if (cachedConfig != null && DateTimeOffset.UtcNow < configExpiresAt)
            return cachedConfig;

        await configLock.WaitAsync(cancellationToken);

        try
        {
            if (cachedConfig != null && DateTimeOffset.UtcNow < configExpiresAt)
                return cachedConfig;

            string html = await httpClient.GetStringAsync(HomeUrl, cancellationToken);
            string apiKey = MatchConfigValue(html, "INNERTUBE_API_KEY");
            string clientVersion = MatchConfigValue(html, "INNERTUBE_CLIENT_VERSION");
            string visitorData = MatchConfigValue(html, "VISITOR_DATA");

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(clientVersion))
                throw new InvalidOperationException("YouTube Music InnerTube config not found.");

            cachedConfig = new YouTubeMusicClientConfig
            {
                ApiKey = apiKey,
                ClientVersion = clientVersion,
                VisitorData = visitorData
            };
            configExpiresAt = DateTimeOffset.UtcNow.AddHours(6);

            return cachedConfig;
        }
        finally
        {
            configLock.Release();
        }
    }

    static string MatchConfigValue(string html, string key)
    {
        var match = Regex.Match(html ?? string.Empty, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? Regex.Unescape(match.Groups[1].Value) : null;
    }

    static async Task<List<YouTubeMusicSongCandidate>> SearchSongCandidatesAsync(YouTubeMusicClientConfig config, string title, string artistName, int? durationMs, CancellationToken cancellationToken)
    {
        int targetMs = durationMs.GetValueOrDefault();
        var result = new List<YouTubeMusicSongCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var query in BuildCandidateQueries(title, artistName))
        {
            var root = await PostAsync(config, "search", new JsonObject
            {
                ["context"] = BuildContext(config),
                ["query"] = query.Query,
                ["params"] = SongsSearchParams
            }, cancellationToken);

            var candidates = new List<YouTubeMusicSongCandidate>();
            CollectSongCandidates(root, candidates);

            foreach (var candidate in candidates.Where(i => IsRelevantCandidate(i, query.Title, query.Artist, targetMs, query.RequireArtist)))
            {
                if (string.IsNullOrWhiteSpace(candidate.VideoId) || !seen.Add(candidate.VideoId))
                    continue;

                result.Add(candidate);
                if (result.Count >= MaxCandidates)
                    return result;
            }
        }

        return result;
    }

    static async Task<MusicLyricsResponse> FetchLyricsAsync(YouTubeMusicClientConfig config, string videoId, int? durationMs, CancellationToken cancellationToken, bool syncedOnly = false)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        var next = await PostAsync(config, "next", new JsonObject
        {
            ["context"] = BuildContext(config),
            ["videoId"] = videoId,
            ["playlistId"] = "RDAMVM" + videoId,
            ["enablePersistentPlaylistPanel"] = true,
            ["isAudioOnly"] = true,
            ["tunerSettingValue"] = "AUTOMIX_SETTING_NORMAL",
            ["watchEndpointMusicSupportedConfigs"] = new JsonObject
            {
                ["watchEndpointMusicConfig"] = new JsonObject
                {
                    ["hasPersistentPlaylistPanel"] = true,
                    ["musicVideoType"] = "MUSIC_VIDEO_TYPE_ATV"
                }
            }
        }, cancellationToken);

        string browseId = FindBrowseIdByPageType(next, "MUSIC_PAGE_TYPE_TRACK_LYRICS");
        if (string.IsNullOrWhiteSpace(browseId))
            return null;

        var timedBrowse = await PostAsync(config, "browse", new JsonObject
        {
            ["context"] = BuildContext(config, mobile: true),
            ["browseId"] = browseId
        }, cancellationToken);

        var timedLines = FindTimedLyricsLines(timedBrowse);
        if (timedLines.Count > 0)
        {
            return new MusicLyricsResponse
            {
                available = true,
                message = "ok",
                source = "youtube_music",
                source_mode = "synced",
                synced = true,
                lines = timedLines,
                plain = string.Join("\n", timedLines.Select(i => i.text).Where(i => !string.IsNullOrWhiteSpace(i)))
            };
        }

        if (syncedOnly)
            return null;

        string staticPlain = FindStaticLyricsText(timedBrowse);
        if (!string.IsNullOrWhiteSpace(staticPlain))
            return BuildPlainResponse(staticPlain);

        string mobilePlain = FindDescriptionShelfText(timedBrowse);
        if (!string.IsNullOrWhiteSpace(mobilePlain) && !mobilePlain.Contains("Lyrics not available", StringComparison.OrdinalIgnoreCase))
            return BuildPlainResponse(mobilePlain);

        var browse = await PostAsync(config, "browse", new JsonObject
        {
            ["context"] = BuildContext(config),
            ["browseId"] = browseId
        }, cancellationToken);

        string lyrics = FindDescriptionShelfText(browse);

        if (string.IsNullOrWhiteSpace(lyrics) || lyrics.Contains("Lyrics not available", StringComparison.OrdinalIgnoreCase))
            return null;

        return BuildPlainResponse(lyrics);
    }

    static async Task<JsonNode> PostAsync(YouTubeMusicClientConfig config, string endpoint, JsonObject body, CancellationToken cancellationToken)
    {
        string url = InnertubeBaseUrl + endpoint + "?key=" + Uri.EscapeDataString(config.ApiKey);

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                InvalidateConfig();

            throw new HttpRequestException($"YouTube Music {endpoint} returned {(int)response.StatusCode}");
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
    }

    static void InvalidateConfig()
    {
        cachedConfig = null;
        configExpiresAt = DateTimeOffset.MinValue;
    }

    static JsonObject BuildContext(YouTubeMusicClientConfig config, bool mobile = false)
    {
        var client = new JsonObject
        {
            ["clientName"] = mobile ? "ANDROID_MUSIC" : "WEB_REMIX",
            ["clientVersion"] = mobile ? "7.21.50" : config.ClientVersion,
            ["hl"] = "ru",
            ["gl"] = "US"
        };

        if (!string.IsNullOrWhiteSpace(config.VisitorData))
            client["visitorData"] = config.VisitorData;

        return new JsonObject
        {
            ["client"] = client
        };
    }

    static void CollectSongCandidates(JsonNode node, List<YouTubeMusicSongCandidate> candidates)
    {
        if (node is JsonObject obj)
        {
            if (obj["musicResponsiveListItemRenderer"] is JsonObject renderer)
            {
                string videoId = FindWatchVideoId(renderer);
                if (!string.IsNullOrWhiteSpace(videoId))
                {
                    var texts = ExtractFlexTexts(renderer);
                    string title = texts.FirstOrDefault();
                    string joined = string.Join(" ", texts.Where(i => !string.IsNullOrWhiteSpace(i)));

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        candidates.Add(new YouTubeMusicSongCandidate
                        {
                            VideoId = videoId,
                            Title = title,
                            Description = joined,
                            DurationMs = ParseDurationMs(joined)
                        });
                    }
                }
            }

            foreach (var child in obj)
                CollectSongCandidates(child.Value, candidates);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                CollectSongCandidates(child, candidates);
        }
    }

    static string FindWatchVideoId(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["watchEndpoint"] is JsonObject endpoint)
            {
                string videoId = endpoint["videoId"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(videoId))
                    return videoId;
            }

            foreach (var child in obj)
            {
                string result = FindWatchVideoId(child.Value);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                string result = FindWatchVideoId(child);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }

        return null;
    }

    static List<string> ExtractFlexTexts(JsonObject renderer)
    {
        var result = new List<string>();

        if (renderer["flexColumns"] is not JsonArray columns)
            return result;

        foreach (var column in columns.OfType<JsonObject>())
        {
            var textNode = column["musicResponsiveListItemFlexColumnRenderer"]?["text"];
            string text = ExtractText(textNode);
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(Regex.Replace(text.Trim(), @"\s+", " "));
        }

        return result;
    }

    static string ExtractText(JsonNode node)
    {
        if (node is not JsonObject obj)
            return null;

        if (obj["simpleText"] != null)
            return obj["simpleText"]?.GetValue<string>();

        if (obj["runs"] is JsonArray runs)
            return string.Concat(runs.OfType<JsonObject>().Select(i => i["text"]?.GetValue<string>() ?? string.Empty));

        return null;
    }

    static int? ParseDurationMs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var matches = Regex.Matches(text, @"(?<!\d)(\d{1,2}):(\d{2})(?::(\d{2}))?(?!\d)");
        if (matches.Count == 0)
            return null;

        var match = matches[^1];
        int first = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int second = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int third = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;

        int seconds = match.Groups[3].Success
            ? first * 3600 + second * 60 + third
            : first * 60 + second;

        return seconds * 1000;
    }

    static List<YouTubeMusicLyricsQuery> BuildCandidateQueries(string title, string artistName)
    {
        var result = new List<YouTubeMusicLyricsQuery>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string cleanTitle = CleanSearchTitle(title);
        string artist = CleanSearchTitle(artistName);

        void add(string query, string queryTitle, string queryArtist, bool requireArtist)
        {
            query = Regex.Replace(query ?? string.Empty, @"\s+", " ").Trim();
            queryTitle = Regex.Replace(queryTitle ?? string.Empty, @"\s+", " ").Trim();
            queryArtist = Regex.Replace(queryArtist ?? string.Empty, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(queryTitle))
                return;

            string key = NormalizeText($"{query}|{queryTitle}|{queryArtist}|{requireArtist}");
            if (!seen.Add(key))
                return;

            result.Add(new YouTubeMusicLyricsQuery
            {
                Query = query,
                Title = queryTitle,
                Artist = queryArtist,
                RequireArtist = requireArtist
            });
        }

        add($"{artistName} {title}", title, artistName, requireArtist: true);

        if (!string.Equals(cleanTitle, title, StringComparison.Ordinal))
            add($"{artist} {cleanTitle}", cleanTitle, artist, requireArtist: !string.IsNullOrWhiteSpace(artist));

        var trailing = ExtractTrailingArtist(cleanTitle);
        if (!string.IsNullOrWhiteSpace(trailing.title) && !string.IsNullOrWhiteSpace(trailing.artist))
            add($"{trailing.artist} {trailing.title}", trailing.title, trailing.artist, requireArtist: true);

        add(cleanTitle, cleanTitle, null, requireArtist: false);
        return result;
    }

    static string CleanSearchTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = Regex.Replace(value, @"#\S+", " ");
        value = Regex.Replace(value, @"\b(official|audio|video|lyrics?|visualizer|remaster(?:ed)?|clip)\b", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"[\[\](){}]", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    static (string title, string artist) ExtractTrailingArtist(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null);

        var parts = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return (null, null);

        string last = parts[^1].Trim();
        string normalizedLast = NormalizeText(last);

        if (normalizedLast.Length < 3)
            return (null, null);

        if (!Regex.IsMatch(last, @"^\p{L}[\p{L}\p{Nd}._-]*$", RegexOptions.IgnoreCase))
            return (null, null);

        if (Regex.IsMatch(normalizedLast, @"^(remix|mix|edit|audio|video|lyrics|music|official|cover|live)$", RegexOptions.IgnoreCase))
            return (null, null);

        string baseTitle = string.Join(' ', parts.Take(parts.Length - 1)).Trim();
        return string.IsNullOrWhiteSpace(baseTitle) ? (null, null) : (baseTitle, last);
    }

    static bool IsRelevantCandidate(YouTubeMusicSongCandidate candidate, string title, string artistName, int targetMs, bool requireArtist = true)
    {
        string wantedTitle = NormalizeTitle(title);
        string candidateTitle = NormalizeTitle(candidate.Title);
        string wantedArtist = NormalizeText(artistName);
        string candidateDescription = NormalizeText(candidate.Description);

        if (string.IsNullOrWhiteSpace(wantedTitle) || string.IsNullOrWhiteSpace(candidateTitle))
            return false;

        bool titleMatches = candidateTitle == wantedTitle
            || candidateTitle.Contains(wantedTitle, StringComparison.Ordinal)
            || wantedTitle.Contains(candidateTitle, StringComparison.Ordinal)
            || TitleTokenCoverage(wantedTitle, candidateTitle) >= 0.75;

        if (!titleMatches)
            return false;

        bool artistMatches = string.IsNullOrWhiteSpace(wantedArtist) || candidateDescription.Contains(wantedArtist, StringComparison.Ordinal);

        if (!artistMatches)
        {
            var artistTokens = wantedArtist.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            artistMatches = artistTokens.Any(i => i.Length > 2 && candidateDescription.Contains(i, StringComparison.Ordinal));
        }

        if (requireArtist && !artistMatches)
            return false;

        if (targetMs > 0 && candidate.DurationMs > 0 && Math.Abs(candidate.DurationMs.Value - targetMs) > 25000)
            return false;

        return true;
    }

    static string NormalizeTitle(string value)
    {
        value = Regex.Replace(value ?? string.Empty, @"\b(feat\.?|ft\.?|featuring)\b.*$", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\((official|audio|video|lyrics?|visualizer|remaster(ed)?|clip)[^)]*\)", " ", RegexOptions.IgnoreCase);
        return NormalizeText(value);
    }

    static double TitleTokenCoverage(string wantedTitle, string candidateTitle)
    {
        var wantedTokens = wantedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (wantedTokens.Length == 0)
            return 0;

        var candidateTokens = candidateTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        int matched = wantedTokens.Count(wanted => candidateTokens.Any(candidate => TokensClose(wanted, candidate)));
        return matched / (double)wantedTokens.Length;
    }

    static bool TokensClose(string left, string right)
    {
        if (left == right)
            return true;

        int min = Math.Min(left.Length, right.Length);
        if (min < 4)
            return false;

        return left.AsSpan(0, 4).SequenceEqual(right.AsSpan(0, 4));
    }

    static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"[^\p{L}\p{Nd}]+", " ").Trim();
    }

    static string FindBrowseIdByPageType(JsonNode node, string pageType)
    {
        if (node is JsonObject obj)
        {
            if (obj["browseEndpoint"] is JsonObject endpoint)
            {
                string browseId = endpoint["browseId"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(browseId) && ContainsPageType(endpoint, pageType))
                    return browseId;
            }

            foreach (var child in obj)
            {
                string result = FindBrowseIdByPageType(child.Value, pageType);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                string result = FindBrowseIdByPageType(child, pageType);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }

        return null;
    }

    static bool ContainsPageType(JsonNode node, string pageType)
    {
        if (node is JsonObject obj)
        {
            foreach (var item in obj)
            {
                if (item.Key == "pageType" && item.Value?.GetValue<string>() == pageType)
                    return true;

                if (ContainsPageType(item.Value, pageType))
                    return true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (ContainsPageType(child, pageType))
                    return true;
            }
        }

        return false;
    }

    static string FindDescriptionShelfText(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["musicDescriptionShelfRenderer"] is JsonObject shelf)
            {
                string text = ExtractText(shelf["description"]);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            foreach (var child in obj)
            {
                string result = FindDescriptionShelfText(child.Value);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                string result = FindDescriptionShelfText(child);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }

        return null;
    }

    static List<MusicLyricsLine> FindTimedLyricsLines(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["timedLyricsData"] is JsonArray timed)
            {
                var lines = timed
                    .OfType<JsonObject>()
                    .Select(ParseTimedLyricsLine)
                    .Where(i => i != null)
                    .OrderBy(i => i.time_ms)
                    .ToList();

                if (lines.Count > 0)
                    return lines;
            }

            foreach (var child in obj)
            {
                var result = FindTimedLyricsLines(child.Value);
                if (result.Count > 0)
                    return result;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                var result = FindTimedLyricsLines(child);
                if (result.Count > 0)
                    return result;
            }
        }

        return new List<MusicLyricsLine>();
    }

    static string FindStaticLyricsText(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj["timedLyricsData"] is JsonArray timed)
            {
                var lines = timed
                    .OfType<JsonObject>()
                    .Select(i => i["lyricLine"]?.GetValue<string>()?.Trim())
                    .Where(i => !string.IsNullOrWhiteSpace(i) && !string.Equals(i, "♪", StringComparison.Ordinal))
                    .ToList();

                if (lines.Count > 0)
                    return string.Join("\n", lines);
            }

            foreach (var child in obj)
            {
                string result = FindStaticLyricsText(child.Value);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                string result = FindStaticLyricsText(child);
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }
        }

        return null;
    }

    static MusicLyricsLine ParseTimedLyricsLine(JsonObject item)
    {
        string text = item["lyricLine"]?.GetValue<string>()?.Trim();
        string start = item["cueRange"]?["startTimeMilliseconds"]?.GetValue<string>();

        if (!long.TryParse(start, NumberStyles.Integer, CultureInfo.InvariantCulture, out long timeMs))
            return null;

        return new MusicLyricsLine
        {
            time_ms = (int)Math.Max(0, Math.Min(int.MaxValue, timeMs)),
            text = string.Equals(text, "♪", StringComparison.Ordinal) ? "" : text
        };
    }

    class YouTubeMusicClientConfig
    {
        public string ApiKey { get; set; }
        public string ClientVersion { get; set; }
        public string VisitorData { get; set; }
    }

    class YouTubeMusicSongCandidate
    {
        public string VideoId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int? DurationMs { get; set; }
    }

    class YouTubeMusicLyricsQuery
    {
        public string Query { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public bool RequireArtist { get; set; }
    }
}
