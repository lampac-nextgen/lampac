using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Telemetry.Models;

namespace Telemetry;

public class AppDbContext : DbContext
{
    public DbSet<LogModelSql> Logs { get; set; } = null!;
    public DbSet<UserInfoModelSql> Users { get; set; } = null!;

    public AppDbContext() : base() { }
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dbPath = ModInit.DbPath;
            var dbDir = Path.GetDirectoryName(dbPath);
            if (dbDir != null && !Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            optionsBuilder.UseSqlite($"Data Source={dbPath};Cache=Shared");
            optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LogModelSql>()
            .HasIndex(l => l.Time);

        modelBuilder.Entity<LogModelSql>()
            .HasIndex(l => new { l.Uid, l.Time });

        modelBuilder.Entity<LogModelSql>()
            .HasIndex(l => new { l.Balancer, l.Time });

        modelBuilder.Entity<LogModelSql>()
            .HasIndex(l => l.UnfoId);

        modelBuilder.Entity<UserInfoModelSql>()
            .HasKey(u => u.Id);

        modelBuilder.Entity<UserInfoModelSql>()
            .HasIndex(u => u.Ip);
    }
}
