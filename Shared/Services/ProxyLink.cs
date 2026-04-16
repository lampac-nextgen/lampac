using Shared.Models.Proxy;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Shared.Services
{
    public class ProxyLink : IProxyLink
    {
        #region static
        [ThreadStatic]
        private static StringBuilder _threadHashBuilder;

        static readonly ConcurrentDictionary<string, ProxyLinkModel> links = new();

        static readonly Timer _cronTimer = new Timer(Cron, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        public static int Stat_ContLinks
            => links.IsEmpty ? 0 : links.Count;
        #endregion


        #region Encrypt
        public string Encrypt(string uri, string plugin, DateTime ex = default, bool IsProxyImg = false)
            => Encrypt(uri, null, verifyip: false, ex: ex, plugin: plugin, IsProxyImg: IsProxyImg);

        public static string Encrypt(string uri, ProxyLinkModel p, bool forceMd5 = false)
            => Encrypt(uri, p.reqip, p.headers, p.proxy, p.plugin, p.verifyip, forceMd5: p.md5 || forceMd5, userdata: p.userdata);

        public static string Encrypt(string uri, string reqip, List<HeadersModel> headers = null, WebProxy proxy = null, string plugin = null, bool verifyip = true, DateTime ex = default, bool forceMd5 = false, bool IsProxyImg = false, object userdata = null)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return string.Empty;

            string uri_clear = uri.Contains("#") ? uri.Split("#")[0].Trim() : uri.Trim();

            StringBuilder hash = (_threadHashBuilder ??= new StringBuilder(1024));
            hash.Clear();

            if (plugin == "posterapi")
            {
                return SerializePayload(hash, IsProxyImg, uri_clear, plugin, null, false, default, null);
            }
            else if (!forceMd5 && proxy == null && userdata == null && !uri_clear.Contains(" or "))
            {
                if (verifyip && CoreInit.conf.serverproxy.verifyip)
                {
                    return SerializePayload(hash, IsProxyImg, uri_clear, plugin, reqip, true, DateTime.Today.AddDays(2), headers?.ToDictionary());
                }
                else
                {
                    return SerializePayload(hash, IsProxyImg, uri_clear, plugin, null, false, default, headers?.ToDictionary());
                }
            }
            else
            {
                hash.Append(CrypTo.md5(uri_clear + (verifyip && CoreInit.conf.serverproxy.verifyip ? reqip : string.Empty)));
                WriteExtension(hash, uri_clear, IsProxyImg);

                string link = hash.ToString();

                var md = new ProxyLinkModel(verifyip ? reqip : null, headers, proxy, uri_clear, plugin, verifyip, ex, userdata)
                {
                    md5 = true
                };

                links.AddOrUpdate(link, md, (d, u) => md);

                return link;
            }
        }
        #endregion

        #region SerializePayload
        static readonly JsonWriterOptions _jsonWriterOptions = new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true
        };

        static string SerializePayload(StringBuilder sbhash, bool isProxyImg, string uri_clear, string plugin, string reqip, bool verifyip, DateTime e, Dictionary<string, string> h)
        {
            using (var utf8Buf = new BufferWriterPool<byte>())
            {
                #region serialize
                using (var writer = new Utf8JsonWriter(utf8Buf, _jsonWriterOptions))
                {
                    //JsonSerializer.Serialize(writer, payload, ProxyLinkJsonContext.Default.AesPayload);

                    writer.WriteStartObject();
                    writer.WriteString("u", uri_clear);

                    if (plugin != null)
                        writer.WriteString("p", plugin);

                    if (reqip != null)
                        writer.WriteString("i", reqip);

                    if (verifyip)
                        writer.WriteBoolean("v", true);

                    if (e != default)
                        writer.WriteString("e", e.ToUniversalTime());

                    if (h != null && h.Count > 0)
                    {
                        writer.WritePropertyName("h");
                        writer.WriteStartObject();

                        foreach (var kv in h)
                        {
                            if (kv.Key != null && kv.Value != null)
                                writer.WriteString(kv.Key, kv.Value);
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                    writer.Flush();
                }

                // JSON уже в UTF-8, перекодировать не нужно
                ReadOnlySpan<byte> utf8 = utf8Buf.WrittenSpan;
                if (utf8.IsEmpty)
                    return "Error Serialize Payload: jsonUtf8";
                #endregion

                #region AES Encrypt
                try
                {
                    var aesinst = AesPool.Instance;

                    // EncryptCbc сам делает PKCS7 padding, paddedLen считать не нужно
                    // destination должен быть достаточно большой: jsonUtf8.Length + blockSize
                    int blockSize = aesinst.Aes.BlockSize / 8; // 16
                    int requiredCipherLen = ((utf8.Length / blockSize) + 1) * blockSize;

                    BufferBytePool destBuf = null;
                    if (requiredCipherLen > aesinst.ByteBuffer.Length)
                        destBuf = new BufferBytePool(requiredCipherLen);

                    try
                    {
                        Span<byte> dest = destBuf != null
                            ? destBuf.Span
                            : aesinst.ByteBuffer;

                        int cipherLen = aesinst.Aes.EncryptCbc(
                            utf8,
                            aesinst.Aes.IV, // iv (16 байт)
                            dest,
                            PaddingMode.PKCS7);

                        if (cipherLen <= 0)
                            return "Error Serialize Payload: cipherLen";

                        int capacity = ((cipherLen + 2) / 3) * 4;

                        BufferCharPool base64Chars = null;
                        if (capacity > aesinst.CharBuffer.Length)
                            base64Chars = new BufferCharPool(capacity);

                        try
                        {
                            Span<char> base64 = base64Chars != null
                                ? base64Chars.Span
                                : aesinst.CharBuffer;

                            if (!Convert.TryToBase64Chars(dest.Slice(0, cipherLen), base64, out int charsWritten) || charsWritten <= 0)
                                return "Error Serialize Payload: base64Chars";

                            base64 = base64.Slice(0, charsWritten);

                            for (int i = 0; i < base64.Length; i++)
                            {
                                ref char c = ref base64[i];

                                if (c == '+')
                                    c = '-';
                                else if (c == '/')
                                    c = '_';
                            }

                            sbhash.Append(base64);
                            WriteExtension(sbhash, uri_clear, isProxyImg);

                            return sbhash.ToString();
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
                catch
                {
                    return "Error Serialize Payload: Exception";
                }
                #endregion
            }
        }
        #endregion

        #region WriteExtension
        static void WriteExtension(StringBuilder hash, ReadOnlySpan<char> uri, bool IsProxyImg)
        {
            if (IsProxyImg)
            {
                if (uri.Contains(".png", StringComparison.Ordinal))
                    hash.Append(".png");
                else if (uri.Contains(".webp", StringComparison.Ordinal))
                    hash.Append(".webp");
                else
                    hash.Append(".jpg");
            }
            else
            {
                if (uri.Contains(".m3u8", StringComparison.Ordinal))
                    hash.Append(".m3u8");
                else if (uri.Contains(".m3u", StringComparison.Ordinal))
                    hash.Append(".m3u");
                else if (uri.Contains(".mpd", StringComparison.Ordinal))
                    hash.Append(".mpd");
                else if (uri.Contains(".webm", StringComparison.Ordinal))
                    hash.Append(".webm");
                else if (uri.Contains(".ts", StringComparison.Ordinal))
                    hash.Append(".ts");
                else if (uri.Contains(".m4s", StringComparison.Ordinal))
                    hash.Append(".m4s");
                else if (uri.Contains(".mp4", StringComparison.Ordinal))
                    hash.Append(".mp4");
                else if (uri.Contains(".mov", StringComparison.Ordinal))
                    hash.Append(".mov");
                else if (uri.Contains(".mkv", StringComparison.Ordinal))
                    hash.Append(".mkv");
                else if (uri.Contains(".aac", StringComparison.Ordinal))
                    hash.Append(".aac");
                else if (uri.Contains(".vtt", StringComparison.Ordinal))
                    hash.Append(".vtt");
                else if (uri.Contains(".srt", StringComparison.Ordinal))
                    hash.Append(".srt");
                else if (uri.Contains(".jpg", StringComparison.Ordinal) || uri.Contains(".jpeg", StringComparison.Ordinal))
                    hash.Append(".jpg");
                else if (uri.Contains(".png", StringComparison.Ordinal))
                    hash.Append(".png");
                else if (uri.Contains(".webp", StringComparison.Ordinal))
                    hash.Append(".webp");
            }
        }
        #endregion


        #region Decrypt
        public static ProxyLinkModel Decrypt(ReadOnlySpan<char> hash, string reqip)
        {
            if (hash.IsEmpty)
                return null;

            try
            {
                if (IsAes(hash))
                {
                    ReadOnlySpan<char> base64hash = hash;
                    int dot = hash.LastIndexOf('.');
                    if (dot > 0)
                        base64hash = base64hash.Slice(0, dot);

                    var aesinst = AesPool.Instance;

                    BufferCharPool aesBuf = null;
                    if (base64hash.Length > aesinst.CharBuffer.Length)
                        aesBuf = new BufferCharPool(base64hash.Length);

                    Span<char> aeshash = aesBuf != null
                        ? aesBuf.Span.Slice(0, base64hash.Length)
                        : aesinst.CharBuffer.AsSpan(0, base64hash.Length);

                    try
                    {
                        for (int i = 0; i < base64hash.Length; i++)
                        {
                            char c = base64hash[i];

                            if (c == '-')
                                aeshash[i] = '+';
                            else if (c == '_')
                                aeshash[i] = '/';
                            else
                                aeshash[i] = c;
                        }

                        int capacity = Encoding.UTF8.GetMaxByteCount(aeshash.Length);

                        BufferBytePool cipherBuf = null;
                        if (capacity > aesinst.ByteBuffer.Length)
                            cipherBuf = new BufferBytePool(capacity);

                        try
                        {
                            Span<byte> cipher = cipherBuf != null
                                ? cipherBuf.Span
                                : aesinst.ByteBuffer;

                            if (!Convert.TryFromBase64Chars(aeshash, cipher, out int cipherLen) || cipherLen <= 0)
                                return null;

                            BufferBytePool destBuf = null;
                            if (cipherLen > aesinst.DestBuffer.Length)
                                destBuf = new BufferBytePool(cipherLen);

                            try
                            {
                                Span<byte> dest = destBuf != null
                                    ? destBuf.Span
                                    : aesinst.DestBuffer;

                                int plainLen = aesinst.Aes.DecryptCbc(
                                    cipher.Slice(0, cipherLen),
                                    aesinst.Aes.IV,
                                    dest,
                                    PaddingMode.PKCS7);

                                if (plainLen <= 0)
                                    return null;

                                var root = JsonSerializer.Deserialize(dest.Slice(0, plainLen), ProxyLinkJsonContext.Default.AesPayload);
                                if (root == null)
                                    return null;

                                //Console.WriteLine(JsonSerializer.Serialize(root));

                                if (root.v)
                                {
                                    if (reqip != null && root.i != reqip)
                                        return null;

                                    if (DateTime.Now > root.e)
                                        return null;
                                }

                                return new ProxyLinkModel(reqip, HeadersModel.InitOrNull(root.h), null, root.u, root.p);
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
                    finally
                    {
                        aesBuf?.Dispose();
                    }
                }
                else
                {
                    if (links.TryGetValue(hash.ToString(), out ProxyLinkModel val))
                    {
                        if (val.verifyip == false || CoreInit.conf.serverproxy.verifyip == false || val.reqip == string.Empty || reqip == null || reqip == val.reqip)
                            return val;
                    }

                    return null;
                }
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region IsAes
        public static bool IsAes(ReadOnlySpan<char> hash)
        {
            if (hash.IsEmpty)
                return false;

            if (hash.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return false;

            // Ищем первый из ?, &, .
            int idx = hash.IndexOfAny('?', '&', '.');

            ReadOnlySpan<char> firstPart;
            if (idx >= 0)
                firstPart = hash.Slice(0, idx);
            else
                firstPart = hash;

            // Если длина 32 — это не AES
            return firstPart.Length != 32;
        }
        #endregion


        #region Cron
        static int _updatingDb = 0;

        static void Cron(object state)
        {
            if (links.IsEmpty)
                return;

            if (Interlocked.Exchange(ref _updatingDb, 1) == 1)
                return;

            try
            {
                var now = DateTime.Now;

                foreach (var link in links)
                {
                    if (now > link.Value.ex)
                        links.TryRemove(link.Key, out _);
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "ProxyLink", "id_s1dvs8n1");
            }
            finally
            {
                Volatile.Write(ref _updatingDb, 0);
            }
        }
        #endregion
    }


    internal sealed class AesPayload
    {
        public string p { get; set; }
        public string u { get; set; }
        public string i { get; set; }
        public bool v { get; set; }
        public DateTime e { get; set; }
        public Dictionary<string, string> h { get; set; }
    }

    [JsonSerializable(typeof(AesPayload))]
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
    internal partial class ProxyLinkJsonContext : JsonSerializerContext
    {
    }
}
