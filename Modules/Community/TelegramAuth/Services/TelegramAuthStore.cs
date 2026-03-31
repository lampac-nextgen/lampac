using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TelegramAuth.Models;

namespace TelegramAuth.Services
{
    public class TelegramAuthStore
    {
        readonly TelegramAuthConf _conf;
        readonly string baseDir;
        readonly string usersPath;
        readonly string adminsPath;
        readonly string langsPath;
        public const int MaxActiveDevicesPerUser = 5;
        public const int AdminUnlimitedDevices = -1;

        public TelegramAuthStore(TelegramAuthConf conf)
        {
            _conf = conf ?? new TelegramAuthConf();
            var rel = string.IsNullOrWhiteSpace(_conf.data_dir)
                ? Path.Combine("database", "tgauth")
                : _conf.data_dir.Trim().TrimStart('/', '\\');
            baseDir = Path.IsPathRooted(rel)
                ? rel
                : Path.Combine(AppContext.BaseDirectory, rel);
            usersPath = Path.Combine(baseDir, "users.json");
            adminsPath = Path.Combine(baseDir, "admins.json");
            langsPath = Path.Combine(baseDir, "user_langs.json");
        }

        public void EnsureStorage()
        {
            Directory.CreateDirectory(baseDir);
            if (!File.Exists(usersPath)) File.WriteAllText(usersPath, "[]");
            if (!File.Exists(adminsPath)) File.WriteAllText(adminsPath, "[]");
            if (!File.Exists(langsPath)) File.WriteAllText(langsPath, "{}");
        }

        public List<TelegramUserRecord> GetUsers()
        {
            EnsureStorage();
            return JsonConvert.DeserializeObject<List<TelegramUserRecord>>(File.ReadAllText(usersPath)) ?? new List<TelegramUserRecord>();
        }

        public void SaveUsers(List<TelegramUserRecord> users)
        {
            EnsureStorage();
            File.WriteAllText(usersPath, JsonConvert.SerializeObject(users, Formatting.Indented));
        }

        public Dictionary<string, string> GetLangs()
        {
            EnsureStorage();
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(langsPath)) ?? new Dictionary<string, string>();
        }

        public TelegramUserRecord? FindByUid(string uid)
        {
            if (string.IsNullOrWhiteSpace(uid)) return null;
            return GetUsers().FirstOrDefault(u => u.Devices.Any(d => d.Active && string.Equals(d.Uid, uid, StringComparison.OrdinalIgnoreCase)));
        }

        public TelegramUserRecord? FindByTelegramId(string telegramId)
        {
            if (string.IsNullOrWhiteSpace(telegramId)) return null;
            return GetUsers().FirstOrDefault(u => string.Equals(u.TelegramId, telegramId, StringComparison.Ordinal));
        }

        public bool IsActive(TelegramUserRecord user)
        {
            if (user == null)
                return false;

            return !user.ExpiresAt.HasValue || user.ExpiresAt.Value.ToUniversalTime() >= DateTime.UtcNow;
        }

        public int GetMaxDevices(TelegramUserRecord user)
        {
            if (user != null && string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return AdminUnlimitedDevices;

            int max = _conf.max_active_devices_per_user;
            return max > 0 ? max : MaxActiveDevicesPerUser;
        }

        public const string BindUserNotFoundMessage = "user not found";

        public void BindDevice(string telegramId, string uid, string? name = null, string source = "manual")
        {
            var users = GetUsers();
            var user = users.FirstOrDefault(u => u.TelegramId == telegramId);
            if (user == null)
            {
                if (!_conf.auto_provision_users)
                    throw new InvalidOperationException(BindUserNotFoundMessage);

                DateTime? expires = null;
                var days = _conf.auto_provision_expires_days;
                if (days > 0)
                    expires = DateTime.UtcNow.AddDays(days);

                var role = string.IsNullOrWhiteSpace(_conf.auto_provision_role)
                    ? "user"
                    : _conf.auto_provision_role.Trim();

                user = new TelegramUserRecord
                {
                    TelegramId = telegramId,
                    TgUsername = name?.Trim() ?? "",
                    Role = role,
                    Lang = string.IsNullOrWhiteSpace(_conf.auto_provision_lang) ? "ru" : _conf.auto_provision_lang.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expires,
                    ApprovedBy = "auto-provision",
                    Devices = new List<DeviceRecord>()
                };
                users.Add(user);
            }

            CleanupInactiveDevices(users, 90);

            var existing = user.Devices.FirstOrDefault(d => string.Equals(d.Uid, uid, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var activeDevices = user.Devices.Where(d => d.Active).OrderByDescending(d => d.LastSeenAt ?? d.LinkedAt).ToList();
                var maxDevices = GetMaxDevices(user);
                if (maxDevices > 0 && activeDevices.Count >= maxDevices)
                {
                    var oldestActive = activeDevices.Last();
                    oldestActive.Active = false;
                }

                user.Devices.Add(new DeviceRecord
                {
                    Uid = uid,
                    Name = name,
                    LinkedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                    Active = true,
                    Source = source
                });
            }
            else
            {
                existing.Active = true;
                existing.Name = name ?? existing.Name;
                existing.LastSeenAt = DateTime.UtcNow;
            }

            SaveUsers(users);
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
            var expired = user.ExpiresAt.HasValue && user.ExpiresAt.Value.ToUniversalTime() < now;
            return new AuthStatusResponse
            {
                Authorized = !expired,
                Pending = false,
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
                            extra.Active = false;
                    }
                }

                removed += before - user.Devices.Count;
            }

            SaveUsers(users);
            return removed;
        }

        public ImportResult ImportFromLegacy(string legacyBasePath)
        {
            EnsureStorage();

            var legacyTokensPath = Path.Combine(legacyBasePath, "tokens.json");
            var legacyAdminsPath = Path.Combine(legacyBasePath, "admin_ids.json");
            var legacyLangsPath = Path.Combine(legacyBasePath, "user_langs.json");

            var users = new Dictionary<string, TelegramUserRecord>(StringComparer.Ordinal);
            var langs = new Dictionary<string, string>(StringComparer.Ordinal);
            var result = new ImportResult();

            if (File.Exists(legacyLangsPath))
            {
                langs = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(legacyLangsPath))
                    ?? new Dictionary<string, string>(StringComparer.Ordinal);
                result.ImportedLangs = langs.Count;
            }

            var adminIds = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(legacyAdminsPath))
            {
                var rawAdmins = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(File.ReadAllText(legacyAdminsPath))
                    ?? new List<Dictionary<string, object>>();

                foreach (var row in rawAdmins)
                {
                    if (row.TryGetValue("telegram_id", out var v) && v != null)
                        adminIds.Add(v.ToString() ?? "");
                }
                result.ImportedAdmins = adminIds.Count;
            }

            if (File.Exists(legacyTokensPath))
            {
                var raw = JsonConvert.DeserializeObject<List<LegacyTokenRecord>>(File.ReadAllText(legacyTokensPath))
                    ?? new List<LegacyTokenRecord>();

                foreach (var item in raw)
                {
                    if (string.IsNullOrWhiteSpace(item.telegram_id))
                        continue;

                    if (!users.TryGetValue(item.telegram_id, out var user))
                    {
                        user = new TelegramUserRecord
                        {
                            TelegramId = item.telegram_id,
                            TgUsername = item.tg_username ?? "",
                            ApprovedBy = item.approved_by,
                            CreatedAt = item.created_at,
                            ExpiresAt = item.expires_at,
                            Lang = langs.TryGetValue(item.telegram_id, out var lang) ? lang : "ru",
                            Role = adminIds.Contains(item.telegram_id) ? "admin" : "user",
                            Devices = new List<DeviceRecord>()
                        };
                        users[item.telegram_id] = user;
                    }

                    foreach (var device in item.devices ?? new List<LegacyDeviceRecord>())
                    {
                        if (device == null || string.IsNullOrWhiteSpace(device.uid))
                            continue;

                        if (user.Devices.Any(d => string.Equals(d.Uid, device.uid, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        DateTime linkedAt = item.created_at ?? DateTime.UtcNow;
                        if (!string.IsNullOrWhiteSpace(device.bound_at))
                            DateTime.TryParse(device.bound_at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out linkedAt);

                        DateTime? lastSeenAt = null;
                        if (!string.IsNullOrWhiteSpace(device.last_seen) && DateTime.TryParse(device.last_seen, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedLastSeen))
                            lastSeenAt = parsedLastSeen;

                        user.Devices.Add(new DeviceRecord
                        {
                            Uid = device.uid,
                            Name = device.label,
                            LinkedAt = linkedAt,
                            LastSeenAt = lastSeenAt,
                            Active = true,
                            Source = "legacy-import"
                        });
                        result.ImportedDevices++;
                    }
                }

                result.ImportedUsers = users.Count;
            }

            SaveUsers(users.Values.OrderBy(u => u.TelegramId, StringComparer.Ordinal).ToList());
            File.WriteAllText(langsPath, JsonConvert.SerializeObject(langs, Formatting.Indented));
            File.WriteAllText(adminsPath, JsonConvert.SerializeObject(adminIds.OrderBy(x => x, StringComparer.Ordinal).ToList(), Formatting.Indented));

            return result;
        }
    }
}
