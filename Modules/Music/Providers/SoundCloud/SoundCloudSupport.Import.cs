using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Music.MusicPlaylistImportHelpers;

namespace Music;

// Импорт пользовательских плейлистов и лайков по ссылке
// (профиль → лайки, плейлист → треки) для MusicUserPlaylistService.
public static partial class SoundCloudSupport
{
    static async Task<MusicUserPlaylistImportResult> ImportResolvedPlaylistAsync(JsonElement playlist, string clientId, CancellationToken cancellationToken)
    {
        int? trackCount = GetInt(playlist, "track_count");
        int loadedTracks = GetArrayLength(playlist, "tracks");
        string playlistId = GetString(playlist, "id");
        bool knownIncompleteTrackList = trackCount.HasValue && loadedTracks < trackCount.Value;

        if (!string.IsNullOrWhiteSpace(playlistId) && (!trackCount.HasValue || loadedTracks < trackCount.Value))
        {
            var hydrated = await LoadPlaylistByIdAsync(playlistId, clientId, cancellationToken);
            if (hydrated.HasValue)
                playlist = hydrated.Value;
            else if (knownIncompleteTrackList)
                return ImportUnavailable("SoundCloud плейлист не удалось полностью загрузить. Старый плейлист не обновлён, попробуй позже.");
        }

        var hydrationIds = CollectTrackIdsForHydration(playlist);
        var trackDetails = await LoadTrackDetailsAsync(hydrationIds, clientId, cancellationToken);
        if (hydrationIds.Count > 0 && !trackDetails.complete)
            return ImportUnavailable("SoundCloud плейлист не удалось полностью загрузить. Старый плейлист не обновлён, попробуй позже.");

        var album = MapResolvedPlaylist(playlist, trackDetails.tracks);
        if (album == null)
            return ImportUnavailable("SoundCloud плейлист не удалось прочитать.");

        var tracks = DeduplicateTracks(album.tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("В SoundCloud плейлисте не найдено треков.");

        string sourceUrl = album.provider_refs?.FirstOrDefault(i => i?.provider == DiscoveryProviderId)?.external_id
            ?? GetString(playlist, "permalink_url");

        return new MusicUserPlaylistImportResult
        {
            available = true,
            title = string.IsNullOrWhiteSpace(album.title) ? "SoundCloud Playlist" : album.title,
            track_count = tracks.Count,
            tracks = tracks,
            source = new MusicUserPlaylistSource
            {
                type = "soundcloud_playlist",
                url = sourceUrl,
                playlist_id = playlistId,
                title = album.title
            }
        };
    }

    static async Task<MusicUserPlaylistImportResult> ImportUserLikesAsync(JsonElement user, string clientId, CancellationToken cancellationToken)
    {
        const int likesImportLimit = 200;
        string userId = GetString(user, "id");
        string userUrl = GetString(user, "permalink_url");
        string username = NormalizeValue(GetString(user, "username")) ?? "SoundCloud";
        if (string.IsNullOrWhiteSpace(userId))
            return ImportUnavailable("SoundCloud профиль не удалось прочитать.");

        var album = new MusicAlbum
        {
            id = $"soundcloud:likes:{userId}",
            title = "Мои лайки (SoundCloud)",
            artist_name = username,
            images = BuildImages(GetString(user, "avatar_url"))
        };

        var likes = await LoadLikedTrackElementsAsync(userId, clientId, likesImportLimit, cancellationToken);
        if (!likes.complete)
            return ImportUnavailable("SoundCloud лайки не удалось полностью загрузить. Старый плейлист не обновлён, попробуй позже.");

        string fallbackArtwork = SelectFirstImageUrl(album.images);
        int position = 1;

        foreach (var item in likes.items)
        {
            var trackElement = ExtractLikedTrack(item);
            if (!trackElement.HasValue)
                continue;

            var mapped = MapPlaylistTrack(trackElement.Value, album, position++, fallbackArtwork);
            if (mapped != null)
                album.tracks.Add(mapped);
        }

        var tracks = DeduplicateTracks(album.tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("Лайки SoundCloud не найдены или скрыты.");

        return new MusicUserPlaylistImportResult
        {
            available = true,
            message = likes.truncated
                ? $"Импортированы последние {tracks.Count} треков SoundCloud. У профиля есть ещё лайки, они не вошли в лимит {likesImportLimit}."
                : null,
            title = album.title,
            track_count = tracks.Count,
            truncated = likes.truncated,
            tracks = tracks,
            source = new MusicUserPlaylistSource
            {
                type = "soundcloud_likes",
                url = userUrl,
                user_id = userId,
                title = username
            }
        };
    }

    static async Task<SoundCloudCollectionPage> LoadLikedTrackElementsAsync(string userId, string clientId, int limit, CancellationToken cancellationToken)
    {
        SoundCloudCollectionPage incomplete = null;

        foreach (var path in new[] { $"users/{userId}/likes", $"users/{userId}/track_likes", $"users/{userId}/likes/tracks" })
        {
            var page = await SafeLoadCollectionElementsAsync(path, clientId, limit, cancellationToken);
            var tracks = page.items.Where(item => ExtractLikedTrack(item).HasValue).ToList();
            if (tracks.Count > 0)
            {
                return new SoundCloudCollectionPage
                {
                    items = tracks,
                    complete = page.complete,
                    hasMore = page.hasMore,
                    nextPage = page.nextPage,
                    truncated = page.truncated
                };
            }

            if (!page.complete)
                incomplete ??= page;
        }

        return incomplete == null
            ? SoundCloudCollectionPage.Empty()
            : new SoundCloudCollectionPage
            {
                complete = false,
                hasMore = incomplete.hasMore,
                nextPage = incomplete.nextPage
            };
    }

    static JsonElement? ExtractLikedTrack(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        if (item.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.Object)
            return track;

        string kind = NormalizeValue(GetString(item, "kind"));
        if (string.Equals(kind, "track", StringComparison.OrdinalIgnoreCase) || item.TryGetProperty("title", out _))
            return item;

        return null;
    }

    static bool LooksLikePlaylist(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("tracks", out var tracks)
            && tracks.ValueKind == JsonValueKind.Array
            && !string.IsNullOrWhiteSpace(GetString(element, "permalink_url"));

    static bool LooksLikeUser(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
            && !string.IsNullOrWhiteSpace(GetString(element, "username"))
            && !string.IsNullOrWhiteSpace(GetString(element, "permalink_url"));

    static string NormalizeSoundCloudUrl(string value)
    {
        value = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value.TrimStart('/');

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return null;

        string host = uri.Host.Trim().ToLowerInvariant();
        if (host == "m.soundcloud.com" || host == "www.soundcloud.com")
            host = "soundcloud.com";

        if (host != "soundcloud.com")
            return null;

        string path = uri.AbsolutePath.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return null;

        return $"https://soundcloud.com{path}";
    }

    static async Task<string> ExpandSoundCloudShortUrlAsync(string value, CancellationToken cancellationToken)
    {
        value = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = "https://" + value.TrimStart('/');

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return null;

        string host = uri.Host.Trim().ToLowerInvariant();
        if (host != "on.soundcloud.com")
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            string finalUrl = response.RequestMessage?.RequestUri?.ToString();
            return NormalizeSoundCloudUrl(finalUrl);
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

    public static async Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(string inputUrl, CancellationToken cancellationToken = default)
    {
        string soundCloudUrl = NormalizeSoundCloudUrl(inputUrl)
            ?? await ExpandSoundCloudShortUrlAsync(inputUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(soundCloudUrl))
            return ImportUnavailable("Вставь ссылку на SoundCloud профиль или плейлист.");

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return ImportUnavailable("Не удалось получить SoundCloud client_id.");

        var resolved = await ResolveUrlElementAsync(soundCloudUrl, clientId, cancellationToken);
        if (!resolved.HasValue)
            return ImportUnavailable("SoundCloud ссылку не удалось открыть.");

        var element = resolved.Value;
        string kind = NormalizeValue(GetString(element, "kind"));

        if (string.Equals(kind, "user", StringComparison.OrdinalIgnoreCase) || LooksLikeUser(element))
            return await ImportUserLikesAsync(element, clientId, cancellationToken);

        if (string.Equals(kind, "playlist", StringComparison.OrdinalIgnoreCase) || LooksLikePlaylist(element))
            return await ImportResolvedPlaylistAsync(element, clientId, cancellationToken);

        return ImportUnavailable("Поддерживаются только SoundCloud профиль и плейлист.");
    }

    public static Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(MusicUserPlaylistSource source, CancellationToken cancellationToken = default)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return Task.FromResult(ImportUnavailable("У плейлиста нет SoundCloud источника."));

        return ImportPlaylistAsync(source.url, cancellationToken);
    }
}
