using System.Net;
using System.Net.Http;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Music;

// Discovery/каталог: чарты, страницы артистов и секций, поиск по запросу,
// маппинг JSON api-v2 в Music*-контракты, кодеки id.
public static partial class SoundCloudSupport
{
    public static async Task<List<MusicAlbum>> GetChartAlbumsAsync(CancellationToken cancellationToken = default)
    {
        return await MusicMetadataCacheService.GetOrCreateAsync(
            DiscoveryProviderId,
            "browse",
            $"charts:{chartsCacheVersion}:{Country.ToLowerInvariant()}",
            chartsCacheTtl,
            () => LoadChartAlbumsAsync(cancellationToken),
            cancellationToken
        ) ?? new List<MusicAlbum>();
    }

    public static async Task<MusicAlbum> GetChartAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        string playlistUrl = DecodePlaylistUrl(id);
        if (string.IsNullOrWhiteSpace(playlistUrl))
            return null;

        var album = await MusicMetadataCacheService.GetOrCreateAsync(
            DiscoveryProviderId,
            "album",
            $"playlist:{chartsCacheVersion}:{playlistUrl}",
            playlistCacheTtl,
            () => LoadChartAlbumAsync(playlistUrl, cancellationToken),
            cancellationToken
        );

        if (album?.tracks?.Count > 0)
            await CacheDiscoveredTracksAsync(album.tracks, cancellationToken);

        return album;
    }

    public static async Task<MusicArtist> GetUserArtistAsync(string id, CancellationToken cancellationToken = default)
    {
        string userUrl = DecodeUserUrl(id);
        if (string.IsNullOrWhiteSpace(userUrl))
            return null;

        return await MusicMetadataCacheService.GetOrCreateAsync(
            DiscoveryProviderId,
            "artist",
            $"user:{chartsCacheVersion}:{userUrl}",
            playlistCacheTtl,
            () => LoadUserArtistAsync(userUrl, cancellationToken),
            cancellationToken
        );
    }

    public static async Task<MusicBrowseSection> GetUserSectionAsync(string id, string page = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        var decoded = DecodeUserSectionId(id);
        if (string.IsNullOrWhiteSpace(decoded.userUrl) || string.IsNullOrWhiteSpace(decoded.section))
            return null;

        string pageKey = string.IsNullOrWhiteSpace(page) ? "first" : page.Trim();
        return await MusicMetadataCacheService.GetOrCreateAsync(
            DiscoveryProviderId,
            "artistsection",
            $"usersection:{chartsCacheVersion}:{decoded.section}:{Math.Max(1, limit)}:{decoded.userUrl}:{pageKey}",
            playlistCacheTtl,
            () => LoadUserSectionAsync(decoded.userUrl, decoded.section, page, limit, cancellationToken),
            cancellationToken
        );
    }

    public static async Task<MusicAlbum> GetUserTracksAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        string userUrl = DecodeUserTracksUrl(id);
        if (string.IsNullOrWhiteSpace(userUrl))
            return null;

        return await MusicMetadataCacheService.GetOrCreateAsync(
            DiscoveryProviderId,
            "album",
            $"usertracks:{chartsCacheVersion}:{userUrl}",
            playlistCacheTtl,
            () => LoadUserTracksAlbumAsync(userUrl, cancellationToken),
            cancellationToken
        );
    }

    public static async Task<MusicTrack> GetTrackAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var cached = await MusicMetadataCacheService.GetAsync<MusicTrack>(
            DiscoveryProviderId,
            "track",
            BuildTrackCacheKey(id),
            cancellationToken
        );
        if (cached != null)
            return cached;

        string apiTrackId = ParseTrackApiId(id);
        if (string.IsNullOrWhiteSpace(apiTrackId))
            return null;

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        string url = BuildApiV2Url($"tracks/{apiTrackId}", new Dictionary<string, string>
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
        var track = MapStandaloneTrack(document.RootElement);
        if (track == null)
            return null;

        await MusicMetadataCacheService.SaveAsync(
            DiscoveryProviderId,
            "track",
            BuildTrackCacheKey(track.id),
            track,
            trackCacheTtl,
            cancellationToken);

        return track;
    }

    public static string BuildPlaylistAlbumId(string playlistUrl)
    {
        if (string.IsNullOrWhiteSpace(playlistUrl))
            return null;

        return PlaylistIdPrefix + Uri.EscapeDataString(playlistUrl.Trim());
    }

    public static string DecodePlaylistUrl(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(PlaylistIdPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string encoded = id.Substring(PlaylistIdPrefix.Length);
        return string.IsNullOrWhiteSpace(encoded) ? null : Uri.UnescapeDataString(encoded);
    }

    public static string BuildUserArtistId(string userUrl)
    {
        if (string.IsNullOrWhiteSpace(userUrl))
            return null;

        return UserIdPrefix + Uri.EscapeDataString(userUrl.Trim());
    }

    public static string DecodeUserUrl(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(UserIdPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string encoded = id.Substring(UserIdPrefix.Length);
        return string.IsNullOrWhiteSpace(encoded) ? null : Uri.UnescapeDataString(encoded);
    }

    public static string BuildUserTracksAlbumId(string userUrl)
    {
        if (string.IsNullOrWhiteSpace(userUrl))
            return null;

        return UserTracksAlbumPrefix + Uri.EscapeDataString(userUrl.Trim());
    }

    static string BuildUserSectionId(string userUrl, string section)
    {
        if (string.IsNullOrWhiteSpace(userUrl) || string.IsNullOrWhiteSpace(section))
            return null;

        return $"{UserSectionPrefix}{Uri.EscapeDataString(userUrl.Trim())}:{section.Trim().ToLowerInvariant()}";
    }

    static (string userUrl, string section) DecodeUserSectionId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(UserSectionPrefix, StringComparison.OrdinalIgnoreCase))
            return (null, null);

        string value = id.Substring(UserSectionPrefix.Length);
        int separator = value.LastIndexOf(':');
        if (separator <= 0 || separator >= value.Length - 1)
            return (null, null);

        string encodedUserUrl = value.Substring(0, separator);
        string section = value.Substring(separator + 1);

        return (
            string.IsNullOrWhiteSpace(encodedUserUrl) ? null : Uri.UnescapeDataString(encodedUserUrl),
            NormalizeUserSectionKey(section)
        );
    }

    static string NormalizeUserSectionKey(string section)
    {
        string value = NormalizeValue(section)?.ToLowerInvariant();
        return value is "toptracks" or "albums" or "tracks" or "fansalsolike" ? value : null;
    }

    public static string DecodeUserTracksUrl(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(UserTracksAlbumPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string encoded = id.Substring(UserTracksAlbumPrefix.Length);
        return string.IsNullOrWhiteSpace(encoded) ? null : Uri.UnescapeDataString(encoded);
    }

    public static bool IsUserArtist(string provider, string id)
    {
        return string.Equals(provider, DiscoveryProviderId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(DecodeUserUrl(id));
    }

    public static bool IsUserTracksAlbum(string provider, string id)
    {
        return string.Equals(provider, DiscoveryProviderId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(DecodeUserTracksUrl(id));
    }

    public static bool IsUserSection(string provider, string id)
    {
        var decoded = DecodeUserSectionId(id);
        return string.Equals(provider, DiscoveryProviderId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(decoded.userUrl)
            && !string.IsNullOrWhiteSpace(decoded.section);
    }

    static async Task<List<MusicAlbum>> LoadChartAlbumsAsync(CancellationToken cancellationToken)
    {
        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return new List<MusicAlbum>();

        string url = BuildApiV2Url("charts/selections", new Dictionary<string, string>
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
            return new List<MusicAlbum>();
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("collection", out var selections) || selections.ValueKind != JsonValueKind.Array)
            return new List<MusicAlbum>();

        JsonElement? selection = PickSelection(selections);
        if (selection == null || !TryGetNestedCollection(selection.Value, "items", out var playlists))
            return new List<MusicAlbum>();

        var results = new List<MusicAlbum>();
        int index = 0;

        foreach (var playlist in playlists.EnumerateArray())
        {
            if (!ShouldExposePlaylist(playlist))
                continue;

            var album = MapChartPlaylist(selection.Value, playlist, index++);
            if (album != null)
                results.Add(album);
        }

        return results;
    }

    static async Task<MusicAlbum> LoadChartAlbumAsync(string playlistUrl, CancellationToken cancellationToken)
    {
        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var resolved = await ResolveUrlElementAsync(playlistUrl, clientId, cancellationToken);
        if (!resolved.HasValue)
            return null;

        var playlist = resolved.Value;

        int? trackCount = GetInt(playlist, "track_count");
        int loadedTracks = GetArrayLength(playlist, "tracks");
        string playlistId = GetString(playlist, "id");

        if (!string.IsNullOrWhiteSpace(playlistId) && (!trackCount.HasValue || loadedTracks < trackCount.Value))
        {
            var hydrated = await LoadPlaylistByIdAsync(playlistId, clientId, cancellationToken);
            if (hydrated.HasValue)
                playlist = hydrated.Value;
        }

        var trackDetails = await LoadTrackDetailsAsync(CollectTrackIdsForHydration(playlist), clientId, cancellationToken);
        return MapResolvedPlaylist(playlist, trackDetails.tracks);
    }

    static async Task<MusicArtist> LoadUserArtistAsync(string userUrl, CancellationToken cancellationToken)
    {
        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var resolved = await ResolveUrlElementAsync(userUrl, clientId, cancellationToken);
        if (!resolved.HasValue)
            return null;

        var user = resolved.Value;
        var artist = MapUser(user, includeTracksAlbum: false);
        if (artist == null)
            return null;

        string userId = GetString(user, "id");
        if (string.IsNullOrWhiteSpace(userId))
            return artist;

        var topTracksTask = SafeLoadCollectionElementsAsync($"users/{userId}/toptracks", clientId, 30, cancellationToken);
        var albumsTask = SafeLoadCollectionElementsAsync($"users/{userId}/albums", clientId, 20, cancellationToken);
        var allTracksTask = SafeLoadCollectionElementsAsync($"users/{userId}/tracks", clientId, 20, cancellationToken);
        var relatedArtistsTask = SafeLoadCollectionElementsAsync(
            $"users/{userId}/relatedartists",
            clientId,
            12,
            cancellationToken,
            new Dictionary<string, string>
            {
                ["creators_only"] = "false",
                ["page_size"] = "12"
            });

        await Task.WhenAll(topTracksTask, albumsTask, allTracksTask, relatedArtistsTask);

        var topTracks = await topTracksTask;
        var albums = await albumsTask;
        var allTracks = await allTracksTask;
        var relatedArtists = await relatedArtistsTask;

        AddSectionIfNotEmpty(artist, BuildTrackSection(userUrl, "toptracks", "Top tracks", topTracks.items, topTracks.hasMore, topTracks.nextPage));

        var albumsSection = BuildAlbumSection(userUrl, "albums", "Albums", albums.items, albums.hasMore, albums.nextPage);
        AddSectionIfNotEmpty(artist, albumsSection);
        if (albumsSection?.albums?.Count > 0)
            artist.albums = albumsSection.albums;

        AddSectionIfNotEmpty(artist, BuildTrackSection(userUrl, "tracks", "All tracks", allTracks.items, allTracks.hasMore, allTracks.nextPage));
        AddSectionIfNotEmpty(artist, BuildArtistSection(userUrl, "fansalsolike", "Fans also like", relatedArtists.items, relatedArtists.hasMore, relatedArtists.nextPage));

        await CacheDiscoveredTracksAsync(
            artist.sections
                ?.Where(section => section?.tracks != null)
                .SelectMany(section => section.tracks)
                ?? Enumerable.Empty<MusicTrack>(),
            cancellationToken);

        return artist;
    }

    static async Task<MusicBrowseSection> LoadUserSectionAsync(string userUrl, string section, string page, int limit, CancellationToken cancellationToken)
    {
        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        section = NormalizeUserSectionKey(section);
        if (string.IsNullOrWhiteSpace(section))
            return null;

        int safeLimit = Math.Max(1, Math.Min(limit <= 0 ? 20 : limit, 50));
        SoundCloudCollectionPage pageResult;

        if (!string.IsNullOrWhiteSpace(page))
        {
            pageResult = await SafeLoadCollectionElementsAsync(
                null,
                clientId,
                safeLimit,
                cancellationToken,
                pageUrl: page);
        }
        else
        {
            var resolved = await ResolveUrlElementAsync(userUrl, clientId, cancellationToken);
            if (!resolved.HasValue)
                return null;

            string userId = GetString(resolved.Value, "id");
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            pageResult = await SafeLoadUserSectionPageAsync(userId, section, clientId, safeLimit, cancellationToken);
        }

        return section switch
        {
            "toptracks" => BuildTrackSection(userUrl, section, "Top tracks", pageResult.items, pageResult.hasMore, pageResult.nextPage),
            "albums" => BuildAlbumSection(userUrl, section, "Albums", pageResult.items, pageResult.hasMore, pageResult.nextPage),
            "tracks" => BuildTrackSection(userUrl, section, "All tracks", pageResult.items, pageResult.hasMore, pageResult.nextPage),
            "fansalsolike" => BuildArtistSection(userUrl, section, "Fans also like", pageResult.items, pageResult.hasMore, pageResult.nextPage),
            _ => null
        };
    }

    static Task<SoundCloudCollectionPage> SafeLoadUserSectionPageAsync(
        string userId,
        string section,
        string clientId,
        int limit,
        CancellationToken cancellationToken)
    {
        return section switch
        {
            "toptracks" => SafeLoadCollectionElementsAsync($"users/{userId}/toptracks", clientId, limit, cancellationToken),
            "albums" => SafeLoadCollectionElementsAsync($"users/{userId}/albums", clientId, limit, cancellationToken),
            "tracks" => SafeLoadCollectionElementsAsync($"users/{userId}/tracks", clientId, limit, cancellationToken),
            "fansalsolike" => SafeLoadCollectionElementsAsync(
                $"users/{userId}/relatedartists",
                clientId,
                limit,
                cancellationToken,
                new Dictionary<string, string>
                {
                    ["creators_only"] = "false",
                    ["page_size"] = limit.ToString()
                }),
            _ => Task.FromResult(SoundCloudCollectionPage.Empty())
        };
    }

    static async Task<MusicAlbum> LoadUserTracksAlbumAsync(string userUrl, CancellationToken cancellationToken)
    {
        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var resolved = await ResolveUrlElementAsync(userUrl, clientId, cancellationToken);
        if (!resolved.HasValue)
            return null;

        var user = resolved.Value;
        string userId = GetString(user, "id");
        string username = GetString(user, "username") ?? "SoundCloud";
        var album = BuildUserTracksAlbum(userUrl, username, BuildImages(GetString(user, "avatar_url")));

        if (string.IsNullOrWhiteSpace(userId))
            return album;

        foreach (var track in await LoadUserTrackElementsAsync(userId, clientId, 50, cancellationToken))
        {
            var mapped = MapPlaylistTrack(track, album, album.tracks.Count + 1, SelectFirstImageUrl(album.images));
            if (mapped != null)
                album.tracks.Add(mapped);
        }

        return album;
    }

    static Task CacheDiscoveredTracksAsync(IEnumerable<MusicTrack> tracks, CancellationToken cancellationToken)
    {
        if (tracks == null)
            return Task.CompletedTask;

        var writes = new List<Task>();

        foreach (var track in tracks)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.id))
                continue;

            writes.Add(MusicMetadataCacheService.SaveAsync(
                DiscoveryProviderId,
                "track",
                BuildTrackCacheKey(track.id),
                track,
                trackCacheTtl,
                cancellationToken));
        }

        return writes.Count == 0 ? Task.CompletedTask : Task.WhenAll(writes);
    }

    static JsonElement? PickSelection(JsonElement selections)
    {
        if (selections.ValueKind != JsonValueKind.Array || selections.GetArrayLength() == 0)
            return null;

        string country = Country.ToUpperInvariant();

        if (country is "GB" or "UK")
        {
            foreach (var selection in selections.EnumerateArray())
            {
                string title = GetString(selection, "title");
                if (!string.IsNullOrWhiteSpace(title) && title.Contains("UK", StringComparison.OrdinalIgnoreCase))
                    return selection;
            }
        }

        foreach (var selection in selections.EnumerateArray())
        {
            string title = GetString(selection, "title");
            if (!string.IsNullOrWhiteSpace(title) && title.Contains("US", StringComparison.OrdinalIgnoreCase))
                return selection;
        }

        foreach (var selection in selections.EnumerateArray())
            return selection;

        return null;
    }

    static bool TryGetNestedCollection(JsonElement element, string propertyName, out JsonElement collection)
    {
        collection = default;

        if (!element.TryGetProperty(propertyName, out var nested) || nested.ValueKind != JsonValueKind.Object)
            return false;

        if (!nested.TryGetProperty("collection", out collection) || collection.ValueKind != JsonValueKind.Array)
            return false;

        return true;
    }

    static bool ShouldExposePlaylist(JsonElement playlist)
    {
        string url = GetString(playlist, "permalink_url");
        string title = GetString(playlist, "title");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
            return false;

        return !string.Equals(title.Trim(), "Artist Pro", StringComparison.OrdinalIgnoreCase);
    }

    static MusicAlbum MapChartPlaylist(JsonElement selection, JsonElement playlist, int index)
    {
        string playlistUrl = GetString(playlist, "permalink_url");
        if (string.IsNullOrWhiteSpace(playlistUrl))
            return null;

        string title = NormalizePlaylistTitle(GetString(playlist, "title"));
        string artwork = UpgradeArtwork(GetString(playlist, "artwork_url"));
        string description = GetString(selection, "title");

        return new MusicAlbum
        {
            id = BuildPlaylistAlbumId(playlistUrl),
            title = title,
            artist_name = "SoundCloud",
            date = GetString(playlist, "display_date") ?? GetString(playlist, "published_at"),
            type = "Playlist",
            description = description,
            images = string.IsNullOrWhiteSpace(artwork)
                ? new List<MusicImage>()
                : new List<MusicImage> { new() { url = artwork, width = 500, height = 500 } },
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = DiscoveryProviderId, external_id = playlistUrl }
            }
        };
    }

    static MusicAlbum MapResolvedPlaylist(JsonElement playlist, Dictionary<string, JsonElement> hydratedTracks = null)
    {
        string playlistUrl = GetString(playlist, "permalink_url");
        if (string.IsNullOrWhiteSpace(playlistUrl))
            return null;

        string title = NormalizePlaylistTitle(GetString(playlist, "title"));
        string artwork = UpgradeArtwork(GetString(playlist, "artwork_url"));
        string date = GetString(playlist, "display_date") ?? GetString(playlist, "published_at");
        string artistName = "SoundCloud";
        string avatar = null;
        if (playlist.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            artistName = NormalizeValue(GetString(user, "username")) ?? artistName;
            avatar = GetString(user, "avatar_url");
        }

        var album = new MusicAlbum
        {
            id = BuildPlaylistAlbumId(playlistUrl),
            title = title,
            artist_name = artistName,
            date = date,
            type = IsSoundCloudAlbumSet(playlist) ? "Album" : "Playlist",
            description = GetString(playlist, "genre") ?? (IsSoundCloudAlbumSet(playlist) ? "Album" : "Playlist"),
            images = BuildImages(artwork, avatar),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = DiscoveryProviderId, external_id = playlistUrl }
            },
            tracks = new List<MusicTrack>()
        };

        if (playlist.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
        {
            int position = 1;
            foreach (var track in tracks.EnumerateArray())
            {
                var mapped = MapPlaylistTrack(track, album, position++, artwork, hydratedTracks);
                if (mapped != null)
                    album.tracks.Add(mapped);
            }
        }

        return album;
    }

    static MusicAlbum MapSearchPlaylist(JsonElement playlist, string type)
    {
        string playlistUrl = GetString(playlist, "permalink_url");
        string title = NormalizeValue(GetString(playlist, "title"));
        if (string.IsNullOrWhiteSpace(playlistUrl) || string.IsNullOrWhiteSpace(title))
            return null;

        string artistName = null;
        string avatar = null;
        if (playlist.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            artistName = NormalizeValue(GetString(user, "username"));
            avatar = GetString(user, "avatar_url");
        }

        return new MusicAlbum
        {
            id = BuildPlaylistAlbumId(playlistUrl),
            title = title,
            artist_name = string.IsNullOrWhiteSpace(artistName) ? "SoundCloud" : artistName,
            date = GetString(playlist, "display_date") ?? GetString(playlist, "published_at"),
            type = string.IsNullOrWhiteSpace(type) ? "Playlist" : type,
            description = GetString(playlist, "genre") ?? type ?? "SoundCloud",
            images = BuildImages(GetString(playlist, "artwork_url"), avatar),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = DiscoveryProviderId, external_id = playlistUrl }
            }
        };
    }

    static bool IsSoundCloudAlbumSet(JsonElement playlist)
    {
        string setType = NormalizeValue(GetString(playlist, "set_type"));
        string playlistType = NormalizeValue(GetString(playlist, "playlist_type"));
        string kind = NormalizeValue(GetString(playlist, "kind"));

        return string.Equals(setType, "album", StringComparison.OrdinalIgnoreCase)
            || string.Equals(playlistType, "album", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "album", StringComparison.OrdinalIgnoreCase);
    }

    static void AddSectionIfNotEmpty(MusicArtist artist, MusicBrowseSection section)
    {
        if (artist == null || section == null)
            return;

        bool hasEntries = section.tracks?.Count > 0
            || section.albums?.Count > 0
            || section.artists?.Count > 0;

        if (hasEntries)
            artist.sections.Add(section);
    }

    static MusicBrowseSection BuildTrackSection(string userUrl, string key, string title, List<JsonElement> items, bool hasMore, string nextPage)
    {
        var section = new MusicBrowseSection
        {
            id = BuildUserSectionId(userUrl, key),
            title = title,
            type = "tracks",
            source_provider = DiscoveryProviderId,
            has_more = hasMore,
            next_page = nextPage,
            tracks = new List<MusicTrack>()
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? new List<JsonElement>())
        {
            var track = MapStandaloneTrack(item);
            if (track == null || string.IsNullOrWhiteSpace(track.id) || !seen.Add(track.id))
                continue;

            section.tracks.Add(track);
        }

        return section;
    }

    static MusicBrowseSection BuildAlbumSection(string userUrl, string key, string title, List<JsonElement> items, bool hasMore, string nextPage)
    {
        var section = new MusicBrowseSection
        {
            id = BuildUserSectionId(userUrl, key),
            title = title,
            type = "albums",
            source_provider = DiscoveryProviderId,
            has_more = hasMore,
            next_page = nextPage,
            albums = new List<MusicAlbum>()
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? new List<JsonElement>())
        {
            var album = MapSearchPlaylist(item, "Album");
            if (album == null || string.IsNullOrWhiteSpace(album.id) || !seen.Add(album.id))
                continue;

            section.albums.Add(album);
        }

        return section;
    }

    static MusicBrowseSection BuildArtistSection(string userUrl, string key, string title, List<JsonElement> items, bool hasMore, string nextPage)
    {
        var section = new MusicBrowseSection
        {
            id = BuildUserSectionId(userUrl, key),
            title = title,
            type = "artists",
            source_provider = DiscoveryProviderId,
            has_more = hasMore,
            next_page = nextPage,
            artists = new List<MusicArtist>()
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? new List<JsonElement>())
        {
            var artist = MapUser(item, includeTracksAlbum: false);
            if (artist == null || string.IsNullOrWhiteSpace(artist.id) || !seen.Add(artist.id))
                continue;

            section.artists.Add(artist);
        }

        return section;
    }

    static MusicArtist MapUser(JsonElement user, bool includeTracksAlbum)
    {
        string userUrl = GetString(user, "permalink_url");
        string username = NormalizeValue(GetString(user, "username"));
        if (string.IsNullOrWhiteSpace(userUrl) || string.IsNullOrWhiteSpace(username))
            return null;

        var images = BuildImages(GetString(user, "avatar_url"));
        var artist = new MusicArtist
        {
            id = BuildUserArtistId(userUrl),
            name = username,
            sort_name = username,
            description = "SoundCloud",
            images = images,
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = DiscoveryProviderId, external_id = userUrl }
            }
        };

        if (includeTracksAlbum)
            artist.albums.Add(BuildUserTracksAlbum(userUrl, username, images));

        return artist;
    }

    static MusicAlbum BuildUserTracksAlbum(string userUrl, string username, List<MusicImage> images)
    {
        return new MusicAlbum
        {
            id = BuildUserTracksAlbumId(userUrl),
            title = "Треки пользователя",
            artist_id = BuildUserArtistId(userUrl),
            artist_name = string.IsNullOrWhiteSpace(username) ? "SoundCloud" : username,
            type = "Playlist",
            description = "SoundCloud",
            images = images ?? new List<MusicImage>(),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = DiscoveryProviderId, external_id = userUrl }
            }
        };
    }

    static MusicTrack MapPlaylistTrack(JsonElement track, MusicAlbum album, int position, string fallbackArtwork, Dictionary<string, JsonElement> hydratedTracks = null)
    {
        string trackId = GetString(track, "id");
        if ((string.IsNullOrWhiteSpace(GetString(track, "title")) || string.IsNullOrWhiteSpace(GetString(track, "artwork_url")))
            && !string.IsNullOrWhiteSpace(trackId)
            && hydratedTracks?.TryGetValue(trackId, out var hydratedTrack) == true)
        {
            track = hydratedTrack;
        }

        string rawTitle = GetString(track, "title");
        var titleParts = SplitArtistTitleFromSoundCloudTitle(rawTitle);
        string title = titleParts.title ?? NormalizeValue(rawTitle);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        string artistName = titleParts.artist;
        if (track.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            artistName ??= GetString(user, "username")?.Trim();

        string artwork = UpgradeArtwork(GetString(track, "artwork_url")) ?? fallbackArtwork;
        string permalinkUrl = GetString(track, "permalink_url");
        string urn = GetString(track, "urn");
        string externalId = !string.IsNullOrWhiteSpace(permalinkUrl)
            ? permalinkUrl
            : (!string.IsNullOrWhiteSpace(urn) ? urn : title);

        return new MusicTrack
        {
            id = !string.IsNullOrWhiteSpace(urn) ? urn : $"soundcloud:track:{Uri.EscapeDataString(externalId)}",
            title = title.Trim(),
            artist_name = string.IsNullOrWhiteSpace(artistName) ? "SoundCloud" : artistName,
            artists = string.IsNullOrWhiteSpace(artistName) ? new List<string>() : new List<string> { artistName },
            album_id = album?.id,
            album_title = album?.title,
            duration_ms = GetInt(track, "full_duration") ?? GetInt(track, "duration"),
            track_number = position,
            date = GetString(track, "published_at"),
            images = string.IsNullOrWhiteSpace(artwork)
                ? new List<MusicImage>()
                : new List<MusicImage> { new() { url = artwork, width = 500, height = 500 } },
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = DiscoveryProviderId, external_id = externalId }
            }
        };
    }

    static List<string> CollectTrackIdsForHydration(JsonElement playlist)
    {
        var ids = new List<string>();
        if (!playlist.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array)
            return ids;

        foreach (var track in tracks.EnumerateArray())
        {
            string title = GetString(track, "title");
            string id = GetString(track, "id");

            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// Рекомендации SoundCloud по конкретному треку: top-1 поиска как сид →
    /// /tracks/{id}/related («Related tracks» сайта, коллаборативная
    /// фильтрация), при недоборе — track-station того же сида. Используется
    /// радио как основной слой похожести (вместо «топ треков артиста»).
    /// </summary>
    public static Task<List<MusicTrack>> FindRelatedTracksByQueryAsync(string query, int limit = 12, CancellationToken cancellationToken = default)
        => FindRelatedTracksByQueryAsync(query, null, null, limit, cancellationToken);

    public static async Task<List<MusicTrack>> FindRelatedTracksByQueryAsync(string query, string expectedArtist, string expectedTitle, int limit = 12, CancellationToken cancellationToken = default)
    {
        var tracks = new List<MusicTrack>();
        if (string.IsNullOrWhiteSpace(query))
            return tracks;

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var seedElements = await SearchTrackElementsAsync(query, clientId, 1, cancellationToken);
        if (seedElements == null)
            return null;

        if (seedElements.Count == 0)
            return tracks;

        if (!IsRelevantRelatedSeed(seedElements[0], expectedArtist, expectedTitle))
            return tracks;

        if (!seedElements[0].TryGetProperty("id", out var idProperty) || idProperty.ValueKind != JsonValueKind.Number)
            return tracks;

        long seedId = idProperty.GetInt64();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAll(IEnumerable<JsonElement> items)
        {
            foreach (var item in items ?? Enumerable.Empty<JsonElement>())
            {
                var element = item;

                // station-элементы бывают обёрнуты в { "track": {...} }
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("track", out var inner)
                    && inner.ValueKind == JsonValueKind.Object)
                    element = inner;

                var track = MapStandaloneTrack(element);
                if (track == null || string.IsNullOrWhiteSpace(track.id) || !seen.Add(track.id))
                    continue;

                tracks.Add(track);

                if (tracks.Count >= limit)
                    return;
            }
        }

        var related = await SafeLoadCollectionElementsAsync($"tracks/{seedId}/related", clientId, limit, cancellationToken);
        AddAll(related.items);

        if (tracks.Count < limit)
        {
            var station = await SafeLoadCollectionElementsAsync($"stations/soundcloud:track-stations:{seedId}/tracks", clientId, limit, cancellationToken);
            AddAll(station.items);

            if (tracks.Count == 0 && !related.complete && !station.complete)
                return null;
        }

        return tracks;
    }

    static bool IsRelevantRelatedSeed(JsonElement seedElement, string expectedArtist, string expectedTitle)
    {
        if (string.IsNullOrWhiteSpace(expectedTitle))
            return true;

        var seed = MapStandaloneTrack(seedElement);
        if (seed == null)
            return false;

        string expectedTitleKey = NormalizeRelatedSeedText(expectedTitle);
        string foundTitleKey = NormalizeRelatedSeedText(seed.title);
        if (string.IsNullOrWhiteSpace(expectedTitleKey) || string.IsNullOrWhiteSpace(foundTitleKey))
            return false;

        int expectedTitleTokens = expectedTitleKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        int sharedTitleTokens = CountSharedTokens(expectedTitleKey, foundTitleKey);
        int requiredTitleTokens = expectedTitleTokens <= 1
            ? 1
            : Math.Max(1, (int)Math.Ceiling(expectedTitleTokens * 0.6));

        if (sharedTitleTokens < requiredTitleTokens)
            return false;

        string expectedArtistKey = NormalizeRelatedSeedText(expectedArtist);
        if (string.IsNullOrWhiteSpace(expectedArtistKey))
            return true;

        string foundArtistKey = NormalizeRelatedSeedText(seed.artist_name);
        string foundCombinedKey = NormalizeRelatedSeedText($"{seed.artist_name} {seed.title}");

        if (CountSharedTokens(expectedArtistKey, foundArtistKey) > 0 || CountSharedTokens(expectedArtistKey, foundCombinedKey) > 0)
            return true;

        // SoundCloud uploads often use usernames instead of real artists. For
        // non-generic titles, exact title coverage is enough to keep related.
        return expectedTitleTokens > 1 && sharedTitleTokens >= expectedTitleTokens;
    }

    static string NormalizeRelatedSeedText(string value)
    {
        value = Regex.Replace(value ?? string.Empty, @"\([^)]*\)|\[[^\]]*\]", " ");
        value = Regex.Replace(value, @"\b(feat|ft|featuring|official|audio|video|lyrics|lyric|clip|mv|hd|4k)\b", " ", RegexOptions.IgnoreCase);
        return NormalizeSearchText(value);
    }

    public static async Task<List<MusicTrack>> SearchTracksByQueryAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        var tracks = new List<MusicTrack>();
        if (string.IsNullOrWhiteSpace(query))
            return tracks;

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elements = await SearchTrackElementsAsync(query, clientId, limit, cancellationToken);
        if (elements == null)
            return null;

        foreach (var item in elements)
        {
            var track = MapStandaloneTrack(item);
            if (track == null || string.IsNullOrWhiteSpace(track.id) || !seen.Add(track.id))
                continue;

            tracks.Add(track);
        }

        return tracks;
    }

    public static async Task<List<MusicAlbum>> SearchPlaylistsByQueryAsync(string query, int limit = 8, CancellationToken cancellationToken = default)
    {
        var playlists = new List<MusicAlbum>();
        if (string.IsNullOrWhiteSpace(query))
            return playlists;

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elements = await SearchElementsAsync("search/playlists", query, clientId, limit, cancellationToken);
        if (elements == null)
            return null;

        foreach (var item in elements)
        {
            if (IsSoundCloudAlbumSet(item))
                continue;

            var playlist = MapSearchPlaylist(item, "Playlist");
            if (playlist == null || string.IsNullOrWhiteSpace(playlist.id) || !seen.Add(playlist.id))
                continue;

            playlists.Add(playlist);
        }

        return playlists;
    }

    public static async Task<List<MusicAlbum>> SearchAlbumsByQueryAsync(string query, int limit = 8, CancellationToken cancellationToken = default)
    {
        var albums = new List<MusicAlbum>();
        if (string.IsNullOrWhiteSpace(query))
            return albums;

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var albumElements = await SearchElementsAsync("search/albums", query, clientId, limit, cancellationToken);
        if (albumElements == null)
            return null;

        foreach (var item in albumElements)
        {
            var album = MapSearchPlaylist(item, "Album");
            if (album == null || string.IsNullOrWhiteSpace(album.id) || !seen.Add(album.id))
                continue;

            albums.Add(album);
        }

        if (albums.Count >= Math.Max(1, limit))
            return albums;

        var playlistElements = await SearchElementsAsync("search/playlists", query, clientId, limit * 2, cancellationToken);
        if (playlistElements == null)
            return albums.Count > 0 ? albums : null;

        foreach (var item in playlistElements)
        {
            if (!IsSoundCloudAlbumSet(item))
                continue;

            var album = MapSearchPlaylist(item, "Album");
            if (album == null || string.IsNullOrWhiteSpace(album.id) || !seen.Add(album.id))
                continue;

            albums.Add(album);

            if (albums.Count >= Math.Max(1, limit))
                break;
        }

        return albums;
    }

    public static async Task<List<MusicArtist>> SearchArtistsByQueryAsync(string query, int limit = 8, CancellationToken cancellationToken = default)
    {
        var artists = new List<MusicArtist>();
        if (string.IsNullOrWhiteSpace(query))
            return artists;

        string clientId = await GetClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var elements = await SearchElementsAsync("search/users", query, clientId, limit, cancellationToken);
        if (elements == null)
            return null;

        foreach (var item in elements)
        {
            var artist = MapUser(item, includeTracksAlbum: false);
            if (artist == null || string.IsNullOrWhiteSpace(artist.id) || !seen.Add(artist.id))
                continue;

            artists.Add(artist);
        }

        return artists;
    }

    static string NormalizePlaylistTitle(string title)
    {
        string value = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return "SoundCloud";

        return value.Equals("All music genres", StringComparison.OrdinalIgnoreCase)
            ? "Top 50"
            : value;
    }

    static string UpgradeArtwork(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return url
            .Replace("-large.", "-t500x500.", StringComparison.OrdinalIgnoreCase)
            .Replace("-t300x300.", "-t500x500.", StringComparison.OrdinalIgnoreCase);
    }

    static List<MusicImage> BuildImages(params string[] urls)
    {
        var images = new List<MusicImage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in urls ?? Array.Empty<string>())
        {
            string url = UpgradeArtwork(raw);
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                continue;

            images.Add(new MusicImage
            {
                url = url,
                width = 500,
                height = 500
            });
        }

        return images;
    }

    static string SelectFirstImageUrl(List<MusicImage> images)
    {
        return images?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i?.url))?.url;
    }

    static string BuildTrackCacheKey(string id) => $"track:{chartsCacheVersion}:{id.Trim().ToLowerInvariant()}";

    static MusicTrack MapStandaloneTrack(JsonElement track)
    {
        string rawTitle = GetString(track, "title");
        var titleParts = SplitArtistTitleFromSoundCloudTitle(rawTitle);
        string title = titleParts.title ?? NormalizeValue(rawTitle);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        string artistName = titleParts.artist;
        if (track.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
            artistName ??= GetString(user, "username")?.Trim();

        string artwork = UpgradeArtwork(GetString(track, "artwork_url"));
        string permalinkUrl = GetString(track, "permalink_url");
        string urn = GetString(track, "urn");
        string externalId = !string.IsNullOrWhiteSpace(permalinkUrl)
            ? permalinkUrl
            : (!string.IsNullOrWhiteSpace(urn) ? urn : title);

        return new MusicTrack
        {
            id = !string.IsNullOrWhiteSpace(urn) ? urn : $"soundcloud:track:{Uri.EscapeDataString(externalId)}",
            title = title.Trim(),
            artist_name = string.IsNullOrWhiteSpace(artistName) ? "SoundCloud" : artistName,
            artists = string.IsNullOrWhiteSpace(artistName) ? new List<string>() : new List<string> { artistName },
            duration_ms = GetInt(track, "full_duration") ?? GetInt(track, "duration"),
            date = GetString(track, "published_at"),
            images = string.IsNullOrWhiteSpace(artwork)
                ? new List<MusicImage>()
                : new List<MusicImage> { new() { url = artwork, width = 500, height = 500 } },
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = DiscoveryProviderId, external_id = externalId }
            }
        };
    }
}
