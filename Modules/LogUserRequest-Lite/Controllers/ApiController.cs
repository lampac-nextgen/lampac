using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LogUserRequest.Models.DTO;

namespace LogUserRequest.Controllers;

[ApiController]
[Route("lite/logrequest")]
public class ApiController : ControllerBase
{
    private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ApiController(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    private static string GetAttemptsKey(string ip) => $"LogUserRequest:auth:IP:{ip}";

    private bool IsAuthorized()
    {
        var sessionToken = Request.Cookies["loguser_session"];
        if (!string.IsNullOrEmpty(sessionToken) && ModInit.ValidateSessionToken(sessionToken))
            return true;
        return false;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (!IsAuthorized()) return Redirect("/lite/logrequest/auth");
        var htmlPath = Path.Combine(ModInit.init.path, "index.html");
        if (!System.IO.File.Exists(htmlPath)) return Content("UI missing", "text/plain");
        var html = System.IO.File.ReadAllText(htmlPath, Encoding.UTF8);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("auth")]
    public IActionResult Auth()
    {
        var sessionToken = Request.Cookies["loguser_session"];

        if (!string.IsNullOrEmpty(sessionToken) && ModInit.ValidateSessionToken(sessionToken))
            return Redirect("/lite/logrequest");

        var htmlPath = Path.Combine(ModInit.init.path, "auth.html");
        if (!System.IO.File.Exists(htmlPath)) return Content("Auth page missing", "text/plain");

        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        var html = System.IO.File.ReadAllText(htmlPath, Encoding.UTF8);
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromForm] string password)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cacheKey = GetAttemptsKey(ip);
        var attempts = _cache.Get<int?>(cacheKey) ?? 0;

        if (attempts > 0) await Task.Delay(attempts * 200);
        if (attempts >= 5) return Redirect("/lite/logrequest/auth?blocked=1");

        if (password == ModInit.conf.adminPassword)
        {
            _cache.Remove(cacheKey);
            var sessionToken = ModInit.CreateSessionToken();

            Response.Cookies.Append("loguser_session", sessionToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromDays(30),
                Path = "/lite/logrequest",
                IsEssential = true
            });

            var script = @"
                <!DOCTYPE html>
                <html><head><meta charset='utf-8'></head><body>
                <script>
                    if (window.history && window.history.replaceState) {
                        window.history.replaceState(null, '', '/lite/logrequest');
                    }
                    window.location.replace('/lite/logrequest');
                </script>
                </body></html>";
            return Content(script, "text/html; charset=utf-8");
        }

        attempts++;
        _cache.Set(cacheKey, attempts, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = DateTime.Today.AddDays(1),
            SlidingExpiration = TimeSpan.FromHours(1)
        });
        await Task.Delay(Random.Shared.Next(300, 800));
        return Redirect($"/lite/logrequest/auth?error=1&remaining={5 - attempts}");
    }

    [HttpGet("logout")]
    public IActionResult Logout()
    {
        var sessionToken = Request.Cookies["loguser_session"];
        if (!string.IsNullOrEmpty(sessionToken)) ModInit.RevokeSessionToken(sessionToken);
        Response.Cookies.Delete("loguser_session", new CookieOptions
{
    Path = "/lite/logrequest"
});
        return Redirect("/lite/logrequest/auth");
    }

    [HttpGet("api")]
    public async Task<IActionResult> Api(string? uid, int skip = 0, int take = 200)
    {
        if (!IsAuthorized()) return UnauthorizedResponse();
        take = Math.Min(take, 500);

        await using var dbContext = await _contextFactory.CreateDbContextAsync();
        var query = dbContext.jurnal.AsNoTracking();
        if (!string.IsNullOrEmpty(uid)) query = query.Where(j => j.uid == uid);

        var jurnal = await query.OrderByDescending(x => x.Id).Skip(skip).Take(take).ToListAsync();
        if (jurnal.Count == 0) return Ok(ApiResponse<List<JournalItemDto>>.Fail("Empty"));

        var unfoIds = jurnal.Select(j => j.unfo).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var unfoDict = await dbContext.unfo.Where(u => unfoIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);

        var result = jurnal.Select(j =>
        {
            unfoDict.TryGetValue(j.unfo ?? "", out var unfo);
            return new JournalItemDto
            {
                Id = j.Id, Time = j.time, Uri = j.uri, UserUid = j.uid,
                Ip = unfo?.IP ?? "unknown", Country = unfo?.Country ?? "",
                UserAgent = unfo?.UserAgent ?? "unknown",
                DurationMs = j.duration_ms, Balancer = j.balancer
            };
        }).ToList();

        return Ok(ApiResponse<List<JournalItemDto>>.Ok(result));
    }

    [HttpGet("premium-chart")]
    public async Task<IActionResult> PremiumChart(int days = 7)
    {
        if (!IsAuthorized()) return UnauthorizedResponse();

        days = Math.Min(days, 3);

        var startDate = DateTime.UtcNow.AddDays(-days);

        await using var dbContext = await _contextFactory.CreateDbContextAsync();
        var data = await dbContext.jurnal
            .Where(j => j.time >= startDate)
            .GroupBy(j => j.time.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(x => x.date)
            .ToListAsync();

        var result = new
        {
            data = data,
            message = "В Lite-версии доступно только 2 дня. Premium — до 90 дней.",
            premiumAvailable = true
        };

        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(string? uid)
    {
        if (!IsAuthorized()) return UnauthorizedResponse();

        await using var dbContext = await _contextFactory.CreateDbContextAsync();
        var query = dbContext.jurnal.AsNoTracking();
        if (!string.IsNullOrEmpty(uid)) query = query.Where(j => j.uid == uid);

        var userLogs = await query.OrderByDescending(j => j.time).Take(50000).ToListAsync();
        if (userLogs.Count == 0) return Ok(ApiResponse<StatsDto>.Fail("Empty"));

        var unfoIds = userLogs.Select(j => j.unfo).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        var unfoList = await dbContext.unfo.Where(u => unfoIds.Contains(u.Id)).ToListAsync();

        var uniqueIp = unfoList.Select(u => u.IP).Where(ip => !string.IsNullOrEmpty(ip)).Distinct().Count();
        var uniqueUserAgent = unfoList.Select(u => u.UserAgent).Where(ua => !string.IsNullOrEmpty(ua)).Distinct().Count();

        var now = DateTime.UtcNow;
        var today = userLogs.Count(l => l.time.Day == now.Day && l.time.Month == now.Month && l.time.Year == now.Year);
        var month = userLogs.Count(l => l.time.Month == now.Month && l.time.Year == now.Year);

        var topBalancers = userLogs
            .GroupBy(l => l.balancer)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(g => new TopItemDto { Name = g.Key ?? "", Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(50).ToArray();

        var topUsers = userLogs
            .GroupBy(j => j.uid)
            .Select(g => new TopItemDto { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(20).ToArray();

        return Ok(ApiResponse<StatsDto>.Ok(new StatsDto
        {
            Today = today, Month = month,
            UniqueIp = uniqueIp, UniqueUserAgent = uniqueUserAgent,
            TopUsers = topUsers, TopBalancers = topBalancers
        }));
    }

    private IActionResult UnauthorizedResponse() => StatusCode(401, ApiResponse<object>.Fail("Unauthorized"));
    private IActionResult Ok<T>(ApiResponse<T> response) => new JsonResult(response);
}
