using Shared.Models.Proxy;
using Shared.Services.Buckets;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Shared.Services;

public class ProxyLink : IProxyLink
{
    #region static
    [ThreadStatic]
    private static StringBuilder _threadHashBuilder;

    [ThreadStatic]
    private static ArrayBufferWriter<byte> _threadBufferWriter;

    static readonly ConcurrentDictionary<string, ProxyLinkModel> links = new();

    static readonly Timer _cronTimer = new Timer(Cron, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    public static int Stat_ContLinks
        => links.IsEmpty ? 0 : links.Count;

    static readonly JsonWriterOptions _jsonWriterOptions = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = true
    };
    #endregion


    #region Encrypt
    public string Encrypt(ReadOnlySpan<char> uri, string plugin, DateTime ex = default, bool IsProxyImg = false)
        => Encrypt(uri, null, verifyip: false, ex: ex, plugin: plugin, IsProxyImg: IsProxyImg);

    public static string Encrypt(ReadOnlySpan<char> uri, ProxyLinkModel p, bool forceMd5 = false, string[] prefix = null, Action<StringBuilder> sbWriter = null)
        => Encrypt(uri, p.reqip, p.headers, p.proxy, p.plugin, p.verifyip, default, p.md5 || forceMd5, false, prefix, p.userdata, sbWriter);

    public static string Encrypt(ReadOnlySpan<char> uri, string reqip, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, string plugin = null, bool verifyip = true, DateTime ex = default, bool forceMd5 = false, bool IsProxyImg = false, string[] prefix = null, object userdata = null, Action<StringBuilder> sbWriter = null, bool writeHeaders = false)
    {
        if (uri.IsEmpty)
            return string.Empty;

        StringBuilder hash = _threadHashBuilder ??= new StringBuilder(2048);
        hash.Clear();

        if (prefix != null)
        {
            foreach (string pfx in prefix)
            {
                if (pfx != null)
                    hash.Append(pfx);
            }
        }

        int sharpIndex = uri.IndexOf('#');
        ReadOnlySpan<char> uri_clear = sharpIndex >= 0
            ? uri[..sharpIndex].Trim()
            : uri.Trim();

        if (plugin == "posterapi")
        {
            return SerializePayload(hash, IsProxyImg, uri_clear, uri, plugin, null, false, default, headers, proxy, sbWriter, writeHeaders);
        }
        else if (!forceMd5 && userdata == null)
        {
            return verifyip && CoreInit.conf.serverproxy.verifyip
                ? SerializePayload(hash, IsProxyImg, uri_clear, uri, plugin, reqip, true, DateTime.UtcNow.AddDays(1), headers, proxy, sbWriter, writeHeaders)
                : SerializePayload(hash, IsProxyImg, uri_clear, uri, plugin, null, false, default, headers, proxy, sbWriter, writeHeaders);
        }
        else
        {
            string uclear = uri_clear.ToString();
            string md5key = CrypTo.md5(verifyip && CoreInit.conf.serverproxy.verifyip
                ? uclear + reqip
                : uclear
            );

            string ext = GetExtension(uri, IsProxyImg);

            hash.Append(md5key);
            hash.Append(ext);

            ulong? H1 = null;

            if (headers != null && headers.Count > 0)
            {
                var hasHeaders = Fnv1a.Empty;
                Fnv1a.Append(ref hasHeaders, "ProxyLinkMd5");
                Fnv1a.Append(ref hasHeaders, ext);

                if (plugin != null)
                    Fnv1a.Append(ref hasHeaders, plugin);

                foreach (var h in headers)
                {
                    Fnv1a.Append(ref hasHeaders, h.name);
                    Fnv1a.Append(ref hasHeaders, h.val);
                }

                H1 = hasHeaders.H1;
                BucketHeaders.AddOrUpdate(hasHeaders.H1, headers);
            }

            links[md5key + ext] = new ProxyLinkModel(verifyip ? reqip : null, null, proxy, uclear, plugin, verifyip, ex, userdata, H1)
            {
                md5 = true
            };

            if (sbWriter != null)
                sbWriter.Invoke(hash);

            return hash.ToString();
        }
    }
    #endregion

    #region SerializePayload
    static string SerializePayload(StringBuilder sbhash, bool isProxyImg, ReadOnlySpan<char> uri_clear, ReadOnlySpan<char> uri, string plugin, string reqip, bool verifyip, DateTime e, IReadOnlyList<HeadersModel> headers, WebProxy proxy, Action<StringBuilder> sbWriter, bool writeHeaders = false)
    {
        _threadBufferWriter ??= new ArrayBufferWriter<byte>(4096);
        _threadBufferWriter.ResetWrittenCount();

        #region serialize
        using (var writer = new Utf8JsonWriter(_threadBufferWriter, _jsonWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("u"u8, uri_clear);

            if (plugin != null)
                writer.WriteString("p"u8, plugin);

            if (reqip != null)
                writer.WriteString("i"u8, reqip);

            if (verifyip)
                writer.WriteBoolean("v"u8, true);

            if (e != default)
                writer.WriteString("e"u8, e.ToUniversalTime());

            #region headers
            if (headers != null && headers.Count > 0)
            {
                writer.WriteNumber("hb"u8, BucketHeaders.AddOrUpdate("ProxyLink", headers));

                if (writeHeaders)
                {
                    writer.WriteNumber("hc"u8, headers.Count);

                    writer.WritePropertyName("h"u8);
                    writer.WriteStartObject();

                    foreach (var h in headers)
                    {
                        if (h.name != null && h.val != null)
                            writer.WriteString(h.name, h.val);
                    }

                    writer.WriteEndObject();
                }
            }
            #endregion

            #region proxy
            if (proxy != null)
            {
                writer.WritePropertyName("wp"u8);
                writer.WriteStartObject();

                var address = proxy.Address;
                var credentials = proxy.Credentials as NetworkCredential;

                writer.WriteString("s"u8, address.Scheme);
                writer.WriteString("h"u8, address.Host);
                writer.WriteNumber("p"u8, address.Port);

                if (!string.IsNullOrEmpty(credentials?.UserName))
                    writer.WriteString("un"u8, credentials.UserName);

                if (!string.IsNullOrEmpty(credentials?.Password))
                    writer.WriteString("pw"u8, credentials.Password);

                writer.WriteEndObject();
            }
            #endregion

            writer.WriteEndObject();
        }

        // JSON уже в UTF-8, перекодировать не нужно
        ReadOnlySpan<byte> json = _threadBufferWriter.WrittenSpan;

        if (json.IsEmpty)
            return "Error Serialize Payload: jsonUtf8";
        #endregion

        #region AES Encrypt
        try
        {
            var aesinst = AesPool.Instance;

            int paddedLen = aesinst.Aes.GetCiphertextLengthCbc(json.Length, PaddingMode.PKCS7);

            BufferBytePool destBuf = null;
            if (paddedLen > AesInstance.ByteSize)
                destBuf = new BufferBytePool(paddedLen);

            try
            {
                Span<byte> dest = destBuf != null
                    ? destBuf.Span
                    : aesinst.ByteBuffer;

                int cipherLen = aesinst.Aes.EncryptCbc(
                    json,
                    aesinst.Aes.IV, // iv (16 байт)
                    dest,
                    PaddingMode.PKCS7);

                if (cipherLen <= 0)
                    return "Error Serialize Payload: cipherLen";

                int maxchars = Base64Url.GetEncodedLength(cipherLen);

                BufferCharPool base64Chars = null;
                if (maxchars > AesInstance.CharSize)
                    base64Chars = new BufferCharPool(maxchars);

                try
                {
                    Span<char> base64 = base64Chars != null
                        ? base64Chars.Span
                        : aesinst.CharBuffer;

                    if (!Base64Url.TryEncodeToChars(dest.Slice(0, cipherLen), base64, out int charsWritten) || charsWritten <= 0)
                        return "Error Serialize Payload: base64Chars";

                    base64 = base64.Slice(0, charsWritten);

                    sbhash.Append(base64);
                    sbhash.Append(GetExtension(uri, isProxyImg));

                    if (sbWriter != null)
                    {
                        sbWriter.Invoke(sbhash);
                        return null;
                    }

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
    #endregion

    #region GetExtension
    static string GetExtension(ReadOnlySpan<char> uri, bool IsProxyImg)
    {
        ReadOnlySpan<char> ext = default;
        int end = uri.LastIndexOf('#');

        if (end > 0 && uri.Length > end + 1)
        {
            // #.m3u8, #.jpg и т.д
            ext = uri.Slice(end + 1);
        }
        else
        {
            end = uri.IndexOf('?');
            if (end >= 0)
                uri = uri[..end];

            int dot = uri.LastIndexOf('.');
            if (dot < 0)
            {
                if (IsProxyImg)
                    return ".jpg"; // по умолчанию для картинок .jpg

                return string.Empty;
            }

            ext = uri[dot..];
        }

        if (IsProxyImg)
        {
            if (ext.Length != 4 && ext.Length != 5)
                return ".jpg";

            return ext switch
            {
                ".png" => ".png",
                ".webp" => ".webp",
                ".svg" => ".svg",
                _ => ".jpg"
            };
        }
        else
        {
            if (ext.Length < 3 || ext.Length > 5)
                return string.Empty;

            return ext switch
            {
                ".m3u8" => ".m3u8",
                ".m3u" => ".m3u",
                ".mpd" => ".mpd",
                ".webm" => ".webm",
                ".ts" => ".ts",
                ".m4s" => ".m4s",
                ".mp4" => ".mp4",
                ".mov" => ".mov",
                ".mkv" => ".mkv",
                ".aac" => ".aac",
                ".vtt" => ".vtt",
                ".srt" => ".srt",
                ".jpg" or ".jpeg" => ".jpg",
                ".png" => ".png",
                ".webp" => ".webp",
                ".svg" => ".svg",
                _ => string.Empty
            };
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

                int maxBytes = Base64Url.GetMaxDecodedLength(base64hash.Length);

                BufferBytePool cipherBuf = null;
                if (maxBytes > AesInstance.ByteSize)
                    cipherBuf = new BufferBytePool(maxBytes);

                try
                {
                    Span<byte> cipher = cipherBuf != null
                        ? cipherBuf.Span
                        : aesinst.ByteBuffer;

                    if (!Base64Url.TryDecodeFromChars(base64hash, cipher, out int cipherLen) || cipherLen <= 0)
                        return null;

                    BufferBytePool destBuf = null;
                    if (cipherLen > AesInstance.ByteSize)
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

                        return ReadAesPayload(dest.Slice(0, plainLen), reqip);
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
            else
            {
                if (links.TryGetValue(hash.ToString(), out ProxyLinkModel val))
                {
                    if (val.verifyip == false || CoreInit.conf.serverproxy.verifyip == false || val.reqip == string.Empty || reqip == null || reqip == val.reqip)
                    {
                        if (val.bucketHeaders.HasValue)
                        {
                            BucketHeaders.TryGetValue(val.bucketHeaders.Value, out var bucketHeaders);
                            val.headers = bucketHeaders;
                        }

                        return val;
                    }
                }

                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ProxyLinkModel ReadAesPayload(ReadOnlySpan<byte> json, string reqip)
    {
        var reader = new Utf8JsonReader(json);

        short headersCount = 0;
        string uri_clear = null, plugin = null, ip = null;
        bool verifyip = false;
        DateTime e = default;
        IReadOnlyList<HeadersModel> headers = null;
        WebProxy proxy = null;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("u"u8))
            {
                reader.Read();
                uri_clear = reader.GetString();
            }
            else if (reader.ValueTextEquals("p"u8))
            {
                reader.Read();
                plugin = reader.GetString();
            }
            else if (reader.ValueTextEquals("i"u8))
            {
                reader.Read();
                ip = reader.GetString();
            }
            else if (reader.ValueTextEquals("v"u8))
            {
                reader.Read();
                verifyip = reader.GetBoolean();
            }
            else if (reader.ValueTextEquals("e"u8))
            {
                reader.Read();
                e = reader.GetDateTime();
            }

            #region headers
            else if (reader.ValueTextEquals("hb"u8))
            {
                reader.Read();

                ulong H1 = reader.GetUInt64();
                BucketHeaders.TryGetValue(H1, out headers);
            }
            else if (reader.ValueTextEquals("hc"u8))
            {
                if (headers != null && headers.Count > 0)
                    reader.Skip();
                else
                {
                    reader.Read();
                    headersCount = reader.GetInt16();
                }
            }
            else if (reader.ValueTextEquals("h"u8))
            {
                if (headers != null && headers.Count > 0)
                    reader.Skip();
                else
                {
                    reader.Read();

                    var newheaders = new List<HeadersModel>(headersCount);

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        string name = reader.GetString();
                        reader.Read();
                        string val = reader.GetString();

                        if (name != null && val != null)
                            newheaders.Add(new(name, val));
                    }

                    headers = newheaders;
                    BucketHeaders.AddOrUpdate("ProxyLink", newheaders);
                }
            }
            #endregion

            #region WebProxy
            else if (reader.ValueTextEquals("wp"u8))
            {
                reader.Read();

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    reader.Skip();
                    continue;
                }

                string scheme = null;
                string host = null;
                int port = 0;
                string userName = null;
                string password = null;

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        reader.Skip();
                        continue;
                    }

                    if (reader.ValueTextEquals("s"u8))
                    {
                        reader.Read();
                        scheme = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("h"u8))
                    {
                        reader.Read();
                        host = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("p"u8))
                    {
                        reader.Read();
                        port = reader.GetInt32();
                    }
                    else if (reader.ValueTextEquals("un"u8))
                    {
                        reader.Read();
                        userName = reader.GetString();
                    }
                    else if (reader.ValueTextEquals("pw"u8))
                    {
                        reader.Read();
                        password = reader.GetString();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                if (!string.IsNullOrEmpty(scheme) && !string.IsNullOrEmpty(host) && port > 0)
                {
                    var webProxy = new WebProxy(new UriBuilder(scheme, host, port).Uri);

                    if (!string.IsNullOrEmpty(userName))
                        webProxy.Credentials = new NetworkCredential(userName, password ?? string.Empty);

                    proxy = webProxy;
                }
            }
            #endregion

            else
            {
                reader.Skip();
            }
        }

        if (uri_clear == null)
            return null;

        if (verifyip)
        {
            if (reqip != null && ip != reqip)
                return null;

            if (DateTime.UtcNow > e)
                return null;
        }

        return new ProxyLinkModel(reqip, headers, proxy, uri_clear, plugin, verifyip, e);
    }
    #endregion

    #region IsAes
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAes(ReadOnlySpan<char> hash)
    {
        if (hash.IsEmpty)
            return false;

        if (hash.Length >= 4 &&
           (hash[0] == 'h' || hash[0] == 'H') &&
           (hash[1] == 't' || hash[1] == 'T') &&
           (hash[2] == 't' || hash[2] == 'T') &&
           (hash[3] == 'p' || hash[3] == 'P'))
        {
            // прямая ссылка tmdb/cub proxy
            return false;
        }

        int idx = hash.IndexOfAny('?', '&', '.');
        int len = idx >= 0 ? idx : hash.Length;

        /// если длина 32 — это MD5 proxy
        /// остальное AES proxy
        return len != 32;
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
            var now = DateTime.UtcNow;

            foreach (var link in links)
            {
                if (link.Value.ex != default && now > link.Value.ex)
                    links.TryRemove(link.Key, out _);
            }
        }
        catch (Exception ex)
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
