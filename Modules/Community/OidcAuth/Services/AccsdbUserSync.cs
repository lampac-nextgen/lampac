using Newtonsoft.Json;
using OidcAuth.Models;
using Shared;
using Shared.Models.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OidcAuth.Services
{
    /// <summary>
    /// Maps OIDC identities to accsdb users.
    /// Linking key is the IdP sub, stored in ids[] / params.oidc_sub of a users.json row.
    /// New identities always get a random uid (never an email).
    /// </summary>
    internal static class AccsdbUserSync
    {
        const string UsersFileName = "users.json";

        static readonly object FileLock = new();

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>Returns the user uid and whether the row was just created</summary>
        public static (string uid, bool created) ResolveOrRegister(OidcAuthConf conf, string sub, string? email, string? username)
        {
            if (string.IsNullOrWhiteSpace(sub))
                throw new InvalidOperationException("empty sub");

            sub = sub.Trim();

            lock (FileLock)
            {
                List<AccsUser> list = ReadListUnlocked();

                AccsUser? found = FindBySub(list, sub);
                if (found != null)
                {
                    bool dirty = EnsureLinkedParams(found, sub, email, username);
                    if (dirty)
                        WriteListUnlocked(list);

                    return (found.id, false);
                }

                string uid = UidGenerator.New(candidate =>
                    list.Any(u => u != null && (
                        string.Equals(u.id, candidate, StringComparison.OrdinalIgnoreCase) ||
                        (u.ids != null && u.ids.Any(i => string.Equals(i, candidate, StringComparison.OrdinalIgnoreCase))))) ||
                    CoreInit.conf.accsdb.findUser(candidate) != null);

                list.Add(new AccsUser
                {
                    id = uid,
                    ids = new List<string> { sub },
                    group = conf.defaultGroup,
                    expires = conf.defaultExpiresDays > 0
                        ? DateTime.Now.AddDays(conf.defaultExpiresDays)
                        : DateTime.MaxValue,
                    comment = "oidc:" + (FirstNonEmpty(email, username) ?? sub),
                    @params = BuildParams(sub, email, username)
                });

                WriteListUnlocked(list);

                AppendUnlinkedLog(conf, sub, email, username, uid);

                return (uid, true);
            }
        }

        static AccsUser? FindBySub(List<AccsUser> list, string sub)
        {
            foreach (AccsUser u in list)
            {
                if (u == null)
                    continue;

                if (u.ids != null && u.ids.Any(i => string.Equals(i, sub, StringComparison.OrdinalIgnoreCase)))
                    return u;

                if (u.@params != null &&
                    u.@params.TryGetValue("oidc_sub", out object? p) &&
                    string.Equals(p?.ToString(), sub, StringComparison.OrdinalIgnoreCase))
                {
                    return u;
                }
            }

            return null;
        }

        /// <summary>Fills in oidc metadata (e.g. for a row linked manually via ids)</summary>
        static bool EnsureLinkedParams(AccsUser user, string sub, string? email, string? username)
        {
            user.@params ??= new Dictionary<string, object>();

            bool dirty = false;

            if (!user.@params.ContainsKey("oidc_sub"))
            {
                user.@params["oidc_sub"] = sub;
                dirty = true;
            }

            if (!string.IsNullOrWhiteSpace(email) && !user.@params.ContainsKey("oidc_email"))
            {
                user.@params["oidc_email"] = email.Trim();
                dirty = true;
            }

            if (!string.IsNullOrWhiteSpace(username) && !user.@params.ContainsKey("oidc_username"))
            {
                user.@params["oidc_username"] = username.Trim();
                dirty = true;
            }

            return dirty;
        }

        static Dictionary<string, object> BuildParams(string sub, string? email, string? username)
        {
            var p = new Dictionary<string, object>
            {
                ["oidc_sub"] = sub
            };

            if (!string.IsNullOrWhiteSpace(email))
                p["oidc_email"] = email.Trim();

            if (!string.IsNullOrWhiteSpace(username))
                p["oidc_username"] = username.Trim();

            return p;
        }

        /// <summary>Log of created rows — source of sub for manual linking of older users</summary>
        static void AppendUnlinkedLog(OidcAuthConf conf, string sub, string? email, string? username, string uid)
        {
            if (!conf.unlinkedLog)
                return;

            try
            {
                Directory.CreateDirectory("logs/oidc");
                File.AppendAllText(
                    Path.Combine("logs", "oidc", "unlinked.log"),
                    $"{DateTime.Now:dd-MM-yyyy HH:mm:ss}\tsub={sub}\temail={email}\tusername={username}\tuid={uid}\n");
            }
            catch { }
        }

        static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        static List<AccsUser> ReadListUnlocked()
        {
            try
            {
                string full = Path.GetFullPath(UsersFileName);
                if (!File.Exists(full))
                    return new List<AccsUser>();

                string txt = File.ReadAllText(full);
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
            string full = Path.GetFullPath(UsersFileName);
            string? dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(full, JsonConvert.SerializeObject(list, JsonSettings));
        }
    }
}
