using System.Text;

namespace Music;

public static class MusicPlaybackService
{
    public static async Task<MusicTrack> ResolveRequestTrackAsync(string id, string provider, string title, string artistName, string albumId, string albumTitle, int? durationMs, string date, string isrc)
    {
        string normalizedIsrc = MusicIsrc.Normalize(isrc);
        MusicTrack track = null;

        // Spotify search отдаёт точный track/album id, но без duration/isrc.
        // Достаём трек из его родного queryAlbum (кэшируется каталогом), вместо
        // заведомо бесполезного поиска spotify:track:* через MusicBrainz.
        if (!string.IsNullOrWhiteSpace(id)
            && id.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase)
            && !durationMs.HasValue
            && !string.IsNullOrWhiteSpace(albumId)
            && albumId.StartsWith("spotify:album:", StringComparison.OrdinalIgnoreCase))
        {
            var albumTask = MusicCatalogService.GetAlbumAsync(albumId, SpotifySupport.ProviderId);
            if (await Task.WhenAny(albumTask, Task.Delay(2500)) == albumTask)
            {
                var album = await albumTask;
                track = album?.tracks?.FirstOrDefault(i => string.Equals(i?.id, id, StringComparison.OrdinalIgnoreCase));
            }
        }
        else
        {
            track = await MusicCatalogService.GetTrackAsync(id, provider);
        }

        if (track != null)
        {
            track.isrc = MusicIsrc.Normalize(track.isrc) ?? normalizedIsrc;
            return track;
        }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        return new MusicTrack
        {
            id = string.IsNullOrWhiteSpace(id) ? BuildInlineTrackId(title, artistName, albumTitle, durationMs, normalizedIsrc) : id,
            title = title,
            artist_name = artistName,
            album_id = albumId,
            album_title = albumTitle,
            isrc = normalizedIsrc,
            duration_ms = durationMs,
            date = date
        };
    }

    public static async Task<MusicPlayResponse> ResolveTrackAsync(MusicTrack track, string audioProvider, string streamMode, string playbackMode, string profileId, CancellationToken cancellationToken = default, bool refreshSources = false)
    {
        var result = await MusicResolver.ResolveTrackAsync(track, audioProvider, playbackMode, profileId, cancellationToken, refreshSources);
        if (result?.sources != null && result.sources.Count > 0)
            result.sources = MusicStreamModeService.Order(result.sources, streamMode);

        return result;
    }

    public static async Task<MusicPlayResponse> ResolvePreferredTrackAsync(MusicTrack track, string audioProvider, string streamMode, string playbackMode, string profileId, CancellationToken cancellationToken = default)
    {
        var result = await MusicResolver.ResolvePreferredTrackAsync(track, audioProvider, playbackMode, profileId, cancellationToken);
        if (result?.sources != null && result.sources.Count > 0)
            result.sources = MusicStreamModeService.Order(result.sources, streamMode);

        return result;
    }

    public static void PrepareStreamSources(string host, MusicTrack track, string provider, string audioProvider, string streamMode, string playbackMode, MusicPlayResponse result)
    {
        if (string.IsNullOrWhiteSpace(host) || track == null || result?.sources == null || result.sources.Count == 0)
            return;

        string resolvedProvider = ResolveTrackProvider(track, provider);
        var fallbackParams = BuildStreamFallbackParams(track, resolvedProvider, audioProvider, streamMode, playbackMode, null, null);

        foreach (var source in result.sources)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.url))
                continue;

            source.external_url = source.url;
            string ticket = MusicStreamTicketService.Create(source);
            if (!string.IsNullOrWhiteSpace(ticket))
            {
                var query = new List<string> { $"ticket={Uri.EscapeDataString(ticket)}" };
                query.AddRange(fallbackParams);
                if (!string.IsNullOrWhiteSpace(source.quality))
                    query.Add($"quality={Uri.EscapeDataString(source.quality)}");

                source.url = $"{host}/music/stream?{string.Join("&", query)}";
            }

            source.headers = new Dictionary<string, string>();
        }
    }

    public static MusicPlaybackSource SelectSource(List<MusicPlaybackSource> sources, string quality, string streamMode)
    {
        if (sources == null || sources.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(quality))
        {
            var exact = sources.FirstOrDefault(i => string.Equals(i.quality, quality, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
        }

        return MusicStreamModeService.Order(sources, streamMode).FirstOrDefault();
    }

    public static string ResolveTrackProvider(MusicTrack track, string fallbackProvider)
    {
        if (!string.IsNullOrWhiteSpace(fallbackProvider))
            return fallbackProvider;

        if (track?.provider_refs == null || track.provider_refs.Count == 0)
            return null;

        foreach (var providerRef in track.provider_refs)
        {
            if (!string.IsNullOrWhiteSpace(providerRef?.provider))
                return providerRef.provider.Trim();
        }

        return null;
    }

    public static List<string> BuildStreamFallbackParams(MusicTrack track, string provider, string audioProvider, string streamMode, string playbackMode, string uid, string accountEmail)
    {
        var url = new List<string>();

        if (!string.IsNullOrWhiteSpace(track?.id))
            url.Add($"id={Uri.EscapeDataString(track.id)}");

        if (!string.IsNullOrWhiteSpace(provider))
            url.Add($"provider={Uri.EscapeDataString(provider)}");

        if (!string.IsNullOrWhiteSpace(audioProvider))
            url.Add($"audio_provider={Uri.EscapeDataString(audioProvider)}");

        if (!string.IsNullOrWhiteSpace(streamMode))
            url.Add($"stream_mode={Uri.EscapeDataString(streamMode)}");

        if (!string.IsNullOrWhiteSpace(playbackMode))
            url.Add($"playback_mode={Uri.EscapeDataString(playbackMode)}");

        if (!string.IsNullOrWhiteSpace(track?.title))
            url.Add($"title={Uri.EscapeDataString(track.title)}");

        if (!string.IsNullOrWhiteSpace(track?.artist_name))
            url.Add($"artist_name={Uri.EscapeDataString(track.artist_name)}");

        if (!string.IsNullOrWhiteSpace(track?.album_id))
            url.Add($"album_id={Uri.EscapeDataString(track.album_id)}");

        if (!string.IsNullOrWhiteSpace(track?.album_title))
            url.Add($"album_title={Uri.EscapeDataString(track.album_title)}");

        string isrc = MusicIsrc.Normalize(track?.isrc);
        if (isrc != null)
            url.Add($"isrc={Uri.EscapeDataString(isrc)}");

        if (track?.duration_ms.HasValue == true)
            url.Add($"duration_ms={track.duration_ms.Value}");

        if (!string.IsNullOrWhiteSpace(track?.date))
            url.Add($"date={Uri.EscapeDataString(track.date)}");

        if (!string.IsNullOrWhiteSpace(uid))
            url.Add($"uid={Uri.EscapeDataString(uid)}");

        if (!string.IsNullOrWhiteSpace(accountEmail))
            url.Add($"account_email={Uri.EscapeDataString(accountEmail)}");

        return url;
    }

    static string BuildInlineTrackId(string title, string artistName, string albumTitle, int? durationMs, string isrc)
    {
        string key = $"{artistName}::{title}::{albumTitle}::{durationMs}::{isrc}";
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        return "inline:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
