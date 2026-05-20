using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace LogUserRequest;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public AppDbContext() : base() { }

    public DbSet<LogModelSql> jurnal { get; set; }
    public DbSet<UserInfoModelSql> unfo { get; set; }
    public DbSet<HeaderModelSql> headers { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, "database", "LogUserRequest", "userlog.db");
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir!);
            optionsBuilder.UseSqlite($"Data Source={dbPath};Cache=Shared");
            optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogModelSql>().HasIndex(x => x.uid);
        modelBuilder.Entity<LogModelSql>().HasIndex(x => x.time);
        modelBuilder.Entity<LogModelSql>().HasIndex(x => x.balancer);
        modelBuilder.Entity<LogModelSql>().HasIndex(x => new { x.uid, x.time });
    }
}

public class LogModelSql
{
    public int Id { get; set; }
    public DateTime time { get; set; }
    public string uri { get; set; } = "";
    public string uid { get; set; } = "";
    public string unfo { get; set; } = "";
    public string header { get; set; } = "";
    public int duration_ms { get; set; }
    public string? balancer { get; set; }
    public int? status_code { get; set; }
}

public class UserInfoModelSql
{
    [Key] public string Id { get; set; } = "";
    public string IP { get; set; } = "";
    public string Country { get; set; } = "";
    public string UserAgent { get; set; } = "";
}

public class HeaderModelSql
{
    [Key] public string Id { get; set; } = "";
    public string HeadersJson { get; set; } = "";
    
    [NotMapped]
    public Dictionary<string, string> Headers
    {
        get
        {
            if (string.IsNullOrEmpty(HeadersJson)) return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(HeadersJson) ?? new(); }
            catch { return new(); }
        }
        set
        {
            var safe = (value ?? new Dictionary<string, string>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Take(50)
                .ToDictionary(
                    x => x.Key.Length > 100 ? x.Key[..100] : x.Key,
                    x => (x.Value ?? "").Length > 500 ? x.Value[..500] : (x.Value ?? "")
                );
            HeadersJson = JsonSerializer.Serialize(safe);
        }
    }
}
