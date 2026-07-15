namespace Music;

public interface IMusicMetadataProvider
{
    string Id { get; }
    string Name { get; }
    bool Enabled { get; }

    Task<MusicSearchResult> SearchAsync(string query, bool expanded = false, CancellationToken cancellationToken = default);
    Task<MusicArtist> GetArtistAsync(string id, CancellationToken cancellationToken = default);
    Task<MusicAlbum> GetAlbumAsync(string id, CancellationToken cancellationToken = default);
    Task<MusicTrack> GetTrackAsync(string id, CancellationToken cancellationToken = default);
}

public interface IMusicDiscoveryProvider
{
    string Id { get; }
    string Name { get; }
    bool Enabled { get; }

    Task<List<MusicBrowseSection>> GetHomeSectionsAsync(int limit, CancellationToken cancellationToken = default);
    Task<MusicBrowseSection> GetSectionAsync(string sectionId, int limit, CancellationToken cancellationToken = default);
}

public interface IMusicAudioProvider
{
    string Id { get; }
    string Name { get; }
    bool Enabled { get; }
    bool RequiresAuth { get; }
    bool CacheMissingMatches { get; }

    Task<IReadOnlyList<MusicAudioMatch>> MatchTrackAsync(MusicTrack track, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MusicAudioMatch>> SearchMatchesByQueryAsync(string query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MusicPlaybackSource>> GetStreamsAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default);
    Task<MusicPlaybackSource> TryGetPreferredStreamAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default);
    bool IsRelevantMatch(MusicTrack track, MusicAudioMatch match);
    bool ShouldValidatePinnedMatch(MusicTrack track, MusicAudioMatch match);
    IReadOnlyList<string> GetFallbackProviderIds(MusicTrack track);
}

public interface IMusicAuthProvider
{
    string Id { get; }
    string Name { get; }
    bool Enabled { get; }

    Task<MusicAuthState> GetStateAsync(string profileId = null, CancellationToken cancellationToken = default);
    Task<bool> SaveAsync(string payload, string profileId = null, CancellationToken cancellationToken = default);
    Task LogoutAsync(string profileId = null, CancellationToken cancellationToken = default);
}
