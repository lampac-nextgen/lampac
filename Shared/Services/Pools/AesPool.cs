using System.Security.Cryptography;
using System.Text;

namespace Shared.Services.Pools;

public static class AesPool
{
    static byte[] aesKey, aesIV;

    [ThreadStatic]
    private static AesInstance _instance;

    static AesPool()
    {
        if (File.Exists("cache/aeskey"))
        {
            var i = File.ReadAllText("cache/aeskey").Split("/");
            aesKey = Encoding.UTF8.GetBytes(i[0]);
            aesIV = Encoding.UTF8.GetBytes(i[1]);
        }
        else
        {
            string k = UnicTo.Code(16);
            string v = UnicTo.Code(16);
            Directory.CreateDirectory("cache");
            File.WriteAllText("cache/aeskey", $"{k}/{v}");

            aesKey = Encoding.UTF8.GetBytes(k);
            aesIV = Encoding.UTF8.GetBytes(v);
        }
    }

    public static AesInstance Instance
        => _instance ??= new AesInstance(aesKey, aesIV);
}


public class AesInstance
{
    public AesInstance(byte[] aesKey, byte[] aesIV)
    {
        Aes = Aes.Create();
        Aes.Mode = CipherMode.CBC;
        Aes.Padding = PaddingMode.PKCS7;

        Aes.Key = aesKey;
        Aes.IV = aesIV;
    }

    public readonly Aes Aes;

    public int CharSize => 4096;

    public int ByteSize => 16 * 1024;

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
