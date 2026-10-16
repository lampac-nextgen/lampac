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
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Services;

public class NativeWebSocket : INws
{
    #region fields
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NativeWebSocket>();

    static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    public static readonly ConcurrentDictionary<string, NwsConnection> _connections = new();

    static readonly Timer ConnectionMonitorTimer = new Timer(ConnectionMonitorCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));

    static readonly TimeSpan sendLockTimeout = TimeSpan.FromSeconds(20);
    static readonly TimeSpan sendTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan closeTimeout = TimeSpan.FromSeconds(5);

    static readonly Memory<byte> pongMessage = new byte[] { (byte)'p', (byte)'o', (byte)'n', (byte)'g' }.AsMemory();

    public static int CountConnection
        => _connections.Count;
    #endregion

    #region stats
    static readonly CounterNws[] statsBySecond = InitStats();

    static CounterNws[] InitStats()
    {
        var arr = new CounterNws[60];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = new CounterNws();

        return arr;
    }

    static long currentUnixSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    static readonly Timer StatsClockTimer = new Timer(_ =>
    {
        Volatile.Write(ref currentUnixSecond, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
    #endregion

    #region interface
    public Task SendAsync(string connectionId, string method, params object[] args)
    {
        if (connectionId != null && _connections.TryGetValue(connectionId, out var client))
            return SendAsync(client, method, args);

        return Task.CompletedTask;
    }

    public Task SendAsync(string connectionId, string method, CancellationToken cancellationToken, params object[] args)
    {
        if (connectionId != null && _connections.TryGetValue(connectionId, out var client))
            return SendAsync(client, method, cancellationToken, args);

        return Task.CompletedTask;
    }

    public Task SendAsync(string connectionId, ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        if (connectionId != null && _connections.TryGetValue(connectionId, out var client))
            return SendAsync(client, buffer, messageType, endOfMessage, cancellationToken);

        return Task.CompletedTask;
    }

    public Task SendConnectionAsync(NwsConnection connection, ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        => SendAsync(connection, buffer, messageType, endOfMessage, cancellationToken);

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
                if (!string.IsNullOrEmpty(_connectionId[0]))
                {
                    connectionId = _connectionId[0];
                    if (_connections.TryRemove(connectionId, out var _conn))
                    {
                        Interlocked.Exchange(ref _conn.SendCancelFlag, 0);
                        _conn.Cancel();
                        _conn.Dispose();
                    }
                }
            }

            if (connectionId == null)
                connectionId = Guid.NewGuid().ToString("N");

            NwsConnection connection = null;
            var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

            try
            {
                #region minimal version
                int clientVersion = GetClientVersion(context);
                if (CoreInit.conf.WebSocket.minVersion > clientVersion)
                {
                    using (var cts = new CancellationTokenSource(closeTimeout))
                    {
                        string errorVersion = $"ws_version_too_low:{clientVersion}:{CoreInit.conf.WebSocket.minVersion}";
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, errorVersion, cts.Token).ConfigureAwait(false);
                    }

                    return;
                }
                #endregion

                var requestInfo = context.Features.Get<RequestModel>();

                connection = new NwsConnection(connectionId, socket, CoreInit.Host(context), requestInfo);
                connection.SetCancellationSource(cancellationSource);

                connection.UpdateActivity();
                connection.UpdateSendActivity();

                _connections[connectionId] = connection;

                #region Send Connected
                using (var utf8Buf = new BufferWriterPool<byte>(BufferWriterPoolType.Tiny))
                {
                    using (var ujw = new Utf8JsonWriter(utf8Buf))
                    {
                        ujw.WriteStartObject();

                        ujw.WriteString("method"u8, "Connected");

                        ujw.WriteStartArray("args"u8);
                        ujw.WriteStringValue(connectionId);
                        ujw.WriteEndArray();

                        ujw.WriteEndObject();
                    }

                    await SendAsync(connection, utf8Buf.WrittenMemory, WebSocketMessageType.Text, true, cancellationSource.Token).ConfigureAwait(false);
                }
                #endregion

                #region EventListener
                if (EventListener.NwsConnected != null)
                {
                    var em = new EventNwsConnected(connectionId, requestInfo, connection, cancellationSource.Token);
                    foreach (Action<EventNwsConnected> handler in EventListener.NwsConnected.GetInvocationList())
                    {
                        try
                        {
                            handler(em);
                        }
                        catch { }
                    }
                }
                #endregion

                await ReceiveLoopAsync(connection, cancellationSource.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_v0c6a6ty");
            }
            finally
            {
                #region OnDisconnected
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
                            {
                                try
                                {
                                    handler(em);
                                }
                                catch { }
                            }
                        }
                    }
                }
                #endregion

                cancellationSource?.Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetClientVersion(HttpContext context)
    {
        if (context?.Request?.Query.TryGetValue("ver", out StringValues wsVersion) == true && wsVersion.Count > 0)
        {
            return wsVersion[0] switch
            {
                "1" => 1,
                "2" => 2,
                _ => 1
            };
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
                int maxMessageSize = CoreInit.conf.WebSocket.MaximumReceiveMessageSize;

                try
                {
                    ReadOnlyMemory<byte> jsonResult = default;
                    WebSocketReceiveResult result;

                    #region Receive
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (token.IsCancellationRequested)
                            return;

                        #region WebSocket Close or Binary
                        try
                        {
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                using (var cts = new CancellationTokenSource(closeTimeout))
                                {
                                    if (socket.State == WebSocketState.CloseReceived ||
                                        socket.State == WebSocketState.Open)
                                    {
                                        await socket.CloseAsync(
                                            result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                                            result.CloseStatusDescription,
                                            cts.Token
                                        ).ConfigureAwait(false);
                                    }
                                }

                                return;
                            }

                            if (result.MessageType == WebSocketMessageType.Binary)
                            {
                                using (var cts = new CancellationTokenSource(closeTimeout))
                                {
                                    await socket.CloseAsync(
                                        WebSocketCloseStatus.InvalidMessageType,
                                        "binary messages are not supported",
                                        cts.Token
                                    ).ConfigureAwait(false);
                                }
                                return;
                            }
                        }
                        catch { }
                        #endregion

                        if (result.Count > 0 && result.MessageType == WebSocketMessageType.Text)
                        {
                            connection.UpdateActivity();

                            #region ping/pong
                            if (result.EndOfMessage && result.Count == 4 &&
                                buffer[0] == (byte)'p' &&
                                buffer[1] == (byte)'i' &&
                                buffer[2] == (byte)'n' &&
                                buffer[3] == (byte)'g')
                            {
                                if (CoreInit.conf.WebSocket.send_pong)
                                    await SendAsync(connection, pongMessage, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                                break;
                            }
                            #endregion

                            if (writer == null && result.EndOfMessage)
                            {
                                // весь json в buffer
                                jsonResult = buffer.AsMemory(0, result.Count);
                            }
                            else
                            {
                                writer ??= new BufferWriterPool<byte>(BufferWriterPoolType.Tiny);

                                if (writer.WrittenCount + result.Count > maxMessageSize)
                                {
                                    using (var cts = new CancellationTokenSource(closeTimeout))
                                        await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "payload too large", cts.Token).ConfigureAwait(false);
                                    return;
                                }

                                var wrmem = writer.GetMemory(result.Count);
                                buffer.AsMemory(0, result.Count).CopyTo(wrmem);
                                writer.Advance(result.Count);
                            }
                        }
                    }
                    while (!result.EndOfMessage);
                    #endregion

                    if (writer != null && writer.WrittenCount > 0)
                        jsonResult = writer.WrittenMemory;

                    if (jsonResult.IsEmpty)
                        continue;

                    if (jsonResult.Length == 4 &&
                        jsonResult.Span.SequenceEqual("ping"u8))
                    {
                        if (CoreInit.conf.WebSocket.send_pong)
                            await SendAsync(connection, pongMessage, WebSocketMessageType.Text, true, token);
                        continue;
                    }

                    var rmsg = ReadMessage(jsonResult.Span);
                    if (rmsg.method == NwsMessageMethod.error)
                        continue;

                    if (EventListener.NwsMessage == null && rmsg.method == NwsMessageMethod.Unknown)
                        continue;

                    if (CoreInit.conf.openstat.enable)
                        IncrementStats(isReceive: true);

                    var ws = CoreInit.conf.WebSocket;

                    switch (rmsg.method)
                    {
                        #region RchRegistry
                        case NwsMessageMethod.RchRegistry:
                            {
                                if (CoreInit.conf.rch.enable && ws.rch)
                                {
                                    writer?.Dispose();

                                    var info = rmsg.registryInfo;
                                    RchClient.Registry(connection.Ip, connection.ConnectionId, connection.Host, info, connection);

                                    using (var utf8Buf = new BufferWriterPool<byte>(BufferWriterPoolType.Tiny))
                                    {
                                        using (var ujw = new Utf8JsonWriter(utf8Buf))
                                        {
                                            ujw.WriteStartObject();

                                            ujw.WriteString("method"u8, "RchRegistry");

                                            ujw.WriteStartArray("args"u8);
                                            ujw.WriteStringValue(connection.Ip);
                                            ujw.WriteStringValue(connection.ConnectionId);
                                            ujw.WriteStringValue(info?.rchtype);
                                            ujw.WriteEndArray();

                                            ujw.WriteEndObject();
                                        }

                                        await SendAsync(connection, utf8Buf.WrittenMemory, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                                    }
                                }
                            }
                            break;
                        #endregion

                        #region RchResult
                        case NwsMessageMethod.RchResult:
                            {
                                if (CoreInit.conf.rch.enable && ws.rch)
                                {
                                    if (RchClient.rchIds.TryGetValue(rmsg.resultId, out var rchHub) &&
                                        (string.IsNullOrEmpty(rchHub.expectedConnectionId) || rchHub.expectedConnectionId == connection.ConnectionId) &&
                                        (string.IsNullOrEmpty(rchHub.expectedIp) || rchHub.expectedIp == connection.Ip))
                                        rchHub.tcs.TrySetResult(rmsg.resultValue ?? string.Empty);
                                }
                            }
                            break;
                        #endregion

                        #region EventListener
                        case NwsMessageMethod.Unknown:
                            {
                                using (JsonDocument document = JsonDocument.Parse(jsonResult))
                                {
                                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                                        continue;

                                    if (!document.RootElement.TryGetProperty("method", out var methodProp))
                                        continue;

                                    string method = methodProp.GetString();
                                    if (string.IsNullOrEmpty(method))
                                        continue;

                                    JsonElement args = default;

                                    if (document.RootElement.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Array)
                                        args = argsProp;

                                    var em = new EventNwsMessage(connection.ConnectionId, method, args);
                                    foreach (Action<EventNwsMessage> handler in EventListener.NwsMessage.GetInvocationList())
                                    {
                                        try
                                        {
                                            handler(em);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Error(ex, "NwsMessage method={method} failed", method);
                                        }
                                    }
                                }
                            }
                            break;
                        #endregion
                    }
                }
                catch (WebSocketException) { /*The remote party closed the WebSocket connection without completing the close handshake.*/ }
                catch (OperationCanceledException) { /*The operation was canceled*/ }
                catch (Exception ex)
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
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_zj52xvn1");
        }
        finally
        {
            _poolReceiveLoop.Add(buffer);
        }
    }
    #endregion

    #region ReadMessage
    static (NwsMessageMethod method, RchClientInfo registryInfo, string resultId, string resultValue) ReadMessage(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, isFinalBlock: true, state: default);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return (NwsMessageMethod.error, null, null, null);

        var method = NwsMessageMethod.error;
        RchClientInfo registryInfo = null;
        string resultId = null;
        string resultValue = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                return (NwsMessageMethod.error, null, null, null);

            if (reader.ValueTextEquals("method"u8))
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                    return (NwsMessageMethod.error, null, null, null);

                if (reader.ValueTextEquals("RchRegistry"u8) || reader.ValueTextEquals("rchregistry"u8))
                    method = NwsMessageMethod.RchRegistry;
                else if (reader.ValueTextEquals("RchResult"u8) || reader.ValueTextEquals("rchresult"u8))
                    method = NwsMessageMethod.RchResult;
                else
                {
                    method = NwsMessageMethod.Unknown;
                    break;
                }
            }
            else if (reader.ValueTextEquals("args"u8))
            {
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                {
                    reader.Skip();
                    continue;
                }

                if (method == NwsMessageMethod.RchRegistry)
                {
                    if (!reader.Read())
                        break;

                    if (reader.TokenType != JsonTokenType.StartObject)
                        break;

                    try
                    {
                        registryInfo = JsonSerializer.Deserialize<RchClientInfo>(ref reader);
                    }
                    catch { }

                    break;
                }
                else if (method == NwsMessageMethod.RchResult)
                {
                    if (!reader.Read())
                        return (NwsMessageMethod.error, null, null, null);

                    resultId = GetStringArg(ref reader);
                    if (string.IsNullOrEmpty(resultId))
                        return (NwsMessageMethod.error, null, null, null);

                    if (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        resultValue = GetStringArg(ref reader);

                    break;
                }
                else
                {
                    reader.Skip();
                }
            }
            else
            {
                reader.Skip();
            }
        }

        return (method, registryInfo, resultId, resultValue);
    }
    #endregion

    #region GetStringArg
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetStringArg(ref Utf8JsonReader reader)
    {
        return reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : null;
    }
    #endregion


    #region SendAsync
    async static Task SendAsync(NwsConnection connection, string method, params object[] args)
    {
        using (var cts = new CancellationTokenSource(sendTimeout))
            await SendAsync(connection, method, cts.Token, args).ConfigureAwait(false);
    }

    async static Task SendAsync(NwsConnection connection, string method, CancellationToken cancellationToken, params object[] args)
    {
        if (string.IsNullOrEmpty(method))
            return;

        using (var utf8Buf = new BufferWriterPool<byte>(BufferWriterPoolType.Tiny))
        {
            using (var writer = new Utf8JsonWriter(utf8Buf))
            {
                writer.WriteStartObject();

                writer.WriteString("method"u8, method);

                writer.WritePropertyName("args"u8);
                JsonSerializer.Serialize(writer, args ?? Array.Empty<object>(), serializerOptions);

                writer.WriteEndObject();
            }

            await SendAsync(connection, utf8Buf.WrittenMemory, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
    }

    static async Task SendAsync(NwsConnection connection, ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        var socket = connection?.Socket;
        if (socket == null || socket.State != WebSocketState.Open || buffer.IsEmpty)
            return;

        bool lockTaken = false;

        try
        {
            lockTaken = await connection.SendLock.WaitAsync(sendLockTimeout, cancellationToken).ConfigureAwait(false);
            if (!lockTaken || socket.State != WebSocketState.Open)
                return;

            await socket
                .SendAsync(buffer, messageType, endOfMessage, cancellationToken)
                .ConfigureAwait(false);

            if (CoreInit.conf.openstat.enable)
                IncrementStats(isReceive: false);

            connection.UpdateActivity();
            connection.UpdateSendActivity();
        }
        catch (WebSocketException ex)
        {
            Log.Warning(ex, "WebSocket send failed. ConnectionId={ConnectionId}", connection.ConnectionId);
            connection.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            Log.Warning(ex, "WebSocket already disposed. ConnectionId={ConnectionId}", connection.ConnectionId);
            connection.Cancel();
        }
        catch (OperationCanceledException)
        {
            connection.Cancel();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_co2znzo7");
            connection.Cancel();
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
        catch (Exception ex)
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
        StatsClockTimer.Dispose();

        foreach (var connection in _connections)
            connection.Value.Cancel();
    }
    #endregion


    #region Stats Last Minute
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IncrementStats(bool isReceive)
    {
        long nowSecond = Volatile.Read(ref currentUnixSecond);
        int index = (int)(nowSecond % 60);
        var slot = statsBySecond[index];

        if (Volatile.Read(ref slot.second) != nowSecond)
        {
            Volatile.Write(ref slot.receive, 0);
            Volatile.Write(ref slot.send, 0);
            Volatile.Write(ref slot.second, nowSecond);
        }

        if (isReceive)
            Interlocked.Increment(ref slot.receive);
        else
            Interlocked.Increment(ref slot.send);
    }

    public static CounterNws GetStatsLastMinute()
    {
        long nowSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long cutoff = nowSecond - 59;

        var result = new CounterNws();

        for (int i = 0; i < statsBySecond.Length; i++)
        {
            var slot = statsBySecond[i];
            long second = Volatile.Read(ref slot.second);

            if (second < cutoff || second > nowSecond)
                continue;

            result.receive += Volatile.Read(ref slot.receive);
            result.send += Volatile.Read(ref slot.send);
        }

        return result;
    }
    #endregion
}


public class CounterNws
{
    public long second;
    public int receive;
    public int send;
}

enum NwsMessageMethod
{
    Unknown,
    RchRegistry,
    RchResult,
    error
}
