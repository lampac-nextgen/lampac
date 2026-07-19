using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Music;

public class MusicBrainzMetadataProvider : IMusicMetadataProvider
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    static readonly SemaphoreSlim requestGate = new(1, 1);
    static DateTime nextRequestAt = DateTime.MinValue;

    const string baseUrl = "https://musicbrainz.org/ws/2";
    const string userAgent = "LampacNextgenMusic/0.1 (https://github.com/lampac-nextgen/lampac)";

    public string Id => "musicbrainz";
    public string Name => "MusicBrainz";
    public bool Enabled => true;

    static MusicBrainzMetadataProvider()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<MusicSearchResult> SearchAsync(string query, bool expanded = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new MusicSearchResult();

        query = query.Trim();

        var artistsJson = await GetJsonAsync($"artist?query={HttpUtility.UrlEncode(query)}&limit={(expanded ? 16 : 8)}&fmt=json", cancellationToken);
        var artists = RankArtists(ParseArtists(artistsJson?["artists"] as JsonArray), query, expanded ? 16 : 8);

        await DiscogsArtistImageService.ApplyCachedAsync(artists, cancellationToken);
        DiscogsArtistImageService.WarmupMissing(artists);

        var context = BuildSearchContext(query, artists);
        var isArtistOnly = context?.BestArtist != null && context.IsArtistOnly;
        var albumsTask = GetJsonAsync(BuildAlbumSearchPath(context, expanded), cancellationToken);
        var tracksTask = isArtistOnly ? null : GetJsonAsync(BuildTrackSearchPath(context, expanded), cancellationToken);
        var albumsJson = await albumsTask;
        var allAlbums = ParseAlbums(albumsJson?["release-groups"] as JsonArray);
        ApplyBestArtistFallback(allAlbums, context?.BestArtist);

        var albums = RankAlbums(allAlbums, context, expanded ? 100 : 24);
        var preferredAlbumTitles = albums
            .Take(8)
            .Select(item => NormalizeText(item.title))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
        var preferredAlbumArtists = albums
            .Take(8)
            .Select(item => NormalizeText(item.artist_name))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);

        List<MusicTrack> tracks;
        if (isArtistOnly)
            tracks = await BuildArtistOnlyTracksAsync(context, allAlbums, preferredAlbumTitles, preferredAlbumArtists, expanded, cancellationToken);
        else
        {
            var tracksJson = await tracksTask;
            tracks = RankTracks(ParseTracks(tracksJson?["recordings"] as JsonArray), context, preferredAlbumTitles, preferredAlbumArtists, expanded ? 100 : 24);
        }

        return new MusicSearchResult
        {
            artists = artists,
            albums = albums,
            tracks = tracks
        };
    }

    public async Task<MusicArtist> GetArtistAsync(string id, CancellationToken cancellationToken = default)
    {
        var artistTask = GetJsonAsync($"artist/{HttpUtility.UrlEncode(id)}?fmt=json", cancellationToken);
        var albumsTask = GetJsonAsync($"release-group?artist={HttpUtility.UrlEncode(id)}&limit=12&fmt=json", cancellationToken);

        await Task.WhenAll(artistTask, albumsTask);

        var artistJson = await artistTask;
        if (artistJson == null)
            return null;

        var albumsJson = await albumsTask;
        var artist = ParseArtist(artistJson);
        artist.albums = ParseAlbums(albumsJson?["release-groups"] as JsonArray);
        await DiscogsArtistImageService.EnrichAsync(artist, cancellationToken);
        return artist;
    }

    public async Task<MusicAlbum> GetAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        var releaseJson = await GetJsonAsync($"release/{HttpUtility.UrlEncode(id)}?inc=recordings+artist-credits+release-groups+media&fmt=json", cancellationToken);
        if (releaseJson != null)
            return ParseAlbumFromRelease(releaseJson);

        var releasesJson = await GetJsonAsync($"release?release-group={HttpUtility.UrlEncode(id)}&limit=12&status=official&inc=media&fmt=json", cancellationToken);
        var releases = RankReleaseCandidates(releasesJson?["releases"] as JsonArray).Take(5).ToList();
        if (releases.Count == 0)
            return null;

        MusicAlbum bestAlbum = null;
        int bestScore = int.MinValue;

        foreach (var release in releases)
        {
            var releaseId = release["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(releaseId))
                continue;

            releaseJson = await GetJsonAsync($"release/{HttpUtility.UrlEncode(releaseId)}?inc=recordings+artist-credits+release-groups+media&fmt=json", cancellationToken);
            if (releaseJson == null)
                continue;

            var parsedAlbum = ParseAlbumFromRelease(releaseJson);
            if (parsedAlbum == null)
                continue;

            var score = ScoreParsedRelease(parsedAlbum, release);
            if (score > bestScore)
            {
                bestScore = score;
                bestAlbum = parsedAlbum;
            }

            if ((parsedAlbum.tracks?.Count ?? 0) >= 8 && !LooksLikeEditionNoise(parsedAlbum.title) && !LooksLikeEditionNoise(parsedAlbum.description))
                break;
        }

        return bestAlbum;
    }

    public async Task<MusicTrack> GetTrackAsync(string id, CancellationToken cancellationToken = default)
    {
        var trackJson = await GetJsonAsync($"recording/{HttpUtility.UrlEncode(id)}?inc=artist-credits+releases&fmt=json", cancellationToken);
        return trackJson == null ? null : ParseTrack(trackJson);
    }

    static async Task<JsonObject> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        await RespectRateLimitAsync(cancellationToken);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{path}");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // MusicBrainz asks clients to keep Web Service requests at 1 req/s.
    static async Task RespectRateLimitAsync(CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken);

        try
        {
            var now = DateTime.UtcNow;
            if (nextRequestAt > now)
                await Task.Delay(nextRequestAt - now, cancellationToken);

            nextRequestAt = DateTime.UtcNow.AddSeconds(1);
        }
        finally
        {
            requestGate.Release();
        }
    }

    static List<MusicArtist> ParseArtists(JsonArray items)
    {
        if (items == null)
            return new List<MusicArtist>();

        return items
            .Select(node => ParseArtist(node as JsonObject))
            .Where(item => item != null)
            .ToList();
    }

    static MusicArtist ParseArtist(JsonObject item)
    {
        if (item == null)
            return null;

        var id = item["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return new MusicArtist
        {
            id = id,
            name = item["name"]?.GetValue<string>(),
            sort_name = item["sort-name"]?.GetValue<string>(),
            country = item["country"]?.GetValue<string>(),
            description = item["disambiguation"]?.GetValue<string>(),
            search_score = ParseInt(item["score"]),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = "musicbrainz", external_id = $"artist:{id}" }
            }
        };
    }

    static List<MusicAlbum> ParseAlbums(JsonArray items)
    {
        if (items == null)
            return new List<MusicAlbum>();

        return items
            .Select(node => ParseAlbum(node as JsonObject))
            .Where(item => item != null)
            .Where(item => string.IsNullOrWhiteSpace(item.type) || item.type is "Album" or "EP" or "Single" or "Soundtrack")
            .ToList();
    }

    static MusicAlbum ParseAlbum(JsonObject item)
    {
        if (item == null)
            return null;

        var id = item["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var artistCredit = item["artist-credit"] as JsonArray;
        var artist = FirstArtist(artistCredit);

        return new MusicAlbum
        {
            id = id,
            title = item["title"]?.GetValue<string>(),
            artist_id = artist.id,
            artist_name = JoinArtistCredit(artistCredit),
            year = ParseYear(item["first-release-date"]?.GetValue<string>()),
            date = item["first-release-date"]?.GetValue<string>(),
            type = item["primary-type"]?.GetValue<string>(),
            description = item["disambiguation"]?.GetValue<string>(),
            search_score = ParseInt(item["score"]),
            secondary_types = ParseStringList(item["secondary-types"] as JsonArray),
            images = BuildReleaseGroupImages(id),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = "musicbrainz", external_id = $"release-group:{id}" }
            }
        };
    }

    static List<MusicTrack> ParseTracks(JsonArray items)
    {
        if (items == null)
            return new List<MusicTrack>();

        return items
            .Select(node => ParseTrack(node as JsonObject))
            .Where(item => item != null)
            .ToList();
    }

    static MusicTrack ParseTrack(JsonObject item)
    {
        if (item == null)
            return null;

        var id = item["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var artistCredit = item["artist-credit"] as JsonArray;
        var release = SelectPreferredRelease(item["releases"] as JsonArray);

        return new MusicTrack
        {
            id = id,
            title = item["title"]?.GetValue<string>(),
            artist_id = FirstArtist(artistCredit).id,
            artist_name = JoinArtistCredit(artistCredit),
            artists = ParseArtistNames(artistCredit),
            album_id = release?["id"]?.GetValue<string>(),
            album_title = release?["title"]?.GetValue<string>(),
            duration_ms = item["length"]?.GetValue<int?>(),
            date = release?["date"]?.GetValue<string>(),
            search_score = ParseInt(item["score"]),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = "musicbrainz", external_id = $"recording:{id}" }
            }
        };
    }

    static MusicAlbum ParseAlbumFromRelease(JsonObject release)
    {
        if (release == null)
            return null;

        var releaseId = release["id"]?.GetValue<string>();
        var releaseGroup = release["release-group"] as JsonObject;
        var releaseGroupId = releaseGroup?["id"]?.GetValue<string>();
        var releaseGroupTitle = releaseGroup?["title"]?.GetValue<string>();
        var artistCredit = (releaseGroup?["artist-credit"] ?? release["artist-credit"]) as JsonArray;
        var artist = FirstArtist(artistCredit);
        var album = new MusicAlbum
        {
            id = releaseGroupId ?? releaseId,
            release_id = releaseId,
            title = releaseGroupTitle ?? release["title"]?.GetValue<string>(),
            artist_id = artist.id,
            artist_name = JoinArtistCredit(artistCredit),
            year = ParseYear(release["date"]?.GetValue<string>() ?? releaseGroup?["first-release-date"]?.GetValue<string>()),
            date = release["date"]?.GetValue<string>() ?? releaseGroup?["first-release-date"]?.GetValue<string>(),
            type = releaseGroup?["primary-type"]?.GetValue<string>(),
            description = releaseGroup?["disambiguation"]?.GetValue<string>(),
            images = !string.IsNullOrWhiteSpace(releaseGroupId) ? BuildReleaseGroupImages(releaseGroupId) : BuildReleaseImages(releaseId),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = "musicbrainz", external_id = $"release:{releaseId}" },
                new() { provider = "musicbrainz", external_id = $"release-group:{releaseGroupId ?? releaseId}" }
            }
        };

        var media = release["media"] as JsonArray;
        if (media != null)
        {
            foreach (var discNode in media.OfType<JsonObject>())
            {
                var discNumber = ParseInt(discNode["position"]);
                var tracks = discNode["tracks"] as JsonArray;
                if (tracks == null)
                    continue;

                foreach (var trackNode in tracks.OfType<JsonObject>())
                {
                    var recording = trackNode["recording"] as JsonObject;
                    var trackId = recording?["id"]?.GetValue<string>() ?? trackNode["id"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(trackId))
                        continue;

                    var trackArtistCredit = (recording?["artist-credit"] ?? trackNode["artist-credit"]) as JsonArray ?? artistCredit;
                    var trackArtist = FirstArtist(trackArtistCredit);

                    album.tracks.Add(new MusicTrack
                    {
                        id = trackId,
                        title = recording?["title"]?.GetValue<string>() ?? trackNode["title"]?.GetValue<string>(),
                        artist_id = trackArtist.id,
                        artist_name = JoinArtistCredit(trackArtistCredit),
                        artists = ParseArtistNames(trackArtistCredit),
                        album_id = album.id,
                        album_title = album.title,
                        duration_ms = recording?["length"]?.GetValue<int?>() ?? trackNode["length"]?.GetValue<int?>(),
                        track_number = ParseInt(trackNode["number"]),
                        disc_number = discNumber,
                        date = album.date,
                        provider_refs = new List<MusicProviderRef>
                        {
                            new() { provider = "musicbrainz", external_id = $"recording:{trackId}" }
                        }
                    });
                }
            }
        }

        return album;
    }

    static JsonObject SelectPreferredRelease(JsonArray releases)
    {
        return RankReleaseCandidates(releases).FirstOrDefault();
    }

    sealed class SearchContext
    {
        public string Query { get; init; }
        public string NormalizedQuery { get; init; }
        public MusicArtist BestArtist { get; init; }
        public MusicArtist SecondaryArtist { get; init; }
        public int BestArtistScore { get; init; }
        public bool IsArtistOnly { get; init; }
        public bool IsArtistPair { get; init; }
        public bool IsMixed { get; init; }
        public string TitleTerms { get; init; }
        public string RankingQuery => !string.IsNullOrWhiteSpace(TitleTerms) ? TitleTerms : Query;
    }

    sealed class ArtistTrackCandidate
    {
        public string Title { get; init; }
        public string NormalizedTitle { get; init; }
        public int? Year { get; init; }
    }

    static SearchContext BuildSearchContext(string query, List<MusicArtist> artists)
    {
        var bestArtist = artists.FirstOrDefault();
        var bestArtistScore = bestArtist == null ? 0 : ScoreArtistCandidate(query, bestArtist);
        if (!ShouldAnchorArtistQuery(query, bestArtist, bestArtistScore))
        {
            bestArtist = null;
            bestArtistScore = 0;
        }

        var secondaryArtist = bestArtist == null
            ? null
            : artists
                .Skip(1)
                .FirstOrDefault(item => ShouldUseAsSecondaryArtist(query, bestArtist, item));
        var isArtistPair = bestArtist != null && secondaryArtist != null;
        var isArtistOnly = bestArtist != null && IsExactArtistQuery(query, bestArtist);
        var titleTerms = !isArtistOnly && !isArtistPair && bestArtistScore >= 180 ? ExtractQueryRemainder(query, bestArtist) : null;

        return new SearchContext
        {
            Query = query,
            NormalizedQuery = NormalizeText(query),
            BestArtist = bestArtist,
            SecondaryArtist = secondaryArtist,
            BestArtistScore = bestArtistScore,
            IsArtistOnly = isArtistOnly,
            IsArtistPair = isArtistPair,
            IsMixed = !string.IsNullOrWhiteSpace(titleTerms),
            TitleTerms = titleTerms
        };
    }

    static void ApplyBestArtistFallback(List<MusicAlbum> albums, MusicArtist bestArtist)
    {
        if (albums == null || bestArtist == null)
            return;

        foreach (var album in albums.Where(item => string.IsNullOrWhiteSpace(item.artist_name)))
        {
            album.artist_id = bestArtist.id;
            album.artist_name = bestArtist.name;
        }
    }

    static List<MusicArtist> RankArtists(List<MusicArtist> artists, string query, int limit)
    {
        return artists
            .OrderByDescending(item => ScoreArtistCandidate(query, item))
            .ThenBy(item => item.name ?? string.Empty)
            .Take(limit)
            .ToList();
    }

    static string BuildAlbumSearchPath(SearchContext context, bool expanded)
    {
        if (context?.BestArtist != null && context.IsArtistOnly)
            return $"release-group?artist={HttpUtility.UrlEncode(context.BestArtist.id)}&limit=100&fmt=json";

        if (context?.BestArtist != null && context.IsArtistPair)
        {
            var query = $"{BuildArtistIdentityQuery(context.BestArtist)} AND {BuildArtistIdentityQuery(context.SecondaryArtist)}";
            return $"release-group?query={HttpUtility.UrlEncode(query)}&limit={(expanded ? 80 : 24)}&fmt=json";
        }

        if (context?.BestArtist != null && context.IsMixed)
        {
            var query = $"{BuildArtistIdentityQuery(context.BestArtist)} AND (releasegroup:{QuoteSearchTerm(context.TitleTerms)} OR release:{QuoteSearchTerm(context.TitleTerms)})";
            return $"release-group?query={HttpUtility.UrlEncode(query)}&limit={(expanded ? 80 : 24)}&fmt=json";
        }

        return $"release-group?query={HttpUtility.UrlEncode(context?.Query ?? string.Empty)}&limit={(expanded ? 80 : 24)}&fmt=json";
    }

    static string BuildTrackSearchPath(SearchContext context, bool expanded)
    {
        if (context?.BestArtist != null && context.IsArtistOnly)
        {
            var query = BuildArtistIdentityQuery(context.BestArtist);
            return $"recording?query={HttpUtility.UrlEncode(query)}&limit={(expanded ? 140 : 60)}&fmt=json";
        }

        if (context?.BestArtist != null && context.IsArtistPair)
        {
            var query = $"{BuildArtistIdentityQuery(context.BestArtist)} AND {BuildArtistIdentityQuery(context.SecondaryArtist)}";
            return $"recording?query={HttpUtility.UrlEncode(query)}&limit={(expanded ? 120 : 40)}&fmt=json";
        }

        if (context?.BestArtist != null && context.IsMixed)
        {
            var query = $"{BuildArtistIdentityQuery(context.BestArtist)} AND recording:{QuoteSearchTerm(context.TitleTerms)}";
            return $"recording?query={HttpUtility.UrlEncode(query)}&limit={(expanded ? 120 : 40)}&fmt=json";
        }

        return $"recording?query={HttpUtility.UrlEncode(context?.Query ?? string.Empty)}&limit={(expanded ? 140 : 60)}&fmt=json";
    }

    static string BuildFeaturedTracksSearchPath(SearchContext context, IEnumerable<string> preferredTrackTitles, bool expanded)
    {
        if (context?.BestArtist == null || preferredTrackTitles == null)
            return null;

        var identityQuery = BuildArtistIdentityQuery(context.BestArtist);
        var titleQuery = string.Join(" OR ", preferredTrackTitles
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item =>
            {
                var quoted = QuoteSearchTerm(item);
                return $"(recording:{quoted} AND (releasegroup:{quoted} OR release:{quoted}))";
            }));

        if (string.IsNullOrWhiteSpace(identityQuery) || string.IsNullOrWhiteSpace(titleQuery))
            return null;

        return $"recording?query={HttpUtility.UrlEncode($"({titleQuery}) AND {identityQuery}")}&limit={(expanded ? 300 : 200)}&fmt=json";
    }

    static string BuildSupplementalSinglesSearchPath(SearchContext context)
    {
        var name = context?.BestArtist?.name;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return $"release-group?query={HttpUtility.UrlEncode($"artist:{QuoteSearchTerm(name)} AND primarytype:single")}&limit=60&fmt=json";
    }

    async Task<List<MusicTrack>> BuildArtistOnlyTracksAsync(
        SearchContext context,
        List<MusicAlbum> albums,
        HashSet<string> preferredAlbumTitles,
        HashSet<string> preferredAlbumArtists,
        bool expanded,
        CancellationToken cancellationToken)
    {
        var candidateLimit = expanded ? 40 : 24;
        var fallbackLimit = expanded ? 10 : 2;
        var targetFeaturedCount = expanded ? 40 : 20;
        var browseCandidates = SelectArtistTrackCandidates(albums);
        ArtistTrackCandidate[] candidates;

        if (browseCandidates.Count >= 20)
        {
            candidates = browseCandidates
                .Where(item => !LooksLikeCandidateSingleNoise(item.Title, context?.BestArtist))
                .Take(candidateLimit)
                .ToArray();
        }
        else
        {
            var candidateAlbums = new List<MusicAlbum>();
            var supplementalSinglesJson = await GetJsonAsync(BuildSupplementalSinglesSearchPath(context), cancellationToken);
            if (supplementalSinglesJson?["release-groups"] is JsonArray supplementalSingles)
                candidateAlbums.AddRange(ParseAlbums(supplementalSingles));

            ApplyBestArtistFallback(candidateAlbums, context?.BestArtist);
            var supplementalCandidates = SelectArtistTrackCandidates(candidateAlbums);
            candidates = MergeArtistTrackCandidates(browseCandidates, supplementalCandidates, context);
        }

        var featuredTracks = new List<MusicTrack>();

        if (candidates.Length > 0)
        {
            var bestByTitle = new Dictionary<string, (MusicTrack track, int score)>(StringComparer.Ordinal);
            var chunkRequests = candidates
                .Chunk(8)
                .Select(chunk => new
                {
                    candidates = chunk.ToArray(),
                    task = GetJsonAsync(BuildFeaturedTracksSearchPath(context, chunk.Select(item => item.Title), expanded), cancellationToken)
                })
                .ToArray();

            await Task.WhenAll(chunkRequests.Select(item => item.task));

            foreach (var request in chunkRequests)
            {
                var json = await request.task;
                CollectFeaturedTracks(bestByTitle, json?["recordings"] as JsonArray, context, request.candidates, preferredAlbumTitles);
            }

            var missingCandidates = candidates
                .Where(item => !bestByTitle.ContainsKey(item.NormalizedTitle))
                .Take(fallbackLimit)
                .ToArray();

            foreach (var candidate in missingCandidates)
            {
                var json = await GetJsonAsync(BuildFeaturedTracksSearchPath(context, new[] { candidate.Title }, expanded), cancellationToken);
                CollectFeaturedTracks(bestByTitle, json?["recordings"] as JsonArray, context, new[] { candidate }, preferredAlbumTitles);

                if (bestByTitle.Count >= targetFeaturedCount)
                    break;
            }

            featuredTracks = bestByTitle.Values
                .OrderByDescending(item => item.score)
                .ThenByDescending(item => ParseYear(item.track.date) ?? 0)
                .ThenBy(item => item.track.title ?? string.Empty)
                .Select(item => item.track)
                .ToList();
        }

        if (featuredTracks.Count >= targetFeaturedCount)
            return RankTracks(featuredTracks, context, preferredAlbumTitles, preferredAlbumArtists, expanded ? 100 : 24);

        var genericTracksJson = await GetJsonAsync(BuildTrackSearchPath(context, expanded), cancellationToken);
        var genericTracks = ParseTracks(genericTracksJson?["recordings"] as JsonArray)
            .Where(item => item != null)
            .Where(item => !LooksLikeEditionNoise(item.album_title))
            .Where(item => !LooksLikeFeaturedTrackNoise(item.title))
            .ToList();

        return RankTracks(featuredTracks.Concat(genericTracks).ToList(), context, preferredAlbumTitles, preferredAlbumArtists, expanded ? 100 : 24);
    }

    static string BuildArtistIdentityQuery(MusicArtist artist)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(artist?.id))
            parts.Add($"(arid:{artist.id})");
        if (!string.IsNullOrWhiteSpace(artist?.name))
        {
            parts.Add($"(artistname:{QuoteSearchTerm(artist.name)})");
            parts.Add($"(creditname:{QuoteSearchTerm(artist.name)})");
        }

        if (!string.IsNullOrWhiteSpace(artist?.sort_name)
            && !string.Equals(NormalizeText(artist.sort_name), NormalizeText(artist.name), StringComparison.Ordinal))
        {
            parts.Add($"(artistname:{QuoteSearchTerm(artist.sort_name)})");
            parts.Add($"(creditname:{QuoteSearchTerm(artist.sort_name)})");
        }

        return parts.Count == 0 ? string.Empty : "(" + string.Join(" OR ", parts) + ")";
    }

    static List<MusicAlbum> RankAlbums(List<MusicAlbum> albums, SearchContext context, int limit)
    {
        return OrderAlbums(albums, context)
            .Take(limit)
            .ToList();
    }

    static List<MusicAlbum> OrderAlbums(List<MusicAlbum> albums, SearchContext context)
    {
        return albums
            .GroupBy(item => item.id)
            .Select(group => group.First())
            .OrderByDescending(item => ScoreAlbum(item, context))
            .ThenBy(item => AlbumSortYear(item, context))
            .ThenBy(item => item.title ?? string.Empty)
            .ToList();
    }

    static List<MusicTrack> RankTracks(List<MusicTrack> tracks, SearchContext context, HashSet<string> preferredAlbumTitles, HashSet<string> preferredAlbumArtists, int limit)
    {
        return tracks
            .GroupBy(BuildTrackDedupKey)
            .Select(group => group
                .OrderByDescending(item => ScoreTrack(item, context, preferredAlbumTitles, preferredAlbumArtists))
                .ThenBy(item => TrackSortYear(item, context))
                .ThenBy(item => item.title ?? string.Empty)
                .First())
            .OrderByDescending(item => ScoreTrack(item, context, preferredAlbumTitles, preferredAlbumArtists))
            .ThenBy(item => TrackSortYear(item, context))
            .ThenBy(item => item.title ?? string.Empty)
            .Take(limit)
            .ToList();
    }

    static string BuildTrackDedupKey(MusicTrack track)
    {
        if (track == null)
            return string.Empty;

        var title = NormalizeText(track.title);
        var artist = NormalizeText(track.artist_name);
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
            return $"{title}|{artist}";

        return track.id ?? string.Empty;
    }

    static List<ArtistTrackCandidate> SelectArtistTrackCandidates(List<MusicAlbum> albums)
    {
        return albums?
            .Where(item => item != null)
            .Where(item => string.Equals(item.type, "Single", StringComparison.OrdinalIgnoreCase))
            .Where(item => item.secondary_types.Count == 0)
            .Where(item => !LooksLikeEditionNoise(item.title))
            .OrderByDescending(item => item.year ?? 0)
            .ThenBy(item => item.title ?? string.Empty)
            .GroupBy(item => NormalizeText(item.title))
            .Select(group =>
            {
                var album = group.First();
                return new ArtistTrackCandidate
                {
                    Title = album.title,
                    NormalizedTitle = NormalizeText(album.title),
                    Year = album.year
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.NormalizedTitle))
            .Take(40)
            .ToList()
            ?? new List<ArtistTrackCandidate>();
    }

    static ArtistTrackCandidate[] MergeArtistTrackCandidates(List<ArtistTrackCandidate> browseCandidates, List<ArtistTrackCandidate> supplementalCandidates, SearchContext context)
    {
        var selected = new List<ArtistTrackCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void append(IEnumerable<ArtistTrackCandidate> source, int limit)
        {
            foreach (var item in source)
            {
                if (selected.Count >= 24 || limit <= 0)
                    break;

                if (item == null || string.IsNullOrWhiteSpace(item.NormalizedTitle))
                    continue;

                if (LooksLikeCandidateSingleNoise(item.Title, context?.BestArtist))
                    continue;

                if (!seen.Add(item.NormalizedTitle))
                    continue;

                selected.Add(item);
                limit--;
            }
        }

        append(supplementalCandidates?.OrderByDescending(item => item.Year ?? 0).ThenBy(item => item.Title ?? string.Empty) ?? Enumerable.Empty<ArtistTrackCandidate>(), 14);
        append(browseCandidates?.OrderByDescending(item => item.Year ?? 0).ThenBy(item => item.Title ?? string.Empty) ?? Enumerable.Empty<ArtistTrackCandidate>(), 12);
        append(supplementalCandidates?.OrderByDescending(item => item.Year ?? 0).ThenBy(item => item.Title ?? string.Empty) ?? Enumerable.Empty<ArtistTrackCandidate>(), 24);
        append(browseCandidates?.OrderByDescending(item => item.Year ?? 0).ThenBy(item => item.Title ?? string.Empty) ?? Enumerable.Empty<ArtistTrackCandidate>(), 24);

        return selected.Take(24).ToArray();
    }

    static void ApplyPreferredCandidateRelease(MusicTrack track, JsonArray releases, ArtistTrackCandidate candidate)
    {
        if (track == null || releases == null || candidate == null || string.IsNullOrWhiteSpace(candidate.NormalizedTitle))
            return;

        var preferred = releases
            .OfType<JsonObject>()
            .Where(item => NormalizeText(item["title"]?.GetValue<string>()) == candidate.NormalizedTitle)
            .OrderBy(item => LooksLikeEditionNoise(item["title"]?.GetValue<string>()) ? 1 : 0)
            .ThenBy(item => item["date"]?.GetValue<string>() ?? "9999")
            .FirstOrDefault();

        if (preferred == null)
            return;

        track.album_id = preferred["id"]?.GetValue<string>() ?? track.album_id;
        track.album_title = preferred["title"]?.GetValue<string>() ?? track.album_title;
        track.date = preferred["date"]?.GetValue<string>() ?? track.date;
    }

    static void CollectFeaturedTracks(
        Dictionary<string, (MusicTrack track, int score)> bestByTitle,
        JsonArray recordings,
        SearchContext context,
        ArtistTrackCandidate[] candidates,
        HashSet<string> preferredAlbumTitles)
    {
        if (bestByTitle == null || recordings == null || context?.BestArtist == null || candidates == null || candidates.Length == 0)
            return;

        var candidateMap = candidates.ToDictionary(item => item.NormalizedTitle, item => item, StringComparer.Ordinal);

        foreach (var item in recordings.OfType<JsonObject>())
        {
            var title = item["title"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(title) || LooksLikeFeaturedTrackNoise(title))
                continue;

            var candidate = MatchArtistTrackCandidate(title, candidateMap);
            if (candidate == null)
                continue;

            var track = ParseTrack(item);
            if (track == null || LooksLikeFeaturedTrackNoise(track.album_title))
                continue;

            ApplyPreferredCandidateRelease(track, item["releases"] as JsonArray, candidate);

            var artistCredit = item["artist-credit"] as JsonArray;
            var artistName = JoinArtistCredit(artistCredit);
            var artistMatch = ScoreArtistMatch(artistName, context.BestArtist);
            if (artistMatch < 55)
                continue;

            var score = ScoreArtistTrackCandidate(item, track, candidate, context.BestArtist, preferredAlbumTitles, artistMatch);
            if (score < 320)
                continue;

            track.search_score = Math.Max(track.search_score ?? 0, 300 + score);

            if (!bestByTitle.TryGetValue(candidate.NormalizedTitle, out var current) || score > current.score)
                bestByTitle[candidate.NormalizedTitle] = (track, score);
        }
    }

    static int ScoreAlbum(MusicAlbum album, SearchContext context)
    {
        if (album == null)
            return int.MinValue;

        var score = (album.search_score ?? 0) * (context?.IsArtistOnly == true ? 1 : 4);
        var artistMatch = ScoreArtistMatch(album.artist_name, context?.BestArtist);
        if (context?.BestArtist != null)
            score += context.IsArtistOnly ? artistMatch * 3 : artistMatch * 2;

        if (context == null || !context.IsArtistOnly)
            score += ScoreTitleMatch(album.title, context?.RankingQuery);

        if (context?.BestArtist == null && IsExactQueryTitle(album.title, context))
            score += 90;

        score += album.type switch
        {
            "Album" => 80,
            "EP" => 55,
            "Single" => 35,
            "Soundtrack" => 20,
            _ => 10
        };

        if (context?.IsArtistOnly == true)
        {
            if (album.type == "Album" && album.secondary_types.Count == 0)
                score += 140;
            else if (album.type == "EP" && album.secondary_types.Count == 0)
                score += 80;
            else if (album.type == "Single" && album.secondary_types.Count == 0)
                score += 30;
        }

        if (album.year.HasValue)
            score += 5;

        if (album.secondary_types.Count > 0)
        {
            if (album.secondary_types.Contains("Compilation", StringComparer.OrdinalIgnoreCase))
                score -= 90;
            if (album.secondary_types.Contains("Live", StringComparer.OrdinalIgnoreCase))
                score -= 85;
            if (album.secondary_types.Contains("Remix", StringComparer.OrdinalIgnoreCase))
                score -= 85;
            if (album.secondary_types.Contains("DJ-mix", StringComparer.OrdinalIgnoreCase))
                score -= 180;
            if (album.secondary_types.Contains("Mixtape/Street", StringComparer.OrdinalIgnoreCase))
                score -= 160;
            if (album.secondary_types.Contains("Soundtrack", StringComparer.OrdinalIgnoreCase) && album.type != "Soundtrack")
                score -= 35;
        }

        if (LooksLikeEditionNoise(album.title) || LooksLikeEditionNoise(album.description))
            score -= 55;

        return score;
    }

    static int ScoreTrack(MusicTrack track, SearchContext context, HashSet<string> preferredAlbumTitles, HashSet<string> preferredAlbumArtists)
    {
        if (track == null)
            return int.MinValue;

        var score = (track.search_score ?? 0) * 4;
        var artistMatch = ScoreArtistMatch(track.artist_name, context?.BestArtist);
        if (context?.BestArtist != null)
            score += context.IsArtistOnly ? artistMatch * 3 : artistMatch * 2;

        if (context == null || !context.IsArtistOnly)
            score += ScoreTitleMatch(track.title, context?.RankingQuery);

        if (context?.BestArtist == null && IsExactQueryTitle(track.title, context))
            score += 90;

        if (track.duration_ms.HasValue)
            score += 8;

        if (!string.IsNullOrWhiteSpace(track.album_title))
            score += 6;

        if (IsExactQueryTitle(track.album_title, context))
            score += 40;

        var normalizedAlbumTitle = NormalizeText(track.album_title);
        if (!string.IsNullOrWhiteSpace(normalizedAlbumTitle) && preferredAlbumTitles?.Contains(normalizedAlbumTitle) == true)
            score += 70;

        var normalizedArtistName = NormalizeText(track.artist_name);
        if (!string.IsNullOrWhiteSpace(normalizedArtistName) && preferredAlbumArtists?.Any(item => normalizedArtistName.Contains(item, StringComparison.Ordinal) || item.Contains(normalizedArtistName, StringComparison.Ordinal)) == true)
            score += 55;

        if (LooksLikeEditionNoise(track.album_title))
            score -= 70;

        if (LooksLikeTrackNoise(track.title))
            score -= 65;

        return score;
    }

    static int ScoreArtistCandidate(string query, MusicArtist artist)
    {
        if (artist == null)
            return int.MinValue;

        var normalizedQuery = NormalizeText(query);
        var normalizedName = NormalizeText(artist.name);
        var normalizedSortName = NormalizeText(artist.sort_name);

        var score = 0;

        if (normalizedQuery == normalizedName)
            score += 320;
        else if (normalizedQuery == normalizedSortName)
            score += 280;
        else if (!string.IsNullOrWhiteSpace(normalizedName) && normalizedQuery.StartsWith(normalizedName + " ", StringComparison.Ordinal))
            score += 220;
        else if (!string.IsNullOrWhiteSpace(normalizedName) && normalizedQuery.Contains(normalizedName, StringComparison.Ordinal))
            score += 170;

        score += ScoreTokenOverlap(normalizedQuery, normalizedName) * 25;
        score += ScoreTokenOverlap(normalizedQuery, normalizedSortName) * 12;

        if (!string.IsNullOrWhiteSpace(artist.description))
            score -= 5;

        return score;
    }

    static bool ShouldAnchorArtistQuery(string query, MusicArtist artist, int score)
    {
        if (artist == null)
            return false;

        if (IsExactArtistQuery(query, artist))
            return true;

        if (score < 220)
            return false;

        if (string.IsNullOrWhiteSpace(artist.country) && string.IsNullOrWhiteSpace(artist.description))
            return false;

        var normalizedQuery = NormalizeText(query);
        var normalizedName = NormalizeText(artist.name);
        var normalizedSortName = NormalizeText(artist.sort_name);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return false;

        if (!string.IsNullOrWhiteSpace(normalizedName) && normalizedQuery.StartsWith(normalizedName + " ", StringComparison.Ordinal))
            return true;

        if (!string.IsNullOrWhiteSpace(normalizedSortName) && normalizedQuery.StartsWith(normalizedSortName + " ", StringComparison.Ordinal))
            return true;

        return Regex.IsMatch(query, @"(?i)(^|\s)(feat\.?|ft\.?|featuring|with|vs\.?|x|×)(\s|$)|[-–—:/]");
    }

    static bool ShouldUseAsSecondaryArtist(string query, MusicArtist primaryArtist, MusicArtist secondaryArtist)
    {
        if (secondaryArtist == null)
            return false;

        if (!Regex.IsMatch(query, @"(?i)(^|\s)(feat\.?|ft\.?|featuring|with|and|vs\.?|x|×)(\s|$)|[-–—:/]"))
            return false;

        var normalizedPrimary = NormalizeText(primaryArtist?.name);
        var normalizedSecondary = NormalizeText(secondaryArtist?.name);
        var normalizedQuery = NormalizeText(query);
        if (string.IsNullOrWhiteSpace(normalizedSecondary) || normalizedSecondary == normalizedPrimary)
            return false;

        if (!normalizedQuery.Contains(normalizedSecondary, StringComparison.Ordinal))
            return false;

        return !string.IsNullOrWhiteSpace(secondaryArtist.country) || !string.IsNullOrWhiteSpace(secondaryArtist.description);
    }

    static bool IsExactArtistQuery(string query, MusicArtist artist)
    {
        var normalizedQuery = NormalizeText(query);
        return normalizedQuery == NormalizeText(artist?.name)
            || normalizedQuery == NormalizeText(artist?.sort_name);
    }

    static string ExtractQueryRemainder(string query, MusicArtist artist)
    {
        foreach (var candidate in new[] { artist?.name, artist?.sort_name }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var stripped = Regex.Replace(query, Regex.Escape(candidate), " ", RegexOptions.IgnoreCase);
            stripped = Regex.Replace(stripped, @"(?i)\b(feat\.?|ft\.?|featuring|with|and|x|×|vs\.?)\b", " ");
            stripped = Regex.Replace(stripped, @"[-–—:;,/()]+", " ");
            stripped = Regex.Replace(stripped, @"\s+", " ").Trim();

            if (!string.IsNullOrWhiteSpace(stripped) && stripped.Length >= 3 && !string.Equals(stripped, query.Trim(), StringComparison.OrdinalIgnoreCase))
                return stripped;
        }

        return null;
    }

    static int ScoreTitleMatch(string title, string query)
    {
        var normalizedTitle = NormalizeText(title);
        var normalizedQuery = NormalizeText(query);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(normalizedQuery))
            return 0;

        var score = 0;
        if (normalizedTitle == normalizedQuery)
            score += 220;
        else if (normalizedTitle.StartsWith(normalizedQuery + " ", StringComparison.Ordinal) || normalizedTitle.Contains(" " + normalizedQuery + " ", StringComparison.Ordinal))
            score += 140;
        else if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal))
            score += 90;

        score += ScoreTokenOverlap(normalizedQuery, normalizedTitle) * 20;
        return score;
    }

    static ArtistTrackCandidate MatchArtistTrackCandidate(string title, Dictionary<string, ArtistTrackCandidate> candidates)
    {
        var normalizedTitle = NormalizeText(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
            return null;

        foreach (var candidate in candidates.Values)
        {
            var matchScore = ScoreArtistTrackTitleVariant(normalizedTitle, candidate.NormalizedTitle);
            if (matchScore > 0)
                return candidate;
        }

        return null;
    }

    static int ScoreArtistTrackTitleVariant(string normalizedTitle, string candidateTitle)
    {
        if (string.IsNullOrWhiteSpace(normalizedTitle) || string.IsNullOrWhiteSpace(candidateTitle))
            return 0;

        if (normalizedTitle == candidateTitle)
            return 220;

        if (!normalizedTitle.StartsWith(candidateTitle + " ", StringComparison.Ordinal))
            return 0;

        var suffix = normalizedTitle[(candidateTitle.Length + 1)..];
        return suffix switch
        {
            "edited version" => 180,
            "single version" => 175,
            "album version" => 170,
            "radio version" => 165,
            _ => 0
        };
    }

    static int ScoreArtistTrackCandidate(
        JsonObject item,
        MusicTrack track,
        ArtistTrackCandidate candidate,
        MusicArtist bestArtist,
        HashSet<string> preferredAlbumTitles,
        int artistMatch)
    {
        var titleScore = ScoreArtistTrackTitleVariant(NormalizeText(track.title), candidate.NormalizedTitle);
        if (titleScore == 0)
            return int.MinValue;

        var score = titleScore;
        score += artistMatch * 3;
        score += (ParseInt(item["score"]) ?? 0) * 2;

        var releases = item["releases"] as JsonArray;
        if (HasExactReleaseTitle(releases, candidate.Title))
            score += 120;

        var releaseYearDelta = BestReleaseYearDelta(releases, candidate.Year);
        if (releaseYearDelta.HasValue)
            score += Math.Max(0, 40 - (releaseYearDelta.Value * 10));

        if (StartsWithArtist(track.artist_name, bestArtist))
            score += 20;

        if (CountDistinctArtists(item["artist-credit"] as JsonArray) >= 2)
            score += 12;

        if (track.duration_ms.HasValue)
            score += 8;

        var normalizedAlbumTitle = NormalizeText(track.album_title);
        if (!string.IsNullOrWhiteSpace(normalizedAlbumTitle) && preferredAlbumTitles?.Contains(normalizedAlbumTitle) == true)
            score += 50;

        if (LooksLikeEditionNoise(track.album_title))
            score -= 50;

        if (LooksLikeFeaturedTrackNoise(track.title) || LooksLikeFeaturedTrackNoise(track.album_title))
            score -= 120;

        return score;
    }

    static bool StartsWithArtist(string artistName, MusicArtist bestArtist)
    {
        var normalizedArtist = NormalizeText(artistName);
        var normalizedName = NormalizeText(bestArtist?.name);
        var normalizedSortName = NormalizeText(bestArtist?.sort_name);

        return (!string.IsNullOrWhiteSpace(normalizedName) && normalizedArtist.StartsWith(normalizedName, StringComparison.Ordinal))
            || (!string.IsNullOrWhiteSpace(normalizedSortName) && normalizedArtist.StartsWith(normalizedSortName, StringComparison.Ordinal));
    }

    static bool HasExactReleaseTitle(JsonArray releases, string title)
    {
        var normalizedTitle = NormalizeText(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle) || releases == null)
            return false;

        return releases
            .OfType<JsonObject>()
            .Select(item => NormalizeText(item["title"]?.GetValue<string>()))
            .Any(item => item == normalizedTitle);
    }

    static int? BestReleaseYearDelta(JsonArray releases, int? year)
    {
        if (releases == null || !year.HasValue)
            return null;

        var deltas = releases
            .OfType<JsonObject>()
            .Select(item => ParseYear(item["date"]?.GetValue<string>()))
            .Where(item => item.HasValue)
            .Select(item => Math.Abs(item.Value - year.Value))
            .ToList();

        return deltas.Count == 0 ? null : deltas.Min();
    }

    static int CountDistinctArtists(JsonArray artistCredit)
    {
        if (artistCredit == null)
            return 0;

        return artistCredit
            .OfType<JsonObject>()
            .Select(item => NormalizeText(item["name"]?.GetValue<string>() ?? item["artist"]?["name"]?.GetValue<string>()))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    static bool IsExactQueryTitle(string value, SearchContext context)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(context?.NormalizedQuery)
            && NormalizeText(value) == context.NormalizedQuery;
    }

    static int AlbumSortYear(MusicAlbum album, SearchContext context)
    {
        if (context?.BestArtist == null && IsExactQueryTitle(album.title, context))
            return album.year ?? 9999;

        return -(album.year ?? 0);
    }

    static int TrackSortYear(MusicTrack track, SearchContext context)
    {
        var year = ParseYear(track.date) ?? 9999;
        if (context?.BestArtist == null && IsExactQueryTitle(track.title, context))
            return year;

        return -year;
    }

    static int ScoreArtistMatch(string artistName, MusicArtist artist)
    {
        if (artist == null || string.IsNullOrWhiteSpace(artistName))
            return 0;

        var normalizedArtist = NormalizeText(artistName);
        var normalizedName = NormalizeText(artist.name);
        var normalizedSortName = NormalizeText(artist.sort_name);

        if (normalizedArtist == normalizedName || normalizedArtist == normalizedSortName)
            return 100;

        if (!string.IsNullOrWhiteSpace(normalizedName) && normalizedArtist.Contains(normalizedName, StringComparison.Ordinal))
            return 70;

        if (!string.IsNullOrWhiteSpace(normalizedSortName) && normalizedArtist.Contains(normalizedSortName, StringComparison.Ordinal))
            return 55;

        return ScoreTokenOverlap(normalizedArtist, normalizedName) * 10;
    }

    static int ScoreTokenOverlap(string left, string right)
    {
        var leftTokens = SplitTokens(left);
        var rightTokens = SplitTokens(right);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
            return 0;

        return leftTokens.Count(token => rightTokens.Contains(token));
    }

    static HashSet<string> SplitTokens(string value)
    {
        return NormalizeText(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    static string NormalizeText(string value)
    {
        return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9а-яёіїєґ]+", " ")
            .Trim();
    }

    static string QuoteSearchTerm(string value)
    {
        value = (value ?? string.Empty).Trim().Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{value}\"";
    }

    static List<string> ParseStringList(JsonArray items)
    {
        if (items == null)
            return new List<string>();

        return items
            .Select(item => item?.GetValue<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    static IEnumerable<JsonObject> RankReleaseCandidates(JsonArray releases)
    {
        return releases?
            .OfType<JsonObject>()
            .OrderByDescending(item => ScoreReleaseCandidate(item))
            .ThenBy(item => item["date"]?.GetValue<string>() ?? "9999")
            ?? Enumerable.Empty<JsonObject>();
    }

    static int ScoreReleaseCandidate(JsonObject release)
    {
        if (release == null)
            return int.MinValue;

        var score = 0;
        if (string.Equals(release["status"]?.GetValue<string>(), "Official", StringComparison.OrdinalIgnoreCase))
            score += 200;

        score += Math.Min(180, ReadTrackCount(release) * 10);

        var title = release["title"]?.GetValue<string>();
        var comment = release["disambiguation"]?.GetValue<string>();
        if (LooksLikeEditionNoise(title) || LooksLikeEditionNoise(comment))
            score -= 80;

        if (!string.IsNullOrWhiteSpace(release["country"]?.GetValue<string>()))
            score += 10;

        if (!string.IsNullOrWhiteSpace(release["date"]?.GetValue<string>()))
            score += 5;

        return score;
    }

    static int ScoreParsedRelease(MusicAlbum album, JsonObject release)
    {
        if (album == null)
            return int.MinValue;

        var score = ScoreReleaseCandidate(release);
        score += Math.Min(240, (album.tracks?.Count ?? 0) * 18);

        if (album.type == "Album")
            score += 35;
        else if (album.type == "EP")
            score += 20;
        else if (album.type == "Single")
            score += 5;

        if (LooksLikeEditionNoise(album.title) || LooksLikeEditionNoise(album.description))
            score -= 60;

        return score;
    }

    static int ReadTrackCount(JsonObject release)
    {
        var media = release?["media"] as JsonArray;
        if (media == null)
            return 0;

        return media
            .OfType<JsonObject>()
            .Sum(item => ParseInt(item["track-count"]) ?? 0);
    }

    static bool LooksLikeEditionNoise(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Regex.IsMatch(value, @"(?i)\b(deluxe|remaster|anniversary|collector|bonus|expanded|instrumental|karaoke|tribute|radio edit|clean|explicit|commentary|acappella|a cappella|remix|mix|edit|edited|video|live|bootleg|mixtape|street)\b");
    }

    static bool LooksLikeTrackNoise(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Regex.IsMatch(value, @"(?i)\b(live|remix|karaoke|instrumental|demo|radio edit|clean|explicit|acappella|a cappella)\b");
    }

    static bool LooksLikeFeaturedTrackNoise(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Regex.IsMatch(value, @"(?i)\b(live|remix|karaoke|instrumental|demo|acappella|a cappella|bootleg|mix)\b");
    }

    static bool LooksLikeCandidateSingleNoise(string value, MusicArtist bestArtist)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (LooksLikeEditionNoise(value))
            return true;

        if (Regex.IsMatch(value, @"(?i)\b(feat\.?|ft\.?|featuring|cypher|bootleg)\b"))
            return true;

        if (value.Contains('/') || value.Contains('\\'))
            return true;

        if (Regex.IsMatch(value, @"(?i)\bgodzilla\s*\d+\b"))
            return true;

        var normalizedTitle = NormalizeText(value);
        var normalizedArtist = NormalizeText(bestArtist?.name);
        if (!string.IsNullOrWhiteSpace(normalizedArtist) && normalizedTitle.StartsWith(normalizedArtist + " ", StringComparison.Ordinal))
            return true;

        return false;
    }

    static List<MusicImage> BuildReleaseGroupImages(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new List<MusicImage>();

        return new List<MusicImage>
        {
            new() { url = $"https://coverartarchive.org/release-group/{id}/front-250", width = 250, height = 250 }
        };
    }

    static List<MusicImage> BuildReleaseImages(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new List<MusicImage>();

        return new List<MusicImage>
        {
            new() { url = $"https://coverartarchive.org/release/{id}/front-250", width = 250, height = 250 }
        };
    }

    static (string id, string name) FirstArtist(JsonArray artistCredit)
    {
        var artist = artistCredit?
            .OfType<JsonObject>()
            .Select(item => item["artist"] as JsonObject)
            .FirstOrDefault(item => item != null);

        return artist == null
            ? default
            : (artist["id"]?.GetValue<string>(), artist["name"]?.GetValue<string>());
    }

    static string JoinArtistCredit(JsonArray artistCredit)
    {
        if (artistCredit == null)
            return null;

        return string.Concat(artistCredit.OfType<JsonObject>().Select(item =>
            (item["name"]?.GetValue<string>() ?? item["artist"]?["name"]?.GetValue<string>() ?? string.Empty) +
            (item["joinphrase"]?.GetValue<string>() ?? string.Empty)));
    }

    static List<string> ParseArtistNames(JsonArray artistCredit)
    {
        if (artistCredit == null)
            return new List<string>();

        return artistCredit
            .OfType<JsonObject>()
            .Select(item => item["artist"]?["name"]?.GetValue<string>() ?? item["name"]?.GetValue<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static int? ParseYear(string date)
    {
        if (!string.IsNullOrWhiteSpace(date) && date.Length >= 4 && int.TryParse(date[..4], out var year))
            return year;

        return null;
    }

    static int? ParseInt(JsonNode value)
    {
        if (value == null)
            return null;

        if (value.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            return value.GetValue<int>();

        return int.TryParse(value.ToString(), out var number) ? number : null;
    }
}
