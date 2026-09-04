using System.Text.RegularExpressions;

namespace Music;

public class SpotifyDiscoveryProvider : IMusicDiscoveryProvider
{
    sealed class Playlist
    {
        public string title { get; init; }
        public string playlistId { get; init; }
    }

    static readonly TimeSpan cacheTtl = TimeSpan.FromHours(1);
    static readonly IReadOnlyList<Playlist> globalPlaylists = new List<Playlist>
    {
        new() { title = "Топ-50 Spotify: мир", playlistId = "37i9dQZEVXbMDoHDwVN2tF" },
        new() { title = "Today's Top Hits", playlistId = "37i9dQZF1DXcBWIGoYBM5M" },
        new() { title = "New Music Friday", playlistId = "37i9dQZF1DX4JAvHpjipBk" },
        new() { title = "RapCaviar", playlistId = "37i9dQZF1DX0XUsuxWHRQd" },
        new() { title = "Fresh Finds", playlistId = "37i9dQZF1DWWjGdmeTyeJ6" }
    };

    static readonly IReadOnlyList<Playlist> themedPlaylists = new List<Playlist>
    {
        new() { title = "Клубные хиты", playlistId = "37i9dQZF1DX0BcQWzuB7ZO" },
        new() { title = "Ностальгическая вечеринка", playlistId = "37i9dQZF1DX7F6T2n2fegs" },
        new() { title = "Хиты 2000-х", playlistId = "37i9dQZF1DX4o1oenSJRJd" },
        new() { title = "Хиты 2010-х", playlistId = "37i9dQZF1DWWylYLMvjuRG" },
        new() { title = "Весёлая пятница", playlistId = "37i9dQZF1DX1g0iEXLFycr" }
    };

    static readonly IReadOnlyDictionary<string, IReadOnlyList<Playlist>> countryThemedPlaylists =
        new Dictionary<string, IReadOnlyList<Playlist>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GLOBAL"] = new List<Playlist>
            {
                new() { title = "Хиты 80-х", playlistId = "37i9dQZF1DX4UtSsGT1Sbe" },
                new() { title = "Хиты 90-х", playlistId = "37i9dQZF1DXbTxeAdrVG2l" },
                new() { title = "Классика рока", playlistId = "37i9dQZF1DWXRqgorJj26U" },
                new() { title = "Песни для поездки", playlistId = "37i9dQZF1DWWMOmoXKqHTD" },
                new() { title = "Спокойные хиты", playlistId = "37i9dQZF1DX4WYpdgoIcn6" }
            },
            ["UA"] = new List<Playlist>
            {
                new() { title = "Літні хіти України", playlistId = "37i9dQZF1DX86ZB9BGPT0a" },
                new() { title = "Українська ностальгія", playlistId = "3FUapgKzH0x7qSygyWS7rk" },
                new() { title = "Українські хіти 2000-х", playlistId = "3Id8TtStr3uzNv79RhOgmb" },
                new() { title = "Українські легенди", playlistId = "158jVnGiYCk96G6oJweWVz" },
                new() { title = "Українська вечірка", playlistId = "3wKPQ5RdRZymbAcEEkVRwT" }
            },
            ["CA"] = new List<Playlist>
            {
                new() { title = "Классика канадского рока", playlistId = "37i9dQZF1DX4bKgmgKIPtO" },
                new() { title = "Канада: хиты 90-х", playlistId = "37i9dQZF1DX9NmM48Aqz3e" },
                new() { title = "Канадский хип-хоп", playlistId = "37i9dQZF1DX59ogDi1Z2XL" },
                new() { title = "Классика канадской инди-музыки", playlistId = "37i9dQZF1DX5dx2bmWL9SZ" },
                new() { title = "Канадское кантри", playlistId = "37i9dQZF1DWYV2Gh2QglGo" }
            },
            ["US"] = new List<Playlist>
            {
                new() { title = "Американа: классика", playlistId = "37i9dQZF1DX7tfbjVrTPnV" },
                new() { title = "R&B-вечеринка", playlistId = "37i9dQZF1DX2hNQN2Fv6Cy" },
                new() { title = "Хип-хоп 90-х", playlistId = "37i9dQZF1DX186v583rmzp" },
                new() { title = "Хип-хоп 2000-х", playlistId = "37i9dQZF1DX1lHW2vbQwNN" },
                new() { title = "Хип-хоп 2010-х", playlistId = "37i9dQZF1DX97h7ftpNSYT" }
            },
            ["RU"] = new List<Playlist>
            {
                new() { title = "Русские клубные хиты", playlistId = "0PwJbcDouwFD1oKhynlUR9" },
                new() { title = "Русская ностальгия", playlistId = "5oeCCfcOxkBLRL5AXf0iVs" },
                new() { title = "Русские хиты 2000-х", playlistId = "1JNYqFwOv1bIrXUjHTIDAC" },
                new() { title = "Русские хиты 2010-х", playlistId = "6OZJ2pHVbVto0SgTMMAK0G" },
                new() { title = "Русская вечеринка", playlistId = "4IYyB7pb7JIoT3ciWBgJJa" }
            },
            ["PL"] = new List<Playlist>
            {
                new() { title = "Rap klub", playlistId = "37i9dQZF1DX3HUaZJRcDLd" },
                new() { title = "Polskie przeboje wszech czasów", playlistId = "37i9dQZF1DX8J2l55TrZk6" },
                new() { title = "Polski rap 2000.", playlistId = "37i9dQZF1DX4tIiLXPvlZC" },
                new() { title = "Polskie lata 2010.", playlistId = "37i9dQZF1DX7bSIS915wSM" },
                new() { title = "Polska impreza", playlistId = "37i9dQZF1DX7P3ukP665LS" }
            },
            ["FR"] = new List<Playlist>
            {
                new() { title = "La French Touch", playlistId = "37i9dQZF1DX9cbNxuNYT3d" },
                new() { title = "Pop francophone", playlistId = "37i9dQZF1DX9ZKIkjFureA" },
                new() { title = "Années 2000", playlistId = "37i9dQZF1DWWTe89CFDZnE" },
                new() { title = "Années 2010", playlistId = "37i9dQZF1DWVrO0SOklesK" },
                new() { title = "Soirée française", playlistId = "37i9dQZF1EIe3TBxFniBXv" }
            },
            ["DE"] = new List<Playlist>
            {
                new() { title = "Deutschpop Klassiker", playlistId = "37i9dQZF1DX2cNqJ4LgCMf" },
                new() { title = "Schlager Klassiker", playlistId = "37i9dQZF1DX6uHioFvkN7A" },
                new() { title = "Deutschrap 2000er", playlistId = "37i9dQZF1DX5sbPvjd2Huv" },
                new() { title = "Deutschrap 2010er", playlistId = "37i9dQZF1DX1q42kBiHxxd" },
                new() { title = "POPLAND", playlistId = "37i9dQZF1DXbKGrOUA30KN" }
            }
        };

    static readonly IReadOnlyDictionary<string, IReadOnlyList<Playlist>> countryPlaylists =
        new Dictionary<string, IReadOnlyList<Playlist>>(StringComparer.OrdinalIgnoreCase)
        {
            ["UA"] = new List<Playlist>
            {
                new() { title = "Топ-50 Spotify: Украина", playlistId = "37i9dQZEVXbKkidEfWYRuD" },
                new() { title = "Hot Hits Україна", playlistId = "37i9dQZF1DX1V3tM4cuX0v" },
                new() { title = "TOP POP 2026", playlistId = "37i9dQZF1DX2vTOtsQ5Isl" },
                new() { title = "EQUAL Україна", playlistId = "37i9dQZF1DWTtjLYc6QFF2" },
                new() { title = "Music for Ukraine", playlistId = "37i9dQZF1DWY0TJvcbosib" }
            },
            ["CA"] = new List<Playlist>
            {
                new() { title = "Топ-50 Spotify: Канада", playlistId = "37i9dQZEVXbKj23U1GF4IR" },
                new() { title = "Hot Hits Canada", playlistId = "37i9dQZF1DWXT8uSSn6PRy" },
                new() { title = "New Music Friday Canada", playlistId = "37i9dQZF1DX5DfG8gQdC3F" },
                new() { title = "Fresh Finds Canada", playlistId = "37i9dQZF1DWVxStm5ni6tl" },
                new() { title = "Today's Top Hits", playlistId = "37i9dQZF1DXcBWIGoYBM5M" }
            },
            ["US"] = new List<Playlist>
            {
                new() { title = "Топ-50 Spotify: США", playlistId = "37i9dQZEVXbLRQDuF5jeBp" },
                new() { title = "Hot Hits USA", playlistId = "37i9dQZF1DX0kbJZpiYdZl" },
                new() { title = "Today's Top Hits", playlistId = "37i9dQZF1DXcBWIGoYBM5M" },
                new() { title = "New Music Friday", playlistId = "37i9dQZF1DX4JAvHpjipBk" },
                new() { title = "RapCaviar", playlistId = "37i9dQZF1DX0XUsuxWHRQd" }
            },
            ["RU"] = new List<Playlist>
            {
                new() { title = "Русский Топ-50", playlistId = "1qkR4WrgRWFg1w3Qik1lTu" },
                new() { title = "Русские хиты", playlistId = "7xm8bTP6NFGMDudMHsC2qp" },
                new() { title = "Русский поп", playlistId = "4dL0DgSaYON6tODhwM72gw" },
                new() { title = "Русский рэп", playlistId = "0Y8djwSTijxFoC3Z0vdinF" },
                new() { title = "Русский рок", playlistId = "0Fb2Hc0MdcME6knQ0vFiby" }
            },
            ["PL"] = new List<Playlist>
            {
                new() { title = "Топ-50 Spotify: Польша", playlistId = "37i9dQZEVXbN6itCcaL3Tt" },
                new() { title = "Hot Hits Polska", playlistId = "37i9dQZF1DX49bSMRljsho" },
                new() { title = "New Music Friday Polska", playlistId = "37i9dQZF1DXaMTfp0LE4AZ" },
                new() { title = "Fresh Finds Polska", playlistId = "37i9dQZF1DWTI0B69TStH2" },
                new() { title = "RADAR Polska", playlistId = "37i9dQZF1DX1aXwAOtpwvU" }
            },
            ["FR"] = new List<Playlist>
            {
                new() { title = "Топ-50 Spotify: Франция", playlistId = "37i9dQZEVXbIPWwFssbupI" },
                new() { title = "Hits du Moment", playlistId = "37i9dQZF1DWVuV87wUBNwc" },
                new() { title = "La Hit Liste", playlistId = "37i9dQZF1DX7LjobXS2hzX" },
                new() { title = "New Music Friday France", playlistId = "37i9dQZF1DX742okrrpwah" },
                new() { title = "PVNCHLNRS", playlistId = "37i9dQZF1DX1X23oiQRTB5" }
            },
            ["DE"] = new List<Playlist>
            {
                new() { title = "Топ-50 Spotify: Германия", playlistId = "37i9dQZEVXbJiZcmkrIHGU" },
                new() { title = "Hot Hits Deutschland", playlistId = "37i9dQZF1DX4jP4eebSWR9" },
                new() { title = "New Music Friday Deutschland", playlistId = "37i9dQZF1DWUW2bvSkjcJ6" },
                new() { title = "Deutschrap Brandneu", playlistId = "37i9dQZF1DWSTqUqJcxFk6" },
                new() { title = "Fresh Finds GSA", playlistId = "37i9dQZF1DX2ddCYH6QIK5" }
            }
        };

    const string defaultCountry = "US";

    public string Id => SpotifySupport.DiscoveryProviderId;
    public string Name => "Spotify";
    public bool Enabled => ModInit.conf?.spotify_discovery_enabled == true;

    public async Task<List<MusicBrowseSection>> GetHomeSectionsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var section = await BuildSectionAsync(limit, cancellationToken);
        return section == null ? new List<MusicBrowseSection>() : new List<MusicBrowseSection> { section };
    }

    public async Task<MusicBrowseSection> GetSectionAsync(string sectionId, int limit, CancellationToken cancellationToken = default)
    {
        var profile = GetProfile();
        if (!string.Equals(sectionId, BuildSectionId(profile.country), StringComparison.OrdinalIgnoreCase))
            return null;

        return await BuildSectionAsync(limit, cancellationToken);
    }

    public static bool IsPlaylistAlbum(string provider, string id)
        => string.Equals(provider, SpotifySupport.DiscoveryProviderId, StringComparison.OrdinalIgnoreCase)
            && TryGetPlaylistId(id, out _);

    public async Task<MusicAlbum> GetPlaylistAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!TryGetPlaylistId(id, out string playlistId))
            return null;

        var profile = GetProfile();
        var playlist = profile.playlists.FirstOrDefault(item =>
            string.Equals(item.playlistId, playlistId, StringComparison.OrdinalIgnoreCase));
        if (playlist == null)
            return null;

        var album = await LoadPlaylistAsync(playlist.playlistId, SpotifySupport.PlaylistTrackLimit, cancellationToken);
        return album == null ? null : CopyAlbum(album, playlist.title, includeTracks: true);
    }

    async Task<MusicBrowseSection> BuildSectionAsync(int limit, CancellationToken cancellationToken)
    {
        var profile = GetProfile();
        int take = Math.Clamp(limit, 1, profile.playlists.Count);
        var playlists = profile.playlists.Take(take).ToList();
        var tasks = playlists.Select(item => LoadPlaylistAsync(item.playlistId, 1, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks);
        var albums = new List<MusicAlbum>();

        for (int i = 0; i < playlists.Count; i++)
        {
            if (results[i] != null)
                albums.Add(CopyAlbum(results[i], playlists[i].title, includeTracks: false));
        }

        if (albums.Count == 0)
            return null;

        return new MusicBrowseSection
        {
            id = BuildSectionId(profile.country),
            title = profile.title,
            type = "album",
            source_provider = SpotifySupport.DiscoveryProviderId,
            has_more = profile.playlists.Count > take,
            albums = albums
        };
    }

    static (string country, string title, IReadOnlyList<Playlist> playlists) GetProfile()
    {
        string country = NormalizeCountry(ModInit.conf?.spotify_country);
        if (!countryPlaylists.TryGetValue(country, out var playlists))
            return ("GLOBAL", "Spotify: мир", AppendThemed("GLOBAL", globalPlaylists));

        string title = country switch
        {
            "UA" => "Spotify: Украина",
            "CA" => "Spotify: Канада",
            "US" => "Spotify: США",
            "RU" => "Spotify: Россия",
            "PL" => "Spotify: Польша",
            "FR" => "Spotify: Франция",
            "DE" => "Spotify: Германия",
            _ => "Spotify"
        };
        return (country, title, AppendThemed(country, playlists));
    }

    static IReadOnlyList<Playlist> AppendThemed(string country, IReadOnlyList<Playlist> playlists)
    {
        var result = playlists.ToList();
        if (countryThemedPlaylists.TryGetValue(country, out var countryPlaylists))
            result.AddRange(countryPlaylists);

        result.AddRange(themedPlaylists);
        return result;
    }

    static string NormalizeCountry(string country)
    {
        country = country?.Trim().ToUpperInvariant();
        return !string.IsNullOrWhiteSpace(country) && Regex.IsMatch(country, "^[A-Z]{2}$")
            ? country
            : defaultCountry;
    }

    static Task<MusicAlbum> LoadPlaylistAsync(string playlistId, int limit, CancellationToken cancellationToken)
    {
        int take = Math.Clamp(limit, 1, SpotifySupport.PlaylistTrackLimit);
        return MusicMetadataCacheService.GetOrCreateAsync(
            SpotifySupport.DiscoveryProviderId,
            "playlist",
            $"{playlistId}:paged-v2:{take}",
            cacheTtl,
            () => SpotifySupport.GetPlaylistAlbumAsync(playlistId, take, cancellationToken),
            cancellationToken
        );
    }

    static MusicAlbum CopyAlbum(MusicAlbum album, string title, bool includeTracks)
    {
        return new MusicAlbum
        {
            id = album.id,
            title = string.IsNullOrWhiteSpace(title) ? album.title : title,
            artist_name = album.artist_name,
            type = album.type,
            description = album.description,
            images = album.images?.ToList() ?? new List<MusicImage>(),
            provider_refs = album.provider_refs?.ToList() ?? new List<MusicProviderRef>(),
            tracks = includeTracks ? album.tracks?.ToList() ?? new List<MusicTrack>() : new List<MusicTrack>()
        };
    }

    static bool TryGetPlaylistId(string id, out string playlistId)
    {
        playlistId = null;
        const string prefix = "spotify:playlist:";
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string value = id[prefix.Length..];
        if (!Regex.IsMatch(value, "^[A-Za-z0-9]{22}$"))
            return false;

        playlistId = value;
        return true;
    }

    static string BuildSectionId(string country)
        => $"browse:spotify:{country.ToLowerInvariant()}";
}
