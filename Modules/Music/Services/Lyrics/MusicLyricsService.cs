using Shared.Services.Hybrid;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Music;

// текст песен через lrclib.net (публичный API без ключей, как в Spotube);
// лирика — volatile metadata-обогащение, кэш в HybridFileCache
public static class MusicLyricsService
{
    const string CacheKeyPrefix = "music:lyrics:v12";
    const string YouTubeNoSyncedCacheKeyPrefix = "music:lyrics:yt-nosynced:v1";
    static readonly TimeSpan SyncedTtl = TimeSpan.FromDays(30);
    static readonly TimeSpan ShortTtl = TimeSpan.FromHours(1);
    static readonly TimeSpan YouTubeNoSyncedTtl = TimeSpan.FromHours(6);
    static readonly HttpClient httpClient = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LampacNextgenMusic/0.1 (https://github.com/lampac-nextgen/lampac)");
        return client;
    }

    public static async Task<MusicLyricsResponse> GetAsync(string title, string artistName, string albumTitle, int? durationMs, string youtubeId = null, CancellationToken cancellationToken = default)
    {
        title = title?.Trim();
        artistName = artistName?.Trim();
        youtubeId = NormalizeYouTubeId(youtubeId);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artistName))
            return new MusicLyricsResponse { available = false, message = "Track metadata is empty." };

        string cacheKey = BuildCacheKey(title, artistName, durationMs, youtubeId);
        var cached = await HybridCache.Get().ReadCacheAsync<MusicLyricsResponse>(cacheKey, false, null, textJson: true);
        if (cached.succes && cached.value != null)
            return cached.value;

        MusicLyricsResponse result;

        try
        {
            result = await FetchAsync(title, artistName, albumTitle, durationMs, youtubeId, cancellationToken)
                ?? new MusicLyricsResponse { available = false, message = "Lyrics not found." };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] lyrics fetch failed: {ex.Message}");
            // сетевые сбои не кэшируем — следующий запрос попробует снова
            return new MusicLyricsResponse { available = false, retry = true, message = "Lyrics provider unavailable." };
        }

        // Долгий кэш заслуживает только synced. Plain-only мог появиться из-за
        // временного сбоя YouTube Music fallback, поэтому partial/retry не кэшируем.
        if (!result.retry)
            HybridCache.Get().Set(cacheKey, result, result.available && result.synced ? SyncedTtl : ShortTtl, textJson: true);

        return result;
    }

    static async Task<MusicLyricsResponse> FetchAsync(string title, string artistName, string albumTitle, int? durationMs, string youtubeId, CancellationToken cancellationToken)
    {
        MusicLyricsResponse lrclib = null;
        bool lrclibFailed = false;

        try
        {
            lrclib = await FetchLrcLibAsync(title, artistName, albumTitle, durationMs, cancellationToken);
        }
        catch
        {
            lrclibFailed = true;
        }

        if (lrclib != null && lrclib.synced)
            return lrclib;

        MusicLyricsResponse youtubeMusic = null;
        bool youtubeMusicFailed = false;
        bool probeSyncedOnly = lrclib != null;
        string youTubeNoSyncedKey = probeSyncedOnly ? BuildYouTubeNoSyncedCacheKey(title, artistName, durationMs, youtubeId) : null;

        if (probeSyncedOnly)
        {
            var noSyncedCached = await HybridCache.Get().ReadCacheAsync<bool>(youTubeNoSyncedKey, false, null, textJson: true);
            if (noSyncedCached.succes && noSyncedCached.value)
                return lrclib;
        }

        try
        {
            youtubeMusic = await YouTubeMusicLyricsFallback.GetAsync(title, artistName, durationMs, youtubeId, probeSyncedOnly, cancellationToken);
        }
        catch (Exception ex)
        {
            youtubeMusicFailed = true;
            Console.WriteLine($"[Music] youtube music lyrics fallback failed: {ex.Message}");
        }

        if (youtubeMusic != null && youtubeMusic.synced)
            return youtubeMusic;

        if (probeSyncedOnly && !youtubeMusicFailed)
            HybridCache.Get().Set(youTubeNoSyncedKey, true, YouTubeNoSyncedTtl, textJson: true);

        if (lrclib != null)
        {
            if (youtubeMusicFailed)
                lrclib.retry = true;

            return lrclib;
        }

        if (youtubeMusic != null)
            return youtubeMusic;

        // Если хоть один провайдер не успел/упал, не кэшируем «нет текста»:
        // следующий запрос может попасть в живой источник.
        if (lrclibFailed || youtubeMusicFailed)
            throw new TimeoutException("lyrics provider timed out");

        return null;
    }

    static async Task<MusicLyricsResponse> FetchLrcLibAsync(string title, string artistName, string albumTitle, int? durationMs, CancellationToken cancellationToken)
    {
        string getUrl = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title)
            + "&artist_name=" + Uri.EscapeDataString(artistName)
            + (string.IsNullOrWhiteSpace(albumTitle) ? "" : "&album_name=" + Uri.EscapeDataString(albumTitle))
            + (durationMs > 0 ? "&duration=" + (durationMs.Value / 1000) : "");

        string searchUrl = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title)
            + "&artist_name=" + Uri.EscapeDataString(artistName);

        // lrclib бывает медленным: оба запроса параллельно с общим бюджетом,
        // чтобы ответ гарантированно уложился в терпение клиента
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(9));

        var getTask = RequestJsonAsync(getUrl, budget.Token);
        var searchTask = RequestJsonAsync(searchUrl, budget.Token);

        JsonNode getNode = null, searchNode = null;
        bool getFailed = false, searchFailed = false;

        try { getNode = await getTask; } catch { getFailed = true; }
        try { searchNode = await searchTask; } catch { searchFailed = true; }

        var exact = BuildResponse(getNode as JsonObject);
        if (exact != null && exact.synced)
            return exact;

        // приоритет: synced-точный → synced-из-поиска → plain-точный → plain-из-поиска
        MusicLyricsResponse searched = null;

        if (searchNode is JsonArray items && items.Count > 0)
        {
            int targetSeconds = durationMs > 0 ? durationMs.Value / 1000 : 0;
            JsonObject best = null;
            JsonObject bestPlain = null;

            foreach (var item in items.OfType<JsonObject>())
            {
                if (targetSeconds > 0)
                {
                    double itemDuration = item["duration"]?.GetValue<double?>() ?? 0;
                    if (itemDuration > 0 && Math.Abs(itemDuration - targetSeconds) > 10)
                        continue;
                }

                if (!string.IsNullOrWhiteSpace(item["syncedLyrics"]?.GetValue<string>()))
                {
                    best = item;
                    break;
                }

                bestPlain ??= string.IsNullOrWhiteSpace(item["plainLyrics"]?.GetValue<string>()) ? null : item;
            }

            searched = BuildResponse(best ?? bestPlain);
        }

        if (searched != null && searched.synced)
            return searched;

        var result = exact ?? searched;

        // ничего не нашли, но часть запросов упала по таймауту — это не «нет
        // текста», а сбой: бросаем, чтобы результат НЕ закэшировался
        if (result == null && (getFailed || searchFailed))
            throw new TimeoutException("lyrics provider timed out");

        return result;
    }

    static async Task<JsonNode> RequestJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
    }

    static MusicLyricsResponse BuildResponse(JsonObject item)
    {
        if (item == null)
            return null;

        string syncedRaw = item["syncedLyrics"]?.GetValue<string>();
        string plain = item["plainLyrics"]?.GetValue<string>();

        var lines = ParseSyncedLyrics(syncedRaw);

        if (lines.Count == 0 && string.IsNullOrWhiteSpace(plain))
            return null;

        return new MusicLyricsResponse
        {
            available = true,
            message = "ok",
            source = "lrclib",
            source_mode = lines.Count > 0 ? "synced" : "plain",
            synced = lines.Count > 0,
            lines = lines,
            plain = plain
        };
    }

    static List<MusicLyricsLine> ParseSyncedLyrics(string lrc)
    {
        var lines = new List<MusicLyricsLine>();

        if (string.IsNullOrWhiteSpace(lrc))
            return lines;

        foreach (var raw in lrc.Split('\n'))
        {
            var match = Regex.Match(raw, @"^\s*\[(\d+):(\d+(?:\.\d+)?)\]\s*(.*)$");
            if (!match.Success)
                continue;

            double minutes = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            lines.Add(new MusicLyricsLine
            {
                time_ms = (int)Math.Round((minutes * 60 + seconds) * 1000),
                text = match.Groups[3].Value.Trim()
            });
        }

        return lines.OrderBy(i => i.time_ms).ToList();
    }

    static string BuildCacheKey(string title, string artistName, int? durationMs, string youtubeId)
    {
        // длительность — в корзинах по 10s, чтобы близкие рипы попадали в один кэш
        int bucket = durationMs > 0 ? durationMs.Value / 10000 : 0;
        return string.Join(':', CacheKeyPrefix, artistName.ToLowerInvariant(), title.ToLowerInvariant(), bucket.ToString(), youtubeId ?? "auto");
    }

    static string BuildYouTubeNoSyncedCacheKey(string title, string artistName, int? durationMs, string youtubeId)
    {
        int bucket = durationMs > 0 ? durationMs.Value / 10000 : 0;
        return string.Join(':', YouTubeNoSyncedCacheKeyPrefix, artistName.ToLowerInvariant(), title.ToLowerInvariant(), bucket.ToString(), youtubeId ?? "auto");
    }

    static string NormalizeYouTubeId(string youtubeId)
    {
        if (string.IsNullOrWhiteSpace(youtubeId))
            return null;

        youtubeId = youtubeId.Trim();
        if (youtubeId.StartsWith("youtube:", StringComparison.OrdinalIgnoreCase))
            youtubeId = youtubeId["youtube:".Length..].Trim();

        return Regex.IsMatch(youtubeId, @"^[A-Za-z0-9_-]{6,32}$") ? youtubeId : null;
    }
}
