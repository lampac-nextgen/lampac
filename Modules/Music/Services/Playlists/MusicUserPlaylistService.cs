using Microsoft.Data.Sqlite;

namespace Music;

// пользовательские плейлисты — durable state (намерение пользователя),
// скоуп per-profile как у playback_history; payload = JSON-массив MusicTrack,
// плейлист самодостаточен и не требует метадата-лукапов при открытии
public static class MusicUserPlaylistService
{
    public static async Task<List<MusicUserPlaylistSummary>> ListAsync(string profileId, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfile(profileId);
        var result = new List<MusicUserPlaylistSummary>();

        try
        {
            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT playlist_id, title, payload, source
                FROM user_playlists
                WHERE profile_id = $profile_id
                ORDER BY updated DESC;
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tracks = ParseTracks(reader.IsDBNull(2) ? null : reader.GetString(2));
                result.Add(BuildSummary(
                    reader.GetString(0),
                    reader.GetString(1),
                    tracks,
                    ParseSource(reader.IsDBNull(3) ? null : reader.GetString(3))
                ));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] playlists list failed: {ex.Message}");
        }

        return result;
    }

    public static async Task<MusicUserPlaylistSummary> GetSummaryAsync(string profileId, string playlistId, CancellationToken cancellationToken = default)
    {
        playlistId = playlistId?.Trim();
        if (string.IsNullOrWhiteSpace(playlistId))
            return null;

        profileId = NormalizeProfile(profileId);

        try
        {
            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT playlist_id, title, payload, source
                FROM user_playlists
                WHERE profile_id = $profile_id AND playlist_id = $playlist_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$playlist_id", playlistId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            var tracks = ParseTracks(reader.IsDBNull(2) ? null : reader.GetString(2));
            return BuildSummary(
                reader.GetString(0),
                reader.GetString(1),
                tracks,
                ParseSource(reader.IsDBNull(3) ? null : reader.GetString(3))
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] playlist summary failed: {ex.Message}");
            return null;
        }
    }

    public static async Task<List<MusicTrack>> GetTracksAsync(string profileId, string playlistId, CancellationToken cancellationToken = default)
    {
        string payload = await ReadPayloadAsync(NormalizeProfile(profileId), playlistId?.Trim(), cancellationToken);
        return ParseTracks(payload);
    }

    public static async Task<string> CreateAsync(string profileId, string title, CancellationToken cancellationToken = default)
    {
        title = title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return null;

        profileId = NormalizeProfile(profileId);
        string playlistId = Guid.NewGuid().ToString("N");

        bool saved = await WriteAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO user_playlists (profile_id, playlist_id, title, payload, updated)
                VALUES ($profile_id, $playlist_id, $title, '[]', $updated);
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$playlist_id", playlistId);
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }, cancellationToken);

        return saved ? playlistId : null;
    }

    // импорт по ссылке: сервис определяется хостом (Spotify / Apple Music / SoundCloud),
    // SoundCloud остаётся веткой по умолчанию (короткие on.soundcloud.com ссылки)
    static Task<MusicUserPlaylistImportResult> ImportByUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (SpotifySupport.CanHandleUrl(url))
            return SpotifySupport.ImportPlaylistAsync(url, cancellationToken);

        if (AppleMusicSupport.CanHandleUrl(url))
            return AppleMusicSupport.ImportPlaylistAsync(url, cancellationToken);

        return SoundCloudSupport.ImportPlaylistAsync(url, cancellationToken);
    }

    static Task<MusicUserPlaylistImportResult> ImportBySourceAsync(MusicUserPlaylistSource source, CancellationToken cancellationToken)
    {
        if (IsSpotifySource(source))
            return SpotifySupport.ImportPlaylistAsync(source, cancellationToken);

        if (IsAppleMusicSource(source))
            return AppleMusicSupport.ImportPlaylistAsync(source, cancellationToken);

        return SoundCloudSupport.ImportPlaylistAsync(source, cancellationToken);
    }

    public static async Task<MusicUserPlaylistImportResult> ImportAsync(string profileId, string url, CancellationToken cancellationToken = default)
    {
        var imported = await ImportByUrlAsync(url, cancellationToken);
        if (imported?.available != true || imported.tracks == null || imported.tracks.Count == 0 || imported.source == null)
            return imported ?? new MusicUserPlaylistImportResult { available = false, message = "Импорт не удался." };

        profileId = NormalizeProfile(profileId);
        imported.title = string.IsNullOrWhiteSpace(imported.title) ? "Импорт" : imported.title.Trim();
        imported.track_count = imported.tracks.Count;

        string savedPlaylistId = null;
        bool saved = await WriteAsync(async connection =>
        {
            savedPlaylistId = await FindSourcePlaylistIdAsync(connection, profileId, imported.source, cancellationToken)
                ?? Guid.NewGuid().ToString("N");

            return await UpsertPlaylistAsync(connection, profileId, savedPlaylistId, imported.title, imported.tracks, imported.source, cancellationToken) > 0;
        }, cancellationToken);

        if (!saved)
        {
            imported.available = false;
            imported.message = "Не удалось сохранить импортированный плейлист.";
            return imported;
        }

        imported.playlist_id = savedPlaylistId;
        return imported;
    }

    public static async Task<MusicUserPlaylistImportResult> SyncAsync(string profileId, string playlistId, CancellationToken cancellationToken = default)
    {
        playlistId = playlistId?.Trim();
        profileId = NormalizeProfile(profileId);
        if (string.IsNullOrWhiteSpace(playlistId))
            return new MusicUserPlaylistImportResult { available = false, message = "Плейлист не выбран." };

        var source = await GetSourceAsync(profileId, playlistId, cancellationToken);
        if (source == null || !IsSyncableSource(source))
            return new MusicUserPlaylistImportResult { available = false, playlist_id = playlistId, message = "У плейлиста нет источника для обновления." };

        var imported = await ImportBySourceAsync(source, cancellationToken);
        if (imported?.available != true || imported.tracks == null || imported.tracks.Count == 0)
            return imported ?? new MusicUserPlaylistImportResult { available = false, playlist_id = playlistId, message = "Обновление не удалось." };

        imported.playlist_id = playlistId;
        imported.title = string.IsNullOrWhiteSpace(imported.title) ? source.title ?? "Импорт" : imported.title.Trim();
        imported.track_count = imported.tracks.Count;
        imported.source ??= source;

        bool saved = await WriteAsync(async connection =>
        {
            // sync — слияние, а не зеркало: локальный порядок и ручные правки
            // сохраняются, новые треки источника добавляются В НАЧАЛО
            string currentPayload = await ReadPayloadAsync(connection, profileId, playlistId, cancellationToken);
            var merged = MergeSyncedTracks(ParseTracks(currentPayload), imported.tracks);

            imported.tracks = merged;
            imported.track_count = merged.Count;

            return await UpsertPlaylistAsync(connection, profileId, playlistId, imported.title, merged, imported.source, cancellationToken) > 0;
        }, cancellationToken);

        if (!saved)
        {
            imported.available = false;
            imported.message = "Не удалось обновить плейлист.";
        }

        return imported;
    }

    static List<MusicTrack> MergeSyncedTracks(List<MusicTrack> local, List<MusicTrack> source)
    {
        source ??= new List<MusicTrack>();

        if (local == null || local.Count == 0)
            return source;

        var localIds = new HashSet<string>(
            local.Where(i => !string.IsNullOrWhiteSpace(i?.id)).Select(i => i.id),
            StringComparer.Ordinal);

        // новинки источника — наверх, в порядке источника
        var fresh = source
            .Where(i => !string.IsNullOrWhiteSpace(i?.id) && !localIds.Contains(i.id))
            .ToList();

        // существующие треки остаются в локальном порядке, но метаданные
        // обновляются свежими из источника (обложки, длительность и т.п.);
        // пропавшие из источника и добавленные вручную — не удаляются
        var sourceById = new Dictionary<string, MusicTrack>(StringComparer.Ordinal);
        foreach (var track in source)
        {
            if (!string.IsNullOrWhiteSpace(track?.id))
                sourceById.TryAdd(track.id, track);
        }

        var merged = new List<MusicTrack>(fresh.Count + local.Count);
        merged.AddRange(fresh);

        foreach (var track in local)
        {
            if (track == null)
                continue;

            merged.Add(!string.IsNullOrWhiteSpace(track.id) && sourceById.TryGetValue(track.id, out var updated)
                ? updated
                : track);
        }

        return merged;
    }

    public static Task<bool> DeleteAsync(string profileId, string playlistId, CancellationToken cancellationToken = default)
    {
        playlistId = playlistId?.Trim();
        if (string.IsNullOrWhiteSpace(playlistId))
            return Task.FromResult(false);

        profileId = NormalizeProfile(profileId);
        return WriteAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM user_playlists
                WHERE profile_id = $profile_id AND playlist_id = $playlist_id;
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$playlist_id", playlistId);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }, cancellationToken);
    }

    public static async Task<bool> AddTrackAsync(string profileId, string playlistId, MusicTrack track, CancellationToken cancellationToken = default)
    {
        playlistId = playlistId?.Trim();
        if (string.IsNullOrWhiteSpace(playlistId) || track == null || string.IsNullOrWhiteSpace(track.id) || string.IsNullOrWhiteSpace(track.title))
            return false;

        profileId = NormalizeProfile(profileId);

        return await WriteAsync(async connection =>
        {
            string payload = await ReadPayloadAsync(connection, profileId, playlistId, cancellationToken);
            if (payload == null)
                return false;

            var tracks = ParseTracks(payload);
            if (tracks.Any(i => i != null && string.Equals(i.id, track.id, StringComparison.Ordinal)))
                return true;

            tracks.Add(track);
            return await UpdatePayloadAsync(connection, profileId, playlistId, tracks, cancellationToken) > 0;
        }, cancellationToken);
    }

    public static async Task<bool> RemoveTrackAsync(string profileId, string playlistId, string trackId, CancellationToken cancellationToken = default)
    {
        playlistId = playlistId?.Trim();
        trackId = trackId?.Trim();
        if (string.IsNullOrWhiteSpace(playlistId) || string.IsNullOrWhiteSpace(trackId))
            return false;

        profileId = NormalizeProfile(profileId);

        return await WriteAsync(async connection =>
        {
            string payload = await ReadPayloadAsync(connection, profileId, playlistId, cancellationToken);
            if (payload == null)
                return false;

            var tracks = ParseTracks(payload);
            var next = tracks.Where(i => i != null && !string.Equals(i.id, trackId, StringComparison.Ordinal)).ToList();

            if (next.Count == tracks.Count)
                return false;

            return await UpdatePayloadAsync(connection, profileId, playlistId, next, cancellationToken) > 0;
        }, cancellationToken);
    }

    public static async Task<bool> MoveTrackAsync(string profileId, string playlistId, string trackId, int position, CancellationToken cancellationToken = default)
    {
        playlistId = playlistId?.Trim();
        trackId = trackId?.Trim();
        if (string.IsNullOrWhiteSpace(playlistId) || string.IsNullOrWhiteSpace(trackId))
            return false;

        profileId = NormalizeProfile(profileId);

        return await WriteAsync(async connection =>
        {
            string payload = await ReadPayloadAsync(connection, profileId, playlistId, cancellationToken);
            if (payload == null)
                return false;

            var tracks = ParseTracks(payload);
            int index = tracks.FindIndex(i => i != null && string.Equals(i.id, trackId, StringComparison.Ordinal));
            if (index < 0)
                return false;

            int target = Math.Clamp(position, 0, tracks.Count - 1);
            if (target == index)
                return true;

            var track = tracks[index];
            tracks.RemoveAt(index);
            tracks.Insert(target, track);

            return await UpdatePayloadAsync(connection, profileId, playlistId, tracks, cancellationToken) > 0;
        }, cancellationToken);
    }

    static async Task<int> UpdatePayloadAsync(SqliteConnection connection, string profileId, string playlistId, List<MusicTrack> tracks, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE user_playlists
            SET payload = $payload, updated = $updated
            WHERE profile_id = $profile_id AND playlist_id = $playlist_id;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$playlist_id", playlistId);
        command.Parameters.AddWithValue("$payload", MusicJson.Serialize(tracks));
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    static async Task<int> UpsertPlaylistAsync(SqliteConnection connection, string profileId, string playlistId, string title, List<MusicTrack> tracks, MusicUserPlaylistSource source, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_playlists (profile_id, playlist_id, title, payload, source, updated)
            VALUES ($profile_id, $playlist_id, $title, $payload, $source, $updated)
            ON CONFLICT(profile_id, playlist_id) DO UPDATE SET
                title = excluded.title,
                payload = excluded.payload,
                source = excluded.source,
                updated = excluded.updated;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$playlist_id", playlistId);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$payload", MusicJson.Serialize(tracks ?? new List<MusicTrack>()));
        command.Parameters.AddWithValue("$source", SerializeSource(source));
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    static async Task<string> ReadPayloadAsync(string profileId, string playlistId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
            return null;

        try
        {
            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT payload
                FROM user_playlists
                WHERE profile_id = $profile_id AND playlist_id = $playlist_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$playlist_id", playlistId);

            return await command.ExecuteScalarAsync(cancellationToken) as string;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] playlist read failed: {ex.Message}");
            return null;
        }
    }

    static async Task<string> ReadPayloadAsync(SqliteConnection connection, string profileId, string playlistId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM user_playlists
            WHERE profile_id = $profile_id AND playlist_id = $playlist_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$playlist_id", playlistId);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    static async Task<MusicUserPlaylistSource> GetSourceAsync(string profileId, string playlistId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT source
                FROM user_playlists
                WHERE profile_id = $profile_id AND playlist_id = $playlist_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$playlist_id", playlistId);

            return ParseSource(await command.ExecuteScalarAsync(cancellationToken) as string);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] playlist source read failed: {ex.Message}");
            return null;
        }
    }

    static async Task<string> FindSourcePlaylistIdAsync(SqliteConnection connection, string profileId, MusicUserPlaylistSource source, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT playlist_id, source
            FROM user_playlists
            WHERE profile_id = $profile_id AND source <> '';
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var existing = ParseSource(reader.IsDBNull(1) ? null : reader.GetString(1));
            if (IsSameSource(existing, source))
                return reader.GetString(0);
        }

        return null;
    }

    static async Task<bool> WriteAsync(Func<SqliteConnection, Task<bool>> operation, CancellationToken cancellationToken)
    {
        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));
        bool acquired = false;

        try
        {
            if (!await semaphore.WaitAsync())
            {
                Console.WriteLine("[Music] playlist write skipped: db semaphore timeout");
                return false;
            }
            acquired = true;

            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            return await operation(connection);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Music] playlist write failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (acquired)
                semaphore.Release();
        }
    }

    static List<MusicTrack> ParseTracks(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new List<MusicTrack>();

        try
        {
            return MusicJson.Deserialize<List<MusicTrack>>(payload) ?? new List<MusicTrack>();
        }
        catch
        {
            return new List<MusicTrack>();
        }
    }

    static MusicUserPlaylistSummary BuildSummary(string playlistId, string title, List<MusicTrack> tracks, MusicUserPlaylistSource source)
    {
        tracks ??= new List<MusicTrack>();

        return new MusicUserPlaylistSummary
        {
            id = playlistId,
            title = title,
            track_count = tracks.Count,
            images = tracks.FirstOrDefault(i => i?.images != null && i.images.Count > 0)?.images ?? new List<MusicImage>(),
            source = source
        };
    }

    static MusicUserPlaylistSource ParseSource(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            return MusicJson.Deserialize<MusicUserPlaylistSource>(payload);
        }
        catch
        {
            return null;
        }
    }

    static string SerializeSource(MusicUserPlaylistSource source)
        => source == null || string.IsNullOrWhiteSpace(source.type) ? string.Empty : MusicJson.Serialize(source);

    static bool IsSoundCloudSource(MusicUserPlaylistSource source)
        => source != null && !string.IsNullOrWhiteSpace(source.type) && source.type.StartsWith("soundcloud_", StringComparison.OrdinalIgnoreCase);

    static bool IsSpotifySource(MusicUserPlaylistSource source)
        => source != null && !string.IsNullOrWhiteSpace(source.type) && source.type.StartsWith("spotify_", StringComparison.OrdinalIgnoreCase);

    static bool IsAppleMusicSource(MusicUserPlaylistSource source)
        => source != null && !string.IsNullOrWhiteSpace(source.type) && source.type.StartsWith("applemusic_", StringComparison.OrdinalIgnoreCase);

    static bool IsSyncableSource(MusicUserPlaylistSource source)
        => IsSoundCloudSource(source) || IsSpotifySource(source) || IsAppleMusicSource(source);

    static bool IsSameSource(MusicUserPlaylistSource left, MusicUserPlaylistSource right)
    {
        if (left == null || right == null)
            return false;

        if (!string.Equals(left.type, right.type, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(left.user_id) || !string.IsNullOrWhiteSpace(right.user_id))
            return string.Equals(left.user_id, right.user_id, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(left.playlist_id) || !string.IsNullOrWhiteSpace(right.playlist_id))
            return string.Equals(left.playlist_id, right.playlist_id, StringComparison.OrdinalIgnoreCase);

        return string.Equals(left.url, right.url, StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeProfile(string profileId)
        => string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
}
