using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;

namespace NativeAppleTV;

/// <summary>Одноразовые коды входа: приложение просит код, пользователь подтверждает его, приложение забирает токен.</summary>
public static class ChallengeStore
{
    public const int CodeLength = 6;
    // без 0/O/1/I/L: код читают с экрана
    const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public class Challenge
    {
        public DateTime expires;
        public string token;      // появляется после подтверждения
        public DateTime lastPoll;
    }

    static readonly ConcurrentDictionary<string, Challenge> challenges = new();
    static readonly ConcurrentDictionary<string, (int fails, DateTime until)> failures = new();

    public static string Normalize(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    public static string Create(int ttlSeconds)
    {
        foreach (var old in challenges.Where(i => i.Value.expires < DateTime.UtcNow).Select(i => i.Key).ToList())
            challenges.TryRemove(old, out _);

        // защита от переполнения словаря анонимными запросами
        if (challenges.Count > 5000)
            return null;

        string code;
        do
        {
            code = new string(Enumerable.Range(0, CodeLength).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
        }
        while (!challenges.TryAdd(code, new Challenge { expires = DateTime.UtcNow.AddSeconds(ttlSeconds) }));

        return code;
    }

    public static bool TryGet(string code, out Challenge item)
    {
        if (challenges.TryGetValue(Normalize(code), out item) && item.expires >= DateTime.UtcNow)
            return true;

        item = null;
        return false;
    }

    public static void Remove(string code) => challenges.TryRemove(Normalize(code), out _);

    /// <summary>Не больше 5 неверных паролей/кодов за 10 минут с одного IP.</summary>
    public static bool IsBlocked(string ip)
        => failures.TryGetValue(ip ?? string.Empty, out var f) && f.fails >= 5 && f.until > DateTime.UtcNow;

    public static void Fail(string ip)
    {
        ip ??= string.Empty;
        failures.AddOrUpdate(ip,
            _ => (1, DateTime.UtcNow.AddMinutes(10)),
            (_, f) => f.until < DateTime.UtcNow ? (1, DateTime.UtcNow.AddMinutes(10)) : (f.fails + 1, DateTime.UtcNow.AddMinutes(10)));
    }

    public static void ResetFails(string ip) => failures.TryRemove(ip ?? string.Empty, out _);
}
