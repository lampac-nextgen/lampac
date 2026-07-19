using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Music;

public static class MusicRadioService
{
    static readonly TimeSpan searchCacheTtl = TimeSpan.FromHours(6);
    static readonly TimeSpan radioTimeout = TimeSpan.FromSeconds(8);
    const int maxSeedArtists = 5;
    const int providerQueryLimit = 12;
    const int maxPoolPerArtist = 24;
    const string cacheVersion = "radio-v2";

    public static async Task<MusicRadioResponse> GetAsync(string profileId, MusicRadioRequest request, int limit = 20, CancellationToken cancellationToken = default)
    {
        request ??= new MusicRadioRequest();
        limit = Math.Clamp(limit, 5, 30);

        var seeds = NormalizeTrackList(request.seeds);
        var exclude = NormalizeTrackList(request.exclude);
        if (seeds.Count == 0)
            seeds = exclude.TakeLast(Math.Min(5, exclude.Count)).ToList();

        var artists = ExtractSeedArtists(seeds);
        if (artists.Count == 0)
            return Unavailable("Недостаточно данных для радио.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(radioTimeout);

        // сиды исключаем всегда, даже если клиент прислал пустой exclude —
        // иначе радио может вернуть сам сид-трек под другим id
        var excludedIds = new HashSet<string>(exclude.Concat(seeds).Select(i => i.id).Where(i => !string.IsNullOrWhiteSpace(i)), StringComparer.OrdinalIgnoreCase);
        var excludedKeys = new HashSet<string>(exclude.Concat(seeds).Select(BuildTrackDedupeKey).Where(i => !string.IsNullOrWhiteSpace(i)), StringComparer.OrdinalIgnoreCase);
        var excludedTitles = new HashSet<string>(exclude.Concat(seeds).Select(BuildTrackTitleKey).Where(i => !string.IsNullOrWhiteSpace(i)), StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var recent in await MusicPlaybackHistoryService.GetRecentAsync(profileId, 150, timeoutCts.Token))
            {
                if (recent == null || recent.played_at.ToUniversalTime() < DateTime.UtcNow.AddDays(-1))
                    continue;

                if (!string.IsNullOrWhiteSpace(recent.track?.id))
                    excludedIds.Add(recent.track.id);

                string key = BuildTrackDedupeKey(recent.track);
                if (!string.IsNullOrWhiteSpace(key))
                    excludedKeys.Add(key);

                string titleKey = BuildTrackTitleKey(recent.track);
                if (!string.IsNullOrWhiteSpace(titleKey))
                    excludedTitles.Add(titleKey);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        var output = new List<MusicTrack>();
        var outputIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // «шумные» слова допустимы в кандидате, только если были в самом сиде
        // (слушаешь remix — получай remix, иначе это мусор)
        var seedNoisyWords = CollectSeedNoisyWords(seeds);
        // script-affinity включаем только когда ВЕСЬ сид-контекст кириллический:
        // при Any смешанная очередь (Eminem + Miyagi) резала бы латинские
        // кандидаты целиком, включая related-пул латинского сида
        bool requireCyrillic = seeds.Count > 0
            && seeds.All(seed => ContainsCyrillic(seed?.title) || ContainsCyrillic(seed?.artist_name));

        // слой 1: рекомендации SoundCloud ПО ТРЕКУ (related/station) — живое
        // радио вместо «топ треков артиста»; artist-пулы остаются как backfill
        var relatedTasks = SoundCloudSupport.IsDiscoveryEnabled
            ? seeds.Take(3).Select(seed => LoadRelatedPoolAsync(seed, timeoutCts.Token)).ToList()
            : new List<Task<List<MusicTrack>>>();

        try
        {
            await Task.WhenAll(relatedTasks);
        }
        catch
        {
        }

        static List<List<MusicTrack>> CollectPools(IEnumerable<Task<List<MusicTrack>>> tasks) => tasks
            .Where(task => task.IsCompletedSuccessfully)
            .Select(task => task.Result ?? new List<MusicTrack>())
            .Where(pool => pool.Count > 0)
            .ToList();

        FillRoundRobin(CollectPools(relatedTasks), output, limit, seedNoisyWords, requireCyrillic, excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles);

        if (output.Count < limit)
        {
            // Backfill is intentionally lazy: if SoundCloud related/station gave
            // enough tracks, do not spend requests on generic artist search.
            var artistTasks = artists.Select(artist => LoadArtistPoolAsync(artist, timeoutCts.Token)).ToList();
            try
            {
                await Task.WhenAll(artistTasks);
            }
            catch
            {
            }

            FillRoundRobin(CollectPools(artistTasks), output, limit, seedNoisyWords, requireCyrillic, excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles);
        }

        return output.Count >= Math.Min(limit, 8)
            ? Available(output)
            : Unavailable("Радио не нашло достаточно похожих треков.");
    }

    // round-robin по пулам: 1-й трек каждого пула, потом 2-е и т.д. — иначе
    // первый пул целиком заполняет лимит и «радио» вырождается в один источник
    static void FillRoundRobin(
        List<List<MusicTrack>> pools,
        List<MusicTrack> output,
        int limit,
        HashSet<string> seedNoisyWords,
        bool requireCyrillic,
        HashSet<string> excludedIds,
        HashSet<string> excludedKeys,
        HashSet<string> excludedTitles,
        HashSet<string> outputIds,
        HashSet<string> outputKeys,
        HashSet<string> outputTitles)
    {
        for (int position = 0; output.Count < limit; position++)
        {
            bool anyLeft = false;

            foreach (var pool in pools)
            {
                if (position >= pool.Count)
                    continue;

                anyLeft = true;
                var candidate = NormalizeCandidateMetadata(pool[position]);

                if (!AcceptCandidate(candidate, seedNoisyWords, requireCyrillic, excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles))
                    continue;

                candidate.auto_radio = true;
                output.Add(candidate);

                if (output.Count >= limit)
                    break;
            }

            if (!anyLeft)
                break;
        }
    }

    static MusicRadioResponse Available(List<MusicTrack> tracks) => new()
    {
        available = true,
        tracks = tracks ?? new List<MusicTrack>()
    };

    static MusicRadioResponse Unavailable(string message) => new()
    {
        available = false,
        message = message
    };

    static List<MusicTrack> NormalizeTrackList(IEnumerable<MusicTrack> tracks)
    {
        var result = new List<MusicTrack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks ?? Enumerable.Empty<MusicTrack>())
        {
            if (track == null)
                continue;

            string id = (track.id ?? string.Empty).Trim();
            string key = !string.IsNullOrWhiteSpace(id)
                ? id
                : BuildTrackDedupeKey(track);

            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                continue;

            if (!string.IsNullOrWhiteSpace(id))
                track.id = id;

            result.Add(track);
        }

        return result;
    }

    static List<string> ExtractSeedArtists(IEnumerable<MusicTrack> seeds)
    {
        var artists = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in seeds ?? Enumerable.Empty<MusicTrack>())
        {
            foreach (var artist in EnumerateTrackArtists(track))
            {
                string value = CleanupArtistName(artist);
                if (string.IsNullOrWhiteSpace(value) || !seen.Add(NormalizeText(value)))
                    continue;

                artists.Add(value);
                if (artists.Count >= maxSeedArtists)
                    return artists;
            }
        }

        return artists;
    }

    static IEnumerable<string> EnumerateTrackArtists(MusicTrack track)
    {
        if (!string.IsNullOrWhiteSpace(track?.artist_name))
            yield return track.artist_name;

        foreach (var artist in track?.artists ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(artist))
                yield return artist;
        }
    }

    static string CleanupArtistName(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = Regex.Replace(value, @"\s+(feat\.?|ft\.?|featuring|with|vs\.?)\s+.*$", "", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value;
    }

    static Task<List<MusicTrack>> LoadRelatedPoolAsync(MusicTrack seed, CancellationToken cancellationToken)
    {
        string artist = CleanupArtistName(seed?.artist_name);
        string title = (seed?.title ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return Task.FromResult(new List<MusicTrack>());

        string query = $"{artist} {title}";

        return LoadCachedTracksAsync(
            SoundCloudSupport.DiscoveryProviderId,
            query,
            token => SoundCloudSupport.FindRelatedTracksByQueryAsync(query, artist, title, maxPoolPerArtist, token),
            cancellationToken,
            cacheType: "radio_related",
            cacheLimit: maxPoolPerArtist);
    }

    static async Task<List<MusicTrack>> LoadArtistPoolAsync(string artist, CancellationToken cancellationToken)
    {
        var tasks = new List<Task<List<MusicTrack>>>();

        if (YouTubeMusicSearchSupport.IsSearchEnabled)
            tasks.Add(LoadCachedTracksAsync(
                YouTubeMusicSearchSupport.ProviderId,
                artist,
                token => YouTubeMusicSearchSupport.SearchTracksByQueryAsync(artist, providerQueryLimit, token),
                cancellationToken));

        if (SoundCloudSupport.IsDiscoveryEnabled)
            tasks.Add(LoadCachedTracksAsync(
                SoundCloudSupport.DiscoveryProviderId,
                artist,
                token => SoundCloudSupport.SearchTracksByQueryAsync(artist, providerQueryLimit, token),
                cancellationToken));

        if (tasks.Count == 0)
            return new List<MusicTrack>();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }

        return tasks
            .Where(i => i.IsCompletedSuccessfully)
            .SelectMany(i => i.Result ?? new List<MusicTrack>())
            .Where(i => i != null)
            .Take(maxPoolPerArtist)
            .ToList();
    }

    static async Task<List<MusicTrack>> LoadCachedTracksAsync(string providerId, string query, Func<CancellationToken, Task<List<MusicTrack>>> factory, CancellationToken cancellationToken, string cacheType = "radio_tracks", int cacheLimit = providerQueryLimit)
    {
        try
        {
            string key = $"{cacheVersion}|{providerId}|{cacheLimit}|{NormalizeText(query)}";
            return await MusicMetadataCacheService.GetOrCreateAsync(
                providerId,
                cacheType,
                key,
                searchCacheTtl,
                () => factory(cancellationToken),
                cancellationToken
            ) ?? new List<MusicTrack>();
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

    // «шумные» слова в названии кандидата допустимы только если встречались
    // в самих сидах: слушаешь remix/nightcore — радио вправе их предлагать
    static readonly string[] conditionalNoisyWords =
    {
        "cover", "karaoke", "sped up", "slowed", "nightcore", "remix",
        "8d audio", "bass boosted", "reverb", "edit", "mashup",
        "type beat", "freestyle"
    };

    static HashSet<string> CollectSeedNoisyWords(IEnumerable<MusicTrack> seeds)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seeds ?? Enumerable.Empty<MusicTrack>())
        {
            string padded = $" {NormalizeNoiseText(seed?.title)} ";

            foreach (var word in conditionalNoisyWords)
            {
                if (padded.Contains($" {word} ", StringComparison.OrdinalIgnoreCase))
                    allowed.Add(word);
            }
        }

        return allowed;
    }

    static bool AcceptCandidate(
        MusicTrack track,
        HashSet<string> seedNoisyWords,
        bool requireCyrillic,
        HashSet<string> excludedIds,
        HashSet<string> excludedKeys,
        HashSet<string> excludedTitles,
        HashSet<string> outputIds,
        HashSet<string> outputKeys,
        HashSet<string> outputTitles)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.id) || string.IsNullOrWhiteSpace(track.title))
            return false;

        string id = track.id.Trim();
        string key = BuildTrackDedupeKey(track);
        string titleKey = BuildTrackTitleKey(track);

        if (!LooksLikeStandaloneSong(track, titleKey, seedNoisyWords, requireCyrillic))
            return false;

        if (excludedIds.Contains(id) || outputIds.Contains(id))
            return false;

        if (!string.IsNullOrWhiteSpace(key) && (excludedKeys.Contains(key) || outputKeys.Contains(key)))
            return false;

        if (!string.IsNullOrWhiteSpace(titleKey) && (excludedTitles.Contains(titleKey) || outputTitles.Contains(titleKey)))
            return false;

        outputIds.Add(id);
        if (!string.IsNullOrWhiteSpace(key))
            outputKeys.Add(key);
        if (!string.IsNullOrWhiteSpace(titleKey))
            outputTitles.Add(titleKey);

        return true;
    }

    static MusicTrack NormalizeCandidateMetadata(MusicTrack track)
    {
        if (track == null || string.IsNullOrWhiteSpace(track.title))
            return track;

        if (!TrySplitArtistTitle(track.title, out var artist, out var title))
            return track;

        track.artist_name = artist;
        track.artists = new List<string> { artist };
        track.title = title;
        return track;
    }

    static bool TrySplitArtistTitle(string rawTitle, out string artist, out string title)
    {
        artist = null;
        title = null;

        var parts = Regex.Split(rawTitle ?? string.Empty, @"\s+[-–—]\s+")
            .Select(i => i.Trim())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .ToList();

        if (parts.Count < 2)
            return false;

        string artistPart = parts[^2];
        string titlePart = parts[^1];

        artistPart = Regex.Replace(artistPart, @"^\s*[\d#._-]+\s*", "").Trim();
        titlePart = Regex.Replace(titlePart, @"^\s*[\d#._-]+\s*", "").Trim();

        if (string.IsNullOrWhiteSpace(artistPart) || string.IsNullOrWhiteSpace(titlePart))
            return false;

        if (NormalizeText(artistPart).Length < 2 || NormalizeText(titlePart).Length < 2)
            return false;

        if (artistPart.Length > 80 || titlePart.Length > 120)
            return false;

        artist = artistPart;
        title = titlePart;
        return true;
    }

    static bool LooksLikeStandaloneSong(MusicTrack track, string titleKey, HashSet<string> seedNoisyWords, bool requireCyrillic)
    {
        int durationMs = track.duration_ms ?? 0;
        if (durationMs > 0 && (durationMs < 45_000 || durationMs > 12 * 60_000))
            return false;

        if (string.IsNullOrWhiteSpace(titleKey))
            return false;

        string padded = $" {titleKey} ";
        string noisePadded = $" {NormalizeNoiseText(track?.title)} ";

        if (requireCyrillic && !ContainsCyrillic(track?.title) && !ContainsCyrillic(track?.artist_name))
            return false;

        string[] noisy =
        {
            " nonstop ",
            " megamix ",
            " full album ",
            " playlist ",
            " reaction ",
            " podcast ",
            " interview ",
            " instrumental beat ",
            " dj set ",
            " live set ",
            " preview ",
            " prod "
        };

        if (noisy.Any(word => padded.Contains(word, StringComparison.OrdinalIgnoreCase) || noisePadded.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return false;

        foreach (var word in conditionalNoisyWords)
        {
            if (seedNoisyWords != null && seedNoisyWords.Contains(word))
                continue;

            if (noisePadded.Contains($" {word} ", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    static bool ContainsCyrillic(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Any(c => c is >= '\u0400' and <= '\u04FF');

    static string BuildTrackDedupeKey(MusicTrack track)
    {
        if (track == null)
            return string.Empty;

        string artist = NormalizeText(track.artist_name);
        string title = BuildTrackTitleKey(track);

        return string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : $"{artist}|{title}";
    }

    static string BuildTrackTitleKey(MusicTrack track)
    {
        string rawTitle = track?.title ?? string.Empty;
        rawTitle = Regex.Replace(rawTitle, @"^\s*.{1,80}\s+[-–—]\s+", "", RegexOptions.IgnoreCase);

        string title = NormalizeText(rawTitle);
        string artist = NormalizeText(track?.artist_name);

        if (!string.IsNullOrWhiteSpace(artist) && title.StartsWith(artist + " ", StringComparison.OrdinalIgnoreCase))
            title = title.Substring(artist.Length).Trim();

        foreach (var artistName in track?.artists ?? new List<string>())
        {
            string normalizedArtist = NormalizeText(artistName);
            if (!string.IsNullOrWhiteSpace(normalizedArtist) && title.StartsWith(normalizedArtist + " ", StringComparison.OrdinalIgnoreCase))
                title = title.Substring(normalizedArtist.Length).Trim();
        }

        return title;
    }

    static string NormalizeText(string value)
    {
        value = RemoveDiacritics((value ?? string.Empty).ToLowerInvariant());
        value = Regex.Replace(value, @"\([^)]*\)|\[[^\]]*\]", " ");
        value = Regex.Replace(value, @"\b(feat|ft|featuring|official|audio|video|lyrics|lyric|clip|mv|hd|4k)\b", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"[^a-z0-9а-яёіїєґ]+", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    static string NormalizeNoiseText(string value)
    {
        value = RemoveDiacritics((value ?? string.Empty).ToLowerInvariant());
        value = Regex.Replace(value, @"\b(feat|ft|featuring|official|audio|video|lyrics|lyric|clip|mv|hd|4k)\b", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"[^a-z0-9а-яёіїєґ]+", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
