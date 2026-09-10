using Shared;
using Shared.Models.Base;

namespace Music;

public static class MusicImageProxyService
{
    static readonly BaseSettings init = new()
    {
        plugin = "Music"
    };

    // Create a shallow copy without listing every DTO property. The delegate is
    // bound once; new metadata fields are preserved automatically.
    static readonly Func<object, object> shallowCopy = typeof(object)
        .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        .CreateDelegate<Func<object, object>>();

    static T Copy<T>(T value) where T : class => (T)shallowCopy(value);

    // Only rewritten image branches are copied. Reused branches stay read-only:
    // controllers serialize this result without modifying its DTOs or lists.
    public static T Apply<T>(BaseController controller, T response)
    {
        if (controller == null || response == null)
            return response;

        return (T)Rewrite(controller, response);
    }

    static object Rewrite(BaseController controller, object value)
    {
        return value switch
        {
            MusicSearchResponse response => RewriteSearch(controller, response),
            MusicHomeResponse response => RewriteHome(controller, response),
            MusicStatsTopResult response => RewriteStats(controller, response),
            MusicArtist artist => RewriteArtist(controller, artist),
            MusicAlbum album => RewriteAlbum(controller, album),
            MusicTrack track => RewriteTrack(controller, track),
            MusicBrowseSection section => RewriteSection(controller, section),
            MusicUserPlaylistSummary playlist => RewritePlaylist(controller, playlist),
            MusicUserPlaylistImportResult response => RewriteImport(controller, response),
            MusicDailyMixResponse response => RewriteDaily(controller, response),
            MusicRadioResponse response => RewriteRadio(controller, response),
            MusicRecentlyPlayedItem item => RewriteHistory(controller, item),
            MusicStatsTopTrack item => RewriteStatsTrack(controller, item),
            MusicImage image => RewriteImage(controller, image),
            List<MusicTrack> tracks => RewriteList(controller, tracks),
            List<MusicUserPlaylistSummary> playlists => RewriteList(controller, playlists),
            MusicTrack[] tracks => RewriteArray(controller, tracks),
            MusicUserPlaylistSummary[] playlists => RewriteArray(controller, playlists),
            _ => value
        };
    }

    static List<T> RewriteList<T>(BaseController controller, List<T> items) where T : class
    {
        if (items == null)
            return null;

        List<T> copy = null;
        for (int i = 0; i < items.Count; i++)
        {
            var item = (T)Rewrite(controller, items[i]);
            if (ReferenceEquals(item, items[i]))
                continue;

            copy ??= new List<T>(items);
            copy[i] = item;
        }

        return copy ?? items;
    }

    static T[] RewriteArray<T>(BaseController controller, T[] items) where T : class
    {
        T[] copy = null;
        for (int i = 0; i < items.Length; i++)
        {
            var item = (T)Rewrite(controller, items[i]);
            if (ReferenceEquals(item, items[i]))
                continue;

            copy ??= (T[])items.Clone();
            copy[i] = item;
        }

        return copy ?? items;
    }

    static MusicSearchResponse RewriteSearch(BaseController controller, MusicSearchResponse value)
    {
        var artists = RewriteList(controller, value.artists);
        var albums = RewriteList(controller, value.albums);
        var tracks = RewriteList(controller, value.tracks);
        var search_sections = RewriteList(controller, value.search_sections);

        if (ReferenceEquals(artists, value.artists)
            && ReferenceEquals(albums, value.albums)
            && ReferenceEquals(tracks, value.tracks)
            && ReferenceEquals(search_sections, value.search_sections))
            return value;

        var copy = Copy(value);
        copy.artists = artists;
        copy.albums = albums;
        copy.tracks = tracks;
        copy.search_sections = search_sections;
        return copy;
    }

    static MusicHomeResponse RewriteHome(BaseController controller, MusicHomeResponse value)
    {
        var browse_sections = RewriteList(controller, value.browse_sections);
        var recently_played = RewriteList(controller, value.recently_played);
        var user_playlists = RewriteList(controller, value.user_playlists);

        if (ReferenceEquals(browse_sections, value.browse_sections)
            && ReferenceEquals(recently_played, value.recently_played)
            && ReferenceEquals(user_playlists, value.user_playlists))
            return value;

        var copy = Copy(value);
        copy.browse_sections = browse_sections;
        copy.recently_played = recently_played;
        copy.user_playlists = user_playlists;
        return copy;
    }

    static MusicStatsTopResult RewriteStats(BaseController controller, MusicStatsTopResult value)
    {
        var tracks = RewriteList(controller, value.tracks);

        if (ReferenceEquals(tracks, value.tracks))
            return value;

        var copy = Copy(value);
        copy.tracks = tracks;
        return copy;
    }

    static MusicArtist RewriteArtist(BaseController controller, MusicArtist value)
    {
        var images = RewriteList(controller, value.images);
        var albums = RewriteList(controller, value.albums);
        var sections = RewriteList(controller, value.sections);

        if (ReferenceEquals(images, value.images)
            && ReferenceEquals(albums, value.albums)
            && ReferenceEquals(sections, value.sections))
            return value;

        var copy = Copy(value);
        copy.images = images;
        copy.albums = albums;
        copy.sections = sections;
        return copy;
    }

    static MusicAlbum RewriteAlbum(BaseController controller, MusicAlbum value)
    {
        var images = RewriteList(controller, value.images);
        var tracks = RewriteList(controller, value.tracks);

        if (ReferenceEquals(images, value.images)
            && ReferenceEquals(tracks, value.tracks))
            return value;

        var copy = Copy(value);
        copy.images = images;
        copy.tracks = tracks;
        return copy;
    }

    static MusicTrack RewriteTrack(BaseController controller, MusicTrack value)
    {
        var images = RewriteList(controller, value.images);

        if (ReferenceEquals(images, value.images))
            return value;

        var copy = Copy(value);
        copy.images = images;
        return copy;
    }

    static MusicBrowseSection RewriteSection(BaseController controller, MusicBrowseSection value)
    {
        var albums = RewriteList(controller, value.albums);
        var artists = RewriteList(controller, value.artists);
        var tracks = RewriteList(controller, value.tracks);

        if (ReferenceEquals(albums, value.albums)
            && ReferenceEquals(artists, value.artists)
            && ReferenceEquals(tracks, value.tracks))
            return value;

        var copy = Copy(value);
        copy.albums = albums;
        copy.artists = artists;
        copy.tracks = tracks;
        return copy;
    }

    static MusicUserPlaylistSummary RewritePlaylist(BaseController controller, MusicUserPlaylistSummary value)
    {
        var images = RewriteList(controller, value.images);

        if (ReferenceEquals(images, value.images))
            return value;

        var copy = Copy(value);
        copy.images = images;
        return copy;
    }

    static MusicUserPlaylistImportResult RewriteImport(BaseController controller, MusicUserPlaylistImportResult value)
    {
        var tracks = RewriteList(controller, value.tracks);

        if (ReferenceEquals(tracks, value.tracks))
            return value;

        var copy = Copy(value);
        copy.tracks = tracks;
        return copy;
    }

    static MusicDailyMixResponse RewriteDaily(BaseController controller, MusicDailyMixResponse value)
    {
        var tracks = RewriteList(controller, value.tracks);

        if (ReferenceEquals(tracks, value.tracks))
            return value;

        var copy = Copy(value);
        copy.tracks = tracks;
        return copy;
    }

    static MusicRadioResponse RewriteRadio(BaseController controller, MusicRadioResponse value)
    {
        var tracks = RewriteList(controller, value.tracks);

        if (ReferenceEquals(tracks, value.tracks))
            return value;

        var copy = Copy(value);
        copy.tracks = tracks;
        return copy;
    }

    static MusicRecentlyPlayedItem RewriteHistory(BaseController controller, MusicRecentlyPlayedItem value)
    {
        var track = (MusicTrack)Rewrite(controller, value.track);

        if (ReferenceEquals(track, value.track))
            return value;

        var copy = Copy(value);
        copy.track = track;
        return copy;
    }

    static MusicStatsTopTrack RewriteStatsTrack(BaseController controller, MusicStatsTopTrack value)
    {
        var track = (MusicTrack)Rewrite(controller, value.track);

        if (ReferenceEquals(track, value.track))
            return value;

        var copy = Copy(value);
        copy.track = track;
        return copy;
    }

    static MusicImage RewriteImage(BaseController controller, MusicImage image)
    {
        string url = image.url;
        if (string.IsNullOrWhiteSpace(url))
            return image;

        // Normalize even when proxying is disabled (e.g. file:// TV clients).
        if (url.StartsWith("//", StringComparison.Ordinal))
            url = "https:" + url;

        // Repair legacy cached proxy URLs without modifying the cached image.
        if (TryRewriteProxyHost(url, controller.host, out string rewritten))
            url = rewritten;
        else if (NeedProxy(url, controller.host))
            url = controller.HostImgProxy(init, url);

        if (string.Equals(url, image.url, StringComparison.Ordinal))
            return image;

        var copy = Copy(image);
        copy.url = url;
        return copy;
    }

    static bool TryRewriteProxyHost(string url, string host, out string rewritten)
    {
        rewritten = null;

        int index = url.IndexOf("/proxyimg", StringComparison.OrdinalIgnoreCase);
        if (index <= 0 || string.IsNullOrWhiteSpace(host))
            return false;

        string prefix = url.Substring(0, index);
        if (string.Equals(prefix, host, StringComparison.OrdinalIgnoreCase))
            return false;

        // префикс должен быть полноценным origin (а не куском чужого пути)
        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var prefixUri) ||
            !prefixUri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;

        rewritten = host + url.Substring(index);
        return true;
    }

    static bool NeedProxy(string url, string host)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (!uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(host) &&
            Uri.TryCreate(host, UriKind.Absolute, out var hostUri) &&
            string.Equals(uri.Host, hostUri.Host, StringComparison.OrdinalIgnoreCase))
            return false;

        return !url.Contains("/proxyimg", StringComparison.OrdinalIgnoreCase);
    }
}
