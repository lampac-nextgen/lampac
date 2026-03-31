using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
                message = "Привяжи UID в Telegram-боте."
            };

            return JsonOk(response);
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/bind/complete")]
        public ActionResult BindComplete([FromBody] BindCompleteRequest? request)
        {
            var mutSecret = ModInit.conf.mutations_api_secret?.Trim() ?? "";
            if (mutSecret.Length > 0 && !TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            if (request == null || string.IsNullOrWhiteSpace(request.Uid) || string.IsNullOrWhiteSpace(request.TelegramId))
                return JsonError(400, "uid and telegramId are required");

            try
            {
                var bindOutcome = store.BindDevice(request.TelegramId, request.Uid, request.Username, request.DeviceName, "manual-complete");
                return JsonOk(new
                {
                    ok = true,
                    message = bindOutcome.PendingAdminApproval
                        ? "Устройство привязано; доступ включит администратор"
                        : "Устройство привязано",
                    uid = request.Uid,
                    telegramId = request.TelegramId,
                    newUserProvisioned = bindOutcome.NewUserProvisioned,
                    pendingAdminApproval = bindOutcome.PendingAdminApproval
                });
            }
            catch (InvalidOperationException ex) when (ex.Message == TelegramAuthStore.BindUserNotFoundMessage)
            {
                return JsonError(404, "user not found", "Включите auto_provision_users в TelegramAuth или добавьте пользователя в users.json.");
            }
            catch (InvalidOperationException ex) when (ex.Message == TelegramAuthStore.BindUserDisabledMessage)
            {
                return JsonError(403, "user disabled", "Аккаунт отключён администратором.");
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

            var registrationPending = TelegramAuthStore.IsRegistrationPending(user);
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
                disabled = user.Disabled,
                registrationPending,
                active = store.IsActive(user),
                deviceCount = user.Devices.Count(d => d.Active),
                maxDevices = store.GetMaxDevices(user)
            });
        }

        [HttpGet]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/admin/users")]
        public ActionResult AdminListUsers()
        {
            if (!TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            var users = store.GetUsers()
                .OrderBy(u => u.TelegramId, StringComparer.Ordinal)
                .Select(u => new
                {
                    telegramId = u.TelegramId,
                    username = u.TgUsername,
                    role = u.Role,
                    disabled = u.Disabled,
                    registrationPending = TelegramAuthStore.IsRegistrationPending(u),
                    active = store.IsActive(u),
                    expiresAt = u.ExpiresAt,
                    deviceCount = u.Devices.Count(d => d.Active),
                    accs = u.Accs == null
                        ? null
                        : new
                        {
                            u.Accs.group,
                            u.Accs.IsPasswd,
                            u.Accs.ban,
                            u.Accs.ban_msg,
                            u.Accs.comment,
                            u.Accs.ids,
                            u.Accs.@params
                        }
                })
                .ToList();

            return JsonOk(new { ok = true, users });
        }

        [HttpGet]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/admin/user")]
        public ActionResult AdminGetUser([FromQuery] string telegramId)
        {
            if (!TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            if (string.IsNullOrWhiteSpace(telegramId))
                return JsonError(400, "telegramId is required");

            var user = store.FindByTelegramId(telegramId.Trim());
            if (user == null)
                return JsonError(404, "user not found");

            return JsonOk(new
            {
                ok = true,
                telegramId = user.TelegramId,
                username = user.TgUsername,
                role = user.Role,
                lang = user.Lang,
                approvedBy = user.ApprovedBy,
                createdAt = user.CreatedAt,
                expiresAt = user.ExpiresAt,
                disabled = user.Disabled,
                registrationPending = TelegramAuthStore.IsRegistrationPending(user),
                active = store.IsActive(user),
                deviceCount = user.Devices.Count(d => d.Active),
                maxDevices = store.GetMaxDevices(user),
                accs = user.Accs,
                devices = user.Devices.Select(d => new
                {
                    uid = d.Uid,
                    name = d.Name,
                    active = d.Active,
                    linkedAt = d.LinkedAt,
                    lastSeenAt = d.LastSeenAt,
                    source = d.Source
                }).ToList()
            });
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/admin/user/patch")]
        public async Task<ActionResult> AdminPatchUser()
        {
            if (!TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            string raw;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
                raw = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(raw))
                return JsonError(400, "body is required");

            JObject body;
            try
            {
                body = JObject.Parse(raw);
            }
            catch (JsonReaderException)
            {
                return JsonError(400, "invalid json");
            }

            var tid = body.Value<string>("telegramId");
            if (string.IsNullOrWhiteSpace(tid))
                return JsonError(400, "telegramId is required");

            var outcome = store.TryAdminPatchUser(tid.Trim(), body, out var err);
            if (outcome == TelegramAuthStore.AdminPatchUserOutcome.NotFound)
                return JsonError(404, "user not found");
            if (outcome == TelegramAuthStore.AdminPatchUserOutcome.InvalidPayload)
                return JsonError(400, "invalid payload", err);

            return JsonOk(new { ok = true, telegramId = tid.Trim() });
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/admin/user/disabled")]
        public ActionResult AdminSetUserDisabled([FromBody] AdminSetUserDisabledRequest? request)
        {
            if (!TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            if (request == null || string.IsNullOrWhiteSpace(request.TelegramId))
                return JsonError(400, "telegramId is required");

            var outcome = store.TrySetUserDisabled(request.TelegramId.Trim(), request.Disabled);
            if (outcome == TelegramAuthStore.SetUserDisabledOutcome.NotFound)
                return JsonError(404, "user not found");
            if (outcome == TelegramAuthStore.SetUserDisabledOutcome.CannotDisableAdmin)
                return JsonError(403, "cannot disable admin", "Нельзя отключить учётную запись с ролью admin.");

            return JsonOk(new
            {
                ok = true,
                telegramId = request.TelegramId.Trim(),
                disabled = request.Disabled
            });
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/admin/user/pending")]
        public ActionResult AdminResolveRegistrationPending([FromBody] AdminPendingDecisionRequest? request)
        {
            if (!TryAuthorizeMutations())
                return JsonError(403, "forbidden", "use accspasswd cookie or " + MutationsSecretHeaderName);

            if (request == null || string.IsNullOrWhiteSpace(request.TelegramId))
                return JsonError(400, "telegramId is required");

            var tid = request.TelegramId.Trim();
            if (request.Approve)
            {
                var outcome = store.TryApproveRegistrationPending(tid);
                if (outcome == TelegramAuthStore.PendingDecisionOutcome.NotFound)
                    return JsonError(404, "user not found");
                if (outcome == TelegramAuthStore.PendingDecisionOutcome.NotPending)
                    return JsonError(400, "not pending", "Пользователь не в статусе ожидания подтверждения.");

                return JsonOk(new { ok = true, telegramId = tid, approved = true });
            }
            else
            {
                var outcome = store.TryRejectRegistrationPending(tid);
                if (outcome == TelegramAuthStore.PendingDecisionOutcome.NotFound)
                    return JsonError(404, "user not found");
                if (outcome == TelegramAuthStore.PendingDecisionOutcome.NotPending)
                    return JsonError(400, "not pending", "Пользователь не в статусе ожидания подтверждения.");
                if (outcome == TelegramAuthStore.PendingDecisionOutcome.CannotRejectAdmin)
                    return JsonError(403, "cannot reject admin");

                return JsonOk(new { ok = true, telegramId = tid, rejected = true });
            }
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
        public ActionResult Unbind([FromBody] DeviceUnbindRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Uid) || string.IsNullOrWhiteSpace(request.TelegramId))
                return JsonError(400, "telegramId and uid are required");

            var outcome = store.TryUnbindDevice(request.TelegramId.Trim(), request.Uid.Trim());
            if (outcome == TelegramAuthStore.UnbindDeviceOutcome.UserNotFound)
                return JsonError(404, "user not found");
            if (outcome == TelegramAuthStore.UnbindDeviceOutcome.DeviceNotFound)
                return JsonError(404, "device not found", "UID не найден среди устройств этого пользователя.");

            return JsonOk(new { ok = true, uid = request.Uid.Trim() });
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/device/reactivate")]
        public ActionResult ReactivateDevice([FromBody] DeviceReactivateRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TelegramId) || string.IsNullOrWhiteSpace(request.Uid))
                return JsonError(400, "telegramId and uid are required");

            var outcome = store.TryReactivateDevice(request.TelegramId.Trim(), request.Uid.Trim());
            if (outcome == TelegramAuthStore.ReactivateDeviceOutcome.UserNotFound)
                return JsonError(404, "user not found");
            if (outcome == TelegramAuthStore.ReactivateDeviceOutcome.UserDisabled)
                return JsonError(403, "user disabled", "Аккаунт отключён администратором.");
            if (outcome == TelegramAuthStore.ReactivateDeviceOutcome.DeviceNotFound)
                return JsonError(404, "device not found", "UID не найден среди ваших устройств.");

            return JsonOk(new { ok = true, uid = request.Uid.Trim() });
        }

        [HttpPost]
        [AllowAnonymous]
        [AuthorizeAnonymous]
        [Route("/tg/auth/device/name")]
        public ActionResult SetDeviceName([FromBody] DeviceSetNameRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Uid))
                return JsonError(400, "uid is required");

            var outcome = store.TrySetActiveDeviceDisplayName(request.Uid, request.Name);
            if (outcome == TelegramAuthStore.SetDeviceDisplayNameOutcome.InvalidUid)
                return JsonError(400, "uid is required");
            if (outcome == TelegramAuthStore.SetDeviceDisplayNameOutcome.NotFoundOrInactive)
                return JsonError(404, "device not found or access inactive", "UID не привязан или срок доступа истёк.");

            return JsonOk(new
            {
                ok = true,
                uid = request.Uid.Trim(),
                name = string.IsNullOrWhiteSpace(request.Name) ? (string?)null : request.Name.Trim()
            });
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
