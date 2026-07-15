namespace Music;

public class Z3FmAudioProvider : IMusicAudioProvider
{
    public string Id => Z3FmSupport.AudioProviderId;
    public string Name => "Z3.FM";
    public bool Enabled => Z3FmSupport.IsAudioEnabled;
    public bool RequiresAuth => false;
    public bool CacheMissingMatches => false;

    public Task<IReadOnlyList<MusicAudioMatch>> MatchTrackAsync(MusicTrack track, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (!Enabled || track == null)
            return Task.FromResult<IReadOnlyList<MusicAudioMatch>>(Array.Empty<MusicAudioMatch>());

        return Z3FmSupport.SearchAsync(track, cancellationToken);
    }

    public Task<IReadOnlyList<MusicPlaybackSource>> GetStreamsAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (!Enabled || match == null)
            return Task.FromResult<IReadOnlyList<MusicPlaybackSource>>(Array.Empty<MusicPlaybackSource>());

        return Z3FmSupport.BuildSourcesAsync(match, cancellationToken);
    }

    public Task<MusicPlaybackSource> TryGetPreferredStreamAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<MusicPlaybackSource>(null);
    }

    public Task<IReadOnlyList<MusicAudioMatch>> SearchMatchesByQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MusicAudioMatch>>(Array.Empty<MusicAudioMatch>());
    }

    public bool IsRelevantMatch(MusicTrack track, MusicAudioMatch match)
    {
        return Z3FmSupport.IsRelevantMatch(track, match);
    }

    public bool ShouldValidatePinnedMatch(MusicTrack track, MusicAudioMatch match)
    {
        return false;
    }

    public IReadOnlyList<string> GetFallbackProviderIds(MusicTrack track)
    {
        return Array.Empty<string>();
    }
}
