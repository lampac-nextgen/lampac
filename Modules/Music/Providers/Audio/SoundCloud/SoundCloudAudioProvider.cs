namespace Music;

public class SoundCloudAudioProvider : IMusicAudioProvider
{
    public string Id => SoundCloudSupport.AudioProviderId;
    public string Name => "SoundCloud";
    public bool Enabled => SoundCloudSupport.IsAudioEnabled;
    public bool RequiresAuth => false;
    public bool CacheMissingMatches => false;

    public async Task<IReadOnlyList<MusicAudioMatch>> MatchTrackAsync(MusicTrack track, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (track == null || MusicPlaybackModeService.IsVideo(playbackMode))
            return Array.Empty<MusicAudioMatch>();

        try
        {
            return await SoundCloudSupport.SearchAudioAsync(track, cancellationToken);
        }
        catch
        {
            return Array.Empty<MusicAudioMatch>();
        }
    }

    public async Task<IReadOnlyList<MusicPlaybackSource>> GetStreamsAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (match == null || MusicPlaybackModeService.IsVideo(playbackMode))
            return Array.Empty<MusicPlaybackSource>();

        try
        {
            return await SoundCloudSupport.BuildAudioSourcesAsync(match, cancellationToken);
        }
        catch
        {
            return Array.Empty<MusicPlaybackSource>();
        }
    }

    public async Task<MusicPlaybackSource> TryGetPreferredStreamAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (match == null || MusicPlaybackModeService.IsVideo(playbackMode))
            return null;

        try
        {
            return await SoundCloudSupport.BuildPreferredAudioSourceAsync(match, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public Task<IReadOnlyList<MusicAudioMatch>> SearchMatchesByQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MusicAudioMatch>>(Array.Empty<MusicAudioMatch>());
    }

    public bool IsRelevantMatch(MusicTrack track, MusicAudioMatch match)
    {
        return SoundCloudSupport.IsRelevantAudioMatch(track, match);
    }

    public bool ShouldValidatePinnedMatch(MusicTrack track, MusicAudioMatch match)
    {
        return SoundCloudSupport.HasExactTrackId(track);
    }

    public IReadOnlyList<string> GetFallbackProviderIds(MusicTrack track)
    {
        return SoundCloudSupport.HasExactTrackId(track)
            ? new[] { YouTubeMusicSearchSupport.ProviderId }
            : Array.Empty<string>();
    }
}
