using Newtonsoft.Json;
using Shared.Models.Events;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Spectre;

public static class Service
{
    static Timer timer;
    static readonly object last_time_lock = new(), resolution_lock = new();
    static readonly ConcurrentDictionary<string, WatchMux> watchs = new();

    public static void Start()
    {
        timer = new Timer(_ =>
        {
            foreach (var kvp in watchs)
            {
                var watch = kvp.Value;
                if (watch.lastreq != default && DateTime.Now.AddMinutes(-15) > watch.lastreq)
                {
                    watch.Dispose();
                    watchs.TryRemove(kvp.Key, out WatchMux _);
                }
            }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));
    }

    public static void Stop()
    {
        timer?.Dispose();
    }


    #region ProxyApiCreateHttpRequest
    async public static Task ProxyApiCreateHttpRequest(EventProxyApiCreateHttpRequest e)
    {
        if (e.plugin != null && e.plugin.Equals("spectre", StringComparison.OrdinalIgnoreCase))
        {
            var streamdata = e.decryptLink?.userdata as StreamData;
            if (streamdata?.id == null || !watchs.TryGetValue(streamdata.id, out WatchMux watch))
            {
                if (ModInit.conf.debug)
                    Console.WriteLine("watch null");

                e.requestMessage.RequestUri = null;
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (watch.edge_hash == null && sw.Elapsed < TimeSpan.FromSeconds(20))
                await Task.Delay(25);

            if (watch.edge_hash == null)
            {
                if (ModInit.conf.debug)
                    Console.WriteLine("edge_hash null");

                e.requestMessage.RequestUri = null;
                return;
            }

            watch.lastreq = DateTime.Now;

            #region resolution
            bool sendResolution = false;

            lock (resolution_lock)
            {
                if (streamdata.resolution != watch.resolution)
                {
                    watch.resolution = streamdata.resolution;
                    sendResolution = watch.ws != null;
                }
            }

            if (sendResolution)
            {
                await WsSendAsync(watch, "playback_start");
                await Task.Delay(1000);
            }
            #endregion

            #region current_time
            string segId = Regex.Match(e.requestMessage.RequestUri.ToString(), "/seg-([0-9]+)-").Groups[1].Value;
            int seg = int.TryParse(segId, out int s) ? s : 0;

            if (25 >= seg)
            {
                watch.current_time = 0;
                watch.last_time = 0;
            }
            else
            {
                bool sendSeeked = false;

                lock (last_time_lock)
                {
                    watch.current_time = (seg - 25) * 6;
                    if (watch.last_time == 0)
                        watch.last_time = watch.current_time;

                    if ((watch.current_time - watch.last_time) > 90)
                        sendSeeked = watch.ws != null;

                    watch.last_time = watch.current_time;
                }

                if (sendSeeked)
                {
                    await WsSendAsync(watch, "seeked");
                    await Task.Delay(1000);
                }
            }
            #endregion

            #region requestMessage
            e.requestMessage.Headers.Clear();

            e.requestMessage.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            e.requestMessage.Headers.TryAddWithoutValidation("sec-ch-ua", Http.defaultUaHeaders["sec-ch-ua"]);
            e.requestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            e.requestMessage.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            e.requestMessage.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
            e.requestMessage.Headers.TryAddWithoutValidation("Accept", "*/*");
            e.requestMessage.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5");
            e.requestMessage.Headers.TryAddWithoutValidation("Accepts-Controls", watch.edge_hash);
            e.requestMessage.Headers.TryAddWithoutValidation("Authorizations", "Bearer pXzvbyDGLYyB6VkwsWZDv3iMKZtsXNzpzRyxZUcsKHXxsSeaYakbo3hw9mBFRc5VQTpqAX6BW8aDEqyLaHYcXSQiV6KHYTVTK6MYRphNAy5sBjtrevqkDzKmLqNdfMZGEU9NELjmtKfZy3RNGzCd767sNh1mXEj4tCcvqndHtzmwAbZNkhm4ghDEasodotMBewypNQ56uotJAQGX11csfeRfBAPk8DcUWWkkqzxca8vbnEw12vUFbBzT6hz8ZB3F3dzUhUXoL2cr1WM1bXQArRCS1MUNMz3X5WDMMQoZKxj2AMTRqp7QQX4dDB9B7VzEZTmyFULhm1AcHHMkoMvSVvKYoBoAKLycYAgMHeD4ECJcGEAGpnkJhrV57zQ7");
            e.requestMessage.Headers.TryAddWithoutValidation("Origin", watch.requestOrigin);
            e.requestMessage.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
            e.requestMessage.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            e.requestMessage.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            e.requestMessage.Headers.TryAddWithoutValidation("Referer", watch.requestReferer);
            e.requestMessage.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");

            if (e.requestMessage.Content?.Headers != null)
                e.requestMessage.Content.Headers.Clear();
            #endregion
        }
    }
    #endregion

    #region AddOrUpdate
    async public static Task<bool> AddOrUpdate(string id, string wsUri, WatchMux watch)
    {
        if (watchs.TryRemove(id, out WatchMux _rw))
            _rw.Dispose();

        bool connected = await WebSocket(wsUri, watch);
        if (!connected)
            return false;

        return watchs.TryAdd(id, watch);
    }

    public static bool AddOrUpdateHeaders(string id, WatchMux watch)
    {
        if (string.IsNullOrEmpty(watch?.edge_hash))
            return false;

        if (watchs.TryRemove(id, out WatchMux _rw))
            _rw.Dispose();

        watch.lastreq = DateTime.Now;
        return watchs.TryAdd(id, watch);
    }
    #endregion


    #region WebSocket
    async static Task<bool> WebSocket(string wsUri, WatchMux watch)
    {
        try
        {
            watch.ws = new ClientWebSocket();
            watch.ws.Options.SetRequestHeader("User-Agent", Http.UserAgent);

            watch.wscts = new CancellationTokenSource();

            await watch.ws.ConnectAsync(new Uri(wsUri), watch.wscts.Token);

            var receiveBuffer = new byte[16 * 1024];

            _ = Task.Factory.StartNew(async () =>
            {
                while (!watch.wscts.Token.IsCancellationRequested && watch.ws.State == WebSocketState.Open)
                {
                    var result = await watch.ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), watch.wscts.Token);
                    if (watch.wscts.Token.IsCancellationRequested)
                        return;

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (ModInit.conf.debug)
                            Console.WriteLine("Connection closed by server");

                        watchs.TryRemove(watch.streamId, out _);
                        watch.Dispose();
                        return;
                    }

                    string message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);

                    string hash = Regex.Match(message ?? string.Empty, "\"edge_hash\":\"([^\"]+)\"").Groups[1].Value;
                    if (!string.IsNullOrEmpty(hash))
                    {
                        watch.edge_hash = hash;

                        if (ModInit.conf.debug)
                            Console.WriteLine("edge_hash: " + watch.edge_hash);
                    }
                }
            }, watch.wscts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            _ = Task.Factory.StartNew(async () =>
            {
                while (!watch.wscts.Token.IsCancellationRequested && watch.ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), watch.wscts.Token);
                        if (watch.wscts.Token.IsCancellationRequested)
                            return;

                        await WsSendAsync(watch, "playing");
                    }
                    catch { }
                }
            }, watch.wscts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            long unixtime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await WsSendAsync(watch, "playback_start", unixtime);
            await WsSendAsync(watch, "init", unixtime);

            return true;
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region WsSendAsync
    static Task WsSendAsync(WatchMux watch, string type, long unixtime = 0)
    {
        if (watch.ws == null || watch.ws.State != WebSocketState.Open)
            return Task.CompletedTask;

        if (unixtime == 0)
            unixtime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string payload = JsonConvert.SerializeObject(new
        {
            type = type,
            current_time = watch.current_time,
            resolution = watch.resolution,
            track_id = "1",
            speed = 1,
            subtitle = -1,
            ts = unixtime
        });

        if (ModInit.conf.debug)
        {
            switch (type)
            {
                case "seeked":
                case "playing":
                    Console.WriteLine($"{type}: {watch.current_time}");
                    break;
                case "playback_start":
                    Console.WriteLine("resolution: " + watch.resolution);
                    break;
                default:
                    break;
            }
        }

        return watch.ws.SendAsync(
          new ArraySegment<byte>(Encoding.UTF8.GetBytes(payload)),
          WebSocketMessageType.Text,
          true,
          watch.wscts.Token
        );
    }
    #endregion
}
