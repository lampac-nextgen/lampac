using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models.Base;
using System;

namespace NativeAppleTV;

/// <summary>Контракт luxo-source/1 §3.1 и §4: start → poll → check.</summary>
public class AuthController : BaseController
{
    public record CodeBody(string code);
    public record TokenBody(string token);

    /// <summary>Приложение просит код и показывает его пользователю (и QR на ссылку подтверждения).</summary>
    [HttpPost, AllowAnonymous]
    [Route("nativeappletv/auth/start")]
    public ActionResult Start()
    {
        SetHeadersNoCache();

        var conf = ModInit.conf;
        string code = ChallengeStore.Create(conf.code_ttl);
        if (code == null)
            return StatusCode(429, new { error = "rate_limited", message = "Слишком много запросов, попробуйте позже" });

        return Json(new
        {
            code,
            expires_in = conf.code_ttl,
            interval = conf.poll_interval,
            verification_uri_complete = $"{PublicUrl()}/nativeappletv/confirm?code={code}"
        });
    }

    /// <summary>Приложение опрашивает раз в interval секунд: pending → ждать, token → готово.</summary>
    [HttpPost, AllowAnonymous]
    [Route("nativeappletv/auth/poll")]
    public ActionResult Poll([FromBody] CodeBody body)
    {
        SetHeadersNoCache();

        if (!ChallengeStore.TryGet(body?.code, out var item))
            return StatusCode(410, new { error = "expired_token", message = "Код истёк, запросите новый" });

        var conf = ModInit.conf;
        if (DateTime.UtcNow - item.lastPoll < TimeSpan.FromSeconds(Math.Max(1, conf.poll_interval) - 0.5))
        {
            item.lastPoll = DateTime.UtcNow;
            return StatusCode(429, new { error = "slow_down" });
        }
        item.lastPoll = DateTime.UtcNow;

        if (item.token == null)
            return Json(new { status = "pending" });

        ChallengeStore.Remove(body.code); // код одноразовый: токен отдаём ровно один раз
        return Json(new { status = "success", token = item.token });
    }

    /// <summary>Проверка сохранённого токена при запуске приложения.</summary>
    [HttpPost, AllowAnonymous]
    [Route("nativeappletv/auth/check")]
    public ActionResult Check([FromBody] TokenBody body)
    {
        SetHeadersNoCache();

        var user = string.IsNullOrWhiteSpace(body?.token) ? null : CoreInit.conf.accsdb.findUser(new StringValues(body.token));

        if (user == null || user.ban)
            return StatusCode(400, new { error = "invalid_token", message = user?.ban_msg });

        if (user.expires < DateTime.Now)
            return StatusCode(402, new { error = "upgrade_required", message = $"Доступ истёк {user.expires:dd.MM.yyyy}" });

        return Json(new { valid = true, expires = user.expires });
    }

    string PublicUrl()
    {
        string pub = ModInit.conf.public_url?.Trim().TrimEnd('/');
        return !string.IsNullOrEmpty(pub) ? pub : $"{Request.Scheme}://{Request.Host}";
    }
}
