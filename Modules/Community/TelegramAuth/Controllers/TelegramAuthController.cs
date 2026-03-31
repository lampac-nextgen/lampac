using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Shared;
using Shared.Attributes;
using TelegramAuth;
using TelegramAuth.Models;
using TelegramAuth.Services;

namespace TelegramAuth.Controllers
{
    [Authorize]
    public class TelegramAuthController : BaseController
    {
        public const string MutationsSecretHeaderName = "X-TelegramAuth-Mutations-Secret";

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        static TelegramAuthStore store => ModInit.Store;

        [HttpGet]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/status")]
        public ActionResult Status([FromQuery] string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
                return JsonError(400, "uid is required");

            var status = store.GetStatus(uid);
            return JsonOk(status);
        }

        [HttpGet]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/me")]
        public ActionResult Me([FromQuery] string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
                return JsonError(400, "uid is required");

            var user = store.FindByUid(uid);
            if (user == null)
                return JsonError(404, "user not found for uid");

            return JsonOk(user);
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/bind/start")]
        public ActionResult BindStart([FromBody] BindStartRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Uid))
                return JsonError(400, "uid is required");

            var response = new
            {
                ok = true,
                pending = true,
                uid = request.Uid,
                message = "MVP: устройство отмечено как ожидающее Telegram-подтверждение. Следующий шаг — связать это с ботом."
            };

            return JsonOk(response);
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/bind/complete")]
        public ActionResult BindComplete([FromBody] BindCompleteRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Uid) || string.IsNullOrWhiteSpace(request.TelegramId))
                return JsonError(400, "uid and telegramId are required");

            try
            {
                store.BindDevice(request.TelegramId, request.Uid, request.Username, "manual-complete");
                return JsonOk(new
                {
                    ok = true,
                    message = "Устройство привязано",
                    uid = request.Uid,
                    telegramId = request.TelegramId
                });
            }
            catch (InvalidOperationException ex) when (ex.Message == TelegramAuthStore.BindUserNotFoundMessage)
            {
                return JsonError(404, "user not found", "Включите auto_provision_users в TelegramAuth или добавьте пользователя в users.json.");
            }
            catch (Exception ex)
            {
                return JsonError(500, "bind failed", ex.Message);
            }
        }

        [HttpGet]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/user/by-telegram")]
        public ActionResult UserByTelegram([FromQuery] string telegramId)
        {
            if (string.IsNullOrWhiteSpace(telegramId))
                return JsonError(400, "telegramId is required");

            var user = store.FindByTelegramId(telegramId);
            if (user == null)
            {
                return JsonOk(new
                {
                    found = false,
                    telegramId
                });
            }

            return JsonOk(new
            {
                found = true,
                telegramId = user.TelegramId,
                username = user.TgUsername,
                role = user.Role,
                lang = user.Lang,
                approvedBy = user.ApprovedBy,
                createdAt = user.CreatedAt,
                expiresAt = user.ExpiresAt,
                active = store.IsActive(user),
                deviceCount = user.Devices.Count(d => d.Active),
                maxDevices = store.GetMaxDevices(user)
            });
        }

        [HttpGet]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/devices")]
        public ActionResult Devices([FromQuery] string telegramId)
        {
            if (string.IsNullOrWhiteSpace(telegramId))
                return JsonError(400, "telegramId is required");

            var user = store.FindByTelegramId(telegramId);
            if (user == null)
                return JsonError(404, "user not found");

            return JsonOk(new
            {
                telegramId = user.TelegramId,
                username = user.TgUsername,
                devices = user.Devices
            });
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/device/unbind")]
        public ActionResult Unbind([FromBody] BindStartRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Uid))
                return JsonError(400, "uid is required");

            var users = store.GetUsers();
            var changed = false;
            foreach (var user in users)
            {
                var device = user.Devices.Find(d => string.Equals(d.Uid, request.Uid, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    device.Active = false;
                    changed = true;
                }
            }

            if (changed)
                store.SaveUsers(users);

            return JsonOk(new { ok = changed, uid = request.Uid });
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/devices/cleanup")]
        public ActionResult CleanupDevices()
        {
            if (!ModInit.conf.enable_cleanup)
                return JsonError(404, "cleanup disabled");

            if (!TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            try
            {
                var removed = store.CleanupInactiveDevices();
                return JsonOk(new { ok = true, removed });
            }
            catch (Exception ex)
            {
                return JsonError(500, "cleanup failed", ex.Message);
            }
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/import")]
        public ActionResult ImportLegacy()
        {
            if (!ModInit.conf.enable_import)
                return JsonError(404, "import disabled");

            if (!TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            var legacyPath = ModInit.conf.legacy_import_path?.Trim();
            if (string.IsNullOrEmpty(legacyPath))
                return JsonError(400, "legacy_import_path is not configured");

            try
            {
                var result = store.ImportFromLegacy(legacyPath);
                return JsonOk(result);
            }
            catch (Exception ex)
            {
                return JsonError(500, "import failed", ex.Message);
            }
        }

        bool TryAuthorizeMutations()
        {
            if (HttpContext.Request.Cookies.TryGetValue("accspasswd", out var cookie)
                && !string.IsNullOrEmpty(CoreInit.rootPasswd)
                && string.Equals(cookie, CoreInit.rootPasswd, StringComparison.Ordinal))
                return true;

            var secret = ModInit.conf.mutations_api_secret?.Trim() ?? "";
            if (secret.Length == 0)
                return false;

            if (!HttpContext.Request.Headers.TryGetValue(MutationsSecretHeaderName, out var hv))
                return false;

            return FixedTimeEqualUtf8(hv.ToString(), secret);
        }

        static bool FixedTimeEqualUtf8(string a, string b)
        {
            var ba = Encoding.UTF8.GetBytes(a ?? "");
            var bb = Encoding.UTF8.GetBytes(b ?? "");
            return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
        }

        ContentResult JsonOk(object data)
        {
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(data, JsonSettings),
                ContentType = "application/json; charset=utf-8",
                StatusCode = 200
            };
        }

        ContentResult JsonError(int status, string error, string? detail = null)
        {
            var payload = new
            {
                error,
                detail
            };
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(payload, JsonSettings),
                ContentType = "application/json; charset=utf-8",
                StatusCode = status
            };
        }
    }
}
