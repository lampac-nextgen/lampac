using Shared.Services.Hybrid;

namespace Music;

public static class MusicMetadataCacheService
{
    const string CacheKeyPrefix = "music:metadata:v2";

    // пустой результат — чаще разовый сбой провайдера, чем правда:
    // полный TTL превращал его в залипшее «ничего не найдено»
    static readonly TimeSpan emptyTtl = TimeSpan.FromMinutes(20);

    public static async Task<T> GetOrCreateAsync<T>(string providerId, string entityType, string cacheKey, TimeSpan ttl, Func<Task<T>> factory, CancellationToken cancellationToken = default) where T : class
    {
        var cached = await GetAsync<T>(providerId, entityType, cacheKey, cancellationToken);
        if (cached != null)
            return cached;

        var created = await factory();
        if (created != null)
            await SaveAsync(providerId, entityType, cacheKey, created, IsEmptyPayload(created) ? emptyTtl : ttl, cancellationToken);

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
