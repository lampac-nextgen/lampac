using System.Security.Cryptography;
using System.Text;

namespace Shared.Services.Pools;

public static class AesPool
{
    static byte[] aesKey, legacyIV;

    [ThreadStatic]
    private static AesInstance _instance;

    static AesPool()
    {
        if (File.Exists("cache/aeskey"))
        {
            string raw = File.ReadAllText("cache/aeskey").Trim();
            int slash = raw.IndexOf('/');
            if (slash > 0)
            {
                // legacy file: ascii key/iv. Keep the key; IV is decrypt-only for in-flight tokens.
                aesKey = Encoding.UTF8.GetBytes(raw[..slash]);
                string iv = raw[(slash + 1)..];
                if (iv.Length > 0)
                    legacyIV = Encoding.UTF8.GetBytes(iv);
            }
            else
            {
                aesKey = Convert.FromHexString(raw);
            }
        }
        else
        {
            aesKey = RandomNumberGenerator.GetBytes(16);
            Directory.CreateDirectory("cache");
            File.WriteAllText("cache/aeskey", Convert.ToHexString(aesKey));
        }

        SelfCheck();
    }

    public static int Seal(Aes aes, ReadOnlySpan<byte> plain, Span<byte> dest)
    {
        Span<byte> iv = stackalloc byte[16];
        RandomNumberGenerator.Fill(iv);
        int n = aes.EncryptCbc(plain, iv, dest.Slice(16), PaddingMode.PKCS7);
        iv.CopyTo(dest);
        return 16 + n;
    }

    public static int Open(Aes aes, ReadOnlySpan<byte> blob, Span<byte> dest)
        => Open(aes, blob, dest, legacyIV);

    static int Open(Aes aes, ReadOnlySpan<byte> blob, Span<byte> dest, byte[] legacy)
    {
        if (blob.Length > 16 && ((blob.Length - 16) & 15) == 0)
        {
            try
            {
                int n = aes.DecryptCbc(blob.Slice(16), blob.Slice(0, 16), dest, PaddingMode.PKCS7);
                if (n > 0)
                    return n;
            }
            catch (CryptographicException) { }
        }

        if (legacy != null && blob.Length >= 16 && (blob.Length & 15) == 0)
        {
            try
            {
                int n = aes.DecryptCbc(blob, legacy, dest, PaddingMode.PKCS7);
                if (n > 0)
                    return n;
            }
            catch (CryptographicException) { }
        }

        return -1;
    }

    static void SelfCheck()
    {
        using var aes = Aes.Create();
        aes.Key = aesKey;

        ReadOnlySpan<byte> plain = "iv-check"u8;
        Span<byte> a = stackalloc byte[48];
        Span<byte> b = stackalloc byte[48];
        int na = Seal(aes, plain, a);
        int nb = Seal(aes, plain, b);
        if (na <= 16 || na != nb || a[..na].SequenceEqual(b[..nb]))
            throw new InvalidOperationException("AesPool self-check failed");

        Span<byte> pa = stackalloc byte[32];
        Span<byte> pb = stackalloc byte[32];
        if (Open(aes, a[..na], pa, null) != plain.Length || !pa[..plain.Length].SequenceEqual(plain)
            || Open(aes, b[..nb], pb, null) != plain.Length || !pb[..plain.Length].SequenceEqual(plain))
            throw new InvalidOperationException("AesPool self-check failed");

        Span<byte> iv = stackalloc byte[16];
        iv.Fill(0xA5);
        Span<byte> ct = stackalloc byte[32];
        int cn = aes.EncryptCbc(plain, iv, ct, PaddingMode.PKCS7);
        Span<byte> opened = stackalloc byte[32];
        int dn = Open(aes, ct[..cn], opened, iv.ToArray());
        if (dn != plain.Length || !opened[..dn].SequenceEqual(plain))
            throw new InvalidOperationException("AesPool self-check failed");
    }

    public static AesInstance Instance
        => _instance ??= new AesInstance(aesKey);
}


public class AesInstance
{
    public AesInstance(byte[] aesKey)
    {
        Aes = Aes.Create();
        Aes.Mode = CipherMode.CBC;
        Aes.Padding = PaddingMode.PKCS7;
        Aes.Key = aesKey;
    }

    public readonly Aes Aes;

    public const int CharSize = 4096;
    public const int ByteSize = 16 * 1024;
    public const int BlockSize = 16;

    private char[] _charBuffer;
    private byte[] _byteBuffer;
    private byte[] _destBuffer;

    public char[] CharBuffer
        => _charBuffer ??= new char[CharSize];

    public byte[] ByteBuffer
        => _byteBuffer ??= new byte[ByteSize];

    public byte[] DestBuffer
        => _destBuffer ??= new byte[ByteSize];
}
