using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TelegramAuth.Models;

namespace TelegramAuth.Services
{
    public partial class TelegramAuthStore
    {
        public enum SetUserDisabledOutcome
        {
            Ok,
            NotFound,
            CannotDisableAdmin
        }

        public SetUserDisabledOutcome TrySetUserDisabled(string telegramId, bool disabled)
        {
            if (string.IsNullOrWhiteSpace(telegramId))
                return SetUserDisabledOutcome.NotFound;

            var users = GetUsers();
            var user = users.FirstOrDefault(u => string.Equals(u.TelegramId, telegramId, StringComparison.Ordinal));
            if (user == null)
                return SetUserDisabledOutcome.NotFound;

            if (disabled && string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return SetUserDisabledOutcome.CannotDisableAdmin;

            user.Disabled = disabled;
            if (disabled)
            {
                user.RegistrationPending = false;
                if (ShouldSyncAccsdb() && user.Devices != null)
                {
                    foreach (var d in user.Devices)
                    {
                        if (!string.IsNullOrWhiteSpace(d.Uid))
                            AccsdbUidSync.RemoveUid(d.Uid);
                    }
                }

                if (user.Devices != null)
                {
                    foreach (var d in user.Devices)
                        d.Active = false;
                }
            }
            else
                user.RegistrationPending = false;

            SaveUsers(users);

            if (!disabled && ShouldSyncAccsdb() && IsActive(user))
            {
                foreach (var d in user.Devices ?? new List<DeviceRecord>())
                {
                    if (d.Active && !string.IsNullOrWhiteSpace(d.Uid))
                        SyncDeviceToAccsdb(user, d.Uid);
                }
            }

            return SetUserDisabledOutcome.Ok;
        }

        public enum PendingDecisionOutcome
        {
            Ok,
            NotFound,
            NotPending,
            CannotRejectAdmin
        }

        public PendingDecisionOutcome TryApproveRegistrationPending(string telegramId)
        {
            var tid = telegramId?.Trim();
            if (string.IsNullOrEmpty(tid))
                return PendingDecisionOutcome.NotFound;

            var users = GetUsers();
            var user = users.FirstOrDefault(u => string.Equals(u.TelegramId, tid, StringComparison.Ordinal));
            if (user == null)
                return PendingDecisionOutcome.NotFound;
            if (!IsRegistrationPending(user))
                return PendingDecisionOutcome.NotPending;

            user.RegistrationPending = false;
            user.Disabled = false;
            user.ApprovedBy = "admin-approved";
            SaveUsers(users);
            if (ShouldSyncAccsdb() && IsActive(user))
            {
                foreach (var d in user.Devices ?? new List<DeviceRecord>())
                {
                    if (d.Active && !string.IsNullOrWhiteSpace(d.Uid))
                        SyncDeviceToAccsdb(user, d.Uid);
                }
            }

            return PendingDecisionOutcome.Ok;
        }

        public PendingDecisionOutcome TryRejectRegistrationPending(string telegramId)
        {
            var tid = telegramId?.Trim();
            if (string.IsNullOrEmpty(tid))
                return PendingDecisionOutcome.NotFound;

            var users = GetUsers();
            var user = users.FirstOrDefault(u => string.Equals(u.TelegramId, tid, StringComparison.Ordinal));
            if (user == null)
                return PendingDecisionOutcome.NotFound;
            if (!IsRegistrationPending(user))
                return PendingDecisionOutcome.NotPending;
            if (string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return PendingDecisionOutcome.CannotRejectAdmin;

            if (ShouldSyncAccsdb())
            {
                foreach (var d in user.Devices ?? new List<DeviceRecord>())
                {
                    if (!string.IsNullOrWhiteSpace(d.Uid))
                        AccsdbUidSync.RemoveUid(d.Uid);
                }
            }

            users.Remove(user);
            SaveUsers(users);
            return PendingDecisionOutcome.Ok;
        }

        internal const int DeviceDisplayNameMaxLen = 500;

        public enum SetDeviceDisplayNameOutcome
        {
            Ok,
            InvalidUid,
            NotFoundOrInactive
        }

        public enum ReactivateDeviceOutcome
        {
            Ok,
            UserNotFound,
            UserDisabled,
            DeviceNotFound
        }

        public enum UnbindDeviceOutcome
        {
            Ok,
            UserNotFound,
            DeviceNotFound
        }

        public UnbindDeviceOutcome TryUnbindDevice(string telegramId, string uid)
        {
            var tid = telegramId?.Trim();
            var u = uid?.Trim();
            if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(u))
                return UnbindDeviceOutcome.DeviceNotFound;

            var users = GetUsers();
            var user = users.FirstOrDefault(x => string.Equals(x.TelegramId, tid, StringComparison.Ordinal));
            if (user == null)
                return UnbindDeviceOutcome.UserNotFound;

            var device = user.Devices?.FirstOrDefault(d => string.Equals(d.Uid, u, StringComparison.OrdinalIgnoreCase));
            if (device == null)
                return UnbindDeviceOutcome.DeviceNotFound;

            device.Active = false;
            SaveUsers(users);
            if (ShouldSyncAccsdb())
                AccsdbUidSync.RemoveUid(u);
            return UnbindDeviceOutcome.Ok;
        }

        public ReactivateDeviceOutcome TryReactivateDevice(string telegramId, string uid)
        {
            var tid = telegramId?.Trim();
            var u = uid?.Trim();
            if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(u))
                return ReactivateDeviceOutcome.DeviceNotFound;

            var users = GetUsers();
            var user = users.FirstOrDefault(x => string.Equals(x.TelegramId, tid, StringComparison.Ordinal));
            if (user == null)
                return ReactivateDeviceOutcome.UserNotFound;
            if (IsRegistrationPending(user) || user.Disabled)
                return ReactivateDeviceOutcome.UserDisabled;

            var device = user.Devices?.FirstOrDefault(d => string.Equals(d.Uid, u, StringComparison.OrdinalIgnoreCase));
            if (device == null)
                return ReactivateDeviceOutcome.DeviceNotFound;
            if (device.Active)
                return ReactivateDeviceOutcome.Ok;

            CleanupInactiveDevices(users, 90);

            BumpOldestActiveDeviceIfAtDeviceLimit(user);

            device.Active = true;
            device.LastSeenAt = DateTime.UtcNow;
            SaveUsers(users);
            if (ShouldSyncAccsdb() && IsActive(user) && device.Active)
                SyncDeviceToAccsdb(user, u);
            return ReactivateDeviceOutcome.Ok;
        }

        public SetDeviceDisplayNameOutcome TrySetActiveDeviceDisplayName(string uid, string? name)
        {
            var u = uid?.Trim();
            if (string.IsNullOrEmpty(u))
                return SetDeviceDisplayNameOutcome.InvalidUid;

            string? resolved = null;
            if (!string.IsNullOrWhiteSpace(name))
            {
                resolved = name.Trim();
                if (resolved.Length > DeviceDisplayNameMaxLen)
                    resolved = resolved.Substring(0, DeviceDisplayNameMaxLen);
            }

            var users = GetUsers();
            foreach (var user in users)
            {
                if (!IsActive(user))
                    continue;

                var dev = user.Devices?.FirstOrDefault(d => d.Active && string.Equals(d.Uid, u, StringComparison.OrdinalIgnoreCase));
                if (dev == null)
                    continue;

                dev.Name = resolved;
                SaveUsers(users);
                return SetDeviceDisplayNameOutcome.Ok;
            }

            return SetDeviceDisplayNameOutcome.NotFoundOrInactive;
        }

        public int GetMaxDevices(TelegramUserRecord user)
        {
            if (user != null && string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return AdminUnlimitedDevices;

            int max = _conf.max_active_devices_per_user;
            return max > 0 ? max : MaxActiveDevicesPerUser;
        }

        public const string BindUserNotFoundMessage = "user not found";
        public const string BindUserDisabledMessage = "user disabled";

        public BindDeviceOutcome BindDevice(string telegramId, string uid, string? telegramUsername = null, string? deviceDisplayName = null, string source = "manual")
        {
            var outcome = new BindDeviceOutcome();
            var users = GetUsers();
            var user = users.FirstOrDefault(u => u.TelegramId == telegramId);
            if (user == null)
            {
                if (!_conf.auto_provision_users)
                    throw new TelegramAuthBindException(TelegramAuthBindFailureKind.UserNotFound, BindUserNotFoundMessage);

                DateTime? expires = null;
                var days = _conf.auto_provision_expires_days;
                if (days > 0)
                    expires = DateTime.UtcNow.AddDays(days);

                var role = string.IsNullOrWhiteSpace(_conf.auto_provision_role)
                    ? "user"
                    : _conf.auto_provision_role.Trim();
                if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
                    role = "user";

                var pendingApproval = !_conf.auto_provision_activate_immediately;
                user = new TelegramUserRecord
                {
                    TelegramId = telegramId,
                    TgUsername = telegramUsername?.Trim() ?? "",
                    Role = role,
                    Lang = string.IsNullOrWhiteSpace(_conf.auto_provision_lang) ? "ru" : _conf.auto_provision_lang.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expires,
                    ApprovedBy = pendingApproval ? RegistrationPendingApprovedByMarker : "auto-provision",
                    Disabled = pendingApproval,
                    RegistrationPending = pendingApproval,
                    Devices = new List<DeviceRecord>()
                };
                users.Add(user);
                outcome.NewUserProvisioned = true;
                outcome.PendingAdminApproval = pendingApproval;
            }
            else if (user.Disabled && !IsRegistrationPending(user))
            {
                throw new TelegramAuthBindException(TelegramAuthBindFailureKind.UserDisabled, BindUserDisabledMessage);
            }
            else if (!string.IsNullOrWhiteSpace(telegramUsername))
            {
                var trimmed = telegramUsername.Trim();
                if (!string.Equals(user.TgUsername, trimmed, StringComparison.Ordinal))
                    user.TgUsername = trimmed;
            }

            string? resolvedDeviceName = null;
            if (!string.IsNullOrWhiteSpace(deviceDisplayName))
            {
                var t = deviceDisplayName.Trim();
                resolvedDeviceName = t.Length > DeviceDisplayNameMaxLen ? t.Substring(0, DeviceDisplayNameMaxLen) : t;
            }

            CleanupInactiveDevices(users, 90);

            var existing = user.Devices.FirstOrDefault(d => string.Equals(d.Uid, uid, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                BumpOldestActiveDeviceIfAtDeviceLimit(user);

                user.Devices.Add(new DeviceRecord
                {
                    Uid = uid,
                    Name = resolvedDeviceName,
                    LinkedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    Active = true,
                    Source = source
                });
            }
            else
            {
                if (!existing.Active)
                    BumpOldestActiveDeviceIfAtDeviceLimit(user);

                existing.Active = true;
                if (!string.IsNullOrWhiteSpace(resolvedDeviceName))
                    existing.Name = resolvedDeviceName;
                existing.LastSeenAt = DateTime.UtcNow;
            }

            SaveUsers(users);
            SyncAccsdbForBind(user, uid);
            return outcome;
        }

        public AuthStatusResponse GetStatus(string uid)
        {
            var user = FindByUid(uid);
            if (user == null)
            {
                return new AuthStatusResponse
                {
                    Authorized = false,
                    Pending = true,
                    Message = "Устройство не привязано. Нужна авторизация через Telegram."
                };
            }

            var now = DateTime.UtcNow;
            if (IsRegistrationPending(user))
            {
                return new AuthStatusResponse
                {
                    Authorized = false,
                    Pending = true,
                    RegistrationPending = true,
                    Message = "Аккаунт ожидает подтверждения администратора.",
                    TelegramId = user.TelegramId,
                    Username = user.TgUsername,
                    Role = user.Role,
                    ExpiresAt = user.ExpiresAt,
                    DeviceCount = user.Devices.Count(d => d.Active)
                };
            }

            if (user.Disabled)
            {
                return new AuthStatusResponse
                {
                    Authorized = false,
                    Pending = false,
                    RegistrationPending = false,
                    Message = "Доступ отключён администратором.",
                    TelegramId = user.TelegramId,
                    Username = user.TgUsername,
                    Role = user.Role,
                    ExpiresAt = user.ExpiresAt,
                    DeviceCount = user.Devices.Count(d => d.Active)
                };
            }

            var expired = user.ExpiresAt.HasValue && user.ExpiresAt.Value.ToUniversalTime() < now;
            return new AuthStatusResponse
            {
                Authorized = !expired,
                Pending = false,
                RegistrationPending = false,
                Message = expired ? "Срок доступа истёк." : "OK",
                TelegramId = user.TelegramId,
                Username = user.TgUsername,
                Role = user.Role,
                ExpiresAt = user.ExpiresAt,
                DeviceCount = user.Devices.Count(d => d.Active)
            };
        }

        public int CleanupInactiveDevices(List<TelegramUserRecord>? users = null, int olderThanDays = 90)
        {
            users ??= GetUsers();
            var threshold = DateTime.UtcNow.AddDays(-olderThanDays);
            var removed = 0;

            foreach (var user in users)
            {
                if (user.Devices == null)
                    continue;

                var before = user.Devices.Count;
                user.Devices = user.Devices
                    .Where(d => d.Active || ((d.LastSeenAt ?? d.LinkedAt) >= threshold))
                    .ToList();

                var maxDevices = GetMaxDevices(user);
                if (maxDevices > 0)
                {
                    var activeDevices = user.Devices.Where(d => d.Active).OrderByDescending(d => d.LastSeenAt ?? d.LinkedAt).ToList();
                    if (activeDevices.Count > maxDevices)
                    {
                        foreach (var extra in activeDevices.Skip(maxDevices))
                        {
                            extra.Active = false;
                            RemoveUidFromAccsdbIfSync(extra.Uid);
                        }
                    }
                }

                removed += before - user.Devices.Count;
            }

            SaveUsers(users);
            return removed;
        }

        public enum AdminPatchUserOutcome
        {
            Ok,
            NotFound,
            InvalidPayload
        }

        public AdminPatchUserOutcome TryAdminPatchUser(string telegramId, JObject body, out string? errorDetail)
        {
            errorDetail = null;
            if (string.IsNullOrWhiteSpace(telegramId))
            {
                errorDetail = "telegramId is required";
                return AdminPatchUserOutcome.InvalidPayload;
            }

            if (body == null)
            {
                errorDetail = "body is required";
                return AdminPatchUserOutcome.InvalidPayload;
            }

            var users = GetUsers();
            var tid = telegramId.Trim();
            var user = users.FirstOrDefault(u => string.Equals(u.TelegramId, tid, StringComparison.Ordinal));
            if (user == null)
                return AdminPatchUserOutcome.NotFound;

            if (body.ContainsKey("expiresAt"))
            {
                var t = body["expiresAt"];
                if (t == null || t.Type == JTokenType.Null)
                    user.ExpiresAt = null;
                else if (t.Type == JTokenType.String && string.IsNullOrWhiteSpace(t.Value<string>()))
                    user.ExpiresAt = null;
                else
                {
                    var s = t.Type == JTokenType.String ? t.Value<string>() : t.ToString();
                    if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    {
                        errorDetail = "expiresAt: invalid date";
                        return AdminPatchUserOutcome.InvalidPayload;
                    }

                    user.ExpiresAt = dt;
                }
            }

            if (body.ContainsKey("lang"))
            {
                var s = body["lang"]?.Value<string>()?.Trim();
                if (!string.IsNullOrEmpty(s))
                    user.Lang = s;
            }

            if (body.ContainsKey("role"))
            {
                var r = body["role"]?.Value<string>()?.Trim();
                if (string.IsNullOrEmpty(r))
                {
                    errorDetail = "role: empty";
                    return AdminPatchUserOutcome.InvalidPayload;
                }

                if (!string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(r, "user", StringComparison.OrdinalIgnoreCase))
                {
                    errorDetail = "role: use user or admin";
                    return AdminPatchUserOutcome.InvalidPayload;
                }

                user.Role = string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "user";
            }

            if (body["accs"] is JObject accsJo)
            {
                user.Accs ??= new TelegramAccsProfile();
                MergeAccsPatch(user.Accs, accsJo, out var accsErr);
                if (accsErr != null)
                {
                    errorDetail = accsErr;
                    return AdminPatchUserOutcome.InvalidPayload;
                }
            }

            if (body["accsRemove"] is JArray rem)
            {
                user.Accs ??= new TelegramAccsProfile();
                foreach (var x in rem)
                {
                    var key = x.Value<string>()?.Trim();
                    if (!string.IsNullOrEmpty(key))
                        ApplyAccsRemove(user.Accs, key);
                }
            }

            if (body["paramsRemove"] is JArray pr)
            {
                user.Accs ??= new TelegramAccsProfile();
                user.Accs.@params ??= new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var x in pr)
                {
                    var k = x.Value<string>();
                    if (!string.IsNullOrEmpty(k))
                        user.Accs.@params.Remove(k);
                }
            }

            SaveUsers(users);
            ResyncUserDevicesToAccsdb(user);
            return AdminPatchUserOutcome.Ok;
        }

        static void MergeAccsPatch(TelegramAccsProfile accs, JObject jo, out string? error)
        {
            error = null;
            if (jo.ContainsKey("group"))
            {
                var t = jo["group"];
                if (t == null || t.Type == JTokenType.Null)
                    accs.group = null;
                else if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
                    accs.group = (int)t.Value<long>();
                else
                {
                    error = "accs.group: expected integer or null";
                    return;
                }
            }

            if (jo.ContainsKey("IsPasswd"))
            {
                var t = jo["IsPasswd"];
                if (t == null || t.Type == JTokenType.Null)
                    accs.IsPasswd = null;
                else if (t.Type != JTokenType.Boolean)
                {
                    error = "accs.IsPasswd: expected boolean or null";
                    return;
                }
                else
                    accs.IsPasswd = t.Value<bool>();
            }

            if (jo.ContainsKey("ban"))
            {
                var t = jo["ban"];
                if (t == null || t.Type == JTokenType.Null)
                    accs.ban = null;
                else if (t.Type != JTokenType.Boolean)
                {
                    error = "accs.ban: expected boolean or null";
                    return;
                }
                else
                    accs.ban = t.Value<bool>();
            }

            if (jo.ContainsKey("ban_msg"))
            {
                var t = jo["ban_msg"];
                if (t == null || t.Type == JTokenType.Null)
                    accs.ban_msg = null;
                else
                {
                    var s = t.Value<string>()?.Trim();
                    accs.ban_msg = string.IsNullOrEmpty(s) ? null : s;
                }
            }

            if (jo.ContainsKey("comment"))
            {
                var t = jo["comment"];
                if (t == null || t.Type == JTokenType.Null)
                    accs.comment = null;
                else
                {
                    var s = t.Value<string>()?.Trim();
                    accs.comment = string.IsNullOrEmpty(s) ? null : s;
                }
            }

            if (jo.ContainsKey("ids"))
            {
                var arr = jo["ids"] as JArray;
                if (arr == null || arr.Count == 0)
                    accs.ids = null;
                else
                {
                    accs.ids = arr
                        .Select(x => x?.ToString())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            if (jo["params"] is JObject pjo)
            {
                accs.@params ??= new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var prop in pjo.Properties())
                {
                    if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                        accs.@params.Remove(prop.Name);
                    else
                        accs.@params[prop.Name] = prop.Value.Type == JTokenType.String
                            ? (prop.Value.Value<string>() ?? "")
                            : prop.Value.Type == JTokenType.Integer
                                ? prop.Value.Value<long>()
                                : prop.Value.Type == JTokenType.Float
                                    ? prop.Value.Value<double>()
                                    : prop.Value.Type == JTokenType.Boolean
                                        ? prop.Value.Value<bool>()
                                        : prop.Value.ToString(Formatting.None);
                }
            }
        }

        static void ApplyAccsRemove(TelegramAccsProfile accs, string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "group":
                    accs.group = null;
                    break;
                case "ispasswd":
                case "is_passwd":
                case "passwd":
                    accs.IsPasswd = null;
                    break;
                case "ban":
                    accs.ban = null;
                    break;
                case "ban_msg":
                case "banmsg":
                    accs.ban_msg = null;
                    break;
                case "comment":
                    accs.comment = null;
                    break;
                case "ids":
                    accs.ids = null;
                    break;
                case "params":
                    accs.@params = null;
                    break;
            }
        }
    }
}
