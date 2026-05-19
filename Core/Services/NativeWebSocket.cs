using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services;
using Shared.Services.Pools;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Services;

public class NativeWebSocket : INws
{
    #region fields
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NativeWebSocket>();

    sealed record NwsSendModel(string method, object[] args);

    static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    public static readonly ConcurrentDictionary<string, NwsConnection> _connections = new();

    static readonly Timer ConnectionMonitorTimer = new Timer(ConnectionMonitorCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5));

    static readonly ConcurrentDictionary<long, CounterNws> _statsBySecond = new();

    static readonly Timer StatsCleanupTimer = new Timer(CleanupStatsCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    public static int CountConnection => _connections.Count;
    #endregion

    #region interface
    public Task SendAsync(string connectionId, string method, params object[] args)
    {
        if (connectionId != null && _connections.TryGetValue(connectionId, out var client))
            return SendAsync(client, method, args);

        return Task.CompletedTask;
    }

    public ConcurrentDictionary<string, NwsConnection> AllConnections()
        => _connections;
    #endregion


    #region handle
    public static async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using (var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false))
        {
            string connectionId = null;

            if (context.Request.Query.TryGetValue("id", out StringValues _connectionId) && _connectionId.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(_connectionId[0]))
                {
                    connectionId = _connectionId[0];
                    if (_connections.TryRemove(connectionId, out var _conn))
                    {
                        Interlocked.Exchange(ref _conn.SendCancel, false);
                        _conn.Cancel();
                        _conn.Dispose();
                    }
                }
            }

            if (connectionId == null)
                connectionId = Guid.NewGuid().ToString("N");

            NwsConnection connection = null;

            try
            {
                #region minimal version
                int clientVersion = GetClientVersion(context);
                if (CoreInit.conf.WebSocket.minVersion > clientVersion)
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        string errorVersion = $"ws_version_too_low:{clientVersion}:{CoreInit.conf.WebSocket.minVersion}";
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, errorVersion, cts.Token).ConfigureAwait(false);
                    }

                    return;
                }
                #endregion

                var requestInfo = context.Features.Get<RequestModel>();

                connection = new NwsConnection(connectionId, socket, CoreInit.Host(context), requestInfo);

                var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                connection.SetCancellationSource(cancellationSource);

                connection.UpdateActivity();
                connection.UpdateSendActivity();

                _connections.AddOrUpdate(connectionId, connection, (k, v) => connection);

                await SendAsync(connection, "Connected", connectionId).ConfigureAwait(false);

                if (EventListener.NwsConnected != null)
                {
                    var em = new EventNwsConnected(connectionId, requestInfo, connection, cancellationSource.Token);
                    foreach (Action<EventNwsConnected> handler in EventListener.NwsConnected.GetInvocationList())
                        handler(em);
                }

                await ReceiveLoopAsync(connection, cancellationSource.Token).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_v0c6a6ty");
            }
            finally
            {
                if (connection != null)
                {
                    if (connection.SendCancel)
                    {
                        _connections.TryRemove(connectionId, out _);
                        connection.Cancel();
                        connection.Dispose();

                        RchClient.OnDisconnected(connectionId);

                        if (EventListener.NwsDisconnected != null)
                        {
                            var em = new EventNwsDisconnected(connectionId);
                            foreach (Action<EventNwsDisconnected> handler in EventListener.NwsDisconnected.GetInvocationList())
                                handler(em);
                        }
                    }
                }
            }
        }
    }

    static int GetClientVersion(HttpContext context)
    {
        if (context?.Request?.Query.TryGetValue("ver", out StringValues wsVersion) == true && wsVersion.Count > 0)
        {
            if (int.TryParse(wsVersion[0], out int version))
                return version;
        }

        return 1;
    }
    #endregion


    #region receive loop
    static readonly ConcurrentBag<byte[]> _poolReceiveLoop = new();

    static async Task ReceiveLoopAsync(NwsConnection connection, CancellationToken token)
    {
        WebSocket socket = connection.Socket;

        if (!_poolReceiveLoop.TryTake(out byte[] buffer))
            buffer = new byte[1024];

        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                BufferWriterPool<byte> writer = null;

                try
                {
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (token.IsCancellationRequested)
                            return;

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                                await socket.CloseAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, cts.Token).ConfigureAwait(false);
                            return;
                        }

                        if (result.Count > 0 && result.MessageType == WebSocketMessageType.Text)
                        {
                            connection.UpdateActivity();

                            if (result.EndOfMessage && result.Count == 4 &&
                                buffer[0] == (byte)'p' &&
                                buffer[1] == (byte)'i' &&
                                buffer[2] == (byte)'n' &&
                                buffer[3] == (byte)'g')
                            {
                                if (CoreInit.conf.WebSocket.send_pong)
                                    await SendPongAsync(connection).ConfigureAwait(false);
                                break;
                            }

                            if (writer == null)
                                writer = new BufferWriterPool<byte>();

                            if (writer.WrittenCount + result.Count > CoreInit.conf.WebSocket.MaximumReceiveMessageSize)
                            {
                                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "payload too large", cts.Token).ConfigureAwait(false);
                                return;
                            }

                            var wrmem = writer.GetMemory(result.Count);
                            buffer.AsMemory(0, result.Count).CopyTo(wrmem);
                            writer.Advance(result.Count);
                        }
                    }
                    while (!result.EndOfMessage);

                    if (writer == null || writer.WrittenCount == 0)
                        continue;

                    if (CoreInit.conf.openstat.enable)
                        IncrementStats(isReceive: true);

                    var payload = writer.WrittenMemory;

                    using (JsonDocument document = JsonDocument.Parse(payload))
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Object)
                            continue;

                        if (!document.RootElement.TryGetProperty("method", out var methodProp))
                            continue;

                        string method = methodProp.GetString();
                        JsonElement args = default;

                        if (document.RootElement.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Array)
                            args = argsProp;

                        if (EventListener.NwsMessage != null)
                        {
                            var em = new EventNwsMessage(connection.ConnectionId, payload, method, args);
                            foreach (Action<EventNwsMessage> handler in EventListener.NwsMessage.GetInvocationList())
                                handler(em);
                        }

                        await InvokeAsync(connection, method, args).ConfigureAwait(false);
                    }
                }
                catch (System.Net.WebSockets.WebSocketException) { /*The remote party closed the WebSocket connection without completing the close handshake.*/ }
                catch (System.OperationCanceledException) { /*The operation was canceled*/ }
                catch (System.Exception ex)
                {
                    if (token.IsCancellationRequested)
                        return;

                    Log.Error(ex, "CatchId={CatchId}", "id_jzimtzze");
                }
                finally
                {
                    writer?.Dispose();
                }
            }
        }
        catch (System.Net.WebSockets.WebSocketException) { }
        catch (OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_zj52xvn1");
        }
        finally
        {
            _poolReceiveLoop.Add(buffer);
        }
    }
    #endregion

    #region message handling
    static async Task InvokeAsync(NwsConnection connection, string method, JsonElement args)
    {
        if (string.IsNullOrEmpty(method))
            return;

        var ws = CoreInit.conf.WebSocket;

        switch (method.ToLower())
        {
            case "rchregistry":
                if (CoreInit.conf.rch.enable && ws.rch)
                {
                    if (args.ValueKind == JsonValueKind.Array && args.GetArrayLength() > 0)
                    {
                        try
                        {
                            var info = args[0].Deserialize<RchClientInfo>();
                            RchClient.Registry(connection.Ip, connection.ConnectionId, connection.Host, info, connection);
                            await SendAsync(connection, "RchRegistry", connection.Ip, connection.ConnectionId, info.rchtype).ConfigureAwait(false);
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_8j50lr31");
                        }
                    }
                }
                break;

            case "rchresult":
                if (CoreInit.conf.rch.enable && ws.rch)
                {
                    string id = GetStringArg(args, 0);
                    string value = GetStringArg(args, 1) ?? string.Empty;

                    if (!string.IsNullOrEmpty(id) && RchClient.rchIds.TryGetValue(id, out var rchHub))
                        rchHub.tcs.TrySetResult(value);
                }
                break;
        }
    }

    static string GetStringArg(JsonElement args, int index)
    {
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index)
            return null;

        var element = args[index];
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();

        if (element.ValueKind == JsonValueKind.Null)
            return null;

        return element.ToString();
    }
    #endregion


    #region SendAsync
    static async Task SendAsync(NwsConnection connection, string method, params object[] args)
    {
        if (connection.Socket.State != WebSocketState.Open || string.IsNullOrEmpty(method))
            return;

        bool lockTaken = false;

        try
        {
            if (CoreInit.conf.openstat.enable)
                IncrementStats(isReceive: false);

            lockTaken = await connection.SendLock.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            if (!lockTaken || connection.Socket.State != WebSocketState.Open)
                return;

            using (var utf8Buf = new BufferWriterPool<byte>())
            {
                using (var writer = new Utf8JsonWriter(utf8Buf))
                {
                    JsonSerializer.Serialize(
                        writer,
                        new NwsSendModel(method, args ?? Array.Empty<object>()),
                        serializerOptions
                    );
                }

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    await connection.Socket
                        .SendAsync(utf8Buf.WrittenMemory, WebSocketMessageType.Text, true, cts.Token)
                        .ConfigureAwait(false);
                }

                connection.UpdateActivity();
                connection.UpdateSendActivity();
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_co2znzo7");
        }
        finally
        {
            if (lockTaken)
                connection.SendLock.Release();
        }
    }

    public static Task SendRchRequestAsync(string connectionId, string rchId, string url, string data, Dictionary<string, string> headers, bool returnHeaders)
    {
        if (string.IsNullOrEmpty(connectionId))
            return Task.CompletedTask;

        if (_connections.TryGetValue(connectionId, out var client))
            return SendAsync(client, "RchClient", rchId, url, data, headers, returnHeaders);

        return Task.CompletedTask;
    }
    #endregion

    #region SendPongAsync
    static readonly Memory<byte> _pongMessage = new byte[] { (byte)'p', (byte)'o', (byte)'n', (byte)'g' }.AsMemory();

    static async Task SendPongAsync(NwsConnection connection)
    {
        if (connection?.Socket == null || connection.Socket.State != WebSocketState.Open)
            return;

        bool lockTaken = false;

        try
        {
            lockTaken = await connection.SendLock.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            if (!lockTaken || connection.Socket.State != WebSocketState.Open)
                return;

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                await connection.Socket.SendAsync(_pongMessage, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);

            connection.UpdateActivity();
            connection.UpdateSendActivity();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_kn2rc95q");
        }
        finally
        {
            if (lockTaken)
                connection.SendLock.Release();
        }
    }
    #endregion


    #region ConnectionMonitorCallback
    static int _updatingMonitorCallback = 0;

    static void ConnectionMonitorCallback(object state)
    {
        if (_connections.IsEmpty)
            return;

        if (Interlocked.Exchange(ref _updatingMonitorCallback, 1) == 1)
            return;

        try
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddSeconds(-110); // ping каждые 50 секунд

            int inactiveAfterMinutes = CoreInit.conf.WebSocket.inactiveAfterMinutes;
            var inactiveCutoff = inactiveAfterMinutes > 0 ? now.AddMinutes(-inactiveAfterMinutes) : now;

            foreach (var connection in _connections)
            {
                if (cutoff >= connection.Value.LastActivityUtc)
                {
                    connection.Value.Cancel();
                    continue;
                }

                if (inactiveAfterMinutes > 0)
                {
                    if (inactiveCutoff >= connection.Value.LastSendActivityUtc)
                        connection.Value.Cancel();
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_zjf5fn31");
        }
        finally
        {
            Volatile.Write(ref _updatingMonitorCallback, 0);
        }
    }
    #endregion

    #region FullDispose
    public static void FullDispose()
    {
        ConnectionMonitorTimer.Dispose();
        StatsCleanupTimer.Dispose();
        foreach (var connection in _connections)
            connection.Value.Cancel();
    }
    #endregion


    #region Stats Last Minute
    static void IncrementStats(bool isReceive)
    {
        long nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var counter = _statsBySecond.GetOrAdd(nowSecond, static _ => new CounterNws());

        if (isReceive)
            Interlocked.Increment(ref counter.receive);
        else
            Interlocked.Increment(ref counter.send);
    }

    public static CounterNws GetStatsLastMinute()
    {
        long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 59;
        var result = new CounterNws();

        foreach (var pair in _statsBySecond)
        {
            if (pair.Key < cutoff)
                continue;

            result.receive += Volatile.Read(ref pair.Value.receive);
            result.send += Volatile.Read(ref pair.Value.send);
        }

        return result;
    }

    static void CleanupStatsCallback(object state)
    {
        long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 59;

        foreach (var pair in _statsBySecond)
        {
            if (pair.Key < cutoff)
                _statsBySecond.TryRemove(pair.Key, out _);
        }
    }
    #endregion
}


public class CounterNws
{
    public int receive;

    public int send;
}
