using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LordFilm;

/// <summary>
/// HTTP-сессия сайта: обход JS-челленджа (get_jhash), отдельный cookie-джар на каждый хост
/// и автоматический переход на новое зеркало по редиректу.
/// Зависимости только от System.Net.Http, поэтому класс тестируется отдельно от Lampac.
/// </summary>
public sealed class LordFilmSession
{
    readonly string entryHost;
    readonly string userAgent;

    readonly HttpClient client;
    readonly SemaphoreSlim refreshLock = new(1, 1);

    readonly Dictionary<string, Dictionary<string, string>> cookieJars = new(StringComparer.OrdinalIgnoreCase);
    readonly object cookiesLock = new object();

    volatile string activeHost;

    public LordFilmSession(string host, string userAgent)
    {
        entryHost = NormalizeHost(host);
        activeHost = entryHost;

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

    /// <summary>Хост из init.conf. Не меняется при переезде зеркала.</summary>
    public string EntryHost => entryHost;

    /// <summary>Текущий рабочий хост. Может отличаться от EntryHost после редиректа.</summary>
    public string ActiveHost => activeHost;

    public bool MatchHost(string h) => string.Equals(entryHost, NormalizeHost(h), StringComparison.OrdinalIgnoreCase);

    #region Public
    public async Task<List<SearchItem>> Search(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        string html = await Request("POST", $"{activeHost}/search-result/", $"do=search&subaction=search&story={Uri.EscapeDataString(title)}");

        if (string.IsNullOrEmpty(html) && !SameOrigin(activeHost, entryHost))
        {
            // Зеркало могло умереть без редиректа — возвращаемся на хост из конфига.
            activeHost = entryHost;
            html = await Request("POST", $"{entryHost}/search-result/", $"do=search&subaction=search&story={Uri.EscapeDataString(title)}");
        }

        if (string.IsNullOrEmpty(html))
            return null;

        return LordFilmParser.ParseSearch(html);
    }

    public async Task<EmbedModel> Embed(string pageUri)
    {
        if (string.IsNullOrEmpty(pageUri))
            return null;

        string page = await Request("GET", Absolutize(pageUri, activeHost), null);
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
        string currentUrl = url;
        string currentMethod = method;
        string currentData = data;
        string currentOrigin = OriginOf(currentUrl);

        int redirects = 0;
        int challenges = 0;

        while (redirects < 8 && challenges < 4)
        {
            var resp = await Send(currentMethod, currentUrl, currentData, attachCookies);

            if (resp == null)
                return null;

            if (resp.Status >= 300 && resp.Status < 400 && !string.IsNullOrEmpty(resp.Location))
            {
                string nextUrl = Absolutize(resp.Location, currentUrl);
                string nextOrigin = OriginOf(nextUrl);
                bool crossOrigin = nextOrigin != null && !SameOrigin(nextOrigin, currentOrigin);
                bool siteOrigin = nextOrigin != null && IsSiteOrigin(nextOrigin);

                if (attachCookies && crossOrigin && siteOrigin)
                    activeHost = nextOrigin;

                bool preserveMethod = resp.Status == 307 || resp.Status == 308;

                // 302/303 на другое зеркало: перезапускаем исходный POST,
                // иначе сайт получит GET без тела и поиск вернёт пустую страницу.
                if (!preserveMethod && currentMethod != "GET" && crossOrigin && siteOrigin)
                {
                    currentUrl = ReplaceOrigin(currentUrl, nextOrigin);
                    currentMethod = method;
                    currentData = data;
                    currentOrigin = nextOrigin;
                    redirects++;
                    continue;
                }

                currentUrl = nextUrl;
                currentOrigin = nextOrigin;

                if (!preserveMethod)
                {
                    currentMethod = "GET";
                    currentData = null;
                }

                redirects++;
                continue;
            }

            if (resp.Status == 200 && resp.Content.Contains("get_jhash", StringComparison.Ordinal))
            {
                if (!attachCookies)
                    return null;

                string solvedOrigin = await SolveChallengeAsync(currentOrigin);
                if (solvedOrigin == null)
                    return null;

                if (!SameOrigin(solvedOrigin, currentOrigin))
                {
                    currentUrl = ReplaceOrigin(currentUrl, solvedOrigin);
                    currentMethod = method;
                    currentData = data;
                }

                currentOrigin = solvedOrigin;
                challenges++;
                continue;
            }

            if (resp.Status == 200)
                return resp.Content;

            break;
        }

        return null;
    }

    /// <summary>
    /// Решает JS-челлендж на конкретном хосте. Если сайт в процессе редиректит
    /// на другое зеркало, возвращает уже новый рабочий origin.
    /// </summary>
    async Task<string> SolveChallengeAsync(string origin)
    {
        if (string.IsNullOrEmpty(origin))
            return null;

        await refreshLock.WaitAsync();

        try
        {
            string currentOrigin = origin;

            for (int round = 0; round < 8; round++)
            {
                var resp = await Send("GET", $"{currentOrigin}/", null, attachCookies: true);
                if (resp == null)
                    return null;

                int redirects = 0;

                while (resp.Status >= 300 && resp.Status < 400 && !string.IsNullOrEmpty(resp.Location) && redirects++ < 5)
                {
                    string nextUrl = Absolutize(resp.Location, $"{currentOrigin}/");
                    string nextOrigin = OriginOf(nextUrl);

                    if (nextOrigin != null && IsSiteOrigin(nextOrigin))
                    {
                        currentOrigin = nextOrigin;
                        activeHost = nextOrigin;
                    }

                    resp = await Send("GET", nextUrl, null, attachCookies: true);
                    if (resp == null)
                        return null;
                }

                if (resp.Status == 200 && !resp.Content.Contains("get_jhash", StringComparison.Ordinal))
                    return currentOrigin;

                if (!TryGetCookie(currentOrigin, "__js_p_", out string js))
                    return null;

                string[] parts = js.Split(',');

                if (parts.Length == 0 || !long.TryParse(parts[0], out long code))
                    return null;

                SetCookie(currentOrigin, "__jhash_", GetJhash(code).ToString());
                SetCookie(currentOrigin, "__jua_", FixedEncode(userAgent));

                await Task.Delay(150);
            }

            return null;
        }
        finally
        {
            refreshLock.Release();
        }
    }
    #endregion

    #region Send
    async Task<Resp> Send(string method, string url, string data, bool attachCookies)
    {
        try
        {
            string origin = OriginOf(url);

            using var req = new HttpRequestMessage(new HttpMethod(method), url);

            req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            if (attachCookies && origin != null)
            {
                string cookie = CookieHeader(origin);

                if (!string.IsNullOrEmpty(cookie))
                    req.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            if (!string.IsNullOrEmpty(data))
                req.Content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead);

            ParseSetCookie(resp, origin);

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
    Dictionary<string, string> GetJarLocked(string origin)
    {
        if (!cookieJars.TryGetValue(origin, out var jar))
            cookieJars[origin] = jar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return jar;
    }

    void SetCookie(string origin, string name, string value)
    {
        if (string.IsNullOrEmpty(origin) || string.IsNullOrEmpty(name))
            return;

        lock (cookiesLock)
        {
            var jar = GetJarLocked(origin);

            if (string.IsNullOrEmpty(value))
                jar.Remove(name);
            else
                jar[name] = value;
        }
    }

    bool TryGetCookie(string origin, string name, out string value)
    {
        lock (cookiesLock)
        {
            value = null;
            return !string.IsNullOrEmpty(origin)
                && cookieJars.TryGetValue(origin, out var jar)
                && jar.TryGetValue(name, out value);
        }
    }

    string CookieHeader(string origin)
    {
        lock (cookiesLock)
        {
            if (!cookieJars.TryGetValue(origin, out var jar) || jar.Count == 0)
                return null;

            var sb = new StringBuilder();

            foreach (var c in jar)
            {
                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(c.Key).Append('=').Append(c.Value);
            }

            return sb.ToString();
        }
    }

    void ParseSetCookie(HttpResponseMessage resp, string origin)
    {
        if (string.IsNullOrEmpty(origin) || !resp.Headers.TryGetValues("Set-Cookie", out var values))
            return;

        foreach (string raw in values)
        {
            string[] parts = raw.Split(';');
            string pair = parts[0];
            int eq = pair.IndexOf('=');

            if (eq <= 0)
                continue;

            string name = pair.Substring(0, eq).Trim();
            string value = pair.Substring(eq + 1);
            bool delete = string.IsNullOrEmpty(value);

            for (int i = 1; i < parts.Length && !delete; i++)
            {
                string attr = parts[i].Trim();

                if (attr.StartsWith("Max-Age=", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(attr.Substring(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out int age)
                    && age <= 0)
                {
                    delete = true;
                }
                else if (attr.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase)
                    && DateTime.TryParse(attr.Substring(8), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime expires)
                    && expires <= DateTime.UtcNow)
                {
                    delete = true;
                }
            }

            SetCookie(origin, name, delete ? null : value);
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
    static string NormalizeHost(string host) => (host ?? string.Empty).Trim().TrimEnd('/');

    static string OriginOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            return null;

        return u.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    static bool SameOrigin(string a, string b)
        => string.Equals((a ?? string.Empty).TrimEnd('/'), (b ?? string.Empty).TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    bool IsSiteOrigin(string origin)
    {
        return !string.IsNullOrEmpty(origin)
            && string.Equals(RootDomain(origin), RootDomain(entryHost), StringComparison.OrdinalIgnoreCase);
    }

    static string RootDomain(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string host = value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var u))
            host = u.Host;

        host = host.Trim().TrimEnd('.').ToLowerInvariant();

        if (IPAddress.TryParse(host, out _))
            return host;

        string[] parts = host.Split('.');

        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : host;
    }

    static string Absolutize(string url, string baseUrl)
    {
        if (string.IsNullOrEmpty(url))
            return baseUrl;

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.StartsWith("//", StringComparison.Ordinal))
            return "https:" + url;

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) && Uri.TryCreate(b, url, out var abs))
            return abs.AbsoluteUri;

        return url;
    }

    static string ReplaceOrigin(string url, string origin)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || string.IsNullOrEmpty(origin))
            return url;

        return origin.TrimEnd('/') + u.PathAndQuery;
    }

    sealed class Resp
    {
        public int Status;

        public string Location;

        public string Content;
    }
    #endregion
}
