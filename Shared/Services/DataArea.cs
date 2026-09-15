using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Shared.Services;

/// <summary>
/// Имя области данных пользователя: сам пользователь плюс, если он есть, профиль.
///
/// Склейка через <c>_</c> была неоднозначной: подчёркивание разрешено и в идентификаторе
/// пользователя, поэтому <c>andrey</c> с <c>profile_id=x</c> попадал ровно туда же, куда пишет
/// пользователь <c>andrey_x</c>, — и читал чужое. Разделитель теперь тот, который вычищается из
/// обеих частей, так что встретиться внутри них он не может.
/// </summary>
public static class DataArea
{
    public const char Separator = ':';

    static readonly Regex forbidden = new("[^a-z0-9\\-_\\.]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Sanitize(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : forbidden.Replace(value, string.Empty);

    public static string Compose(string userId, string profileId)
    {
        string user = Sanitize(userId);

        if (string.IsNullOrEmpty(profileId) || profileId == "0")
            return user;

        string profile = Sanitize(profileId);
        return string.IsNullOrEmpty(profile) ? user : $"{user}{Separator}{profile}";
    }

    /// <summary>
    /// Прежнее имя той же области. Нужно ровно один раз — чтобы переименовать накопленное.
    /// </summary>
    public static string Legacy(string userId, string profileId)
    {
        string user = Sanitize(userId);

        if (string.IsNullOrEmpty(profileId) || profileId == "0")
            return user;

        return Sanitize($"{userId}_{profileId}");
    }

    /// <summary>
    /// Разобрать старое имя на пользователя и профиль.
    ///
    /// Однозначно это делается только по списку известных пользователей, и при совпадении
    /// побеждает самый длинный: если есть и <c>andrey</c>, и <c>andrey_x</c>, то <c>andrey_x</c> —
    /// это пользователь, а не профиль <c>x</c>. Именно так и читал бы accsdb, и именно того,
    /// чьи это данные на самом деле.
    /// </summary>
    public static bool TrySplitLegacy(string legacyUser, IEnumerable<string> knownUsers, out string renamed)
    {
        renamed = null;

        if (string.IsNullOrEmpty(legacyUser) || !legacyUser.Contains('_'))
            return false;

        string best = null;

        foreach (string candidate in knownUsers)
        {
            string user = Sanitize(candidate);
            if (string.IsNullOrEmpty(user) || user.Length >= legacyUser.Length)
                continue;

            if (!legacyUser.StartsWith($"{user}_", StringComparison.Ordinal))
                continue;

            if (best == null || user.Length > best.Length)
                best = user;
        }

        // Совпал целиком с известным пользователем — это он сам, а не чей-то профиль.
        foreach (string candidate in knownUsers)
        {
            if (string.Equals(Sanitize(candidate), legacyUser, StringComparison.Ordinal))
                return false;
        }

        if (best == null)
            return false;

        renamed = $"{best}{Separator}{legacyUser.Substring(best.Length + 1)}";
        return true;
    }

    /// <summary>Идентичности, которые сервер вообще признаёт: список accsdb плюс общая.</summary>
    public static IReadOnlyCollection<string> KnownUsers()
    {
        var users = new List<string>();

        var accsdb = CoreInit.conf.accsdb?.users;
        if (accsdb != null)
        {
            foreach (var user in accsdb)
            {
                if (!string.IsNullOrEmpty(user.id))
                    users.Add(user.id);

                if (user.ids != null)
                    users.AddRange(user.ids);
            }
        }

        string assigned = InstanceIdentity.Assigned;
        if (!string.IsNullOrEmpty(assigned))
            users.Add(assigned);

        return users;
    }
}
