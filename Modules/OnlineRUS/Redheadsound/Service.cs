using Shared.Models.Events;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Redheadsound;

public static class Service
{
    static readonly HashSet<string> skipHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "host", "content-length", "connection", "cookie", "range", "if-range", "if-none-match", "if-modified-since"
    };

    /// <summary>
    /// stream-proxy повторяет запрос плеера: те же Origin/Referer и служебные
    /// заголовки (Accepts-Controls, Authorizations), что были у m3u8 в браузере.
    /// </summary>
    public static Task ProxyApiCreateHttpRequest(EventProxyApiCreateHttpRequest e)
    {
        try
        {
            if (e.plugin == null || !e.plugin.Equals("redheadsound", StringComparison.OrdinalIgnoreCase))
                return Task.CompletedTask;

            var watch = e.decryptLink?.userdata as StreamData;
            if (watch?.headers == null || watch.headers.Count == 0)
                return Task.CompletedTask;

            string range = null;
            if (e.requestMessage.Headers.TryGetValues("Range", out var ranges))
                range = string.Join(",", ranges);

            e.requestMessage.Headers.Clear();

            foreach (var h in watch.headers)
            {
                if (string.IsNullOrEmpty(h.Key) || h.Key.StartsWith(":") || skipHeaders.Contains(h.Key))
                    continue;

                if (h.Key.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase))
                    continue;

                e.requestMessage.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            if (!watch.headers.ContainsKey("user-agent"))
                e.requestMessage.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);

            if (!string.IsNullOrEmpty(range))
                e.requestMessage.Headers.TryAddWithoutValidation("Range", range);

            if (e.requestMessage.Content?.Headers != null)
                e.requestMessage.Content.Headers.Clear();
        }
        catch { }

        return Task.CompletedTask;
    }
}
