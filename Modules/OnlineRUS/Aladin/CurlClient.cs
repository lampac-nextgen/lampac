using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aladin;

/// <summary>
/// Browser-like HTTP/WebSocket client backed by libcurl-impersonate.
/// The library is a normal C library (BoringSSL/nghttp2), so it does not
/// install a Go runtime or Go signal handlers in the Lampac process.
/// </summary>
internal static class CurlClient
{
    const string DefaultProfile = "chrome_133";

    static readonly object _sync = new();
    static readonly ConcurrentDictionary<IntPtr, CurlExchange> _exchanges = new();
    static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _cookieJars = new();

    static Api _api;
    static string _loadError;

    // libcurl keeps these pointers after the setopt call, so the delegates
    // must stay rooted for the whole transfer.
    static readonly CurlWriteFn _writeCallback = WriteCallback;
    static readonly CurlHeaderFn _headerCallback = HeaderCallback;

    public static string Profile { get; set; } = DefaultProfile;
    public static bool InsecureSkipVerify { get; set; }

    public static bool IsAvailable
    {
        get { lock (_sync) return _api != null; }
    }

    public static string LoadError
    {
        get { lock (_sync) return _loadError; }
    }

    public static bool Initialize(string configuredPath)
    {
        lock (_sync)
        {
            if (_api != null)
                return true;

            string path = ResolveLibraryPath(configuredPath);
            if (path == null)
            {
                _loadError = "libcurl-impersonate library not found";
                return false;
            }

            try
            {
                var handle = NativeLibrary.Load(path);
                var api = new Api
                {
                    GlobalInit = Bind<GlobalInitFn>(handle, "curl_global_init"),
                    EasyInit = Bind<EasyInitFn>(handle, "curl_easy_init"),
                    EasyCleanup = Bind<EasyCleanupFn>(handle, "curl_easy_cleanup"),
                    EasyImpersonate = Bind<EasyImpersonateFn>(handle, "curl_easy_impersonate"),
                    EasyPerform = Bind<EasyPerformFn>(handle, "curl_easy_perform"),
                    EasyStrError = Bind<EasyStrErrorFn>(handle, "curl_easy_strerror"),
                    SlistAppend = Bind<SlistAppendFn>(handle, "curl_slist_append"),
                    SlistFreeAll = Bind<SlistFreeAllFn>(handle, "curl_slist_free_all"),
                    SetOptLong = Bind<SetOptLongFn>(handle, "curl_easy_setopt"),
                    SetOptString = Bind<SetOptStringFn>(handle, "curl_easy_setopt"),
                    SetOptPtr = Bind<SetOptPtrFn>(handle, "curl_easy_setopt"),
                    SetOptWrite = Bind<SetOptWriteFn>(handle, "curl_easy_setopt"),
                    SetOptHeader = Bind<SetOptHeaderFn>(handle, "curl_easy_setopt"),
                    GetInfoLong = Bind<GetInfoLongFn>(handle, "curl_easy_getinfo"),
                    GetInfoString = Bind<GetInfoStringFn>(handle, "curl_easy_getinfo"),
                    WsSend = Bind<WsSendFn>(handle, "curl_ws_send"),
                    WsRecv = Bind<WsRecvFn>(handle, "curl_ws_recv")
                };

                if (api.GlobalInit(3) != 0)
                    throw new InvalidOperationException("curl_global_init failed");

                _api = api;
                _loadError = null;
                return true;
            }
            catch (Exception ex)
            {
                _loadError = ex.Message;
                _api = null;
                return false;
            }
        }
    }

    static T Bind<T>(IntPtr handle, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));

    static string ResolveLibraryPath(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string env = Environment.GetEnvironmentVariable("ALADIN_CURL_LIBRARY");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        string[] names = OperatingSystem.IsMacOS()
            ? ["libcurl-impersonate.dylib", "libcurl-impersonate.so"]
            : ["libcurl-impersonate.so.4.8.0", "libcurl-impersonate.so.4", "libcurl-impersonate.so"];

        string baseDir = AppContext.BaseDirectory;
        var candidates = new List<string> { Path.Combine(baseDir, "native") };
        candidates.Add(baseDir);
        candidates.Add(Path.Combine(baseDir, "module", "Aladin"));
        candidates.Add(Path.Combine(baseDir, "module", "Aladin", "native"));

        foreach (string directory in candidates)
        {
            foreach (string name in names)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    public static CurlResponse Request(CurlRequest request)
    {
        var api = EnsureApi();
        request.Profile ??= Profile;

        IntPtr easy = api.EasyInit();
        if (easy == IntPtr.Zero)
            throw new InvalidOperationException("curl_easy_init failed");

        var exchange = new CurlExchange();
        if (!string.IsNullOrEmpty(request.StreamOutputPath))
        {
            string directory = Path.GetDirectoryName(request.StreamOutputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            exchange.File = new FileStream(request.StreamOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        }

        _exchanges[easy] = exchange;
        IntPtr headers = IntPtr.Zero;

        try
        {
            string body = request.RequestBody ?? "";
            string method = (request.RequestMethod ?? "GET").ToUpperInvariant();
            string url = request.RequestUrl;

            api.SetOptString(easy, CurlUrl, url);
            api.SetOptLong(easy, CurlHttpVersion, 3); // HTTP/2
            api.SetOptLong(easy, CurlFollowLocation, request.FollowRedirects == false ? 0 : 1);
            api.SetOptLong(easy, CurlMaxRedirs, 10);
            api.SetOptLong(easy, CurlNosignal, 1);
            api.SetOptLong(easy, CurlTimeoutMs, Math.Max(1000, request.TimeoutMilliseconds));
            api.SetOptLong(easy, CurlConnectTimeoutMs, Math.Max(1000, request.TimeoutMilliseconds));
            api.SetOptString(easy, CurlAcceptEncoding, "gzip, deflate, br, zstd");

            if (request.InsecureSkipVerify || InsecureSkipVerify)
            {
                api.SetOptLong(easy, CurlSslVerifyPeer, 0);
                api.SetOptLong(easy, CurlSslVerifyHost, 0);
            }

            if (!string.IsNullOrWhiteSpace(request.ProxyUrl))
                api.SetOptString(easy, CurlProxy, request.ProxyUrl);

            headers = BuildHeaderList(api, request, easy);
            api.SetOptPtr(easy, CurlHttpHeader, headers);
            api.SetOptPtr(easy, CurlWriteData, easy);
            api.SetOptPtr(easy, CurlHeaderData, easy);
            api.SetOptWrite(easy, CurlWriteFunction, _writeCallback);
            api.SetOptHeader(easy, CurlHeaderFunction, _headerCallback);

            if (method == "POST")
            {
                api.SetOptLong(easy, CurlPost, 1);
                api.SetOptLong(easy, CurlPostFieldSize, Encoding.UTF8.GetByteCount(body));
                api.SetOptString(easy, CurlCopyPostFields, body);
            }
            else if (method != "GET")
            {
                api.SetOptString(easy, CurlCustomRequest, method);
            }

            int impersonate = api.EasyImpersonate(easy, CurlTarget(request.Profile), 0);
            if (impersonate != 0)
                throw new InvalidOperationException(api.StrError(impersonate));

            int rc = api.EasyPerform(easy);
            var response = new CurlResponse
            {
                Status = 0,
                Body = "",
                SessionId = request.SessionId,
                Headers = exchange.ToHeaders(),
                Cookies = exchange.ToCookieMap(),
                Target = ReadEffectiveUrl(api, easy) ?? url,
                UsedProtocol = ReadProtocol(api, easy)
            };

            if (rc != 0)
            {
                response.Body = api.StrError(rc);
                return response;
            }

            response.Status = (int)ReadLong(api, easy, CurlInfoResponseCode);
            UpdateCookieJar(request.SessionId, exchange.SetCookies);
            response.Cookies = exchange.ToCookieMap();

            if (exchange.File != null)
            {
                exchange.File.Flush();
                response.Body = "";
            }
            else if (request.IsByteResponse == true)
            {
                response.Body = Convert.ToBase64String(exchange.Body.ToArray());
            }
            else
            {
                response.Body = Encoding.UTF8.GetString(exchange.Body.ToArray());
            }

            return response;
        }
        finally
        {
            if (headers != IntPtr.Zero)
                api.SlistFreeAll(headers);

            _exchanges.TryRemove(easy, out _);
            exchange.Dispose();
            api.EasyCleanup(easy);
        }
    }

    static IntPtr BuildHeaderList(Api api, CurlRequest request, IntPtr easy)
    {
        var headers = request.Headers ?? new Dictionary<string, string>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr list = IntPtr.Zero;

        void Add(string name, string value)
        {
            if (string.IsNullOrEmpty(name) || value == null)
                return;

            list = api.SlistAppend(list, $"{name}: {value}");
            added.Add(name);
        }

        foreach (string name in request.HeaderOrder ?? Array.Empty<string>())
        {
            if (name.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            if (headers.TryGetValue(name, out string value))
                Add(name, value);
        }

        foreach (var pair in headers)
        {
            if (pair.Key.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!added.Contains(pair.Key))
                Add(pair.Key, pair.Value);
        }

        if (!string.IsNullOrEmpty(request.SessionId) &&
            _cookieJars.TryGetValue(request.SessionId, out var jar) &&
            jar.Count > 0 &&
            !headers.ContainsKey("cookie"))
        {
            Add("Cookie", string.Join("; ", jar.Select(x => $"{x.Key}={x.Value}")));
        }

        // Chrome does not send an empty Expect header; suppress curl's 100-continue.
        Add("Expect", "");
        return list;
    }

    static void UpdateCookieJar(string sessionId, List<string> setCookies)
    {
        if (string.IsNullOrEmpty(sessionId) || setCookies == null || setCookies.Count == 0)
            return;

        var jar = _cookieJars.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
        foreach (string header in setCookies)
            ParseSetCookie(header, jar);
    }

    static void ParseSetCookie(string header, ConcurrentDictionary<string, string> jar)
    {
        if (string.IsNullOrEmpty(header))
            return;

        int semicolon = header.IndexOf(';');
        string pair = semicolon >= 0 ? header[..semicolon] : header;
        int equals = pair.IndexOf('=');
        if (equals <= 0)
            return;

        string name = pair[..equals].Trim();
        string value = pair[(equals + 1)..].Trim();
        if (name.Length > 0)
            jar[name] = value;
    }

    public static List<CurlCookie> GetCookies(string sessionId, string url)
    {
        var result = new List<CurlCookie>();
        if (!string.IsNullOrEmpty(sessionId) && _cookieJars.TryGetValue(sessionId, out var jar))
        {
            foreach (var pair in jar)
                result.Add(new CurlCookie { Name = pair.Key, Value = pair.Value });
        }

        return result;
    }

    public static CurlWebSocket WebSocket(CurlWsConnect request)
    {
        var api = EnsureApi();
        request.Profile ??= Profile;
        string target = CurlTarget(request.Profile);

        IntPtr easy = api.EasyInit();
        if (easy == IntPtr.Zero)
            throw new InvalidOperationException("curl_easy_init failed");

        IntPtr headers = IntPtr.Zero;
        try
        {
            api.SetOptString(easy, CurlUrl, request.Url);
            api.SetOptLong(easy, CurlConnectOnly, 2);
            api.SetOptLong(easy, CurlHttpVersion, 2); // WebSocket handshake is HTTP/1.1
            api.SetOptLong(easy, CurlWsOptions, CurlWsPermessageDeflate);
            api.SetOptLong(easy, CurlNosignal, 1);
            api.SetOptLong(easy, CurlConnectTimeoutMs, Math.Max(1000, request.HandshakeTimeoutMilliseconds));

            if (request.InsecureSkipVerify || InsecureSkipVerify)
            {
                api.SetOptLong(easy, CurlSslVerifyPeer, 0);
                api.SetOptLong(easy, CurlSslVerifyHost, 0);
            }

            if (!string.IsNullOrWhiteSpace(request.ProxyUrl))
                api.SetOptString(easy, CurlProxy, request.ProxyUrl);

            var values = request.Headers ?? new Dictionary<string, string>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr list = IntPtr.Zero;

            foreach (string name in request.HeaderOrder ?? Array.Empty<string>())
            {
                if (values.TryGetValue(name, out string value))
                {
                    list = api.SlistAppend(list, $"{name}: {value}");
                    added.Add(name);
                }
            }

            foreach (var pair in values)
            {
                if (!added.Contains(pair.Key))
                    list = api.SlistAppend(list, $"{pair.Key}: {pair.Value}");
            }

            headers = list;
            api.SetOptPtr(easy, CurlHttpHeader, headers);

            int impersonate = api.EasyImpersonate(easy, target, 0);
            if (impersonate != 0)
                throw new InvalidOperationException(api.StrError(impersonate));

            int rc = api.EasyPerform(easy);
            if (rc != 0)
                throw new InvalidOperationException(api.StrError(rc));

            return new CurlWebSocket(api, easy);
        }
        catch
        {
            api.EasyCleanup(easy);
            throw;
        }
        finally
        {
            if (headers != IntPtr.Zero)
                api.SlistFreeAll(headers);
        }
    }

    static Api EnsureApi()
    {
        lock (_sync)
        {
            if (_api != null)
                return _api;
        }

        if (!Initialize(null))
            throw new InvalidOperationException(_loadError ?? "libcurl-impersonate is not available");

        lock (_sync)
            return _api;
    }

    static long ReadLong(Api api, IntPtr easy, int info)
    {
        long value = 0;
        api.GetInfoLong(easy, info, out value);
        return value;
    }

    static string ReadEffectiveUrl(Api api, IntPtr easy)
    {
        api.GetInfoString(easy, CurlInfoEffectiveUrl, out IntPtr value);
        return value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(value);
    }

    static string ReadProtocol(Api api, IntPtr easy)
    {
        long version = ReadLong(api, easy, CurlInfoHttpVersion);
        return version >= 3 ? "HTTP/2.0" : "HTTP/1.1";
    }

    static string CurlTarget(string profile)
    {
        string value = (profile ?? DefaultProfile).Replace("_", "").ToLowerInvariant();
        return value switch
        {
            "chrome131" => "chrome131",
            "chrome133" or "chrome133a" => "chrome133a",
            "chrome136" => "chrome136",
            "chrome142" => "chrome142",
            "chrome145" => "chrome145",
            "chrome146" => "chrome146",
            "chrome150" => "chrome150",
            "chrome151" => "chrome151",
            "chrome152" => "chrome152",
            _ => "chrome133a"
        };
    }

    static UIntPtr WriteCallback(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata)
    {
        int length = checked((int)((long)size * (long)nmemb));
        if (length <= 0 || !_exchanges.TryGetValue(userdata, out var exchange))
            return (UIntPtr)(ulong)length;

        byte[] buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);

        if (exchange.File != null)
            exchange.File.Write(buffer, 0, length);
        else
            exchange.Body.Write(buffer, 0, length);

        return (UIntPtr)(ulong)length;
    }

    static UIntPtr HeaderCallback(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata)
    {
        int length = checked((int)((long)size * (long)nmemb));
        if (length <= 0 || !_exchanges.TryGetValue(userdata, out var exchange))
            return (UIntPtr)(ulong)length;

        string line = Marshal.PtrToStringUTF8(ptr, length);
        int colon = line.IndexOf(':');
        if (colon > 0)
        {
            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (name.Length > 0)
            {
                exchange.Headers[name] = value;
                if (name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
                    exchange.SetCookies.Add(value);
            }
        }

        return (UIntPtr)(ulong)length;
    }

    const int CurlUrl = 10002;
    const int CurlProxy = 10004;
    const int CurlWriteData = 10001;
    const int CurlHeaderData = 10029;
    const int CurlHttpHeader = 10023;
    const int CurlCopyPostFields = 10165;
    const int CurlCustomRequest = 10036;
    const int CurlPost = 47;
    const int CurlPostFieldSize = 60;
    const int CurlFollowLocation = 52;
    const int CurlMaxRedirs = 68;
    const int CurlSslVerifyPeer = 64;
    const int CurlSslVerifyHost = 81;
    const int CurlHttpVersion = 84;
    const int CurlNosignal = 99;
    const int CurlAcceptEncoding = 10102;
    const int CurlTimeoutMs = 155;
    const int CurlConnectTimeoutMs = 156;
    const int CurlConnectOnly = 141;
    const int CurlWsOptions = 320;
    const int CurlWsPermessageDeflate = 1 << 2;
    const int CurlWriteFunction = 20011;
    const int CurlHeaderFunction = 20079;
    const int CurlInfoEffectiveUrl = 0x100001;
    const int CurlInfoResponseCode = 0x200002;
    const int CurlInfoHttpVersion = 0x20002e;

    internal const int CurlWsText = 1;
    internal const int CurlWsBinary = 2;
    internal const int CurlWsClose = 8;
    internal const int CurlWsPing = 16;
    internal const int CurlWsPong = 64;
    internal const int CurleAgain = 81;

    internal sealed class Api
    {
        public GlobalInitFn GlobalInit;
        public EasyInitFn EasyInit;
        public EasyCleanupFn EasyCleanup;
        public EasyImpersonateFn EasyImpersonate;
        public EasyPerformFn EasyPerform;
        public EasyStrErrorFn EasyStrError;
        public SlistAppendFn SlistAppend;
        public SlistFreeAllFn SlistFreeAll;
        public SetOptLongFn SetOptLong;
        public SetOptStringFn SetOptString;
        public SetOptPtrFn SetOptPtr;
        public SetOptWriteFn SetOptWrite;
        public SetOptHeaderFn SetOptHeader;
        public GetInfoLongFn GetInfoLong;
        public GetInfoStringFn GetInfoString;
        public WsSendFn WsSend;
        public WsRecvFn WsRecv;

        public string StrError(int code)
            => Marshal.PtrToStringUTF8(EasyStrError(code)) ?? ("curl error " + code);
    }

    sealed class CurlExchange : IDisposable
    {
        public readonly MemoryStream Body = new();
        public FileStream File;
        public readonly Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> SetCookies = new();

        public Dictionary<string, string[]> ToHeaders()
        {
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Headers)
                result[pair.Key] = [pair.Value];
            return result;
        }

        public Dictionary<string, string> ToCookieMap()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string header in SetCookies)
            {
                int semicolon = header.IndexOf(';');
                string pair = semicolon >= 0 ? header[..semicolon] : header;
                int equals = pair.IndexOf('=');
                if (equals > 0)
                    result[pair[..equals].Trim()] = pair[(equals + 1)..].Trim();
            }

            return result;
        }

        public void Dispose()
        {
            File?.Dispose();
            File = null;
            Body.Dispose();
        }
    }
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int GlobalInitFn(long flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr EasyInitFn();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void EasyCleanupFn(IntPtr easy);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int EasyImpersonateFn(IntPtr easy, [MarshalAs(UnmanagedType.LPUTF8Str)] string target, int defaultHeaders);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int EasyPerformFn(IntPtr easy);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr EasyStrErrorFn(int code);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate IntPtr SlistAppendFn(IntPtr list, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void SlistFreeAllFn(IntPtr list);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SetOptLongFn(IntPtr easy, int option, long value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SetOptStringFn(IntPtr easy, int option, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SetOptPtrFn(IntPtr easy, int option, IntPtr value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate UIntPtr CurlWriteFn(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate UIntPtr CurlHeaderFn(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SetOptWriteFn(IntPtr easy, int option, CurlWriteFn value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int SetOptHeaderFn(IntPtr easy, int option, CurlHeaderFn value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int GetInfoLongFn(IntPtr easy, int info, out long value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int GetInfoStringFn(IntPtr easy, int info, out IntPtr value);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int WsSendFn(IntPtr easy, IntPtr buffer, UIntPtr length, out UIntPtr sent, long fragsize, uint flags);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate int WsRecvFn(IntPtr easy, IntPtr buffer, UIntPtr length, out UIntPtr received, out IntPtr meta);

[StructLayout(LayoutKind.Sequential)]
internal struct CurlWsFrame
{
    public int age;
    public int flags;
    public long offset;
    public long bytesleft;
    public UIntPtr len;
}

internal sealed class CurlWebSocket : IDisposable
{
    readonly CurlClient.Api _api;
    readonly IntPtr _easy;
    readonly BlockingCollection<CurlWsRead> _queue = new(new ConcurrentQueue<CurlWsRead>());
    readonly IntPtr _buffer = Marshal.AllocHGlobal(65536);
    readonly object _socketLock = new();
    readonly ManualResetEventSlim _readerDone = new(false);
    volatile bool _closed;

    internal CurlWebSocket(CurlClient.Api api, IntPtr easy)
    {
        _api = api;
        _easy = easy;
        _ = Task.Run(ReadLoop);
    }

    public CurlWsRead Read(int timeoutMilliseconds)
    {
        if (_queue.TryTake(out var message, Math.Max(1, timeoutMilliseconds)))
            return message;

        return new CurlWsRead { TimedOut = !_closed, Closed = _closed };
    }

    public void Write(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
        lock (_socketLock)
        {
            IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                int rc = _api.WsSend(_easy, buffer, (UIntPtr)(ulong)bytes.Length, out _, 0, 1);
                if (rc != 0)
                    throw new InvalidOperationException(_api.StrError(rc));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    void ReadLoop()
    {
        try
        {
            while (!_closed)
            {
                int rc;
                UIntPtr received;
                IntPtr meta;

                // libcurl's easy handle is not thread-safe: a send from the
                // heartbeat task must not overlap with this recv.
                lock (_socketLock)
                {
                    rc = _api.WsRecv(_easy, _buffer, (UIntPtr)65536, out received, out meta);
                }

                if (rc == CurlClient.CurleAgain) // CURLE_AGAIN
                {
                    // curl_ws_recv is non-blocking; without a pause this loop
                    // would spin a whole core while the socket is idle.
                    Thread.Sleep(10);
                    continue;
                }

                if (rc != 0)
                {
                    _queue.Add(new CurlWsRead { Closed = true, Error = _api.StrError(rc) });
                    return;
                }

                int length = checked((int)(ulong)received);
                var frame = Marshal.PtrToStructure<CurlWsFrame>(meta);

                if ((frame.flags & CurlClient.CurlWsClose) != 0)
                {
                    int code = 0;
                    string reason = "";
                    if (length >= 2)
                    {
                        byte[] close = new byte[length];
                        Marshal.Copy(_buffer, close, 0, length);
                        code = (close[0] << 8) | close[1];
                        reason = length > 2 ? Encoding.UTF8.GetString(close, 2, length - 2) : "";
                    }

                    _queue.Add(new CurlWsRead
                    {
                        Closed = true,
                        Error = code == 0 ? "websocket closed" : $"close {code} {reason}".Trim()
                    });
                    return;
                }

                if ((frame.flags & CurlClient.CurlWsPing) != 0)
                {
                    SendPong(length);
                    continue;
                }

                if ((frame.flags & CurlClient.CurlWsText) != 0)
                {
                    string text = length == 0 ? "" : Marshal.PtrToStringUTF8(_buffer, length);
                    _queue.Add(new CurlWsRead { Data = text, MessageType = 1 });
                }
                else if ((frame.flags & CurlClient.CurlWsBinary) != 0)
                {
                    byte[] bytes = new byte[length];
                    Marshal.Copy(_buffer, bytes, 0, length);

                    // The telemetry server sends the same JSON in text and
                    // binary frames; keep it readable for the edge hash parser.
                    _queue.Add(new CurlWsRead { Data = Encoding.UTF8.GetString(bytes), MessageType = 2 });
                }
            }
        }
        catch (Exception ex)
        {
            _queue.Add(new CurlWsRead { Closed = true, Error = ex.Message });
        }
        finally
        {
            _readerDone.Set();
        }
    }

    void SendPong(int length)
    {
        lock (_socketLock)
        {
            int rc = _api.WsSend(_easy, _buffer, (UIntPtr)(ulong)length, out _, 0, CurlClient.CurlWsPong);
            if (rc != 0)
                throw new InvalidOperationException(_api.StrError(rc));
        }
    }

    public void Dispose()
    {
        if (_closed)
            return;

        _closed = true;

        try
        {
            long socket = 0;
            if (_api.GetInfoLong(_easy, 0x50002c, out socket) == 0 && socket > 0)
                Shutdown((int)socket, 2);
        }
        catch
        {
            // Best effort: closing the socket is only needed to wake curl_ws_recv.
        }

        if (_readerDone.Wait(2000))
        {
            _queue.CompleteAdding();
            try { _api.EasyCleanup(_easy); } catch { }
            try { Marshal.FreeHGlobal(_buffer); } catch { }
            _readerDone.Dispose();
        }
    }

    [DllImport("libc", SetLastError = false)]
    static extern int shutdown(int socket, int how);

    static void Shutdown(int socket, int how)
    {
        try { shutdown(socket, how); } catch { }
    }
}

internal sealed class CurlRequest
{
    public string Profile { get; set; }
    public string RequestUrl { get; set; }
    public string RequestMethod { get; set; } = "GET";
    public string RequestBody { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string[] HeaderOrder { get; set; }
    public string SessionId { get; set; }
    public bool? FollowRedirects { get; set; } = true;
    public int TimeoutMilliseconds { get; set; } = 30000;
    public bool? IsByteResponse { get; set; }
    public string StreamOutputPath { get; set; }
    public bool InsecureSkipVerify { get; set; }
    public string ProxyUrl { get; set; }
}

internal sealed class CurlResponse
{
    public int Status { get; set; }
    public string Body { get; set; }
    public Dictionary<string, string[]> Headers { get; set; }
    public Dictionary<string, string> Cookies { get; set; }
    public string SessionId { get; set; }
    public string Target { get; set; }
    public string UsedProtocol { get; set; }

    public string Header(string name)
    {
        if (Headers == null)
            return null;

        foreach (var pair in Headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                return pair.Value != null && pair.Value.Length > 0 ? pair.Value[0] : null;
        }

        return null;
    }
}

internal sealed class CurlCookie
{
    public string Name { get; set; }
    public string Value { get; set; }
    public string Domain { get; set; }
    public string Path { get; set; }
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
}

internal sealed class CurlWsConnect
{
    public string Url { get; set; }
    public string Profile { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string[] HeaderOrder { get; set; }
    public int HandshakeTimeoutMilliseconds { get; set; } = 30000;
    public bool InsecureSkipVerify { get; set; }
    public string ProxyUrl { get; set; }
}

internal sealed class CurlWsRead
{
    public string Data { get; set; }
    public int MessageType { get; set; }
    public bool TimedOut { get; set; }
    public bool Closed { get; set; }
    public string Error { get; set; }
}
