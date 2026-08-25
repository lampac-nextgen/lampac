using System.Collections.Concurrent;

namespace Music;

public static class MusicStreamTicketService
{
    sealed class TicketEntry
    {
        public MusicPlaybackSource source;
        // Interlocked по long: touch идёт из параллельных range-запросов без lock
        public long lastAccessTicks;
        public DateTime absoluteExpireUtc;
    }

    // пауза на локскрине может длиться долго, поэтому тикет продлевается на каждом
    // обращении (sliding) вместо жёстких 30 минут; absolute cap ограничен временем
    // жизни upstream-ссылок (googlevideo и т.п.)
    static readonly TimeSpan slidingLifetime = TimeSpan.FromHours(2);
    static readonly TimeSpan absoluteLifetime = TimeSpan.FromHours(6);

    // кап жёсткий. Create() обязан вернуть живой тикет, поэтому вытеснение самых
    // старых неактивных записей выполняется ДО вставки под тем же lock:
    // MemoryCache.SizeLimit так не умеет — мог бы не сохранить запись, хотя билет
    // уже ушёл клиенту (тогда /music/stream платил бы лишним re-resolve)
    const int maxTickets = 2048;
    static readonly object writeLock = new();
    static readonly ConcurrentDictionary<string, TicketEntry> tickets = new(StringComparer.Ordinal);

    public static string Create(MusicPlaybackSource source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return null;

        var now = DateTime.UtcNow;
        string ticket = Guid.NewGuid().ToString("N");

        lock (writeLock)
        {
            if (tickets.Count >= maxTickets)
                TrimNoLock(now);

            tickets[ticket] = new TicketEntry
            {
                source = Clone(source),
                lastAccessTicks = now.Ticks,
                absoluteExpireUtc = now.Add(absoluteLifetime)
            };
        }

        return ticket;
    }

    public static bool TryGet(string ticket, out MusicPlaybackSource source)
    {
        source = null;

        if (string.IsNullOrWhiteSpace(ticket))
            return false;

        if (!tickets.TryGetValue(ticket, out var entry) || entry == null)
            return false;

        var now = DateTime.UtcNow;
        if (now > entry.absoluteExpireUtc || now.Ticks - Interlocked.Read(ref entry.lastAccessTicks) > slidingLifetime.Ticks)
        {
            // remove-if-same: не задеваем запись, если под этим ключом что-то пересоздали
            tickets.TryRemove(new KeyValuePair<string, TicketEntry>(ticket, entry));
            return false;
        }

        // sliding touch
        Interlocked.Exchange(ref entry.lastAccessTicks, now.Ticks);

        source = Clone(entry.source);
        return true;
    }

    static void TrimNoLock(DateTime now)
    {
        long slidingCutoffTicks = now.Ticks - slidingLifetime.Ticks;

        foreach (var item in tickets)
        {
            if (now > item.Value.absoluteExpireUtc || Interlocked.Read(ref item.Value.lastAccessTicks) < slidingCutoffTicks)
                tickets.TryRemove(item.Key, out _);
        }

        while (tickets.Count >= maxTickets)
        {
            string oldestKey = null;
            long oldestTicks = long.MaxValue;

            foreach (var item in tickets)
            {
                long ticks = Interlocked.Read(ref item.Value.lastAccessTicks);
                if (ticks < oldestTicks)
                {
                    oldestTicks = ticks;
                    oldestKey = item.Key;
                }
            }

            if (oldestKey == null || !tickets.TryRemove(oldestKey, out _))
                break;
        }
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
            proxy_password = source.proxy_password,
            proxy_scope = source.proxy_scope
        };
    }
}
