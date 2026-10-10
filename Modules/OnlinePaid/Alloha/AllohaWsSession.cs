using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Alloha;

public class AllohaWsSession : IDisposable
{
    private readonly string _wsUrl;
    private readonly string _sid;
    private readonly string _origin;
    private readonly CancellationTokenSource _cts = new();

    private ClientWebSocket _ws;
    private Task _workerTask;
    private DateTime _lastActiveAt = DateTime.UtcNow;
    private bool _disposed;

    public string EdgeHash { get; private set; }
    public string Sid => _sid;

    private const string ChromeUa = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    public AllohaWsSession(string wsUrl, string sid, string origin)
    {
        _wsUrl = wsUrl;
        _sid = sid;
        _origin = origin.TrimEnd('/');
    }

    public void Start()
    {
        if (_workerTask == null && !_disposed)
        {
            _workerTask = Task.Run(RunLoopAsync);
        }
    }

    public void Touch()
    {
        _lastActiveAt = DateTime.UtcNow;
    }

    private async Task RunLoopAsync()
    {
        int maxAttempts = 20;
        int attempt = 0;

        while (!_cts.IsCancellationRequested && attempt < maxAttempts)
        {
            if (DateTime.UtcNow - _lastActiveAt > TimeSpan.FromMinutes(15))
                break;

            if (attempt > 0)
            {
                int delaySec = Math.Min((int)Math.Pow(2, attempt - 1), 30);
                await Task.Delay(TimeSpan.FromSeconds(delaySec), _cts.Token);
            }

            attempt++;

            try
            {
                await ConnectAndStreamAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // reconnect on connection failure
            }
        }
    }

    private async Task ConnectAndStreamAsync()
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string wsEndpoint = $"{_wsUrl}?sid={Uri.EscapeDataString(_sid)}&v=2.1&t={ts}";

        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Origin", _origin);
        _ws.Options.SetRequestHeader("User-Agent", ChromeUa);
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        linkedCts.CancelAfter(TimeSpan.FromHours(4));

        await _ws.ConnectAsync(new Uri(wsEndpoint), linkedCts.Token);

        // 1. Handshake messages
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string playbackStartMsg = JsonSerializer.Serialize(new
        {
            type = "playback_start",
            current_time = 0,
            resolution = "1080",
            track_id = "1",
            speed = 1,
            subtitle = -1,
            ts = now
        });
        await SendTextAsync(_ws, playbackStartMsg, linkedCts.Token);

        string initMsg = JsonSerializer.Serialize(new
        {
            type = "init",
            current_time = 0,
            resolution = "1080",
            track_id = "1",
            speed = 1,
            subtitle = -1,
            ts = now + 1
        });
        await SendTextAsync(_ws, initMsg, linkedCts.Token);

        // 2. Start heartbeat loop task (every 30 seconds)
        using var heartbeatCts = new CancellationTokenSource();
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), heartbeatCts.Token);
                    if (_ws.State != WebSocketState.Open)
                        break;

                    long curTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string playingMsg = JsonSerializer.Serialize(new
                    {
                        type = "playing",
                        current_time = 0,
                        resolution = "1080",
                        track_id = "1",
                        speed = 1,
                        subtitle = -1,
                        ts = curTs
                    });
                    await SendTextAsync(_ws, playingMsg, heartbeatCts.Token);
                }
                catch
                {
                    break;
                }
            }
        });

        // 3. Receive messages and extract edge_hash
        var buffer = new byte[8192];
        try
        {
            while (!linkedCts.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), linkedCts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                string text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var match = Regex.Match(text, @"""edge_hash""\s*:\s*""([^""]+)""");
                if (match.Success)
                {
                    EdgeHash = match.Groups[1].Value;
                    AllohaSessionManager.SetGlobalEdgeHash(EdgeHash);
                }
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask; } catch { }
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                catch { }
            }
            _ws.Dispose();
        }
    }

    private static async Task SendTextAsync(ClientWebSocket ws, string message, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open)
            return;
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _ws?.Dispose();
    }
}

public static class AllohaSessionManager
{
    private static readonly ConcurrentDictionary<string, AllohaWsSession> _sessions = new();
    private static string _globalEdgeHash;
    private static DateTime _globalEdgeHashTime = DateTime.MinValue;

    public static void SetGlobalEdgeHash(string hash)
    {
        if (!string.IsNullOrEmpty(hash))
        {
            _globalEdgeHash = hash;
            _globalEdgeHashTime = DateTime.UtcNow;
        }
    }

    public static string GetGlobalEdgeHash()
    {
        if (!string.IsNullOrEmpty(_globalEdgeHash) && (DateTime.UtcNow - _globalEdgeHashTime) < TimeSpan.FromMinutes(3))
            return _globalEdgeHash;
        return null;
    }

    public static AllohaWsSession GetOrCreate(string pnr, string pnk, string origin)
    {
        if (string.IsNullOrEmpty(pnr) || string.IsNullOrEmpty(pnk))
            return null;

        var session = _sessions.GetOrAdd(pnk, sid =>
        {
            var s = new AllohaWsSession(pnr, sid, origin);
            s.Start();
            return s;
        });

        session.Touch();
        return session;
    }

    public static string GetLiveEdgeHash(string pnk)
    {
        if (!string.IsNullOrEmpty(pnk) && _sessions.TryGetValue(pnk, out var sess))
        {
            if (!string.IsNullOrEmpty(sess.EdgeHash))
                return sess.EdgeHash;
        }

        foreach (var s in _sessions.Values)
        {
            if (!string.IsNullOrEmpty(s.EdgeHash))
                return s.EdgeHash;
        }

        return GetGlobalEdgeHash();
    }
}
