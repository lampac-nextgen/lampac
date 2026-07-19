using Microsoft.Data.Sqlite;

namespace Music;

public static class MusicPlaybackHistoryService
{
    public static async Task SaveAsync(string profileId, MusicTrack track, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);

        if (track == null || string.IsNullOrWhiteSpace(track.id) || string.IsNullOrWhiteSpace(profileId))
            return;

        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));

        try
        {
            if (!await semaphore.WaitAsync())
                return;

            await using var connection = new SqliteConnection(MusicContext.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO playback_history (profile_id, track_id, payload, updated)
                VALUES ($profile_id, $track_id, $payload, $updated)
                ON CONFLICT(profile_id, track_id) DO UPDATE SET
                    payload = excluded.payload,
                    updated = excluded.updated;
                """;

            command.Parameters.AddWithValue("$profile_id", profileId);
            command.Parameters.AddWithValue("$track_id", track.id);
            command.Parameters.AddWithValue("$payload", MusicJson.Serialize(track));
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task<List<MusicRecentlyPlayedItem>> GetRecentAsync(string profileId, int limit = 12, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);
        var items = new List<MusicRecentlyPlayedItem>();

        if (string.IsNullOrWhiteSpace(profileId))
            return items;

        await using var connection = new SqliteConnection(MusicContext.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload, updated
            FROM playback_history
            WHERE profile_id = $profile_id
            ORDER BY updated DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(payload))
                continue;

            var track = MusicJson.Deserialize<MusicTrack>(payload);
            if (track == null)
                continue;

            items.Add(new MusicRecentlyPlayedItem
            {
                track = track,
                played_at = reader.IsDBNull(1) ? DateTime.UtcNow : reader.GetDateTime(1)
            });
        }

        return items;
    }

    public static async Task<MusicTrack> GetTrackAsync(string profileId, string trackId, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);
        trackId = string.IsNullOrWhiteSpace(trackId) ? null : trackId.Trim();

        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(trackId))
            return null;

        await using var connection = new SqliteConnection(MusicContext.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload
            FROM playback_history
            WHERE profile_id = $profile_id
              AND track_id = $track_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$track_id", trackId);

        var payload = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(payload) ? null : MusicJson.Deserialize<MusicTrack>(payload);
    }

    public static async Task<Dictionary<string, MusicTrack>> GetTracksAsync(string profileId, IEnumerable<string> trackIds, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);

        if (string.IsNullOrWhiteSpace(profileId) || trackIds == null)
            return new Dictionary<string, MusicTrack>(StringComparer.Ordinal);

        var normalizedIds = trackIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedIds.Count == 0)
            return new Dictionary<string, MusicTrack>(StringComparer.Ordinal);

        await using var connection = new SqliteConnection(MusicContext.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        var placeholders = new List<string>(normalizedIds.Count);

        for (int i = 0; i < normalizedIds.Count; i++)
        {
            string parameterName = $"$track_id_{i}";
            placeholders.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, normalizedIds[i]);
        }

        command.CommandText = $"""
            SELECT track_id, payload
            FROM playback_history
            WHERE profile_id = $profile_id
              AND track_id IN ({string.Join(", ", placeholders)});
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);

        var result = new Dictionary<string, MusicTrack>(normalizedIds.Count, StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var trackId = reader.IsDBNull(0) ? null : reader.GetString(0);
            var payload = reader.IsDBNull(1) ? null : reader.GetString(1);

            if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(payload))
                continue;

            var track = MusicJson.Deserialize<MusicTrack>(payload);
            if (track != null)
                result[trackId] = track;
        }

        return result;
    }

    public static async Task<bool> RemoveAsync(string profileId, string trackId, CancellationToken cancellationToken = default)
    {
        profileId = NormalizeProfileId(profileId);
        trackId = string.IsNullOrWhiteSpace(trackId) ? null : trackId.Trim();

        if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(trackId))
            return false;

        await using var connection = new SqliteConnection(MusicContext.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM playback_history
            WHERE profile_id = $profile_id
              AND track_id = $track_id;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId);
        command.Parameters.AddWithValue("$track_id", trackId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    static string NormalizeProfileId(string profileId)
        => string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim().ToLowerInvariant();
}
