using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace NativeAppleTV;

/// <summary>Подтверждение кода: страница по QR (пароль администратора) и вызов бота (общий секрет).</summary>
public class ConfirmController : BaseController
{
    public record BotBody(string code, int days = 0, string device = null);

    static bool Same(string a, string b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    [HttpGet, AllowAnonymous]
    [Route("nativeappletv/confirm")]
    public ActionResult Page(string code) => Html(Form(code, null));

    [HttpPost, AllowAnonymous]
    [Route("nativeappletv/confirm")]
    public ActionResult Submit([FromForm] string code, [FromForm] string passwd)
    {
        string ip = requestInfo.IP;
        if (ChallengeStore.IsBlocked(ip))
            return Html(Form(code, "Слишком много попыток, подождите 10 минут"), 429);

        if (!Same(passwd, CoreInit.rootPasswd))
        {
            ChallengeStore.Fail(ip);
            return Html(Form(code, "Неверный пароль"), 401);
        }

        if (!ChallengeStore.TryGet(code, out var item))
            return Html(Form(code, "Код не найден или истёк. Обновите код в приложении"), 404);

        ChallengeStore.ResetFails(ip);
        Approve(item, 0, "web");
        return Html(Layout("<h2>Готово</h2><p>Вход подтверждён. Вернитесь к приложению.</p>"));
    }

    /// <summary>Вызывает Telegram-бот/личный кабинет, когда пользователь подтвердил код. Заголовок X-Bot-Secret.</summary>
    [HttpPost, AllowAnonymous]
    [Route("nativeappletv/bot/confirm")]
    public ActionResult BotConfirm([FromBody] BotBody body)
    {
        string ip = requestInfo.IP;
        if (ChallengeStore.IsBlocked(ip))
            return StatusCode(429, new { error = "rate_limited" });

        if (!Same(Request.Headers["X-Bot-Secret"], ModInit.conf.bot_secret))
        {
            ChallengeStore.Fail(ip);
            return StatusCode(401, new { error = "access_denied" });
        }

        if (!ChallengeStore.TryGet(body?.code, out var item))
            return StatusCode(404, new { error = "expired_token" });

        Approve(item, body.days, body.device ?? "bot");
        return Json(new { confirmed = true });
    }

    static void Approve(ChallengeStore.Challenge item, int days, string device)
    {
        lock (item)
        {
            // повторное нажатие не выдаёт второй токен
            if (item.token != null)
                return;

            var conf = ModInit.conf;
            item.token = TokenStore.Issue(days > 0 ? days : conf.token_days, conf.group, device);
        }
    }

    ActionResult Html(string html, int status = 200)
    {
        SetHeadersNoCache();
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = status };
    }

    static string Form(string code, string error)
    {
        string c = WebUtility.HtmlEncode(ChallengeStore.Normalize(code));
        string err = string.IsNullOrEmpty(error) ? "" : $"<p style=\"color:#c33\">{WebUtility.HtmlEncode(error)}</p>";

        return Layout($@"<h2>Подтверждение входа</h2>{err}
<form method=""post"" action=""/nativeappletv/confirm"">
<p><input name=""code"" value=""{c}"" placeholder=""Код"" autocomplete=""off"" style=""font-size:28px;letter-spacing:4px;text-transform:uppercase""></p>
<p><input name=""passwd"" type=""password"" placeholder=""Пароль администратора"" style=""font-size:18px""></p>
<p><button type=""submit"" style=""font-size:18px;padding:8px 24px"">Подтвердить</button></p>
</form>");
    }

    static string Layout(string body)
        => $"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Apple TV</title></head><body style=\"font-family:-apple-system,sans-serif;max-width:420px;margin:48px auto;padding:0 16px\">{body}</body></html>";
}
