using System;
using System.Security.Cryptography;

namespace OidcAuth.Services
{
    /// <summary>
    /// Random uid (base36, 12 chars) — never an email or anything guessable.
    /// Passes ValidateIdentity (a-z0-9) and becomes the AccsUser.id in users.json.
    /// </summary>
    internal static class UidGenerator
    {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

        public static string New(Func<string, bool> exists)
        {
            Span<byte> bytes = stackalloc byte[12];
            Span<char> chars = stackalloc char[12];

            for (int attempt = 0; attempt < 10; attempt++)
            {
                RandomNumberGenerator.Fill(bytes);

                for (int i = 0; i < bytes.Length; i++)
                    chars[i] = Alphabet[bytes[i] % Alphabet.Length];

                string uid = new(chars);
                if (exists == null || !exists(uid))
                    return uid;
            }

            throw new InvalidOperationException("failed to generate unique uid");
        }
    }
}
