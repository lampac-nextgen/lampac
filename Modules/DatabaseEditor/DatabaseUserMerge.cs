using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DatabaseEditor;

public sealed class DatabaseUserArea
{
    public string user { get; set; }
    public long syncRecords { get; set; }
    public long timecodeRecords { get; set; }
}

public sealed class MergeUsersRequest
{
    public string firstUser { get; set; }
    public string secondUser { get; set; }
    public string newUser { get; set; }
    public string revision { get; set; }
}

public sealed class MergeUsersResult
{
    public string firstUser { get; set; }
    public string secondUser { get; set; }
    public string targetUser { get; set; }
    public int syncInputRecords { get; set; }
    public int syncRecords { get; set; }
    public int syncActiveCards { get; set; }
    public int syncDuplicates { get; set; }
    public int timecodeInputRecords { get; set; }
    public int timecodeRecords { get; set; }
    public int timecodeActiveRecords { get; set; }
    public int timecodeDuplicates { get; set; }
    public int hashCollisions { get; set; }
    public string revision { get; set; }
    public List<DatabaseBackupResult> backups { get; set; }
}

static partial class DatabaseStore
{
    sealed class SyncMergeRow
    {
        public long id, changed, updated;
        public string user, cardId, card, categories;
    }

    sealed class TimeMergeRow
    {
        public long id, profile, watched, updated;
        public string user, identity, card, item, extra;
        public double position, duration, percent;
        public bool deleted;
    }

    sealed class TimeMergeGroup
    {
        public string identity;
        public List<TimeMergeRow> rows = new();
    }

    sealed class MergePlan
    {
        public MergeUsersResult result;
        public List<SyncMergeRow> bookmarks;
        public List<TimeMergeRow> timecodes;
    }

    public static async Task<List<DatabaseUserArea>> GetUserAreasAsync()
    {
        var users = new Dictionary<string, DatabaseUserArea>(StringComparer.Ordinal);
        foreach (DatabaseSpec spec in new[] { Sync, TimeCode })
        {
            if (!System.IO.File.Exists(spec.path)) continue;
            await using var connection = await OpenAsync(spec);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT user, COUNT(*) FROM {spec.table} WHERE user IS NOT NULL AND TRIM(user) <> '' GROUP BY user;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string user = reader.GetString(0);
                if (!users.TryGetValue(user, out var area))
                    users[user] = area = new DatabaseUserArea { user = user };
                if (spec.timecode) area.timecodeRecords = reader.GetInt64(1);
                else area.syncRecords = reader.GetInt64(1);
            }
        }
        return users.Values.OrderBy(area => area.user, StringComparer.OrdinalIgnoreCase).ThenBy(area => area.user, StringComparer.Ordinal).ToList();
    }

    public static Task<MergeUsersResult> PreviewMergeUsersAsync(MergeUsersRequest request) => MergeUsersInternalAsync(request, apply: false);
    public static Task<MergeUsersResult> MergeUsersAsync(MergeUsersRequest request) => MergeUsersInternalAsync(request, apply: true);

    static async Task<MergeUsersResult> MergeUsersInternalAsync(MergeUsersRequest request, bool apply)
    {
        if (request == null) throw new DatabaseEditorValidationException("request_required");
        string first = ValidateKey(request.firstUser, "first_user_required");
        string second = ValidateKey(request.secondUser, "second_user_required");
        string target = string.IsNullOrWhiteSpace(request.newUser) ? second : ValidateKey(request.newUser, "new_user_required");
        if (first == second) throw new DatabaseEditorValidationException("different_users_required");
        if (apply && string.IsNullOrEmpty(request.revision)) throw new DatabaseEditorValidationException("merge_preview_required");
        var locks = await WriteLocks.AcquireAsync("Sync", "Sync:" + first, "Sync:" + second, "Sync:" + target,
            "TimeCode", "TimeCode:" + first, "TimeCode:" + second, "TimeCode:" + target);
        try
        {
            // Validate both schemas before attaching, and never pool the ATTACH connection.
            await using (var check = await OpenAsync(Sync, pooling: false)) { }
            await using var connection = await OpenAsync(TimeCode, pooling: false);
            await using (var attach = connection.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE @path AS syncdb;";
                attach.Parameters.AddWithValue("@path", System.IO.Path.GetFullPath(Sync.path));
                await attach.ExecuteNonQueryAsync();
            }
            MergePlan plan;
            using (var previewTransaction = connection.BeginTransaction(deferred: true))
            {
                plan = await BuildMergePlanAsync(connection, previewTransaction, first, second, target);
                previewTransaction.Commit();
            }
            if (!apply) return plan.result;
            if (!string.Equals(request.revision, plan.result.revision, StringComparison.Ordinal))
                throw new DatabaseEditorConflictException("merge_preview_changed");

            var backups = new List<DatabaseBackupResult>
            {
                new() { database = TimeCode.key, path = await CreateBackupLockedAsync(TimeCode, "before-merge") },
                new() { database = Sync.key, path = await CreateBackupLockedAsync(Sync, "before-merge") }
            };
            using var transaction = connection.BeginTransaction();
            // Repeat the check under the write transaction; a restore or external writer can
            // change data between the preview and the safety copies.
            var current = await BuildMergePlanAsync(connection, transaction, first, second, target);
            if (current.result.revision != plan.result.revision)
                throw new DatabaseEditorConflictException("merge_preview_changed");
            await DeleteMergeSourcesAsync(connection, transaction, "syncdb.bookmarks", first, second);
            await DeleteMergeSourcesAsync(connection, transaction, "main.timecodes", first, second);
            foreach (var row in current.bookmarks)
            {
                await using var save = connection.CreateCommand();
                save.Transaction = transaction;
                save.CommandText = "INSERT INTO syncdb.bookmarks(Id,user,card_id,card,categories,changed_at,updated_at) VALUES (@id,@user,@cardId,@card,@categories,@changed,@updated);";
                AddMergeParameter(save, "@id", row.id); AddMergeParameter(save, "@user", target);
                AddMergeParameter(save, "@cardId", row.cardId); AddMergeParameter(save, "@card", row.card);
                AddMergeParameter(save, "@categories", row.categories); AddMergeParameter(save, "@changed", row.changed); AddMergeParameter(save, "@updated", row.updated);
                await save.ExecuteNonQueryAsync();
            }
            foreach (var row in current.timecodes)
            {
                await using var save = connection.CreateCommand();
                save.Transaction = transaction;
                save.CommandText = "INSERT INTO main.timecodes(Id,user,identity,card,item,position,duration,percent,profile,deleted,watched_at,updated_at,extra) VALUES (@id,@user,@identity,@card,@item,@position,@duration,@percent,@profile,@deleted,@watched,@updated,@extra);";
                AddMergeParameter(save, "@id", row.id); AddMergeParameter(save, "@user", target); AddMergeParameter(save, "@identity", row.identity);
                AddMergeParameter(save, "@card", row.card); AddMergeParameter(save, "@item", row.item); AddMergeParameter(save, "@position", row.position);
                AddMergeParameter(save, "@duration", row.duration); AddMergeParameter(save, "@percent", row.percent); AddMergeParameter(save, "@profile", row.profile);
                AddMergeParameter(save, "@deleted", row.deleted); AddMergeParameter(save, "@watched", row.watched); AddMergeParameter(save, "@updated", row.updated); AddMergeParameter(save, "@extra", row.extra);
                await save.ExecuteNonQueryAsync();
            }
            transaction.Commit();
            current.result.backups = backups;
            Serilog.Log.Warning("DatabaseEditor merged users {FirstUser} and {SecondUser} into {TargetUser}: {SyncRecords} Sync, {TimecodeRecords} TimeCode records", first, second, target, current.result.syncRecords, current.result.timecodeRecords);
            return current.result;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new DatabaseEditorConflictException("merge_user_conflict");
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new DatabaseEditorBusyException("database_busy");
        }
        finally { locks.Dispose(); }
    }

    static async Task<MergePlan> BuildMergePlanAsync(SqliteConnection connection, SqliteTransaction transaction, string first, string second, string target)
    {
        var bookmarks = new List<SyncMergeRow>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT Id,user,card_id,card,categories,changed_at,updated_at FROM syncdb.bookmarks WHERE user IN (@first,@second) ORDER BY Id;";
            AddMergeParameter(query, "@first", first); AddMergeParameter(query, "@second", second);
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync()) bookmarks.Add(new SyncMergeRow { id = reader.GetInt64(0), user = reader.GetString(1), cardId = reader.GetString(2), card = ReadString(reader, 3), categories = reader.GetString(4), changed = reader.GetInt64(5), updated = reader.GetInt64(6) });
        }
        var timecodes = new List<TimeMergeRow>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT Id,user,identity,card,item,position,duration,percent,profile,deleted,watched_at,updated_at,extra FROM main.timecodes WHERE user IN (@first,@second) ORDER BY Id;";
            AddMergeParameter(query, "@first", first); AddMergeParameter(query, "@second", second);
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync()) timecodes.Add(new TimeMergeRow { id = reader.GetInt64(0), user = reader.GetString(1), identity = ReadString(reader, 2), card = reader.GetString(3), item = ReadString(reader, 4), position = reader.GetDouble(5), duration = reader.GetDouble(6), percent = reader.GetDouble(7), profile = reader.GetInt64(8), deleted = reader.GetBoolean(9), watched = reader.GetInt64(10), updated = reader.GetInt64(11), extra = ReadString(reader, 12) });
        }
        if ((!bookmarks.Any(row => row.user == first) && !timecodes.Any(row => row.user == first)) ||
            (!bookmarks.Any(row => row.user == second) && !timecodes.Any(row => row.user == second)))
            throw new DatabaseEditorValidationException("user_not_found");
        foreach (string table in new[] { "main.timecodes", "syncdb.bookmarks" })
        {
            await using var occupied = connection.CreateCommand();
            occupied.Transaction = transaction;
            occupied.CommandText = $"SELECT 1 FROM {table} WHERE user = @target COLLATE NOCASE AND user NOT IN (@first,@second) LIMIT 1;";
            AddMergeParameter(occupied, "@first", first); AddMergeParameter(occupied, "@second", second); AddMergeParameter(occupied, "@target", target);
            if (await occupied.ExecuteScalarAsync() != null) throw new DatabaseEditorConflictException("merge_target_occupied");
        }
        string revisionData = JsonSerializer.Serialize(new { first, second, target, bookmarks, timecodes }, new JsonSerializerOptions { IncludeFields = true });
        var result = new MergeUsersResult
        {
            firstUser = first,
            secondUser = second,
            targetUser = target,
            syncInputRecords = bookmarks.Count,
            timecodeInputRecords = timecodes.Count,
            revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revisionData)))
        };
        var mergedBookmarks = MergeBookmarks(bookmarks, target);
        var mergedTimecodes = MergeTimecodes(timecodes, target, out int hashCollisions);
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long syncStamp = Math.Max(now, bookmarks.Select(row => Math.Max(row.updated, row.changed)).DefaultIfEmpty(0).Max() + 1);
        foreach (var row in mergedBookmarks)
            foreach (var category in MergeJsonObject(row.categories)) syncStamp = Math.Max(syncStamp, MergeCategoryStamp(category.Value) + 1);
        foreach (var row in mergedBookmarks.OrderBy(row => row.id)) row.updated = row.changed = syncStamp++;
        long timeStamp = Math.Max(now, timecodes.Select(row => row.updated).DefaultIfEmpty(0).Max() + 1);
        foreach (var row in mergedTimecodes.OrderBy(row => row.id)) row.updated = timeStamp++;
        result.syncRecords = mergedBookmarks.Count; result.syncActiveCards = mergedBookmarks.Count(row => MergeJsonObject(row.categories).Count > 0);
        result.timecodeRecords = mergedTimecodes.Count; result.timecodeActiveRecords = mergedTimecodes.Count(row => !row.deleted);
        result.syncDuplicates = bookmarks.Count - mergedBookmarks.Count; result.timecodeDuplicates = timecodes.Count - mergedTimecodes.Count;
        result.hashCollisions = hashCollisions;
        return new MergePlan { result = result, bookmarks = mergedBookmarks, timecodes = mergedTimecodes };
    }

    static List<SyncMergeRow> MergeBookmarks(List<SyncMergeRow> rows, string target)
    {
        var result = new List<SyncMergeRow>();
        foreach (var group in rows.GroupBy(row => row.cardId, StringComparer.Ordinal))
        {
            var ordered = group.OrderByDescending(row => MergeJsonObject(row.categories).Count > 0).ThenByDescending(row => row.changed).ThenByDescending(row => row.updated).ThenByDescending(row => row.user == target).ThenBy(row => row.id).ToList();
            var categories = new JsonObject();
            foreach (var row in ordered)
            {
                foreach (var pair in MergeJsonObject(row.categories))
                    if (!categories.ContainsKey(pair.Key) || MergeCategoryStamp(pair.Value) > MergeCategoryStamp(categories[pair.Key]))
                        categories[pair.Key] = pair.Value?.DeepClone();
            }
            string status = SyncStatusCategories.Where(categories.ContainsKey).OrderByDescending(key => MergeCategoryStamp(categories[key])).FirstOrDefault();
            foreach (string key in SyncStatusCategories) if (key != status) categories.Remove(key);
            JsonObject card = new();
            foreach (var row in ordered.Where(row => !string.IsNullOrEmpty(row.card))) FillMissingMergeFields(card, MergeJsonObject(row.card));
            result.Add(new SyncMergeRow { id = group.FirstOrDefault(row => row.user == target)?.id ?? ordered[0].id, user = target, cardId = group.Key, card = card.Count == 0 ? null : card.ToJsonString(), categories = categories.ToJsonString() });
        }
        return result;
    }

    static List<TimeMergeRow> MergeTimecodes(List<TimeMergeRow> rows, string target, out int hashCollisions)
    {
        var groups = rows.Where(row => !string.IsNullOrEmpty(row.identity)).GroupBy(row => row.identity, StringComparer.Ordinal)
            .Select(group => new TimeMergeGroup { identity = group.Key, rows = group.ToList() }).ToList();
        var webGroups = new Dictionary<(string card, string item), TimeMergeGroup>();
        foreach (var row in rows.Where(row => string.IsNullOrEmpty(row.identity)))
        {
            if (string.IsNullOrEmpty(row.item)) throw new DatabaseEditorValidationException("merge_invalid_data");
            var matching = groups.Where(group => group.rows.Any(native => native.card == row.card && native.item == row.item)).ToList();
            if (matching.Count == 1) matching[0].rows.Add(row);
            else
            {
                var key = (row.card, row.item);
                if (!webGroups.TryGetValue(key, out var group)) webGroups[key] = group = new TimeMergeGroup();
                group.rows.Add(row);
            }
        }
        groups.AddRange(webGroups.Values);
        var result = new List<TimeMergeRow>();
        foreach (var group in groups)
        {
            var ordered = group.rows.OrderBy(row => row.deleted).ThenByDescending(row => row.watched).ThenByDescending(row => row.updated).ThenByDescending(row => row.user == target).ThenBy(row => row.id).ToList();
            var winner = ordered[0];
            var targetRow = group.rows.FirstOrDefault(row => row.user == target);
            JsonObject extra = new();
            foreach (var row in ordered.Where(row => !string.IsNullOrEmpty(row.extra))) FillMissingMergeFields(extra, MergeJsonObject(row.extra));
            result.Add(new TimeMergeRow
            {
                id = targetRow?.id ?? winner.id,
                user = target,
                identity = group.identity,
                card = winner.card,
                item = (targetRow?.card == winner.card ? targetRow.item : null) ?? winner.item ?? ordered.FirstOrDefault(row => !string.IsNullOrEmpty(row.item))?.item,
                position = winner.position,
                duration = winner.duration,
                percent = winner.percent,
                profile = winner.profile,
                deleted = winner.deleted,
                watched = winner.watched,
                extra = extra.Count == 0 ? null : extra.ToJsonString()
            });
        }
        hashCollisions = 0;
        var occupied = new HashSet<(string card, string item)>();
        // A web-only row needs its hash. Native identities remain addressable when a hash
        // collides, so retain both native rows and detach only the conflicting hash.
        foreach (var row in result.OrderBy(row => row.identity != null).ThenByDescending(row => rows.Any(original => original.id == row.id && original.user == target)).ThenByDescending(row => row.watched).ThenBy(row => row.id))
        {
            if (row.item == null || occupied.Add((row.card, row.item))) continue;
            if (row.identity == null) throw new DatabaseEditorConflictException("merge_user_conflict");
            row.item = null; hashCollisions++;
        }
        return result;
    }

    static JsonObject MergeJsonObject(string text)
    {
        if (string.IsNullOrEmpty(text)) return new JsonObject();
        try
        {
            if (JsonNode.Parse(text) is JsonObject value) return value;
        }
        catch (JsonException) { }
        throw new DatabaseEditorValidationException("merge_invalid_data");
    }

    static long MergeCategoryStamp(JsonNode value) => long.TryParse(NodeText(value), out long stamp) ? stamp : 0;

    static void FillMissingMergeFields(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (!target.ContainsKey(pair.Key) || target[pair.Key] == null) target[pair.Key] = pair.Value?.DeepClone();
            else if (target[pair.Key] is JsonObject targetObject && pair.Value is JsonObject sourceObject) FillMissingMergeFields(targetObject, sourceObject);
        }
    }

    static void AddMergeParameter(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    static async Task DeleteMergeSourcesAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string first, string second)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE user IN (@first,@second);";
        AddMergeParameter(command, "@first", first); AddMergeParameter(command, "@second", second);
        await command.ExecuteNonQueryAsync();
    }
}
