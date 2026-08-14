using Microsoft.Data.Sqlite;

namespace Music;

/// <summary>
/// Дневная статистика прослушиваний (track_stats_daily). Счётчики отдельно
/// от витрины: payload трека живёт в playback_history и джойнится при чтении.
/// Инкремент вызывается ТОЛЬКО из честного play-пути (count_play=true в mark) —
/// обновления payload (смена источника и т.п.) статистику не трогают.
/// </summary>
public static class MusicStatsService
{
    // unlock «Твоего топа»: 10 разных треков обязательны (иначе «альбом из
    // трёх песен»), плюс либо фаворит на повторе, либо просто наслушан объём —
    // fallback для тех, кто слушает широко и не гоняет один трек по кругу
    const int UnlockTrackPlays = 10;
    const int UnlockDistinctTracks = 10;
    const int UnlockTotalPlays = 100;

    public static Task IncrementPlayAsync(string profileId, MusicTrack track, CancellationToken cancellationToken = default)
        => RecordPlayAsync(profileId, track, true, null, cancellationToken);

    public static async Task<int> ClearAsync(string profileId, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);

        if (string.IsNullOrWhiteSpace(profileId))
            return 0;

        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));
        var acquired = false;

        try
        {
            if (!await semaphore.WaitAsync())
                return 0;

            acquired = true;

            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM track_stats_daily
                WHERE profile_id = $profile_id;
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (acquired)
                semaphore.Release();
        }
    }

    public static async Task RecordPlayAsync(string profileId, MusicTrack track, bool countPlay, long? playedMs = null, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);

        if (track == null || string.IsNullOrWhiteSpace(track.id) || string.IsNullOrWhiteSpace(profileId))
            return;

        long durationMs = Math.Max(0, track.duration_ms ?? 0);
        long listenedMs = Math.Max(0, playedMs ?? 0);

        // Старые клиенты присылали только count_play=true; для совместимости
        // оставляем прежнюю оценку полной длительностью. Новый клиент досылает
        // реальные куски played_ms, поэтому накрутка не превращается в часы.
        if (listenedMs <= 0 && countPlay)
            listenedMs = durationMs;

        if (durationMs > 0 && listenedMs > durationMs)
            listenedMs = durationMs;

        // sanity для треков без duration: один mark не должен записать сутки.
        listenedMs = Math.Min(listenedMs, TimeSpan.FromHours(6).Ticks / TimeSpan.TicksPerMillisecond);

        if (!countPlay && listenedMs <= 0)
            return;

        var now = DateTime.UtcNow;

        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));
        var acquired = false;

        try
        {
            if (!await semaphore.WaitAsync())
                return;

            acquired = true;

            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = countPlay
                ? """
                    INSERT INTO track_stats_daily (profile_id, track_id, day, play_count, total_ms, last_played)
                    VALUES ($profile_id, $track_id, $day, 1, $total_ms, $last_played)
                    ON CONFLICT(profile_id, track_id, day) DO UPDATE SET
                        play_count = play_count + 1,
                        total_ms = total_ms + excluded.total_ms,
                        last_played = excluded.last_played;
                    """
                : """
                    UPDATE track_stats_daily
                    SET total_ms = total_ms + $total_ms,
                        last_played = $last_played
                    WHERE profile_id = $profile_id
                        AND track_id = $track_id
                        AND day = $day
                        AND play_count > 0;
                    """;

            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$track_id", track.id);
            command.Parameters.AddWithValue("$day", now.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$total_ms", listenedMs);
            command.Parameters.AddWithValue("$last_played", now);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (acquired)
                semaphore.Release();
        }
    }

    public static async Task<MusicStatsTopResult> GetTopAsync(string profileId, int limit = 30, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);
        var result = new MusicStatsTopResult();

        if (string.IsNullOrWhiteSpace(profileId))
            return result;

        limit = Math.Clamp(limit, 1, 100);

        var rows = new List<(string trackId, long plays, long totalMs, DateTime lastPlayed)>();
        long totalPlays = 0;
        long totalMs = 0;
        long distinctTracks = 0;

        await using (var connection = new SqliteConnection(MusicContext.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);

            await using (var totals = connection.CreateCommand())
            {
                totals.CommandText = """
                    SELECT COALESCE(SUM(play_count), 0), COALESCE(SUM(total_ms), 0), COUNT(DISTINCT track_id)
                    FROM track_stats_daily
                    WHERE profile_id = $profile_id;
                    """;
                totals.Parameters.AddWithValue("$profile_id", profileId);

                await using var totalsReader = await totals.ExecuteReaderAsync(cancellationToken);
                if (await totalsReader.ReadAsync(cancellationToken))
                {
                    totalPlays = totalsReader.GetInt64(0);
                    totalMs = totalsReader.GetInt64(1);
                    distinctTracks = totalsReader.GetInt64(2);
                }
            }

            await using (var top = connection.CreateCommand())
            {
                top.CommandText = """
                    SELECT track_id, SUM(play_count) AS plays, SUM(total_ms) AS ms, MAX(last_played) AS last
                    FROM track_stats_daily
                    WHERE profile_id = $profile_id
                    GROUP BY track_id
                    ORDER BY plays DESC, last DESC
                    LIMIT $limit;
                    """;
                top.Parameters.AddWithValue("$profile_id", profileId);
                top.Parameters.AddWithValue("$limit", limit);

                await using var reader = await top.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var trackId = reader.IsDBNull(0) ? null : reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(trackId))
                        continue;

                    rows.Add((
                        trackId,
                        reader.GetInt64(1),
                        reader.GetInt64(2),
                        reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3)
                    ));
                }
            }
        }

        result.total_plays = totalPlays;
        result.total_ms = totalMs;

        long maxTrackPlays = rows.Count > 0 ? rows[0].plays : 0;
        result.unlocked = distinctTracks >= UnlockDistinctTracks
            && (maxTrackPlays >= UnlockTrackPlays || totalPlays >= UnlockTotalPlays);

        if (!result.unlocked || rows.Count == 0)
            return result;

        // витрина: payload треков из playback_history (mark всегда пишет туда же)
        var payloads = await MusicPlaybackHistoryService.GetTracksAsync(profileId, rows.Select(i => i.trackId), cancellationToken);

        foreach (var row in rows)
        {
            if (!payloads.TryGetValue(row.trackId, out var track) || track == null)
                continue;

            result.tracks.Add(new MusicStatsTopTrack
            {
                track = track,
                play_count = row.plays,
                total_ms = row.totalMs,
                last_played = row.lastPlayed == DateTime.MinValue ? null : row.lastPlayed
            });
        }

        result.available = result.tracks.Count > 0;
        return result;
    }

    static string NormalizeProfileId(string profileId)
        => string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim().ToLowerInvariant();
}

public class MusicStatsTopResult
{
    public bool available { get; set; }

    public bool unlocked { get; set; }

    public long total_plays { get; set; }

    public long total_ms { get; set; }

    public List<MusicStatsTopTrack> tracks { get; set; } = new();
}

public class MusicStatsTopTrack
{
    public MusicTrack track { get; set; }

    public long play_count { get; set; }

    public long total_ms { get; set; }

    public DateTime? last_played { get; set; }
}
