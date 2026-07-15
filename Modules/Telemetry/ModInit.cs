using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Telemetry.Models;

namespace Telemetry;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    public static string modpath { get; private set; } = string.Empty;
    public static readonly string DbPath = Path.Combine(AppContext.BaseDirectory, "database", "Telemetry", "stats.db");
    private static Timer? _updateDbTimer;
    private static Timer? _cleanupTimer;
    private static int _updatingDb = 0;

    public void Configure(ConfigureModel app)
    {
        app.services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlite($"Data Source={DbPath};Cache=Shared");
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
    }

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;

        var dbDir = Path.GetDirectoryName(DbPath);
        if (dbDir != null) Directory.CreateDirectory(dbDir);

        using (var sqlDb = new AppDbContext())
        {
            sqlDb.Database.EnsureCreated();
            try
            {
                sqlDb.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
                sqlDb.Database.ExecuteSqlRaw("PRAGMA synchronous = NORMAL;");
                sqlDb.Database.ExecuteSqlRaw("PRAGMA cache_size = -64000;");
                sqlDb.Database.ExecuteSqlRaw("PRAGMA temp_store = MEMORY;");
                sqlDb.Database.ExecuteSqlRaw("PRAGMA mmap_size = 33554432;");
            }
            catch { }
        }

        _updateDbTimer?.Dispose();
        _cleanupTimer?.Dispose();

        _updateDbTimer = new Timer(UpdateDb, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        _cleanupTimer = new Timer(CleanupDb, null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(12));

        RequestListener.LoadSettings();

        EventListener.MiddlewareAsync -= RequestListener.InvokeAsync;
        EventListener.MiddlewareAsync += RequestListener.InvokeAsync;

        Console.WriteLine("[Telemetry] Module loaded.");
    }

    private static void UpdateDb(object? state)
    {
        if (Interlocked.Exchange(ref _updatingDb, 1) == 1) return;
        try
        {
            using var sqlDb = new AppDbContext();
            sqlDb.ChangeTracker.AutoDetectChangesEnabled = false;

            var batch = new List<(LogModelSql log, UserInfoModelSql user)>();
            while (batch.Count < 2000 && RequestListener.Queue.TryDequeue(out var item))
            {
                RequestListener.DequeueItem();
                batch.Add(item);
            }

            if (batch.Count == 0) return;

            var userIds = batch.Select(b => b.user.Id).Distinct().ToList();
            var existingUsers = sqlDb.Users.Where(u => userIds.Contains(u.Id)).Select(u => u.Id).ToHashSet();

            foreach (var item in batch.OrderBy(i => i.log.Time))
            {
                if (!existingUsers.Contains(item.user.Id))
                {
                    sqlDb.Users.Add(item.user);
                    existingUsers.Add(item.user.Id);
                }
                sqlDb.Logs.Add(item.log);
            }
            sqlDb.SaveChanges();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telemetry] DB Update Error: {ex.Message}");
        }
        finally { Interlocked.Exchange(ref _updatingDb, 0); }
    }

    private static void CleanupDb(object? state)
    {
        try
        {
            // Settings should be configurable, hardcoded for now: 90 days or max 500MB DB
            int logDay = 90;
            var cutoff = DateTime.UtcNow.AddDays(-logDay);

            using var sqlDb = new AppDbContext();
            int batchSize = 5000;

            // Delete old records
            while (true)
            {
                var ids = sqlDb.Logs.Where(l => l.Time < cutoff).Select(l => l.Id).Take(batchSize).ToList();
                if (ids.Count == 0) break;
                sqlDb.Logs.Where(l => ids.Contains(l.Id)).ExecuteDelete();
                if (ids.Count < batchSize) break;
            }

            // Delete orphaned users who have no logs left
            sqlDb.Users.Where(u => !sqlDb.Logs.Any(l => l.UnfoId == u.Id)).ExecuteDelete();

            // Run VACUUM to reclaim deleted disk space
            try { sqlDb.Database.ExecuteSqlRaw("VACUUM;"); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telemetry] DB Cleanup Error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        EventListener.MiddlewareAsync -= RequestListener.InvokeAsync;
        _updateDbTimer?.Dispose();
        _cleanupTimer?.Dispose();
        Console.WriteLine("[Telemetry] Module disposed.");
    }
}
