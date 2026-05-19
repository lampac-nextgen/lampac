using System.Security.Cryptography;
using System.Text;

namespace Shared.Services.Utilities;

public static class AesTo
{
    #region Encrypt
    public static string Encrypt(ReadOnlySpan<char> plainText)
    {
        if (plainText.IsEmpty)
            return string.Empty;

        try
        {
            var aesinst = AesPool.Instance;

            int maxbytes = Encoding.UTF8.GetMaxByteCount(plainText.Length);

            BufferBytePool cipherBuf = null;
            if (maxbytes > aesinst.ByteSize)
                cipherBuf = new BufferBytePool(maxbytes);

            try
            {
                Span<byte> cipher = cipherBuf != null
                    ? cipherBuf.Span
                    : aesinst.ByteBuffer;

                if (!Encoding.UTF8.TryGetBytes(plainText, cipher, out int writtenPlain) || writtenPlain == 0)
                    return string.Empty;

                int blockSize = aesinst.Aes.BlockSize / 8; // 16
                int paddedLen = ((writtenPlain / blockSize) + 1) * blockSize;

                BufferBytePool destBuf = null;
                if (paddedLen > aesinst.ByteSize)
                    destBuf = new BufferBytePool(paddedLen);

                try
                {
                    Span<byte> dest = destBuf != null
                        ? destBuf.Span
                        : aesinst.DestBuffer;

                    // ВАЖНО: iv вторым параметром, destination третьим
                    int cipherLen = aesinst.Aes.EncryptCbc(
                        cipher.Slice(0, writtenPlain),
                        aesinst.Aes.IV,  // iv (16 байт)
                        dest,    // destination
                        PaddingMode.PKCS7);

                    if (cipherLen <= 0)
                        return string.Empty;

                    int maxchars = ((cipherLen + 2) / 3) * 4;

                    BufferCharPool base64Chars = null;
                    if (maxchars > aesinst.CharSize)
                        base64Chars = new BufferCharPool(maxchars);

                    try
                    {
                        Span<char> buffer = base64Chars != null
                            ? base64Chars.Span
                            : aesinst.CharBuffer;

                        if (!Convert.TryToBase64Chars(dest.Slice(0, cipherLen), buffer, out int charsWritten))
                            return string.Empty;

                        return new string(buffer.Slice(0, charsWritten));
                    }
                    finally
                    {
                        base64Chars?.Dispose();
                    }
                }
                finally
                {
                    destBuf?.Dispose();
                }
            }
            finally
            {
                cipherBuf?.Dispose();
            }
        }
        catch
        {
            return string.Empty;
        }
    }
    #endregion

    #region Decrypt
    public static string Decrypt(ReadOnlySpan<char> cipherText)
    {
        if (cipherText.IsEmpty)
            return null;

        try
        {
            var aesinst = AesPool.Instance;

            int capacity = Encoding.UTF8.GetMaxByteCount(cipherText.Length);

            BufferBytePool cipherBuf = null;
            if (capacity > aesinst.ByteSize)
                cipherBuf = new BufferBytePool(capacity);

            try
            {
                Span<byte> cipher = cipherBuf != null
                    ? cipherBuf.Span
                    : aesinst.ByteBuffer;

                if (!Convert.TryFromBase64Chars(cipherText, cipher, out int cipherLen))
                    return null;

                BufferBytePool destBuf = null;
                if (cipherLen > aesinst.ByteSize)
                    destBuf = new BufferBytePool(cipherLen);

                try
                {
                    Span<byte> dest = destBuf != null
                        ? destBuf.Span
                        : aesinst.DestBuffer;

                    // ВАЖНО: iv вторым параметром, destination третьим
                    int plainLen = aesinst.Aes.DecryptCbc(
                        cipher.Slice(0, cipherLen),
                        aesinst.Aes.IV,  // iv (16 байт)
                        dest,    // destination
                        PaddingMode.PKCS7);

                    if (plainLen <= 0)
                        return null;

                    return Encoding.UTF8.GetString(dest.Slice(0, plainLen));
                }
                finally
                {
                    destBuf?.Dispose();
                }
            }
            finally
            {
                cipherBuf?.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }
    #endregion
}
