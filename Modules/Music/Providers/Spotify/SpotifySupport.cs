using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Music.MusicPlaylistImportHelpers;

namespace Music;

// Импорт публичных плейлистов/альбомов Spotify БЕЗ ключей и логина:
// гостевой accessToken со страницы embed-плеера + pathfinder GraphQL
// (persisted queries) — тот же путь, что у веб-плеера Spotify.
// Стримов Spotify не отдаёт (DRM): треки приезжают чистыми метаданными,
// аудио резолвится обычным конвейером (YouTube-матчер), как у VK-чарта.
public static class SpotifySupport
{
    public const string ProviderId = "spotify";
    public const string PlaylistSourceType = "spotify_playlist";
    public const string AlbumSourceType = "spotify_album";

    const string PathfinderUrl = "https://api-partner.spotify.com/pathfinder/v1/query";

    // persisted-query хэши веб-плеера; Spotify их периодически ротирует.
    // Симптом ротации: ошибка PersistedQueryNotFound в ответе pathfinder —
    // тогда взять свежие из бандла веб-плеера (или SpotifyScraper/api/pathfinder.py).
    const string FetchPlaylistHash = "a65e12194ed5fc443a1cdebed5fabe33ca5b07b987185d63c72483867ad13cb4";
    const string GetAlbumHash = "b9bfabef66ed756e5e13f68a942deb60bd4125ec1f1be8cc42769dc0259b4b10";

    const int PlaylistPageLimit = 100;
    const int AlbumPageLimit = 50;
    const int MaxImportTracks = 2000;
    const string BrowserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    static readonly Regex entityUrlRegex = new(@"^https?://open\.spotify\.com/(?:intl-[a-z\-]+/)?(playlist|album)/([A-Za-z0-9]{22})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex entityUriRegex = new(@"^spotify:(playlist|album):([A-Za-z0-9]{22})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex accessTokenRegex = new("\"accessToken\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    static readonly Regex accessTokenExpiresRegex = new("\"accessTokenExpirationTimestampMs\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
    static readonly SemaphoreSlim anonymousTokenLock = new(1, 1);
    static string anonymousToken;
    static DateTime anonymousTokenExpiresAt;

    static SpotifySupport()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public static bool CanHandleUrl(string url) => ParseEntity(url) != null;

    public static async Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(string inputUrl, CancellationToken cancellationToken = default)
    {
        var entity = ParseEntity(inputUrl);
        if (entity == null)
            return ImportUnavailable("Вставь ссылку на Spotify плейлист или альбом.");

        // прогрев кэша токена + дружелюбная ошибка, если Spotify недоступен
        string token = await GetAnonymousTokenAsync(entity.Value.type, entity.Value.id, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return ImportUnavailable("Не удалось получить Spotify токен.");

        try
        {
            return entity.Value.type == "album"
                ? await ImportAlbumAsync(entity.Value.id, cancellationToken)
                : await ImportPlaylistByIdAsync(entity.Value.id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] spotify import failed: {ex.Message}");
            return ImportUnavailable("Spotify не ответил, попробуй ещё раз.");
        }
    }

    public static Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(MusicUserPlaylistSource source, CancellationToken cancellationToken = default)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return Task.FromResult(ImportUnavailable("У плейлиста нет Spotify источника."));

        return ImportPlaylistAsync(source.url, cancellationToken);
    }

    static (string type, string id)? ParseEntity(string value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = entityUriRegex.Match(value);
        if (!match.Success)
            match = entityUrlRegex.Match(value);

        if (!match.Success)
            return null;

        return (match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value);
    }

    // Гостевой токен живёт на любой публичной embed-странице (~1 час);
    // логина/ключей не требует — это тот же токен, что получает браузер инкогнито.
    static async Task<string> GetAnonymousTokenAsync(string entityType, string entityId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(anonymousToken) && anonymousTokenExpiresAt > DateTime.UtcNow)
            return anonymousToken;

        await anonymousTokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(anonymousToken) && anonymousTokenExpiresAt > DateTime.UtcNow)
                return anonymousToken;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://open.spotify.com/embed/{entityType}/{entityId}");
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenMatch = accessTokenRegex.Match(html ?? string.Empty);
            if (!tokenMatch.Success)
                return null;

            var expiresMatch = accessTokenExpiresRegex.Match(html);
            DateTime expiresAt = expiresMatch.Success && long.TryParse(expiresMatch.Groups[1].Value, out long expiresMs)
                ? DateTimeOffset.FromUnixTimeMilliseconds(expiresMs).UtcDateTime.AddMinutes(-2)
                : DateTime.UtcNow.AddMinutes(30);

            anonymousToken = tokenMatch.Groups[1].Value;
            anonymousTokenExpiresAt = expiresAt;
            return anonymousToken;
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
            anonymousTokenLock.Release();
        }
    }

    static void InvalidateAnonymousToken()
    {
        anonymousToken = null;
        anonymousTokenExpiresAt = DateTime.MinValue;
    }

    // сам берёт токен из кэша; на 401 сбрасывает его и повторяет запрос
    // один раз со свежим — протухший гостевой токен не роняет импорт/sync
    static async Task<JsonElement?> QueryAsync(string operationName, string hash, object variables, string entityType, string entityId, CancellationToken cancellationToken)
    {
        string url = PathfinderUrl
            + "?operationName=" + Uri.EscapeDataString(operationName)
            + "&variables=" + Uri.EscapeDataString(MusicJson.Serialize(variables))
            + "&extensions=" + Uri.EscapeDataString($"{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{hash}\"}}}}");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await GetAnonymousTokenAsync(entityType, entityId, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("app-platform", "WebPlayer");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                InvalidateAnonymousToken();
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement.Clone();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                string firstError = errors.EnumerateArray().Select(e => GetString(e, "message")).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
                if (firstError?.Contains("PersistedQueryNotFound", StringComparison.OrdinalIgnoreCase) == true)
                    Console.WriteLine("[Music] spotify pathfinder hash rotated — module update required");
                else
                    Console.WriteLine($"[Music] spotify pathfinder error: {firstError}");
                return null;
            }

            return root;
        }

        return null;
    }

    static async Task<MusicUserPlaylistImportResult> ImportPlaylistByIdAsync(string playlistId, CancellationToken cancellationToken)
    {
        var tracks = new List<MusicTrack>();
        string title = null;
        int totalCount = -1;

        for (int offset = 0; totalCount < 0 || (offset < totalCount && tracks.Count < MaxImportTracks); offset += PlaylistPageLimit)
        {
            var root = await QueryAsync("fetchPlaylist", FetchPlaylistHash, new
            {
                uri = $"spotify:playlist:{playlistId}",
                offset,
                limit = PlaylistPageLimit,
                enableWatchFeedEntrypoint = false
            }, "playlist", playlistId, cancellationToken);

            var playlist = GetProperty(root, "data", "playlistV2");
            if (playlist == null || playlist.Value.ValueKind != JsonValueKind.Object)
            {
                // страница не отдалась — импорт атомарный, усечённый список не сохраняем
                return offset == 0
                    ? ImportUnavailable("Spotify плейлист не найден или приватный.")
                    : ImportUnavailable("Spotify не отдал плейлист целиком, попробуй ещё раз.");
            }

            title ??= GetString(playlist.Value, "name");

            var content = GetProperty(playlist.Value, "content");
            if (content == null)
                return ImportUnavailable("Spotify плейлист не удалось прочитать.");

            if (totalCount < 0)
                totalCount = GetInt(content.Value, "totalCount") ?? 0;

            if (!content.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return ImportUnavailable("Spotify не отдал плейлист целиком, попробуй ещё раз.");

            if (items.GetArrayLength() == 0 && offset < totalCount)
                return ImportUnavailable("Spotify не отдал плейлист целиком, попробуй ещё раз.");

            foreach (var item in items.EnumerateArray())
            {
                var mapped = MapPlaylistItem(item);
                if (mapped != null)
                    tracks.Add(mapped);
            }

            if (totalCount <= 0)
                break;
        }

        tracks = DeduplicateTracks(tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("В Spotify плейлисте не найдено треков.");

        title = string.IsNullOrWhiteSpace(title) ? "Spotify Playlist" : title.Trim();

        return new MusicUserPlaylistImportResult
        {
            available = true,
            title = title,
            track_count = tracks.Count,
            truncated = totalCount > tracks.Count && tracks.Count >= MaxImportTracks,
            tracks = tracks,
            source = new MusicUserPlaylistSource
            {
                type = PlaylistSourceType,
                url = $"https://open.spotify.com/playlist/{playlistId}",
                playlist_id = playlistId,
                title = title
            }
        };
    }

    static async Task<MusicUserPlaylistImportResult> ImportAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        var tracks = new List<MusicTrack>();
        string title = null, artistName = null, date = null;
        List<MusicImage> albumImages = null;
        int totalCount = -1;

        for (int offset = 0; totalCount < 0 || (offset < totalCount && tracks.Count < MaxImportTracks); offset += AlbumPageLimit)
        {
            var root = await QueryAsync("getAlbum", GetAlbumHash, new
            {
                uri = $"spotify:album:{albumId}",
                locale = "",
                offset,
                limit = AlbumPageLimit
            }, "album", albumId, cancellationToken);

            var album = GetProperty(root, "data", "albumUnion");
            if (album == null || album.Value.ValueKind != JsonValueKind.Object)
            {
                return offset == 0
                    ? ImportUnavailable("Spotify альбом не найден.")
                    : ImportUnavailable("Spotify не отдал альбом целиком, попробуй ещё раз.");
            }

            if (title == null)
            {
                title = GetString(album.Value, "name");
                artistName = ExtractArtistNames(GetProperty(album.Value, "artists")).FirstOrDefault();
                date = GetString(GetProperty(album.Value, "date") ?? default, "isoString");
                albumImages = MapCoverArt(GetProperty(album.Value, "coverArt"));
            }

            var tracksV2 = GetProperty(album.Value, "tracksV2");
            if (tracksV2 == null)
                return ImportUnavailable("Spotify альбом не удалось прочитать.");

            if (totalCount < 0)
                totalCount = GetInt(tracksV2.Value, "totalCount") ?? 0;

            if (!tracksV2.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return ImportUnavailable("Spotify не отдал альбом целиком, попробуй ещё раз.");

            if (items.GetArrayLength() == 0 && offset < totalCount)
                return ImportUnavailable("Spotify не отдал альбом целиком, попробуй ещё раз.");

            foreach (var item in items.EnumerateArray())
            {
                var track = GetProperty(item, "track");
                if (track == null)
                    continue;

                var mapped = MapTrackElement(track.Value, title, albumImages, date, durationProperty: "duration");
                if (mapped != null)
                    tracks.Add(mapped);
            }

            if (totalCount <= 0)
                break;
        }

        tracks = DeduplicateTracks(tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("В Spotify альбоме не найдено треков.");

        title = string.IsNullOrWhiteSpace(title) ? "Spotify Album" : title.Trim();
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
                url = $"https://open.spotify.com/album/{albumId}",
                playlist_id = albumId,
                title = playlistTitle
            }
        };
    }

    static MusicTrack MapPlaylistItem(JsonElement item)
    {
        var data = GetProperty(item, "itemV2", "data");
        if (data == null || !string.Equals(GetString(data.Value, "__typename"), "Track", StringComparison.OrdinalIgnoreCase))
            return null;

        var album = GetProperty(data.Value, "albumOfTrack");
        string albumTitle = album != null ? GetString(album.Value, "name") : null;
        var images = album != null ? MapCoverArt(GetProperty(album.Value, "coverArt")) : null;

        return MapTrackElement(data.Value, albumTitle, images, date: null, durationProperty: "trackDuration");
    }

    static MusicTrack MapTrackElement(JsonElement track, string albumTitle, List<MusicImage> images, string date, string durationProperty)
    {
        string title = GetString(track, "name")?.Trim();
        string uri = GetString(track, "uri");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(uri))
            return null;

        var artists = ExtractArtistNames(GetProperty(track, "artists"));
        var duration = GetProperty(track, durationProperty);

        return new MusicTrack
        {
            id = uri,
            title = title,
            artist_name = artists.Count > 0 ? string.Join(", ", artists) : "Spotify",
            artists = artists,
            album_title = string.IsNullOrWhiteSpace(albumTitle) ? null : albumTitle.Trim(),
            duration_ms = duration != null ? GetInt(duration.Value, "totalMilliseconds") : null,
            track_number = GetInt(track, "trackNumber"),
            disc_number = GetInt(track, "discNumber"),
            date = date,
            images = images?.ToList() ?? new List<MusicImage>(),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = uri }
            }
        };
    }

    static List<string> ExtractArtistNames(JsonElement? artists)
    {
        var result = new List<string>();
        if (artists == null || !artists.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in items.EnumerateArray())
        {
            string name = GetString(GetProperty(item, "profile") ?? default, "name")?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
                result.Add(name);
        }

        return result;
    }

    static List<MusicImage> MapCoverArt(JsonElement? coverArt)
    {
        var result = new List<MusicImage>();
        if (coverArt == null || !coverArt.Value.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var source in sources.EnumerateArray())
        {
            string url = GetString(source, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;

            result.Add(new MusicImage
            {
                url = url,
                width = GetInt(source, "width"),
                height = GetInt(source, "height")
            });
        }

        // крупные первыми — как отдаёт SoundCloud-маппер
        return result.OrderByDescending(i => i.width ?? 0).ToList();
    }

}
