using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace LogUserRequest;

public class ModInit : IModuleLoaded, IModuleConfigure
{
    public static InitspaceModel init { get; set; } = null!;
    public static (int logDay, string adminPassword) conf = (90, "");
    public static object? stats = null;
    static Timer? _statsTimer, _clearJurnalTimer, _updateDbTimer;

    private static int _updatingStats = 0;
    private static int _updatingDb = 0;
    private static readonly MemoryCache _sessionTokens = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private static readonly TimeSpan _sessionLifetime = TimeSpan.FromDays(30);

    private static string _workPath = AppContext.BaseDirectory;
    private static string _dbDirectory = Path.Combine(AppContext.BaseDirectory, "database", "LogUserRequest");
    private static string _dbPath = Path.Combine(AppContext.BaseDirectory, "database", "LogUserRequest", "userlog.db");
    private static string _passwdPath = Path.Combine(AppContext.BaseDirectory, "database", "LogUserRequest", "passlogreg");

    // === IModuleConfigure ===
    public void Configure(ConfigureModel app)
    {
        app.services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlite($"Data Source={_dbPath};Cache=Shared");
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
    }

    public static bool ValidateSessionToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        return _sessionTokens.TryGetValue(token, out _);
    }

    public static string CreateSessionToken()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessionTokens.Set(token, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _sessionLifetime,
            SlidingExpiration = TimeSpan.FromDays(7),
            Size = 1
        });
        return token;
    }

    public static void RevokeSessionToken(string token) => _sessionTokens.Remove(token);

    private static string GenerateRandomPassword(int length = 36)
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetBytes(bytes);
        var base64 = Convert.ToBase64String(bytes);
        return base64.Replace('+', 'A').Replace('/', 'B')[..length];
    }

    private static string GetOrCreateAdminPassword()
    {
        var envPassword = Environment.GetEnvironmentVariable("LOGUSER_ADMIN_PASSWORD");
        if (!string.IsNullOrEmpty(envPassword))
            return envPassword;

        if (File.Exists(_passwdPath))
        {
            try
            {
                var filePassword = File.ReadAllText(_passwdPath).Trim();
                if (!string.IsNullOrEmpty(filePassword))
                    return filePassword;
            }
            catch { }
        }

        var oldPasswdPath = Path.Combine(_workPath, "passlogreg");
        if (File.Exists(oldPasswdPath))
        {
            try
            {
                var filePassword = File.ReadAllText(oldPasswdPath).Trim();
                if (!string.IsNullOrEmpty(filePassword))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_passwdPath)!);
                    File.Move(oldPasswdPath, _passwdPath);
                    return filePassword;
                }
            }
            catch { }
        }

        var newPassword = GenerateRandomPassword(36);
        try
        {
            var passwdDir = Path.GetDirectoryName(_passwdPath);
            if (!string.IsNullOrEmpty(passwdDir))
                Directory.CreateDirectory(passwdDir);

            File.WriteAllText(_passwdPath, newPassword);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            {
                try { File.SetUnixFileMode(_passwdPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            }
        }
        catch
        {
            Console.WriteLine("[LogUserRequest-Lite] Warning: Cannot save password to file. Using memory-only password.");
        }

        return newPassword;
    }

    public void Loaded(InitspaceModel initspace)
    {
        init = initspace;

        _workPath = AppContext.BaseDirectory;
        _dbDirectory = Path.Combine(_workPath, "database", "LogUserRequest");
        _dbPath = Path.Combine(_dbDirectory, "userlog.db");
        _passwdPath = Path.Combine(_dbDirectory, "passlogreg");

        Console.WriteLine($"[LogUserRequest-Lite] Work path: {_workPath}");
        Console.WriteLine($"[LogUserRequest-Lite] DB path: {_dbPath}");
        Console.WriteLine($"[LogUserRequest-Lite] Password path: {_passwdPath}");

        try
        {
            Directory.CreateDirectory(_dbDirectory);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[LogUserRequest-Lite] Warning: Cannot create directory {_dbDirectory}: {ex.Message}");
        }

        using (var sqlDb = new AppDbContext())
        {
            sqlDb.Database.EnsureCreated();
            try { sqlDb.Database.ExecuteSqlRaw("ALTER TABLE jurnal ADD COLUMN balancer TEXT;"); } catch { }
            try { sqlDb.Database.ExecuteSqlRaw("ALTER TABLE jurnal ADD COLUMN status_code INTEGER NULL;"); } catch { }
            try { sqlDb.Database.ExecuteSqlRaw("ALTER TABLE jurnal ADD COLUMN duration_ms INTEGER DEFAULT 0;"); } catch { }
            try { sqlDb.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_jurnal_uid ON jurnal(uid);"); } catch { }
            try { sqlDb.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_jurnal_time ON jurnal(time);"); } catch { }

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

        var manifestPath = Path.Combine(init.path, "manifest.json");
        JObject manifest = new();
        if (File.Exists(manifestPath))
        {
            manifest = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(manifestPath)) ?? new();
            conf.logDay = manifest["logDay"]?.Value<int>() ?? 90;
        }

        conf.adminPassword = GetOrCreateAdminPassword();

        _clearJurnalTimer = new Timer(ClearJurnal, null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24));
        _statsTimer = new Timer(UpdateStatsCallback, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
        _updateDbTimer = new Timer(UpdateDbCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        // === Подписка на EventListener вместо app.UseMiddleware ===
        EventListener.MiddlewareAsync -= LogUserRequestListener.InvokeAsync;
        EventListener.MiddlewareAsync += LogUserRequestListener.InvokeAsync;

        Console.WriteLine($"[LogUserRequest-Lite] Module loaded (logDay={conf.logDay})");
    }

    public void Dispose()
    {
        // === Отписка ===
        EventListener.MiddlewareAsync -= LogUserRequestListener.InvokeAsync;

        _clearJurnalTimer?.Dispose();
        _statsTimer?.Dispose();
        _updateDbTimer?.Dispose();
        _sessionTokens.Dispose();
    }

    // ... остальные методы (ClearJurnal, UpdateStats, UpdateDb) без изменений
    static void ClearJurnal(object? state)
    {
        try
        {
            using var sqlDb = new AppDbContext();
            var cutoff = DateTime.UtcNow.AddDays(-conf.logDay);

            const int batchSize = 5000;
            int totalDeleted = 0;

            while (true)
            {
                var idsToDelete = sqlDb.jurnal
                    .Where(j => j.time < cutoff)
                    .OrderBy(j => j.Id)
                    .Select(j => j.Id)
                    .Take(batchSize)
                    .ToList();

                if (idsToDelete.Count == 0) break;

                sqlDb.jurnal.Where(j => idsToDelete.Contains(j.Id)).ExecuteDelete();
                totalDeleted += idsToDelete.Count;

                if (idsToDelete.Count < batchSize) break;
            }

            if (totalDeleted > 0)
            {
                var usedUnfo = sqlDb.jurnal.Select(j => j.unfo).Distinct().Take(50000).ToHashSet();
                var usedHeaders = sqlDb.jurnal.Select(j => j.header).Distinct().Take(50000).ToHashSet();

                sqlDb.unfo.Where(u => !usedUnfo.Contains(u.Id)).Take(batchSize).ExecuteDelete();
                sqlDb.headers.Where(h => !usedHeaders.Contains(h.Id)).Take(batchSize).ExecuteDelete();

                Console.WriteLine($"[LogUserRequest-Lite] Cleaned {totalDeleted} old records");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LogUserRequest-Lite] ClearJurnal error: {ex.Message}");
        }
    }

    private static void UpdateStatsCallback(object? state) => UpdateStats();

    public static void UpdateStats()
    {
        if (Interlocked.Exchange(ref _updatingStats, 1) == 1) return;
        try
        {
            using var sqlDb = new AppDbContext();
            var now = DateTime.UtcNow;

            var monthStart = new DateTime(now.Year, now.Month, 1);
            var todayStart = new DateTime(now.Year, now.Month, now.Day);
            var tomorrowStart = todayStart.AddDays(1);

            var monthQuery = sqlDb.jurnal.Where(j => j.time >= monthStart);

            int today = monthQuery.Count(j => j.time >= todayStart && j.time < tomorrowStart);
            int month = monthQuery.Count();

            var unfoIds = monthQuery.Select(j => j.unfo).Distinct().Take(10000).ToList();

            if (unfoIds.Count > 0)
            {
                var unfoData = sqlDb.unfo
                    .Where(u => unfoIds.Contains(u.Id))
                    .Select(u => new { u.IP, u.UserAgent })
                    .Take(10000)
                    .ToList();

                int uniqueUserAgent = unfoData.Select(u => u.UserAgent).Distinct().Count();
                int uniqueIp = unfoData.Select(u => u.IP).Where(ip => !string.IsNullOrEmpty(ip)).Distinct().Count();

                var topUsers = monthQuery
                    .GroupBy(j => j.uid)
                    .Select(g => new { uid = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .Take(20)
                    .ToArray();

                var topBalancers = monthQuery
                    .Where(j => j.balancer != null)
                    .GroupBy(j => j.balancer)
                    .Select(g => new { balancer = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .Take(50)
                    .ToArray();

                stats = new { today, month, uniqueUserAgent, uniqueIp, topUsers, topBalancers };
            }
            else
            {
                stats = new
                {
                    today = 0,
                    month = 0,
                    uniqueUserAgent = 0,
                    uniqueIp = 0,
                    topUsers = Array.Empty<object>(),
                    topBalancers = Array.Empty<object>()
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LogUserRequest-Lite] UpdateStats error: {ex.Message}");
        }
        finally { Interlocked.Exchange(ref _updatingStats, 0); }
    }

    private static void UpdateDbCallback(object? state) => UpdateDb();

    static void UpdateDb()
    {
        if (Interlocked.Exchange(ref _updatingDb, 1) == 1) return;
        try
        {
            using var sqlDb = new AppDbContext();
            sqlDb.ChangeTracker.AutoDetectChangesEnabled = false;

            var batch = new List<(LogModelSql jurnal, UserInfoModelSql unfo, HeaderModelSql header)>();

            int batchSize = 1000;
            while (LogUserRequestListener.Queue.TryDequeue(out var item) && batch.Count < batchSize)
            {
                batch.Add(item);
                LogUserRequestListener.DequeueItem();
            }

            if (batch.Count == 0) return;

            var unfoIds = batch.Select(b => b.unfo.Id).Distinct().ToList();
            var headerIds = batch.Select(b => b.header.Id).Distinct().ToList();

            var existingUnfo = sqlDb.unfo
                .Where(u => unfoIds.Contains(u.Id))
                .Select(u => u.Id)
                .ToHashSet();

            var existingHeaders = sqlDb.headers
                .Where(h => headerIds.Contains(h.Id))
                .Select(h => h.Id)
                .ToHashSet();

            foreach (var item in batch.OrderBy(i => i.jurnal.time))
            {
                if (!existingUnfo.Contains(item.unfo.Id))
                {
                    sqlDb.unfo.Add(item.unfo);
                    existingUnfo.Add(item.unfo.Id);
                }
                if (!existingHeaders.Contains(item.header.Id))
                {
                    sqlDb.headers.Add(item.header);
                    existingHeaders.Add(item.header.Id);
                }
                sqlDb.jurnal.Add(new LogModelSql
                {
                    time = item.jurnal.time,
                    uri = item.jurnal.uri,
                    uid = item.jurnal.uid,
                    unfo = item.unfo.Id,
                    header = item.header.Id,
                    duration_ms = item.jurnal.duration_ms,
                    balancer = item.jurnal.balancer,
                    status_code = item.jurnal.status_code
                });
            }

            sqlDb.SaveChanges();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LogUserRequest-Lite] UpdateDb error: {ex.Message}");
        }
        finally { Interlocked.Exchange(ref _updatingDb, 0); }
    }
}
