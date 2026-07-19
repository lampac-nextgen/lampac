using System.Collections;
using System.Text.RegularExpressions;
using YoutubeExplode;
using YoutubeExplode.Channels;
using YoutubeExplode.Playlists;
using YoutubeExplode.Search;

namespace Music;

internal static class YouTubeMusicSearchSupport
{
    public const string SearchSectionId = "search:youtubemusic";
    public const string TracksSectionId = "search:youtubemusic:tracks";
    public const string PlaylistsSectionId = "search:youtubemusic:playlists";
    public const string ArtistsSectionId = "search:youtubemusic:artists";
    public const string ProviderId = "youtubeaudio";

    const string playlistPrefix = "youtube:playlist:";
    const string channelPrefix = "youtube:channel:";
    const string channelUploadsPrefix = "youtube:channeluploads:";
    const int maxSearchQueries = 2;
    static readonly string[] titleSeparators = { " - ", " – ", " — " };

    public static bool IsSearchEnabled => ModInit.conf?.youtube_audio_enabled == true;

    public static async Task<List<MusicTrack>> SearchTracksByQueryAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicTrack>();

        try
        {
            using var youtube = new YoutubeClient();
            var videos = new List<VideoSearchResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var searchQuery in BuildQueries(query).Take(maxSearchQueries))
            {
                int queryCount = 0;

                await foreach (var video in youtube.Search.GetVideosAsync(searchQuery, cancellationToken))
                {
                    string videoId = video.Id.Value;
                    if (!string.IsNullOrWhiteSpace(videoId) && seen.Add(videoId))
                        videos.Add(video);

                    queryCount++;
                    if (queryCount >= Math.Max(limit * 2, 12) || videos.Count >= Math.Max(limit * 3, 18))
                        break;
                }

                if (videos.Count >= Math.Max(limit * 3, 18))
                    break;
            }

            return videos
                .Select(MapTrack)
                .Where(i => i != null)
                .Take(Math.Max(1, limit))
                .ToList();
        }
        catch
        {
            return new List<MusicTrack>();
        }
    }

    public static async Task<List<MusicAlbum>> SearchPlaylistsByQueryAsync(string query, int limit = 8, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicAlbum>();

        try
        {
            using var youtube = new YoutubeClient();
            var playlists = new List<MusicAlbum>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var playlist in youtube.Search.GetPlaylistsAsync(query, cancellationToken))
            {
                var mapped = MapPlaylist(playlist);
                if (mapped != null && seen.Add(mapped.id))
                    playlists.Add(mapped);

                if (playlists.Count >= Math.Max(1, limit))
                    break;
            }

            return playlists;
        }
        catch
        {
            return new List<MusicAlbum>();
        }
    }

    public static async Task<List<MusicArtist>> SearchArtistsByQueryAsync(string query, int limit = 8, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicArtist>();

        try
        {
            using var youtube = new YoutubeClient();
            var artists = new List<MusicArtist>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var channel in youtube.Search.GetChannelsAsync($"{query} music", cancellationToken))
            {
                var mapped = MapChannel(channel);
                if (mapped != null && seen.Add(mapped.id))
                    artists.Add(mapped);

                if (artists.Count >= Math.Max(1, limit))
                    break;
            }

            return artists;
        }
        catch
        {
            return new List<MusicArtist>();
        }
    }

    public static async Task<MusicAlbum> GetPlaylistAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        string playlistId = ExtractPlaylistId(id);
        if (!string.IsNullOrWhiteSpace(playlistId))
            return await GetPlaylistAlbumInternalAsync(playlistId, cancellationToken);

        string channelId = ExtractChannelUploadsId(id);
        if (!string.IsNullOrWhiteSpace(channelId))
            return await GetChannelUploadsAlbumAsync(channelId, cancellationToken);

        return null;
    }

    static async Task<MusicAlbum> GetPlaylistAlbumInternalAsync(string playlistId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
            return null;

        try
        {
            using var youtube = new YoutubeClient();
            Playlist playlist = null;

            try
            {
                playlist = await youtube.Playlists.GetAsync(playlistId, cancellationToken);
            }
            catch
            {
                playlist = null;
            }

            var album = new MusicAlbum
            {
                id = $"{playlistPrefix}{playlistId}",
                title = playlist?.Title ?? "YouTube Music Playlist",
                artist_name = CleanArtist(playlist?.Author.ChannelTitle),
                type = "Playlist",
                description = "YouTube Music",
                images = ExtractImages(playlist),
                provider_refs = new List<MusicProviderRef>
                {
                    new() { provider = ProviderId, external_id = playlistId }
                }
            };

            int number = 1;
            await foreach (var video in youtube.Playlists.GetVideosAsync(playlistId, cancellationToken))
            {
                var track = MapPlaylistTrack(video, album, number);
                if (track != null)
                {
                    if (album.images.Count == 0 && track.images.Count > 0)
                        album.images = track.images;

                    album.tracks.Add(track);
                    number++;
                }

                if (album.tracks.Count >= 50)
                    break;
            }

            return album;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<MusicArtist> GetChannelArtistAsync(string id, CancellationToken cancellationToken = default)
    {
        string channelId = ExtractChannelId(id);
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        try
        {
            using var youtube = new YoutubeClient();
            Channel channel = null;

            try
            {
                channel = await youtube.Channels.GetAsync(channelId, cancellationToken);
            }
            catch
            {
                channel = null;
            }

            string name = channel?.Title ?? channelId;
            var images = ExtractImages(channel);

            return new MusicArtist
            {
                id = $"{channelPrefix}{channelId}",
                name = name,
                description = "YouTube Music",
                images = images,
                provider_refs = new List<MusicProviderRef>
                {
                    new() { provider = ProviderId, external_id = channelId }
                },
                albums = new List<MusicAlbum>
                {
                    BuildChannelUploadsAlbum(channelId, name, images)
                }
            };
        }
        catch
        {
            return null;
        }
    }

    public static bool IsPlaylistAlbum(string provider, string id)
    {
        return string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(ExtractPlaylistId(id)) || !string.IsNullOrWhiteSpace(ExtractChannelUploadsId(id)));
    }

    public static bool IsChannelArtist(string provider, string id)
    {
        return string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ExtractChannelId(id));
    }

    static IEnumerable<string> BuildQueries(string query)
    {
        query = Regex.Replace(query.Trim(), @"\s+", " ");
        yield return $"{query} official audio";
        yield return $"{query} topic";
        yield return query;
    }

    static MusicTrack MapTrack(VideoSearchResult video)
    {
        string videoId = video?.Id.Value;
        string rawTitle = video?.Title?.Trim();

        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(rawTitle))
            return null;

        string artist = CleanArtist(video.Author.ChannelTitle);
        string title = CleanTitle(rawTitle, artist, out var titleArtist);
        if (string.IsNullOrWhiteSpace(artist))
            artist = titleArtist;

        return new MusicTrack
        {
            id = $"youtube:{videoId}",
            title = string.IsNullOrWhiteSpace(title) ? rawTitle : title,
            artist_name = artist,
            artists = string.IsNullOrWhiteSpace(artist) ? new List<string>() : new List<string> { artist },
            duration_ms = video.Duration.HasValue ? (int)video.Duration.Value.TotalMilliseconds : null,
            images = ExtractImages(video),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = videoId }
            }
        };
    }

    static MusicAlbum MapPlaylist(PlaylistSearchResult playlist)
    {
        string playlistId = playlist?.Id.Value;
        string title = playlist?.Title?.Trim();

        if (string.IsNullOrWhiteSpace(playlistId) || string.IsNullOrWhiteSpace(title))
            return null;

        return new MusicAlbum
        {
            id = $"{playlistPrefix}{playlistId}",
            title = title,
            artist_name = CleanArtist(playlist.Author.ChannelTitle),
            type = "Playlist",
            description = "YouTube Music",
            images = ExtractImages(playlist),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = playlistId }
            }
        };
    }

    static MusicArtist MapChannel(ChannelSearchResult channel)
    {
        string channelId = channel?.Id.Value;
        string title = channel?.Title?.Trim();

        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(title))
            return null;

        return new MusicArtist
        {
            id = $"{channelPrefix}{channelId}",
            name = title,
            description = "YouTube Music",
            images = ExtractImages(channel),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = channelId }
            }
        };
    }

    static MusicAlbum BuildChannelUploadsAlbum(string channelId, string channelTitle, List<MusicImage> images = null)
    {
        return new MusicAlbum
        {
            id = $"{channelUploadsPrefix}{channelId}",
            title = "Видео канала",
            artist_id = $"{channelPrefix}{channelId}",
            artist_name = channelTitle,
            type = "Playlist",
            description = "YouTube Music",
            images = images ?? new List<MusicImage>(),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = channelId }
            }
        };
    }

    static async Task<MusicAlbum> GetChannelUploadsAlbumAsync(string channelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        try
        {
            using var youtube = new YoutubeClient();
            Channel channel = null;

            try
            {
                channel = await youtube.Channels.GetAsync(channelId, cancellationToken);
            }
            catch
            {
                channel = null;
            }

            var album = BuildChannelUploadsAlbum(channelId, channel?.Title ?? channelId, ExtractImages(channel));

            int number = 1;
            await foreach (var video in youtube.Channels.GetUploadsAsync(channelId, cancellationToken))
            {
                var track = MapUploadTrack(video, album, number);
                if (track != null)
                {
                    if (album.images.Count == 0 && track.images.Count > 0)
                        album.images = track.images;

                    album.tracks.Add(track);
                    number++;
                }

                if (album.tracks.Count >= 50)
                    break;
            }

            return album;
        }
        catch
        {
            return null;
        }
    }

    static MusicTrack MapPlaylistTrack(PlaylistVideo video, MusicAlbum album, int number)
    {
        string videoId = video?.Id.Value;
        string rawTitle = video?.Title?.Trim();

        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(rawTitle))
            return null;

        string artist = CleanArtist(video.Author.ChannelTitle);
        string title = CleanTitle(rawTitle, artist, out var titleArtist);
        if (string.IsNullOrWhiteSpace(artist))
            artist = titleArtist;

        return new MusicTrack
        {
            id = $"youtube:{videoId}",
            title = string.IsNullOrWhiteSpace(title) ? rawTitle : title,
            artist_name = artist,
            artists = string.IsNullOrWhiteSpace(artist) ? new List<string>() : new List<string> { artist },
            album_id = album?.id,
            album_title = album?.title,
            duration_ms = video.Duration.HasValue ? (int)video.Duration.Value.TotalMilliseconds : null,
            track_number = number,
            images = ExtractImages(video),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = videoId }
            }
        };
    }

    static MusicTrack MapUploadTrack(object video, MusicAlbum album, int number)
    {
        string videoId = GetNestedString(video, "Id", "Value");
        string rawTitle = GetPropertyString(video, "Title")?.Trim();

        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(rawTitle))
            return null;

        string artist = CleanArtist(GetNestedString(video, "Author", "ChannelTitle"));
        string title = CleanTitle(rawTitle, artist, out var titleArtist);
        if (string.IsNullOrWhiteSpace(artist))
            artist = titleArtist;

        return new MusicTrack
        {
            id = $"youtube:{videoId}",
            title = string.IsNullOrWhiteSpace(title) ? rawTitle : title,
            artist_name = artist,
            artists = string.IsNullOrWhiteSpace(artist) ? new List<string>() : new List<string> { artist },
            album_id = album?.id,
            album_title = album?.title,
            duration_ms = GetDurationMs(video),
            track_number = number,
            images = ExtractImages(video),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = videoId }
            }
        };
    }

    static List<MusicImage> ExtractImages(params object[] sources)
    {
        var images = new List<MusicImage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string url, int? width = null, int? height = null)
        {
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                return;

            images.Add(new MusicImage
            {
                url = url,
                width = width,
                height = height
            });
        }

        foreach (var source in sources)
        {
            if (source == null)
                continue;

            Add(GetPropertyString(source, "LogoUrl"));

            if (GetPropertyValue(source, "Thumbnails") is not IEnumerable thumbnails)
                continue;

            foreach (var thumbnail in thumbnails)
            {
                string url = GetPropertyString(thumbnail, "Url");
                int? width = GetInt(GetNestedValue(thumbnail, "Resolution", "Width")) ?? GetInt(GetPropertyValue(thumbnail, "Width"));
                int? height = GetInt(GetNestedValue(thumbnail, "Resolution", "Height")) ?? GetInt(GetPropertyValue(thumbnail, "Height"));

                Add(url, width, height);
            }
        }

        return images
            .OrderBy(i => i.width ?? 0)
            .ThenBy(i => i.height ?? 0)
            .ToList();
    }

    static string GetPropertyString(object target, string name)
    {
        return GetPropertyValue(target, name)?.ToString();
    }

    static string GetNestedString(object target, params string[] names)
    {
        return GetNestedValue(target, names)?.ToString();
    }

    static object GetNestedValue(object target, params string[] names)
    {
        object value = target;
        foreach (var name in names)
        {
            value = GetPropertyValue(value, name);
            if (value == null)
                return null;
        }

        return value;
    }

    static object GetPropertyValue(object target, string name)
    {
        return target?.GetType().GetProperty(name)?.GetValue(target);
    }

    static int? GetDurationMs(object target)
    {
        var value = GetPropertyValue(target, "Duration");
        return value is TimeSpan duration ? (int)duration.TotalMilliseconds : null;
    }

    static int? GetInt(object value)
    {
        if (value == null)
            return null;

        if (value is int intValue)
            return intValue;

        if (value is long longValue)
            return longValue > int.MaxValue ? int.MaxValue : (int)longValue;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    static string ExtractPlaylistId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (id.StartsWith(playlistPrefix, StringComparison.OrdinalIgnoreCase))
            return id.Substring(playlistPrefix.Length);

        return null;
    }

    static string ExtractChannelUploadsId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (id.StartsWith(channelUploadsPrefix, StringComparison.OrdinalIgnoreCase))
            return id.Substring(channelUploadsPrefix.Length);

        return null;
    }

    static string ExtractChannelId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (id.StartsWith(channelPrefix, StringComparison.OrdinalIgnoreCase))
            return id.Substring(channelPrefix.Length);

        return null;
    }

    static string CleanArtist(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = Regex.Replace(value.Trim(), @"\s*-\s*Topic$", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\s*VEVO$", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    static string CleanTitle(string rawTitle, string knownArtist, out string titleArtist)
    {
        titleArtist = null;
        if (string.IsNullOrWhiteSpace(rawTitle))
            return rawTitle;

        string title = Regex.Replace(rawTitle.Trim(), @"\s+", " ");

        foreach (var separator in titleSeparators)
        {
            var parts = title.Split(separator, 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;

            string left = parts[0];
            string right = parts[1];
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                continue;

            titleArtist = left;

            if (string.IsNullOrWhiteSpace(knownArtist) || SameArtist(left, knownArtist))
                return StripTitleNoise(right);
        }

        return StripTitleNoise(title);
    }

    static string StripTitleNoise(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return title;

        title = Regex.Replace(title, @"\s*\((official\s+)?(audio|video|music\s+video|visualizer|lyrics?)\)\s*$", string.Empty, RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s*\[(official\s+)?(audio|video|music\s+video|visualizer|lyrics?)\]\s*$", string.Empty, RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    static bool SameArtist(string left, string right)
    {
        string a = Normalize(left);
        string b = Normalize(right);

        return !string.IsNullOrWhiteSpace(a)
            && !string.IsNullOrWhiteSpace(b)
            && (a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = Regex.Replace(value.ToLowerInvariant(), @"\s*-\s*topic$", string.Empty);
        value = Regex.Replace(value, @"\s*vevo$", string.Empty);
        value = new string(value.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());

        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}
