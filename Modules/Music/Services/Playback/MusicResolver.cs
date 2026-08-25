using System.Collections.Concurrent;

namespace Music;

public static class MusicResolver
{
    // Кэш stream-sources (только youtubeaudio): манифест YouTube стоил ~1-1.5s
    // на КАЖДЫЙ тёплый /music/play, хотя googlevideo-ссылки живут часами.
    // TTL 30min — с запасом короче срока жизни ссылок. У Sefon/SoundCloud свои
    // (короткие/сессионные) сроки жизни — их не кэшируем. Ошибки и пустые
    // списки не кэшируются. Хранится и отдаётся ГЛУБОКИМИ КОПИЯМИ:
    // PrepareStreamSources мутирует source.url под хост запроса — общий
    // инстанс запёк бы хост первого клиента (урок отравления image-proxy).
    // Инвалидация: refreshSources=true (пере-резолв из /music/stream после
    // смерти upstream-ссылки в relay) пропускает чтение и перезаписывает свежим.
    static readonly TimeSpan sourcesCacheTtl = TimeSpan.FromMinutes(30);
    const int sourcesCacheCapacity = 512;
    static readonly ConcurrentDictionary<string, (DateTime at, List<MusicPlaybackSource> sources)> sourcesCache = new(StringComparer.Ordinal);
    // кап жёсткий: чтение lock-free, но trim + вставка под общим lock, иначе
    // параллельные запросы одновременно пройдут проверку Count и вместе её пробьют
    static readonly object sourcesCacheWriteLock = new();

    public static async Task<MusicPlayResponse> ResolveTrackAsync(MusicTrack track, string provider = null, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default, bool refreshSources = false)
    {
        return await ResolveTrackCoreAsync(track, provider, playbackMode, profileId, preferSingleSource: false, refreshSources, cancellationToken);
    }

    public static async Task<MusicPlayResponse> ResolvePreferredTrackAsync(MusicTrack track, string provider = null, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        return await ResolveTrackCoreAsync(track, provider, playbackMode, profileId, preferSingleSource: true, refreshSources: false, cancellationToken);
    }

    static async Task<MusicPlayResponse> ResolveTrackCoreAsync(MusicTrack track, string provider, string playbackMode, string profileId, bool preferSingleSource, bool refreshSources, CancellationToken cancellationToken)
    {
        if (track == null)
        {
            return new MusicPlayResponse
            {
                available = false,
                message = "Track not found."
            };
        }

        var requestedProviders = GetProviderOrder(provider);
        var requestedProviderIds = requestedProviders
            .Select(i => i.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var providers = ExpandProviderOrder(track, requestedProviders);
        if (providers.Count == 0)
        {
            return new MusicPlayResponse
            {
                available = false,
                message = "Audio provider not found.",
                track_id = track.id
            };
        }

        MusicAudioMatch firstMatch = null;

        foreach (var sourceProvider in providers)
        {
            var providerTrack = requestedProviderIds.Contains(sourceProvider.Id)
                ? track
                : BuildFallbackSearchTrack(track);

            if (sourceProvider.CacheMissingMatches && await MusicSourceMatchService.IsMarkedMissingAsync(track.id, sourceProvider.Id, playbackMode, cancellationToken))
                continue;

            var selectedMatch = await MusicSourceMatchService.GetAsync(track.id, sourceProvider.Id, playbackMode, cancellationToken);
            selectedMatch = NormalizeSelectedMatch(providerTrack, sourceProvider, selectedMatch);

            if (selectedMatch != null)
            {
                if (firstMatch == null)
                    firstMatch = selectedMatch;

                var selectedSources = await ResolveSourcesAsync(sourceProvider, selectedMatch, preferSingleSource, playbackMode, profileId, refreshSources, cancellationToken);
                if (selectedSources.Count > 0)
                {
                    return new MusicPlayResponse
                    {
                        available = true,
                        message = "ok",
                        track_id = track.id,
                        selected_match = selectedMatch,
                        sources = selectedSources.ToList()
                    };
                }
            }

            var matches = await GetOrderedMatchesAsync(providerTrack, sourceProvider, selectedMatch, playbackMode, profileId, cancellationToken);

            foreach (var match in matches)
            {
                if (selectedMatch != null && match.id == selectedMatch.id)
                    continue;

                if (firstMatch == null)
                    firstMatch = match;

                var sources = await ResolveSourcesAsync(sourceProvider, match, preferSingleSource, playbackMode, profileId, refreshSources, cancellationToken);
                if (sources.Count == 0)
                    continue;

                // временный сбой источников у пинованного матча не должен
                // стирать ручной выбор — играем fallback без перезаписи
                if (selectedMatch == null || !selectedMatch.pinned)
                    await MusicSourceMatchService.SaveAsync(track.id, match, playbackMode, cancellationToken);

                return new MusicPlayResponse
                {
                    available = true,
                    message = "ok",
                    track_id = track.id,
                    selected_match = match,
                    sources = sources.ToList()
                };
            }

            if (sourceProvider.CacheMissingMatches)
                await MusicSourceMatchService.MarkMissingAsync(track.id, sourceProvider.Id, playbackMode, cancellationToken);
        }

        return new MusicPlayResponse
        {
            available = false,
            message = "No audio source resolved yet.",
            track_id = track.id,
            selected_match = firstMatch,
            sources = new List<MusicPlaybackSource>()
        };
    }

    static async Task<List<MusicPlaybackSource>> ResolveSourcesAsync(IMusicAudioProvider provider, MusicAudioMatch match, bool preferSingleSource, string playbackMode, string profileId, bool refreshSources, CancellationToken cancellationToken)
    {
        if (provider == null || match == null)
            return new List<MusicPlaybackSource>();

        if (preferSingleSource)
        {
            var preferred = await provider.TryGetPreferredStreamAsync(match, playbackMode, profileId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(preferred?.url))
            {
                var preferredSources = new List<MusicPlaybackSource> { preferred };
                MusicProxyService.ApplyStreamProxy(provider.Id, preferredSources);
                return preferredSources;
            }
        }

        bool cacheable = string.Equals(provider.Id, "youtubeaudio", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(match.id);
        string cacheKey = cacheable
            ? BuildSourcesCacheKey(
                provider.Id,
                match.id,
                playbackMode,
                MusicProxyService.CurrentRouteKey(provider.Id, MusicProxyPurpose.Stream)
            )
            : null;

        if (cacheable && !refreshSources
            && sourcesCache.TryGetValue(cacheKey, out var entry)
            && DateTime.UtcNow - entry.at < sourcesCacheTtl)
            return CloneSources(entry.sources);

        var sources = await provider.GetStreamsAsync(match, playbackMode, profileId, cancellationToken);
        var result = sources?
            .Where(source => !string.IsNullOrWhiteSpace(source?.url))
            .ToList() ?? new List<MusicPlaybackSource>();

        MusicProxyService.ApplyStreamProxy(provider.Id, result);

        if (cacheable && result.Count > 0)
        {
            string resultCacheKey = BuildSourcesCacheKey(
                provider.Id,
                match.id,
                playbackMode,
                MusicProxyService.SourceRouteKey(result[0])
            );

            lock (sourcesCacheWriteLock)
            {
                if (!sourcesCache.ContainsKey(resultCacheKey) && sourcesCache.Count >= sourcesCacheCapacity)
                {
                    // сначала протухшие; если их нет — вытесняем старейшие,
                    // иначе при равномерном трафике свежие записи переполнят кап
                    foreach (var stale in sourcesCache.Where(i => DateTime.UtcNow - i.Value.at >= sourcesCacheTtl).Select(i => i.Key).ToList())
                        sourcesCache.TryRemove(stale, out _);

                    while (sourcesCache.Count >= sourcesCacheCapacity)
                    {
                        var oldest = sourcesCache.OrderBy(i => i.Value.at).FirstOrDefault();
                        if (oldest.Key == null || !sourcesCache.TryRemove(oldest.Key, out _))
                            break;
                    }
                }

                sourcesCache[resultCacheKey] = (DateTime.UtcNow, CloneSources(result));
            }
        }

        return result;
    }

    static string BuildSourcesCacheKey(string providerId, string matchId, string playbackMode, string routeKey)
        => $"{providerId}|{matchId}|{playbackMode ?? string.Empty}|route:{routeKey}|cfg:{MusicProxyService.ConfigurationVersion}";

    static List<MusicPlaybackSource> CloneSources(List<MusicPlaybackSource> sources)
    {
        return sources.Select(source => new MusicPlaybackSource
        {
            provider_id = source.provider_id,
            url = source.url,
            external_url = source.external_url,
            mime_type = source.mime_type,
            bitrate = source.bitrate,
            quality = source.quality,
            headers = source.headers != null ? new Dictionary<string, string>(source.headers) : new(),
            proxy_url = source.proxy_url,
            proxy_username = source.proxy_username,
            proxy_password = source.proxy_password,
            proxy_scope = source.proxy_scope
        }).ToList();
    }

    public static async Task<MusicMatchesResponse> GetMatchesAsync(MusicTrack track, string provider = null, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (track == null)
        {
            return new MusicMatchesResponse
            {
                available = false,
                message = "Track not found."
            };
        }

        var providers = GetProviderOrder(provider);
        if (providers.Count == 0)
        {
            return new MusicMatchesResponse
            {
                available = false,
                message = "Audio provider not found.",
                track_id = track.id
            };
        }

        foreach (var sourceProvider in providers)
        {
            var selectedMatch = await MusicSourceMatchService.GetAsync(track.id, sourceProvider.Id, playbackMode, cancellationToken);
            selectedMatch = NormalizeSelectedMatch(track, sourceProvider, selectedMatch);
            var matches = await GetOrderedMatchesAsync(track, sourceProvider, selectedMatch, playbackMode, profileId, cancellationToken);

            if (matches.Count == 0)
                continue;

            return new MusicMatchesResponse
            {
                available = true,
                message = "ok",
                track_id = track.id,
                selected_match = selectedMatch ?? matches.First(),
                matches = matches.ToList()
            };
        }

        return new MusicMatchesResponse
        {
            available = false,
            message = "No audio matches resolved yet.",
            track_id = track.id
        };
    }

    // «Найти вручную»: матчи по сырому пользовательскому запросу вместо
    // метаданных трека — последний рубеж для совсем битых метаданных
    public static async Task<MusicMatchesResponse> GetMatchesByQueryAsync(MusicTrack track, string provider, string searchQuery, string playbackMode = null, CancellationToken cancellationToken = default)
    {
        if (track == null || string.IsNullOrWhiteSpace(searchQuery))
        {
            return new MusicMatchesResponse
            {
                available = false,
                message = "Query is empty.",
                track_id = track?.id
            };
        }

        foreach (var sourceProvider in GetProviderOrder(provider))
        {
            var matches = await SearchMatchesByQueryAsync(sourceProvider, searchQuery, cancellationToken);
            if (matches.Count == 0)
                continue;

            var selectedMatch = await MusicSourceMatchService.GetAsync(track.id, sourceProvider.Id, playbackMode, cancellationToken);
            selectedMatch = NormalizeSelectedMatch(track, sourceProvider, selectedMatch);

            return new MusicMatchesResponse
            {
                available = true,
                message = "ok",
                track_id = track.id,
                selected_match = selectedMatch,
                matches = matches.ToList()
            };
        }

        return new MusicMatchesResponse
        {
            available = false,
            message = "No matches for query.",
            track_id = track.id
        };
    }

    static async Task<IReadOnlyList<MusicAudioMatch>> SearchMatchesByQueryAsync(IMusicAudioProvider provider, string searchQuery, CancellationToken cancellationToken)
    {
        return provider == null || !provider.Enabled
            ? Array.Empty<MusicAudioMatch>()
            : await provider.SearchMatchesByQueryAsync(searchQuery, cancellationToken);
    }

    public static async Task<bool> SelectMatchAsync(MusicTrack track, string provider, string matchId, string playbackMode = null, string profileId = null, string searchQuery = null, CancellationToken cancellationToken = default)
    {
        if (track == null || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(matchId))
            return false;

        var sourceProvider = MusicProviderRegistry.GetAudioProvider(provider);
        if (sourceProvider == null || !sourceProvider.Enabled)
            return false;

        IReadOnlyList<MusicAudioMatch> matches;

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // кандидат из ручного поиска валидируется тем же запросом
            matches = await SearchMatchesByQueryAsync(sourceProvider, searchQuery, cancellationToken);
        }
        else
        {
            var selectedMatch = await MusicSourceMatchService.GetAsync(track.id, sourceProvider.Id, playbackMode, cancellationToken);
            selectedMatch = NormalizeSelectedMatch(track, sourceProvider, selectedMatch);
            matches = await GetOrderedMatchesAsync(track, sourceProvider, selectedMatch, playbackMode, profileId, cancellationToken);
        }

        var match = matches.FirstOrDefault(i => i.id == matchId && i.provider_id == provider);

        if (match == null)
            return false;

        match.pinned = true;
        return await MusicSourceMatchService.SaveAsync(track.id, match, playbackMode, cancellationToken);
    }

    static List<IMusicAudioProvider> GetProviderOrder(string provider)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var selected = MusicProviderRegistry.GetAudioProvider(provider);
            return selected == null ? new List<IMusicAudioProvider>() : new List<IMusicAudioProvider> { selected };
        }

        var ordered = new List<IMusicAudioProvider>();
        var preferred = MusicProviderRegistry.GetAudioProvider();

        if (preferred != null)
            ordered.Add(preferred);

        ordered.AddRange(MusicProviderRegistry.AudioProviders.Where(i => i.Enabled && ordered.All(x => x.Id != i.Id)));
        return ordered;
    }

    static List<IMusicAudioProvider> ExpandProviderOrder(MusicTrack track, List<IMusicAudioProvider> providers)
    {
        if (track == null || providers == null || providers.Count == 0)
            return providers;

        var expanded = providers.ToList();
        foreach (var provider in providers)
        {
            foreach (var fallbackId in provider.GetFallbackProviderIds(track) ?? Array.Empty<string>())
            {
                var fallback = MusicProviderRegistry.GetAudioProvider(fallbackId);
                if (fallback == null || !fallback.Enabled || expanded.Any(i => string.Equals(i.Id, fallback.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;

                expanded.Add(fallback);
            }
        }

        return expanded;
    }

    static MusicTrack BuildFallbackSearchTrack(MusicTrack track)
    {
        if (track == null)
            return null;

        return new MusicTrack
        {
            title = track.title,
            artist_id = track.artist_id,
            artist_name = track.artist_name,
            artists = track.artists?.ToList() ?? new List<string>(),
            album_id = track.album_id,
            album_title = track.album_title,
            duration_ms = track.duration_ms,
            track_number = track.track_number,
            disc_number = track.disc_number,
            date = track.date,
            search_score = track.search_score,
            images = track.images?.ToList() ?? new List<MusicImage>()
        };
    }

    static async Task<IReadOnlyList<MusicAudioMatch>> GetOrderedMatchesAsync(MusicTrack track, IMusicAudioProvider provider, MusicAudioMatch selectedMatch, string playbackMode, string profileId, CancellationToken cancellationToken)
    {
        selectedMatch = NormalizeSelectedMatch(track, provider, selectedMatch);

        var matches = (await provider.MatchTrackAsync(track, playbackMode, profileId, cancellationToken)).ToList();

        if (selectedMatch == null)
            return matches;

        var ordered = new List<MusicAudioMatch> { selectedMatch };
        ordered.AddRange(matches.Where(i => i.id != selectedMatch.id));
        return ordered;
    }

    static MusicAudioMatch NormalizeSelectedMatch(MusicTrack track, IMusicAudioProvider provider, MusicAudioMatch selectedMatch)
    {
        if (selectedMatch == null)
            return null;

        bool relevant = provider?.IsRelevantMatch(track, selectedMatch) == true;
        if (!relevant && provider?.ShouldValidatePinnedMatch(track, selectedMatch) == true)
            return null;

        // ручной выбор пользователя авторитетен: эвристики релевантности написаны
        // против протухших АВТО-матчей, а здесь метаданные трека могут быть битыми
        // (пользователь ровно поэтому и выбирал источник вручную)
        if (selectedMatch.pinned)
            return selectedMatch;

        return relevant ? selectedMatch : null;
    }
}
