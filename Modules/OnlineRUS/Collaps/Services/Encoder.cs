using Shared.Models.Events;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collaps;

public static class Encoder
{
    const string marker = "/x-en-x/";

    async public static Task ProxyApiCreateHttpRequest(EventProxyApiCreateHttpRequest e)
    {
        if (e.plugin != null && e.plugin.Equals("collaps", StringComparison.OrdinalIgnoreCase))
        {
            if (!e.requestMessage.RequestUri.AbsolutePath.Contains(marker))
                e.requestMessage.RequestUri = new Uri(Uri(e.requestMessage.RequestUri.ToString(), true));
        }
    }

    public static string Uri(string url, bool clearUri = false)
    {
        const string L = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        const string E = "DlChEXitLONYRkFjAsnBbymWzSHMqKPgQZpvwerofJTVdIuUcxaG";

        if (string.IsNullOrWhiteSpace(url) || url.Contains(marker))
            return url;

        var uri = new Uri(url);

        long n = (long)Math.Round(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 / 60 / 60
        );

        string payload = $"{n}/{uri.AbsolutePath}{uri.Query}";

        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        string encoded = new string(base64.Select(c =>
        {
            int index = L.IndexOf(c);
            return index >= 0 ? E[index] : c;
        }).ToArray());

        if (clearUri)
            return $"{uri.Scheme}://{uri.Authority}{marker}{encoded}";

        return $"{uri.Scheme}://{uri.Authority}{marker}{encoded}" + (url.Contains(".mpd") ? "#.mpd" : "#.m3u8");
    }
}
