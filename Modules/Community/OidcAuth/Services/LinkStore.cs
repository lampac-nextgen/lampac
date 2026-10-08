using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace OidcAuth.Services
{
    /// <summary>
    /// Pending QR-login codes: code -> bound uid.
    /// Created by the TV, bound to an OIDC identity on the phone, then consumed by the TV.
    /// In-memory, TTL 10 minutes. A code is deleted as soon as its uid is read once.
    /// </summary>
    internal static class LinkStore
    {
        const int LifetimeMinutes = 10;

        static readonly object _sync = new();
        static readonly Dictionary<string, (DateTimeOffset expires, string? uid)> _codes = new();

        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string Create()
        {
            Span<byte> bytes = stackalloc byte[6];
            var chars = new char[6];

            string code;
            lock (_sync)
            {
                Sweep();
                do
                {
                    RandomNumberGenerator.Fill(bytes);
                    for (int i = 0; i < bytes.Length; i++)
                        chars[i] = Alphabet[bytes[i] % Alphabet.Length];
                    code = new string(chars);
                }
                while (_codes.ContainsKey(code));

                _codes.Add(code, (DateTimeOffset.UtcNow.AddMinutes(LifetimeMinutes), null));
            }
            return code;
        }

        public static bool Exists(string code)
        {
            lock (_sync)
                return !string.IsNullOrEmpty(code) && _codes.TryGetValue(code, out var entry) &&
                       entry.expires > DateTimeOffset.UtcNow && entry.uid == null;
        }

        public static bool Bind(string code, string uid)
        {
            lock (_sync)
            {
                if (!Exists(code) || string.IsNullOrEmpty(uid))
                    return false;

                var entry = _codes[code];
                _codes[code] = (entry.expires, uid);
                return true;
            }
        }

        public static string? Take(string code)
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(code) || !_codes.TryGetValue(code, out var entry))
                    return null;

                if (entry.expires <= DateTimeOffset.UtcNow)
                {
                    _codes.Remove(code);
                    return null;
                }

                if (entry.uid != null)
                    _codes.Remove(code);
                return entry.uid;
            }
        }

        static void Sweep()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var expired = new List<string>();
            foreach (var kv in _codes)
                if (kv.Value.expires <= now)
                    expired.Add(kv.Key);
            foreach (string code in expired)
                _codes.Remove(code);
        }
    }
}
