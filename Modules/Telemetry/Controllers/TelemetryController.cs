using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Attributes;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telemetry.Models;
using System.Collections.Generic;

namespace Telemetry.Controllers;

[Authorization(redirectUri: "/adminpanel/auth")]
[Route("/telemetry")]
public class TelemetryController : BaseController
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TelemetryController(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    private class MovieRawDto
    {
        public string? Title { get; set; }
        public string? Tmdb { get; set; }
        public string? Kp { get; set; }
        public string? Imdb { get; set; }
        public bool IsTv { get; set; }
        public int Count { get; set; }
    }

    private static (DateTime start, DateTime end) GetPeriod(int days)
    {
        var now = DateTime.UtcNow.Date;
        var periodStart = days == 0 ? now : (days == -1 ? now.AddDays(-1) : now.AddDays(-days));
        var periodEnd = days == -1 ? now : DateTime.UtcNow;
        return (periodStart, periodEnd);
    }

    private static List<object> MapMovies(List<MovieRawDto> raw)
    {
        return raw.Select(m => new
        {
            title = m.Title,
            id = m.Tmdb ?? m.Kp ?? m.Imdb ?? m.Title,
            tmdb = m.Tmdb,
            kp = m.Kp,
            imdb = m.Imdb,
            count = m.Count,
            source = m.Tmdb != null ? "tmdb" : m.Kp != null ? "kinopoisk" : m.Imdb != null ? "imdb" : "unknown",
            isTv = m.IsTv
        }).Cast<object>().ToList();
    }

    private IQueryable<LogModelSql> ApplySearchFilter(AppDbContext db, IQueryable<LogModelSql> query, string? search)
    {
        if (string.IsNullOrEmpty(search)) return query;

        var rawSearch = search;
        var encodedSearch = Uri.EscapeDataString(search);
        var encodedSearchPlus = encodedSearch.Replace("%20", "+");

        return query.Where(l =>
            l.Uid.Contains(rawSearch) ||
            l.Uri.Contains(rawSearch) ||
            l.Uri.Contains(encodedSearch) ||
            l.Uri.Contains(encodedSearchPlus) ||
            (l.Balancer != null && l.Balancer.Contains(rawSearch)) ||
            db.Users.Any(u => u.Id == l.UnfoId && (u.Ip.Contains(rawSearch) || u.UserAgent.Contains(rawSearch)))
        );
    }

    [HttpGet]
    [Route("")]
    public ActionResult Index()
    {
        var path = Path.Combine(ModInit.modpath, "index.html");
        if (!System.IO.File.Exists(path)) return Content("UI missing", "text/plain");
        var html = System.IO.File.ReadAllText(path, Encoding.UTF8);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("api/stats")]
    public async Task<IActionResult> Stats(string? search, int days = 30)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var query = db.Logs.AsNoTracking();
        query = ApplySearchFilter(db, query, search);

        var (periodStart, periodEnd) = GetPeriod(days);
        var now = DateTime.UtcNow.Date;

        int total;
        int today;
        if (days == 0)
        {
            today = await query.Where(l => l.Time >= now.Date && l.Time < periodEnd).CountAsync();
            total = today;
        }
        else if (days == -1)
        {
            total = await query.Where(l => l.Time >= periodStart && l.Time < periodEnd).CountAsync();
            today = 0;
        }
        else
        {
            total = await query.Where(l => l.Time >= periodStart && l.Time < periodEnd).CountAsync();
            today = await query.Where(l => l.Time >= now.Date && l.Time < periodEnd).CountAsync();
        }

        var uids = await query.Where(l => l.Time >= periodStart && l.Time < periodEnd && l.Uid != "anonymous" && !string.IsNullOrEmpty(l.Uid)).Select(l => l.Uid).Distinct().CountAsync();

        var validLogs = query.Where(l => l.Time >= periodStart && l.Time < periodEnd && l.UnfoId != null);
        var ips = await validLogs.Join(db.Users, l => l.UnfoId, u => u.Id, (l, u) => u.Ip).Where(ip => ip != null && ip != "").Distinct().CountAsync();
        var uas = await validLogs.Join(db.Users, l => l.UnfoId, u => u.Id, (l, u) => u.UserAgent).Where(ua => ua != null && ua != "").Distinct().CountAsync();

        return new JsonResult(new { success = true, data = new { today, total, uids, ips, uas } });
    }

    [HttpGet("api/logs")]
    public async Task<IActionResult> Logs(string? search, int days = 30, int skip = 0, int take = 100)
    {
        take = Math.Min(take, 1000);
        await using var db = await _contextFactory.CreateDbContextAsync();

        var query = db.Logs.AsNoTracking();
        var (periodStart, periodEnd) = GetPeriod(days);

        query = query.Where(l => l.Time >= periodStart && l.Time < periodEnd);
        query = ApplySearchFilter(db, query, search);

        var logs = await query.OrderByDescending(l => l.Time).Skip(skip).Take(take)
            .GroupJoin(db.Users, l => l.UnfoId, u => u.Id, (l, users) => new { Log = l, Users = users })
            .SelectMany(x => x.Users.DefaultIfEmpty(), (x, u) => new
            {
                id = x.Log.Id,
                time = x.Log.Time,
                uri = x.Log.Uri,
                uid = x.Log.Uid,
                ip = u != null ? u.Ip : "unknown",
                userAgent = u != null ? u.UserAgent : "unknown",
                balancer = x.Log.Balancer,
                statusCode = x.Log.StatusCode,
                duration = x.Log.DurationMs
            })
            .ToListAsync();

        return new JsonResult(new { success = true, data = logs });
    }

    [HttpGet("api/chart")]
    public async Task<IActionResult> Chart(string? search, int days = 30)
    {
        var (periodStart, periodEnd) = GetPeriod(days);

        await using var db = await _contextFactory.CreateDbContextAsync();
        var query = db.Logs.AsNoTracking();
        query = ApplySearchFilter(db, query, search);

        var data = await query
            .Where(l => l.Time >= periodStart && l.Time < periodEnd)
            .GroupBy(l => l.Time.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(x => x.date)
            .ToListAsync();

        return new JsonResult(new { success = true, data = data });
    }

    [HttpGet("api/tops")]
    public async Task<IActionResult> GetTops(string? search, [FromQuery] int days = 7)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var (periodStart, periodEnd) = GetPeriod(days);

        var query = db.Logs.AsNoTracking();
        query = ApplySearchFilter(db, query, search);
        var baseQuery = query.Where(l => l.Time >= periodStart && l.Time < periodEnd);

        var topBalancers = await baseQuery
            .Where(l => l.Balancer != null && l.Balancer != "" && !l.Balancer.StartsWith("18+ "))
            .GroupBy(l => l.Balancer)
            .Select(g => new { balancer = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(10)
            .ToListAsync();

        var topAdult = await baseQuery
            .Where(l => l.Balancer != null && l.Balancer.StartsWith("18+ "))
            .GroupBy(l => l.Balancer)
            .Select(g => new { balancer = g.Key!.Substring(4), count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(10)
            .ToListAsync();

        var topUsers = await baseQuery
            .Where(l => l.Uid != "anonymous" && l.Uid != null && l.Uid != "")
            .GroupBy(l => l.Uid)
            .Select(g => new { uid = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(10)
            .ToListAsync();

        var topMoviesRaw = await baseQuery
            .Where(l => l.MovieTitle != null && l.MovieTitle != "")
            .GroupBy(l => l.MovieTitle)
            .Select(g => new MovieRawDto
            {
                Title = g.Key,
                Tmdb = g.Max(l => l.TmdbId),
                Kp = g.Max(l => l.KpId),
                Imdb = g.Max(l => l.ImdbId),
                IsTv = g.Max(l => l.IsTv ? 1 : 0) == 1,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var topMovies = MapMovies(topMoviesRaw);

        var userAgents = await baseQuery
            .Where(l => l.UnfoId != null)
            .Join(db.Users, l => l.UnfoId, u => u.Id, (l, u) => u.UserAgent)
            .GroupBy(ua => ua)
            .Select(g => new { ua = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(1000)
            .ToListAsync();

        var devices = userAgents
            .Select(x => new { name = ParseDevice(x.ua), count = x.count })
            .GroupBy(x => x.name)
            .Select(g => new { name = g.Key, count = g.Sum(x => x.count) })
            .OrderByDescending(x => x.count)
            .ToList();

        var hours = await baseQuery
            .GroupBy(l => l.Time.Hour)
            .Select(g => new { hour = g.Key, count = g.Count() })
            .OrderBy(x => x.hour)
            .ToListAsync();

        var seriesCount = await baseQuery.Where(l => l.IsTv).CountAsync();
        var moviesCount = await baseQuery.Where(l => l.MovieTitle != null && l.MovieTitle != "" && !l.IsTv).CountAsync();
        var animeCount = await baseQuery.Where(l => l.Uri.Contains("anime=1") || l.Uri.Contains("anime=true")).CountAsync();

        var contentTypes = new[] {
            new { name = "Фильмы", count = moviesCount },
            new { name = "Сериалы", count = seriesCount },
            new { name = "Аниме", count = animeCount }
        }.Where(x => x.count > 0).ToList();

        return new JsonResult(new { success = true, data = new { balancers = topBalancers, adult = topAdult, users = topUsers, movies = topMovies, devices, hours, contentTypes } });
    }

    private string ParseDevice(string? ua)
    {
        if (ua == null) return "Другие";
        ua = ua.ToLower();
        if (ua.Contains("tizen") || ua.Contains("samsung")) return "Tizen";
        if (ua.Contains("web0s") || ua.Contains("webos")) return "WebOS";
        if (ua.Contains("android")) return "Android";
        if (ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("mac os")) return "Apple";
        if (ua.Contains("windows")) return "Windows";
        if (ua.Contains("linux")) return "Linux";
        return "Другие";
    }

    [HttpGet("api/user")]
    public async Task<IActionResult> UserInfo(string uid, int days = 30)
    {
        if (string.IsNullOrEmpty(uid)) return BadRequest();

        await using var db = await _contextFactory.CreateDbContextAsync();
        var (periodStart, periodEnd) = GetPeriod(days);

        var baseQuery = db.Logs.AsNoTracking().Where(l => l.Uid == uid && l.Time >= periodStart && l.Time < periodEnd);
        var count = await baseQuery.CountAsync();

        if (count == 0) return new JsonResult(new { success = false });

        var totalRequests = count;
        var firstSeen = await baseQuery.MinAsync(l => l.Time);
        var lastSeen = await baseQuery.MaxAsync(l => l.Time);

        var ips = await baseQuery.Where(l => l.UnfoId != null).Join(db.Users, l => l.UnfoId, u => u.Id, (l, u) => u.Ip).Where(ip => ip != null && ip != "").Distinct().ToListAsync();
        var userAgents = await baseQuery.Where(l => l.UnfoId != null).Join(db.Users, l => l.UnfoId, u => u.Id, (l, u) => u.UserAgent).Where(ua => ua != null && ua != "").Distinct().ToListAsync();

        var balancers = await baseQuery.Where(l => l.Balancer != null && l.Balancer != "")
            .GroupBy(l => l.Balancer)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        var isHourly = days <= 1;
        var timeline = await (isHourly
            ? baseQuery.GroupBy(l => new DateTime(l.Time.Year, l.Time.Month, l.Time.Day, l.Time.Hour, 0, 0)).Select(g => new { date = g.Key, count = g.Count() }).OrderBy(x => x.date).ToListAsync()
            : baseQuery.GroupBy(l => l.Time.Date).Select(g => new { date = g.Key, count = g.Count() }).OrderBy(x => x.date).ToListAsync());

        var moviesRaw = await baseQuery
            .Where(l => l.MovieTitle != null && l.MovieTitle != "")
            .GroupBy(l => l.MovieTitle)
            .Select(g => new MovieRawDto
            {
                Title = g.Key,
                Tmdb = g.Max(l => l.TmdbId),
                Kp = g.Max(l => l.KpId),
                Imdb = g.Max(l => l.ImdbId),
                IsTv = g.Max(l => l.IsTv ? 1 : 0) == 1,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var movies = MapMovies(moviesRaw);

        return new JsonResult(new
        {
            success = true,
            data = new
            {
                totalRequests,
                firstSeen,
                lastSeen,
                ips,
                userAgents,
                balancers,
                timeline,
                movies
            }
        });
    }

    [HttpGet("api/ip")]
    public async Task<IActionResult> IpInfo(string ip, int days = 30)
    {
        if (string.IsNullOrEmpty(ip)) return BadRequest();

        await using var db = await _contextFactory.CreateDbContextAsync();
        var (periodStart, periodEnd) = GetPeriod(days);

        var baseQuery = db.Logs.AsNoTracking().Where(l => l.Time >= periodStart && l.Time < periodEnd && l.UnfoId != null && db.Users.Any(u => u.Id == l.UnfoId && u.Ip == ip));
        var count = await baseQuery.CountAsync();

        if (count == 0) return new JsonResult(new { success = false });

        var totalRequests = count;
        var firstSeen = await baseQuery.MinAsync(l => l.Time);
        var lastSeen = await baseQuery.MaxAsync(l => l.Time);

        var uids = await baseQuery.Where(l => l.Uid != "anonymous" && l.Uid != null && l.Uid != "").Select(l => l.Uid).Distinct().ToListAsync();
        var userAgents = await baseQuery.Join(db.Users, l => l.UnfoId, u => u.Id, (l, u) => u.UserAgent).Where(ua => ua != null && ua != "").Distinct().ToListAsync();

        var balancers = await baseQuery.Where(l => l.Balancer != null && l.Balancer != "")
            .GroupBy(l => l.Balancer)
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        var isHourly = days <= 1;
        var timeline = await (isHourly
            ? baseQuery.GroupBy(l => new DateTime(l.Time.Year, l.Time.Month, l.Time.Day, l.Time.Hour, 0, 0)).Select(g => new { date = g.Key, count = g.Count() }).OrderBy(x => x.date).ToListAsync()
            : baseQuery.GroupBy(l => l.Time.Date).Select(g => new { date = g.Key, count = g.Count() }).OrderBy(x => x.date).ToListAsync());

        var moviesRaw = await baseQuery
            .Where(l => l.MovieTitle != null && l.MovieTitle != "")
            .GroupBy(l => l.MovieTitle)
            .Select(g => new MovieRawDto
            {
                Title = g.Key,
                Tmdb = g.Max(l => l.TmdbId),
                Kp = g.Max(l => l.KpId),
                Imdb = g.Max(l => l.ImdbId),
                IsTv = g.Max(l => l.IsTv ? 1 : 0) == 1,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var movies = MapMovies(moviesRaw);

        return new JsonResult(new
        {
            success = true,
            data = new
            {
                totalRequests,
                firstSeen,
                lastSeen,
                uids,
                userAgents,
                balancers,
                timeline,
                movies
            }
        });
    }

    [HttpGet("api/settings")]
    public IActionResult GetSettings()
    {
        return new JsonResult(new
        {
            success = true,
            data = new
            {
                prefixes = string.Join("\n", RequestListener.SkipPrefixes),
                extensions = string.Join("\n", RequestListener.SkipExtensions),
                regexes = string.Join("\n", RequestListener.RegexFilters),
                ips = string.Join("\n", RequestListener.BlacklistIps),
                rateLimit = RequestListener.RateLimitMs
            }
        });
    }

    [HttpPost("api/settings")]
    public IActionResult UpdateSettings([FromForm] string? prefixes, [FromForm] string? extensions, [FromForm] string? regexes, [FromForm] string? ips, [FromForm] int? rateLimit)
    {
        if (prefixes != null) RequestListener.SkipPrefixes = prefixes.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        if (extensions != null) RequestListener.SkipExtensions = extensions.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        if (regexes != null)
        {
            var patterns = regexes.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            var invalid = new List<string>();
            foreach (var p in patterns)
            {
                try { System.Text.RegularExpressions.Regex.Match("", p, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50)); }
                catch { invalid.Add(p); }
            }
            if (invalid.Count > 0)
                return new JsonResult(new { success = false, error = $"Невалидные regex: {string.Join(", ", invalid)}" });
            RequestListener.RegexFilters = patterns;
        }
        if (ips != null) RequestListener.BlacklistIps = ips.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        if (rateLimit.HasValue) RequestListener.RateLimitMs = rateLimit.Value;

        RequestListener.SaveSettings();

        return new JsonResult(new { success = true });
    }

    [HttpGet("api/balancer")]
    public async Task<IActionResult> BalancerInfo(string balancer, int days = 30)
    {
        if (string.IsNullOrEmpty(balancer)) return BadRequest();

        await using var db = await _contextFactory.CreateDbContextAsync();
        var (periodStart, periodEnd) = GetPeriod(days);
        var now = DateTime.UtcNow.Date;

        var baseQuery = db.Logs.AsNoTracking().Where(l => l.Balancer == balancer && l.Time >= periodStart && l.Time < periodEnd);

        var total = await baseQuery.CountAsync();
        var today = await baseQuery.Where(l => l.Time >= now.Date).CountAsync();

        var topUids = await baseQuery.Where(l => l.Uid != "anonymous" && l.Uid != null && l.Uid != "")
                          .GroupBy(l => l.Uid)
                          .Select(g => new { uid = g.Key, count = g.Count() })
                          .OrderByDescending(x => x.count).Take(10).ToListAsync();

        var topIps = await baseQuery.Where(l => l.UnfoId != null)
                         .Join(db.Users, l => l.UnfoId, u => u.Id, (l, u) => u.Ip)
                         .Where(ip => ip != null && ip != "")
                         .GroupBy(ip => ip)
                         .Select(g => new { ip = g.Key, count = g.Count() })
                         .OrderByDescending(x => x.count).Take(10).ToListAsync();

        var isHourly = days <= 1;
        var timeline = await (isHourly
            ? baseQuery.GroupBy(l => new DateTime(l.Time.Year, l.Time.Month, l.Time.Day, l.Time.Hour, 0, 0)).Select(g => new { date = g.Key, count = g.Count() }).OrderBy(x => x.date).ToListAsync()
            : baseQuery.GroupBy(l => l.Time.Date).Select(g => new { date = g.Key, count = g.Count() }).OrderBy(x => x.date).ToListAsync());

        var moviesRaw = await baseQuery
            .Where(l => l.MovieTitle != null && l.MovieTitle != "")
            .GroupBy(l => l.MovieTitle)
            .Select(g => new MovieRawDto
            {
                Title = g.Key,
                Tmdb = g.Max(l => l.TmdbId),
                Kp = g.Max(l => l.KpId),
                Imdb = g.Max(l => l.ImdbId),
                IsTv = g.Max(l => l.IsTv ? 1 : 0) == 1,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var movies = MapMovies(moviesRaw);

        return new JsonResult(new { success = true, data = new { total, today, uids = topUids, ips = topIps, timeline, movies } });
    }

    [HttpPost("api/logs/delete")]
    public async Task<IActionResult> DeleteLogs([FromForm] string? search, [FromForm] int days = 30)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow.Date;
        var periodStart = days == 0 ? now : (days == -1 ? now.AddDays(-1) : now.AddDays(-days));
        var periodEnd = days == -1 ? now : DateTime.UtcNow;

        var query = db.Logs.Where(l => l.Time >= periodStart && l.Time < periodEnd);
        query = ApplySearchFilter(db, query, search);

        var deleted = await query.ExecuteDeleteAsync();
        return new JsonResult(new { success = true, deleted });
    }

    [HttpGet("api/export")]
    public async Task<IActionResult> ExportData()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var result = await db.Logs.AsNoTracking()
            .OrderByDescending(l => l.Id)
            .Take(50000)
            .GroupJoin(db.Users, l => l.UnfoId, u => u.Id, (l, users) => new { Log = l, Users = users })
            .SelectMany(x => x.Users.DefaultIfEmpty(), (x, u) => new
            {
                time = x.Log.Time,
                uri = x.Log.Uri,
                uid = x.Log.Uid,
                ip = u != null ? u.Ip : "",
                userAgent = u != null ? u.UserAgent : "",
                balancer = x.Log.Balancer,
                statusCode = x.Log.StatusCode
            })
            .ToListAsync();

        return new JsonResult(result);
    }
}
