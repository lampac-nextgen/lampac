using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Shared;
using TelegramAuth.Models;

namespace TelegramAuth.Services
{
    public partial class TelegramAuthStore
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

        public void EnsureOwnerUsersAtStartup()
        {
            var ids = _conf.owner_telegram_ids;
            if (ids == null || ids.Length == 0)
                return;

            var users = GetUsers();
            var changed = false;
            var lang = string.IsNullOrWhiteSpace(_conf.auto_provision_lang) ? "ru" : _conf.auto_provision_lang.Trim();

            foreach (var ownerNumericId in ids)
            {
                var telegramId = ownerNumericId.ToString(CultureInfo.InvariantCulture);
                var user = users.FirstOrDefault(u => string.Equals(u.TelegramId, telegramId, StringComparison.Ordinal));
                if (user == null)
                {
                    users.Add(new TelegramUserRecord
                    {
                        TelegramId = telegramId,
                        TgUsername = "",
                        Role = "admin",
                        Lang = lang,
                        CreatedAt = DateTime.UtcNow,
                        ApprovedBy = "owner-config",
                        Disabled = false,
                        Devices = new List<DeviceRecord>()
                    });
                    changed = true;
                    continue;
                }

                if (!string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    user.Role = "admin";
                    changed = true;
                }

                if (user.Disabled)
                {
                    user.Disabled = false;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(user.ApprovedBy))
                {
                    user.ApprovedBy = "owner-config";
                    changed = true;
                }
            }

            if (changed)
                SaveUsers(users);
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

        public static bool IsRegistrationPending(TelegramUserRecord? user) => user?.RegistrationPending == true;

        public bool IsActive(TelegramUserRecord user)
        {
            if (user == null)
                return false;

            if (IsRegistrationPending(user))
                return false;

            if (user.Disabled)
                return false;

            return !user.ExpiresAt.HasValue || user.ExpiresAt.Value.ToUniversalTime() >= DateTime.UtcNow;
        }

        bool ShouldSyncAccsdb() =>
            _conf.enable &&
            CoreInit.conf != null && CoreInit.conf.accsdb.enable;

        void RemoveUidFromAccsdbIfSync(string deviceUid)
        {
            if (!ShouldSyncAccsdb() || string.IsNullOrWhiteSpace(deviceUid))
                return;
            AccsdbUidSync.RemoveUid(deviceUid.Trim());
        }

        void BumpOldestActiveDeviceIfAtDeviceLimit(TelegramUserRecord user)
        {
            var maxDevices = GetMaxDevices(user);
            if (maxDevices <= 0 || user.Devices == null)
                return;

            var activeDevices = user.Devices.Where(d => d.Active).OrderByDescending(d => d.LastSeenAt ?? d.LinkedAt).ToList();
            if (activeDevices.Count < maxDevices)
                return;

            var oldestActive = activeDevices.Last();
            oldestActive.Active = false;
            RemoveUidFromAccsdbIfSync(oldestActive.Uid);
        }

        DateTime ResolveAccsdbExpiresLocal(TelegramUserRecord user)
        {
            if (user.ExpiresAt.HasValue)
            {
                var v = user.ExpiresAt.Value;
                return v.Kind == DateTimeKind.Utc ? v.ToLocalTime() : v;
            }

            var sd = CoreInit.conf?.accsdb?.shared_daytime ?? 0;
            var days = sd > 0 ? Math.Max(1, sd) : 365;
            return DateTime.Now.AddDays(days);
        }

        void SyncDeviceToAccsdb(TelegramUserRecord user, string deviceUid)
        {
            if (string.IsNullOrWhiteSpace(deviceUid) || user == null)
                return;

            if (!ShouldSyncAccsdb())
                return;

            var u = deviceUid.Trim();

            if (user.Disabled || IsRegistrationPending(user))
            {
                AccsdbUidSync.RemoveUid(u);
                return;
            }

            AccsdbUidSync.UpsertTelegramDevice(
                u,
                user,
                ResolveAccsdbExpiresLocal(user),
                _conf.accsdb_sync_group_admin,
                _conf.accsdb_sync_group_user);
        }

        public void ResyncUserDevicesToAccsdb(TelegramUserRecord user)
        {
            if (user == null || !ShouldSyncAccsdb())
                return;

            if (user.Disabled || IsRegistrationPending(user))
            {
                foreach (var d in user.Devices ?? Enumerable.Empty<DeviceRecord>())
                {
                    if (!string.IsNullOrWhiteSpace(d.Uid))
                        AccsdbUidSync.RemoveUid(d.Uid.Trim());
                }

                return;
            }

            foreach (var d in user.Devices ?? Enumerable.Empty<DeviceRecord>())
            {
                if (d.Active && !string.IsNullOrWhiteSpace(d.Uid))
                    SyncDeviceToAccsdb(user, d.Uid.Trim());
            }
        }

        void SyncAccsdbForBind(TelegramUserRecord user, string? deviceUid)
        {
            if (!ShouldSyncAccsdb() || string.IsNullOrWhiteSpace(deviceUid))
                return;

            var u = deviceUid.Trim();

            if (user.Disabled || IsRegistrationPending(user))
            {
                AccsdbUidSync.RemoveUid(u);
                return;
            }

            if (!IsActive(user))
                return;

            var dev = user.Devices?.FirstOrDefault(d => string.Equals(d.Uid, u, StringComparison.OrdinalIgnoreCase));
            if (dev == null || !dev.Active)
                return;

            SyncDeviceToAccsdb(user, u);
        }

        const string RegistrationPendingApprovedByMarker = "registration-pending";

        static bool LegacyApprovedByIsRegistrationPending(string? approvedBy)
        {
            if (string.IsNullOrWhiteSpace(approvedBy))
                return false;
            return string.Equals(approvedBy.Trim(), RegistrationPendingApprovedByMarker, StringComparison.OrdinalIgnoreCase);
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
                        var pending = LegacyApprovedByIsRegistrationPending(item.approved_by);
                        var isAdmin = adminIds.Contains(item.telegram_id);
                        if (isAdmin)
                            pending = false;

                        var approvedBy = string.IsNullOrWhiteSpace(item.approved_by)
                            ? "legacy-import"
                            : item.approved_by.Trim();

                        user = new TelegramUserRecord
                        {
                            TelegramId = item.telegram_id,
                            TgUsername = item.tg_username ?? "",
                            ApprovedBy = approvedBy,
                            CreatedAt = item.created_at,
                            ExpiresAt = item.expires_at,
                            Lang = langs.TryGetValue(item.telegram_id, out var lang) ? lang : "ru",
                            Role = isAdmin ? "admin" : "user",
                            Disabled = pending,
                            RegistrationPending = pending,
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
