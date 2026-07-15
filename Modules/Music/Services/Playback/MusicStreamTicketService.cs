using Microsoft.Extensions.Caching.Memory;

namespace Music;

public static class MusicStreamTicketService
{
    static readonly MemoryCache cache = new(new MemoryCacheOptions());

    // пауза на локскрине может длиться долго, поэтому тикет продлевается на каждом
    // обращении (sliding) вместо жёстких 30 минут; absolute cap ограничен временем
    // жизни upstream-ссылок (googlevideo и т.п.)
    static readonly TimeSpan slidingLifetime = TimeSpan.FromHours(2);
    static readonly TimeSpan absoluteLifetime = TimeSpan.FromHours(6);

    public static string Create(MusicPlaybackSource source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return null;

        string ticket = Guid.NewGuid().ToString("N");
        cache.Set(ticket, Clone(source), new MemoryCacheEntryOptions
        {
            SlidingExpiration = slidingLifetime,
            AbsoluteExpirationRelativeToNow = absoluteLifetime
        });
        return ticket;
    }

    public static bool TryGet(string ticket, out MusicPlaybackSource source)
    {
        source = null;

        if (string.IsNullOrWhiteSpace(ticket))
            return false;

        if (!cache.TryGetValue(ticket, out MusicPlaybackSource cached) || cached == null)
            return false;

        source = Clone(cached);
        return true;
    }

    static MusicPlaybackSource Clone(MusicPlaybackSource source)
    {
        return new MusicPlaybackSource
        {
            provider_id = source.provider_id,
            url = source.url,
            external_url = source.external_url,
            mime_type = source.mime_type,
            bitrate = source.bitrate,
            quality = source.quality,
            headers = source.headers?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<string, string>(),
            proxy_url = source.proxy_url,
            proxy_username = source.proxy_username,
            proxy_password = source.proxy_password
        };
    }
}
