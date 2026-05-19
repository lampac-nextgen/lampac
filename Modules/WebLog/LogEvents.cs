using Shared;
using Shared.Models.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebLog;

public static class LogEvents
{
    static readonly ConcurrentDictionary<string, byte> clients = new();
    static int initialized;
    static int httpResponseSubscribed;
    static int playwrightHttpResponseSubscribed;

    public static void Start()
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
            return;

        EventListener.NwsMessage += OnNwsMessage;
        EventListener.NwsDisconnected += OnNwsDisconnected;
    }

    public static void Stop()
    {
        Interlocked.Exchange(ref initialized, 0);

        EventListener.NwsMessage -= OnNwsMessage;
        EventListener.NwsDisconnected -= OnNwsDisconnected;

        UnsubscribeHttpResponse();
        UnsubscribePlaywrightHttpResponse();
        clients.Clear();
    }

    static void OnNwsMessage(EventNwsMessage e)
    {
        if (string.IsNullOrWhiteSpace(e.connectionId) || string.IsNullOrWhiteSpace(e.method))
            return;

        if (e.method.Equals("registryweblog", StringComparison.OrdinalIgnoreCase))
        {
            if (CoreInit.rootPasswd == GetStringArg(e.args, 0))
            {
                clients.AddOrUpdate(e.connectionId, 0, static (_, __) => 0);
                SubscribeHttpResponse();
                SubscribePlaywrightHttpResponse();
            }
        }
    }

    static void OnNwsDisconnected(EventNwsDisconnected e)
    {
        if (!string.IsNullOrWhiteSpace(e.connectionId))
            clients.TryRemove(e.connectionId, out _);

        if (clients.IsEmpty)
        {
            UnsubscribeHttpResponse();
            UnsubscribePlaywrightHttpResponse();
        }
    }


    static void SubscribeHttpResponse()
    {
        if (Interlocked.Exchange(ref httpResponseSubscribed, 1) == 1)
            return;

        EventListener.HttpResponse += OnHttpResponse;
    }

    static void UnsubscribeHttpResponse()
    {
        if (Interlocked.Exchange(ref httpResponseSubscribed, 0) == 0)
            return;

        EventListener.HttpResponse -= OnHttpResponse;
    }

    static void SubscribePlaywrightHttpResponse()
    {
        if (Interlocked.Exchange(ref playwrightHttpResponseSubscribed, 1) == 1)
            return;

        EventListener.PlaywrightHttpResponse += OnPlaywrightHttpResponse;
    }

    static void UnsubscribePlaywrightHttpResponse()
    {
        if (Interlocked.Exchange(ref playwrightHttpResponseSubscribed, 0) == 0)
            return;

        EventListener.PlaywrightHttpResponse -= OnPlaywrightHttpResponse;
    }

    static async Task OnHttpResponse(EventHttpResponse e)
    {
        if (clients.IsEmpty || Startup.Nws == null)
            return;

        string message;

        try
        {
            message = await BuildHttpMessage(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            message = "[WebLog] Format error\n" + ex;
        }

        await BroadcastAsync(message, "http").ConfigureAwait(false);
    }

    static async Task OnPlaywrightHttpResponse(EventPlaywrightHttpResponse e)
    {
        if (clients.IsEmpty || Startup.Nws == null)
            return;

        var sb = new StringBuilder();
        sb.Append($"{e.method ?? "GET"}: ").AppendLine(e.url);
        sb.AppendLine();
        AppendHeaders(sb, e.requestHeaders);
        sb.Append("\nStatusCode: ").AppendLine(e.status.ToString());
        AppendHeaders(sb, e.responseHeaders);
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(e.error) ? (string.IsNullOrWhiteSpace(e.result) ? "<empty>" : e.result) : e.error);

        await BroadcastAsync(sb.ToString(), "playwright").ConfigureAwait(false);
    }

    static async Task<string> BuildHttpMessage(EventHttpResponse e)
    {
        var sb = new StringBuilder();

        var request = e.response?.RequestMessage;
        string method = request?.Method?.Method ?? (e.data != null ? "POST" : "GET");

        sb.Append($"{method}: ").AppendLine(e.url);

        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            string requestBody = await ReadRequestBodyAsync(request, e.data).ConfigureAwait(false);
            sb.AppendLine(requestBody ?? string.Empty);
        }

        sb.AppendLine();
        AppendHeaders(sb, request?.Headers, request?.Content?.Headers);

        int code = (int)(e.response?.StatusCode ?? 0);
        sb.Append("\nStatusCode: ").AppendLine(code.ToString());
        AppendHeaders(sb, e.response?.Headers, e.response?.Content?.Headers);

        sb.AppendLine();
        sb.AppendLine(e.result ?? string.Empty);

        return sb.ToString();
    }

    static async Task<string> ReadRequestBodyAsync(HttpRequestMessage request, HttpContent fallback)
    {
        HttpContent content = request?.Content ?? fallback;
        if (content == null)
            return null;

        try
        {
            return await content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return "<unable to read>";
        }
    }

    static void AppendHeaders(StringBuilder sb, params HttpHeaders[] headerSets)
    {
        foreach (var headers in headerSets)
        {
            if (headers == null)
                continue;

            foreach (var header in headers)
            {
                sb.Append(header.Key)
                  .Append(": ")
                  .AppendLine(string.Join("; ", header.Value));
            }
        }
    }

    static void AppendHeaders(StringBuilder sb, IReadOnlyDictionary<string, string> headers)
    {
        if (headers == null || headers.Count == 0)
            return;

        foreach (var header in headers)
            sb.Append(header.Key).Append(": ").AppendLine(header.Value);
    }

    static async Task BroadcastAsync(string message, string channel)
    {
        if (string.IsNullOrWhiteSpace(message) || clients.IsEmpty || Startup.Nws == null)
            return;

        var targets = clients.Keys.ToArray();
        if (targets.Length == 0)
            return;

        var tasks = new Task[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            tasks[i] = Startup.Nws.SendAsync(targets[i], "Receive", message, channel);

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    static string GetStringArg(JsonElement args, int index)
    {
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() <= index)
            return null;

        var item = args[index];
        if (item.ValueKind == JsonValueKind.String)
            return item.GetString();

        if (item.ValueKind == JsonValueKind.Null)
            return null;

        return item.ToString();
    }
}
