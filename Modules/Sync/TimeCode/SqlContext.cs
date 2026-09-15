using Microsoft.Data.Sqlite;
using Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;

namespace TimeCode;

public class SqlContext : DbContext
{
    public static readonly string semaphoreKey = "TimeCode";

    /// <summary>Пишущие запросы одного пользователя сериализуются между собой, но не со всей базой.</summary>
    public static string SemaphoreKeyFor(string userId) => $"{semaphoreKey}:{userId}";

    public static IDbContextFactory<SqlContext> Factory { get; private set; }

    public static SqlContext Create()
    {
        if (Factory != null)
            return Factory.CreateDbContext();

        return new SqlContext();
    }

    public static void Initialization(IServiceProvider applicationServices)
    {
        Directory.CreateDirectory("database");

        Factory = applicationServices.GetService<IDbContextFactory<SqlContext>>();

        using (var sqlDb = new SqlContext())
        {
            sqlDb.Database.EnsureCreated();
            MigrateLegacyBlob(sqlDb);
            MigrateProfileAreas(sqlDb);
        }
    }

    static readonly string _connection = new SqliteConnectionStringBuilder
    {
        DataSource = "database/TimeCode.sql",
        Cache = SqliteCacheMode.Shared,
        DefaultTimeout = 10,
        Pooling = true
    }.ToString();

    public DbSet<SqlModel> timecodes { get; set; }

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
        // Веб-Lampa адресует запись только хешем, поэтому старый уникальный ключ остаётся.
        modelBuilder.Entity<SqlModel>()
                    .HasIndex(t => new { t.user, t.card, t.item })
                    .IsUnique();

        // Нативные клиенты адресуют запись TMDB-идентичностью. Частичный индекс — потому что
        // у записей, пришедших из веба, identity вывести не из чего (хеш не несёт сезон/серию).
        modelBuilder.Entity<SqlModel>()
                    .HasIndex(t => new { t.user, t.identity })
                    .IsUnique()
                    .HasFilter("identity IS NOT NULL");

        // Курсор дельт: WHERE user = ? AND updated_at > ?
        modelBuilder.Entity<SqlModel>()
                    .HasIndex(t => new { t.user, t.updated_at });
    }

    #region MigrateProfileAreas
    /// <summary>
    /// Переименование областей профилей: <c>uid_profile</c> → <c>uid:profile</c>.
    ///
    /// Прежний разделитель был неоднозначен — подчёркивание разрешено и в самом идентификаторе
    /// пользователя, поэтому одно имя означало две разные области. Разбираем старое имя по списку
    /// известных пользователей, и при совпадении побеждает самый длинный: это и есть тот, чьи
    /// данные на самом деле. Неразобранное не трогаем — лучше оставить как есть, чем увести
    /// чужое.
    /// </summary>
    static void MigrateProfileAreas(SqlContext db)
    {
        var known = DataArea.KnownUsers();
        if (known.Count == 0)
            return;

        var existing = new HashSet<string>(StringComparer.Ordinal);
        var legacy = new List<string>();

        using (var connection = new SqliteConnection(_connection))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT user FROM timecodes;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                    continue;

                string user = reader.GetString(0);
                existing.Add(user);
                if (user.Contains('_'))
                    legacy.Add(user);
            }
        }

        int renamed = 0;

        foreach (string user in legacy)
        {
            if (!DataArea.TrySplitLegacy(user, known, out string target))
                continue;

            // Область с новым именем уже есть — сливать две истории молча нельзя.
            if (existing.Contains(target))
            {
                Serilog.Log.Warning(
                    "{Module} data area {Legacy} not renamed: {Target} already exists", "TimeCode", user, target);
                continue;
            }

            db.Database.ExecuteSqlRaw("UPDATE timecodes SET user = {0} WHERE user = {1};", target, user);
            existing.Add(target);
            renamed++;
        }

        if (renamed > 0)
            Serilog.Log.Information("{Module} renamed {Count} profile data area(s)", "TimeCode", renamed);
    }
    #endregion

    #region MigrateLegacyBlob
    /// <summary>
    /// Старая схема хранила весь road-объект Lampa строкой в колонке <c>data</c>, а колонку
    /// <c>updated</c> писала и никогда не читала. Разбираем блоб в типизированные колонки один раз.
    /// Пересоздание таблицы, а не восемь ALTER: меняется форма уникальных индексов, и EF не умеет
    /// вставлять строки в таблицу, где остался <c>updated NOT NULL</c> без значения по умолчанию.
    /// </summary>
    static void MigrateLegacyBlob(SqlContext db)
    {
        if (!HasColumn("timecodes", "data"))
            return;

        // Одной транзакцией: на середине замены падение оставило бы базу без исходной таблицы.
        // DDL в SQLite транзакционен, поэтому откатывается и создание таблиц.
        using var transaction = db.Database.BeginTransaction();

        // Форма должна совпадать с SqlModel — EF создаёт её сам только на чистой установке.
        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS timecodes_migrated;");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE timecodes_migrated (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                user TEXT NOT NULL,
                identity TEXT NULL,
                card TEXT NOT NULL,
                item TEXT NULL,
                position REAL NOT NULL DEFAULT 0,
                duration REAL NOT NULL DEFAULT 0,
                percent REAL NOT NULL DEFAULT 0,
                profile INTEGER NOT NULL DEFAULT 0,
                deleted INTEGER NOT NULL DEFAULT 0,
                watched_at INTEGER NOT NULL DEFAULT 0,
                updated_at INTEGER NOT NULL DEFAULT 0,
                extra TEXT NULL
            );
            """);

        // json_extract вместо разбора в C#: JSON1 в Microsoft.Data.Sqlite включён, а строк может
        // быть много. `extra` собирает ключи road, которых мы не знаем, чтобы не потерять их.
        db.Database.ExecuteSqlRaw("""
            INSERT INTO timecodes_migrated
                (user, identity, card, item, position, duration, percent, profile, deleted, watched_at, updated_at, extra)
            SELECT
                user,
                NULL,
                card,
                item,
                COALESCE(json_extract(data, '$.time'), 0),
                COALESCE(json_extract(data, '$.duration'), 0),
                COALESCE(json_extract(data, '$.percent'), 0),
                COALESCE(json_extract(data, '$.profile'), 0),
                0,
                COALESCE(json_extract(data, '$.updated'), 0),
                CAST((julianday(updated) - 2440587.5) * 86400000 AS INTEGER),
                NULLIF(json_remove(data, '$.time', '$.duration', '$.percent', '$.profile', '$.updated', '$.hash'), {0})
            FROM timecodes
            WHERE json_valid(data);
            """, "{}");

        db.Database.ExecuteSqlRaw("DROP TABLE timecodes;");
        db.Database.ExecuteSqlRaw("ALTER TABLE timecodes_migrated RENAME TO timecodes;");

        BackfillMovieIdentity(db);

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_timecodes_user_card_item ON timecodes (user, card, item);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_timecodes_user_identity ON timecodes (user, identity) WHERE identity IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_timecodes_user_updated_at ON timecodes (user, updated_at);
            """);

        transaction.Commit();

        Serilog.Log.Information("{Module} legacy blob migrated to typed columns", "TimeCode");
    }

    /// <summary>
    /// У фильмов идентичность лежала в базе всё это время: `card` — это `{tmdb}_movie`, то есть
    /// `674_movie` и есть `movie-674`. Вписываем её сразу, без патчей клиента.
    ///
    /// Сериалы так не восстановить: `1851_tv` называет шоу, а какая это серия — знает только хеш,
    /// и он необратим. `tv-1851` им присвоить нельзя — это идентичность шоу целиком, не серии.
    /// </summary>
    static void BackfillMovieIdentity(SqlContext db)
    {
        const string movieCard = "card LIKE '%\\_movie' ESCAPE '\\'";

        // Один фильм мог накопить две строки с разными хешами (сменилось original_title), а
        // identity у них выйдет одна. Поэтому её получает только свежайшая строка карточки —
        // остальные остаются по хешу, иначе уникальный индекс ниже упадёт.
        int filled = db.Database.ExecuteSqlRaw(
            "UPDATE timecodes SET identity = 'movie-' || substr(card, 1, length(card) - 6) " +
            $"WHERE {movieCard} " +
            "AND CAST(substr(card, 1, length(card) - 6) AS INTEGER) > 0 " +
            "AND Id IN (SELECT Id FROM (" +
                "SELECT Id, ROW_NUMBER() OVER (PARTITION BY user, card " +
                    "ORDER BY watched_at DESC, updated_at DESC, Id DESC) AS rn " +
                $"FROM timecodes WHERE {movieCard}" +
            ") WHERE rn = 1);");

        Serilog.Log.Information("{Module} identity restored for {Count} movie row(s)", "TimeCode", filled);
    }

    static bool HasColumn(string tableName, string columnName)
    {
        using var connection = new SqliteConnection(_connection);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
    #endregion
}

[Table("timecodes")]
public class SqlModel
{
    [Key]
    public long Id { get; set; }

    [Required]
    public string user { get; set; }

    /// <summary>TMDB-идентичность: <c>movie-674</c> / <c>tv-1851-s1e2</c>. NULL у записей из веб-Lampa.</summary>
    public string identity { get; set; }

    /// <summary>Карточка целиком: <c>1851_tv</c>. Выводится из identity, когда клиент её не прислал.</summary>
    [Required]
    public string card { get; set; }

    /// <summary>Хеш Lampa. NULL, если нативный клиент его не прислал.</summary>
    public string item { get; set; }

    public double position { get; set; }

    public double duration { get; set; }

    public double percent { get; set; }

    /// <summary>road.profile Lampa — профиль аккаунта, внутри которого сделана отметка.</summary>
    public long profile { get; set; }

    /// <summary>Снятая отметка. Строка остаётся: без неё следующий dump вернёт запись назад.</summary>
    public bool deleted { get; set; }

    /// <summary>Когда смотрели. Мс, штампует клиент. Этим арбитрируются конфликты устройств.</summary>
    public long watched_at { get; set; }

    /// <summary>Когда сервер принял строку. Мс, монотонно растёт внутри пользователя. Курсор дельт.</summary>
    public long updated_at { get; set; }

    /// <summary>Ключи road, которых нет в колонках, — чтобы обновление Lampa не теряло данные.</summary>
    public string extra { get; set; }
}
