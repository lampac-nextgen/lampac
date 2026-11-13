using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Shared;
using Shared.Models.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NativeAppleTV;

/// <summary>
/// Выдача токена = строка пользователя в users.json (переживает перезапуск; Core подхватывает файл сам)
/// плюс немедленная запись в accsdb.users, чтобы токен заработал без ожидания.
/// Дальше срок, бан и лимиты проверяет штатный accsdb по ?token=.
/// </summary>
public static class TokenStore
{
    const string UsersFileName = "users.json";
    static readonly object fileLock = new();

    /// <summary>
    /// Без user: каждый токен — отдельный пользователь. С user: токен добавляется в ids этого пользователя
    /// (создаётся, если его нет), и все приставки делят закладки, таймкоды, срок и бан.
    /// </summary>
    public static string Issue(int days, int group, string device, string userId = null)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        string owner = NormalizeUser(userId);
        DateTime expires = DateTime.Now.AddDays(Math.Clamp(days, 1, 3650));
        string comment = "NativeAppleTV " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var meta = new Dictionary<string, object> { ["source"] = "nativeappletv", ["device"] = device ?? string.Empty };

        lock (fileLock)
        {
            var list = new List<AccsUser>();
            bool fileOk = true;
            if (File.Exists(UsersFileName))
            {
                try { list = JsonConvert.DeserializeObject<List<AccsUser>>(File.ReadAllText(UsersFileName)) ?? list; }
                catch { fileOk = false; } // битый файл не затираем: выдаём токен только в память
            }

            AccsUser memUser = null;
            AccsUser fileUser = null;

            if (owner == null)
            {
                memUser = fileUser = new AccsUser { id = token, expires = expires, group = group, comment = comment, @params = meta };
                list.Add(fileUser);
            }
            else
            {
                memUser = CoreInit.conf.accsdb.findUser(new StringValues(owner));
                fileUser = list.FirstOrDefault(u => Matches(u, owner));

                if (fileUser == null)
                {
                    // пользователь есть только в init.conf/accsdb.accounts или его нет совсем — заводим запись в файле
                    fileUser = memUser != null
                        ? new AccsUser { id = memUser.id, ids = new List<string>(memUser.ids ?? []), IsPasswd = memUser.IsPasswd, expires = memUser.expires, group = memUser.group, bypass_accsdb = memUser.bypass_accsdb, ban = memUser.ban, ban_msg = memUser.ban_msg, comment = memUser.comment, @params = memUser.@params }
                        : new AccsUser { id = owner, expires = expires, group = group, comment = comment, @params = meta };
                    list.Add(fileUser);
                }

                fileUser.ids ??= new List<string>();
                fileUser.ids.Add(token);

                if (memUser == null)
                    memUser = fileUser;
                else
                    (memUser.ids ??= new List<string>()).Add(token);
            }

            if (fileOk)
            {
                string tmp = UsersFileName + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(list, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
                File.Move(tmp, UsersFileName, overwrite: true);
            }

            RegisterInMemory(memUser);
        }

        return token;
    }

    /// <summary>Идентификатор пользователя как в accsdb: без пробелов, в нижнем регистре. Пусто — отдельный пользователь на токен.</summary>
    static string NormalizeUser(string userId)
    {
        string u = (userId ?? string.Empty).Trim().ToLowerInvariant();
        return u.Length == 0 || u.Length > 128 ? null : u;
    }

    static bool Matches(AccsUser u, string owner)
        => string.Equals(u?.id, owner, StringComparison.OrdinalIgnoreCase)
        || (u?.ids?.Any(i => string.Equals(i, owner, StringComparison.OrdinalIgnoreCase)) ?? false);

    static void RegisterInMemory(AccsUser user)
    {
        var accsdb = CoreInit.conf.accsdb;
        if (!accsdb.users.Contains(user))
            accsdb.users.Add(user);
        accsdb.RefreshUsers();
    }
}
