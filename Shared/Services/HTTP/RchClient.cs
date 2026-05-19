using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Models;
using Shared.Models.Events;
using Shared.Services.Pools.Json;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Shared.Services;

public class RchClientInfo
{
    public string host { get; set; }
    public string rchtype { get; set; }
    public int apkVersion { get; set; }
    public string player { get; set; }
}

public class RchClient
{
    #region static
    static bool logEnable => CoreInit.conf.serilog;
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<RchClient>();

    static readonly JsonSerializerOptions jsonTextOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    static readonly JsonSerializerSettings newtonsoftIgnoreErrorsSettings = new()
    {
        Error = static (se, ev) => { ev.ErrorContext.Handled = true; }
    };

    static readonly FrozenSet<string> excludedSendHeaders = new[]
    {
        "user-agent",
        "cookie",
        "referer",
        "origin",
        "accept",
        "accept-language",
        "accept-encoding",
        "cache-control",
        "dnt",
        "pragma",
        "priority",
        "upgrade-insecure-requests",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    static readonly ConcurrentDictionary<string, string> connectionsMsg = new();

    public static string ErrorMsg
        => CoreInit.conf.rch.enable ? "rhub не работает с данным балансером" : "Включите rch в init.conf";

    public record class hubEntry(string connectionId, string rchId, string url, string data, Dictionary<string, string> headers, bool returnHeaders);

    public static EventHandler<hubEntry> hub = null;

    public record class clientEntry(string ip, string host, RchClientInfo info, NwsConnection connection);

    public static readonly ConcurrentDictionary<string, clientEntry> clients = new();

    public record class rchIdEntry(MemoryStream ms, TaskCompletionSource<string> tcs, CancellationToken ct);

    public static readonly ConcurrentDictionary<string, rchIdEntry> rchIds = new();

    public static void Registry(string ip, string connectionId, string host = null, RchClientInfo info = null, NwsConnection connection = null)
    {
        if (info == null)
            info = new RchClientInfo();

        if (CoreInit.conf.rch.blacklistHost != null && info.host != null)
        {
            foreach (string h in CoreInit.conf.rch.blacklistHost)
            {
                if (info.host.Contains(h))
                    return;
            }
        }

        clients.AddOrUpdate(connectionId, new clientEntry(ip, host, info, connection), (i, j) => new clientEntry(ip, host, info, connection));

        if (EventListener.RchRegistry != null)
        {
            var em = new EventRchRegistry(connectionId, ip, host, info, connection);
            foreach (Action<EventRchRegistry> handler in EventListener.RchRegistry.GetInvocationList())
                handler(em);
        }
    }


    public static void OnDisconnected(string connectionId)
    {
        if (clients.TryRemove(connectionId, out _))
        {
            if (EventListener.RchDisconnected != null)
            {
                var em = new EventRchDisconnected(connectionId);
                foreach (Action<EventRchDisconnected> handler in EventListener.RchDisconnected.GetInvocationList())
                    handler(em);
            }
        }
    }
    #endregion

    BaseSettings init;

    HttpContext httpContext;

    string ip, connectionId;

    bool enableRhub, rhub_fallback;

    public bool enable => init != null && init.rhub && enableRhub;

    public string connectionMsg { get; private set; }

    public string ipkey(string key)
        => enableRhub ? $"ipkey:{key}:{ip}" : key;

    public string ipkey(string key, ProxyManager proxy)
        => $"ipkey:{key}:{(enableRhub ? ip : proxy?.CurrentProxyIp)}";

    public RchClient(string connectionId)
    {
        this.connectionId = connectionId;
    }

    public RchClient(HttpContext context, string host, BaseSettings init, RequestModel requestInfo, int? keepalive = null)
    {
        this.init = init;
        httpContext = context;
        enableRhub = init.rhub;
        rhub_fallback = init.rhub_fallback;
        ip = requestInfo.IP;

        if (enableRhub && rhub_fallback && init.rhub_geo_disable != null)
        {
            if (requestInfo.Country != null && init.rhub_geo_disable.Contains(requestInfo.Country))
            {
                enableRhub = false;
                init.rhub = false;
            }
        }

        if (connectionsMsg.TryGetValue(host, out string _msg))
        {
            connectionMsg = _msg;
        }
        else
        {
            connectionMsg = System.Text.Json.JsonSerializer.Serialize(new
            {
                rch = true,
                nws = $"{(host.StartsWith("https") ? "wss" : "ws")}://{Regex.Replace(host, "^https?://", "")}/nws"
            });

            if (!string.IsNullOrEmpty(connectionMsg))
                connectionsMsg[host] = connectionMsg;
        }
    }


    public void Disabled()
    {
        enableRhub = false;
    }


    #region Eval
    public void EvalRun(string data)
    {
        _ = SendHub("evalrun", data).ConfigureAwait(false);
    }

    public Task<string> Eval(string data)
        => SendHub("eval", data);

    async public Task<T> Eval<T>(string data, bool IgnoreDeserializeObject = false)
    {
        try
        {
            T result = default;

            await SendHub("eval", data, useDefaultHeaders: false, msAction: ms =>
            {
                try
                {
                    using (var streamReader = new JsonStreamReaderPool(ms, Encoding.UTF8, leaveOpen: true))
                    {
                        using (var jsonReader = new JsonTextReader(streamReader)
                        {
                            ArrayPool = NewtonsoftPool.Array
                        })
                        {
                            var serializer = IgnoreDeserializeObject
                                ? Newtonsoft.Json.JsonSerializer.Create(newtonsoftIgnoreErrorsSettings)
                                : Newtonsoft.Json.JsonSerializer.CreateDefault();

                            result = serializer.Deserialize<T>(jsonReader);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logEnable)
                        Log.Error(ex, "CatchId={CatchId}", "id_3c999oy2");
                }
            }).ConfigureAwait(false);

            return result;
        }
        catch
        {
            return default;
        }
    }
    #endregion

    #region Headers
    async public Task<(JObject headers, string currentUrl, string body)> Headers(string url, string data, IReadOnlyList<HeadersModel> headers = null, bool useDefaultHeaders = true)
    {
        try
        {
            // на версиях ниже java.lang.OutOfMemoryError
            if (484 > InfoConnected()?.apkVersion)
                return default;

            (JObject headers, string currentUrl, string body) result = default;

            await SendHub(url, data, headers, useDefaultHeaders, true, msAction: ms =>
            {
                try
                {
                    using (var streamReader = new JsonStreamReaderPool(ms, Encoding.UTF8, leaveOpen: true))
                    {
                        using (var jsonReader = new JsonTextReader(streamReader)
                        {
                            ArrayPool = NewtonsoftPool.Array
                        })
                        {
                            var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
                            var job = serializer.Deserialize<JObject>(jsonReader);
                            if (!job.ContainsKey("body"))
                                return;

                            result = (job.Value<JObject>("headers"), job.Value<string>("currentUrl"), job.Value<string>("body"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logEnable)
                        Log.Error(ex, "CatchId={CatchId}", "id_1zqgz3gk");
                }
            }).ConfigureAwait(false);

            return result;
        }
        catch
        {
            return default;
        }
    }
    #endregion

    #region Get
    public Task<string> Get(string url, IReadOnlyList<HeadersModel> headers = null, bool useDefaultHeaders = true)
        => SendHub(url, null, headers, useDefaultHeaders);

    async public Task<T> Get<T>(string url, IReadOnlyList<HeadersModel> headers = null, bool IgnoreDeserializeObject = false, bool useDefaultHeaders = true, bool textJson = false)
    {
        try
        {
            T result = default;

            await SendHub(url, null, headers, useDefaultHeaders, msAction: msm =>
            {
                try
                {
                    if (textJson)
                        result = System.Text.Json.JsonSerializer.Deserialize<T>(msm, jsonTextOptions);
                    else
                    {
                        using (var streamReader = new JsonStreamReaderPool(msm, Encoding.UTF8, leaveOpen: true))
                        {
                            using (var jsonReader = new JsonTextReader(streamReader)
                            {
                                ArrayPool = NewtonsoftPool.Array
                            })
                            {
                                var serializer = IgnoreDeserializeObject
                                    ? Newtonsoft.Json.JsonSerializer.Create(newtonsoftIgnoreErrorsSettings)
                                    : Newtonsoft.Json.JsonSerializer.CreateDefault();

                                result = serializer.Deserialize<T>(jsonReader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logEnable)
                        Log.Error(ex, "CatchId={CatchId}", "id_gznrsr3e");
                }
            }).ConfigureAwait(false);

            return result;
        }
        catch
        {
            return default;
        }
    }
    #endregion

    #region Span
    public Task GetSpan(string url, Action<ReadOnlySpan<char>> spanAction, IReadOnlyList<HeadersModel> headers = null, bool useDefaultHeaders = true)
        => SendHub(url, null, headers, useDefaultHeaders, spanAction: spanAction);

    public Task PostSpan(string url, Action<ReadOnlySpan<char>> spanAction, string data, IReadOnlyList<HeadersModel> headers = null, bool useDefaultHeaders = true)
        => SendHub(url, data, headers, useDefaultHeaders, spanAction: spanAction);
    #endregion

    #region Post
    public Task<string> Post(string url, string data, IReadOnlyList<HeadersModel> headers = null, bool useDefaultHeaders = true)
        => SendHub(url, data, headers, useDefaultHeaders);

    async public Task<T> Post<T>(string url, string data, IReadOnlyList<HeadersModel> headers = null, bool IgnoreDeserializeObject = false, bool useDefaultHeaders = true, bool textJson = false)
    {
        try
        {
            T result = default;

            await SendHub(url, data, headers, useDefaultHeaders, msAction: msm =>
            {
                try
                {
                    if (textJson)
                        result = System.Text.Json.JsonSerializer.Deserialize<T>(msm, jsonTextOptions);
                    else
                    {
                        using (var streamReader = new JsonStreamReaderPool(msm, Encoding.UTF8, leaveOpen: true))
                        {
                            using (var jsonReader = new JsonTextReader(streamReader)
                            {
                                ArrayPool = NewtonsoftPool.Array
                            })
                            {
                                var serializer = IgnoreDeserializeObject
                                    ? Newtonsoft.Json.JsonSerializer.Create(newtonsoftIgnoreErrorsSettings)
                                    : Newtonsoft.Json.JsonSerializer.CreateDefault();

                                result = serializer.Deserialize<T>(jsonReader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logEnable)
                        Log.Error(ex, "CatchId={CatchId}", "id_6gloiffn");
                }
            }).ConfigureAwait(false);

            return result;
        }
        catch
        {
            return default;
        }
    }
    #endregion

    #region SendHub
    async Task<string> SendHub(string url, string data = null, IReadOnlyList<HeadersModel> headers = null, bool useDefaultHeaders = true, bool returnHeaders = false, bool waiting = true, Action<ReadOnlySpan<char>> spanAction = null, Action<MemoryStream> msAction = null)
    {
        if (hub == null)
            return null;

        var clientInfo = SocketClient();
        connectionId = clientInfo.connectionId;

        if (string.IsNullOrEmpty(connectionId))
            return null;

        string rchId = Guid.NewGuid().ToString("N");

        var ms = PoolInvk.msm.GetStream();
        CancellationTokenSource cts = null;

        if (httpContext != null)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
        }
        else
        {
            cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        }

        try
        {
            var rchHub = new rchIdEntry(ms, new TaskCompletionSource<string>(), cts.Token);
            rchIds[rchId] = rchHub;

            #region send_headers
            Dictionary<string, string> send_headers = null;

            if (useDefaultHeaders && clientInfo.data?.info?.rchtype == "apk")
            {
                send_headers = new Dictionary<string, string>(Http.defaultUaHeaders, StringComparer.OrdinalIgnoreCase)
                {
                    { "accept-language", "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5" }
                };
            }

            if (headers != null)
            {
                if (send_headers == null)
                    send_headers = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var h in headers)
                    send_headers[h.name] = h.val;
            }

            if (send_headers != null && send_headers.Count > 0 && clientInfo.data?.info?.rchtype != "apk")
            {
                var new_headers = new Dictionary<string, string>(send_headers.Count);

                foreach (var h in send_headers)
                {
                    string key = h.Key;

                    if (key.StartsWith("sec-", StringComparison.OrdinalIgnoreCase) ||
                        excludedSendHeaders.Contains(key))
                        continue;

                    new_headers[key] = h.Value;
                }

                send_headers = new_headers;
            }
            #endregion

            hub.Invoke(null, new hubEntry(connectionId, rchId, url, data, Http.NormalizeHeaders(send_headers), returnHeaders));

            if (!waiting)
                return null;

            string stringValue = await rchHub.tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);

            if (stringValue != null)
            {
                if (string.IsNullOrWhiteSpace(stringValue))
                    return null;

                spanAction?.Invoke(stringValue);

                return stringValue;
            }
            else
            {
                if (ms.Length == 0)
                    return null;

                if (msAction != null)
                {
                    msAction.Invoke(ms);
                    return null;
                }

                string resultString = null;

                OwnerTo.Span(ms, Encoding.UTF8, span =>
                {
                    if (span.IsEmpty)
                        return;

                    if (spanAction != null)
                    {
                        spanAction.Invoke(span);
                        return;
                    }

                    resultString = span.ToString();
                });

                return resultString;
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            ms.Dispose();
            rchIds.TryRemove(rchId, out _);
            cts.Dispose();
        }
    }
    #endregion


    #region IsNotConnected
    public bool IsNotConnected()
    {
        if (!enableRhub)
            return false; // rch не используется

        if (IsCheckSearchRequest())
            return true; // заглушка для checksearch

        return SocketClient().connectionId == null;
    }
    #endregion

    #region IsRequiredConnected
    public bool IsRequiredConnected()
    {
        if (httpContext != null)
        {
            var requestInfo = httpContext.Features.Get<RequestModel>();
            if (requestInfo.IsLocalRequest)
                return false;
        }

        if (CoreInit.conf.rch.requiredConnected                       // Обязательное подключение
            || (init.rchstreamproxy != null && !init.streamproxy))   // Нужно знать rchtype устройства
            return SocketClient().connectionId == null;

        return false;
    }
    #endregion

    #region IsNotSupport
    public bool IsNotSupport(out string rch_msg)
    {
        rch_msg = null;

        if (!enableRhub)
            return false; // rch не используется

        if (IsCheckSearchRequest())
            return false; // заглушка для checksearch

        if (IsNotSupportRchAccess(init.RchAccessNotSupport(nocheck: true), out rch_msg))
            return true;

        return IsNotSupportStreamAccess(init.StreamAccessNotSupport(), out rch_msg);
    }

    public bool IsNotSupportRchAccess(string rch_deny, out string rch_msg)
    {
        rch_msg = null;

        if (rch_deny == null)
            return false;

        if (!enableRhub)
            return false; // rch не используется

        if (IsCheckSearchRequest())
            return false; // заглушка для checksearch

        var info = InfoConnected();
        if (info == null || string.IsNullOrEmpty(info.rchtype))
            return false; // клиент не в сети

        // разрешен возврат на сервер
        if (rhub_fallback)
        {
            if (rch_deny.Contains(info.rchtype))
            {
                enableRhub = false;
                init.rhub = false;
            }
            return false;
        }

        // указан webcorshost или включен corseu
        if (!string.IsNullOrWhiteSpace(init.webcorshost) || init.corseu)
            return false;

        if (CoreInit.conf.rch.notSupportMsg != null)
            rch_msg = CoreInit.conf.rch.notSupportMsg;
        else if (info.rchtype == "web")
            rch_msg = "На MSX недоступно";
        else
            rch_msg = "Только на android";

        return rch_deny.Contains(info.rchtype);
    }

    public bool IsNotSupportStreamAccess(string deny, out string rch_msg)
    {
        rch_msg = null;

        if (deny == null)
            return false;

        if (!enableRhub)
            return false; // rch не используется

        if (IsCheckSearchRequest())
            return false; // заглушка для checksearch

        var info = InfoConnected();
        if (info == null || string.IsNullOrEmpty(info.rchtype))
            return false; // клиент не в сети

        if (CoreInit.conf.rch.notSupportMsg != null)
            rch_msg = CoreInit.conf.rch.notSupportMsg;
        else if (info.rchtype == "web")
            rch_msg = "На MSX недоступно";
        else
            rch_msg = "Только на android";

        return deny.Contains(info.rchtype);
    }
    #endregion

    #region IsCheckSearchRequest
    bool IsCheckSearchRequest()
    {
        return httpContext != null
            && httpContext.Request.Query.TryGetValue("checksearch", out StringValues checksearch)
            && checksearch.Count > 0
            && checksearch[0] == "true";
    }
    #endregion

    #region InfoConnected
    public RchClientInfo InfoConnected()
    {
        return SocketClient().data?.info;
    }
    #endregion

    #region SocketClient
    public (string connectionId, clientEntry data) SocketClient()
    {
        if (!string.IsNullOrEmpty(connectionId) && clients.TryGetValue(connectionId, out var _client))
        {
            if (CoreInit.conf.rch.autoReconnect || ip == _client.ip)
                return (connectionId, _client);
        }

        if (httpContext != null && httpContext.Request.Query.TryGetValue("nws_id", out StringValues _nwsid) && _nwsid.Count > 0)
        {
            string nws_id = _nwsid[0];
            if (!string.IsNullOrEmpty(nws_id) && clients.TryGetValue(nws_id, out _client))
            {
                if (CoreInit.conf.rch.autoReconnect || ip == _client.ip)
                    return (nws_id, _client);
            }
        }

        return default;
    }
    #endregion
}
