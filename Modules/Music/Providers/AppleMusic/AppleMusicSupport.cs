using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Music.MusicPlaylistImportHelpers;

namespace Music;

// Импорт публичных плейлистов/альбомов Apple Music БЕЗ ключей и логина:
// анонимный developer-JWT из JS-бандла music.apple.com (живёт ~месяц)
// + amp-api catalog — тот же путь, что у веб-плеера. Стримов нет (DRM):
// треки приезжают метаданными, аудио резолвит обычный конвейер (YouTube-матчер).
public static class AppleMusicSupport
{
    public const string ProviderId = "applemusic";
    public const string PlaylistSourceType = "applemusic_playlist";
    public const string AlbumSourceType = "applemusic_album";

    const string ApiBaseUrl = "https://amp-api.music.apple.com";
    const int PageLimit = 100;
    const int MaxImportTracks = 2000;
    const int MaxPages = 30;
    const string BrowserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    // storefront (us/ru/ua/...), тип, id: pl.xxx / pl.u-xxx у плейлистов, цифры у альбомов
    static readonly Regex entityUrlRegex = new(@"^https?://music\.apple\.com/([a-z]{2})/(playlist|album)/(?:[^/]+/)?(pl\.[A-Za-z0-9\.\-]+|\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex assetBundleRegex = new("src=\"(/assets/index~[^\"]+\\.js)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex developerTokenRegex = new("\"(eyJ0[A-Za-z0-9_\\-]+\\.[A-Za-z0-9_\\-]+\\.[A-Za-z0-9_\\-]+)\"", RegexOptions.Compiled);
    static readonly SemaphoreSlim developerTokenLock = new(1, 1);
    static string developerToken;
    static DateTime developerTokenExpiresAt;

    static AppleMusicSupport()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public static bool CanHandleUrl(string url) => IsAppleMusicUrl(url);

    public static async Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(string inputUrl, CancellationToken cancellationToken = default)
    {
        var entity = ParseEntity(inputUrl);
        if (entity == null)
        {
            if (IsProfileUrl(inputUrl))
                return ImportUnavailable("Apple Music профиль не импортируется. Вставь ссылку на конкретный публичный плейлист или альбом.");

            return ImportUnavailable("Вставь ссылку на Apple Music плейлист или альбом.");
        }

        // прогрев кэша токена + дружелюбная ошибка, если Apple недоступен
        string token = await GetDeveloperTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return ImportUnavailable("Не удалось получить Apple Music токен.");

        try
        {
            return entity.Value.type == "album"
                ? await ImportAlbumAsync(entity.Value.storefront, entity.Value.id, cancellationToken)
                : await ImportPlaylistByIdAsync(entity.Value.storefront, entity.Value.id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] apple music import failed: {ex.Message}");
            return ImportUnavailable("Apple Music не ответил, попробуй ещё раз.");
        }
    }

    public static Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(MusicUserPlaylistSource source, CancellationToken cancellationToken = default)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return Task.FromResult(ImportUnavailable("У плейлиста нет Apple Music источника."));

        return ImportPlaylistAsync(source.url, cancellationToken);
    }

    static (string storefront, string type, string id)? ParseEntity(string value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = entityUrlRegex.Match(value);
        if (!match.Success)
            return null;

        string type = match.Groups[2].Value.ToLowerInvariant();
        string id = match.Groups[3].Value;

        // альбомная ссылка с плейлистным id (и наоборот) — мусор, не берём
        bool playlistId = id.StartsWith("pl.", StringComparison.OrdinalIgnoreCase);
        if (type == "playlist" != playlistId)
            return null;

        return (match.Groups[1].Value.ToLowerInvariant(), type, id);
    }

    static bool IsAppleMusicUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Host, "music.apple.com", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsProfileUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Host, "music.apple.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/profile/", StringComparison.OrdinalIgnoreCase);
    }

    // Анонимный developer-JWT лежит в бандле /assets/index~*.js главной страницы;
    // Apple выпускает его на ~месяц — кэшируем сутки и сбрасываем на 401.
    static async Task<string> GetDeveloperTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(developerToken) && developerTokenExpiresAt > DateTime.UtcNow)
            return developerToken;

        await developerTokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(developerToken) && developerTokenExpiresAt > DateTime.UtcNow)
                return developerToken;

            string html = await FetchTextAsync("https://music.apple.com/us/browse", "text/html,application/xhtml+xml", cancellationToken);
            var bundleMatch = assetBundleRegex.Match(html ?? string.Empty);
            if (!bundleMatch.Success)
                return null;

            string script = await FetchTextAsync("https://music.apple.com" + bundleMatch.Groups[1].Value, "application/javascript,*/*", cancellationToken);
            var tokenMatch = developerTokenRegex.Match(script ?? string.Empty);
            if (!tokenMatch.Success)
                return null;

            developerToken = tokenMatch.Groups[1].Value;
            developerTokenExpiresAt = DateTime.UtcNow.AddHours(24);
            return developerToken;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            developerTokenLock.Release();
        }
    }

    static void InvalidateDeveloperToken()
    {
        developerToken = null;
        developerTokenExpiresAt = DateTime.MinValue;
    }

    static async Task<string> FetchTextAsync(string url, string accept, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", accept);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    // сам берёт токен из кэша; на 401 сбрасывает его и повторяет запрос
    // один раз со свежим — протухший developer-токен не роняет импорт/sync
    static async Task<JsonElement?> ApiAsync(string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await GetDeveloperTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseUrl + path);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Origin", "https://music.apple.com");
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                InvalidateDeveloperToken();
                Console.WriteLine("[Music] apple music developer token expired");
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        return null;
    }

    static async Task<MusicUserPlaylistImportResult> ImportPlaylistByIdAsync(string storefront, string playlistId, CancellationToken cancellationToken)
    {
        // мета плейлиста ради имени; треки из relationships не берём —
        // единообразно листаем /tracks с нуля (одна лишняя страница, зато один путь)
        var meta = await ApiAsync($"/v1/catalog/{storefront}/playlists/{Uri.EscapeDataString(playlistId)}", cancellationToken);
        var playlist = FirstDataElement(meta);
        if (playlist == null)
            return ImportUnavailable("Apple Music плейлист не найден или приватный.");

        string title = GetString(GetProperty(playlist.Value, "attributes") ?? default, "name");

        var tracks = new List<MusicTrack>();
        for (int page = 0; page < MaxPages && tracks.Count < MaxImportTracks; page++)
        {
            var root = await ApiAsync($"/v1/catalog/{storefront}/playlists/{Uri.EscapeDataString(playlistId)}/tracks?limit={PageLimit}&offset={page * PageLimit}", cancellationToken);
            if (root == null)
            {
                // страница не отдалась — импорт атомарный, усечёнку не сохраняем
                return page == 0
                    ? ImportUnavailable("Apple Music плейлист не удалось прочитать.")
                    : ImportUnavailable("Apple Music не отдал плейлист целиком, попробуй ещё раз.");
            }

            int pageItems = MapTrackPage(root.Value, tracks, albumImagesFallback: null, albumTitleFallback: null, dateFallback: null);
            bool hasNext = GetProperty(root.Value, "next") != null;

            if (pageItems == 0 || !hasNext)
                break;
        }

        tracks = DeduplicateTracks(tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("В Apple Music плейлисте не найдено треков.");

        title = string.IsNullOrWhiteSpace(title) ? "Apple Music Playlist" : title.Trim();

        return new MusicUserPlaylistImportResult
        {
            available = true,
            title = title,
            track_count = tracks.Count,
            truncated = tracks.Count >= MaxImportTracks,
            tracks = tracks,
            source = new MusicUserPlaylistSource
            {
                type = PlaylistSourceType,
                url = $"https://music.apple.com/{storefront}/playlist/_/{playlistId}",
                playlist_id = playlistId,
                title = title
            }
        };
    }

    static async Task<MusicUserPlaylistImportResult> ImportAlbumAsync(string storefront, string albumId, CancellationToken cancellationToken)
    {
        var meta = await ApiAsync($"/v1/catalog/{storefront}/albums/{Uri.EscapeDataString(albumId)}", cancellationToken);
        var album = FirstDataElement(meta);
        if (album == null)
            return ImportUnavailable("Apple Music альбом не найден.");

        var attributes = GetProperty(album.Value, "attributes");
        string title = attributes != null ? GetString(attributes.Value, "name") : null;
        string artistName = attributes != null ? GetString(attributes.Value, "artistName") : null;
        string date = attributes != null ? GetString(attributes.Value, "releaseDate") : null;
        var albumImages = attributes != null ? MapArtwork(GetProperty(attributes.Value, "artwork")) : null;

        var tracks = new List<MusicTrack>();
        int fetched = 0;

        // первая порция треков приезжает в relationships, остальное — пагинацией;
        // offset считаем по сырому числу элементов (не по замапленным — часть фильтруется)
        var relationshipTracks = GetProperty(album.Value, "relationships", "tracks");
        bool hasNext = false;
        if (relationshipTracks != null)
        {
            fetched += MapTrackPage(relationshipTracks.Value, tracks, albumImages, title, date);
            hasNext = GetProperty(relationshipTracks.Value, "next") != null;
        }

        for (int page = 1; hasNext && page < MaxPages && tracks.Count < MaxImportTracks; page++)
        {
            var root = await ApiAsync($"/v1/catalog/{storefront}/albums/{Uri.EscapeDataString(albumId)}/tracks?limit={PageLimit}&offset={fetched}", cancellationToken);
            if (root == null)
                return ImportUnavailable("Apple Music не отдал альбом целиком, попробуй ещё раз.");

            int pageItems = MapTrackPage(root.Value, tracks, albumImages, title, date);
            fetched += pageItems;
            hasNext = GetProperty(root.Value, "next") != null && pageItems > 0;
        }

        tracks = DeduplicateTracks(tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("В Apple Music альбоме не найдено треков.");

        title = string.IsNullOrWhiteSpace(title) ? "Apple Music Album" : title.Trim();
        string playlistTitle = string.IsNullOrWhiteSpace(artistName) ? title : $"{artistName} — {title}";

        return new MusicUserPlaylistImportResult
        {
            available = true,
            title = playlistTitle,
            track_count = tracks.Count,
            tracks = tracks,
            source = new MusicUserPlaylistSource
            {
                type = AlbumSourceType,
                url = $"https://music.apple.com/{storefront}/album/_/{albumId}",
                playlist_id = albumId,
                title = playlistTitle
            }
        };
    }

    // маппит data[] страницы (плейлист-треки или альбом-треки); возвращает СЫРОЕ
    // число элементов страницы — по нему считается offset (часть элементов фильтруется)
    static int MapTrackPage(JsonElement root, List<MusicTrack> tracks, List<MusicImage> albumImagesFallback, string albumTitleFallback, string dateFallback)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return 0;

        int rawCount = 0;
        foreach (var item in data.EnumerateArray())
        {
            rawCount++;

            // в плейлистах бывают music-videos — берём только песни
            string kind = GetString(item, "type");
            if (!string.IsNullOrWhiteSpace(kind) && !string.Equals(kind, "songs", StringComparison.OrdinalIgnoreCase))
                continue;

            var mapped = MapTrackElement(item, albumImagesFallback, albumTitleFallback, dateFallback);
            if (mapped != null)
                tracks.Add(mapped);
        }

        return rawCount;
    }

    static MusicTrack MapTrackElement(JsonElement item, List<MusicImage> albumImagesFallback, string albumTitleFallback, string dateFallback)
    {
        var attributes = GetProperty(item, "attributes");
        if (attributes == null)
            return null;

        string title = GetString(attributes.Value, "name")?.Trim();
        string trackId = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(trackId))
            return null;

        string artistName = GetString(attributes.Value, "artistName")?.Trim();
        var images = MapArtwork(GetProperty(attributes.Value, "artwork"));
        if (images.Count == 0 && albumImagesFallback != null)
            images = albumImagesFallback.ToList();

        return new MusicTrack
        {
            id = $"applemusic:track:{trackId}",
            title = title,
            artist_name = string.IsNullOrWhiteSpace(artistName) ? "Apple Music" : artistName,
            artists = string.IsNullOrWhiteSpace(artistName) ? new List<string>() : new List<string> { artistName },
            album_title = GetString(attributes.Value, "albumName")?.Trim() ?? albumTitleFallback,
            duration_ms = GetInt(attributes.Value, "durationInMillis"),
            track_number = GetInt(attributes.Value, "trackNumber"),
            disc_number = GetInt(attributes.Value, "discNumber"),
            date = GetString(attributes.Value, "releaseDate") ?? dateFallback,
            images = images,
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = trackId }
            }
        };
    }

    // artwork.url — шаблон с {w}x{h}; просим два размера, крупный первым
    static List<MusicImage> MapArtwork(JsonElement? artwork)
    {
        var result = new List<MusicImage>();
        string template = artwork != null ? GetString(artwork.Value, "url") : null;
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{w}", StringComparison.OrdinalIgnoreCase))
            return result;

        foreach (int size in new[] { 600, 300 })
        {
            result.Add(new MusicImage
            {
                url = template.Replace("{w}", size.ToString()).Replace("{h}", size.ToString()),
                width = size,
                height = size
            });
        }

        return result;
    }

    static JsonElement? FirstDataElement(JsonElement? root)
    {
        if (root == null || !root.Value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            return null;

        return data[0];
    }

}
