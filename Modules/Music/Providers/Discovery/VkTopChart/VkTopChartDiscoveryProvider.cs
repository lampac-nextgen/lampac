using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Music;

public class VkTopChartDiscoveryProvider : IMusicDiscoveryProvider
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    static readonly TimeSpan cacheTtl = TimeSpan.FromHours(1);

    const string providerId = "vktop200";
    const string baseUrl = "https://www.top200chart.ru/";
    const string userAgent = "LampacNextgenMusic/0.1 (https://github.com/lampac-nextgen/lampac)";

    public string Id => providerId;
    public string Name => "VK Top 200";
    public bool Enabled => true;

    static VkTopChartDiscoveryProvider()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<List<MusicBrowseSection>> GetHomeSectionsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var feed = await GetTracksAsync(cancellationToken);
        if (feed.Count == 0)
            return new List<MusicBrowseSection>();

        return new List<MusicBrowseSection>
        {
            new()
            {
                id = "browse:vk_top_tracks",
                title = "Топ треки VK",
                type = "track",
                source_provider = Id,
                has_more = feed.Count > limit,
                tracks = feed.Take(limit).ToList()
            }
        };
    }

    public async Task<MusicBrowseSection> GetSectionAsync(string sectionId, int limit, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(sectionId, "browse:vk_top_tracks", StringComparison.OrdinalIgnoreCase))
            return null;

        var feed = await GetTracksAsync(cancellationToken);
        if (feed.Count == 0)
            return null;

        return new MusicBrowseSection
        {
            id = sectionId,
            title = "Топ треки VK",
            type = "track",
            source_provider = Id,
            has_more = false,
            tracks = feed.Take(Math.Max(limit, 1)).ToList()
        };
    }

    async Task<List<MusicTrack>> GetTracksAsync(CancellationToken cancellationToken)
    {
        return await MusicMetadataCacheService.GetOrCreateAsync(
            providerId,
            "browse",
            "top200:tracks:v2",
            cacheTtl,
            () => LoadTracksAsync(cancellationToken),
            cancellationToken
        ) ?? new List<MusicTrack>();
    }

    async Task<List<MusicTrack>> LoadTracksAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new List<MusicTrack>();

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = ExtractTracksJsonArray(html);
        if (json == null)
            return new List<MusicTrack>();

        return json
            .OfType<JsonObject>()
            .Select(ParseTrack)
            .Where(i => i != null)
            .ToList();
    }

    static JsonArray ExtractTracksJsonArray(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        const string marker = "\\\"tracks\\\":[";
        int markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;

        int start = html.IndexOf('[', markerIndex);
        if (start < 0)
            return null;

        int depth = 0;

        for (int i = start; i < html.Length; i++)
        {
            char c = html[i];

            if (c == '[')
            {
                depth++;
                continue;
            }

            if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    string arrayJson = html.Substring(start, i - start + 1)
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");
                    return JsonNode.Parse(arrayJson) as JsonArray;
                }
            }
        }

        return null;
    }

    static MusicTrack ParseTrack(JsonObject item)
    {
        if (item == null)
            return null;

        string title = item["title"]?.GetValue<string>()?.Trim();
        string artist = item["artist"]?.GetValue<string>()?.Trim();
        string sourceId = item["id"]?.GetValue<int?>()?.ToString();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        string trackId = string.IsNullOrWhiteSpace(sourceId)
            ? $"vkchart:{StableId($"{artist}::{title}")}"
            : $"vkchart:{sourceId}";

        string cover = item["cover_url"]?.GetValue<string>();
        int? position = item["position"]?.GetValue<int?>();

        return new MusicTrack
        {
            id = trackId,
            title = title,
            artist_name = artist,
            album_title = "VK Top 200",
            track_number = position,
            images = string.IsNullOrWhiteSpace(cover)
                ? new List<MusicImage>()
                : new List<MusicImage> { new() { url = cover, width = 640, height = 640 } },
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = providerId, external_id = sourceId ?? trackId }
            }
        };
    }

    static string StableId(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
