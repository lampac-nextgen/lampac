using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Music;

// Транспорт и общая инфраструктура SoundCloud: client_id (конфиг или скрап с кэшем),
// построение URL/заголовков, resolve, постраничные коллекции api-v2, JSON-хелперы.
// Остальные части класса: .Catalog.cs (discovery), .Audio.cs (матчинг/стримы),
// .Import.cs (импорт плейлистов/лайков), .Auth.cs (OAuth credentials).
public static partial class SoundCloudSupport
{
    public const string AuthProviderId = "soundcloud";
    public const string AudioProviderId = "soundcloudaudio";
    public const string DiscoveryProviderId = "soundcloudcharts";
    public const string ApiBaseUrl = "https://api.soundcloud.com";
    public const string ApiV2BaseUrl = "https://api-v2.soundcloud.com";
    public const string OAuthTokenUrl = "https://secure.soundcloud.com/oauth/token";
    public const string ChartsSectionId = "browse:soundcloud";
    public const string SearchSectionId = "search:soundcloud";
    public const string SearchTracksSectionId = "search:soundcloud:tracks";
    public const string SearchAlbumsSectionId = "search:soundcloud:albums";
    public const string SearchPlaylistsSectionId = "search:soundcloud:playlists";
    public const string SearchArtistsSectionId = "search:soundcloud:artists";
    public const string PlaylistIdPrefix = "soundcloud:playlist:";
    public const string UserIdPrefix = "soundcloud:user:";
    public const string UserTracksAlbumPrefix = "soundcloud:usertracks:";
    public const string UserSectionPrefix = "soundcloud:usersection:";

    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    static readonly Regex apiClientIdRegex = new("\"apiClient\".*?\"id\":\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex publicClientIdRegex = new("(?:client_id|clientId)\\s*[:=]\\s*[\"']?([A-Za-z0-9_-]{20,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex publicAssetRegex = new("https://a-v2\\.sndcdn\\.com/assets/[^\"'<>\\s]+\\.js", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex titleArtistSeparatorRegex = new(@"^\s*(?<artist>.+?)\s+(?:-|–|—|:)\s+(?<title>.+?)\s*$", RegexOptions.Compiled);
    static readonly TimeSpan chartsCacheTtl = TimeSpan.FromHours(1);
    static readonly TimeSpan playlistCacheTtl = TimeSpan.FromMinutes(30);
    static readonly TimeSpan trackCacheTtl = TimeSpan.FromMinutes(30);
    static readonly TimeSpan preferredSourceCacheTtl = TimeSpan.FromMinutes(20);
    static readonly TimeSpan publicClientIdCacheTtl = TimeSpan.FromHours(6);
    static readonly SemaphoreSlim publicClientIdLock = new(1, 1);
    static string publicClientId;
    static DateTime publicClientIdExpiresAt;
    const string chartsCacheVersion = "v8";

    static SoundCloudSupport()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public static HttpClient HttpClient => httpClient;

    public static bool IsModuleEnabled => ModInit.conf?.soundcloud_enabled == true;
    public static bool IsDiscoveryEnabled => IsModuleEnabled && ModInit.conf?.soundcloud_discovery_enabled == true;
    public static bool IsAudioEnabled => IsModuleEnabled && ModInit.conf?.soundcloud_audio_enabled == true;
    public static bool IsAuthEnabled => IsModuleEnabled && ModInit.conf?.soundcloud_auth_enabled == true && HasOAuthConfig();

    public static bool HasClientId() => !string.IsNullOrWhiteSpace(ModInit.conf?.soundcloud_client_id);
    public static bool HasClientCredentials() => HasClientId() && !string.IsNullOrWhiteSpace(ModInit.conf?.soundcloud_client_secret);
    public static bool HasOAuthConfig() => HasClientCredentials() && !string.IsNullOrWhiteSpace(ModInit.conf?.soundcloud_redirect_uri);

    public static string Country => string.IsNullOrWhiteSpace(ModInit.conf?.soundcloud_country) ? "US" : ModInit.conf.soundcloud_country.Trim().ToUpperInvariant();

    public static string BuildApiUrl(string path, IDictionary<string, string> query = null)
    {
        var baseUrl = $"{ApiBaseUrl.TrimEnd('/')}/{path?.TrimStart('/')}";
        if (query == null || query.Count == 0)
            return baseUrl;

        var items = query
            .Where(i => !string.IsNullOrWhiteSpace(i.Key) && !string.IsNullOrWhiteSpace(i.Value))
            .Select(i => $"{Uri.EscapeDataString(i.Key)}={Uri.EscapeDataString(i.Value)}")
            .ToList();

        return items.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join("&", items)}";
    }

    public static string BuildApiV2Url(string path, IDictionary<string, string> query = null)
    {
        var baseUrl = $"{ApiV2BaseUrl.TrimEnd('/')}/{path?.TrimStart('/')}";
        if (query == null || query.Count == 0)
            return baseUrl;

        var items = query
            .Where(i => !string.IsNullOrWhiteSpace(i.Key) && !string.IsNullOrWhiteSpace(i.Value))
            .Select(i => $"{Uri.EscapeDataString(i.Key)}={Uri.EscapeDataString(i.Value)}")
            .ToList();

        return items.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join("&", items)}";
    }

    public static string BuildChartsUrl(bool trending, string genre = "all-music")
    {
        string route = trending ? "new" : "";
        string url = $"https://soundcloud.com/charts/{route}".TrimEnd('/');
        return $"{url}?country={Uri.EscapeDataString(Country)}&genre={Uri.EscapeDataString(string.IsNullOrWhiteSpace(genre) ? "all-music" : genre)}";
    }

    public static Dictionary<string, string> CreatePublicHeaders(string accessToken = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["Accept"] = "application/json; charset=utf-8",
            ["User-Agent"] = "LampacNextgenMusic/0.1 (https://github.com/lampac-nextgen/lampac)"
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
            headers["Authorization"] = $"OAuth {accessToken.Trim()}";

        return headers;
    }

    public static async Task<string> GetClientIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(ModInit.conf?.soundcloud_client_id))
            return ModInit.conf.soundcloud_client_id.Trim();

        if (!string.IsNullOrWhiteSpace(publicClientId) && publicClientIdExpiresAt > DateTime.UtcNow)
            return publicClientId;

        await publicClientIdLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(publicClientId) && publicClientIdExpiresAt > DateTime.UtcNow)
                return publicClientId;

            string clientId = await FetchPublicClientIdAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                publicClientId = clientId;
                publicClientIdExpiresAt = DateTime.UtcNow.Add(publicClientIdCacheTtl);
            }

            return clientId;
        }
        finally
        {
            publicClientIdLock.Release();
        }
    }

    static async Task<string> FetchPublicClientIdAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://soundcloud.com/");
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        string html = await response.Content.ReadAsStringAsync(cancellationToken);
        string clientId = ExtractPublicClientId(html);
        if (!string.IsNullOrWhiteSpace(clientId))
            return clientId;

        foreach (Match asset in publicAssetRegex.Matches(html ?? string.Empty).Take(12))
        {
            try
            {
                using var assetRequest = new HttpRequestMessage(HttpMethod.Get, asset.Value);
                assetRequest.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
                assetRequest.Headers.TryAddWithoutValidation("Accept", "application/javascript,*/*");

                using var assetResponse = await httpClient.SendAsync(assetRequest, cancellationToken);
                if (!assetResponse.IsSuccessStatusCode)
                    continue;

                string script = await assetResponse.Content.ReadAsStringAsync(cancellationToken);
                clientId = ExtractPublicClientId(script);
                if (!string.IsNullOrWhiteSpace(clientId))
                    return clientId;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // SoundCloud rotates asset bundles often; try the next one.
            }
        }

        return null;
    }

    static string ExtractPublicClientId(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var legacy = apiClientIdRegex.Match(content);
        if (legacy.Success)
            return legacy.Groups[1].Value.Trim();

        var current = publicClientIdRegex.Match(content);
        return current.Success ? current.Groups[1].Value.Trim() : null;
    }

    static void InvalidatePublicClientIdOnUnauthorized(HttpStatusCode statusCode, string clientId)
    {
        if (statusCode != HttpStatusCode.Unauthorized || string.IsNullOrWhiteSpace(clientId))
            return;

        if (!string.Equals(publicClientId, clientId, StringComparison.Ordinal))
            return;

        publicClientId = null;
        publicClientIdExpiresAt = DateTime.MinValue;
    }

    static async Task<JsonElement?> ResolveUrlElementAsync(string entityUrl, string clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityUrl) || string.IsNullOrWhiteSpace(clientId))
            return null;

        string url = BuildApiV2Url("resolve", new Dictionary<string, string>
        {
            ["url"] = entityUrl,
            ["client_id"] = clientId
        });

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
        return document.RootElement.Clone();
    }

    static async Task<JsonElement?> LoadPlaylistByIdAsync(string playlistId, string clientId, CancellationToken cancellationToken)
    {
        string url = BuildApiV2Url($"playlists/{playlistId}", new Dictionary<string, string>
        {
            ["representation"] = "full",
            ["client_id"] = clientId
        });

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
        return document.RootElement.Clone();
    }

    static async Task<SoundCloudTrackDetails> LoadTrackDetailsAsync(List<string> ids, string clientId, CancellationToken cancellationToken)
    {
        var result = new SoundCloudTrackDetails();
        if (ids == null || ids.Count == 0 || string.IsNullOrWhiteSpace(clientId))
            return result;

        foreach (var chunk in ids.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(50))
        {
            string url = BuildApiV2Url("tracks", new Dictionary<string, string>
            {
                ["ids"] = string.Join(",", chunk),
                ["client_id"] = clientId
            });

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var header in CreatePublicHeaders())
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                InvalidatePublicClientIdOnUnauthorized(response.StatusCode, clientId);
                result.complete = false;
                continue;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                result.complete = false;
                continue;
            }

            foreach (var track in document.RootElement.EnumerateArray())
            {
                string id = GetString(track, "id");
                if (!string.IsNullOrWhiteSpace(id))
                    result.tracks[id] = track.Clone();
            }
        }

        return result;
    }

    static async Task<JsonElement?> LoadTrackElementAsync(string trackId, string clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(clientId))
            return null;

        string url = BuildApiV2Url($"tracks/{trackId}", new Dictionary<string, string>
        {
            ["client_id"] = clientId
        });

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
        return document.RootElement.Clone();
    }

    sealed class SoundCloudCollectionPage
    {
        public List<JsonElement> items { get; set; } = new();
        public bool complete { get; set; } = true;
        public bool hasMore { get; set; }
        public string nextPage { get; set; }
        public bool truncated { get; set; }

        public static SoundCloudCollectionPage Empty() => new();
    }

    sealed class SoundCloudTrackDetails
    {
        public Dictionary<string, JsonElement> tracks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool complete { get; set; } = true;
    }

    static async Task<SoundCloudCollectionPage> SafeLoadCollectionElementsAsync(
        string path,
        string clientId,
        int limit,
        CancellationToken cancellationToken,
        IDictionary<string, string> query = null,
        string pageUrl = null)
    {
        try
        {
            return await LoadCollectionElementsAsync(path, clientId, limit, cancellationToken, query, pageUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new SoundCloudCollectionPage { complete = false };
        }
    }

    static async Task<SoundCloudCollectionPage> LoadCollectionElementsAsync(
        string path,
        string clientId,
        int limit,
        CancellationToken cancellationToken,
        IDictionary<string, string> query = null,
        string pageUrl = null)
    {
        var results = new List<JsonElement>();
        var result = new SoundCloudCollectionPage { items = results };
        if ((string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(pageUrl)) || string.IsNullOrWhiteSpace(clientId))
            return result;

        int target = Math.Max(1, limit);
        var queryParams = query == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(query, StringComparer.OrdinalIgnoreCase);

        string url;
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            url = EnsureSoundCloudCollectionUrl(pageUrl, clientId);
        }
        else
        {
            queryParams["client_id"] = clientId;
            queryParams["limit"] = target.ToString();
            queryParams["linked_partitioning"] = "1";

            url = BuildApiV2Url(path, queryParams);
        }

        for (int page = 0; page < 8 && !string.IsNullOrWhiteSpace(url) && results.Count < target; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var header in CreatePublicHeaders())
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                InvalidatePublicClientIdOnUnauthorized(response.StatusCode, clientId);
                result.complete = false;
                result.hasMore = true;
                result.nextPage = CleanSoundCloudPageUrl(url);
                return result;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (results.Count >= target)
                        break;

                    results.Add(item.Clone());
                }

                result.hasMore = false;
                result.nextPage = null;
                break;
            }

            if (!document.RootElement.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array)
            {
                result.complete = false;
                break;
            }

            int beforeCount = results.Count;
            foreach (var item in collection.EnumerateArray())
            {
                if (results.Count >= target)
                    break;

                results.Add(item.Clone());
            }

            string nextUrl = GetString(document.RootElement, "next_href");
            result.hasMore = !string.IsNullOrWhiteSpace(nextUrl) && results.Count > beforeCount;
            result.nextPage = result.hasMore ? CleanSoundCloudPageUrl(nextUrl) : null;
            result.truncated = result.hasMore && results.Count >= target;
            url = result.hasMore && results.Count < target
                ? EnsureSoundCloudCollectionUrl(nextUrl, clientId)
                : null;
        }

        if (!string.IsNullOrWhiteSpace(url) && results.Count < target)
        {
            result.complete = false;
            result.hasMore = true;
            result.nextPage = CleanSoundCloudPageUrl(url);
        }

        if (result.hasMore && results.Count >= target)
            result.truncated = true;

        return result;
    }

    static async Task<List<JsonElement>> LoadUserTrackElementsAsync(string userId, string clientId, int limit, CancellationToken cancellationToken)
    {
        var page = await LoadCollectionElementsAsync($"users/{userId}/tracks", clientId, limit, cancellationToken);
        return page.items;
    }

    static string EnsureSoundCloudCollectionUrl(string url, string clientId)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (!url.StartsWith(ApiV2BaseUrl, StringComparison.OrdinalIgnoreCase))
            return null;

        url = AddQueryParameterIfMissing(url, "client_id", clientId);
        return AddQueryParameterIfMissing(url, "linked_partitioning", "1");
    }

    static string CleanSoundCloudPageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!url.StartsWith(ApiV2BaseUrl, StringComparison.OrdinalIgnoreCase))
            return null;

        int queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
            return url;

        string path = url.Substring(0, queryIndex);
        var query = url.Substring(queryIndex + 1)
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("client_id=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return query.Count == 0 ? path : $"{path}?{string.Join("&", query)}";
    }

    static string AddQueryParameterIfMissing(string url, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return url;

        if (Regex.IsMatch(url, $@"[?&]{Regex.Escape(key)}=", RegexOptions.IgnoreCase))
            return url;

        char separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    static async Task<List<JsonElement>> SearchTrackElementsAsync(string query, string clientId, int limit, CancellationToken cancellationToken)
    {
        return await SearchElementsAsync("search/tracks", query, clientId, limit, cancellationToken);
    }

    static async Task<List<JsonElement>> SearchElementsAsync(string path, string query, string clientId, int limit, CancellationToken cancellationToken)
    {
        var results = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(clientId))
            return results;

        try
        {
            string url = BuildApiV2Url(path, new Dictionary<string, string>
            {
                ["q"] = query.Trim(),
                ["client_id"] = clientId,
                ["limit"] = Math.Max(1, limit).ToString(),
                ["linked_partitioning"] = "1"
            });

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
            if (!document.RootElement.TryGetProperty("collection", out var collection) || collection.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var item in collection.EnumerateArray())
                results.Add(item.Clone());

            return results;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    static (string artist, string title) SplitArtistTitleFromSoundCloudTitle(string value)
    {
        string normalized = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return (null, null);

        var match = titleArtistSeparatorRegex.Match(normalized);
        if (!match.Success)
            return (null, normalized);

        string artist = NormalizeValue(match.Groups["artist"].Value);
        string title = NormalizeValue(match.Groups["title"].Value);
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return (null, normalized);

        return (artist, title);
    }

    static string ParseTrackApiId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var match = Regex.Match(id.Trim(), @"soundcloud:tracks?:(\d+)$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var intValue))
                return intValue;

            if (value.TryGetInt64(out var longValue) && longValue is <= int.MaxValue and >= int.MinValue)
                return (int)longValue;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    static int GetArrayLength(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return 0;

        return value.GetArrayLength();
    }

    static string NormalizeValue(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
