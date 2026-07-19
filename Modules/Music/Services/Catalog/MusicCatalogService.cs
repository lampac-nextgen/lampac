namespace Music;

public static class MusicCatalogService
{
    static readonly TimeSpan searchCacheTtl = TimeSpan.FromHours(6);
    static readonly TimeSpan artistCacheTtl = TimeSpan.FromDays(7);
    static readonly TimeSpan albumCacheTtl = TimeSpan.FromDays(7);
    static readonly TimeSpan trackCacheTtl = TimeSpan.FromDays(7);
    static readonly TimeSpan discoveryHomeProviderResponseBudget = TimeSpan.FromSeconds(2);
    static readonly TimeSpan discoveryHomeProviderWarmTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan discoverySectionProviderTimeout = TimeSpan.FromSeconds(8);
    static readonly TimeSpan searchSectionProviderTimeout = TimeSpan.FromSeconds(4);
    static readonly TimeSpan searchMetadataResponseBudget = TimeSpan.FromSeconds(2);
    static readonly TimeSpan searchMetadataTimeout = TimeSpan.FromSeconds(20);
    const int recentSectionLimit = 100;
    const int browseSectionLimit = 20;
    const int browseSectionFullLimit = 100;
    const int searchSectionLimit = 8;
    const int searchSectionFullLimit = 24;
    // v27: инвалидация после миграции на 1.40.0 — залипшие пустые SC-секции
    // («Макс Корж»: пустышка пережила 20-минутный emptyTtl, вариантные
    // запросы работали — классическая ловушка перезаписи HybridFileCache)
    const string metadataCacheVersion = "v27";
    static readonly object homeWarmLock = new();
    static readonly Dictionary<string, Task<List<MusicBrowseSection>>> homeWarmTasks = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<MusicHomeResponse> GetHomeAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var sections = new List<MusicSection>
        {
            new() { id = "search", title = "Search", endpoint = "/music/search", type = "search" },
            new() { id = "providers", title = "Providers", endpoint = "/music/providers", type = "info" }
        };

        if (MusicProviderRegistry.AuthProviders.Any(i => i.Enabled))
            sections.Add(new MusicSection { id = "auth", title = "Auth", endpoint = "/music/auth/state", type = "info" });

        var recentlyPlayedTask = MusicPlaybackHistoryService.GetRecentAsync(profileId, recentSectionLimit, cancellationToken);
        var userPlaylistsTask = MusicUserPlaylistService.ListAsync(profileId, cancellationToken);
        var browseSectionsTask = GetBrowseSectionsAsync(cancellationToken);

        await Task.WhenAll(recentlyPlayedTask, userPlaylistsTask, browseSectionsTask);

        return new MusicHomeResponse
        {
            title = "Music",
            status = "ok",
            version = "0.1.0",
            sections = sections,
            metadata_providers = MusicProviderRegistry.DescribeMetadata(),
            audio_providers = MusicProviderRegistry.DescribeAudio(),
            auth_providers = MusicProviderRegistry.DescribeAuth(),
            recently_played = await recentlyPlayedTask,
            user_playlists = await userPlaylistsTask,
            browse_sections = await browseSectionsTask
        };
    }

    public static async Task<MusicBrowseSection> GetBrowseSectionAsync(string sectionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return null;

        foreach (var provider in MusicProviderRegistry.DiscoveryProviders.Where(i => i.Enabled))
        {
            var section = await SafeGetSectionAsync(provider, sectionId, browseSectionFullLimit, cancellationToken);
            if (section != null)
                return section;
        }

        return null;
    }

    public static async Task<MusicSearchResponse> SearchAsync(string query, string provider = null, bool expanded = false, CancellationToken cancellationToken = default)
    {
        var metadata = MusicProviderRegistry.GetMetadataProvider(provider);
        var metadataTask = SearchMetadataAsync(metadata, query, expanded, cancellationToken);
        ObserveBackgroundTask(metadataTask);
        var searchSectionsTask = GetSearchSectionsAsync(query, expanded, cancellationToken);
        var metadataBudgetTask = Task.Delay(searchMetadataResponseBudget, cancellationToken);

        var searchSections = await searchSectionsTask;
        var hasSearchSections = searchSections.Count > 0;
        (MusicSearchResult result, bool pending) metadataState;
        if (hasSearchSections)
            metadataState = await WaitForSearchMetadataForResponseAsync(metadataTask, metadataBudgetTask, cancellationToken);
        else
            metadataState = (await metadataTask, false);

        var result = metadataState.result;

        if (result.artists != null && result.artists.Count > 0)
            await DiscogsArtistImageService.ApplyCachedAsync(result.artists, cancellationToken);

        bool hasResults =
            (result.artists?.Count > 0)
            || (result.albums?.Count > 0)
            || (result.tracks?.Count > 0)
            || hasSearchSections;

        return new MusicSearchResponse
        {
            query = query,
            status = hasResults ? "ok" : "empty",
            metadata_provider = metadata?.Id,
            metadata_pending = metadataState.pending,
            audio_providers = MusicProviderRegistry.AudioProviders.Where(i => i.Enabled).Select(i => i.Id).ToList(),
            artists = result.artists,
            albums = result.albums,
            tracks = result.tracks,
            search_sections = searchSections
        };
    }

    static async Task<(MusicSearchResult result, bool pending)> WaitForSearchMetadataForResponseAsync(Task<MusicSearchResult> metadataTask, Task budgetTask, CancellationToken cancellationToken)
    {
        if (metadataTask.IsCompleted)
            return (await metadataTask, false);

        var completed = await Task.WhenAny(metadataTask, budgetTask);
        if (completed == metadataTask)
            return (await metadataTask, false);

        cancellationToken.ThrowIfCancellationRequested();
        return (new MusicSearchResult(), true);
    }

    static void ObserveBackgroundTask(Task task)
    {
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    static async Task<MusicSearchResult> SearchMetadataAsync(IMusicMetadataProvider metadata, string query, bool expanded, CancellationToken cancellationToken)
    {
        if (metadata == null || string.IsNullOrWhiteSpace(query))
            return new MusicSearchResult();

        try
        {
            // MusicBrainz живёт на rate-limit и 20s HttpClient-таймауте:
            // ответ поиска ждёт metadata только короткое окно, но сама metadata-ветка
            // может догреться дольше и положить результат в кэш. Таймауты не кэшируются.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(searchMetadataTimeout);

            return await MusicMetadataCacheService.GetOrCreateAsync(
                metadata.Id,
                expanded ? "search_full" : "search",
                VersionedKey((expanded ? "full|" : "base|") + NormalizeSearchKey(query)),
                searchCacheTtl,
                () => metadata.SearchAsync(query, expanded, timeoutCts.Token),
                timeoutCts.Token
            ) ?? new MusicSearchResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new MusicSearchResult();
        }
    }

    public static Task<MusicArtist> GetArtistAsync(string id, string provider = null, CancellationToken cancellationToken = default)
    {
        if (YouTubeMusicSearchSupport.IsChannelArtist(provider, id))
            return YouTubeMusicSearchSupport.GetChannelArtistAsync(id, cancellationToken);

        if (SoundCloudSupport.IsUserArtist(provider, id))
            return SoundCloudSupport.GetUserArtistAsync(id, cancellationToken);

        var metadata = MusicProviderRegistry.GetMetadataProvider(provider);
        return metadata == null
            ? Task.FromResult<MusicArtist>(null)
            : MusicMetadataCacheService.GetOrCreateAsync(
                metadata.Id,
                "artist",
                VersionedKey(id),
                artistCacheTtl,
                () => metadata.GetArtistAsync(id, cancellationToken),
                cancellationToken
            );
    }

    public static Task<MusicBrowseSection> GetArtistSectionAsync(string id, string provider = null, string page = null, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (SoundCloudSupport.IsUserSection(provider, id))
            return SoundCloudSupport.GetUserSectionAsync(id, page, limit, cancellationToken);

        return Task.FromResult<MusicBrowseSection>(null);
    }

    public static Task<MusicAlbum> GetAlbumAsync(string id, string provider = null, CancellationToken cancellationToken = default)
    {
        if (YouTubeMusicSearchSupport.IsPlaylistAlbum(provider, id))
            return YouTubeMusicSearchSupport.GetPlaylistAlbumAsync(id, cancellationToken);

        if (SoundCloudSupport.IsUserTracksAlbum(provider, id))
            return SoundCloudSupport.GetUserTracksAlbumAsync(id, cancellationToken);

        if (string.Equals(provider, SoundCloudSupport.DiscoveryProviderId, StringComparison.OrdinalIgnoreCase))
        {
            var discovery = MusicProviderRegistry.DiscoveryProviders
                .FirstOrDefault(i => string.Equals(i.Id, provider, StringComparison.OrdinalIgnoreCase)) as SoundCloudDiscoveryProvider;

            return discovery?.Enabled == true
                ? discovery.GetAlbumAsync(id, cancellationToken)
                : Task.FromResult<MusicAlbum>(null);
        }

        var metadata = MusicProviderRegistry.GetMetadataProvider(provider);
        return metadata == null
            ? Task.FromResult<MusicAlbum>(null)
            : MusicMetadataCacheService.GetOrCreateAsync(
                metadata.Id,
                "album",
                VersionedKey(id),
                albumCacheTtl,
                () => metadata.GetAlbumAsync(id, cancellationToken),
                cancellationToken
            );
    }

    public static Task<MusicTrack> GetTrackAsync(string id, string provider = null, CancellationToken cancellationToken = default)
    {
        if (string.Equals(provider, SoundCloudSupport.DiscoveryProviderId, StringComparison.OrdinalIgnoreCase))
            return SoundCloudSupport.GetTrackAsync(id, cancellationToken);

        if (ShouldSkipMetadataTrackLookup(id, provider))
            return Task.FromResult<MusicTrack>(null);

        var metadata = MusicProviderRegistry.GetMetadataProvider(provider);
        return metadata == null
            ? Task.FromResult<MusicTrack>(null)
            : MusicMetadataCacheService.GetOrCreateAsync(
                metadata.Id,
                "track",
                VersionedKey(id),
                trackCacheTtl,
                () => metadata.GetTrackAsync(id, cancellationToken),
                cancellationToken
            );
    }

    static async Task<List<MusicBrowseSection>> GetBrowseSectionsAsync(CancellationToken cancellationToken)
    {
        var providers = MusicProviderRegistry.DiscoveryProviders.Where(i => i.Enabled).ToList();
        if (providers.Count == 0)
            return new List<MusicBrowseSection>();

        var results = await Task.WhenAll(providers.Select(provider =>
            GetHomeSectionsForResponseAsync(provider, browseSectionLimit, cancellationToken)));

        var sections = new List<MusicBrowseSection>();
        foreach (var providerSections in results)
        {
            if (providerSections != null && providerSections.Count > 0)
                sections.AddRange(providerSections);
        }

        return sections;
    }

    static async Task<List<MusicBrowseSection>> GetSearchSectionsAsync(string query, bool expanded, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicBrowseSection>();

        var tasks = new List<Task<List<MusicBrowseSection>>>();

        if (YouTubeMusicSearchSupport.IsSearchEnabled)
            tasks.Add(SafeGetSearchSectionsAsync(token => BuildYouTubeMusicSearchSectionsAsync(query, expanded, token), cancellationToken));

        if (SoundCloudSupport.IsDiscoveryEnabled)
            tasks.Add(SafeGetSearchSectionsAsync(token => BuildSoundCloudSearchSectionsAsync(query, expanded, token), cancellationToken));

        if (MusicProviderRegistry.AudioProviders.Any(i => i.Enabled && string.Equals(i.Id, SefonSupport.ProviderId, StringComparison.OrdinalIgnoreCase)))
            tasks.Add(SafeGetSearchSectionsAsync(async token => ToSearchSectionList(await BuildSefonSearchSectionAsync(query, expanded, token)), cancellationToken));

        if (tasks.Count == 0)
            return new List<MusicBrowseSection>();

        var results = await Task.WhenAll(tasks);
        return results
            .Where(i => i != null)
            .SelectMany(i => i)
            .Where(HasSearchSectionEntries)
            .ToList();
    }

    static async Task<List<MusicBrowseSection>> BuildYouTubeMusicSearchSectionsAsync(string query, bool expanded, CancellationToken cancellationToken)
    {
        int trackLimit = expanded ? searchSectionFullLimit : searchSectionLimit;
        int sideLimit = expanded ? 18 : 6;
        string searchKey = NormalizeSearchKey(query);

        var tracksTask = MusicMetadataCacheService.GetOrCreateAsync(
            YouTubeMusicSearchSupport.ProviderId,
            "search_tracks",
            VersionedKey($"youtubemusic-tracks|{trackLimit}|{searchKey}"),
            searchCacheTtl,
            () => YouTubeMusicSearchSupport.SearchTracksByQueryAsync(query, trackLimit, cancellationToken),
            cancellationToken
        );

        var playlistsTask = MusicMetadataCacheService.GetOrCreateAsync(
            YouTubeMusicSearchSupport.ProviderId,
            "search_playlists",
            VersionedKey($"youtubemusic-playlists|{sideLimit}|{searchKey}"),
            searchCacheTtl,
            () => YouTubeMusicSearchSupport.SearchPlaylistsByQueryAsync(query, sideLimit, cancellationToken),
            cancellationToken
        );

        var artistsTask = MusicMetadataCacheService.GetOrCreateAsync(
            YouTubeMusicSearchSupport.ProviderId,
            "search_artists",
            VersionedKey($"youtubemusic-artists|{sideLimit}|{searchKey}"),
            searchCacheTtl,
            () => YouTubeMusicSearchSupport.SearchArtistsByQueryAsync(query, sideLimit, cancellationToken),
            cancellationToken
        );

        await Task.WhenAll(tracksTask, playlistsTask, artistsTask);

        var tracks = await tracksTask ?? new List<MusicTrack>();
        var playlists = await playlistsTask ?? new List<MusicAlbum>();
        var artists = await artistsTask ?? new List<MusicArtist>();
        var sections = new List<MusicBrowseSection>();

        if (tracks.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = YouTubeMusicSearchSupport.TracksSectionId,
                title = "Треки",
                type = "tracks",
                source_provider = YouTubeMusicSearchSupport.ProviderId,
                has_more = !expanded && tracks.Count >= trackLimit,
                tracks = tracks
            });
        }

        if (playlists.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = YouTubeMusicSearchSupport.PlaylistsSectionId,
                title = "Плейлисты / альбомы",
                type = "albums",
                source_provider = YouTubeMusicSearchSupport.ProviderId,
                has_more = !expanded && playlists.Count >= sideLimit,
                albums = playlists
            });
        }

        if (artists.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = YouTubeMusicSearchSupport.ArtistsSectionId,
                title = "Исполнители / каналы",
                type = "artists",
                source_provider = YouTubeMusicSearchSupport.ProviderId,
                has_more = !expanded && artists.Count >= sideLimit,
                artists = artists
            });
        }

        return sections;
    }

    static bool ShouldSkipMetadataTrackLookup(string id, string provider)
    {
        if (StartsWithAny(id, "inline:", "youtube:", "sefon:", "soundcloud:"))
            return true;

        return !string.IsNullOrWhiteSpace(provider)
            && MusicProviderRegistry.AudioProviders.Any(i => string.Equals(i.Id, provider, StringComparison.OrdinalIgnoreCase));
    }

    static bool StartsWithAny(string value, params string[] prefixes)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    static async Task<List<MusicBrowseSection>> GetHomeSectionsForResponseAsync(IMusicDiscoveryProvider provider, int limit, CancellationToken cancellationToken)
    {
        var warmTask = GetOrStartHomeWarmTask(provider, limit);
        if (warmTask.IsCompleted)
            return await warmTask;

        var completed = await Task.WhenAny(warmTask, Task.Delay(discoveryHomeProviderResponseBudget, cancellationToken));
        if (completed == warmTask)
            return await warmTask;

        cancellationToken.ThrowIfCancellationRequested();
        return new List<MusicBrowseSection>();
    }

    static Task<List<MusicBrowseSection>> GetOrStartHomeWarmTask(IMusicDiscoveryProvider provider, int limit)
    {
        string key = $"{provider.Id}:{limit}";

        lock (homeWarmLock)
        {
            if (homeWarmTasks.TryGetValue(key, out var running) && !running.IsCompleted)
                return running;

            var task = SafeGetHomeSectionsAsync(provider, limit);
            homeWarmTasks[key] = task;
            _ = task.ContinueWith(completed =>
            {
                lock (homeWarmLock)
                {
                    if (homeWarmTasks.TryGetValue(key, out var current) && ReferenceEquals(current, completed))
                        homeWarmTasks.Remove(key);
                }

                if (completed.IsFaulted)
                    _ = completed.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return task;
        }
    }

    static async Task<List<MusicBrowseSection>> SafeGetHomeSectionsAsync(IMusicDiscoveryProvider provider, int limit)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(discoveryHomeProviderWarmTimeout);
            return await provider.GetHomeSectionsAsync(limit, timeoutCts.Token) ?? new List<MusicBrowseSection>();
        }
        catch (OperationCanceledException)
        {
            return new List<MusicBrowseSection>();
        }
        catch
        {
            return new List<MusicBrowseSection>();
        }
    }

    static async Task<MusicBrowseSection> SafeGetSectionAsync(IMusicDiscoveryProvider provider, string sectionId, int limit, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(discoverySectionProviderTimeout);
            return await provider.GetSectionAsync(sectionId, limit, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    static async Task<MusicBrowseSection> SafeGetSearchSectionAsync(Func<CancellationToken, Task<MusicBrowseSection>> factory, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(searchSectionProviderTimeout);
            return await factory(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    static async Task<List<MusicBrowseSection>> SafeGetSearchSectionsAsync(Func<CancellationToken, Task<List<MusicBrowseSection>>> factory, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(searchSectionProviderTimeout);
            return await factory(timeoutCts.Token) ?? new List<MusicBrowseSection>();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new List<MusicBrowseSection>();
        }
        catch
        {
            return new List<MusicBrowseSection>();
        }
    }

    static List<MusicBrowseSection> ToSearchSectionList(MusicBrowseSection section)
    {
        return section == null
            ? new List<MusicBrowseSection>()
            : new List<MusicBrowseSection> { section };
    }

    static async Task<List<MusicBrowseSection>> BuildSoundCloudSearchSectionsAsync(string query, bool expanded, CancellationToken cancellationToken)
    {
        int trackLimit = expanded ? searchSectionFullLimit : searchSectionLimit;
        int sideLimit = expanded ? 18 : 6;
        string searchKey = NormalizeSearchKey(query);

        var tracksTask = MusicMetadataCacheService.GetOrCreateAsync(
            SoundCloudSupport.DiscoveryProviderId,
            "search_tracks",
            VersionedKey($"soundcloud-tracks|{trackLimit}|{searchKey}"),
            searchCacheTtl,
            () => SoundCloudSupport.SearchTracksByQueryAsync(query, trackLimit, cancellationToken),
            cancellationToken
        );

        var playlistsTask = MusicMetadataCacheService.GetOrCreateAsync(
            SoundCloudSupport.DiscoveryProviderId,
            "search_playlists",
            VersionedKey($"soundcloud-playlists|{sideLimit}|{searchKey}"),
            searchCacheTtl,
            () => SoundCloudSupport.SearchPlaylistsByQueryAsync(query, sideLimit, cancellationToken),
            cancellationToken
        );

        var albumsTask = MusicMetadataCacheService.GetOrCreateAsync(
            SoundCloudSupport.DiscoveryProviderId,
            "search_albums",
            VersionedKey($"soundcloud-albums|{sideLimit}|{searchKey}"),
            searchCacheTtl,
            () => SoundCloudSupport.SearchAlbumsByQueryAsync(query, sideLimit, cancellationToken),
            cancellationToken
        );

        var artistsTask = MusicMetadataCacheService.GetOrCreateAsync(
            SoundCloudSupport.DiscoveryProviderId,
            "search_artists",
            VersionedKey($"soundcloud-artists|{sideLimit}|{searchKey}"),
            searchCacheTtl,
            () => SoundCloudSupport.SearchArtistsByQueryAsync(query, sideLimit, cancellationToken),
            cancellationToken
        );

        await Task.WhenAll(tracksTask, albumsTask, playlistsTask, artistsTask);

        var tracks = await tracksTask ?? new List<MusicTrack>();
        var albums = await albumsTask ?? new List<MusicAlbum>();
        var playlists = await playlistsTask ?? new List<MusicAlbum>();
        var artists = await artistsTask ?? new List<MusicArtist>();
        var sections = new List<MusicBrowseSection>();

        if (tracks.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = SoundCloudSupport.SearchTracksSectionId,
                title = "Треки",
                type = "tracks",
                source_provider = SoundCloudSupport.DiscoveryProviderId,
                has_more = !expanded && tracks.Count >= trackLimit,
                tracks = tracks
            });
        }

        if (albums.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = SoundCloudSupport.SearchAlbumsSectionId,
                title = "Альбомы",
                type = "albums",
                source_provider = SoundCloudSupport.DiscoveryProviderId,
                has_more = !expanded && albums.Count >= sideLimit,
                albums = albums
            });
        }

        if (playlists.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = SoundCloudSupport.SearchPlaylistsSectionId,
                title = "Плейлисты",
                type = "albums",
                source_provider = SoundCloudSupport.DiscoveryProviderId,
                has_more = !expanded && playlists.Count >= sideLimit,
                albums = playlists
            });
        }

        if (artists.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = SoundCloudSupport.SearchArtistsSectionId,
                title = "Исполнители",
                type = "artists",
                source_provider = SoundCloudSupport.DiscoveryProviderId,
                has_more = !expanded && artists.Count >= sideLimit,
                artists = artists
            });
        }

        return sections;
    }

    static async Task<MusicBrowseSection> BuildSefonSearchSectionAsync(string query, bool expanded, CancellationToken cancellationToken)
    {
        int limit = expanded ? searchSectionFullLimit : searchSectionLimit;
        var tracks = await MusicMetadataCacheService.GetOrCreateAsync(
            SefonSupport.ProviderId,
            "search_tracks",
            VersionedKey($"sefon|{limit}|{NormalizeSearchKey(query)}"),
            searchCacheTtl,
            () => SefonSupport.SearchTracksByQueryAsync(query, limit, cancellationToken),
            cancellationToken
        ) ?? new List<MusicTrack>();

        return tracks.Count == 0
            ? null
            : new MusicBrowseSection
            {
                id = "search:sefon",
                title = "Sefon",
                type = "tracks",
                source_provider = SefonSupport.ProviderId,
                has_more = !expanded && tracks.Count >= limit,
                tracks = tracks
            };
    }

    static bool HasSearchSectionEntries(MusicBrowseSection section)
    {
        return section != null
            && ((section.tracks?.Count > 0) || (section.albums?.Count > 0) || (section.artists?.Count > 0));
    }

    static string NormalizeSearchKey(string query) => query.Trim().ToLowerInvariant();
    static string VersionedKey(string value) => $"{metadataCacheVersion}|{value}";
}
