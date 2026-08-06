using Shared;
using Shared.Models.Base;

namespace Music;

public static class MusicImageProxyService
{
    static readonly BaseSettings init = new()
    {
        plugin = "Music"
    };

    public static MusicSearchResponse Apply(BaseController controller, MusicSearchResponse response)
    {
        if (controller == null || response == null)
            return response;

        if (response.artists != null)
        {
            foreach (var artist in response.artists)
                Apply(controller, artist);
        }

        if (response.albums != null)
        {
            foreach (var album in response.albums)
                Apply(controller, album);
        }

        if (response.tracks != null)
        {
            foreach (var track in response.tracks)
                Apply(controller, track);
        }

        if (response.search_sections != null)
        {
            foreach (var section in response.search_sections)
                Apply(controller, section);
        }

        return response;
    }

    public static MusicHomeResponse Apply(BaseController controller, MusicHomeResponse response)
    {
        if (controller == null || response == null)
            return response;

        if (response.browse_sections != null)
        {
            foreach (var section in response.browse_sections)
                Apply(controller, section);
        }

        if (response.recently_played != null)
        {
            foreach (var item in response.recently_played)
            {
                if (item?.track != null)
                    Apply(controller, item.track);
            }
        }

        if (response.user_playlists != null)
        {
            foreach (var playlist in response.user_playlists)
                Apply(controller, playlist);
        }

        return response;
    }

    public static MusicUserPlaylistSummary Apply(BaseController controller, MusicUserPlaylistSummary playlist)
    {
        if (controller == null || playlist == null)
            return playlist;

        ProxyImages(controller, playlist.images);
        return playlist;
    }

    public static MusicStatsTopResult Apply(BaseController controller, MusicStatsTopResult response)
    {
        if (controller == null || response == null)
            return response;

        if (response.tracks != null)
        {
            foreach (var item in response.tracks)
            {
                if (item?.track != null)
                    Apply(controller, item.track);
            }
        }

        return response;
    }

    public static MusicArtist Apply(BaseController controller, MusicArtist artist)
    {
        if (controller == null || artist == null)
            return artist;

        ProxyImages(controller, artist.images);

        if (artist.albums != null)
        {
            foreach (var album in artist.albums)
                Apply(controller, album);
        }

        if (artist.sections != null)
        {
            foreach (var section in artist.sections)
                Apply(controller, section);
        }

        return artist;
    }

    public static MusicAlbum Apply(BaseController controller, MusicAlbum album)
    {
        if (controller == null || album == null)
            return album;

        ProxyImages(controller, album.images);
        return album;
    }

    public static MusicTrack Apply(BaseController controller, MusicTrack track)
    {
        if (controller == null || track == null)
            return track;

        ProxyImages(controller, track.images);
        return track;
    }

    public static MusicBrowseSection Apply(BaseController controller, MusicBrowseSection section)
    {
        if (controller == null || section == null)
            return section;

        if (section.albums != null)
        {
            foreach (var album in section.albums)
                Apply(controller, album);
        }

        if (section.artists != null)
        {
            foreach (var artist in section.artists)
                Apply(controller, artist);
        }

        if (section.tracks != null)
        {
            foreach (var track in section.tracks)
                Apply(controller, track);
        }

        return section;
    }

    static void ProxyImages(BaseController controller, List<MusicImage> images)
    {
        if (images == null || images.Count == 0)
            return;

        foreach (var image in images)
        {
            if (image == null || string.IsNullOrWhiteSpace(image.url))
                continue;

            // кэши секций/сущностей шарят инстансы между запросами, а Apply
            // мутирует url на месте — хост ПЕРВОГО запросившего запекался в
            // кэш навсегда (живой баг: SC-полка с 127.0.0.1 после curl-тестов
            // с хоста — на телефоне все обложки битые). Уже проксированный
            // url переписываем на хост текущего запроса: токен proxyimg от
            // хоста не зависит (проверено живьём)
            if (TryRewriteProxyHost(image.url, controller.host, out string rewritten))
            {
                image.url = rewritten;
                continue;
            }

            if (!NeedProxy(image.url, controller.host))
                continue;

            image.url = controller.HostImgProxy(init, image.url);
        }
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
