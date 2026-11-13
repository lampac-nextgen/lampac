using Newtonsoft.Json;
using Shared;
using Shared.Models.Base;
using System;
using System.Collections.Generic;
using System.IO;
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

    public static string Issue(int days, int group, string device)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var user = new AccsUser
        {
            id = token,
            expires = DateTime.Now.AddDays(Math.Clamp(days, 1, 3650)),
            group = group,
            comment = "NativeAppleTV " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            @params = new Dictionary<string, object>
            {
                ["source"] = "nativeappletv",
                ["device"] = device ?? string.Empty
            }
        };

        lock (fileLock)
        {
            var list = new List<AccsUser>();
            if (File.Exists(UsersFileName))
            {
                try { list = JsonConvert.DeserializeObject<List<AccsUser>>(File.ReadAllText(UsersFileName)) ?? list; }
                catch { /* битый файл не затираем: выдаём токен только в память */
                    AddToMemory(user);
                    return token;
                }
            }

            list.Add(user);

            string tmp = UsersFileName + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(list, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            File.Move(tmp, UsersFileName, overwrite: true);
        }

        AddToMemory(user);
        return token;
    }

    static void AddToMemory(AccsUser user)
    {
        var accsdb = CoreInit.conf.accsdb;
        accsdb.users.Add(user);
        accsdb.RefreshUsers();
    }
}
