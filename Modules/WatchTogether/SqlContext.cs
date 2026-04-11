using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;

namespace WatchTogether
{
    public class RoomModel
    {
        [Key]
        public string id { get; set; }

        public string title { get; set; }

        public int tmdb_id { get; set; }

        public string state { get; set; }

        public double position { get; set; }

        public int season { get; set; }

        public int episode { get; set; }

        public string source { get; set; }

        public string type { get; set; }

        public DateTime create_time { get; set; }

        public DateTime update_time { get; set; }
    }

    public class RoomMemberModel
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int id { get; set; }

        public string room_id { get; set; }

        public string uid { get; set; }

        public string username { get; set; }

        public string connection_id { get; set; }

        public DateTime last_seen { get; set; }
    }

    public class SqlContext : DbContext
    {
        public DbSet<RoomModel> Rooms { get; set; }
        public DbSet<RoomMemberModel> RoomMembers { get; set; }

        public static IDbContextFactory<SqlContext> Factory { get; private set; }

        static string _connection;

        public SqlContext() { }

        public SqlContext(DbContextOptions<SqlContext> options) : base(options) { }

        public static SqlContext Create()
        {
            if (Factory != null)
                return Factory.CreateDbContext();

            return new SqlContext();
        }

        public static void Initialization(IServiceProvider applicationServices)
        {
            string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "database");
            Directory.CreateDirectory(dbPath);

            _connection = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(dbPath, "watchtogether.sql"),
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 10,
                Pooling = true
            }.ToString();

            Factory = applicationServices.GetService<IDbContextFactory<SqlContext>>();

            using (var sqlDb = Create())
                sqlDb.Database.EnsureCreated();
        }

        public static void ConfiguringDbBuilder(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite(_connection);
            }
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            ConfiguringDbBuilder(optionsBuilder);
        }
    }
}