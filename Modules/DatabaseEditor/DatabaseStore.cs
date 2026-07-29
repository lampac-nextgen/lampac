using Microsoft.Data.Sqlite;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DatabaseEditor;

public sealed class SaveRecordRequest
{
    public string database { get; set; }
    public long? id { get; set; }
    public string user { get; set; }
    public string card { get; set; }
    public string item { get; set; }
    public string data { get; set; }
}

public sealed class DeleteRecordRequest
{
    public string database { get; set; }
    public long id { get; set; }
}

public sealed class BackupRequest
{
    public string database { get; set; }
}

public sealed class RestoreBackupRequest
{
    public string database { get; set; }
    public string file { get; set; }
}

public sealed class RenameUserRequest
{
    public string oldUser { get; set; }
    public string newUser { get; set; }
}

public sealed class RenameUserResult
{
    public string oldUser { get; set; }
    public string newUser { get; set; }
    public int syncRecords { get; set; }
    public int timecodeRecords { get; set; }
}

public sealed class DeleteUserRequest
{
    public string user { get; set; }
}

public sealed class DeleteUserResult
{
    public string user { get; set; }
    public int syncRecords { get; set; }
    public int timecodeRecords { get; set; }
}

public sealed class SaveSyncItemRequest
{
    public long recordId { get; set; }
    public string cardId { get; set; }
    public string[] categories { get; set; }
}

public sealed class DeleteSyncItemRequest
{
    public long recordId { get; set; }
    public string cardId { get; set; }
}

public sealed class DatabaseSummary
{
    public string database { get; set; }
    public string title { get; set; }
    public string file { get; set; }
    public bool available { get; set; }
    public long records { get; set; }
    public long bytes { get; set; }
    public string updated { get; set; }
}

public sealed class DatabaseUserOption
{
    public string user { get; set; }
    public long records { get; set; }
}

public sealed class DatabaseRecord
{
    public long id { get; set; }
    public string user { get; set; }
    public string card { get; set; }
    public string item { get; set; }
    public string data { get; set; }
    public string preview { get; set; }
    public long dataLength { get; set; }
    public string updated { get; set; }
    public string title { get; set; }
    public string poster { get; set; }
    public string year { get; set; }
    public string mediaType { get; set; }
    public int? season { get; set; }
    public int? episode { get; set; }
    public double? position { get; set; }
    public double? duration { get; set; }
    public double? percent { get; set; }
}

public sealed class SyncUserItem
{
    public string cardId { get; set; }
    public string title { get; set; }
    public string poster { get; set; }
    public string year { get; set; }
    public string mediaType { get; set; }
    public List<string> categories { get; set; }
    public int order { get; set; }
    public Dictionary<string, int> categoryOrder { get; set; }
}

public sealed class SyncUserDetails
{
    public long id { get; set; }
    public string user { get; set; }
    public string updated { get; set; }
    public int total { get; set; }
    public List<SyncUserItem> items { get; set; }
}

public sealed class DatabaseBackupResult
{
    public string database { get; set; }
    public string path { get; set; }
}

public sealed class DatabaseBackupFile
{
    public string database { get; set; }
    public string file { get; set; }
    public string path { get; set; }
    public long bytes { get; set; }
    public string created { get; set; }
}

public sealed class DatabaseRestoreResult
{
    public string database { get; set; }
    public string restoredFrom { get; set; }
    public string safetyBackup { get; set; }
}

public sealed class RecordsPage
{
    public string database { get; set; }
    public int page { get; set; }
    public int pageSize { get; set; }
    public long total { get; set; }
    public int pages { get; set; }
    public List<DatabaseRecord> records { get; set; }
}

public sealed class DatabaseEditorValidationException : Exception
{
    public DatabaseEditorValidationException(string message) : base(message) { }
}

public sealed class DatabaseEditorConflictException : Exception
{
    public DatabaseEditorConflictException(string message) : base(message) { }
}

public sealed class DatabaseEditorBusyException : Exception
{
    public DatabaseEditorBusyException(string message) : base(message) { }
}

static class DatabaseStore
{
    const int MaxDataBytes = 8 * 1024 * 1024;
    const int MaxKeyLength = 512;

    public static readonly string[] SyncCategories =
    {
        "history", "like", "watch", "wath", "book", "look",
        "viewed", "scheduled", "continued", "thrown"
    };

    public static readonly string[] SyncEditorCategories =
    {
        "book", "like", "wath", "history",
        "look", "viewed", "scheduled", "continued", "thrown"
    };

    public static readonly string[] SyncStatusCategories =
    {
        "look", "viewed", "scheduled", "continued", "thrown"
    };

    sealed class DatabaseSpec
    {
        public string key;
        public string title;
        public string path;
        public string table;
        public string semaphore;
        public bool timecode;
    }

    sealed class MediaMetadata
    {
        public string title;
        public string poster;
        public string year;
        public string mediaType;
        public List<string> hashTitles;
        public int seasons;
        public int episodes;
    }

    static readonly DatabaseSpec Sync = new()
    {
        key = "sync",
        title = "Sync / закладки",
        path = Path.Combine("database", "Sync.sql"),
        table = "bookmarks",
        semaphore = "Sync"
    };

    static readonly DatabaseSpec TimeCode = new()
    {
        key = "timecode",
        title = "TimeCode / позиции",
        path = Path.Combine("database", "TimeCode.sql"),
        table = "timecodes",
        semaphore = "TimeCode",
        timecode = true
    };

    public static async Task<List<DatabaseSummary>> GetSummaryAsync()
    {
        var result = new List<DatabaseSummary>(2);
        result.Add(await ReadSummaryAsync(TimeCode));
        result.Add(await ReadSummaryAsync(Sync));
        return result;
    }

    public static async Task<List<DatabaseUserOption>> GetUsersAsync(string database)
    {
        DatabaseSpec spec = GetSpec(database);
        await using var connection = await OpenAsync(spec);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT user, COUNT(*) FROM {spec.table} WHERE user IS NOT NULL AND TRIM(user) <> '' GROUP BY user COLLATE NOCASE ORDER BY user COLLATE NOCASE;";

        var users = new List<DatabaseUserOption>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new DatabaseUserOption
            {
                user = ReadString(reader, 0),
                records = reader.IsDBNull(1) ? 0 : reader.GetInt64(1)
            });
        }
        return users;
    }

    public static async Task<RecordsPage> GetRecordsAsync(string database, string query, int page, int pageSize, string user = null)
    {
        DatabaseSpec spec = GetSpec(database);
        page = Math.Max(1, page);
        pageSize = pageSize is 25 or 50 or 100 ? pageSize : 25;
        query = (query ?? string.Empty).Trim();
        if (query.Length > 256)
            throw new DatabaseEditorValidationException("search_too_long");
        user = (user ?? string.Empty).Trim();
        if (user.Length > MaxKeyLength)
            throw new DatabaseEditorValidationException("key_too_long");

        await using var connection = await OpenAsync(spec);
        string where = BuildWhere(spec, query, user, out string searchValue, out string selectedUser);

        long total;
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) FROM {spec.table}{where};";
            AddFilters(count, searchValue, selectedUser);
            total = Convert.ToInt64(await count.ExecuteScalarAsync());
        }

        int pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, pages);
        int offset = (page - 1) * pageSize;
        var records = new List<DatabaseRecord>(pageSize);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = spec.timecode
                ? $"SELECT Id, user, card, item, length(data), data, updated FROM {spec.table}{where} ORDER BY updated DESC, Id DESC LIMIT @limit OFFSET @offset;"
                : $"SELECT Id, user, length(data), substr(data, 1, 420), updated FROM {spec.table}{where} ORDER BY updated DESC, Id DESC LIMIT @limit OFFSET @offset;";
            AddFilters(command, searchValue, selectedUser);
            command.Parameters.AddWithValue("@limit", pageSize);
            command.Parameters.AddWithValue("@offset", offset);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int index = 0;
                var record = new DatabaseRecord
                {
                    id = reader.GetInt64(index++),
                    user = ReadString(reader, index++)
                };

                if (spec.timecode)
                {
                    record.card = ReadString(reader, index++);
                    record.item = ReadString(reader, index++);
                }

                record.dataLength = reader.IsDBNull(index) ? 0 : reader.GetInt64(index);
                index++;
                string dataValue = ReadString(reader, index++);
                record.preview = BuildPreview(dataValue);
                record.updated = ReadString(reader, index);
                if (spec.timecode)
                    ReadPlaybackData(record, dataValue);
                records.Add(record);
            }
        }

        if (spec.timecode && records.Count > 0)
            await EnrichTimeCodeRecordsAsync(records);

        return new RecordsPage
        {
            database = spec.key,
            page = page,
            pageSize = pageSize,
            total = total,
            pages = pages,
            records = records
        };
    }

    public static async Task<DatabaseRecord> GetRecordAsync(string database, long id)
    {
        DatabaseSpec spec = GetSpec(database);
        if (id <= 0)
            throw new DatabaseEditorValidationException("invalid_id");

        await using var connection = await OpenAsync(spec);
        await using var command = connection.CreateCommand();
        command.CommandText = spec.timecode
            ? $"SELECT Id, user, card, item, data, updated FROM {spec.table} WHERE Id = @id LIMIT 1;"
            : $"SELECT Id, user, data, updated FROM {spec.table} WHERE Id = @id LIMIT 1;";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
            return null;

        int index = 0;
        var record = new DatabaseRecord
        {
            id = reader.GetInt64(index++),
            user = ReadString(reader, index++)
        };

        if (spec.timecode)
        {
            record.card = ReadString(reader, index++);
            record.item = ReadString(reader, index++);
        }

        record.data = ReadString(reader, index++);
        record.dataLength = Encoding.UTF8.GetByteCount(record.data ?? string.Empty);
        record.updated = ReadString(reader, index);
        return record;
    }

    public static async Task<SyncUserDetails> GetSyncUserAsync(long id)
    {
        if (id <= 0)
            throw new DatabaseEditorValidationException("invalid_id");

        await using var connection = await OpenAsync(Sync);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, user, data, updated FROM bookmarks WHERE Id = @id LIMIT 1;";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (!await reader.ReadAsync())
            return null;

        string data = ReadString(reader, 2);
        var details = BuildSyncUserDetails(reader.GetInt64(0), ReadString(reader, 1), ReadString(reader, 3), data);
        return details;
    }

    public static async Task<List<string>> SaveSyncItemAsync(SaveSyncItemRequest request)
    {
        if (request == null || request.recordId <= 0)
            throw new DatabaseEditorValidationException("invalid_id");

        string cardId = ValidateKey(request.cardId, "card_required");
        var selected = new HashSet<string>(request.categories ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (string category in selected)
        {
            if (Array.IndexOf(SyncEditorCategories, category) < 0)
                throw new DatabaseEditorValidationException("unknown_category");
        }

        int selectedStatuses = 0;
        foreach (string category in SyncStatusCategories)
        {
            if (selected.Contains(category))
                selectedStatuses++;
        }
        if (selectedStatuses > 1)
            throw new DatabaseEditorValidationException("multiple_statuses");

        await UpdateSyncJsonAsync(request.recordId, root =>
        {
            JsonObject card = FindSyncCard(root, cardId);
            if (card == null)
                throw new DatabaseEditorValidationException("card_not_found");

            foreach (string category in SyncCategories)
            {
                JsonArray array = EnsureArray(root, category);
                bool isSelected = selected.Contains(category);
                bool isPresent = ContainsCardId(array, cardId);
                if (isSelected && !isPresent)
                    array.Insert(0, CreateCardIdNode(cardId));
                else if (!isSelected && isPresent)
                    RemoveCardId(array, cardId);
            }
        });

        Serilog.Log.Information("DatabaseEditor updated Sync card {CardId} in record {RecordId}", cardId, request.recordId);
        var result = new List<string>();
        foreach (string category in SyncEditorCategories)
        {
            if (selected.Contains(category))
                result.Add(category);
        }
        return result;
    }

    public static async Task<bool> DeleteSyncItemAsync(DeleteSyncItemRequest request)
    {
        if (request == null || request.recordId <= 0)
            throw new DatabaseEditorValidationException("invalid_id");

        string cardId = ValidateKey(request.cardId, "card_required");
        bool removed = false;
        await UpdateSyncJsonAsync(request.recordId, root =>
        {
            if (root["card"] is JsonArray cards)
            {
                for (int index = cards.Count - 1; index >= 0; index--)
                {
                    if (cards[index] is JsonObject card && string.Equals(NodeText(card["id"]), cardId, StringComparison.Ordinal))
                    {
                        cards.RemoveAt(index);
                        removed = true;
                    }
                }
            }

            foreach (string category in SyncCategories)
            {
                if (root[category] is JsonArray array)
                    removed |= RemoveCardId(array, cardId);
            }
        });

        if (removed)
            Serilog.Log.Information("DatabaseEditor deleted Sync card {CardId} from record {RecordId}", cardId, request.recordId);
        return removed;
    }

    public static async Task<DatabaseRecord> SaveAsync(SaveRecordRequest request)
    {
        if (request == null)
            throw new DatabaseEditorValidationException("request_required");

        DatabaseSpec spec = GetSpec(request.database);
        string user = ValidateKey(request.user, "user_required");
        string card = spec.timecode ? ValidateKey(request.card, "card_required") : null;
        string item = spec.timecode ? ValidateKey(request.item, "item_required") : null;
        string data = NormalizeJson(request.data);
        long id = request.id.GetValueOrDefault();
        if (id < 0)
            throw new DatabaseEditorValidationException("invalid_id");

        var semaphore = new SemaphorManager(spec.semaphore, TimeSpan.FromSeconds(20));
        bool acquired = await semaphore.WaitAsync();
        if (!acquired)
            throw new DatabaseEditorBusyException("database_busy");

        try
        {
            await using var connection = await OpenAsync(spec);
            using var transaction = connection.BeginTransaction();
            string updated = DateTime.UtcNow.ToString("O");

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            if (id == 0)
            {
                command.CommandText = spec.timecode
                    ? $"INSERT INTO {spec.table} (user, card, item, data, updated) VALUES (@user, @card, @item, @data, @updated); SELECT last_insert_rowid();"
                    : $"INSERT INTO {spec.table} (user, data, updated) VALUES (@user, @data, @updated); SELECT last_insert_rowid();";
            }
            else
            {
                command.CommandText = spec.timecode
                    ? $"UPDATE {spec.table} SET user = @user, card = @card, item = @item, data = @data, updated = @updated WHERE Id = @id;"
                    : $"UPDATE {spec.table} SET user = @user, data = @data, updated = @updated WHERE Id = @id;";
                command.Parameters.AddWithValue("@id", id);
            }

            command.Parameters.AddWithValue("@user", user);
            if (spec.timecode)
            {
                command.Parameters.AddWithValue("@card", card);
                command.Parameters.AddWithValue("@item", item);
            }
            command.Parameters.AddWithValue("@data", data);
            command.Parameters.AddWithValue("@updated", updated);

            if (id == 0)
                id = Convert.ToInt64(await command.ExecuteScalarAsync());
            else if (await command.ExecuteNonQueryAsync() == 0)
                throw new DatabaseEditorValidationException("record_not_found");

            transaction.Commit();
            Serilog.Log.Information("DatabaseEditor saved {Database} record {RecordId}", spec.key, id);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new DatabaseEditorConflictException("duplicate_record_key");
        }
        finally
        {
            semaphore.Release();
        }

        return await GetRecordAsync(spec.key, id);
    }

    public static async Task<bool> DeleteAsync(string database, long id)
    {
        DatabaseSpec spec = GetSpec(database);
        if (id <= 0)
            throw new DatabaseEditorValidationException("invalid_id");

        var semaphore = new SemaphorManager(spec.semaphore, TimeSpan.FromSeconds(20));
        bool acquired = await semaphore.WaitAsync();
        if (!acquired)
            throw new DatabaseEditorBusyException("database_busy");

        try
        {
            await using var connection = await OpenAsync(spec);
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {spec.table} WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            bool deleted = await command.ExecuteNonQueryAsync() > 0;
            if (deleted)
                Serilog.Log.Information("DatabaseEditor deleted {Database} record {RecordId}", spec.key, id);
            return deleted;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task<RenameUserResult> RenameUserAsync(RenameUserRequest request)
    {
        if (request == null)
            throw new DatabaseEditorValidationException("request_required");

        string oldUser = ValidateKey(request.oldUser, "old_user_required");
        string newUser = ValidateKey(request.newUser, "new_user_required");
        if (string.Equals(oldUser, newUser, StringComparison.Ordinal))
            throw new DatabaseEditorValidationException("user_name_unchanged");
        if (!File.Exists(Sync.path) || !File.Exists(TimeCode.path))
            throw new DatabaseEditorValidationException("database_not_found");

        var syncSemaphore = new SemaphorManager(Sync.semaphore, TimeSpan.FromSeconds(20));
        var timecodeSemaphore = new SemaphorManager(TimeCode.semaphore, TimeSpan.FromSeconds(20));
        bool syncAcquired = await syncSemaphore.WaitAsync();
        if (!syncAcquired)
            throw new DatabaseEditorBusyException("database_busy");

        bool timecodeAcquired = false;
        try
        {
            timecodeAcquired = await timecodeSemaphore.WaitAsync();
            if (!timecodeAcquired)
                throw new DatabaseEditorBusyException("database_busy");

            await using var connection = await OpenAsync(TimeCode, pooling: false);
            await using (var attach = connection.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE @syncPath AS syncdb;";
                attach.Parameters.AddWithValue("@syncPath", Path.GetFullPath(Sync.path));
                await attach.ExecuteNonQueryAsync();
            }

            using var transaction = connection.BeginTransaction();
            await EnsureRenameTargetAvailableAsync(connection, transaction, "main.timecodes", oldUser, newUser);
            await EnsureRenameTargetAvailableAsync(connection, transaction, "syncdb.bookmarks", oldUser, newUser);

            int timecodeRecords = await RenameUserRowsAsync(connection, transaction, "main.timecodes", oldUser, newUser);
            int syncRecords = await RenameUserRowsAsync(connection, transaction, "syncdb.bookmarks", oldUser, newUser);
            if (timecodeRecords == 0 && syncRecords == 0)
                throw new DatabaseEditorValidationException("user_not_found");

            transaction.Commit();
            Serilog.Log.Information("DatabaseEditor renamed user {OldUser} to {NewUser}: {SyncRecords} Sync, {TimecodeRecords} TimeCode records", oldUser, newUser, syncRecords, timecodeRecords);
            return new RenameUserResult
            {
                oldUser = oldUser,
                newUser = newUser,
                syncRecords = syncRecords,
                timecodeRecords = timecodeRecords
            };
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new DatabaseEditorConflictException("rename_user_conflict");
        }
        finally
        {
            if (timecodeAcquired)
                timecodeSemaphore.Release();
            syncSemaphore.Release();
        }
    }

    static async Task EnsureRenameTargetAvailableAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string oldUser, string newUser)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT 1 FROM {table} WHERE user = @newUser COLLATE NOCASE AND user <> @oldUser COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue("@oldUser", oldUser);
        command.Parameters.AddWithValue("@newUser", newUser);
        if (await command.ExecuteScalarAsync() != null)
            throw new DatabaseEditorConflictException("rename_user_conflict");
    }

    static async Task<int> RenameUserRowsAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string oldUser, string newUser)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {table} SET user = @newUser WHERE user = @oldUser COLLATE NOCASE;";
        command.Parameters.AddWithValue("@oldUser", oldUser);
        command.Parameters.AddWithValue("@newUser", newUser);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<DeleteUserResult> DeleteUserAsync(DeleteUserRequest request)
    {
        if (request == null)
            throw new DatabaseEditorValidationException("request_required");

        string user = ValidateKey(request.user, "user_required");
        if (!File.Exists(Sync.path) || !File.Exists(TimeCode.path))
            throw new DatabaseEditorValidationException("database_not_found");

        var syncSemaphore = new SemaphorManager(Sync.semaphore, TimeSpan.FromSeconds(20));
        var timecodeSemaphore = new SemaphorManager(TimeCode.semaphore, TimeSpan.FromSeconds(20));
        bool syncAcquired = await syncSemaphore.WaitAsync();
        if (!syncAcquired)
            throw new DatabaseEditorBusyException("database_busy");

        bool timecodeAcquired = false;
        try
        {
            timecodeAcquired = await timecodeSemaphore.WaitAsync();
            if (!timecodeAcquired)
                throw new DatabaseEditorBusyException("database_busy");

            await using var connection = await OpenAsync(TimeCode, pooling: false);
            await using (var attach = connection.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE @syncPath AS syncdb;";
                attach.Parameters.AddWithValue("@syncPath", Path.GetFullPath(Sync.path));
                await attach.ExecuteNonQueryAsync();
            }

            using var transaction = connection.BeginTransaction();
            int timecodeRecords = await DeleteUserRowsAsync(connection, transaction, "main.timecodes", user);
            int syncRecords = await DeleteUserRowsAsync(connection, transaction, "syncdb.bookmarks", user);
            if (timecodeRecords == 0 && syncRecords == 0)
                throw new DatabaseEditorValidationException("user_not_found");

            transaction.Commit();
            Serilog.Log.Information("DatabaseEditor deleted user {User}: {SyncRecords} Sync, {TimecodeRecords} TimeCode records", user, syncRecords, timecodeRecords);
            return new DeleteUserResult { user = user, syncRecords = syncRecords, timecodeRecords = timecodeRecords };
        }
        finally
        {
            if (timecodeAcquired)
                timecodeSemaphore.Release();
            syncSemaphore.Release();
        }
    }

    static async Task<int> DeleteUserRowsAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string user)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE user = @user COLLATE NOCASE;";
        command.Parameters.AddWithValue("@user", user);
        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<string> BackupAsync(string database)
    {
        DatabaseSpec spec = GetSpec(database);
        var semaphore = new SemaphorManager(spec.semaphore, TimeSpan.FromSeconds(20));
        bool acquired = await semaphore.WaitAsync();
        if (!acquired)
            throw new DatabaseEditorBusyException("database_busy");

        try
        {
            string directory = Path.Combine("database", "backup", "database-editor");
            Directory.CreateDirectory(directory);
            string fileName = $"{Path.GetFileNameWithoutExtension(spec.path)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.sql";
            string destinationPath = Path.Combine(directory, fileName);
            if (File.Exists(destinationPath))
                destinationPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(spec.path)}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.sql");

            await using var source = await OpenAsync(spec);
            var destinationBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };
            await using var destination = new SqliteConnection(destinationBuilder.ToString());
            await destination.OpenAsync();
            source.BackupDatabase(destination);
            Serilog.Log.Information("DatabaseEditor backed up {Database} to {BackupPath}", spec.key, destinationPath);
            return destinationPath.Replace('\\', '/');
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task<List<DatabaseBackupResult>> BackupAllAsync()
    {
        var result = new List<DatabaseBackupResult>(2)
        {
            new() { database = TimeCode.key, path = await BackupAsync(TimeCode.key) },
            new() { database = Sync.key, path = await BackupAsync(Sync.key) }
        };
        return result;
    }

    public static List<DatabaseBackupFile> GetBackups(string database)
    {
        DatabaseSpec spec = GetSpec(database);
        string directory = Path.Combine("database", "backup", "database-editor");
        var result = new List<DatabaseBackupFile>();
        if (!Directory.Exists(directory))
            return result;

        string prefix = Path.GetFileNameWithoutExtension(spec.path) + "-";
        foreach (string path in Directory.GetFiles(directory, prefix + "*.sql"))
        {
            var file = new FileInfo(path);
            result.Add(new DatabaseBackupFile
            {
                database = spec.key,
                file = file.Name,
                path = path.Replace('\\', '/'),
                bytes = file.Length,
                created = file.LastWriteTimeUtc.ToString("O")
            });
        }
        result.Sort((left, right) => string.CompareOrdinal(right.created, left.created));
        return result;
    }

    public static async Task<DatabaseRestoreResult> RestoreAsync(RestoreBackupRequest request)
    {
        if (request == null)
            throw new DatabaseEditorValidationException("request_required");

        DatabaseSpec spec = GetSpec(request.database);
        string fileName = Path.GetFileName((request.file ?? string.Empty).Trim());
        if (string.IsNullOrEmpty(fileName) || !string.Equals(fileName, request.file?.Trim(), StringComparison.Ordinal))
            throw new DatabaseEditorValidationException("invalid_backup_file");

        string prefix = Path.GetFileNameWithoutExtension(spec.path) + "-";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !fileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            throw new DatabaseEditorValidationException("invalid_backup_file");

        string directory = Path.GetFullPath(Path.Combine("database", "backup", "database-editor"));
        string sourcePath = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!sourcePath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(sourcePath))
            throw new DatabaseEditorValidationException("backup_not_found");

        var semaphore = new SemaphorManager(spec.semaphore, TimeSpan.FromSeconds(20));
        bool acquired = await semaphore.WaitAsync();
        if (!acquired)
            throw new DatabaseEditorBusyException("database_busy");

        try
        {
            await ValidateBackupAsync(spec, sourcePath);
            string safetyPath = await CreateBackupLockedAsync(spec, "before-restore");

            var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            await using (var source = new SqliteConnection(sourceBuilder.ToString()))
            {
                await source.OpenAsync();
                await using var destination = await OpenAsync(spec);
                source.BackupDatabase(destination);
            }
            SqliteConnection.ClearAllPools();

            Serilog.Log.Warning("DatabaseEditor restored {Database} from {BackupPath}; safety backup: {SafetyPath}", spec.key, sourcePath, safetyPath);
            return new DatabaseRestoreResult
            {
                database = spec.key,
                restoredFrom = sourcePath.Replace('\\', '/'),
                safetyBackup = safetyPath
            };
        }
        finally
        {
            semaphore.Release();
        }
    }

    static async Task ValidateBackupAsync(DatabaseSpec spec, string sourcePath)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync();
            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync()), "ok", StringComparison.OrdinalIgnoreCase))
                throw new DatabaseEditorValidationException("backup_integrity_failed");

            await using var schema = connection.CreateCommand();
            schema.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @table LIMIT 1;";
            schema.Parameters.AddWithValue("@table", spec.table);
            if (await schema.ExecuteScalarAsync() == null)
                throw new DatabaseEditorValidationException("backup_schema_mismatch");
        }
        catch (SqliteException)
        {
            throw new DatabaseEditorValidationException("invalid_backup_database");
        }
    }

    static async Task<string> CreateBackupLockedAsync(DatabaseSpec spec, string marker)
    {
        string directory = Path.Combine("database", "backup", "database-editor");
        Directory.CreateDirectory(directory);
        string baseName = Path.GetFileNameWithoutExtension(spec.path);
        string destinationPath = Path.Combine(directory, $"{baseName}-{marker}-{DateTime.Now:yyyyMMdd-HHmmssfff}.sql");
        await using var source = await OpenAsync(spec);
        var builder = new SqliteConnectionStringBuilder { DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
        await using var destination = new SqliteConnection(builder.ToString());
        await destination.OpenAsync();
        source.BackupDatabase(destination);
        return destinationPath.Replace('\\', '/');
    }

    static async Task EnrichTimeCodeRecordsAsync(List<DatabaseRecord> records)
    {
        if (!File.Exists(Sync.path))
            return;

        var users = new HashSet<string>(StringComparer.Ordinal);
        foreach (DatabaseRecord record in records)
        {
            if (!string.IsNullOrEmpty(record.user))
                users.Add(record.user);
            if (TryParseTimeCodeCard(record.card, out _, out string mediaType))
                record.mediaType = mediaType;
        }

        if (users.Count == 0)
            return;

        var metadata = new Dictionary<string, MediaMetadata>(StringComparer.Ordinal);
        await using var connection = await OpenAsync(Sync);
        await using var command = connection.CreateCommand();
        var placeholders = new List<string>(users.Count);
        int parameterIndex = 0;
        foreach (string user in users)
        {
            string parameter = "@user" + parameterIndex++;
            placeholders.Add(parameter);
            command.Parameters.AddWithValue(parameter, user);
        }
        command.CommandText = $"SELECT user, data FROM bookmarks WHERE user IN ({string.Join(",", placeholders)});";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string user = ReadString(reader, 0);
            JsonObject root = ParseRoot(ReadString(reader, 1));
            if (root?["card"] is not JsonArray cards)
                continue;

            foreach (JsonNode node in cards)
            {
                if (node is not JsonObject card)
                    continue;
                string cardId = NodeText(card["id"]);
                if (string.IsNullOrEmpty(cardId))
                    continue;
                metadata[MediaKey(user, cardId)] = ReadMediaMetadata(card);
            }
        }

        foreach (DatabaseRecord record in records)
        {
            if (!TryParseTimeCodeCard(record.card, out string cardId, out string mediaType))
                continue;

            record.mediaType = mediaType;
            if (!metadata.TryGetValue(MediaKey(record.user, cardId), out MediaMetadata media))
                continue;

            record.title = media.title;
            record.poster = media.poster;
            record.year = media.year;
            if (string.IsNullOrEmpty(record.mediaType))
                record.mediaType = media.mediaType;
            if (string.Equals(record.mediaType, "tv", StringComparison.OrdinalIgnoreCase) &&
                TryFindEpisode(media, record.item, out int season, out int episode))
            {
                record.season = season;
                record.episode = episode;
            }
        }
    }

    static SyncUserDetails BuildSyncUserDetails(long id, string user, string updated, string data)
    {
        JsonObject root = ParseRoot(data);
        var categoryMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var categoryOrderMap = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var allCategoryIds = new List<string>();

        foreach (string category in SyncCategories)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            if (root?[category] is JsonArray array)
            {
                foreach (JsonNode node in array)
                {
                    string cardId = NodeText(node);
                    if (!string.IsNullOrEmpty(cardId) && ids.Add(cardId))
                    {
                        positions[cardId] = positions.Count;
                        allCategoryIds.Add(cardId);
                    }
                }
            }
            categoryMap[category] = ids;
            categoryOrderMap[category] = positions;
        }

        var items = new List<SyncUserItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root?["card"] is JsonArray cards)
        {
            foreach (JsonNode node in cards)
            {
                if (node is not JsonObject card)
                    continue;
                string cardId = NodeText(card["id"]);
                if (string.IsNullOrEmpty(cardId) || !seen.Add(cardId))
                    continue;
                MediaMetadata media = ReadMediaMetadata(card);
                items.Add(new SyncUserItem
                {
                    cardId = cardId,
                    title = media.title,
                    poster = media.poster,
                    year = media.year,
                    mediaType = media.mediaType,
                    categories = CategoriesFor(cardId, categoryMap),
                    order = items.Count,
                    categoryOrder = CategoryOrderFor(cardId, categoryOrderMap)
                });
            }
        }

        foreach (string cardId in allCategoryIds)
        {
            if (!seen.Add(cardId))
                continue;
            items.Add(new SyncUserItem
            {
                cardId = cardId,
                title = "Карточка #" + cardId,
                categories = CategoriesFor(cardId, categoryMap),
                order = items.Count,
                categoryOrder = CategoryOrderFor(cardId, categoryOrderMap)
            });
        }

        return new SyncUserDetails
        {
            id = id,
            user = user,
            updated = updated,
            total = items.Count,
            items = items
        };
    }

    static async Task UpdateSyncJsonAsync(long recordId, Action<JsonObject> update)
    {
        var semaphore = new SemaphorManager(Sync.semaphore, TimeSpan.FromSeconds(20));
        bool acquired = await semaphore.WaitAsync();
        if (!acquired)
            throw new DatabaseEditorBusyException("database_busy");

        try
        {
            await using var connection = await OpenAsync(Sync);
            using var transaction = connection.BeginTransaction();
            string data;
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = "SELECT data FROM bookmarks WHERE Id = @id LIMIT 1;";
                select.Parameters.AddWithValue("@id", recordId);
                data = Convert.ToString(await select.ExecuteScalarAsync());
            }
            if (string.IsNullOrEmpty(data))
                throw new DatabaseEditorValidationException("record_not_found");

            JsonObject root = ParseRoot(data) ?? throw new DatabaseEditorValidationException("invalid_json");
            update(root);
            string updatedData = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            if (Encoding.UTF8.GetByteCount(updatedData) > MaxDataBytes)
                throw new DatabaseEditorValidationException("data_too_large");

            await using var save = connection.CreateCommand();
            save.Transaction = transaction;
            save.CommandText = "UPDATE bookmarks SET data = @data, updated = @updated WHERE Id = @id;";
            save.Parameters.AddWithValue("@data", updatedData);
            save.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
            save.Parameters.AddWithValue("@id", recordId);
            if (await save.ExecuteNonQueryAsync() == 0)
                throw new DatabaseEditorValidationException("record_not_found");
            transaction.Commit();
        }
        finally
        {
            semaphore.Release();
        }
    }

    static MediaMetadata ReadMediaMetadata(JsonObject card)
    {
        string mediaType = NodeText(card?["media_type"]);
        if (string.IsNullOrEmpty(mediaType))
            mediaType = card?["name"] != null && card?["title"] == null ? "tv" : "movie";

        string date = FirstNotEmpty(NodeText(card?["release_date"]), NodeText(card?["first_air_date"]));
        return new MediaMetadata
        {
            title = FirstNotEmpty(NodeText(card?["title"]), NodeText(card?["name"]), NodeText(card?["original_title"]), NodeText(card?["original_name"])),
            poster = FirstNotEmpty(NodeText(card?["img"]), NodeText(card?["poster_path"])),
            year = !string.IsNullOrEmpty(date) && date.Length >= 4 ? date.Substring(0, 4) : date,
            mediaType = mediaType,
            hashTitles = DistinctValues(
                NodeText(card?["original_name"]),
                NodeText(card?["original_title"]),
                NodeText(card?["name"]),
                NodeText(card?["title"])),
            seasons = ReadPositiveInt(card?["number_of_seasons"]),
            episodes = ReadPositiveInt(card?["number_of_episodes"])
        };
    }

    static bool TryFindEpisode(MediaMetadata media, string item, out int season, out int episode)
    {
        season = 0;
        episode = 0;
        if (media?.hashTitles == null || media.hashTitles.Count == 0 ||
            !long.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out long target) ||
            target < 0 || target > 2147483648L)
            return false;

        int maxSeasons = media.seasons > 0 ? Math.Min(media.seasons, 100) : 50;
        int maxEpisodes = media.episodes > 0 ? Math.Min(media.episodes, 3000) : 500;
        maxSeasons = Math.Max(maxSeasons, 10);
        maxEpisodes = Math.Max(maxEpisodes, 100);

        if (FindEpisode(media.hashTitles, target, maxSeasons, maxEpisodes, out season, out episode))
            return true;

        // Long-running daily shows and anime can exceed the compact first pass.
        if (maxSeasons < 100 || maxEpisodes < 3000)
            return FindEpisode(media.hashTitles, target, 100, 3000, out season, out episode);

        return false;
    }

    static bool FindEpisode(List<string> titles, long target, int maxSeasons, int maxEpisodes, out int season, out int episode)
    {
        var titleHashes = new List<(int hash, int power)>(titles.Count);
        foreach (string title in titles)
        {
            int power = 1;
            for (int index = 0; index < title.Length; index++)
                power = unchecked(power * 31);
            titleHashes.Add((JsHash(title), power));
        }

        for (int seasonNumber = 0; seasonNumber <= maxSeasons; seasonNumber++)
        {
            string separator = seasonNumber > 10 ? ":" : string.Empty;
            string seasonPrefix = seasonNumber.ToString(CultureInfo.InvariantCulture) + separator;
            for (int episodeNumber = 0; episodeNumber <= maxEpisodes; episodeNumber++)
            {
                int prefixHash = JsHash(seasonPrefix + episodeNumber.ToString(CultureInfo.InvariantCulture));
                foreach ((int titleHash, int power) in titleHashes)
                {
                    int hash = unchecked(prefixHash * power + titleHash);
                    if (Math.Abs((long)hash) != target)
                        continue;
                    season = seasonNumber;
                    episode = episodeNumber;
                    return true;
                }
            }
        }

        season = 0;
        episode = 0;
        return false;
    }

    static int JsHash(string value)
    {
        int hash = 0;
        foreach (char character in value ?? string.Empty)
            hash = unchecked(hash * 31 + character);
        return hash;
    }

    static List<string> DistinctValues(params string[] values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                result.Add(value);
        }
        return result;
    }

    static int ReadPositiveInt(JsonNode node)
    {
        return int.TryParse(NodeText(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : 0;
    }

    static List<string> CategoriesFor(string cardId, Dictionary<string, HashSet<string>> categoryMap)
    {
        var result = new List<string>();
        foreach (string category in SyncEditorCategories)
        {
            if (categoryMap[category].Contains(cardId))
                result.Add(category);
        }
        return result;
    }

    static Dictionary<string, int> CategoryOrderFor(string cardId, Dictionary<string, Dictionary<string, int>> categoryOrderMap)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string category in SyncEditorCategories)
        {
            if (categoryOrderMap[category].TryGetValue(cardId, out int position))
                result[category] = position;
        }
        return result;
    }

    static JsonObject FindSyncCard(JsonObject root, string cardId)
    {
        if (root?["card"] is not JsonArray cards)
            return null;
        foreach (JsonNode node in cards)
        {
            if (node is JsonObject card && string.Equals(NodeText(card["id"]), cardId, StringComparison.Ordinal))
                return card;
        }
        return null;
    }

    static JsonArray EnsureArray(JsonObject root, string name)
    {
        if (root[name] is JsonArray array)
            return array;
        array = new JsonArray();
        root[name] = array;
        return array;
    }

    static bool RemoveCardId(JsonArray array, string cardId)
    {
        bool removed = false;
        for (int index = array.Count - 1; index >= 0; index--)
        {
            if (string.Equals(NodeText(array[index]), cardId, StringComparison.Ordinal))
            {
                array.RemoveAt(index);
                removed = true;
            }
        }
        return removed;
    }

    static bool ContainsCardId(JsonArray array, string cardId)
    {
        foreach (JsonNode node in array)
        {
            if (string.Equals(NodeText(node), cardId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    static JsonNode CreateCardIdNode(string cardId)
    {
        if (long.TryParse(cardId, out long numericId))
            return JsonValue.Create(numericId);
        return JsonValue.Create(cardId);
    }

    static JsonObject ParseRoot(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;
        try
        {
            return JsonNode.Parse(data) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string NodeText(JsonNode node)
    {
        if (node == null)
            return null;
        if (node is JsonValue value && value.TryGetValue<string>(out string text))
            return text;
        return node.ToJsonString().Trim('"');
    }

    static string FirstNotEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    static string MediaKey(string user, string cardId) => (user ?? string.Empty) + "\u001f" + cardId;

    static bool TryParseTimeCodeCard(string value, out string cardId, out string mediaType)
    {
        cardId = null;
        mediaType = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        int separator = value.LastIndexOf('_');
        if (separator <= 0 || separator == value.Length - 1)
            return false;
        string suffix = value.Substring(separator + 1);
        if (!string.Equals(suffix, "movie", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(suffix, "tv", StringComparison.OrdinalIgnoreCase))
            return false;
        cardId = value.Substring(0, separator);
        mediaType = suffix.ToLowerInvariant();
        return true;
    }

    static void ReadPlaybackData(DatabaseRecord record, string data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return;
        try
        {
            using var document = JsonDocument.Parse(data);
            JsonElement root = document.RootElement;
            record.position = ReadDouble(root, "time");
            record.duration = ReadDouble(root, "duration");
            record.percent = ReadDouble(root, "percent");
        }
        catch (JsonException) { }
    }

    static double? ReadDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
            return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out number))
            return number;
        return null;
    }

    static async Task<DatabaseSummary> ReadSummaryAsync(DatabaseSpec spec)
    {
        var summary = new DatabaseSummary
        {
            database = spec.key,
            title = spec.title,
            file = spec.path.Replace('\\', '/'),
            available = File.Exists(spec.path),
            bytes = File.Exists(spec.path) ? new FileInfo(spec.path).Length : 0
        };

        if (!summary.available)
            return summary;

        await using var connection = await OpenAsync(spec);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*), MAX(updated) FROM {spec.table};";
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
        if (await reader.ReadAsync())
        {
            summary.records = reader.GetInt64(0);
            summary.updated = ReadString(reader, 1);
        }

        return summary;
    }

    static DatabaseSpec GetSpec(string database)
    {
        if (string.Equals(database, Sync.key, StringComparison.OrdinalIgnoreCase))
            return Sync;
        if (string.Equals(database, TimeCode.key, StringComparison.OrdinalIgnoreCase))
            return TimeCode;
        throw new DatabaseEditorValidationException("unknown_database");
    }

    static async Task<SqliteConnection> OpenAsync(DatabaseSpec spec, bool pooling = true)
    {
        if (!File.Exists(spec.path))
            throw new DatabaseEditorValidationException("database_not_found");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = spec.path,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 10,
            Pooling = pooling
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();
        return connection;
    }

    static string BuildWhere(DatabaseSpec spec, string query, string user, out string searchValue, out string selectedUser)
    {
        var clauses = new List<string>();
        if (string.IsNullOrEmpty(query))
        {
            searchValue = null;
        }
        else
        {
            searchValue = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            clauses.Add(spec.timecode
                ? "(CAST(Id AS TEXT) LIKE @search ESCAPE '\\' OR user LIKE @search ESCAPE '\\' COLLATE NOCASE OR card LIKE @search ESCAPE '\\' COLLATE NOCASE OR item LIKE @search ESCAPE '\\' COLLATE NOCASE OR data LIKE @search ESCAPE '\\' COLLATE NOCASE)"
                : "(CAST(Id AS TEXT) LIKE @search ESCAPE '\\' OR user LIKE @search ESCAPE '\\' COLLATE NOCASE OR data LIKE @search ESCAPE '\\' COLLATE NOCASE)");
        }

        selectedUser = string.IsNullOrEmpty(user) ? null : user;
        if (selectedUser != null)
            clauses.Add("user = @selectedUser COLLATE NOCASE");

        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    static void AddFilters(SqliteCommand command, string searchValue, string selectedUser)
    {
        if (searchValue != null)
            command.Parameters.AddWithValue("@search", searchValue);
        if (selectedUser != null)
            command.Parameters.AddWithValue("@selectedUser", selectedUser);
    }

    static string ValidateKey(string value, string error)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new DatabaseEditorValidationException(error);
        if (value.Length > MaxKeyLength)
            throw new DatabaseEditorValidationException("key_too_long");
        return value;
    }

    static string NormalizeJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DatabaseEditorValidationException("data_required");
        if (Encoding.UTF8.GetByteCount(value) > MaxDataBytes)
            throw new DatabaseEditorValidationException("data_too_large");

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new DatabaseEditorValidationException("data_must_be_json_object");
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            throw new DatabaseEditorValidationException("invalid_json");
        }
    }

    static string BuildPreview(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        string preview = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (preview.Contains("  ", StringComparison.Ordinal))
            preview = preview.Replace("  ", " ", StringComparison.Ordinal);
        return preview.Length <= 240 ? preview : preview.Substring(0, 240) + "…";
    }

    static string ReadString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
