using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using QRAuth.Services;

namespace QRAuth.Controllers
{
    /// <summary>
    /// HTTP surface for the DenyPage QR login flow. Deliberately a plain MVC
    /// controller (no Shared/BaseController dependency) so this repo keeps building
    /// standalone via QRAuth.csproj (see README "Локальная сборка").
    ///
    /// These routes must be reachable by a visitor who is NOT yet authorized — add
    /// them to accsdb.whitepattern in init.conf (e.g. "^/(adminpanel|tgbot/qr)"),
    /// same as any other public webhook/static path per this module's README.
    /// </summary>
    [ApiController]
    [Route("tgbot/qr")]
    public class QrAuthController : ControllerBase
    {
        [HttpGet("start")]
        public ActionResult Start()
        {
            if (!ModInit.conf.enable)
                return NotFound();

            return Ok(new { session = QrAuthSessions.Create() });
        }

        [HttpGet("status")]
        public ActionResult Status([FromQuery] string session)
        {
            if (string.IsNullOrWhiteSpace(session))
                return BadRequest(new { error = "session is required" });

            var (status, token) = QrAuthSessions.ConsumeIfConfirmed(session);
            return Ok(new { status, token });
        }

        /// <summary>Poster wall manifest for the deny page (see PosterWall). wall=false means
        /// "no wall" — the page keeps its plain gradient background.</summary>
        [HttpGet("posters")]
        public ActionResult Posters()
        {
            if (!ModInit.denyConf.poster_wall)
                return Ok(new { count = 0, v = 0, wall = false, state = "disabled" });

            return Ok(new { count = PosterWall.Count, v = PosterWall.Version, wall = PosterWall.WallPath() != null, state = PosterWall.State });
        }

        /// <summary>The whole wall pre-rendered as one JPEG (PosterWall.BuildWall).
        /// No ".jpg" in the route on purpose: Lampac's accsdb middleware 404s any *.jpg for
        /// an unauthorized visitor (IsStaticAsset) BEFORE module Accsdb handlers run, so an
        /// extension here would make ModInit.AllowQrRoutes unable to let it through.</summary>
        [HttpGet("wall")]
        public ActionResult Wall([FromQuery] string s = null)
        {
            // ?s=4k: 3840x2160 for screens wider than ~2000 device px (2K/4K); ?s=m: portrait
            // wall for phones; else 1080p (also the fallback if the requested one is missing).
            string path = (s == "4k" ? PosterWall.WallPath(true) : s == "m" ? PosterWall.PortraitPath() : null) ?? PosterWall.WallPath();
            if (path == null)
                return NotFound();

            // ?v= in the page URL changes with every refreshed set, so a long cache is safe.
            Response.Headers.CacheControl = "public, max-age=86400";
            return PhysicalFile(path, "image/jpeg");
        }

        /// <summary>Fire-and-forget ping from the deny-page password form (see doLogin() in
        /// DenyPageGenerator.cs) — the only way this module learns about a plain-password
        /// login, since that request goes straight to Lampac's own /testaccsdb and never
        /// touches this module otherwise. Always 200s so a missing/unknown token can't be
        /// used to probe which tokens exist.</summary>
        [HttpPost("login-ping")]
        public async Task<ActionResult> LoginPing([FromQuery] string token)
        {
            if (!ModInit.conf.enable || string.IsNullOrWhiteSpace(token))
                return Ok();

            var bot = TelegramBotHostedService.Bot;
            var repo = TelegramBotHostedService.Repo;
            if (bot == null || repo == null)
                return Ok();

            var user = repo.GetByToken(token);
            if (user == null)
                return Ok();

            var text = string.Join("\n", new[]
            {
                "🔑  <b>Вход по паролю</b>",
                $"👤  <b>{System.Net.WebUtility.HtmlEncode(user.Comment)}</b>",
                $"🆔  <code>{user.TgId}</code>"
            });

            foreach (var adminId in ModInit.conf.admin_ids)
            {
                try
                {
                    await bot.SendMessage(adminId, text, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html);
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[TelegramBot] notify admin {adminId} failed", ex);
                }
            }

            return Ok();
        }
    }
}
