using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Music.MusicPlaylistImportHelpers;

namespace Music;

// Импорт публичных плейлистов/альбомов Spotify БЕЗ ключей и логина:
// гостевой accessToken со страницы embed-плеера + pathfinder GraphQL
// (persisted queries) — тот же путь, что у веб-плеера Spotify.
// Стримов Spotify не отдаёт (DRM): треки приезжают чистыми метаданными,
// аудио резолвится обычным конвейером (YouTube-матчер), как у VK-чарта.
public static class SpotifySupport
{
    public const string ProviderId = "spotify";
    public const string PlaylistSourceType = "spotify_playlist";
    public const string AlbumSourceType = "spotify_album";
    public const string TracksSectionId = "search:spotify:tracks";
    public const string ArtistsSectionId = "search:spotify:artists";
    public const string AlbumsSectionId = "search:spotify:albums";

    public static bool IsSearchEnabled => ModInit.conf?.spotify_search_fallback_enabled == true;

    const string PathfinderUrl = "https://api-partner.spotify.com/pathfinder/v1/query";
    const string PathfinderV2Url = "https://api-partner.spotify.com/pathfinder/v2/query";

    // persisted-query хэши веб-плеера; Spotify их периодически ротирует.
    // Симптом ротации: ошибка PersistedQueryNotFound в ответе pathfinder —
    // тогда взять свежие из бандла веб-плеера (или SpotifyScraper/api/pathfinder.py).
    const string FetchPlaylistHash = "a65e12194ed5fc443a1cdebed5fabe33ca5b07b987185d63c72483867ad13cb4";
    const string GetAlbumHash = "b9bfabef66ed756e5e13f68a942deb60bd4125ec1f1be8cc42769dc0259b4b10";
    // pathfinder v2 (POST) хэши веб-плеера, разведаны с живого open.spotify.com
    // (2026-07-20). Питают отдельную Spotify-вкладку поиска + её экраны
    // артиста/альбома. Ротируются как v1-хэши импорта (симптом PersistedQueryNotFound).
    const string FindTopResultsHash = "755858df4daab8d212980b02a81dcf8c9a58447de318b59d07c4651a1d0450b9";
    const string QueryArtistHash = "a8226439dffdc02caecf1fc6e693e98be57638c0195f002d7802e4b8e5802760";
    const string QueryAlbumHash = "ce390dbf7ca6b61a23aec210619e1094fe9d23d7f101ff773ce1146f84d4dd10";
    const string QueryArtistDiscographyHash = "5e07d323febb57b4a56a42abbf781490e58764aa45feb6e3dc0591564fc56599";
    const string QueryArtistAppearsOnHash = "9a4bb7a20d6720fe52d7b47bc001cfa91940ddf5e7113761460b4a288d18a4c1";
    // embed-страница для гостевого токена: любой публичный плейлист
    const string TokenSeedPlaylistId = "37i9dQZF1DXcBWIGoYBM5M";

    const int PlaylistPageLimit = 100;
    const int AlbumPageLimit = 50;
    const int ArtistOverviewShelfLimit = 20;
    const int MaxImportTracks = 2000;
    const string ArtistDiscographyOrder = "DATE_DESC";
    const string BrowserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient(useCookies: false);
    static readonly Regex entityUrlRegex = new(@"^https?://open\.spotify\.com/(?:intl-[a-z\-]+/)?(playlist|album)/([A-Za-z0-9]{22})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex entityUriRegex = new(@"^spotify:(playlist|album):([A-Za-z0-9]{22})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex accessTokenRegex = new("\"accessToken\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    static readonly Regex accessTokenExpiresRegex = new("\"accessTokenExpirationTimestampMs\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);
    static readonly SemaphoreSlim anonymousTokenLock = new(1, 1);
    static string anonymousToken;
    static DateTime anonymousTokenExpiresAt;

    static SpotifySupport()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public static bool CanHandleUrl(string url) => ParseEntity(url) != null;

    // Поиск треков для отдельной Spotify-вкладки в результатах поиска.
    // Spotify «по-человечески» понимает ввод («рианна», «маданна», опечатки),
    // где MusicBrainz спотыкается. Треки чистые (без стрима) — играются обычным
    // конвейером (YouTube-матчер), как импортные и VK-чарт. Пустой список при
    // любой неудаче (сеть/ротация/пусто) — вкладка просто не появится.
    public static async Task<List<MusicTrack>> SearchTracksAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicTrack>();

        try
        {
            var root = await QueryV2Async("findTopResults", FindTopResultsHash, new
            {
                query,
                numberOfTopResults = Math.Clamp(limit, 1, 30)
            }, cancellationToken);

            var tracksV2 = GetProperty(GetProperty(root, "data", "searchV2") ?? default, "tracksV2");
            if (tracksV2 == null || !tracksV2.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return new List<MusicTrack>();

            var tracks = new List<MusicTrack>();
            foreach (var item in items.EnumerateArray())
            {
                // items[].item.data = Track; альбом/обложка в albumOfTrack
                var data = GetProperty(item, "item", "data");
                if (data == null || !string.Equals(GetString(data.Value, "__typename"), "Track", StringComparison.OrdinalIgnoreCase))
                    continue;

                var album = GetProperty(data.Value, "albumOfTrack");
                string albumTitle = album != null ? GetString(album.Value, "name") : null;
                var images = album != null ? MapCoverArt(GetProperty(album.Value, "coverArt")) : null;

                var mapped = MapTrackElement(data.Value, albumTitle, images, date: null, durationProperty: "duration");
                if (mapped != null)
                    tracks.Add(mapped);

                if (tracks.Count >= limit)
                    break;
            }

            return DeduplicateTracks(tracks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new List<MusicTrack>();
        }
    }

    public static async Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(string inputUrl, CancellationToken cancellationToken = default)
    {
        var entity = ParseEntity(inputUrl);
        if (entity == null)
            return ImportUnavailable("Вставь ссылку на Spotify плейлист или альбом.");

        // прогрев кэша токена + дружелюбная ошибка, если Spotify недоступен
        string token = await GetAnonymousTokenAsync(entity.Value.type, entity.Value.id, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return ImportUnavailable("Не удалось получить Spotify токен.");

        try
        {
            return entity.Value.type == "album"
                ? await ImportAlbumAsync(entity.Value.id, cancellationToken)
                : await ImportPlaylistByIdAsync(entity.Value.id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] spotify import failed: {ex.Message}");
            return ImportUnavailable("Spotify не ответил, попробуй ещё раз.");
        }
    }

    public static Task<MusicUserPlaylistImportResult> ImportPlaylistAsync(MusicUserPlaylistSource source, CancellationToken cancellationToken = default)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return Task.FromResult(ImportUnavailable("У плейлиста нет Spotify источника."));

        return ImportPlaylistAsync(source.url, cancellationToken);
    }

    static (string type, string id)? ParseEntity(string value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = entityUriRegex.Match(value);
        if (!match.Success)
            match = entityUrlRegex.Match(value);

        if (!match.Success)
            return null;

        return (match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value);
    }

    // Гостевой токен живёт на любой публичной embed-странице (~1 час);
    // логина/ключей не требует — это тот же токен, что получает браузер инкогнито.
    static async Task<string> GetAnonymousTokenAsync(string entityType, string entityId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(anonymousToken) && anonymousTokenExpiresAt > DateTime.UtcNow)
            return anonymousToken;

        await anonymousTokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(anonymousToken) && anonymousTokenExpiresAt > DateTime.UtcNow)
                return anonymousToken;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://open.spotify.com/embed/{entityType}/{entityId}");
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenMatch = accessTokenRegex.Match(html ?? string.Empty);
            if (!tokenMatch.Success)
                return null;

            var expiresMatch = accessTokenExpiresRegex.Match(html);
            DateTime expiresAt = expiresMatch.Success && long.TryParse(expiresMatch.Groups[1].Value, out long expiresMs)
                ? DateTimeOffset.FromUnixTimeMilliseconds(expiresMs).UtcDateTime.AddMinutes(-2)
                : DateTime.UtcNow.AddMinutes(30);

            anonymousToken = tokenMatch.Groups[1].Value;
            anonymousTokenExpiresAt = expiresAt;
            return anonymousToken;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            anonymousTokenLock.Release();
        }
    }

    static void InvalidateAnonymousToken()
    {
        anonymousToken = null;
        anonymousTokenExpiresAt = DateTime.MinValue;
    }

    // сам берёт токен из кэша; на 401 сбрасывает его и повторяет запрос
    // один раз со свежим — протухший гостевой токен не роняет импорт/sync
    static async Task<JsonElement?> QueryAsync(string operationName, string hash, object variables, string entityType, string entityId, CancellationToken cancellationToken)
    {
        string url = PathfinderUrl
            + "?operationName=" + Uri.EscapeDataString(operationName)
            + "&variables=" + Uri.EscapeDataString(MusicJson.Serialize(variables))
            + "&extensions=" + Uri.EscapeDataString($"{{\"persistedQuery\":{{\"version\":1,\"sha256Hash\":\"{hash}\"}}}}");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await GetAnonymousTokenAsync(entityType, entityId, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("app-platform", "WebPlayer");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                InvalidateAnonymousToken();
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement.Clone();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                string firstError = errors.EnumerateArray().Select(e => GetString(e, "message")).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
                if (firstError?.Contains("PersistedQueryNotFound", StringComparison.OrdinalIgnoreCase) == true)
                    Console.WriteLine("[Music] spotify pathfinder hash rotated — module update required");
                else
                    Console.WriteLine($"[Music] spotify pathfinder error: {firstError}");
                return null;
            }

            return root;
        }

        return null;
    }

    public static bool IsSpotifyArtist(string provider, string id)
        => string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase)
           || (id ?? string.Empty).StartsWith("spotify:artist:", StringComparison.OrdinalIgnoreCase);

    public static bool IsSpotifyAlbum(string provider, string id)
        => (string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase)
            && (id ?? string.Empty).StartsWith("spotify:album:", StringComparison.OrdinalIgnoreCase))
           || (id ?? string.Empty).StartsWith("spotify:album:", StringComparison.OrdinalIgnoreCase);

    public static bool IsSpotifyArtistSection(string provider, string id)
        => string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase)
           && ParseSpotifyArtistSectionId(id) != null;

    // Артисты для Spotify-вкладки поиска (findTopResults -> searchV2.artists).
    public static async Task<List<MusicArtist>> SearchArtistsAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicArtist>();

        try
        {
            var root = await QueryV2Async("findTopResults", FindTopResultsHash, new { query, numberOfTopResults = Math.Clamp(limit, 1, 30) }, cancellationToken);
            var artists = GetProperty(GetProperty(root, "data", "searchV2") ?? default, "artists");
            if (artists == null || !artists.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return new List<MusicArtist>();

            var result = new List<MusicArtist>();
            foreach (var item in items.EnumerateArray())
            {
                var mapped = MapArtistNode(GetProperty(item, "data"));
                if (mapped != null)
                    result.Add(mapped);
                if (result.Count >= limit)
                    break;
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new List<MusicArtist>(); }
    }

    // Альбомы для Spotify-вкладки поиска (findTopResults -> searchV2.albumsV2).
    public static async Task<List<MusicAlbum>> SearchAlbumsAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return new List<MusicAlbum>();

        try
        {
            var root = await QueryV2Async("findTopResults", FindTopResultsHash, new { query, numberOfTopResults = Math.Clamp(limit, 1, 30) }, cancellationToken);
            var albums = GetProperty(GetProperty(root, "data", "searchV2") ?? default, "albumsV2");
            if (albums == null || !albums.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return new List<MusicAlbum>();

            var result = new List<MusicAlbum>();
            foreach (var item in items.EnumerateArray())
            {
                var mapped = MapAlbumNode(GetProperty(item, "data"));
                if (mapped != null)
                    result.Add(mapped);
                if (result.Count >= limit)
                    break;
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return new List<MusicAlbum>(); }
    }

    public static Task<MusicBrowseSection> GetArtistSectionAsync(string sectionId, string page = null, int limit = 20, string artistName = null, CancellationToken cancellationToken = default)
    {
        var parsed = ParseSpotifyArtistSectionId(sectionId);
        if (parsed == null)
            return Task.FromResult<MusicBrowseSection>(null);

        int offset = 0;
        if (!string.IsNullOrWhiteSpace(page) && int.TryParse(page, out int parsedOffset))
            offset = Math.Max(0, parsedOffset);

        limit = Math.Clamp(limit, 1, 50);

        return parsed.Value.sectionKey switch
        {
            "albums" => GetDiscographySectionAsync(parsed.Value.artistId, "albums", "Альбомы", artistName, offset, limit, cancellationToken),
            "singles" => GetDiscographySectionAsync(parsed.Value.artistId, "singles", "Синглы / EP", artistName, offset, limit, cancellationToken),
            "compilations" => GetDiscographySectionAsync(parsed.Value.artistId, "compilations", "Сборники", artistName, offset, limit, cancellationToken),
            "releases" => GetDiscographySectionAsync(parsed.Value.artistId, "releases", "Релизы", artistName, offset, limit, cancellationToken),
            "appears-on" => GetAppearsOnSectionAsync(parsed.Value.artistId, artistName, offset, limit, cancellationToken),
            _ => Task.FromResult<MusicBrowseSection>(null)
        };
    }

    // Экран артиста: профиль + дискография (альбомы+синглы). id = spotify:artist:xxx.
    public static async Task<MusicArtist> GetArtistAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        try
        {
            var root = await QueryV2Async("queryArtist", QueryArtistHash, new { uri = id }, cancellationToken);
            var au = GetProperty(root, "data", "artistUnion");
            if (au == null || au.Value.ValueKind != JsonValueKind.Object)
                return null;

            string name = GetString(GetProperty(au.Value, "profile") ?? default, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var artist = new MusicArtist
            {
                id = id,
                name = name,
                images = MapCoverArt(GetProperty(GetProperty(au.Value, "visuals") ?? default, "avatarImage")),
                provider_refs = new List<MusicProviderRef> { new() { provider = ProviderId, external_id = id } }
            };

            var albumsTask = GetDiscographySectionAsync(id, "albums", "Альбомы", name, 0, ArtistOverviewShelfLimit, cancellationToken);
            var singlesTask = GetDiscographySectionAsync(id, "singles", "Синглы / EP", name, 0, ArtistOverviewShelfLimit, cancellationToken);
            var compilationsTask = GetDiscographySectionAsync(id, "compilations", "Сборники", name, 0, ArtistOverviewShelfLimit, cancellationToken);
            var appearsOnTask = GetAppearsOnSectionAsync(id, name, 0, ArtistOverviewShelfLimit, cancellationToken);

            var disco = GetProperty(au.Value, "discography");
            if (disco != null)
            {
                var topTracks = MapTopTracks(GetProperty(disco.Value, "topTracks"), limit: 12);
                AddTrackSection(artist, $"{id}:top-tracks", "Популярные треки", topTracks);

                var popularReleases = MapDirectAlbumItems(GetProperty(disco.Value, "popularReleasesAlbums"), name, limit: 12);
                AddAlbumSection(artist, $"{id}:popular-releases", "Популярные релизы", popularReleases);
            }

            await Task.WhenAll(albumsTask, singlesTask, compilationsTask, appearsOnTask);

            var albumsSection = await albumsTask;
            var singlesSection = await singlesTask;
            var compilationsSection = await compilationsTask;
            var appearsOnSection = await appearsOnTask;

            AddSectionIfNotEmpty(artist, albumsSection);
            AddSectionIfNotEmpty(artist, singlesSection);
            AddSectionIfNotEmpty(artist, compilationsSection);

            var seenAlbums = new HashSet<string>(StringComparer.Ordinal);
            foreach (var album in (albumsSection?.albums ?? new List<MusicAlbum>())
                .Concat(singlesSection?.albums ?? new List<MusicAlbum>())
                .Concat(compilationsSection?.albums ?? new List<MusicAlbum>()))
            {
                if (album != null && seenAlbums.Add(album.id))
                    artist.albums.Add(album);
            }

            var related = GetProperty(au.Value, "relatedContent");
            if (related != null)
            {
                AddSectionIfNotEmpty(artist, appearsOnSection);

                var relatedArtists = MapRelatedArtists(GetProperty(related.Value, "relatedArtists"), limit: 12);
                AddArtistSection(artist, $"{id}:related-artists", "Похожие артисты", relatedArtists);
            }

            return artist;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    // Экран альбома: мета + треки. id = spotify:album:xxx.
    public static async Task<MusicAlbum> GetAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        try
        {
            var root = await QueryV2Async("queryAlbum", QueryAlbumHash, new { uri = id, offset = 0 }, cancellationToken);
            var au = GetProperty(root, "data", "albumUnion");
            if (au == null || au.Value.ValueKind != JsonValueKind.Object)
                return null;

            string title = GetString(au.Value, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(title))
                return null;

            string artistName = ExtractArtistNames(GetProperty(au.Value, "artists")).FirstOrDefault();
            var images = MapCoverArt(GetProperty(au.Value, "coverArt"));
            string date = GetString(GetProperty(au.Value, "date") ?? default, "isoString");
            int? year = GetInt(GetProperty(au.Value, "date") ?? default, "year");

            var album = new MusicAlbum
            {
                id = id,
                title = title,
                artist_name = string.IsNullOrWhiteSpace(artistName) ? "Spotify" : artistName,
                year = year,
                date = date,
                images = images,
                provider_refs = new List<MusicProviderRef> { new() { provider = ProviderId, external_id = id } }
            };

            var tracksV2 = GetProperty(au.Value, "tracksV2");
            if (tracksV2 != null && tracksV2.Value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var track = GetProperty(item, "track");
                    if (track == null)
                        continue;

                    var mapped = MapTrackElement(track.Value, title, images, date, durationProperty: "duration");
                    if (mapped != null)
                    {
                        mapped.album_title = title;
                        album.tracks.Add(mapped);
                    }
                }
            }

            return album;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    static MusicArtist MapArtistNode(JsonElement? node)
    {
        if (node == null || node.Value.ValueKind != JsonValueKind.Object)
            return null;

        string uri = GetString(node.Value, "uri");
        string name = GetString(GetProperty(node.Value, "profile") ?? default, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(name))
            return null;

        return new MusicArtist
        {
            id = uri,
            name = name,
            images = MapCoverArt(GetProperty(GetProperty(node.Value, "visuals") ?? default, "avatarImage")),
            provider_refs = new List<MusicProviderRef> { new() { provider = ProviderId, external_id = uri } }
        };
    }

    static MusicAlbum MapAlbumNode(JsonElement? node)
    {
        if (node == null || node.Value.ValueKind != JsonValueKind.Object)
            return null;

        string uri = GetString(node.Value, "uri");
        string title = GetString(node.Value, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(title))
            return null;

        return new MusicAlbum
        {
            id = uri,
            title = title,
            artist_name = ExtractArtistNames(GetProperty(node.Value, "artists")).FirstOrDefault() ?? "Spotify",
            year = GetInt(GetProperty(node.Value, "date") ?? default, "year"),
            images = MapCoverArt(GetProperty(node.Value, "coverArt")),
            provider_refs = new List<MusicProviderRef> { new() { provider = ProviderId, external_id = uri } }
        };
    }

    static MusicAlbum MapDiscographyRelease(JsonElement release, string artistName)
    {
        string uri = GetString(release, "uri");
        string title = GetString(release, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(title))
            return null;

        return new MusicAlbum
        {
            id = uri,
            title = title,
            artist_name = string.IsNullOrWhiteSpace(artistName) ? null : artistName,
            year = GetInt(GetProperty(release, "date") ?? default, "year"),
            type = GetString(release, "type"),
            images = MapCoverArt(GetProperty(release, "coverArt")),
            provider_refs = new List<MusicProviderRef> { new() { provider = ProviderId, external_id = uri } }
        };
    }

    static async Task<MusicBrowseSection> GetDiscographySectionAsync(string artistId, string sectionKey, string title, string artistName, int offset, int limit, CancellationToken cancellationToken)
    {
        try
        {
            string operationName = sectionKey switch
            {
                "albums" => "queryArtistDiscographyAlbums",
                "singles" => "queryArtistDiscographySingles",
                "compilations" => "queryArtistDiscographyCompilations",
                "releases" => "queryArtistDiscographyAll",
                _ => null
            };

            string responseKey = sectionKey == "releases" ? "all" : sectionKey;
            if (string.IsNullOrWhiteSpace(operationName))
                return null;

            var root = await QueryV2Async(operationName, QueryArtistDiscographyHash, new
            {
                uri = artistId,
                offset,
                limit,
                order = ArtistDiscographyOrder
            }, cancellationToken);

            var group = GetProperty(root, "data", "artistUnion", "discography", responseKey);
            if (group == null)
                return null;

            int totalCount = GetInt(group.Value, "totalCount") ?? offset + limit;

            if (sectionKey == "singles")
            {
                var tracks = MapDiscographySingleTracks(group, artistName, limit);
                return BuildTrackSection(
                    BuildSpotifyArtistSectionId(artistId, sectionKey),
                    title,
                    tracks,
                    totalCount > offset + tracks.Count,
                    NextOffset(offset, tracks.Count, totalCount)
                );
            }

            var albums = MapDiscographyGroup(group, artistName, limit);

            return BuildAlbumSection(
                BuildSpotifyArtistSectionId(artistId, sectionKey),
                title,
                albums,
                totalCount > offset + albums.Count,
                NextOffset(offset, albums.Count, totalCount)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    static async Task<MusicBrowseSection> GetAppearsOnSectionAsync(string artistId, string artistName, int offset, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var root = await QueryV2Async("queryArtistAppearsOn", QueryArtistAppearsOnHash, new { uri = artistId }, cancellationToken);
            var group = GetProperty(root, "data", "artistUnion", "relatedContent", "appearsOn");
            if (group == null)
                return null;

            var allAlbums = MapDiscographyGroup(group, artistName, 50);
            var pageAlbums = allAlbums.Skip(offset).Take(limit).ToList();
            int totalCount = allAlbums.Count;

            return BuildAlbumSection(
                BuildSpotifyArtistSectionId(artistId, "appears-on"),
                "Релизы с участием",
                pageAlbums,
                totalCount > offset + pageAlbums.Count,
                NextOffset(offset, pageAlbums.Count, totalCount)
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    static List<MusicTrack> MapTopTracks(JsonElement? topTracks, int limit)
    {
        var result = new List<MusicTrack>();
        if (topTracks == null || !topTracks.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in items.EnumerateArray())
        {
            var track = GetProperty(item, "track");
            if (track == null)
                continue;

            var album = GetProperty(track.Value, "albumOfTrack");
            string albumTitle = album != null ? GetString(album.Value, "name") : null;
            var images = album != null ? MapCoverArt(GetProperty(album.Value, "coverArt")) : null;

            var mapped = MapTrackElement(track.Value, albumTitle, images, date: null, durationProperty: "duration");
            if (mapped != null)
                result.Add(mapped);

            if (result.Count >= limit)
                break;
        }

        return DeduplicateTracks(result);
    }

    static List<MusicAlbum> MapDirectAlbumItems(JsonElement? container, string artistName, int limit)
    {
        var result = new List<MusicAlbum>();
        if (container == null || !container.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            var album = MapDiscographyRelease(item, artistName);
            if (album != null && seen.Add(album.id))
                result.Add(album);

            if (result.Count >= limit)
                break;
        }

        return result;
    }

    static List<MusicAlbum> MapDiscographyGroup(JsonElement? groupNode, string artistName, int limit)
    {
        var result = new List<MusicAlbum>();
        if (groupNode == null || !groupNode.Value.TryGetProperty("items", out var relItems) || relItems.ValueKind != JsonValueKind.Array)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relItem in relItems.EnumerateArray())
        {
            var releases = GetProperty(GetProperty(relItem, "releases") ?? default, "items");
            if (releases == null || releases.Value.ValueKind != JsonValueKind.Array || releases.Value.GetArrayLength() == 0)
                continue;

            var album = MapDiscographyRelease(releases.Value[0], artistName);
            if (album != null && seen.Add(album.id))
                result.Add(album);

            if (result.Count >= limit)
                break;
        }

        return result;
    }

    static List<MusicTrack> MapDiscographySingleTracks(JsonElement? groupNode, string artistName, int limit)
    {
        var result = new List<MusicTrack>();
        if (groupNode == null || !groupNode.Value.TryGetProperty("items", out var relItems) || relItems.ValueKind != JsonValueKind.Array)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relItem in relItems.EnumerateArray())
        {
            var releases = GetProperty(GetProperty(relItem, "releases") ?? default, "items");
            if (releases == null || releases.Value.ValueKind != JsonValueKind.Array || releases.Value.GetArrayLength() == 0)
                continue;

            var track = MapDiscographySingleTrack(releases.Value[0], artistName);
            string key = track?.album_id ?? track?.id;
            if (track != null && !string.IsNullOrWhiteSpace(key) && seen.Add(key))
                result.Add(track);

            if (result.Count >= limit)
                break;
        }

        return result;
    }

    static MusicTrack MapDiscographySingleTrack(JsonElement release, string artistName)
    {
        string albumUri = GetString(release, "uri");
        string albumId = GetString(release, "id");
        string title = GetString(release, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(albumUri) || string.IsNullOrWhiteSpace(title))
            return null;

        string date = GetString(GetProperty(release, "date") ?? default, "isoString");
        var images = MapCoverArt(GetProperty(release, "coverArt"));
        string releaseTrackId = !string.IsNullOrWhiteSpace(albumId)
            ? $"spotify:release:{albumId}"
            : albumUri.Replace("spotify:album:", "spotify:release:", StringComparison.OrdinalIgnoreCase);

        return new MusicTrack
        {
            id = releaseTrackId,
            title = title,
            artist_name = string.IsNullOrWhiteSpace(artistName) ? "Spotify" : artistName,
            artists = string.IsNullOrWhiteSpace(artistName) ? new List<string>() : new List<string> { artistName },
            album_id = albumUri,
            album_title = title,
            date = date,
            track_number = 1,
            images = images,
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = albumUri }
            }
        };
    }

    static List<MusicArtist> MapRelatedArtists(JsonElement? relatedArtists, int limit)
    {
        var result = new List<MusicArtist>();
        if (relatedArtists == null || !relatedArtists.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            var artist = MapArtistNode(item);
            if (artist != null && seen.Add(artist.id))
                result.Add(artist);

            if (result.Count >= limit)
                break;
        }

        return result;
    }

    static void AddSectionIfNotEmpty(MusicArtist artist, MusicBrowseSection section)
    {
        if (artist == null || section == null)
            return;

        bool hasEntries = section.tracks?.Count > 0
            || section.albums?.Count > 0
            || section.artists?.Count > 0;

        if (hasEntries)
            artist.sections.Add(section);
    }

    static void AddTrackSection(MusicArtist artist, string id, string title, List<MusicTrack> tracks)
    {
        if (artist == null || tracks == null || tracks.Count == 0)
            return;

        artist.sections.Add(new MusicBrowseSection
        {
            id = id,
            title = title,
            type = "tracks",
            source_provider = ProviderId,
            has_more = false,
            tracks = tracks
        });
    }

    static MusicBrowseSection BuildAlbumSection(string id, string title, List<MusicAlbum> albums, bool hasMore, string nextPage)
    {
        return new MusicBrowseSection
        {
            id = id,
            title = title,
            type = "albums",
            source_provider = ProviderId,
            has_more = hasMore,
            next_page = nextPage,
            albums = albums ?? new List<MusicAlbum>()
        };
    }

    static MusicBrowseSection BuildTrackSection(string id, string title, List<MusicTrack> tracks, bool hasMore, string nextPage)
    {
        return new MusicBrowseSection
        {
            id = id,
            title = title,
            type = "tracks",
            source_provider = ProviderId,
            has_more = hasMore,
            next_page = nextPage,
            tracks = tracks ?? new List<MusicTrack>()
        };
    }

    static void AddAlbumSection(MusicArtist artist, string id, string title, List<MusicAlbum> albums)
    {
        if (artist == null || albums == null || albums.Count == 0)
            return;

        artist.sections.Add(new MusicBrowseSection
        {
            id = id,
            title = title,
            type = "albums",
            source_provider = ProviderId,
            has_more = false,
            albums = albums
        });
    }

    static void AddArtistSection(MusicArtist artist, string id, string title, List<MusicArtist> artists)
    {
        if (artist == null || artists == null || artists.Count == 0)
            return;

        artist.sections.Add(new MusicBrowseSection
        {
            id = id,
            title = title,
            type = "artists",
            source_provider = ProviderId,
            has_more = false,
            artists = artists
        });
    }

    static string BuildSpotifyArtistSectionId(string artistId, string sectionKey)
        => $"{artistId}:{sectionKey}";

    static (string artistId, string sectionKey)? ParseSpotifyArtistSectionId(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return null;

        int separator = sectionId.LastIndexOf(':');
        if (separator <= 0 || separator >= sectionId.Length - 1)
            return null;

        string artistId = sectionId.Substring(0, separator);
        string sectionKey = sectionId.Substring(separator + 1);
        if (!artistId.StartsWith("spotify:artist:", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(sectionKey))
            return null;

        return (artistId, sectionKey);
    }

    static string NextOffset(int offset, int count, int totalCount)
    {
        int next = offset + Math.Max(0, count);
        return count > 0 && totalCount > next ? next.ToString() : null;
    }

    // pathfinder v2: POST с телом {variables, operationName, extensions}.
    // Отдельный транспорт от v1-QueryAsync (тот GET и для импорта). Токен —
    // тот же гостевой, с общей embed-страницы; на 401 сброс+повтор.
    static async Task<JsonElement?> QueryV2Async(string operationName, string hash, object variables, CancellationToken cancellationToken)
    {
        string body = MusicJson.Serialize(new
        {
            variables,
            operationName,
            extensions = new { persistedQuery = new { version = 1, sha256Hash = hash } }
        });

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await GetAnonymousTokenAsync("playlist", TokenSeedPlaylistId, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Post, PathfinderV2Url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("app-platform", "WebPlayer");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                InvalidateAnonymousToken();
                continue;
            }

            if (!response.IsSuccessStatusCode)
                return null;

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement.Clone();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                string firstError = errors.EnumerateArray().Select(e => GetString(e, "message")).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
                if (firstError?.Contains("PersistedQueryNotFound", StringComparison.OrdinalIgnoreCase) == true)
                    Console.WriteLine("[Music] spotify pathfinder v2 hash rotated — module update required");
                else
                    Console.WriteLine($"[Music] spotify pathfinder v2 error: {firstError}");
                return null;
            }

            return root;
        }

        return null;
    }

    static async Task<MusicUserPlaylistImportResult> ImportPlaylistByIdAsync(string playlistId, CancellationToken cancellationToken)
    {
        var tracks = new List<MusicTrack>();
        string title = null;
        int totalCount = -1;

        for (int offset = 0; totalCount < 0 || (offset < totalCount && tracks.Count < MaxImportTracks); offset += PlaylistPageLimit)
        {
            var root = await QueryAsync("fetchPlaylist", FetchPlaylistHash, new
            {
                uri = $"spotify:playlist:{playlistId}",
                offset,
                limit = PlaylistPageLimit,
                enableWatchFeedEntrypoint = false
            }, "playlist", playlistId, cancellationToken);

            var playlist = GetProperty(root, "data", "playlistV2");
            if (playlist == null || playlist.Value.ValueKind != JsonValueKind.Object)
            {
                // страница не отдалась — импорт атомарный, усечённый список не сохраняем
                return offset == 0
                    ? ImportUnavailable("Spotify плейлист не найден или приватный.")
                    : ImportUnavailable("Spotify не отдал плейлист целиком, попробуй ещё раз.");
            }

            title ??= GetString(playlist.Value, "name");

            var content = GetProperty(playlist.Value, "content");
            if (content == null)
                return ImportUnavailable("Spotify плейлист не удалось прочитать.");

            if (totalCount < 0)
                totalCount = GetInt(content.Value, "totalCount") ?? 0;

            if (!content.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return ImportUnavailable("Spotify не отдал плейлист целиком, попробуй ещё раз.");

            if (items.GetArrayLength() == 0 && offset < totalCount)
                return ImportUnavailable("Spotify не отдал плейлист целиком, попробуй ещё раз.");

            foreach (var item in items.EnumerateArray())
            {
                var mapped = MapPlaylistItem(item);
                if (mapped != null)
                    tracks.Add(mapped);
            }

            if (totalCount <= 0)
                break;
        }

        tracks = DeduplicateTracks(tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("В Spotify плейлисте не найдено треков.");

        title = string.IsNullOrWhiteSpace(title) ? "Spotify Playlist" : title.Trim();

        return new MusicUserPlaylistImportResult
        {
            available = true,
            title = title,
            track_count = tracks.Count,
            truncated = totalCount > tracks.Count && tracks.Count >= MaxImportTracks,
            tracks = tracks,
            source = new MusicUserPlaylistSource
            {
                type = PlaylistSourceType,
                url = $"https://open.spotify.com/playlist/{playlistId}",
                playlist_id = playlistId,
                title = title
            }
        };
    }

    static async Task<MusicUserPlaylistImportResult> ImportAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        var tracks = new List<MusicTrack>();
        string title = null, artistName = null, date = null;
        List<MusicImage> albumImages = null;
        int totalCount = -1;

        for (int offset = 0; totalCount < 0 || (offset < totalCount && tracks.Count < MaxImportTracks); offset += AlbumPageLimit)
        {
            var root = await QueryAsync("getAlbum", GetAlbumHash, new
            {
                uri = $"spotify:album:{albumId}",
                locale = "",
                offset,
                limit = AlbumPageLimit
            }, "album", albumId, cancellationToken);

            var album = GetProperty(root, "data", "albumUnion");
            if (album == null || album.Value.ValueKind != JsonValueKind.Object)
            {
                return offset == 0
                    ? ImportUnavailable("Spotify альбом не найден.")
                    : ImportUnavailable("Spotify не отдал альбом целиком, попробуй ещё раз.");
            }

            if (title == null)
            {
                title = GetString(album.Value, "name");
                artistName = ExtractArtistNames(GetProperty(album.Value, "artists")).FirstOrDefault();
                date = GetString(GetProperty(album.Value, "date") ?? default, "isoString");
                albumImages = MapCoverArt(GetProperty(album.Value, "coverArt"));
            }

            var tracksV2 = GetProperty(album.Value, "tracksV2");
            if (tracksV2 == null)
                return ImportUnavailable("Spotify альбом не удалось прочитать.");

            if (totalCount < 0)
                totalCount = GetInt(tracksV2.Value, "totalCount") ?? 0;

            if (!tracksV2.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return ImportUnavailable("Spotify не отдал альбом целиком, попробуй ещё раз.");

            if (items.GetArrayLength() == 0 && offset < totalCount)
                return ImportUnavailable("Spotify не отдал альбом целиком, попробуй ещё раз.");

            foreach (var item in items.EnumerateArray())
            {
                var track = GetProperty(item, "track");
                if (track == null)
                    continue;

                var mapped = MapTrackElement(track.Value, title, albumImages, date, durationProperty: "duration");
                if (mapped != null)
                    tracks.Add(mapped);
            }

            if (totalCount <= 0)
                break;
        }

        tracks = DeduplicateTracks(tracks);
        if (tracks.Count == 0)
            return ImportUnavailable("В Spotify альбоме не найдено треков.");

        title = string.IsNullOrWhiteSpace(title) ? "Spotify Album" : title.Trim();
        string playlistTitle = string.IsNullOrWhiteSpace(artistName) ? title : $"{artistName} — {title}";

        return new MusicUserPlaylistImportResult
        {
            available = true,
            title = playlistTitle,
            track_count = tracks.Count,
            tracks = tracks,
            source = new MusicUserPlaylistSource
            {
                type = AlbumSourceType,
                url = $"https://open.spotify.com/album/{albumId}",
                playlist_id = albumId,
                title = playlistTitle
            }
        };
    }

    static MusicTrack MapPlaylistItem(JsonElement item)
    {
        var data = GetProperty(item, "itemV2", "data");
        if (data == null || !string.Equals(GetString(data.Value, "__typename"), "Track", StringComparison.OrdinalIgnoreCase))
            return null;

        var album = GetProperty(data.Value, "albumOfTrack");
        string albumTitle = album != null ? GetString(album.Value, "name") : null;
        var images = album != null ? MapCoverArt(GetProperty(album.Value, "coverArt")) : null;

        return MapTrackElement(data.Value, albumTitle, images, date: null, durationProperty: "trackDuration");
    }

    static MusicTrack MapTrackElement(JsonElement track, string albumTitle, List<MusicImage> images, string date, string durationProperty)
    {
        string title = GetString(track, "name")?.Trim();
        string uri = GetString(track, "uri");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(uri))
            return null;

        var artists = ExtractArtistNames(GetProperty(track, "artists"));
        var duration = GetProperty(track, durationProperty);

        return new MusicTrack
        {
            id = uri,
            title = title,
            artist_name = artists.Count > 0 ? string.Join(", ", artists) : "Spotify",
            artists = artists,
            album_title = string.IsNullOrWhiteSpace(albumTitle) ? null : albumTitle.Trim(),
            duration_ms = duration != null ? GetInt(duration.Value, "totalMilliseconds") : null,
            track_number = GetInt(track, "trackNumber"),
            disc_number = GetInt(track, "discNumber"),
            date = date,
            images = images?.ToList() ?? new List<MusicImage>(),
            provider_refs = new List<MusicProviderRef>
            {
                new() { provider = ProviderId, external_id = uri }
            }
        };
    }

    static List<string> ExtractArtistNames(JsonElement? artists)
    {
        var result = new List<string>();
        if (artists == null || !artists.Value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in items.EnumerateArray())
        {
            string name = GetString(GetProperty(item, "profile") ?? default, "name")?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name, StringComparer.OrdinalIgnoreCase))
                result.Add(name);
        }

        return result;
    }

    static List<MusicImage> MapCoverArt(JsonElement? coverArt)
    {
        var result = new List<MusicImage>();
        if (coverArt == null || !coverArt.Value.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var source in sources.EnumerateArray())
        {
            string url = GetString(source, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;

            result.Add(new MusicImage
            {
                url = url,
                width = GetInt(source, "width"),
                height = GetInt(source, "height")
            });
        }

        // крупные первыми — как отдаёт SoundCloud-маппер
        return result.OrderByDescending(i => i.width ?? 0).ToList();
    }

}
