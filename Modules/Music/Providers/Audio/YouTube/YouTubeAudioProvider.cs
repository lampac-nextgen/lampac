using YoutubeExplode;
using YoutubeExplode.Search;

namespace Music;

public class YouTubeAudioProvider : IMusicAudioProvider
{
    static readonly YoutubeClient youtube = new();

    public string Id => "youtubeaudio";
    public string Name => "YouTube Audio";
    public bool Enabled => ModInit.conf?.youtube_audio_enabled == true;
    public bool RequiresAuth => false;
    public bool CacheMissingMatches => false;

    public async Task<IReadOnlyList<MusicAudioMatch>> MatchTrackAsync(MusicTrack track, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (track == null)
            return Array.Empty<MusicAudioMatch>();

        var directMatch = YouTubeAudioSupport.BuildDirectMatch(track);
        if (directMatch != null)
            return new[] { directMatch };

        var queries = YouTubeAudioSupport.BuildTrackQueries(track, playbackMode);
        if (queries.Count == 0)
            return Array.Empty<MusicAudioMatch>();

        try
        {
            var results = new List<VideoSearchResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var query in queries)
            {
                int queryCount = 0;

                await foreach (var video in youtube.Search.GetVideosAsync(query, cancellationToken))
                {
                    var videoId = video.Id.Value;
                    if (!string.IsNullOrWhiteSpace(videoId) && seen.Add(videoId))
                        results.Add(video);

                    queryCount++;
                    if (queryCount >= 16 || results.Count >= 36)
                        break;
                }

                if (results.Count >= 36)
                    break;
            }

            var matches = YouTubeAudioSupport.ConvertSearchResults(results);
            return YouTubeAudioSupport.RankMatches(track, matches, playbackMode);
        }
        catch
        {
            return Array.Empty<MusicAudioMatch>();
        }
    }

    // «Найти вручную» в Источниках: сырой пользовательский запрос — без
    // переписывания, ранжирования и гейта релевантности, решает человек
    public async Task<IReadOnlyList<MusicAudioMatch>> SearchMatchesByQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<MusicAudioMatch>();

        try
        {
            var results = new List<VideoSearchResult>();

            await foreach (var video in youtube.Search.GetVideosAsync(query, cancellationToken))
            {
                results.Add(video);
                if (results.Count >= 20)
                    break;
            }

            return YouTubeAudioSupport.ConvertSearchResults(results);
        }
        catch
        {
            return Array.Empty<MusicAudioMatch>();
        }
    }

    public async Task<IReadOnlyList<MusicPlaybackSource>> GetStreamsAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (match == null || string.IsNullOrWhiteSpace(match.id))
            return Array.Empty<MusicPlaybackSource>();

        try
        {
            var manifest = await youtube.Videos.Streams.GetManifestAsync(match.id, cancellationToken);
            if (MusicPlaybackModeService.IsVideo(playbackMode))
            {
                var videoStreams = manifest.GetMuxedStreams();
                return YouTubeAudioSupport.ConvertVideoStreams(videoStreams);
            }

            var audioStreams = manifest.GetAudioOnlyStreams();
            return YouTubeAudioSupport.ConvertAudioStreams(audioStreams);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] youtube stream manifest failed for {match.id}: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<MusicPlaybackSource>();
        }
    }

    public Task<MusicPlaybackSource> TryGetPreferredStreamAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<MusicPlaybackSource>(null);
    }

    public bool IsRelevantMatch(MusicTrack track, MusicAudioMatch match)
    {
        return YouTubeAudioSupport.IsRelevantMatch(track, match);
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
