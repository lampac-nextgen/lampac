using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading;

namespace Music;

public class MusicContext : DbContext
{
    public static readonly string semaphoreKey = "Music";
    static readonly string DatabaseDirectoryPath = Path.Combine("database", "music");
    static readonly string DatabaseFilePath = Path.Combine(DatabaseDirectoryPath, "Music.sql");
    static readonly string BackupDirectoryPath = Path.Combine(DatabaseDirectoryPath, "backups");
    static readonly TimeSpan BackupInterval = TimeSpan.FromHours(6);
    static readonly TimeSpan MinBackupSpacing = TimeSpan.FromHours(1);
    const int MaxBackupFiles = 5;
    static readonly SemaphoreSlim backupLock = new(1, 1);
    static Timer backupTimer;

    public static IDbContextFactory<MusicContext> Factory { get; private set; }

    public static MusicContext Create()
    {
        if (Factory != null)
            return Factory.CreateDbContext();

        return new MusicContext();
    }

    public static void Initialization(IServiceProvider applicationServices)
    {
        Directory.CreateDirectory(DatabaseDirectoryPath);
        Directory.CreateDirectory(BackupDirectoryPath);
        ValidateOrRecoverDatabase();

        Factory = applicationServices.GetService<IDbContextFactory<MusicContext>>();

        bool schemaChanged;
        using (var sqlDb = Factory?.CreateDbContext() ?? new MusicContext())
        {
            ApplyConnectionPragmas(sqlDb.Database.GetDbConnection());
            sqlDb.Database.EnsureCreated();
            schemaChanged = EnsureSchema(sqlDb);
        }

        if (schemaChanged)
            CompactDatabase();

        CreateBackupSnapshotIfNeeded(force: schemaChanged);
        TrimOldBackups();
        StartBackupTimer();
    }

    static readonly string _connection = new SqliteConnectionStringBuilder
    {
        DataSource = DatabaseFilePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        DefaultTimeout = 30,
        Pooling = false
    }.ToString();

    public static string ConnectionString => _connection;

    public DbSet<MusicAuthCredentialSqlModel> auth_credentials { get; set; }
    public DbSet<MusicPlaybackHistorySqlModel> playback_history { get; set; }

    public static void ConfiguringDbBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(_connection);
            optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ConfiguringDbBuilder(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MusicAuthCredentialSqlModel>()
            .HasIndex(t => new { t.profile_id, t.provider_id })
            .IsUnique();

        modelBuilder.Entity<MusicPlaybackHistorySqlModel>()
            .HasIndex(t => new { t.profile_id, t.track_id })
            .IsUnique();
    }

    static bool EnsureSchema(MusicContext db)
    {
        bool changed = false;

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS auth_credentials (
                Id INTEGER NOT NULL CONSTRAINT PK_auth_credentials PRIMARY KEY AUTOINCREMENT,
                profile_id TEXT NOT NULL DEFAULT '',
                provider_id TEXT NOT NULL,
                payload TEXT NOT NULL,
                updated TEXT NOT NULL
            );
            """);

        changed |= EnsureColumn(db, "auth_credentials", "profile_id", "TEXT NOT NULL DEFAULT ''");

        db.Database.ExecuteSqlRaw("""
            DROP INDEX IF EXISTS IX_auth_credentials_provider_id;
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_auth_credentials_profile_id_provider_id
            ON auth_credentials (profile_id, provider_id);
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS playback_history (
                Id INTEGER NOT NULL CONSTRAINT PK_playback_history PRIMARY KEY AUTOINCREMENT,
                profile_id TEXT NOT NULL DEFAULT '',
                track_id TEXT NOT NULL,
                payload TEXT NOT NULL,
                updated TEXT NOT NULL
            );
            """);

        changed |= EnsureColumn(db, "playback_history", "profile_id", "TEXT NOT NULL DEFAULT ''");

        db.Database.ExecuteSqlRaw("""
            DROP INDEX IF EXISTS IX_playback_history_track_id;
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_playback_history_profile_id_track_id
            ON playback_history (profile_id, track_id);
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS IX_playback_history_profile_id_updated
            ON playback_history (profile_id, updated DESC);
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS audio_source_matches (
                Id INTEGER NOT NULL CONSTRAINT PK_audio_source_matches PRIMARY KEY AUTOINCREMENT,
                track_id TEXT NOT NULL,
                provider_scope TEXT NOT NULL,
                payload TEXT NOT NULL,
                updated TEXT NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_audio_source_matches_track_id_provider_scope
            ON audio_source_matches (track_id, provider_scope);
            """);

        // в durable-таблице живёт только ручной выбор (pinned) — авто-матчи
        // хранятся в volatile cache; заодно чистит авто-записи, попавшие сюда
        // в промежуточной ревизии, когда SaveAsync писал в базу всё подряд
        db.Database.ExecuteSqlRaw("""
            DELETE FROM audio_source_matches
            WHERE payload NOT LIKE '%"pinned":true%';
            """);

        // дневная статистика прослушиваний: только счётчики, payload трека
        // живёт в playback_history (джойн по track_id при чтении); дневная
        // гранулярность нужна для окон «месяц/год/на повторе/забытые»
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS track_stats_daily (
                Id INTEGER NOT NULL CONSTRAINT PK_track_stats_daily PRIMARY KEY AUTOINCREMENT,
                profile_id TEXT NOT NULL DEFAULT '',
                track_id TEXT NOT NULL,
                day TEXT NOT NULL,
                play_count INTEGER NOT NULL DEFAULT 0,
                total_ms INTEGER NOT NULL DEFAULT 0,
                last_played TEXT NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_track_stats_daily_profile_track_day
            ON track_stats_daily (profile_id, track_id, day);
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS IX_track_stats_daily_profile_day
            ON track_stats_daily (profile_id, day);
            """);

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS user_playlists (
                Id INTEGER NOT NULL CONSTRAINT PK_user_playlists PRIMARY KEY AUTOINCREMENT,
                profile_id TEXT NOT NULL DEFAULT '',
                playlist_id TEXT NOT NULL,
                title TEXT NOT NULL,
                payload TEXT NOT NULL,
                source TEXT NOT NULL DEFAULT '',
                updated TEXT NOT NULL
            );
            """);

        changed |= EnsureColumn(db, "user_playlists", "source", "TEXT NOT NULL DEFAULT ''");

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_user_playlists_profile_id_playlist_id
            ON user_playlists (profile_id, playlist_id);
            """);

        changed |= DropLegacyCacheTables(db);
        return changed;
    }

    static bool EnsureColumn(MusicContext db, string tableName, string columnName, string definition)
    {
        using var connection = new SqliteConnection(_connection);
        connection.Open();

        using var exists = connection.CreateCommand();
        exists.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = exists.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
        return true;
    }

    static bool DropLegacyCacheTables(MusicContext db)
    {
        bool changed = TableExists("metadata_cache") || TableExists("source_matches");

        db.Database.ExecuteSqlRaw("""
            DROP INDEX IF EXISTS IX_metadata_cache_provider_id_entity_type_cache_key;
            DROP INDEX IF EXISTS IX_source_matches_track_id_provider_id;
            DROP TABLE IF EXISTS metadata_cache;
            DROP TABLE IF EXISTS source_matches;
            """);

        return changed;
    }

    static bool TableExists(string tableName)
    {
        using var connection = new SqliteConnection(_connection);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = $table_name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$table_name", tableName);

        return command.ExecuteScalar() != null;
    }

    static void ApplyConnectionPragmas(System.Data.Common.DbConnection dbConnection)
    {
        if (dbConnection is not SqliteConnection connection)
            return;

        bool shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA busy_timeout = 10000;
                PRAGMA synchronous = FULL;
                """;
            command.ExecuteNonQuery();
        }
        finally
        {
            if (shouldClose)
                connection.Close();
        }
    }

    static void StartBackupTimer()
    {
        backupTimer ??= new Timer(_ =>
        {
            try
            {
                CreateBackupSnapshotIfNeeded(force: false);
            }
            catch
            {
            }
        }, null, BackupInterval, BackupInterval);
    }

    static void CompactDatabase()
    {
        if (!File.Exists(DatabaseFilePath))
            return;

        using var connection = new SqliteConnection(_connection);
        connection.Open();
        ApplyConnectionPragmas(connection);

        using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        checkpoint.ExecuteNonQuery();

        using var vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        vacuum.ExecuteNonQuery();
    }

    static void ValidateOrRecoverDatabase()
    {
        if (!File.Exists(DatabaseFilePath))
            return;

        if (IsDatabaseValid(DatabaseFilePath))
            return;

        string corruptPath = Path.Combine(DatabaseDirectoryPath, $"Music.sql.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

        try
        {
            if (File.Exists(corruptPath))
                File.Delete(corruptPath);

            File.Move(DatabaseFilePath, corruptPath);
        }
        catch
        {
            try
            {
                File.Copy(DatabaseFilePath, corruptPath, overwrite: true);
                File.Delete(DatabaseFilePath);
            }
            catch
            {
            }
        }

        // сайдкары битой базы нельзя оставлять рядом с восстановленным файлом:
        // SQLite попытается проиграть чужой WAL и испортит свежую копию
        QuarantineSidecarFiles(corruptPath);

        RestoreLatestValidBackup();
    }

    static void QuarantineSidecarFiles(string corruptPath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            string sidecar = DatabaseFilePath + suffix;

            try
            {
                if (!File.Exists(sidecar))
                    continue;

                string target = corruptPath + suffix;
                if (File.Exists(target))
                    File.Delete(target);

                File.Move(sidecar, target);
            }
            catch
            {
                try
                {
                    File.Delete(sidecar);
                }
                catch
                {
                }
            }
        }
    }

    static bool RestoreLatestValidBackup()
    {
        if (!Directory.Exists(BackupDirectoryPath))
            return false;

        foreach (var backupFile in Directory.GetFiles(BackupDirectoryPath, "Music-*.sqlite")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (!IsDatabaseValid(backupFile))
                continue;

            TryDeleteSidecarFiles(DatabaseFilePath);
            File.Copy(backupFile, DatabaseFilePath, overwrite: true);
            return true;
        }

        return false;
    }

    static bool IsDatabaseValid(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var info = new FileInfo(path);
            if (info.Length < 100)
                return false;

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Span<byte> header = stackalloc byte[16];
                if (stream.Read(header) != header.Length)
                    return false;

                if (!header.SequenceEqual(Encoding.ASCII.GetBytes("SQLite format 3\0")))
                    return false;
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5
            };

            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = command.ExecuteScalar()?.ToString();

            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static void CreateBackupSnapshotIfNeeded(bool force)
    {
        if (!backupLock.Wait(0))
            return;

        try
        {
            Directory.CreateDirectory(BackupDirectoryPath);

            if (!force)
            {
                var latest = Directory.GetFiles(BackupDirectoryPath, "Music-*.sqlite")
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest != null && DateTime.UtcNow - latest.LastWriteTimeUtc < MinBackupSpacing)
                    return;

                if (latest != null && File.GetLastWriteTimeUtc(DatabaseFilePath) <= latest.LastWriteTimeUtc)
                    return;
            }

            if (!IsDatabaseValid(DatabaseFilePath))
                return;

            string tempPath = Path.Combine(BackupDirectoryPath, $"Music-{DateTime.UtcNow:yyyyMMdd-HHmmss}.tmp");
            string finalPath = Path.ChangeExtension(tempPath, ".sqlite");

            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                using var source = new SqliteConnection(_connection);
                source.Open();
                ApplyConnectionPragmas(source);

                var backupBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = tempPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                };

                using var destination = new SqliteConnection(backupBuilder.ToString());
                destination.Open();
                source.BackupDatabase(destination);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                throw;
            }

            if (!IsDatabaseValid(tempPath))
            {
                File.Delete(tempPath);
                TryDeleteSidecarFiles(tempPath);
                return;
            }

            if (File.Exists(finalPath))
                File.Delete(finalPath);

            File.Move(tempPath, finalPath);
            TryDeleteSidecarFiles(tempPath);
            TrimOldBackups();
        }
        finally
        {
            backupLock.Release();
        }
    }

    static void TrimOldBackups()
    {
        var backupFiles = Directory.GetFiles(BackupDirectoryPath, "Music-*.sqlite")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var file in backupFiles)
            TryDeleteSidecarFiles(file.FullName);

        foreach (var file in backupFiles.Skip(MaxBackupFiles))
        {
            try
            {
                file.Delete();
            }
            catch
            {
            }
        }
    }

    static void TryDeleteSidecarFiles(string basePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            try
            {
                string sidecar = basePath + suffix;
                if (File.Exists(sidecar))
                    File.Delete(sidecar);
            }
            catch
            {
            }
        }
    }
}

public class MusicAuthCredentialSqlModel
{
    [Key]
    public long Id { get; set; }

    [Required]
    public string profile_id { get; set; }

    [Required]
    public string provider_id { get; set; }

    [Required]
    public string payload { get; set; }

    public DateTime updated { get; set; }
}

public class MusicPlaybackHistorySqlModel
{
    [Key]
    public long Id { get; set; }

    [Required]
    public string profile_id { get; set; }

    [Required]
    public string track_id { get; set; }

    [Required]
    public string payload { get; set; }

    public DateTime updated { get; set; }
}
