#nullable enable
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace QRAuth.Services
{
    /// <summary>
    /// Short-lived in-memory pairing sessions for the deny-page QR login flow:
    /// the deny page requests a session id, encodes it into the bot deep-link QR
    /// (<c>https://t.me/&lt;bot&gt;?start=qr_&lt;id&gt;</c>), the user confirms in Telegram
    /// (BotSession.HandleQrStartAsync/HandleCallbackAsync "qrauth:" case), and the
    /// deny page polls until it can read back the user's existing Lampac token —
    /// then logs in with it exactly like a manually typed password.
    /// Process-local by design: a bot restart just invalidates in-flight sessions,
    /// same as the deny page's own 2-3 minute QR lifetime.
    /// </summary>
    public static class QrAuthSessions
    {
        class Entry
        {
            public bool Confirmed;
            public string? Token;
            public DateTime ExpiresAt;
        }

        static readonly TimeSpan Ttl = TimeSpan.FromMinutes(3);
        static readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);

        public static string Create()
        {
            CleanupExpired();

            string id;
            do { id = RandomId(); } while (!_sessions.TryAdd(id, new Entry { ExpiresAt = DateTime.UtcNow + Ttl }));
            return id;
        }

        /// <summary>Called from the bot's "✅ Подтвердить вход" callback once the token is known.</summary>
        public static bool TryConfirm(string sessionId, string token)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var e))
                return false;

            if (e.ExpiresAt < DateTime.UtcNow)
            {
                _sessions.TryRemove(sessionId, out _);
                return false;
            }

            e.Confirmed = true;
            e.Token = token;
            return true;
        }

        /// <summary>
        /// Polled by the deny page. Removes the session on the confirmed read (one-time
        /// handoff) so a captured status response can't be replayed to log in again later.
        /// </summary>
        public static (string status, string? token) ConsumeIfConfirmed(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var e))
                return ("expired", null);

            if (e.ExpiresAt < DateTime.UtcNow)
            {
                _sessions.TryRemove(sessionId, out _);
                return ("expired", null);
            }

            if (!e.Confirmed)
                return ("pending", null);

            _sessions.TryRemove(sessionId, out _);
            return ("confirmed", e.Token);
        }

        static void CleanupExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _sessions)
            {
                if (kv.Value.ExpiresAt < now)
                    _sessions.TryRemove(kv.Key, out _);
            }
        }

        static string RandomId()
        {
            Span<byte> bytes = stackalloc byte[9];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
