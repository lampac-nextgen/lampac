using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace Music;

public static class DiscogsArtistImageService
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    static readonly SemaphoreSlim requestGate = new(1, 1);
    static readonly ConcurrentDictionary<string, Task<List<MusicImage>>> pendingLookups = new();
    static DateTime nextRequestAt = DateTime.MinValue;

    static readonly TimeSpan cacheTtl = TimeSpan.FromDays(30);
    const string cacheVersion = "v6";

    const string baseUrl = "https://api.discogs.com";
    const string userAgent = "LampacNextgenMusic/0.1 (https://github.com/lampac-nextgen/lampac)";

    static DiscogsArtistImageService()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public static async Task EnrichAsync(List<MusicArtist> artists, CancellationToken cancellationToken = default)
    {
        if (artists == null || artists.Count == 0)
            return;

        var seen = new HashSet<string>();

        foreach (var artist in artists)
        {
            if (artist == null)
                continue;

            string normalized = NormalizeArtistName(artist.name);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                continue;

            await EnrichAsync(artist, cancellationToken);
        }
    }

    public static async Task ApplyCachedAsync(List<MusicArtist> artists, CancellationToken cancellationToken = default)
    {
        if (artists == null || artists.Count == 0)
            return;

        var seen = new HashSet<string>();

        foreach (var artist in artists)
        {
            if (artist == null || !string.IsNullOrWhiteSpace(artist.images?.FirstOrDefault()?.url) || string.IsNullOrWhiteSpace(artist.name))
                continue;

            string normalized = NormalizeArtistName(artist.name);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                continue;

            var images = await GetCachedImagesAsync(artist.name, artist.country, cancellationToken);

            if (images.Count > 0)
                artist.images = images;
        }
    }

    public static void WarmupMissing(List<MusicArtist> artists)
    {
        if (artists == null || artists.Count == 0)
            return;

        var queue = artists
            .Where(i => i != null && string.IsNullOrWhiteSpace(i.images?.FirstOrDefault()?.url) && !string.IsNullOrWhiteSpace(i.name))
            .GroupBy(i => BuildCacheKey(i.name, i.country))
            .Select(i => i.First())
            .Take(3)
            .ToList();

        if (queue.Count == 0)
            return;

        _ = Task.Run(async () =>
        {
            foreach (var artist in queue)
            {
                try
                {
                    await ResolveImagesAsync(artist, CancellationToken.None);
                }
                catch
                {
                }
            }
        });
    }

    public static async Task EnrichAsync(MusicArtist artist, CancellationToken cancellationToken = default)
    {
        if (artist == null || !string.IsNullOrWhiteSpace(artist.images?.FirstOrDefault()?.url) || string.IsNullOrWhiteSpace(artist.name))
            return;

        var images = await ResolveImagesAsync(artist, cancellationToken);

        if (images.Count > 0)
            artist.images = images;
    }

    public static async Task<List<MusicImage>> ResolveImagesAsync(MusicArtist artist, CancellationToken cancellationToken = default)
    {
        if (artist == null || string.IsNullOrWhiteSpace(artist.name))
            return new List<MusicImage>();

        string cacheKey = BuildCacheKey(artist.name, artist.country);
        var cached = await GetCachedImagesAsync(artist.name, artist.country, cancellationToken);
        if (cached.Count > 0)
            return cached;

        var pending = pendingLookups.GetOrAdd(cacheKey, _ => LookupAndCacheAsync(cacheKey, artist.name, artist.country));

        try
        {
            return await pending;
        }
        finally
        {
            if (pending.IsCompleted)
                pendingLookups.TryRemove(new KeyValuePair<string, Task<List<MusicImage>>>(cacheKey, pending));
        }
    }

    public static async Task<List<MusicImage>> GetCachedImagesAsync(string artistName, string country = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artistName))
            return new List<MusicImage>();

        string cacheKey = BuildCacheKey(artistName, country);
        var exact = await MusicMetadataCacheService.GetAsync<List<MusicImage>>(
            "discogs",
            "artist-image",
            cacheKey,
            cancellationToken
        ) ?? new List<MusicImage>();

        if (exact.Count > 0 || string.IsNullOrWhiteSpace(country))
            return exact;

        return await MusicMetadataCacheService.GetAsync<List<MusicImage>>(
            "discogs",
            "artist-image",
            BuildCacheKey(artistName, null),
            cancellationToken
        ) ?? new List<MusicImage>();
    }

    static async Task<List<MusicImage>> LookupAndCacheAsync(string cacheKey, string artistName, string country)
    {
        return await MusicMetadataCacheService.GetOrCreateAsync(
            "discogs",
            "artist-image",
            cacheKey,
            cacheTtl,
            () => LookupImagesAsync(artistName, country, CancellationToken.None),
            CancellationToken.None
        ) ?? new List<MusicImage>();
    }

    static async Task<List<MusicImage>> LookupImagesAsync(string artistName, string country, CancellationToken cancellationToken)
    {
        var searchJson = await GetJsonAsync($"database/search?q={HttpUtility.UrlEncode(artistName)}&type=artist&per_page=5", cancellationToken);

        // null = сеть/429 — НЕ кэшируем (GetOrCreateAsync пропускает null),
        // чтобы сбой Discogs не превращался в «у артиста нет фото»
        if (searchJson == null)
            return null;

        var best = SelectBestArtist(searchJson["results"] as JsonArray, artistName, country);
        if (best == null)
            return new List<MusicImage>();

        var id = best["id"]?.GetValue<int?>();
        if (id == null || id <= 0)
            return new List<MusicImage>();

        var artistJson = await GetJsonAsync($"artists/{id}", cancellationToken);
        if (artistJson == null)
            return null;

        return ParseImages(artistJson["images"] as JsonArray);
    }

    static JsonObject SelectBestArtist(JsonArray results, string artistName, string country)
    {
        if (results == null || results.Count == 0)
            return null;

        string target = NormalizeArtistName(artistName);
        int bestScore = int.MinValue;
        JsonObject best = null;

        foreach (var item in results.OfType<JsonObject>())
        {
            int score = ScoreArtist(item, target, country);
            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        return bestScore > 0 ? best : null;
    }

    static int ScoreArtist(JsonObject item, string target, string country)
    {
        string rawTitle = item["title"]?.GetValue<string>() ?? string.Empty;
        string title = NormalizeArtistName(rawTitle);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(target))
            return 0;

        if (title != target)
            return 0;

        int score = 100;

        if (string.Equals(rawTitle.Trim().ToLowerInvariant(), target, StringComparison.Ordinal))
            score += 15;

        if (rawTitle.Contains("(")) score -= 10;
        if (Regex.IsMatch(rawTitle, @"\(\d+\)\s*$")) score -= 10;

        if (!string.IsNullOrWhiteSpace(country) && rawTitle.Contains(country, StringComparison.OrdinalIgnoreCase))
            score += 5;

        string thumb = item["thumb"]?.GetValue<string>() ?? string.Empty;
        string cover = item["cover_image"]?.GetValue<string>() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(thumb) || !string.IsNullOrWhiteSpace(cover))
            score += 3;

        return score;
    }

    static string NormalizeArtistName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = Regex.Replace(value, @"\s*\(\d+\)\s*$", string.Empty);
        value = Regex.Replace(value, @"[^0-9\p{L}\s]+", " ");
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value.ToLowerInvariant();
    }

    static List<MusicImage> ParseImages(JsonArray items)
    {
        if (items == null || items.Count == 0)
            return new List<MusicImage>();

        var primary = items
            .OfType<JsonObject>()
            .OrderByDescending(item => string.Equals(item["type"]?.GetValue<string>(), "primary", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (primary == null)
            return new List<MusicImage>();

        string small = primary["uri150"]?.GetValue<string>();
        string full = primary["uri"]?.GetValue<string>();
        int width = primary["width"]?.GetValue<int?>() ?? 0;
        int height = primary["height"]?.GetValue<int?>() ?? 0;

        if (string.IsNullOrWhiteSpace(small) && string.IsNullOrWhiteSpace(full))
            return new List<MusicImage>();

        var result = new List<MusicImage>();

        if (!string.IsNullOrWhiteSpace(small))
        {
            result.Add(new()
            {
                url = small,
                width = 150,
                height = 150
            });
        }

        if (!string.IsNullOrWhiteSpace(full) && width > 0 && height > 0)
        {
            result.Add(new()
            {
                url = full,
                width = width,
                height = height
            });
        }

        return result
            .GroupBy(i => i.url)
            .Select(i => i.First())
            .OrderBy(i => i.width)
            .ToList();
    }

    static async Task<JsonObject> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        await RespectRateLimitAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{path}");
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(json) as JsonObject;
    }

    static async Task RespectRateLimitAsync(CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken);

        try
        {
            var now = DateTime.UtcNow;
            if (nextRequestAt > now)
                await Task.Delay(nextRequestAt - now, cancellationToken);

            // анонимный лимит Discogs — 25 запросов/мин, т.е. не чаще ~2.4s
            nextRequestAt = DateTime.UtcNow.AddMilliseconds(2500);
        }
        finally
        {
            requestGate.Release();
        }
    }

    static string BuildCacheKey(string artistName, string country)
        => $"{cacheVersion}|{NormalizeArtistName(artistName)}|{(country ?? string.Empty).Trim().ToLowerInvariant()}";
}
