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
    public readonly char[] CharBuffer = new char[4096];
    public readonly byte[] ByteBuffer = new byte[32 * 1024];
    public readonly byte[] DestBuffer = new byte[32 * 1024];
}
