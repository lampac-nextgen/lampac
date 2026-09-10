using Shared.Services.Hybrid;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;

namespace Music;

public static class MusicSourceMatchService
{
    const string CacheKeyPrefix = "music:sourcematch:v3";
    const string MissingCacheKeyPrefix = "music:sourcematch-miss:v2";
    static readonly TimeSpan MatchTtl = TimeSpan.FromDays(180);
    static readonly TimeSpan MissingTtl = TimeSpan.FromMinutes(30);
    const long MatchCacheSizeLimit = 10_000;
    static readonly MemoryCache matchCache = new(new MemoryCacheOptions
    {
        SizeLimit = MatchCacheSizeLimit
    });
    static readonly MemoryCache missingCache = new(new MemoryCacheOptions());

    // Fixed stripes bound memory use; one track/mode shares a gate across providers
    // so reset, manual selection and late automatic results are ordered together.
    static readonly SemaphoreSlim[] stateLocks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    static SemaphoreSlim StateLock(string trackId, string playbackMode)
    {
        string key = NormalizeTrackId(trackId) + ":" + MusicPlaybackModeService.Normalize(playbackMode);
        return stateLocks[(int)((uint)key.GetHashCode() % (uint)stateLocks.Length)];
    }

    static MusicAudioMatch CopyMatch(MusicAudioMatch match)
        => match == null ? null : MusicJson.Deserialize<MusicAudioMatch>(MusicJson.Serialize(match));

    static string ScopeProviderId(string providerId, string playbackMode)
    {
        providerId = providerId?.Trim();
        if (string.IsNullOrWhiteSpace(providerId))
            return providerId;

        return $"{providerId}:{MusicPlaybackModeService.Normalize(playbackMode)}";
    }

    public static async Task<MusicAudioMatch> GetAsync(string trackId, string providerId, string playbackMode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(providerId))
            return null;

        var stateLock = StateLock(trackId, playbackMode);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            string scopedProviderId = ScopeProviderId(providerId, playbackMode);
            string cacheKey = BuildCacheKey(trackId, scopedProviderId);
            if (matchCache.TryGetValue(cacheKey, out MusicAudioMatch cachedMatch))
                return CopyMatch(cachedMatch);

            var dbMatch = await ReadDbMatchAsync(trackId, scopedProviderId, cancellationToken);
            if (dbMatch != null)
            {
                SetMatchCache(cacheKey, dbMatch);
                return CopyMatch(dbMatch);
            }

            var entry = await HybridCache.Get().ReadCacheAsync<MusicAudioMatch>(cacheKey, false, null, textJson: true);
            if (entry.succes && entry.value != null)
            {
                // hybrid — территория авто-подбора: pinned авторитетен только из БД,
                // иначе legacy-запись воскресит сброшенный пользователем выбор
                entry.value = CopyMatch(entry.value);
                entry.value.pinned = false;
                SetMatchCache(cacheKey, entry.value);
            }

            return entry.succes ? CopyMatch(entry.value) : null;
        }
        finally
        {
            stateLock.Release();
        }
    }

    // сброс ручного выбора: убирает pinned-строки трека в данном режиме,
    // авто-подбор в volatile-кэше не трогаем — резолвер продолжит с него
    public static async Task<bool> DeleteAsync(string trackId, string playbackMode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            return false;

        var stateLock = StateLock(trackId, playbackMode);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!await DeleteDbMatchesAsync(trackId, playbackMode, cancellationToken))
                return false;

            foreach (var provider in MusicProviderRegistry.AudioProviders)
                matchCache.Remove(BuildCacheKey(trackId, ScopeProviderId(provider.Id, playbackMode)));

            return true;
        }
        finally
        {
            stateLock.Release();
        }
    }

    static async Task<bool> DeleteDbMatchesAsync(string trackId, string playbackMode, CancellationToken cancellationToken)
    {
        trackId = NormalizeTrackId(trackId);
        if (string.IsNullOrWhiteSpace(trackId))
            return false;

        string mode = MusicPlaybackModeService.Normalize(playbackMode);
        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));

        try
        {
            if (!await semaphore.WaitAsync())
            {
                Console.WriteLine("[Music] source match reset skipped: db semaphore timeout");
                return false;
            }

            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM audio_source_matches
                WHERE track_id = $track_id
                  AND provider_scope LIKE $scope_pattern;
                """;
            command.Parameters.AddWithValue("$track_id", trackId);
            command.Parameters.AddWithValue("$scope_pattern", "%:" + NormalizeProviderScope(mode));

            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] source match reset failed: {ex.Message}");
            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task<bool> SaveAsync(string trackId, MusicAudioMatch match, string playbackMode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId) || match == null || string.IsNullOrWhiteSpace(match.provider_id) || string.IsNullOrWhiteSpace(match.id))
            return false;

        var stateLock = StateLock(trackId, playbackMode);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            match = CopyMatch(match);
            string scopedProviderId = ScopeProviderId(match.provider_id, playbackMode);
            string cacheKey = BuildCacheKey(trackId, scopedProviderId);

            if (match.pinned)
            {
                // ручной выбор — durable state; memory обновляем только после
                // успешной записи, чтобы «Сохранено» клиенту не врало
                if (!await SaveDbMatchAsync(trackId, scopedProviderId, match, cancellationToken))
                    return false;
            }
            else
            {
                // авто-матч — volatile cache: SQLite не трогаем (правило модуля),
                // in-process свежесть обеспечивает matchCache ниже
                HybridCache.Get().Set(cacheKey, match, MatchTtl, textJson: true);
                // A resolver may have started before the user pinned a source.
                // Keep the automatic result volatile, but never replace that choice.
                var pinned = await ReadDbMatchAsync(trackId, scopedProviderId, cancellationToken);
                if (pinned != null)
                    match = pinned;
            }

            SetMatchCache(cacheKey, match);
            missingCache.Remove(BuildMissingCacheKey(trackId, scopedProviderId));
            HybridCache.Get().Set(BuildMissingCacheKey(trackId, scopedProviderId), false, MissingTtl, textJson: true);
            return true;
        }
        finally
        {
            stateLock.Release();
        }
    }

    public static async Task<bool> IsMarkedMissingAsync(string trackId, string providerId, string playbackMode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(providerId))
            return false;

        _ = cancellationToken;
        string scopedProviderId = ScopeProviderId(providerId, playbackMode);
        string missingKey = BuildMissingCacheKey(trackId, scopedProviderId);

        if (missingCache.TryGetValue(missingKey, out bool cachedMissing))
            return cachedMissing;

        var entry = await HybridCache.Get().ReadCacheAsync<bool>(BuildMissingCacheKey(trackId, scopedProviderId), false, null, textJson: true);
        if (entry.succes && entry.value)
            missingCache.Set(missingKey, true, MissingTtl);

        return entry.succes && entry.value;
    }

    public static async Task MarkMissingAsync(string trackId, string providerId, string playbackMode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(providerId))
            return;

        var stateLock = StateLock(trackId, playbackMode);
        await stateLock.WaitAsync(cancellationToken);
        try
        {
            string scopedProviderId = ScopeProviderId(providerId, playbackMode);
            string missingKey = BuildMissingCacheKey(trackId, scopedProviderId);

            // A lookup may have started before the user pinned a source and
            // finished afterwards. Never let that stale miss hide durable state.
            if (await ReadDbMatchAsync(trackId, scopedProviderId, cancellationToken) != null)
            {
                missingCache.Remove(missingKey);
                HybridCache.Get().Set(missingKey, false, MissingTtl, textJson: true);
                return;
            }

            missingCache.Set(missingKey, true, MissingTtl);
            HybridCache.Get().Set(missingKey, true, MissingTtl, textJson: true);
        }
        finally
        {
            stateLock.Release();
        }
    }

    static string BuildCacheKey(string trackId, string scopedProviderId)
    {
        return string.Join(':',
            CacheKeyPrefix,
            NormalizeTrackId(trackId),
            NormalizeProviderScope(scopedProviderId));
    }

    static string BuildMissingCacheKey(string trackId, string scopedProviderId)
    {
        return string.Join(':',
            MissingCacheKeyPrefix,
            NormalizeTrackId(trackId),
            NormalizeProviderScope(scopedProviderId));
    }

    static void SetMatchCache(string cacheKey, MusicAudioMatch match)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || match == null)
            return;

        matchCache.Set(cacheKey, match, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = MatchTtl,
            Size = 1
        });
    }

    static async Task<MusicAudioMatch> ReadDbMatchAsync(string trackId, string scopedProviderId, CancellationToken cancellationToken)
    {
        trackId = NormalizeTrackId(trackId);
        scopedProviderId = NormalizeProviderScope(scopedProviderId);
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(scopedProviderId))
            return null;

        await using var connection = new SqliteConnection(MusicContext.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM audio_source_matches
            WHERE track_id = $track_id
              AND provider_scope = $provider_scope
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$track_id", trackId);
        command.Parameters.AddWithValue("$provider_scope", scopedProviderId);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(payload) ? null : MusicJson.Deserialize<MusicAudioMatch>(payload);
    }

    static async Task<bool> SaveDbMatchAsync(string trackId, string scopedProviderId, MusicAudioMatch match, CancellationToken cancellationToken)
    {
        trackId = NormalizeTrackId(trackId);
        scopedProviderId = NormalizeProviderScope(scopedProviderId);
        if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(scopedProviderId) || match == null)
            return false;

        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));

        try
        {
            if (!await semaphore.WaitAsync())
            {
                Console.WriteLine("[Music] source match save skipped: db semaphore timeout");
                return false;
            }

            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO audio_source_matches (track_id, provider_scope, payload, updated)
                VALUES ($track_id, $provider_scope, $payload, $updated)
                ON CONFLICT(track_id, provider_scope) DO UPDATE SET
                    payload = excluded.payload,
                    updated = excluded.updated;
                """;
            command.Parameters.AddWithValue("$track_id", trackId);
            command.Parameters.AddWithValue("$provider_scope", scopedProviderId);
            command.Parameters.AddWithValue("$payload", MusicJson.Serialize(match));
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] source match save failed: {ex.Message}");
            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    static string NormalizeTrackId(string trackId) => trackId?.Trim() ?? string.Empty;
    static string NormalizeProviderScope(string providerId) => providerId?.Trim().ToLowerInvariant() ?? string.Empty;
}
