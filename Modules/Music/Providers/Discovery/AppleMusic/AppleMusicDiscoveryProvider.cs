using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Music;

public class AppleMusicDiscoveryProvider : IMusicDiscoveryProvider
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    static readonly TimeSpan cacheTtl = TimeSpan.FromHours(6);

    const string providerId = "applemusiccharts";
    const string country = "us";
    const string userAgent = "LampacNextgenMusic/0.1 (https://github.com/lampac-nextgen/lampac)";

    public string Id => providerId;
    public string Name => "Apple Music Charts";
    public bool Enabled => true;

    static AppleMusicDiscoveryProvider()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<List<MusicBrowseSection>> GetHomeSectionsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var feed = await GetTopAlbumsAsync(cancellationToken);
        if (feed.Count == 0)
            return new List<MusicBrowseSection>();

        var sections = new List<MusicBrowseSection>
        {
            new MusicBrowseSection
            {
                id = "browse:top_albums",
                title = "Популярные альбомы",
                type = "album",
                source_provider = Id,
                has_more = feed.Count > limit,
                albums = feed.Take(limit).ToList()
            }
        };

        var fresh = feed
            .Where(i => TryParseDate(i.date, out var releaseDate) && releaseDate >= DateTime.UtcNow.Date.AddDays(-180))
            .OrderByDescending(i => i.date)
            .Take(limit)
            .ToList();

        if (fresh.Count > 0)
        {
            sections.Add(new MusicBrowseSection
            {
                id = "browse:fresh_releases",
                title = "Свежие релизы",
                type = "album",
                source_provider = Id,
                has_more = fresh.Count >= limit,
                albums = fresh
            });
        }

        sections.AddRange(BuildGenreSections(feed, limit));

        return sections;
    }

    public async Task<MusicBrowseSection> GetSectionAsync(string sectionId, int limit, CancellationToken cancellationToken = default)
    {
        var feed = await GetTopAlbumsAsync(cancellationToken);
        if (feed.Count == 0)
            return null;

        return sectionId switch
        {
            "browse:top_albums" => new MusicBrowseSection
            {
                id = sectionId,
                title = "Популярные альбомы",
                type = "album",
                source_provider = Id,
                has_more = false,
                albums = feed.Take(Math.Max(limit, 1)).ToList()
            },
            "browse:fresh_releases" => new MusicBrowseSection
            {
                id = sectionId,
                title = "Свежие релизы",
                type = "album",
                source_provider = Id,
                has_more = false,
                albums = feed
                    .Where(i => TryParseDate(i.date, out var releaseDate) && releaseDate >= DateTime.UtcNow.Date.AddDays(-180))
                    .OrderByDescending(i => i.date)
                    .Take(Math.Max(limit, 1))
                    .ToList()
            },
            _ when TryParseGenreSection(feed, sectionId, out var genre) => BuildGenreSection(feed, genre, Math.Max(limit, 1), hasMore: false),
            _ => null
        };
    }

    async Task<List<MusicAlbum>> GetTopAlbumsAsync(CancellationToken cancellationToken)
    {
        return await MusicMetadataCacheService.GetOrCreateAsync(
            providerId,
            "browse",
            $"{country}:top-albums",
            cacheTtl,
            () => LoadTopAlbumsAsync(cancellationToken),
            cancellationToken
        ) ?? new List<MusicAlbum>();
    }

    async Task<List<MusicAlbum>> LoadTopAlbumsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://rss.marketingtools.apple.com/api/v2/{country}/music/most-played/100/albums.json");
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new List<MusicAlbum>();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var root = JsonNode.Parse(json) as JsonObject;
        var results = root?["feed"]?["results"] as JsonArray;

        if (results == null)
            return new List<MusicAlbum>();

        return results
            .Select(node => ParseAlbum(node as JsonObject))
            .Where(i => i != null)
            .ToList();
    }

    static MusicAlbum ParseAlbum(JsonObject item)
    {
        if (item == null)
            return null;

        string title = item["name"]?.GetValue<string>()?.Trim();
        string artistName = item["artistName"]?.GetValue<string>()?.Trim();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artistName))
            return null;

        string lookupQuery = $"{artistName} {title}";
        string artwork = UpgradeArtwork(item["artworkUrl100"]?.GetValue<string>());
        string date = item["releaseDate"]?.GetValue<string>();
        string appleId = item["id"]?.GetValue<string>();
        string externalId = string.IsNullOrWhiteSpace(appleId)
            ? StableId(lookupQuery)
            : appleId;

        return new MusicAlbum
        {
            id = $"applecharts:{externalId}",
            title = title,
            artist_name = artistName,
            lookup_query = lookupQuery,
            date = date,
            year = ParseYear(date),
            type = "Album",
            description = JoinGenres(item["genres"] as JsonArray),
            images = string.IsNullOrWhiteSpace(artwork)
                ? new List<MusicImage>()
                : new List<MusicImage> { new() { url = artwork, width = 600, height = 600 } },
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = providerId, external_id = externalId }
            }
        };
    }

    static string JoinGenres(JsonArray items)
    {
        if (items == null)
            return null;

        var genres = items
            .Select(node => node?["name"]?.GetValue<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToList();

        return genres.Count == 0 ? null : string.Join(" · ", genres);
    }

    static List<MusicBrowseSection> BuildGenreSections(List<MusicAlbum> feed, int limit)
    {
        if (feed == null || feed.Count == 0)
            return new List<MusicBrowseSection>();

        return feed
            .Select(item => PrimaryGenre(item))
            .Where(genre => !string.IsNullOrWhiteSpace(genre))
            .GroupBy(genre => genre)
            .Where(group => group.Count() >= Math.Max(6, limit / 2))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(3)
            .Select(group => BuildGenreSection(feed, group.Key, limit, hasMore: group.Count() > limit))
            .Where(section => section != null && section.albums.Count > 0)
            .ToList();
    }

    static MusicBrowseSection BuildGenreSection(List<MusicAlbum> feed, string genre, int limit, bool hasMore)
    {
        if (feed == null || feed.Count == 0 || string.IsNullOrWhiteSpace(genre))
            return null;

        var albums = feed
            .Where(album => string.Equals(PrimaryGenre(album), genre, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Max(limit, 1))
            .ToList();

        if (albums.Count == 0)
            return null;

        return new MusicBrowseSection
        {
            id = $"browse:genre:{GenreSlug(genre)}",
            title = $"Популярное: {LocalizeGenre(genre)}",
            type = "album",
            source_provider = providerId,
            has_more = hasMore,
            albums = albums
        };
    }

    static string PrimaryGenre(MusicAlbum album)
    {
        if (album == null || string.IsNullOrWhiteSpace(album.description))
            return null;

        return album.description
            .Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(genre => !string.Equals(genre, "Music", StringComparison.OrdinalIgnoreCase));
    }

    static string GenreSlug(string genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
            return string.Empty;

        var slug = System.Text.RegularExpressions.Regex.Replace(genre.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? StableId(genre) : slug;
    }

    static bool TryParseGenreSection(List<MusicAlbum> feed, string sectionId, out string genre)
    {
        genre = null;
        if (string.IsNullOrWhiteSpace(sectionId) || !sectionId.StartsWith("browse:genre:", StringComparison.OrdinalIgnoreCase))
            return false;

        var slug = sectionId.Substring("browse:genre:".Length);
        if (string.IsNullOrWhiteSpace(slug))
            return false;

        var feedGenres = feed?
            .Select(PrimaryGenre)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        genre = feedGenres.FirstOrDefault(item => string.Equals(GenreSlug(item), slug, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(genre);
    }

    static string LocalizeGenre(string genre)
    {
        return genre switch
        {
            "Hip-Hop/Rap" => "Хип-хоп и рэп",
            "Pop" => "Поп",
            "Rock" => "Рок",
            "Country" => "Кантри",
            "Alternative" => "Альтернатива",
            "R&B/Soul" => "R&B и соул",
            "Dance" => "Танцевальная",
            "Singer/Songwriter" => "Авторская песня",
            "Latin" => "Латино",
            "Christian" => "Христианская музыка",
            "Jazz" => "Джаз",
            "Electronic" => "Электроника",
            "K-Pop" => "K-Pop",
            "Indie Rock" => "Инди-рок",
            "Hard Rock" => "Хард-рок",
            "Soundtrack" => "Саундтреки",
            _ => genre
        };
    }

    static string UpgradeArtwork(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return System.Text.RegularExpressions.Regex.Replace(
            url,
            "/\\d+x\\d+bb\\.",
            "/600x600bb.",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
    }

    static int? ParseYear(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
            return null;

        return int.TryParse(value.Substring(0, 4), out var year) ? year : null;
    }

    static bool TryParseDate(string value, out DateTime date)
    {
        return DateTime.TryParse(value, out date);
    }

    static string StableId(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
