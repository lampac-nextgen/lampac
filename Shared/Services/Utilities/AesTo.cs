using System.Buffers.Text;
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
            if (maxbytes > AesInstance.ByteSize)
                cipherBuf = new BufferBytePool(maxbytes);

            try
            {
                Span<byte> cipher = cipherBuf != null
                    ? cipherBuf.Span
                    : aesinst.ByteBuffer;

                if (!Encoding.UTF8.TryGetBytes(plainText, cipher, out int writtenPlain) || writtenPlain == 0)
                    return string.Empty;

                int sealedLen = aesinst.Aes.GetCiphertextLengthCbc(writtenPlain, PaddingMode.PKCS7) + AesInstance.BlockSize;

                BufferBytePool destBuf = null;
                if (sealedLen > AesInstance.ByteSize)
                    destBuf = new BufferBytePool(sealedLen);

                try
                {
                    Span<byte> dest = destBuf != null
                        ? destBuf.Span
                        : aesinst.DestBuffer;

                    int cipherLen = AesPool.Seal(aesinst.Aes, cipher.Slice(0, writtenPlain), dest);

                    if (cipherLen <= 0)
                        return string.Empty;

                    int maxchars = Base64Url.GetEncodedLength(cipherLen);

                    BufferCharPool base64Chars = null;
                    if (maxchars > AesInstance.CharSize)
                        base64Chars = new BufferCharPool(maxchars);

                    try
                    {
                        Span<char> buffer = base64Chars != null
                            ? base64Chars.Span
                            : aesinst.CharBuffer;

                        if (!Base64Url.TryEncodeToChars(dest.Slice(0, cipherLen), buffer, out int charsWritten))
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

            int maxBytes = Base64Url.GetMaxDecodedLength(cipherText.Length);

            BufferBytePool cipherBuf = null;
            if (maxBytes > AesInstance.ByteSize)
                cipherBuf = new BufferBytePool(maxBytes);

            try
            {
                Span<byte> cipher = cipherBuf != null
                    ? cipherBuf.Span
                    : aesinst.ByteBuffer;

                if (!Base64Url.TryDecodeFromChars(cipherText, cipher, out int cipherLen))
                    return null;

                BufferBytePool destBuf = null;
                if (cipherLen > AesInstance.ByteSize)
                    destBuf = new BufferBytePool(cipherLen);

                try
                {
                    Span<byte> dest = destBuf != null
                        ? destBuf.Span
                        : aesinst.DestBuffer;

                    int plainLen = AesPool.Open(aesinst.Aes, cipher.Slice(0, cipherLen), dest);

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
