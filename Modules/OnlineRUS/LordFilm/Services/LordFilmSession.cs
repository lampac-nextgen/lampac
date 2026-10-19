using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LordFilm;

/// <summary>
/// HTTP-сессия сайта: обход JS-челленджа (get_jhash) и работа с cookie-джаром.
/// Зависимости только от System.Net.Http, поэтому класс тестируется отдельно от Lampac.
/// </summary>
public sealed class LordFilmSession
{
    readonly string host;
    readonly string userAgent;

    readonly HttpClient client;
    readonly SemaphoreSlim refreshLock = new(1, 1);

    readonly Dictionary<string, string> cookies = new();
    DateTime validUntil = DateTime.MinValue;

    public LordFilmSession(string host, string userAgent)
    {
        this.host = (host ?? string.Empty).TrimEnd('/');
        this.userAgent = !string.IsNullOrWhiteSpace(userAgent)
            ? userAgent
            : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        var handler = new HttpClientHandler()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All
        };

        client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public bool MatchHost(string h) => string.Equals(host, (h ?? string.Empty).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    #region Public
    public async Task<List<SearchItem>> Search(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        string html = await Request("POST", $"{host}/search-result/", $"do=search&subaction=search&story={Uri.EscapeDataString(title)}");

        if (string.IsNullOrEmpty(html))
            return null;

        return LordFilmParser.ParseSearch(html);
    }

    public async Task<EmbedModel> Embed(string pageUri)
    {
        if (string.IsNullOrEmpty(pageUri))
            return null;

        string page = await Request("GET", Absolutize(pageUri), null);
        if (string.IsNullOrEmpty(page))
            return null;

        string id = LordFilmParser.GetEmbedId(page);
        if (string.IsNullOrEmpty(id))
            return null;

        string embed = await Request("GET", $"https://api.nextembed.ws/embed/movie/{id}", null, attachCookies: false);
        if (string.IsNullOrEmpty(embed))
            return null;

        return LordFilmParser.ParseEmbed(embed);
    }

    /// <summary>Сырой текст произвольного ресурса (например, master.m3u8 с CDN) без cookie-сессии.</summary>
    public async Task<string> Fetch(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var resp = await Send("GET", url, null, attachCookies: false);

        return resp != null && resp.Status == 200 ? resp.Content : null;
    }
    #endregion

    #region Request
    async Task<string> Request(string method, string url, string data, bool attachCookies = true)
    {
        if (attachCookies)
            await EnsureSessionAsync();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            string currentUrl = url;
            string currentMethod = method;
            string currentData = data;

            for (int redirect = 0; redirect < 5; redirect++)
            {
                var resp = await Send(currentMethod, currentUrl, currentData, attachCookies);

                if (resp == null)
                    return null;

                if (resp.Status >= 300 && resp.Status < 400 && !string.IsNullOrEmpty(resp.Location))
                {
                    currentUrl = Absolutize(resp.Location);
                    currentMethod = "GET";
                    currentData = null;

                    continue;
                }

                if (resp.Status == 200 && !resp.Content.Contains("get_jhash", StringComparison.Ordinal))
                    return resp.Content;

                break;
            }

            if (!attachCookies)
                return null;

            await RefreshSessionAsync();
        }

        return null;
    }

    async Task EnsureSessionAsync()
    {
        if (DateTime.UtcNow < validUntil && HasCookie("__lhash_"))
            return;

        await refreshLock.WaitAsync();

        try
        {
            if (DateTime.UtcNow < validUntil && HasCookie("__lhash_"))
                return;

            await RefreshSessionAsync();
        }
        finally
        {
            refreshLock.Release();
        }
    }

    /// <summary>
    /// Сайт отдает JS-челлендж (get_jhash) и может требовать несколько раундов подряд,
    /// как это делает браузер: получаем __js_p_, подписываем, повторяем запрос.
    /// </summary>
    async Task RefreshSessionAsync()
    {
        lock (cookies)
            validUntil = DateTime.MinValue;

        for (int round = 0; round < 8; round++)
        {
            var resp = await Send("GET", $"{host}/", null, attachCookies: true);

            if (resp == null)
                return;

            for (int redirect = 0; redirect < 5 && resp.Status >= 300 && resp.Status < 400 && !string.IsNullOrEmpty(resp.Location); redirect++)
                resp = await Send("GET", Absolutize(resp.Location), null, attachCookies: true);

            if (resp == null)
                return;

            if (resp.Status == 200 && !resp.Content.Contains("get_jhash", StringComparison.Ordinal))
            {
                lock (cookies)
                    validUntil = DateTime.UtcNow.AddMinutes(15);

                return;
            }

            if (!TryGetCookie("__js_p_", out string js))
                return;

            string[] parts = js.Split(',');

            if (parts.Length == 0 || !long.TryParse(parts[0], out long code))
                return;

            lock (cookies)
            {
                cookies["__jhash_"] = GetJhash(code).ToString();
                cookies["__jua_"] = FixedEncode(userAgent);
            }

            await Task.Delay(200);
        }
    }

    async Task<Resp> Send(string method, string url, string data, bool attachCookies)
    {
        try
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), url);

            req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            if (attachCookies)
            {
                string cookie = CookieHeader();

                if (!string.IsNullOrEmpty(cookie))
                    req.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            if (!string.IsNullOrEmpty(data))
                req.Content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead);

            ParseSetCookie(resp);

            string content = string.Empty;

            if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
                content = await resp.Content.ReadAsStringAsync();

            return new Resp()
            {
                Status = (int)resp.StatusCode,
                Location = resp.Headers.Location?.ToString(),
                Content = content ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Cookies
    bool HasCookie(string name)
    {
        lock (cookies)
            return cookies.ContainsKey(name);
    }

    bool TryGetCookie(string name, out string value)
    {
        lock (cookies)
            return cookies.TryGetValue(name, out value);
    }

    string CookieHeader()
    {
        lock (cookies)
        {
            if (cookies.Count == 0)
                return null;

            var sb = new StringBuilder();

            foreach (var c in cookies)
            {
                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(c.Key).Append('=').Append(c.Value);
            }

            return sb.ToString();
        }
    }

    void ParseSetCookie(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values))
            return;

        foreach (string raw in values)
        {
            int semicolon = raw.IndexOf(';');
            string pair = semicolon >= 0 ? raw.Substring(0, semicolon) : raw;
            int eq = pair.IndexOf('=');

            if (eq <= 0)
                continue;

            string name = pair.Substring(0, eq).Trim();
            string value = pair.Substring(eq + 1);

            lock (cookies)
            {
                if (string.IsNullOrEmpty(value))
                    cookies.Remove(name);
                else
                    cookies[name] = value;
            }
        }
    }
    #endregion

    #region Challenge
    /// <summary>get_jhash(b) из скрипта проверки сайта.</summary>
    public static int GetJhash(long b)
    {
        long x = 123456789, k = 0;

        for (long i = 0; i < 1677696; i++)
        {
            x = ((x + b) ^ (x + (x % 3) + (x % 17) + b) ^ i) % 16776960;

            if (x % 117 == 0)
                k = (k + 1) % 1111;
        }

        return (int)k;
    }

    /// <summary>fixedEncodeURIComponent из скрипта проверки сайта.</summary>
    public static string FixedEncode(string str)
    {
        return Uri.EscapeDataString(str)
            .Replace("!", "%21")
            .Replace("'", "%27")
            .Replace("(", "%28")
            .Replace(")", "%29")
            .Replace("*", "%2A");
    }
    #endregion

    #region Helpers
    string Absolutize(string url)
    {
        if (string.IsNullOrEmpty(url))
            return host;

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.StartsWith("//", StringComparison.Ordinal))
            return "https:" + url;

        if (url.StartsWith("/", StringComparison.Ordinal))
            return host + url;

        return $"{host}/{url}";
    }

    sealed class Resp
    {
        public int Status;

        public string Location;

        public string Content;
    }
    #endregion
}
