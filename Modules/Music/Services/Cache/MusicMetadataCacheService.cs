using Shared.Services.Hybrid;
using System.Collections.Concurrent;

namespace Music;

public static class MusicMetadataCacheService
{
    const string CacheKeyPrefix = "music:metadata:v2";

    // пустой результат — чаще разовый сбой провайдера, чем правда:
    // полный TTL превращал его в залипшее «ничего не найдено»
    static readonly TimeSpan emptyTtl = TimeSpan.FromMinutes(20);

    sealed class PendingFill<T> where T : class
    {
        public CancellationToken OwnerCancellation { get; init; }
        public Lazy<Task<T>> Work { get; init; }
    }

    static class PendingFills<T> where T : class
    {
        public static readonly ConcurrentDictionary<string, PendingFill<T>> Items = new();
    }

    public static async Task<T> GetOrCreateAsync<T>(
        string providerId,
        string entityType,
        string cacheKey,
        TimeSpan ttl,
        Func<Task<T>> factory,
        CancellationToken cancellationToken = default,
        Func<T, TimeSpan> ttlSelector = null) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cached = await GetAsync<T>(providerId, entityType, cacheKey, cancellationToken);
        if (cached != null)
            return cached;

        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(cacheKey))
            return await CreateAndSaveAsync(providerId, entityType, cacheKey, ttl, factory, cancellationToken, ttlSelector);

        string key = BuildCacheKey(providerId, entityType, cacheKey);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingFill<T> candidate = null;
            candidate = new PendingFill<T>
            {
                OwnerCancellation = cancellationToken,
                Work = new Lazy<Task<T>>(async () =>
                {
                    try
                    {
                        // A previous fill may have finished between the miss and registration.
                        var existing = await GetAsync<T>(providerId, entityType, cacheKey, cancellationToken);
                        if (existing != null)
                            return existing;

                        return await CreateAndSaveAsync(providerId, entityType, cacheKey, ttl, factory, cancellationToken, ttlSelector);
                    }
                    finally
                    {
                        PendingFills<T>.Items.TryRemove(new KeyValuePair<string, PendingFill<T>>(key, candidate));
                    }
                }, LazyThreadSafetyMode.ExecutionAndPublication)
            };

            var pending = PendingFills<T>.Items.GetOrAdd(key, candidate);
            try
            {
                return await pending.Work.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && pending.OwnerCancellation.IsCancellationRequested)
            {
                // Factories capture their caller's token. Retry with our own factory
                // after an abandoned owner's fill, without cancelling other waiters.
            }
        }
    }

    static async Task<T> CreateAndSaveAsync<T>(
        string providerId, string entityType, string cacheKey, TimeSpan ttl,
        Func<Task<T>> factory, CancellationToken cancellationToken,
        Func<T, TimeSpan> ttlSelector) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var created = await factory();
        cancellationToken.ThrowIfCancellationRequested();
        if (created != null)
        {
            TimeSpan selectedTtl = IsEmptyPayload(created)
                ? emptyTtl
                : ttlSelector?.Invoke(created) ?? ttl;

            await SaveAsync(providerId, entityType, cacheKey, created, selectedTtl, cancellationToken);
        }

        return created;
    }

    static bool IsEmptyPayload(object payload)
    {
        if (payload is System.Collections.ICollection collection)
            return collection.Count == 0;

        if (payload is MusicSearchResult search)
        {
            return (search.artists == null || search.artists.Count == 0)
                && (search.albums == null || search.albums.Count == 0)
                && (search.tracks == null || search.tracks.Count == 0);
        }

        return false;
    }

    public static async Task<T> GetAsync<T>(string providerId, string entityType, string cacheKey, CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(cacheKey))
            return null;

        var cache = HybridCache.Get();
        // fileCache:false — иначе чтение слепо к свежим записям в tempDb-буфере
        // HybridCache (до флаша ~15-60s), и повторный запрос делает полный рефетч
        var entry = await cache.ReadCacheAsync<T>(BuildCacheKey(providerId, entityType, cacheKey), false, null, textJson: true);
        return entry.succes ? entry.value : null;
    }

    public static Task SaveAsync<T>(string providerId, string entityType, string cacheKey, T payload, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(cacheKey) || payload == null)
            return Task.CompletedTask;

        _ = cancellationToken;

        HybridCache.Get().Set(BuildCacheKey(providerId, entityType, cacheKey), payload, ttl, textJson: true);
        return Task.CompletedTask;
    }

    static string BuildCacheKey(string providerId, string entityType, string cacheKey)
    {
        return string.Join(':',
            CacheKeyPrefix,
            NormalizeSegment(providerId),
            NormalizeSegment(entityType),
            NormalizeKey(cacheKey));
    }

    static string NormalizeKey(string cacheKey) => cacheKey.Trim().ToLowerInvariant();
    static string NormalizeSegment(string value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
