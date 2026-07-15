namespace Music;

public static class MusicResolver
{
    public static async Task<MusicPlayResponse> ResolveTrackAsync(MusicTrack track, string provider = null, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        return await ResolveTrackCoreAsync(track, provider, playbackMode, profileId, preferSingleSource: false, cancellationToken);
    }

    public static async Task<MusicPlayResponse> ResolvePreferredTrackAsync(MusicTrack track, string provider = null, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        return await ResolveTrackCoreAsync(track, provider, playbackMode, profileId, preferSingleSource: true, cancellationToken);
    }

    static async Task<MusicPlayResponse> ResolveTrackCoreAsync(MusicTrack track, string provider, string playbackMode, string profileId, bool preferSingleSource, CancellationToken cancellationToken)
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

                var selectedSources = await ResolveSourcesAsync(sourceProvider, selectedMatch, preferSingleSource, playbackMode, profileId, cancellationToken);
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

                var sources = await ResolveSourcesAsync(sourceProvider, match, preferSingleSource, playbackMode, profileId, cancellationToken);
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

    static async Task<List<MusicPlaybackSource>> ResolveSourcesAsync(IMusicAudioProvider provider, MusicAudioMatch match, bool preferSingleSource, string playbackMode, string profileId, CancellationToken cancellationToken)
    {
        if (provider == null || match == null)
            return new List<MusicPlaybackSource>();

        if (preferSingleSource)
        {
            var preferred = await provider.TryGetPreferredStreamAsync(match, playbackMode, profileId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(preferred?.url))
                return new List<MusicPlaybackSource> { preferred };
        }

        var sources = await provider.GetStreamsAsync(match, playbackMode, profileId, cancellationToken);
        return sources?
            .Where(source => !string.IsNullOrWhiteSpace(source?.url))
            .ToList() ?? new List<MusicPlaybackSource>();
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
