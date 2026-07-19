using System.Text;

namespace Music;

public sealed class MusicPlaylistRequest
{
    public string ids { get; set; }
    public string provider { get; set; }
    public string audio_provider { get; set; }
    public string stream_mode { get; set; }
    public string playback_mode { get; set; }
    public string playlist_strategy { get; set; }
    public string profile_id { get; set; }
    public string host { get; set; }
    public string uid { get; set; }
    public string account_email { get; set; }
    public CancellationToken cancellation_token { get; set; }
}

public static class MusicPlaylistService
{
    public static async Task<string> BuildAsync(MusicPlaylistRequest request)
    {
        request ??= new MusicPlaylistRequest();

        if (string.IsNullOrWhiteSpace(request.ids))
            return string.Empty;

        var trackIds = request.ids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        if (trackIds.Length == 0)
            return string.Empty;

        var tracks = await ResolvePlaylistTracksAsync(trackIds, request);

        if (string.Equals(request.audio_provider, SefonSupport.ProviderId, StringComparison.OrdinalIgnoreCase))
            return await BuildResolvedExternalPlaylistAsync(trackIds, tracks, request);

        if (string.Equals(request.audio_provider, SoundCloudSupport.AudioProviderId, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(request.playlist_strategy, "resolved", StringComparison.OrdinalIgnoreCase))
                return await BuildResolvedExternalPlaylistAsync(trackIds, tracks, request);

            return await BuildHybridSoundCloudExternalPlaylistAsync(trackIds, tracks, request);
        }

        return BuildInternalPlaylist(trackIds, tracks, request);
    }

    static async Task<MusicTrack[]> ResolvePlaylistTracksAsync(string[] trackIds, MusicPlaylistRequest request)
    {
        const int metadataResolveParallelism = 6;
        var tracks = new MusicTrack[trackIds.Length];
        var historyTracks = await MusicPlaybackHistoryService.GetTracksAsync(request.profile_id, trackIds, request.cancellation_token);

        for (int i = 0; i < trackIds.Length; i++)
        {
            if (historyTracks.TryGetValue(trackIds[i], out var historyTrack))
                tracks[i] = historyTrack;
        }

        if (string.IsNullOrWhiteSpace(request.provider))
            return tracks;

        using var resolveGate = new SemaphoreSlim(metadataResolveParallelism);
        var resolveTasks = new List<Task>();

        async Task ResolveTrackAtAsync(int index)
        {
            await resolveGate.WaitAsync(request.cancellation_token);

            try
            {
                tracks[index] = await MusicCatalogService.GetTrackAsync(trackIds[index], request.provider, request.cancellation_token);
            }
            finally
            {
                resolveGate.Release();
            }
        }

        for (int i = 0; i < trackIds.Length; i++)
        {
            if (tracks[i] == null)
                resolveTasks.Add(ResolveTrackAtAsync(i));
        }

        if (resolveTasks.Count > 0)
            await Task.WhenAll(resolveTasks);

        return tracks;
    }

    static string BuildInternalPlaylist(string[] trackIds, MusicTrack[] tracks, MusicPlaylistRequest request)
    {
        var playlist = new StringBuilder();
        playlist.AppendLine("#EXTM3U");

        for (int i = 0; i < trackIds.Length; i++)
        {
            var track = tracks[i];
            string id = trackIds[i];
            int position = i + 1;

            string title = BuildTitle(track, position);
            int seconds = GetSeconds(track);

            playlist.AppendLine($"#EXTINF:{seconds},{EscapePlaylistTitle(title)}");
            string playlistUrl = BuildPlaylistEntryUrl(track, id, request);
            if (!string.IsNullOrWhiteSpace(playlistUrl))
                playlist.AppendLine(playlistUrl);
        }

        return playlist.ToString();
    }

    static async Task<string> BuildHybridSoundCloudExternalPlaylistAsync(string[] trackIds, MusicTrack[] tracks, MusicPlaylistRequest request)
    {
        var playlist = new StringBuilder();
        playlist.AppendLine("#EXTM3U");

        const int leadChunkSize = 6;
        int leadIndex = -1;
        var leadResolvedEntries = new Dictionary<int, PlaylistResolvedEntry>();

        for (int offset = 0; offset < trackIds.Length && leadIndex < 0; offset += leadChunkSize)
        {
            int chunkCount = Math.Min(leadChunkSize, trackIds.Length - offset);
            var tasks = new Task<PlaylistResolvedEntry>[chunkCount];

            for (int chunkOffset = 0; chunkOffset < chunkCount; chunkOffset++)
            {
                int index = offset + chunkOffset;
                var track = tracks[index];
                if (track == null)
                    continue;

                tasks[chunkOffset] = ResolvePreferredPlaylistEntryAsync(index, track, request);
            }

            await Task.WhenAll(tasks.Where(task => task != null));

            for (int chunkOffset = 0; chunkOffset < chunkCount; chunkOffset++)
            {
                int index = offset + chunkOffset;
                var task = tasks[chunkOffset];
                var entry = task?.Result;
                if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
                    continue;

                leadResolvedEntries[index] = entry;
                if (leadIndex < 0)
                    leadIndex = index;
            }
        }

        if (leadIndex < 0)
            return playlist.ToString();

        for (int i = leadIndex; i < trackIds.Length; i++)
        {
            var track = tracks[i];
            string trackId = trackIds[i];
            if (track == null)
                continue;

            if (leadResolvedEntries.TryGetValue(i, out var resolvedEntry) && !string.IsNullOrWhiteSpace(resolvedEntry.Url))
            {
                playlist.AppendLine($"#EXTINF:{resolvedEntry.Seconds},{EscapePlaylistTitle(resolvedEntry.Title)}");
                playlist.AppendLine(resolvedEntry.Url);
                continue;
            }

            string playlistUrl = BuildPlaylistEntryUrl(track, trackId, request);
            if (string.IsNullOrWhiteSpace(playlistUrl))
                continue;

            playlist.AppendLine($"#EXTINF:{GetSeconds(track)},{EscapePlaylistTitle(BuildTitle(track, i + 1))}");
            playlist.AppendLine(playlistUrl);
        }

        return playlist.ToString();
    }

    static async Task<string> BuildResolvedExternalPlaylistAsync(string[] trackIds, MusicTrack[] tracks, MusicPlaylistRequest request)
    {
        var entries = new PlaylistResolvedEntry[trackIds.Length];
        using var concurrency = new SemaphoreSlim(8);

        var tasks = trackIds
            .Select((trackId, index) => ResolveResolvedPlaylistEntryAsync(index, tracks[index], request, entries, concurrency))
            .ToArray();

        await Task.WhenAll(tasks);

        var playlist = new StringBuilder();
        playlist.AppendLine("#EXTM3U");

        foreach (var entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
                continue;

            playlist.AppendLine($"#EXTINF:{entry.Seconds},{EscapePlaylistTitle(entry.Title)}");
            playlist.AppendLine(entry.Url);
        }

        return playlist.ToString();
    }

    static async Task ResolveResolvedPlaylistEntryAsync(int index, MusicTrack track, MusicPlaylistRequest request, PlaylistResolvedEntry[] entries, SemaphoreSlim concurrency)
    {
        await concurrency.WaitAsync(request.cancellation_token);
        try
        {
            if (track == null)
                return;

            var entry = await ResolvePreferredPlaylistEntryAsync(index, track, request);
            if (entry != null)
                entries[index] = entry;
        }
        catch
        {
        }
        finally
        {
            concurrency.Release();
        }
    }

    static async Task<PlaylistResolvedEntry> ResolvePreferredPlaylistEntryAsync(int index, MusicTrack track, MusicPlaylistRequest request)
    {
        if (track == null)
            return null;

        var result = await MusicPlaybackService.ResolvePreferredTrackAsync(track, request.audio_provider, request.stream_mode, request.playback_mode, request.profile_id, request.cancellation_token);
        if (result?.available != true || result.sources == null || result.sources.Count == 0)
            return null;

        var source = MusicPlaybackService.SelectSource(result.sources, null, request.stream_mode);
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return null;

        string url = source.url;
        string resolvedAudioProvider = string.IsNullOrWhiteSpace(source.provider_id) ? request.audio_provider : source.provider_id;

        bool canUseDirectExternalUrl =
            (string.Equals(resolvedAudioProvider, SefonSupport.ProviderId, StringComparison.OrdinalIgnoreCase)
             || string.Equals(resolvedAudioProvider, SoundCloudSupport.AudioProviderId, StringComparison.OrdinalIgnoreCase))
            && (source.headers == null || source.headers.Count == 0);

        if (!canUseDirectExternalUrl)
        {
            MusicPlaybackService.PrepareStreamSources(request.host, track, MusicPlaybackService.ResolveTrackProvider(track, request.provider), resolvedAudioProvider, request.stream_mode, request.playback_mode, result);
            source = MusicPlaybackService.SelectSource(result.sources, null, request.stream_mode);
            if (source == null || string.IsNullOrWhiteSpace(source.url))
                return null;

            url = source.url;
        }

        return new PlaylistResolvedEntry
        {
            Title = BuildTitle(track, index + 1),
            Seconds = GetSeconds(track),
            Url = url
        };
    }

    static string BuildPlaylistEntryUrl(MusicTrack track, string trackId, MusicPlaylistRequest request)
    {
        trackId = string.IsNullOrWhiteSpace(trackId) ? track?.id : trackId.Trim();
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(request.host))
            return null;

        string resolvedProvider = MusicPlaybackService.ResolveTrackProvider(track, request.provider);
        var fallbackTrack = track ?? new MusicTrack { id = trackId };
        if (string.IsNullOrWhiteSpace(fallbackTrack.id))
            fallbackTrack.id = trackId;

        var url = MusicPlaybackService.BuildStreamFallbackParams(
            fallbackTrack,
            resolvedProvider,
            request.audio_provider,
            request.stream_mode,
            request.playback_mode,
            request.uid,
            request.account_email
        );

        return $"{request.host}/music/stream?{string.Join("&", url)}";
    }

    static string BuildTitle(MusicTrack track, int position)
    {
        return string.IsNullOrWhiteSpace(track?.artist_name)
            ? track?.title ?? $"Track {position}"
            : $"{track.artist_name} - {track.title}";
    }

    static int GetSeconds(MusicTrack track)
    {
        return track?.duration_ms.HasValue == true
            ? (int)Math.Max(0, Math.Round(track.duration_ms.Value / 1000d))
            : -1;
    }

    static string EscapePlaylistTitle(string title)
        => (title ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

    sealed class PlaylistResolvedEntry
    {
        public string Title { get; set; }
        public int Seconds { get; set; }
        public string Url { get; set; }
    }
}
