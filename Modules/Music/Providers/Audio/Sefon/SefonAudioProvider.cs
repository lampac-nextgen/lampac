namespace Music;

public class SefonAudioProvider : IMusicAudioProvider
{
    public string Id => SefonSupport.ProviderId;
    public string Name => "Sefon";
    public bool Enabled => ModInit.conf?.sefon_audio_enabled == true;
    public bool RequiresAuth => false;
    public bool CacheMissingMatches => true;

    public async Task<IReadOnlyList<MusicAudioMatch>> MatchTrackAsync(MusicTrack track, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (track == null || MusicPlaybackModeService.IsVideo(playbackMode))
            return Array.Empty<MusicAudioMatch>();

        try
        {
            return await SefonSupport.SearchAsync(track, cancellationToken);
        }
        catch
        {
            return Array.Empty<MusicAudioMatch>();
        }
    }

    public Task<IReadOnlyList<MusicPlaybackSource>> GetStreamsAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (match == null || MusicPlaybackModeService.IsVideo(playbackMode))
            return Task.FromResult<IReadOnlyList<MusicPlaybackSource>>(Array.Empty<MusicPlaybackSource>());

        return Task.FromResult(SefonSupport.BuildSources(match));
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
        return SefonSupport.IsRelevantMatch(track, match);
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
