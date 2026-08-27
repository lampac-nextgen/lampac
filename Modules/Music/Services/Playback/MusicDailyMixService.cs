using System.Globalization;

namespace Music;

/// <summary>
/// «Миксы недели»: каждому дню недели детерминированно назначаются два
/// артиста из истории прослушиваний профиля + до двух похожих (доминирующие
/// чужие артисты из SoundCloud related-пулов) — это основа микса. Поверх —
/// ЕДИНЫЙ слой открытий с общим бюджетом 5 треков (строго additive, в слотах
/// не анонсируется): приоритет у внешних артистов с Music-Map (по 2 трека),
/// остаток добирает жанр дня (популярное канонического SC-жанра от совсем
/// незнакомых артистов); открытия вплетаются в микс равномерно. Seed =
/// profile + ISO-неделя, поэтому внутри дня и недели состав стабилен, а на
/// следующей неделе раскладка меняется. Сводки по дням — чистый SQL без
/// внешних HTTP; треки грузятся лениво при открытии микса.
/// </summary>
public static class MusicDailyMixService
{
    static readonly TimeSpan mixTimeout = TimeSpan.FromSeconds(8);
    static readonly TimeSpan mixCacheTtl = TimeSpan.FromHours(12);
    const int historyDepth = 150;
    const int ownArtistsPerDay = 2;
    const int minHistoryArtists = 3;
    const int maxAssignableArtists = 28;
    // квота артистов из плейлистов пользователя (импорт Spotify/SC или свои):
    // «декларированный вкус» участвует в неделе даже при богатой истории,
    // а пользователю без истории плейлист сам открывает полку
    const int maxPlaylistArtists = 8;
    const int mixTrackLimit = 24;
    const int minMixTracks = 8;
    const int minSimilarPoolTracks = 3;
    // рецепт разнообразия (просьба пользователя 2026-07-16: «чтобы клиент
    // получал что новенькое»): 2 своих + до 2 похожих из SC related + до 2
    // внешних с Music-Map = до 6 артистов на микс; round-robin выравнивает
    // по ~4 трека на артиста, свои артисты идут первыми пулами
    const int maxSimilarArtists = 2;
    const int maxExternalArtists = 2;
    const int maxRefillExternalArtists = 6;
    const int externalPickWindow = 12;
    // ЕДИНЫЙ бюджет «открытий» (ревью Codex): Music-Map externals + жанровые
    // треки делят 5 слотов на микс, приоритет у Music-Map, жанр добирает
    // остаток; иначе микс рискует стать «слишком не твоим»
    const int discoveryTrackBudget = 5;
    const int desiredMixTrackCount = 20;
    const int maxMainTracksPerArtist = 4;
    const int maxDiscoveryTracksPerArtist = 2;
    const int externalPoolCandidateLimit = 10;
    const int genreTrackQuota = 4;
    const int genrePoolFetchLimit = 30;
    static readonly TimeSpan genreCacheTtl = TimeSpan.FromHours(6);
    static readonly TimeSpan artistGenresCacheTtl = TimeSpan.FromDays(14);
    // кэш собранных кандидатов недели (история + парс payload всех плейлистов —
    // самая дорогая часть /music/home). Сбор НЕ зависит от недели и salt (они
    // входят только в шаффл), поэтому ключ — профиль; сброс миксов и смена
    // недели получают новую раскладку мгновенно из кэшированного сбора.
    // Пустой/ошибочный сбор живёт 45s (ревью Codex: новый пользователь после
    // импорта плейлиста не должен ждать TTL, чтобы полка появилась)
    static readonly TimeSpan seedCacheTtl = TimeSpan.FromMinutes(12);
    static readonly TimeSpan seedCacheEmptyTtl = TimeSpan.FromSeconds(45);
    const int seedArtistsCacheCapacity = 256;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime expiresAt, List<HistoryArtist> artists)> seedArtistsCache = new(StringComparer.Ordinal);
    // кап жёсткий: чтение lock-free, trim + вставка под общим lock — иначе
    // параллельные сборы одновременно пройдут проверку Count и вместе её пробьют
    static readonly object seedArtistsCacheWriteLock = new();
    // v12: основной artist-cap снижен до 4 треков, чтобы якорь не доминировал
    // в 20-трековом миксе; v11: короткие миксы после artist-cap добираются дополнительными
    // Music-Map/genre артистами до ~20 треков без снятия лимитов на артиста;
    // v10: финальный artist-cap в миксе, чтобы один исполнитель/related-пул
    // не забивал весь день и у discovery/genre оставалось место;
    // v9: косметика title strip для дуэтов "Artist & Other - Title";
    // v8: artist-pool теперь гейтит кандидатов по запрошенному артисту и
    // канонизирует uploader/channel-имена; v7 — title-prefix нормализация сидов
    // применяется и к истории (YouTube/SC channel/uploader -> реальный Artist - Title)
    const string cacheVersion = "dailymix-v12";

    static readonly string[] dayTitles =
    {
        "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье"
    };

    sealed class HistoryArtist
    {
        public string Name;
        public string NormalizedName;
        public MusicTrack RecentTrack;
    }

    sealed class PlaylistArtistCandidate
    {
        public string Name;
        public string NormalizedName;
        public MusicTrack RecentTrack;
        public int Count;
        public int FirstIndex;
        public int NoisyCount;
    }

    // кэшируемый payload микса; null из фабрики (слабый результат) не кэшируется
    sealed class DailyMixPayload
    {
        public List<string> artists { get; set; } = new();
        public List<MusicTrack> tracks { get; set; } = new();
    }

    public static async Task<List<MusicDailyMixSummary>> GetSummariesAsync(string profileId, int salt = 0, CancellationToken cancellationToken = default)
    {
        var (assignment, historyArtists) = await BuildWeekAssignmentAsync(profileId, salt, cancellationToken);
        if (assignment == null)
            return new List<MusicDailyMixSummary>();

        // при бедной истории показываем столько дней, сколько закрывается
        // РАЗНЫМИ парами артистов, — иначе полка выглядит натянуто.
        // Полное покрытие (14+ артистов) — вся неделя с переносом через
        // Вс→Пн; частичное — остаток текущей недели от сегодня БЕЗ переноса:
        // шаг пар по модулю n на границе недели коллизится (7 простое),
        // а «полка живёт до воскресенья» честно матчится с «обновится в
        // понедельник»
        int coverage = Math.Min(7, historyArtists.Count / ownArtistsPerDay);
        int today = IsoDayOfWeek(DateTime.UtcNow);
        var result = new List<MusicDailyMixSummary>(coverage);

        for (int offset = 0; offset < coverage; offset++)
        {
            int day = coverage >= 7
                ? (today - 1 + offset) % 7 + 1
                : today + offset;

            if (day > 7)
                break;

            result.Add(new MusicDailyMixSummary
            {
                day = day,
                title = dayTitles[day - 1],
                today = day == today,
                artists = assignment[day - 1].Select(i => i.Name).ToList()
            });
        }

        return result;
    }

    public static async Task<MusicDailyMixResponse> GetMixAsync(string profileId, int day, int salt = 0, CancellationToken cancellationToken = default)
    {
        if (day is < 1 or > 7)
            return Unavailable(day, "Некорректный день недели.");

        var (assignment, historyArtists) = await BuildWeekAssignmentAsync(profileId, salt, cancellationToken);
        if (assignment == null)
            return Unavailable(day, "Слишком мало истории — послушай больше музыки, миксы соберутся сами.");

        var seeds = assignment[day - 1];
        string cacheKey = $"{cacheVersion}|{NormalizeProfileKey(profileId)}|{SeedKey(salt)}|{day}|{string.Join(",", seeds.Select(i => i.NormalizedName))}";

        DailyMixPayload payload = null;

        try
        {
            payload = await MusicMetadataCacheService.GetOrCreateAsync(
                "dailymix",
                "daily_mix",
                cacheKey,
                mixCacheTtl,
                () => BuildMixAsync(profileId, day, salt, seeds, historyArtists, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        if (payload == null || payload.tracks.Count < minMixTracks)
            return Unavailable(day, "Микс не собрался — попробуй чуть позже.");

        return new MusicDailyMixResponse
        {
            available = true,
            day = day,
            title = dayTitles[day - 1],
            artists = payload.artists,
            tracks = payload.tracks
        };
    }

    // раскладка артистов истории по 7 дням; null = истории мало, полку не показываем.
    // Детерминирована в пределах ISO-недели: seed-перемешивание + шаг по 2 артиста
    // на день, с wrap'ом при коротком списке. Сколько дней из раскладки реально
    // показывать (уникальные пары), решает GetSummariesAsync по artistCount
    static async Task<(List<HistoryArtist>[] assignment, List<HistoryArtist> artists)> BuildWeekAssignmentAsync(string profileId, int salt, CancellationToken cancellationToken)
    {
        var artists = await CollectSeedArtistsCachedAsync(profileId, cancellationToken);
        if (artists.Count < minHistoryArtists)
            return (null, artists);

        // вход шаффла сортируем по стабильному ключу: без этого любое
        // прослушивание меняло recency-порядок истории и пересобирало ВСЮ
        // раскладку недели (живой промах: миксы уехали после теста на
        // телефоне). Теперь раскладка сдвигается только при смене САМОГО
        // состава top-28 (дебют/выпадение артиста), а не порядка
        artists.Sort((a, b) => string.CompareOrdinal(a.NormalizedName, b.NormalizedName));

        ShuffleDeterministic(artists, Fnv1aHash($"{NormalizeProfileKey(profileId)}|{SeedKey(salt)}"));

        var assignment = new List<HistoryArtist>[7];

        for (int day = 1; day <= 7; day++)
        {
            var picks = new List<HistoryArtist>(ownArtistsPerDay);

            for (int slot = 0; slot < ownArtistsPerDay; slot++)
            {
                var candidate = artists[((day - 1) * ownArtistsPerDay + slot) % artists.Count];
                if (!picks.Contains(candidate))
                    picks.Add(candidate);
            }

            assignment[day - 1] = picks;
        }

        return (assignment, artists);
    }

    static async Task<List<HistoryArtist>> CollectSeedArtistsCachedAsync(string profileId, CancellationToken cancellationToken)
    {
        string key = NormalizeProfileKey(profileId);

        // возвращаем КОПИЮ списка: BuildWeekAssignmentAsync сортирует и
        // шаффлит его на месте — общий инстанс между запросами дал бы гонку
        if (seedArtistsCache.TryGetValue(key, out var cached) && cached.expiresAt > DateTime.UtcNow)
            return new List<HistoryArtist>(cached.artists);

        var artists = await CollectSeedArtistsAsync(profileId, cancellationToken);

        var ttl = artists.Count >= minHistoryArtists ? seedCacheTtl : seedCacheEmptyTtl;

        lock (seedArtistsCacheWriteLock)
        {
            if (!seedArtistsCache.ContainsKey(key) && seedArtistsCache.Count >= seedArtistsCacheCapacity)
            {
                // сначала протухшие; если их нет — вытесняем с ближайшим истечением,
                // иначе при равномерном трафике свежие профили переполнят кап
                foreach (var stale in seedArtistsCache.Where(i => i.Value.expiresAt <= DateTime.UtcNow).ToList())
                    seedArtistsCache.TryRemove(stale.Key, out _);

                while (seedArtistsCache.Count >= seedArtistsCacheCapacity)
                {
                    var oldest = seedArtistsCache.OrderBy(i => i.Value.expiresAt).FirstOrDefault();
                    if (oldest.Key == null || !seedArtistsCache.TryRemove(oldest.Key, out _))
                        break;
                }
            }

            seedArtistsCache[key] = (DateTime.UtcNow.Add(ttl), new List<HistoryArtist>(artists));
        }

        return artists;
    }

    // кандидаты недели: история прослушиваний (реальное поведение, до 28) +
    // артисты из плейлистов пользователя (декларированный вкус, квота 8,
    // гарантирована — история при переполнении ужимается с наименее свежего
    // края). Пользователь без истории, но с плейлистом ≥3 артистов получает
    // полку целиком из плейлиста
    static async Task<List<HistoryArtist>> CollectSeedArtistsAsync(string profileId, CancellationToken cancellationToken)
    {
        var artists = await CollectHistoryArtistsAsync(profileId, cancellationToken);
        var playlistArtists = new List<HistoryArtist>();

        try
        {
            var playlistTracks = await MusicUserPlaylistService.GetAllTracksAsync(profileId, cancellationToken);
            playlistArtists = RankPlaylistArtists(playlistTracks, artists)
                .Take(maxPlaylistArtists)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        if (playlistArtists.Count == 0)
            return artists;

        int historyKeep = Math.Min(artists.Count, maxAssignableArtists - playlistArtists.Count);
        return artists.Take(historyKeep).Concat(playlistArtists).ToList();
    }

    static List<HistoryArtist> RankPlaylistArtists(List<MusicTrack> playlistTracks, List<HistoryArtist> historyArtists)
    {
        var groups = new List<PlaylistArtistCandidate>();
        var byNormalized = new Dictionary<string, PlaylistArtistCandidate>(StringComparer.OrdinalIgnoreCase);
        var history = historyArtists ?? new List<HistoryArtist>();
        int index = 0;

        foreach (var rawTrack in playlistTracks ?? new List<MusicTrack>())
        {
            var track = NormalizeSeedTrack(rawTrack);
            string name = MusicRadioService.CleanupArtistName(track?.artist_name);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string normalized = MusicRadioService.NormalizeText(name);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            if (history.Any(existing => MusicMapSupport.IsAliasOf(name, existing.Name)))
                continue;

            if (!byNormalized.TryGetValue(normalized, out var group))
            {
                group = groups.FirstOrDefault(existing => MusicMapSupport.IsAliasOf(name, existing.Name));

                if (group == null)
                {
                    group = new PlaylistArtistCandidate
                    {
                        Name = name,
                        NormalizedName = normalized,
                        RecentTrack = track,
                        FirstIndex = index
                    };
                    groups.Add(group);
                }

                byNormalized[normalized] = group;
            }

            group.Count++;
            if (IsNoisyDiscoveryTrack(track))
                group.NoisyCount++;

            // Для related-пула лучше держать первый чистый трек артиста: он
            // идёт сидом в SoundCloud related. Если первый был slowed/reverb, а
            // позже встретился обычный трек — заменяем только сид, не порядок.
            if (IsNoisyDiscoveryTrack(group.RecentTrack) && !IsNoisyDiscoveryTrack(track))
                group.RecentTrack = track;

            index++;
        }

        return groups
            .OrderByDescending(group => PlaylistArtistScore(group))
            .ThenBy(group => group.FirstIndex)
            .Select(group => new HistoryArtist
            {
                Name = group.Name,
                NormalizedName = group.NormalizedName,
                RecentTrack = group.RecentTrack
            })
            .ToList();
    }

    static int PlaylistArtistScore(PlaylistArtistCandidate group)
    {
        // Повторы в плейлистах — главный сигнал намерения. Одноразовые SC-
        // uploader'ы остаются fallback'ом для холодного старта, но не перебивают
        // артистов, реально часто встречающихся в сохранённой музыке.
        return group.Count * 100 - group.NoisyCount * 30;
    }

    // канонизация сида рекомендаций (история И плейлисты): из uploader-имён
    // вида «Artist - Title» вытаскивается реальный артист
    static MusicTrack NormalizeSeedTrack(MusicTrack track)
    {
        if (track == null)
            return null;

        var result = CloneTrack(track);

        if ((IsProviderUploadTrack(result) || string.IsNullOrWhiteSpace(result.artist_name))
            && TrySplitLeadingArtistTitle(result.title, out var artist, out var title))
        {
            result.artist_name = artist;
            result.artists = new List<string> { artist };
            result.title = title;
        }

        return result;
    }

    static bool TrySplitLeadingArtistTitle(string rawTitle, out string artist, out string title)
    {
        artist = null;
        title = null;

        var match = System.Text.RegularExpressions.Regex.Match(rawTitle ?? string.Empty, @"^\s*(?<artist>.{2,80}?)\s+[-–—]{1,2}\s+(?<title>.{2,180})\s*$");
        if (!match.Success)
            return false;

        artist = MusicRadioService.CleanupArtistName(match.Groups["artist"].Value.Trim());
        title = match.Groups["title"].Value.Trim();

        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return false;

        return MusicRadioService.NormalizeText(artist).Length >= 2
            && MusicRadioService.NormalizeText(title).Length >= 2;
    }

    static bool IsProviderUploadTrack(MusicTrack track)
    {
        if (track == null)
            return false;

        if ((track.id ?? string.Empty).StartsWith("soundcloud:", StringComparison.OrdinalIgnoreCase))
            return true;

        if ((track.id ?? string.Empty).StartsWith("youtube:", StringComparison.OrdinalIgnoreCase))
            return true;

        return (track.provider_refs ?? new List<MusicProviderRef>())
            .Any(i => (i.provider ?? string.Empty).Contains("soundcloud", StringComparison.OrdinalIgnoreCase));
    }

    static bool IsNoisyDiscoveryTrack(MusicTrack track)
    {
        string value = MusicRadioService.NormalizeText(track?.title);
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string padded = $" {value} ";
        string[] noisy =
        {
            " slowed ", " reverb ", " sped up ", " nightcore ", " remix ",
            " cover ", " karaoke ", " type beat ", " freestyle "
        };

        return noisy.Any(word => padded.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    static MusicTrack CloneTrack(MusicTrack track)
    {
        return new MusicTrack
        {
            id = track.id,
            title = track.title,
            artist_id = track.artist_id,
            artist_name = track.artist_name,
            artists = track.artists?.ToList() ?? new List<string>(),
            album_id = track.album_id,
            album_title = track.album_title,
            isrc = track.isrc,
            duration_ms = track.duration_ms,
            track_number = track.track_number,
            disc_number = track.disc_number,
            date = track.date,
            search_score = track.search_score,
            images = track.images?.ToList() ?? new List<MusicImage>(),
            provider_refs = track.provider_refs?.ToList() ?? new List<MusicProviderRef>(),
            auto_radio = track.auto_radio
        };
    }

    static async Task<List<HistoryArtist>> CollectHistoryArtistsAsync(string profileId, CancellationToken cancellationToken)
    {
        var result = new List<HistoryArtist>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<MusicRecentlyPlayedItem> history;
        try
        {
            history = await MusicPlaybackHistoryService.GetRecentAsync(profileId, historyDepth, cancellationToken);
        }
        catch
        {
            return result;
        }

        // история отсортирована по свежести — первый встреченный трек артиста
        // и есть его самый свежий (идёт сид-треком в related-пул)
        foreach (var item in history)
        {
            var track = NormalizeSeedTrack(item?.track);
            if (track == null)
                continue;

            string name = MusicRadioService.CleanupArtistName(track.artist_name);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string normalized = MusicRadioService.NormalizeText(name);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                continue;

            // кластер-дедуп: варианты одного артиста («Cкриптoнит»/«Скриптонит»,
            // «Basta, GUF»/«Basta & Guf», «Miyagi & Эндшпиль»/«MiyaGi & Эндшпиль»)
            // не должны занимать несколько слотов недели — живой промах: день
            // из двух алиасов одного кластера вырождается в моно-артист микс
            if (result.Any(existing => MusicMapSupport.IsAliasOf(name, existing.Name)))
                continue;

            result.Add(new HistoryArtist
            {
                Name = name,
                NormalizedName = normalized,
                RecentTrack = track
            });

            if (result.Count >= maxAssignableArtists)
                break;
        }

        return result;
    }

    static async Task<DailyMixPayload> BuildMixAsync(string profileId, int day, int salt, List<HistoryArtist> seeds, List<HistoryArtist> historyArtists, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(mixTimeout);

        var artistPoolTasks = seeds
            .Select(seed => MusicRadioService.LoadArtistPoolAsync(seed.Name, timeoutCts.Token))
            .ToList();

        var relatedTasks = SoundCloudSupport.IsDiscoveryEnabled
            ? seeds.Select(seed => MusicRadioService.LoadRelatedPoolAsync(seed.RecentTrack, timeoutCts.Token)).ToList()
            : new List<Task<List<MusicTrack>>>();

        try
        {
            await Task.WhenAll(artistPoolTasks.Concat(relatedTasks));
        }
        catch
        {
        }

        static List<MusicTrack> PoolOf(Task<List<MusicTrack>> task) => task.IsCompletedSuccessfully
            ? task.Result ?? new List<MusicTrack>()
            : new List<MusicTrack>();

        var pools = artistPoolTasks.Select(PoolOf).Where(pool => pool.Count > 0).ToList();
        var mixArtists = seeds.Select(i => i.Name).ToList();

        // похожие — доминирующие чужие артисты из related-пулов сид-треков:
        // их треки уже на руках, дополнительных HTTP не нужно
        var relatedTracks = relatedTasks.SelectMany(PoolOf).ToList();
        var similars = PickSimilarArtistPools(relatedTracks, seeds, maxSimilarArtists);

        foreach (var similar in similars)
        {
            pools.Add(similar.pool);
            mixArtists.Add(similar.name);
        }

        if (pools.Count == 0)
            return null;

        // Music-Map — только additive-слой. Запускаем его после проверки, что
        // базовый микс уже есть: так при пустых artist/related-пулах не остаются
        // фоновые HTTP-задачи, а общий 8-секундный бюджет всё равно сохраняется.
        var musicMapTasks = seeds
            .Select(seed => MusicMapSupport.GetSimilarArtistsAsync(seed.Name, timeoutCts.Token))
            .ToList();

        // жанровые пулы — тоже additive-слой, стартуют параллельно с картами
        // (жанры артистов кэшируются 14d, пул жанра — 6h)
        var genrePoolsTask = SoundCloudSupport.IsDiscoveryEnabled
            ? LoadGenrePoolsAsync(seeds, timeoutCts.Token)
            : Task.FromResult(new List<List<MusicTrack>>());

        // сэмплирование внутри дня тоже детерминировано: пул может обновиться
        // (6h TTL radio-кэша), а микс дня не должен скакать между заходами
        uint sampleSeed = Fnv1aHash($"{NormalizeProfileKey(profileId)}|{SeedKey(salt)}|{day}|tracks");

        for (int i = 0; i < pools.Count; i++)
        {
            var pool = pools[i].ToList();
            ShuffleDeterministic(pool, sampleSeed + (uint)i);
            pools[i] = pool;
        }

        var seedTracks = seeds.Select(i => i.RecentTrack).ToList();
        var seedNoisyWords = MusicRadioService.CollectSeedNoisyWords(seedTracks);
        bool requireCyrillic = seedTracks.Count > 0
            && seedTracks.All(seed => MusicRadioService.ContainsCyrillic(seed?.title) || MusicRadioService.ContainsCyrillic(seed?.artist_name));

        // дедуп-сеты общие для слоя открытий и основного заполнения
        var excludedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ЕДИНЫЙ СЛОЙ ОТКРЫТИЙ (общий бюджет discoveryTrackBudget, ревью
        // Codex): сперва Music-Map externals (похожесть на твоих), остаток
        // добирает жанр дня; Music-Map пуст — жанр берёт весь бюджет.
        // Строго additive, в слотах карточки НЕ анонсируется — сюрприз.
        // Для «специй» шумовые слова не прощаются, даже если были в сидах:
        // discovery должен удивлять артистом, а не slowed/remix-версией
        var discoveryOutput = new List<MusicTrack>();
        var strictNoisyWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveryArtistCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var externalArtists = new List<string>();
        List<List<MusicTrack>> genrePools = null;

        try
        {
            externalArtists = await PickExternalArtistsAsync(musicMapTasks, seeds, historyArtists, similars.Select(i => i.name), sampleSeed);

            var externalPools = await LoadExternalArtistPoolsAsync(externalArtists, timeoutCts.Token);

            if (externalPools.Count > 0)
                MusicRadioService.FillRoundRobin(
                    externalPools, discoveryOutput, discoveryTrackBudget,
                    strictNoisyWords, requireCyrillic,
                    excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles,
                    discoveryArtistCounts, maxDiscoveryTracksPerArtist);

            int remaining = Math.Min(discoveryTrackBudget - discoveryOutput.Count, genreTrackQuota);

            if (remaining > 0)
            {
                var knownNames = historyArtists.Select(i => i.Name)
                    .Concat(mixArtists)
                    .Concat(externalArtists)
                    .ToList();

                genrePools = await PrepareGenrePoolsAsync(genrePoolsTask, knownNames, sampleSeed, timeoutCts.Token);

                if (genrePools.Count > 0)
                    FillDiscoveryFromGenrePools(
                        genrePools, discoveryOutput, discoveryOutput.Count + remaining,
                        strictNoisyWords, requireCyrillic,
                        excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles,
                        discoveryArtistCounts);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        var output = new List<MusicTrack>();
        var mainArtistCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        MusicRadioService.FillRoundRobin(
            pools,
            output,
            mixTrackLimit - discoveryOutput.Count,
            seedNoisyWords,
            requireCyrillic,
            excludedIds,
            excludedKeys,
            excludedTitles,
            outputIds,
            outputKeys,
            outputTitles,
            mainArtistCounts,
            maxMainTracksPerArtist);

        int desiredDiscoveryLimit = Math.Min(
            mixTrackLimit - output.Count,
            desiredMixTrackCount - output.Count);

        if (desiredDiscoveryLimit > discoveryOutput.Count)
        {
            await RefillShortMixAsync(
                musicMapTasks,
                genrePoolsTask,
                seeds,
                historyArtists,
                mixArtists,
                similars.Select(i => i.name),
                externalArtists,
                sampleSeed,
                strictNoisyWords,
                requireCyrillic,
                discoveryOutput,
                desiredDiscoveryLimit,
                discoveryArtistCounts,
                excludedIds,
                excludedKeys,
                excludedTitles,
                outputIds,
                outputKeys,
                outputTitles,
                genrePools,
                timeoutCts.Token);
        }

        // открытия вплетаются равномерно, а не хвостом:
        // при 19+5 — позиции 4, 9, 14, 19, 24
        if (discoveryOutput.Count > 0)
        {
            int step = (output.Count + discoveryOutput.Count) / (discoveryOutput.Count + 1);

            for (int i = 0; i < discoveryOutput.Count; i++)
                output.Insert(Math.Min(output.Count, (i + 1) * step + i), discoveryOutput[i]);
        }

        // auto_radio — маркер автопродолжения очереди; для микса дня это
        // обычные треки плейлиста, флаг с общего FillRoundRobin снимаем
        foreach (var track in output)
            track.auto_radio = false;

        return output.Count >= minMixTracks
            ? new DailyMixPayload { artists = mixArtists, tracks = output }
            : null;
    }

    // Artist-cap защищает от доминирования одного исполнителя, но у бедного дня
    // после этого может остаться 10-15 треков. Добор включается только тогда:
    // берём ещё внешних Music-Map артистов и, если надо, жанровые пулы. Лимиты на
    // артиста остаются теми же, поэтому микс растёт шириной, а не повтором.
    static async Task RefillShortMixAsync(
        List<Task<List<string>>> musicMapTasks,
        Task<List<List<MusicTrack>>> genrePoolsTask,
        List<HistoryArtist> seeds,
        List<HistoryArtist> historyArtists,
        List<string> mixArtists,
        IEnumerable<string> similarArtists,
        List<string> selectedExternalArtists,
        uint sampleSeed,
        HashSet<string> strictNoisyWords,
        bool requireCyrillic,
        List<MusicTrack> discoveryOutput,
        int desiredDiscoveryLimit,
        Dictionary<string, int> discoveryArtistCounts,
        HashSet<string> excludedIds,
        HashSet<string> excludedKeys,
        HashSet<string> excludedTitles,
        HashSet<string> outputIds,
        HashSet<string> outputKeys,
        HashSet<string> outputTitles,
        List<List<MusicTrack>> preparedGenrePools,
        CancellationToken cancellationToken)
    {
        if (desiredDiscoveryLimit <= discoveryOutput.Count)
            return;

        try
        {
            var extraExternalArtists = await PickExternalArtistsAsync(
                musicMapTasks,
                seeds,
                historyArtists,
                (similarArtists ?? Enumerable.Empty<string>()).Concat(selectedExternalArtists),
                sampleSeed + 0x51F15EED,
                maxRefillExternalArtists);

            if (extraExternalArtists.Count > 0)
            {
                selectedExternalArtists.AddRange(extraExternalArtists);

                var extraPools = await LoadExternalArtistPoolsAsync(extraExternalArtists, cancellationToken);

                if (extraPools.Count > 0)
                    MusicRadioService.FillRoundRobin(
                        extraPools, discoveryOutput, desiredDiscoveryLimit,
                        strictNoisyWords, requireCyrillic,
                        excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles,
                        discoveryArtistCounts, maxDiscoveryTracksPerArtist);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        if (desiredDiscoveryLimit <= discoveryOutput.Count)
            return;

        try
        {
            var genrePools = preparedGenrePools;
            if (genrePools == null)
            {
                var knownNames = historyArtists.Select(i => i.Name)
                    .Concat(mixArtists)
                    .Concat(selectedExternalArtists)
                    .ToList();

                genrePools = await PrepareGenrePoolsAsync(genrePoolsTask, knownNames, sampleSeed, cancellationToken);
            }

            if (genrePools.Count == 0)
                return;

            FillDiscoveryFromGenrePools(
                genrePools, discoveryOutput, desiredDiscoveryLimit,
                strictNoisyWords, requireCyrillic,
                excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles,
                discoveryArtistCounts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }
    }

    // общий для main-заполнения и refill: пулы внешних Music-Map артистов.
    // Топ выдачи поиска = популярные треки артиста — правильное знакомство
    // с незнакомым именем. Окно шире лимита на артиста: если первые результаты
    // шумные/дубли, FillRoundRobin дойдёт до нормального трека, а artist-cap
    // не даст внешнему артисту доминировать
    static async Task<List<List<MusicTrack>>> LoadExternalArtistPoolsAsync(List<string> artists, CancellationToken cancellationToken)
    {
        var poolTasks = artists
            .Select(artist => MusicRadioService.LoadArtistPoolAsync(artist, cancellationToken))
            .ToList();

        try
        {
            await Task.WhenAll(poolTasks);
        }
        catch
        {
        }

        return poolTasks
            .Where(task => task.IsCompletedSuccessfully)
            .Select(task => task.Result ?? new List<MusicTrack>())
            .Where(pool => pool.Count > 0)
            .Select(pool => pool.Take(externalPoolCandidateLimit).ToList())
            .ToList();
    }

    // общий для main-заполнения и refill: жанровый слой в два прохода —
    // сперва в родном скрипт-режиме микса; если кириллический фильтр съел всё
    // (глобальный жанровый топ чаще латиница) — второй проход без него:
    // жанровый слой и задуман как сознательный выход за рамки привычного
    static void FillDiscoveryFromGenrePools(
        List<List<MusicTrack>> genrePools,
        List<MusicTrack> discoveryOutput,
        int limit,
        HashSet<string> strictNoisyWords,
        bool requireCyrillic,
        HashSet<string> excludedIds,
        HashSet<string> excludedKeys,
        HashSet<string> excludedTitles,
        HashSet<string> outputIds,
        HashSet<string> outputKeys,
        HashSet<string> outputTitles,
        Dictionary<string, int> discoveryArtistCounts)
    {
        int beforeGenre = discoveryOutput.Count;

        MusicRadioService.FillRoundRobin(
            genrePools, discoveryOutput, limit,
            strictNoisyWords, requireCyrillic,
            excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles,
            discoveryArtistCounts, maxDiscoveryTracksPerArtist);

        if (discoveryOutput.Count == beforeGenre && requireCyrillic)
            MusicRadioService.FillRoundRobin(
                genrePools, discoveryOutput, limit,
                strictNoisyWords, false,
                excludedIds, excludedKeys, excludedTitles, outputIds, outputKeys, outputTitles,
                discoveryArtistCounts, maxDiscoveryTracksPerArtist);
    }

    static async Task<List<List<MusicTrack>>> PrepareGenrePoolsAsync(
        Task<List<List<MusicTrack>>> genrePoolsTask,
        IEnumerable<string> knownNames,
        uint sampleSeed,
        CancellationToken cancellationToken)
    {
        List<List<MusicTrack>> source;

        try
        {
            source = await genrePoolsTask ?? new List<List<MusicTrack>>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new List<List<MusicTrack>>();
        }

        var known = (knownNames ?? Enumerable.Empty<string>())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .ToList();

        var genrePools = source
            .Select(genreTracks => genreTracks
                .Select(MusicRadioService.NormalizeCandidateMetadata)
                .Where(track => !string.IsNullOrWhiteSpace(track?.artist_name)
                    && !known.Any(name => MusicMapSupport.IsAliasOf(track.artist_name, name)))
                .ToList())
            .Where(pool => pool.Count > 0)
            .ToList();

        for (int i = 0; i < genrePools.Count; i++)
            ShuffleDeterministic(genrePools[i], sampleSeed + 777 + (uint)i);

        return genrePools;
    }

    // жанр дня: доминирующие genre-теги из SC-поиска по обоим сид-артистам
    // (кэш 14d на артиста) -> канонический жанр-фасет SC -> популярные треки
    // жанра (кэш 6h). Жанр берётся только ПРИ УВЕРЕННОСТИ (ревью Codex):
    // явный победитель — один пул; ничья двух сильных жанров (артисты дня
    // из разных миров) — слой делится между обоими; слабый сигнал — слоя нет
    static async Task<List<List<MusicTrack>>> LoadGenrePoolsAsync(List<HistoryArtist> seeds, CancellationToken cancellationToken)
    {
        var genreTasks = seeds.Select(seed => MusicMetadataCacheService.GetOrCreateAsync(
            SoundCloudSupport.DiscoveryProviderId,
            "artist_genres",
            $"genres-v1|{MusicRadioService.NormalizeText(seed.Name)}",
            artistGenresCacheTtl,
            () => SoundCloudSupport.SearchDominantGenresAsync(seed.Name, cancellationToken),
            cancellationToken)).ToList();

        try
        {
            await Task.WhenAll(genreTasks);
        }
        catch
        {
        }

        // голоса по убыванию ранга: топ-жанр артиста весит больше
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in genreTasks.Where(t => t.IsCompletedSuccessfully))
        {
            var genres = task.Result ?? new List<string>();

            for (int rank = 0; rank < genres.Count; rank++)
            {
                string canonical = SoundCloudSupport.MapToCanonicalGenre(genres[rank]);
                if (string.IsNullOrWhiteSpace(canonical))
                    continue;

                votes[canonical] = votes.TryGetValue(canonical, out int current)
                    ? current + (3 - rank)
                    : 3 - rank;
            }
        }

        var ranked = votes.OrderByDescending(i => i.Value).ToList();

        // слабый сигнал: жанр не был топ-тегом ни одного артиста (score < 3)
        if (ranked.Count == 0 || ranked[0].Value < 3)
            return new List<List<MusicTrack>>();

        var chosenGenres = new List<string> { ranked[0].Key };

        // ничья двух сильных жанров — артисты дня из разных миров:
        // делим жанровый слой между обоими вместо случайного победителя
        if (ranked.Count > 1 && ranked[1].Value >= 3 && ranked[1].Value * 2 > ranked[0].Value)
            chosenGenres.Add(ranked[1].Key);

        var poolTasks = chosenGenres.Select(genre => MusicMetadataCacheService.GetOrCreateAsync(
            SoundCloudSupport.DiscoveryProviderId,
            "genre_tracks",
            $"genrepool-v1|{genre}",
            genreCacheTtl,
            () => SoundCloudSupport.SearchTracksByGenreAsync(genre, genrePoolFetchLimit, cancellationToken),
            cancellationToken)).ToList();

        try
        {
            await Task.WhenAll(poolTasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        return poolTasks
            .Where(task => task.IsCompletedSuccessfully)
            .Select(task => task.Result ?? new List<MusicTrack>())
            .Where(pool => pool.Count > 0)
            .ToList();
    }

    // внешние похожие артисты с Music-Map: пересечение карт обоих сид-артистов
    // (похож на обоих = сильный сигнал вайба дня), при пустом пересечении —
    // union в порядке близости; алиасы сидов, вся top-28 история и уже
    // выбранные SC-similar исключены; seeded-выбор из ближайшего окна —
    // стабилен в пределах недели, разный между неделями
    static async Task<List<string>> PickExternalArtistsAsync(
        List<Task<List<string>>> musicMapTasks,
        List<HistoryArtist> seeds,
        List<HistoryArtist> historyArtists,
        IEnumerable<string> similarArtists,
        uint seed,
        int maxArtists = maxExternalArtists)
    {
        try
        {
            await Task.WhenAll(musicMapTasks);
        }
        catch
        {
        }

        var maps = musicMapTasks
            .Where(task => task.IsCompletedSuccessfully)
            .Select(task => task.Result ?? new List<string>())
            .Where(map => map.Count > 0)
            .ToList();

        if (maps.Count == 0)
            return new List<string>();

        var aliasReferences = seeds.Select(i => i.Name).ToList();
        var excludeNames = historyArtists.Select(i => i.Name)
            .Concat(similarArtists ?? Enumerable.Empty<string>())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .ToList();

        var filtered = maps
            .Select(map => MusicMapSupport.FilterCandidates(map, aliasReferences, excludeNames))
            .Where(map => map.Count > 0)
            .ToList();

        if (filtered.Count == 0)
            return new List<string>();

        List<string> candidates;

        if (filtered.Count > 1)
        {
            var otherKeys = filtered.Skip(1)
                .Select(map => map.Select(MusicMapSupport.NormalizeName).ToHashSet(StringComparer.Ordinal))
                .ToList();

            candidates = filtered[0]
                .Where(name => otherKeys.All(keys => keys.Contains(MusicMapSupport.NormalizeName(name))))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var name in filtered.SelectMany(map => map))
                {
                    if (seen.Add(MusicMapSupport.NormalizeName(name)))
                        candidates.Add(name);
                }
            }
        }
        else
        {
            candidates = filtered[0];
        }

        if (candidates.Count == 0)
            return new List<string>();

        // несколько seeded-выборов из ближайшего окна без повторов/алиасов
        var window = candidates.Take(externalPickWindow).ToList();
        var picks = new List<string>();
        uint state = seed ^ 0x9E3779B9;

        while (picks.Count < maxArtists && window.Count > 0)
        {
            state = NextXorshift(state);
            string pick = window[(int)(state % (uint)window.Count)];
            window.Remove(pick);

            if (!picks.Any(chosen => MusicMapSupport.IsAliasOf(pick, chosen)))
                picks.Add(pick);
        }

        return picks;
    }

    static uint NextXorshift(uint seed)
    {
        uint state = seed == 0 ? 2463534242 : seed;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    static List<(string name, List<MusicTrack> pool)> PickSimilarArtistPools(List<MusicTrack> relatedTracks, List<HistoryArtist> seeds, int maxArtists)
    {
        var seedNames = new HashSet<string>(seeds.Select(i => i.NormalizedName), StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, (string name, List<MusicTrack> pool)>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in relatedTracks)
        {
            var track = MusicRadioService.NormalizeCandidateMetadata(raw);
            string name = MusicRadioService.CleanupArtistName(track?.artist_name);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string normalized = MusicRadioService.NormalizeText(name);
            if (string.IsNullOrWhiteSpace(normalized) || seedNames.Contains(normalized))
                continue;

            // кросс-скрипт/опечаточные дубли сида NormalizeText не ловит
            // (живой кейс: «Cкриптoнит» с латинскими C/o в истории vs
            // «Скриптонит» из related) — добиваем транслит+fuzzy Music-Map
            if (seeds.Any(seed => MusicMapSupport.IsAliasOf(name, seed.Name)))
                continue;

            if (!groups.TryGetValue(normalized, out var group))
                groups[normalized] = group = (name, new List<MusicTrack>());

            group.pool.Add(track);
        }

        var result = new List<(string name, List<MusicTrack> pool)>();

        foreach (var group in groups.Values.OrderByDescending(i => i.pool.Count))
        {
            // группы отсортированы по убыванию — дальше только мельче
            if (group.pool.Count < minSimilarPoolTracks)
                break;

            if (result.Any(chosen => MusicMapSupport.IsAliasOf(group.name, chosen.name)))
                continue;

            result.Add(group);

            if (result.Count >= maxArtists)
                break;
        }

        return result;
    }

    static MusicDailyMixResponse Unavailable(int day, string message) => new()
    {
        available = false,
        day = day,
        title = day is >= 1 and <= 7 ? dayTitles[day - 1] : null,
        message = message
    };

    static string NormalizeProfileKey(string profileId)
        => (profileId ?? string.Empty).Trim().ToLowerInvariant();

    // Неделя СОЗНАТЕЛЬНО считается по UTC, а не по локальному времени
    // (для UTC+3 полка обновляется ~03:00 ночи понедельника — приемлемо):
    // граница недели не зависит от таймзоны контейнера и переводов часов,
    // а «локальный понедельник» потребовал бы таймзону профиля — новое
    // состояние ради сдвига на пару часов. Это решение, не недосмотр
    static string WeekKey()
    {
        var now = DateTime.UtcNow;
        return $"{ISOWeek.GetYear(now)}-w{ISOWeek.GetWeekOfYear(now):D2}";
    }

    // salt — «сброс миксов недели» по кнопке: клиент хранит счётчик в своём
    // Storage (скоуп совпадает с per-browser профилем) и шлёт параметром;
    // salt=0 сохраняет исходный формат seed'а — дефолтная раскладка не
    // меняется от самого факта появления параметра
    static string SeedKey(int salt)
        => salt == 0 ? WeekKey() : $"{WeekKey()}|{salt}";

    static int IsoDayOfWeek(DateTime date)
        => ((int)date.DayOfWeek + 6) % 7 + 1;

    // string.GetHashCode рандомизирован per-process — для стабильного seed
    // между рестартами контейнера нужен собственный хэш
    static uint Fnv1aHash(string value)
    {
        uint hash = 2166136261;

        foreach (char c in value ?? string.Empty)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }

    static void ShuffleDeterministic<T>(List<T> list, uint seed)
    {
        uint state = seed == 0 ? 2463534242 : seed;

        uint next()
        {
            // xorshift32 — дешёвый детерминированный PRNG, качества хватает
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(next() % (uint)(i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
