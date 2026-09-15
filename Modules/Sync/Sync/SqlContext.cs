using Microsoft.Data.Sqlite;
using Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;

namespace Sync;

public class SqlContext : DbContext
{
    public static readonly string semaphoreKey = "Sync";

    /// <summary>Курсор монотонен внутри пользователя, поэтому и замок берётся на пользователя.</summary>
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
        DataSource = "database/Sync.sql",
        Cache = SqliteCacheMode.Shared,
        DefaultTimeout = 10,
        Pooling = true
    }.ToString();

    public static void ConfiguringDbBuilder(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite(_connection);
            optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }
    }

    public DbSet<SyncUserBookmarkSqlModel> bookmarks { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ConfiguringDbBuilder(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyncUserBookmarkSqlModel>()
                    .HasIndex(t => new { t.user, t.card_id })
                    .IsUnique();

        // Курсор дельт: WHERE user = ? AND updated_at > ?
        modelBuilder.Entity<SyncUserBookmarkSqlModel>()
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
            command.CommandText = "SELECT DISTINCT user FROM bookmarks;";

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
                    "{Module} data area {Legacy} not renamed: {Target} already exists", "Sync", user, target);
                continue;
            }

            db.Database.ExecuteSqlRaw("UPDATE bookmarks SET user = {0} WHERE user = {1};", target, user);
            existing.Add(target);
            renamed++;
        }

        if (renamed > 0)
            Serilog.Log.Information("{Module} renamed {Count} profile data area(s)", "Sync", renamed);
    }
    #endregion

    #region MigrateLegacyBlob
    /// <summary>
    /// Старая схема держала все закладки пользователя одной строкой JSON: список карточек плюс
    /// по массиву идентификаторов на категорию. Ни отметок времени по карточкам, ни надгробий,
    /// ни курсора — поэтому удаление было неотличимо от «ещё не доехало», а сверка тянула всё
    /// целиком. Раскладываем блоб по строкам: одна строка на карточку.
    ///
    /// Порядок внутри категории значим (Lampa вставляет новое в начало), а времени добавления в
    /// блобе нет. Восстанавливаем его из позиции в массиве: чем ближе к началу, тем больше метка.
    /// </summary>
    static void MigrateLegacyBlob(SqlContext db)
    {
        if (!HasColumn("bookmarks", "data"))
            return;

        var legacy = ReadLegacyRows();

        // Одной транзакцией: на середине замены падение оставило бы базу без исходной таблицы.
        // DDL в SQLite транзакционен, поэтому откатывается и создание таблиц.
        using var transaction = db.Database.BeginTransaction();

        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS bookmarks_migrated;");

        // Форма должна совпадать с SyncUserBookmarkSqlModel — EF создаёт её сам только на чистой установке.
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE bookmarks_migrated (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                user TEXT NOT NULL,
                card_id TEXT NOT NULL,
                card TEXT NULL,
                categories TEXT NOT NULL,
                changed_at INTEGER NOT NULL DEFAULT 0,
                updated_at INTEGER NOT NULL DEFAULT 0
            );
            """);

        int cards = 0;

        foreach (var row in legacy)
        {
            foreach (var card in ExplodeBlob(row.data, row.updatedAt))
            {
                db.Database.ExecuteSqlRaw(
                    "INSERT INTO bookmarks_migrated (user, card_id, card, categories, changed_at, updated_at) VALUES ({0}, {1}, {2}, {3}, {4}, {4});",
                    row.user, card.cardId, card.card, card.categories, row.updatedAt);

                cards++;
            }
        }

        db.Database.ExecuteSqlRaw("DROP TABLE bookmarks;");
        db.Database.ExecuteSqlRaw("ALTER TABLE bookmarks_migrated RENAME TO bookmarks;");

        db.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_bookmarks_user_card_id ON bookmarks (user, card_id);
            CREATE INDEX IF NOT EXISTS IX_bookmarks_user_updated_at ON bookmarks (user, updated_at);
            """);

        transaction.Commit();

        Serilog.Log.Information(
            "{Module} legacy blob migrated: {Users} user(s), {Cards} card(s)", "Sync", legacy.Count, cards);
    }

    /// <summary>Категории Lampa. <c>watch</c> — опечатка, живущая рядом с <c>wath</c> с самого начала.</summary>
    static readonly string[] categories =
        ["history", "like", "watch", "wath", "book", "look", "viewed", "scheduled", "continued", "thrown"];

    /// <summary>
    /// Блоб → строки. Карточка, не попавшая ни в одну категорию, всё равно сохраняется: пустой
    /// объект категорий и есть надгробие, и по нему клиент понимает, что карточку убрали отовсюду.
    /// </summary>
    static IEnumerable<(string cardId, string card, string categories)> ExplodeBlob(string data, long updatedAt)
    {
        JObject root;
        try { root = JObject.Parse(data); }
        catch { yield break; }

        var membership = new Dictionary<string, JObject>(StringComparer.Ordinal);

        foreach (string category in categories)
        {
            if (root[category] is not JArray list)
                continue;

            for (int i = 0; i < list.Count; i++)
            {
                string id = list[i]?.ToString();
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!membership.TryGetValue(id, out var owned))
                    membership[id] = owned = new JObject();

                // Начало массива — самое свежее, поэтому метка убывает вместе с позицией.
                owned[category] = updatedAt - i;
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (root["card"] is JArray cards)
        {
            foreach (var card in cards.Children<JObject>())
            {
                string id = card["id"]?.ToString();
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    continue;

                var owned = membership.TryGetValue(id, out var found) ? found : new JObject();
                yield return (id, card.ToString(Newtonsoft.Json.Formatting.None), owned.ToString(Newtonsoft.Json.Formatting.None));
            }
        }

        // Идентификатор в категории без карточки: сама карточка потерялась, а членство осталось.
        // Держим его — иначе при сверке оно выглядит как удаление и уедет у всех клиентов.
        foreach (var pair in membership)
        {
            if (seen.Add(pair.Key))
                yield return (pair.Key, null, pair.Value.ToString(Newtonsoft.Json.Formatting.None));
        }
    }

    static List<(string user, string data, long updatedAt)> ReadLegacyRows()
    {
        var rows = new List<(string, string, long)>();

        using var connection = new SqliteConnection(_connection);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT user, data, CAST((julianday(updated) - 2440587.5) * 86400000 AS INTEGER) FROM bookmarks;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;

            long updated = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            rows.Add((reader.GetString(0), reader.GetString(1), updated > 0 ? updated : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        return rows;
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


[Table("bookmarks")]
public class SyncUserBookmarkSqlModel
{
    [Key]
    public long Id { get; set; }

    [Required]
    public string user { get; set; }

    /// <summary>Идентификатор карточки, как его знает Lampa: обычно TMDB id строкой.</summary>
    [Required]
    public string card_id { get; set; }

    /// <summary>Карточка целиком. Веб рисует список из неё, больше её взять неоткуда.</summary>
    public string card { get; set; }

    /// <summary>
    /// Членство и порядок разом: <c>{"history": 1789…, "like": 1789…}</c>. Пустой объект — карточку
    /// убрали отовсюду; строка остаётся, иначе следующая сверка вернёт её назад.
    /// </summary>
    [Required]
    public string categories { get; set; }

    /// <summary>Когда закладку изменили. Мс, штампует клиент. Этим арбитрируются конфликты устройств.</summary>
    public long changed_at { get; set; }

    /// <summary>Когда сервер принял строку. Мс, монотонно растёт внутри пользователя. Курсор дельт.</summary>
    public long updated_at { get; set; }
}
