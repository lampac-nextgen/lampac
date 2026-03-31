using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Shared.Models.Base;
using TelegramAuth.Models;

namespace TelegramAuth.Services
{
    internal static class AccsdbUidSync
    {
        const string UsersFileName = "users.json";
        static readonly object FileLock = new object();

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static void UpsertTelegramDevice(string deviceUid, TelegramUserRecord tgUser, DateTime expiresLocal, int groupAdmin, int groupUser)
        {
            if (string.IsNullOrWhiteSpace(deviceUid) || tgUser == null)
                return;

            var row = BuildAccsUserForDevice(deviceUid.Trim(), tgUser, expiresLocal, groupAdmin, groupUser);
            lock (FileLock)
            {
                EnsureFileExistsUnlocked();

                var list = ReadListUnlocked();
                var key = row.id;
                var found = list.FirstOrDefault(u =>
                    (u.id != null && string.Equals(u.id, key, StringComparison.OrdinalIgnoreCase)) ||
                    (u.ids != null && u.ids.Any(id => string.Equals(id, key, StringComparison.OrdinalIgnoreCase))));

                if (found != null)
                {
                    found.id = row.id;
                    found.ids = row.ids ?? new List<string>();
                    found.IsPasswd = row.IsPasswd;
                    found.expires = row.expires;
                    found.group = row.group;
                    found.ban = row.ban;
                    found.ban_msg = row.ban_msg;
                    found.comment = row.comment;
                    found.@params = row.@params;
                }
                else
                    list.Add(row);

                WriteListUnlocked(list);
            }
        }

        static AccsUser BuildAccsUserForDevice(string deviceUid, TelegramUserRecord tg, DateTime expiresLocal, int groupAdmin, int groupUser)
        {
            var accs = tg.Accs;
            var group = accs?.group ?? (string.Equals(tg.Role, "admin", StringComparison.OrdinalIgnoreCase) ? groupAdmin : groupUser);
            var isPasswd = accs?.IsPasswd ?? false;
            var banned = accs?.ban == true;

            var ids = accs?.ids != null && accs.ids.Count > 0
                ? accs.ids.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

            var paramDict = new Dictionary<string, object>(StringComparer.Ordinal);
            if (accs?.@params != null)
            {
                foreach (var kv in accs.@params)
                    paramDict[kv.Key] = kv.Value;
            }

            paramDict["telegram_id"] = tg.TelegramId;
            if (!string.IsNullOrWhiteSpace(tg.TgUsername))
                paramDict["telegram_username"] = tg.TgUsername.Trim();

            string comment;
            if (!string.IsNullOrWhiteSpace(accs?.comment))
                comment = accs.comment.Trim();
            else if (!string.IsNullOrWhiteSpace(tg.TgUsername))
                comment = $"telegram:{tg.TelegramId} @{tg.TgUsername.Trim()}";
            else
                comment = $"telegram:{tg.TelegramId}";

            return new AccsUser
            {
                id = deviceUid,
                ids = ids,
                expires = expiresLocal,
                group = group,
                ban = banned,
                ban_msg = string.IsNullOrWhiteSpace(accs?.ban_msg) ? null : accs.ban_msg.Trim(),
                comment = comment,
                IsPasswd = isPasswd,
                @params = paramDict
            };
        }

        public static void RemoveUid(string deviceUid)
        {
            if (string.IsNullOrWhiteSpace(deviceUid))
                return;

            var key = deviceUid.Trim();
            lock (FileLock)
            {
                if (!File.Exists(Path.GetFullPath(UsersFileName)))
                    return;

                var list = ReadListUnlocked();
                foreach (var u in list)
                {
                    if (u.ids != null && u.ids.Count > 0)
                        u.ids = u.ids.Where(id => !string.Equals(id, key, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                list = list
                    .Where(u => u.id == null || !string.Equals(u.id, key, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                WriteListUnlocked(list);
            }
        }

        static void EnsureFileExistsUnlocked()
        {
            var full = Path.GetFullPath(UsersFileName);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(full))
                File.WriteAllText(full, "[]" + Environment.NewLine);
        }

        static List<AccsUser> ReadListUnlocked()
        {
            try
            {
                var full = Path.GetFullPath(UsersFileName);
                if (!File.Exists(full))
                    return new List<AccsUser>();

                var txt = File.ReadAllText(full);
                if (string.IsNullOrWhiteSpace(txt))
                    return new List<AccsUser>();

                return JsonConvert.DeserializeObject<List<AccsUser>>(txt) ?? new List<AccsUser>();
            }
            catch
            {
                return new List<AccsUser>();
            }
        }

        static void WriteListUnlocked(List<AccsUser> list)
        {
            var full = Path.GetFullPath(UsersFileName);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(full, JsonConvert.SerializeObject(list, JsonSettings));
        }
    }
}
