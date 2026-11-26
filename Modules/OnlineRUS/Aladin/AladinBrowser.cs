using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Aladin;

internal enum AladinRequestKind
{
    Page,
    Xhr,
    Media,
    Ws
}

/// <summary>
/// One browser-like HTTP session: the same cookie jar and TLS profile for the
/// player page, the balancer call and the CDN requests.
/// </summary>
internal sealed class AladinBrowser
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";

    const string SecChUa = "\"Not(A:Brand\";v=\"99\", \"Google Chrome\";v=\"133\", \"Chromium\";v=\"133\"";
    const string AcceptLanguage = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7";
    const string AcceptEncoding = "gzip, deflate, br, zstd";

    readonly ModuleConf _init;

    public AladinBrowser(ModuleConf init)
    {
        _init = init;
        SessionId = Guid.NewGuid().ToString("N");
    }

    public string SessionId { get; }

    public CurlResponse Get(string url, AladinRequestKind kind, string referer = null, string origin = null)
        => Send("GET", url, null, kind, referer, origin, 12000);

    public CurlResponse Send(string method, string url, string body, AladinRequestKind kind, string referer, string origin, int timeoutMilliseconds, string edgeHash = null, string borth = null)
    {
        var headers = BuildHeaders(kind, referer, origin, edgeHash);
        if (!string.IsNullOrEmpty(borth))
            headers["borth"] = borth;

        var order = HeaderOrder(kind);

        return CurlClient.Request(new CurlRequest
        {
            RequestUrl = url,
            RequestMethod = method,
            RequestBody = body,
            Headers = headers,
            HeaderOrder = order,
            SessionId = SessionId,
            FollowRedirects = true,
            TimeoutMilliseconds = timeoutMilliseconds,
            IsByteResponse = false,
            ProxyUrl = string.IsNullOrWhiteSpace(_init.nativeProxy) ? null : _init.nativeProxy
        });
    }

    /// <summary>
    /// Downloads the body to a temporary file and returns the bytes. The file
    /// path is required because the native client writes binary bodies there.
    /// </summary>
    public CurlResponse FetchBytes(string url, AladinRequestKind kind, string referer, string origin, string edgeHash, out byte[] bytes)
    {
        bytes = null;
        string path = Path.Combine(Path.GetTempPath(), "aladin-" + Guid.NewGuid().ToString("N") + ".bin");

        try
        {
            var headers = BuildHeaders(kind, referer, origin, edgeHash);
            var order = HeaderOrder(kind);

            var response = CurlClient.Request(new CurlRequest
            {
                RequestUrl = url,
                RequestMethod = "GET",
                Headers = headers,
                HeaderOrder = order,
                SessionId = SessionId,
                FollowRedirects = true,
                TimeoutMilliseconds = 30000,
                IsByteResponse = true,
                StreamOutputPath = path,
                        ProxyUrl = string.IsNullOrWhiteSpace(_init.nativeProxy) ? null : _init.nativeProxy
            });

            if (File.Exists(path))
                bytes = File.ReadAllBytes(path);

            return response;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    public string CookieHeader(string url)
    {
        try
        {
            var cookies = CurlClient.GetCookies(SessionId, url);
            if (cookies == null || cookies.Count == 0)
                return null;

            var sb = new StringBuilder();
            foreach (var cookie in cookies)
            {
                if (string.IsNullOrEmpty(cookie.Name))
                    continue;

                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(cookie.Name).Append('=').Append(cookie.Value);
            }

            return sb.Length == 0 ? null : sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    public CurlWebSocket WebSocket(string url, string origin, string cookie)
    {
        var headers = BuildHeaders(AladinRequestKind.Ws, null, origin, null);
        if (!string.IsNullOrEmpty(cookie))
            headers["cookie"] = cookie;

        return CurlClient.WebSocket(new CurlWsConnect
        {
            Url = url,
            Profile = CurlClient.Profile,
            Headers = headers,
            HeaderOrder = HeaderOrder(AladinRequestKind.Ws),
            HandshakeTimeoutMilliseconds = 30000,
            InsecureSkipVerify = CurlClient.InsecureSkipVerify,
            ProxyUrl = string.IsNullOrWhiteSpace(_init.nativeProxy) ? null : _init.nativeProxy
        });
    }

    Dictionary<string, string> BuildHeaders(AladinRequestKind kind, string referer, string origin, string edgeHash)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        headers["sec-ch-ua"] = SecChUa;
        headers["sec-ch-ua-mobile"] = "?0";
        headers["sec-ch-ua-platform"] = "\"macOS\"";

        switch (kind)
        {
            case AladinRequestKind.Page:
                headers["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8";
                headers["sec-fetch-dest"] = "iframe";
                headers["sec-fetch-mode"] = "navigate";
                headers["sec-fetch-site"] = "cross-site";
                headers["upgrade-insecure-requests"] = "1";
                break;

            case AladinRequestKind.Media:
                headers["accept"] = "*/*";
                headers["sec-fetch-dest"] = "empty";
                headers["sec-fetch-mode"] = "cors";
                headers["sec-fetch-site"] = "cross-site";
                headers["priority"] = "u=1, i";
                break;

            case AladinRequestKind.Ws:
                headers["upgrade-insecure-requests"] = "1";
                headers["accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";
                headers["sec-fetch-site"] = "none";
                headers["sec-fetch-mode"] = "navigate";
                headers["sec-fetch-user"] = "?1";
                headers["sec-fetch-dest"] = "document";
                headers["cache-control"] = "no-cache";
                headers["pragma"] = "no-cache";
                break;

            default:
                headers["accept"] = "*/*";
                headers["sec-fetch-dest"] = "empty";
                headers["sec-fetch-mode"] = "cors";
                headers["sec-fetch-site"] = "same-origin";
                headers["priority"] = "u=1, i";
                break;
        }

        headers["user-agent"] = UserAgent;
        headers["accept-language"] = string.IsNullOrWhiteSpace(_init.acceptLanguage) ? AcceptLanguage : _init.acceptLanguage;
        headers["accept-encoding"] = AcceptEncoding;

        if (!string.IsNullOrEmpty(referer))
            headers["referer"] = referer;

        if (!string.IsNullOrEmpty(origin))
            headers["origin"] = origin;

        if (kind == AladinRequestKind.Xhr)
        {
            headers["x-requested-with"] = "XMLHttpRequest";
            headers["content-type"] = "application/x-www-form-urlencoded; charset=UTF-8";
        }

        if (kind == AladinRequestKind.Media)
        {
            if (!string.IsNullOrWhiteSpace(_init.authorizations))
                headers["authorizations"] = _init.authorizations;

            if (!string.IsNullOrWhiteSpace(edgeHash))
                headers["accepts-controls"] = edgeHash;
        }

        return headers;
    }

    static string[] HeaderOrder(AladinRequestKind kind)
    {
        return kind switch
        {
            AladinRequestKind.Page =>
            [
                "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
                "upgrade-insecure-requests", "user-agent", "accept",
                "sec-fetch-site", "sec-fetch-mode", "sec-fetch-dest",
                "referer", "accept-encoding", "accept-language"
            ],
            AladinRequestKind.Media =>
            [
                "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
                "accept", "sec-fetch-site", "sec-fetch-mode", "sec-fetch-dest",
                "origin", "referer", "accept-encoding", "accept-language",
                "priority", "user-agent", "authorizations", "accepts-controls"
            ],
            AladinRequestKind.Ws =>
            [
                "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
                "upgrade-insecure-requests", "user-agent", "accept",
                "sec-fetch-site", "sec-fetch-mode", "sec-fetch-user", "sec-fetch-dest",
                "accept-encoding", "accept-language", "cache-control", "pragma",
                "origin", "cookie"
            ],
            _ =>
            [
                "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
                "accept", "x-requested-with", "user-agent", "origin",
                "sec-fetch-site", "sec-fetch-mode", "sec-fetch-dest",
                "referer", "accept-encoding", "accept-language",
                "content-type", "borth"
            ]
        };
    }
}
