using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VideoDB;

/// <summary>
/// Cookie антибота obrut.show (wd_approval) из внешнего Chrome через CDP.
/// Chrome должен выходить в сеть с того же IP, что и Lampac: cookie привязана к IP.
/// </summary>
public static class ChromeCookie
{
    static readonly SemaphoreSlim semaphore = new(1, 1);
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
    static DateTime lastRefresh;

    public static string Value { get; private set; }

    public static async Task<string> Refresh(string cdp, string pageUrl)
    {
        string used = Value;

        await semaphore.WaitAsync();
        try
        {
            // ponytail: один запрос в Chrome в минуту на весь процесс; параллельные 403 ждут его результат
            if (Value != used || DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(1))
                return Value;

            lastRefresh = DateTime.UtcNow;
            Value = await Fetch(cdp, pageUrl, used) ?? Value;
            return Value;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("VideoDB cdp: {Error}", ex.Message);
            return Value;
        }
        finally
        {
            semaphore.Release();
        }
    }

    static async Task<string> Fetch(string cdp, string pageUrl, string used)
    {
        // DevTools принимает только Host в виде IP или localhost, поэтому имя сервиса резолвим сами
        var uri = new Uri(cdp);
        var ip = (await Dns.GetHostAddressesAsync(uri.Host, AddressFamily.InterNetwork)).First();
        string endpoint = $"{ip}:{uri.Port}";

        var version = JsonNode.Parse(await http.GetStringAsync($"http://{endpoint}/json/version"));
        string ws = Regex.Replace(version["webSocketDebuggerUrl"].GetValue<string>(), "^ws://[^/]+", $"ws://{endpoint}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(ws), cts.Token);

        int id = 0;
        async Task<JsonNode> send(string method, object args = null)
        {
            int msgId = ++id;
            byte[] msg = JsonSerializer.SerializeToUtf8Bytes(new { id = msgId, method, @params = args ?? new { } });
            await socket.SendAsync(msg, WebSocketMessageType.Text, true, cts.Token);

            var buffer = new byte[64 * 1024];
            while (true)
            {
                using var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await socket.ReceiveAsync(buffer, cts.Token);
                    ms.Write(buffer, 0, res.Count);
                }
                while (!res.EndOfMessage);

                var node = JsonNode.Parse(ms.ToArray());
                if (node["id"]?.GetValue<int>() == msgId)
                    return node["error"] != null ? throw new Exception(node["error"].ToJsonString()) : node["result"];
            }
        }

        // Только Target/Storage: Runtime.enable и прочая автоматизация палятся антиботом
        string targetId = (await send("Target.createTarget", new { url = pageUrl }))["targetId"].GetValue<string>();
        string host = new Uri(pageUrl).Host;

        try
        {
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(1000, cts.Token);

                var cookies = (await send("Storage.getCookies"))["cookies"].AsArray()
                    .Where(c => c["domain"].GetValue<string>().TrimStart('.') is string d && (host == d || host.EndsWith("." + d)))
                    .Select(c => $"{c["name"].GetValue<string>()}={c["value"].GetValue<string>()}")
                    .ToList();

                string approval = cookies.FirstOrDefault(c => c.StartsWith("wd_approval="));
                if (approval != null && (used == null || !used.Contains(approval)))
                    return string.Join("; ", cookies);
            }

            return null;
        }
        finally
        {
            await send("Target.closeTarget", new { targetId });
        }
    }
}
