using Microsoft.IO;
using Newtonsoft.Json;
using Shared.Models.Events;
using Shared.Services.Pools.Json;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Shared.Services;

public static class Http
{
    #region static
    static Http()
    {
        EventListener.UpdateInitFile += cacheDefaultRequestHeaders.Clear;
    }

    static bool logEnable => CoreInit.conf.serilog;
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext("SourceContext", nameof(Http));

    public static IHttpClientFactory httpClientFactory;
    static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> cacheDefaultRequestHeaders = new();

    static readonly JsonSerializerOptions jsonTextOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    static readonly JsonSerializerSettings newtonsoftIgnoreErrorsSettings = new()
    {
        Error = static (se, ev) => { ev.ErrorContext.Handled = true; }
    };

    public static bool AlwaysAllowCertificate(HttpRequestMessage request, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        => true;

    readonly static HttpResponseMessage internalServerErrorResponse = new()
    {
        StatusCode = HttpStatusCode.InternalServerError,
        RequestMessage = new HttpRequestMessage()
    };

    static async Task InvokeHttpResponseHandlersAsync(EventHttpResponse eventHttpResponse)
    {
        foreach (Func<EventHttpResponse, Task> handler in EventListener.HttpResponse.GetInvocationList())
            await handler.Invoke(eventHttpResponse).ConfigureAwait(false);
    }
    #endregion

    #region normalizedKnownHeaders
    static readonly IReadOnlyDictionary<string, string> normalizedKnownHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["accept"] = "Accept",
        ["accept-language"] = "Accept-Language",
        ["authorization"] = "Authorization",
        ["cache-control"] = "Cache-Control",
        ["content-type"] = "Content-Type",
        ["cookie"] = "Cookie",
        ["dnt"] = "DNT",
        ["origin"] = "Origin",
        ["pragma"] = "Pragma",
        ["priority"] = "Priority",
        ["referer"] = "Referer",

        // Chrome Client Hints: обычно оставляют lowercase.
        ["sec-ch-ua"] = "sec-ch-ua",
        ["sec-ch-ua-mobile"] = "sec-ch-ua-mobile",
        ["sec-ch-ua-platform"] = "sec-ch-ua-platform",

        ["sec-fetch-dest"] = "Sec-Fetch-Dest",
        ["sec-fetch-mode"] = "Sec-Fetch-Mode",
        ["sec-fetch-site"] = "Sec-Fetch-Site",
        ["sec-fetch-user"] = "Sec-Fetch-User",

        ["upgrade-insecure-requests"] = "Upgrade-Insecure-Requests",
        ["user-agent"] = "User-Agent",
    };
    #endregion

    #region defaultHeaders / UserAgent
    public static readonly IReadOnlyDictionary<string, string> defaultUaHeaders = new Dictionary<string, string>()
    {
        ["sec-ch-ua"] = "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"",
        ["sec-ch-ua-mobile"] = "?0",
        ["sec-ch-ua-platform"] = "\"Windows\"",
        ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
    };

    public static readonly IReadOnlyDictionary<string, string> defaultCommonHeaders = new Dictionary<string, string>()
    {
        ["pragma"] = "no-cache",
        ["cache-control"] = "no-cache",
        ["dnt"] = "1",
        //["priority"] = "u=0, i"
    };

    public static readonly IReadOnlyDictionary<string, string> defaultFullHeaders = new Dictionary<string, string>()
    {
        ["pragma"] = "no-cache",
        ["cache-control"] = "no-cache",
        ["sec-ch-ua"] = "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"",
        ["sec-ch-ua-mobile"] = "?0",
        ["sec-ch-ua-platform"] = "\"Windows\"",
        ["dnt"] = "1",
        ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
    };

    public static string UserAgent
        => defaultUaHeaders["user-agent"];
    #endregion

    #region Handler
    public static HttpClientHandler Handler(string url, WebProxy proxy, CookieContainer cookieContainer = null)
    {
        var handler = new HttpClientHandler()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = AlwaysAllowCertificate
        };

        if (proxy != null)
        {
            handler.UseProxy = true;
            handler.Proxy = proxy;
        }
        else
        {
            handler.UseProxy = false;
        }

        if (cookieContainer != null)
        {
            handler.CookieContainer = cookieContainer;
            handler.UseCookies = true; //<-- Enable the use of cookies.
        }

        try
        {
            if (CoreInit.conf.globalproxy != null && CoreInit.conf.globalproxy.Length > 0)
            {
                foreach (var p in CoreInit.conf.globalproxy)
                {
                    if (p.list == null || p.list.Length == 0 || p.pattern == null)
                        continue;

                    if (Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
                    {
                        string proxyip = p.list[Random.Shared.Next(p.list.Length)];

                        NetworkCredential credentials = null;

                        if (proxyip.Contains("@"))
                        {
                            var g = Regex.Match(proxyip, p.pattern_auth).Groups;
                            proxyip = g["sheme"].Value + g["host"].Value;
                            credentials = new NetworkCredential(g["username"].Value, g["password"].Value);
                        }
                        else if (p.useAuth)
                            credentials = new NetworkCredential(p.username, p.password);

                        handler.UseProxy = true;
                        handler.Proxy = new WebProxy(proxyip, p.BypassOnLocal, null, credentials);

                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (logEnable)
                Log.Error(ex, "CatchId={CatchId}", "id_6g2snq8w");
        }

        if (EventListener.HttpHandler != null)
        {
            var em = new EventHttpHandler(url, handler, proxy, cookieContainer);

            foreach (Action<EventHttpHandler> eventHandler in EventListener.HttpHandler.GetInvocationList())
                eventHandler.Invoke(em);
        }

        return handler;
    }
    #endregion


    #region DefaultRequestHeaders
    public static void DefaultRequestHeaders(string url, HttpRequestMessage client, string cookie, string referer, IReadOnlyList<HeadersModel> headers, bool useDefaultHeaders = true, string prefixCacheHeader = null)
    {
        if (prefixCacheHeader != null && cacheDefaultRequestHeaders.TryGetValue(prefixCacheHeader, out IReadOnlyDictionary<string, string> _cacheHeaders))
        {
            foreach (var h in _cacheHeaders)
            {
                if (client.Headers.TryAddWithoutValidation(h.Key, h.Value)) { }
                else if (client.Content?.Headers != null)
                {
                    client.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
            }
        }
        else
        {
            var addHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (useDefaultHeaders)
            {
                addHeaders.TryAdd("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
                addHeaders.TryAdd("accept-language", "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5");
            }

            if (cookie != null)
                addHeaders.TryAdd("cookie", cookie);

            if (referer != null)
                addHeaders.TryAdd("referer", referer);

            if (useDefaultHeaders)
            {
                if (HasUserAgent(headers))
                {
                    foreach (var h in defaultCommonHeaders)
                        addHeaders.TryAdd(h.Key, h.Value);
                }
                else
                {
                    foreach (var h in defaultFullHeaders)
                        addHeaders.TryAdd(h.Key, h.Value);
                }
            }

            if (headers != null)
            {
                foreach (var h in headers)
                    addHeaders[h.name] = h.val;
            }

            // TryAddWithoutValidation сам сериализует заголовки для http 2/3
            var normalizeHeaders = client.Version == HttpVersion.Version11
                ? NormalizeHeaders(addHeaders)
                : addHeaders;

            if (normalizeHeaders == null)
                return;

            if (prefixCacheHeader != null)
                cacheDefaultRequestHeaders[prefixCacheHeader] = normalizeHeaders;

            foreach (var h in normalizeHeaders)
            {
                if (client.Headers.TryAddWithoutValidation(h.Key, h.Value)) { }
                else if (client.Content?.Headers != null)
                {
                    client.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
            }

            if (EventListener.HttpRequestHeaders != null)
            {
                var em = new EventHttpHeaders(url, client, cookie, referer, headers, useDefaultHeaders);

                foreach (Action<EventHttpHeaders> handler in EventListener.HttpRequestHeaders.GetInvocationList())
                    handler.Invoke(em);
            }
        }
    }
    #endregion

    #region NormalizeHeaders
    public static Dictionary<string, T> NormalizeHeaders<T>(IReadOnlyDictionary<string, T> raw)
    {
        if (raw == null || raw.Count == 0)
            return null;

        var result = new Dictionary<string, T>(raw.Count, StringComparer.Ordinal);

        foreach (var kv in raw)
            result[NormalizeHeaderName(kv.Key)] = kv.Value;

        return result;
    }

    public static Dictionary<string, string> NormalizeHeaders(IReadOnlyList<HeadersModel> raw)
    {
        if (raw == null || raw.Count == 0)
            return null;

        var result = new Dictionary<string, string>(raw.Count, StringComparer.Ordinal);

        foreach (var kv in raw)
            result[NormalizeHeaderName(kv.name)] = kv.val;

        return result;
    }

    static string NormalizeHeaderName(string key)
    {
        if (normalizedKnownHeaders.TryGetValue(key, out string normalized))
            return normalized;

        return string.Create(key.Length, key, static (span, src) =>
        {
            bool upper = true;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '-')
                {
                    span[i] = '-';
                    upper = true;
                    continue;
                }

                if (upper)
                {
                    span[i] = char.ToUpperInvariant(c);
                    upper = false;
                }
                else
                {
                    span[i] = c;
                }
            }
        });
    }
    #endregion


    #region GetLocation
    async public static Task<string> GetLocation(string url, string referer = null, int timeoutSeconds = 8, IReadOnlyList<HeadersModel> headers = null, int httpversion = 1, bool allowAutoRedirect = false, WebProxy proxy = null)
    {
        var handler = Handler(url, proxy);
        handler.AllowAutoRedirect = allowAutoRedirect;

        var client = FriendlyHttp.MessageClient(
            httpversion switch
            {
                2 => "http2",
                3 => "http3",
                _ => "base"
            },
            handler,
            out bool disposeHttpClient
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = httpversion switch
                {
                    2 => HttpVersion.Version20,
                    3 => HttpVersion.Version30,
                    _ => HttpVersion.Version11
                }
            };

            DefaultRequestHeaders(url, req, null, referer, headers);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            {
                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    string location = (int)response.StatusCode == 301 || (int)response.StatusCode == 302 || (int)response.StatusCode == 307
                        ? response.Headers.Location?.ToString()
                        : response.RequestMessage.RequestUri?.ToString();

                    if (string.IsNullOrEmpty(location))
                        return null;

                    location = System.Web.HttpUtility.UrlDecode(location);

                    if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
                        return uri.AbsoluteUri;

                    return location;
                }
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion

    #region ResponseHeaders
    async public static Task<HttpResponseMessage> ResponseHeaders(string url, int timeoutSeconds = 8, IReadOnlyList<HeadersModel> headers = null, int httpversion = 1, bool allowAutoRedirect = false, WebProxy proxy = null)
    {
        var handler = Handler(url, proxy);
        handler.AllowAutoRedirect = allowAutoRedirect;

        var client = FriendlyHttp.MessageClient(
            httpversion switch
            {
                2 => "http2",
                3 => "http3",
                _ => "base"
            },
            handler,
            out bool disposeHttpClient
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = httpversion switch
                {
                    2 => HttpVersion.Version20,
                    3 => HttpVersion.Version30,
                    _ => HttpVersion.Version11
                }
            };

            DefaultRequestHeaders(url, req, null, null, headers);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            {
                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                    return response;
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion


    #region Get<T>
    async public static Task<T> Get<T>(string url, string cookie = null, string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, bool IgnoreDeserializeObject = false, WebProxy proxy = null, bool statusCodeOK = true, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, HttpContent body = null, HttpClient httpClient = null, bool textJson = false)
    {
        return (await BaseGetAsync<T>(
            url, cookie, referer, MaxResponseContentBufferSize, timeoutSeconds, headers, IgnoreDeserializeObject, proxy, statusCodeOK, httpversion, cookieContainer, useDefaultHeaders, body, httpClient, textJson
        ).ConfigureAwait(false)).content;
    }
    #endregion

    #region BaseGetAsync<T>
    async public static Task<(T content, HttpResponseMessage response)> BaseGetAsync<T>(string url, string cookie = null, string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, bool IgnoreDeserializeObject = false, WebProxy proxy = null, bool statusCodeOK = true, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, HttpContent body = null, HttpClient httpClient = null, bool textJson = false)
    {
        RecyclableMemoryStream msm = null;

        try
        {
            T result = default;

            var req = await BaseGetReaderAsync(async e =>
            {
                try
                {
                    msm = PoolInvk.msm.GetStream();

                    using (var byteBuf = new BufferPool())
                    {
                        int bytesRead;
                        var memBuf = byteBuf.Memory;

                        while ((bytesRead = await e.stream.ReadAsync(memBuf, e.ct).ConfigureAwait(false)) > 0)
                            msm.Write(memBuf.Span.Slice(0, bytesRead));
                    }

                    msm.Position = 0;

                    if (textJson)
                        result = System.Text.Json.JsonSerializer.Deserialize<T>(msm, jsonTextOptions);
                    else
                    {
                        using (var streamReader = new JsonStreamReaderPool(msm, Encoding.UTF8, leaveOpen: true))
                        {
                            using (var jsonReader = new JsonTextReader(streamReader)
                            {
                                ArrayPool = NewtonsoftPool.Array
                            })
                            {
                                var serializer = IgnoreDeserializeObject
                                    ? Newtonsoft.Json.JsonSerializer.Create(newtonsoftIgnoreErrorsSettings)
                                    : Newtonsoft.Json.JsonSerializer.CreateDefault();

                                result = serializer.Deserialize<T>(jsonReader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logEnable)
                        Serilog.Log.Error(ex, "CatchId={CatchId}, Url={Url}", "id_1t8mrdlh", url);
                }
            },
                url, cookie, referer, MaxResponseContentBufferSize, timeoutSeconds, headers, IgnoreDeserializeObject, proxy, statusCodeOK, httpversion, cookieContainer, useDefaultHeaders, body, httpClient
            ).ConfigureAwait(false);

            return (result, req.response);
        }
        finally
        {
            msm?.Dispose();
        }
    }
    #endregion

    #region BaseGetReaderAsync
    async public static Task<(bool success, HttpResponseMessage response)> BaseGetReaderAsync(Func<(Stream stream, CancellationToken ct), Task> action, string url, string cookie = null, string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, bool IgnoreDeserializeObject = false, WebProxy proxy = null, bool statusCodeOK = true, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, HttpContent body = null, HttpClient httpClient = null)
    {
        var client = FriendlyHttp.MessageClient(
            httpversion switch
            {
                2 => "http2",
                3 => "http3",
                _ => "base"
            },
            Handler(url, proxy, cookieContainer),
            out bool disposeHttpClient,
            MaxResponseContentBufferSize,
            httpClient
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = httpversion switch
                {
                    2 => HttpVersion.Version20,
                    3 => HttpVersion.Version30,
                    _ => HttpVersion.Version11
                },
                Content = body
            };

            DefaultRequestHeaders(url, req, cookie, referer, headers, useDefaultHeaders);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            {
                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    using (HttpContent content = response.Content)
                    {
                        if (EventListener.HttpResponse != null)
                        {
                            if (statusCodeOK && response.StatusCode != HttpStatusCode.OK)
                            {
                                await InvokeHttpResponseHandlersAsync(new EventHttpResponse(url, client, body, response, string.Empty)).ConfigureAwait(false);
                                return (false, response);
                            }

                            await content.LoadIntoBufferAsync().ConfigureAwait(false);

                            string result = await content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                            await InvokeHttpResponseHandlersAsync(new EventHttpResponse(url, client, body, response, result)).ConfigureAwait(false);
                        }
                        else
                        {
                            if (statusCodeOK && response.StatusCode != HttpStatusCode.OK)
                                return (false, response);
                        }

                        await using (var stream = await content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                        {
                            await action.Invoke((stream, cts.Token));
                            return (true, response);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (logEnable && ex is not TaskCanceledException)
                Serilog.Log.Error(ex, "CatchId={CatchId}, Url={Url}", "id_6cd10d26", url);

            if (EventListener.HttpResponse != null)
            {
                await InvokeHttpResponseHandlersAsync(new EventHttpResponse(
                    url,
                    null,
                    null,
                    internalServerErrorResponse,
                    ex.ToString()
                )).ConfigureAwait(false);
            }

            return (false, internalServerErrorResponse);
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion


    #region GetSpan
    async public static Task<bool> GetSpan(string url, Action<ReadOnlySpan<char>> spanAction, Encoding encoding = default, string cookie = null, string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, bool statusCodeOK = true, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, HttpContent body = null, HttpClient httpClient = null)
    {
        RecyclableMemoryStream msm = null;

        try
        {
            var req = await BaseGetReaderAsync(async e =>
            {
                try
                {
                    msm = PoolInvk.msm.GetStream();

                    using (var byteBuf = new BufferPool())
                    {
                        int bytesRead;
                        var memBuf = byteBuf.Memory;

                        while ((bytesRead = await e.stream.ReadAsync(memBuf, e.ct).ConfigureAwait(false)) > 0)
                            msm.Write(memBuf.Span.Slice(0, bytesRead));
                    }

                    msm.Position = 0;

                    OwnerTo.Span(msm, encoding != default ? encoding : Encoding.UTF8, span =>
                    {
                        if (span.IsEmpty)
                            return;

                        spanAction.Invoke(span);
                    });
                }
                catch { }
            },
                url, cookie, referer, MaxResponseContentBufferSize, timeoutSeconds, headers, false, proxy, statusCodeOK, httpversion, cookieContainer, useDefaultHeaders, body, httpClient
            ).ConfigureAwait(false);

            return req.success;
        }
        finally
        {
            msm?.Dispose();
        }
    }
    #endregion

    #region PostSpan
    async public static Task<bool> PostSpan(string url, Action<ReadOnlySpan<char>> spanAction, string data, string cookie = null, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, Encoding encoding = default, WebProxy proxy = null, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, int httpversion = 1, int MaxResponseContentBufferSize = 0, bool statusCodeOK = true, HttpClient httpClient = null)
    {
        RecyclableMemoryStream msm = null;

        try
        {
            var req = await BasePostReaderAsync(async e =>
            {
                try
                {
                    msm = PoolInvk.msm.GetStream();

                    using (var byteBuf = new BufferPool())
                    {
                        int bytesRead;
                        var memBuf = byteBuf.Memory;

                        while ((bytesRead = await e.stream.ReadAsync(memBuf, e.ct).ConfigureAwait(false)) > 0)
                            msm.Write(memBuf.Span.Slice(0, bytesRead));
                    }

                    msm.Position = 0;

                    OwnerTo.Span(msm, encoding != default ? encoding : Encoding.UTF8, span =>
                    {
                        if (span.IsEmpty)
                            return;

                        spanAction.Invoke(span);
                    });
                }
                catch { }
            },
                url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"),
                cookie, MaxResponseContentBufferSize, timeoutSeconds, headers, proxy, httpversion, cookieContainer, useDefaultHeaders, statusCodeOK, httpClient
            ).ConfigureAwait(false);

            return req.success;
        }
        finally
        {
            msm?.Dispose();
        }
    }
    #endregion


    #region Get
    async public static Task<string> Get(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, long MaxResponseContentBufferSize = 0, WebProxy proxy = null, int httpversion = 1, bool statusCodeOK = true, bool weblog = true, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, HttpContent body = null, HttpClient httpClient = null)
    {
        return (await BaseGet(url, encoding, cookie: cookie, referer: referer, timeoutSeconds: timeoutSeconds, headers: headers, MaxResponseContentBufferSize: MaxResponseContentBufferSize, proxy: proxy, httpversion: httpversion, statusCodeOK: statusCodeOK, weblog: weblog, cookieContainer: cookieContainer, useDefaultHeaders: useDefaultHeaders, body: body, httpClient: httpClient).ConfigureAwait(false)).content;
    }
    #endregion

    #region BaseGet
    async public static Task<(string content, HttpResponseMessage response)> BaseGet(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, long MaxResponseContentBufferSize = 0, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, int httpversion = 1, bool statusCodeOK = true, bool weblog = true, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, HttpContent body = null, HttpClient httpClient = null)
    {
        var client = FriendlyHttp.MessageClient(
            httpversion switch
            {
                2 => "http2",
                3 => "http3",
                _ => "base"
            },
            Handler(url, proxy, cookieContainer),
            out bool disposeHttpClient,
            MaxResponseContentBufferSize,
            httpClient
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = httpversion switch
                {
                    2 => HttpVersion.Version20,
                    3 => HttpVersion.Version30,
                    _ => HttpVersion.Version11
                },
                Content = body
            };

            DefaultRequestHeaders(url, req, cookie, referer, headers, useDefaultHeaders);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            {
                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    using (HttpContent content = response.Content)
                    {
                        if (statusCodeOK && response.StatusCode != HttpStatusCode.OK)
                            return (null, response);

                        string res = null;

                        if (encoding == default || encoding == Encoding.UTF8)
                        {
                            res = await content.ReadAsStringAsync(cts.Token);
                        }
                        else
                        {
                            await using (var stream = await content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                            {
                                using (var reader = new StreamReader(stream, encoding, false, PoolInvk.bufferSize, leaveOpen: true))
                                    res = await reader.ReadToEndAsync(cts.Token).ConfigureAwait(false);
                            }
                        }

                        if (EventListener.HttpResponse != null)
                            await InvokeHttpResponseHandlersAsync(new EventHttpResponse(url, client, body, response, res)).ConfigureAwait(false);

                        if (string.IsNullOrWhiteSpace(res))
                            return (null, response);

                        return (res, response);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (logEnable && ex is not TaskCanceledException)
                Serilog.Log.Error(ex, "CatchId={CatchId}, Url={Url}", "id_017bf8af", url);

            if (EventListener.HttpResponse != null)
            {
                await InvokeHttpResponseHandlersAsync(new EventHttpResponse(
                    url,
                    null,
                    null,
                    internalServerErrorResponse,
                    ex.ToString()
                )).ConfigureAwait(false);
            }

            return (null, internalServerErrorResponse);
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion


    #region Post
    public static Task<string> Post(string url, string data, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, bool removeContentType = false, Encoding encoding = default, bool statusCodeOK = true, HttpClient httpClient = null)
    {
        return Post(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"),
            encoding, cookie, MaxResponseContentBufferSize, timeoutSeconds, headers, proxy, httpversion, cookieContainer, useDefaultHeaders, removeContentType, statusCodeOK, httpClient
        );
    }

    async public static Task<string> Post(string url, HttpContent data, Encoding encoding = default, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, bool removeContentType = false, bool statusCodeOK = true, HttpClient httpClient = null)
    {
        return (await BasePost(
            url, data, encoding, cookie, MaxResponseContentBufferSize, timeoutSeconds, headers, proxy, httpversion, cookieContainer, useDefaultHeaders, removeContentType, statusCodeOK, httpClient
        ).ConfigureAwait(false)).content;
    }
    #endregion

    #region BasePost
    async public static Task<(string content, HttpResponseMessage response)> BasePost(string url, HttpContent data, Encoding encoding = default, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, bool removeContentType = false, bool statusCodeOK = true, HttpClient httpClient = null)
    {
        var client = FriendlyHttp.MessageClient(
            httpversion switch
            {
                2 => "http2",
                3 => "http3",
                _ => "base"
            },
            Handler(url, proxy, cookieContainer),
            out bool disposeHttpClient,
            MaxResponseContentBufferSize,
            httpClient
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Version = httpversion switch
                {
                    2 => HttpVersion.Version20,
                    3 => HttpVersion.Version30,
                    _ => HttpVersion.Version11
                },
                Content = data
            };

            DefaultRequestHeaders(url, req, cookie, null, headers, useDefaultHeaders);

            if (removeContentType)
                req.Content.Headers.Remove("Content-Type");

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            {
                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    using (HttpContent content = response.Content)
                    {
                        if (statusCodeOK && response.StatusCode != HttpStatusCode.OK)
                            return (null, response);

                        string res = null;

                        if (encoding == default || encoding == Encoding.UTF8)
                        {
                            res = await content.ReadAsStringAsync(cts.Token);
                        }
                        else
                        {
                            await using (var stream = await content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                            {
                                using (var reader = new StreamReader(stream, encoding, false, PoolInvk.bufferSize, leaveOpen: true))
                                    res = await reader.ReadToEndAsync(cts.Token).ConfigureAwait(false);
                            }
                        }

                        if (EventListener.HttpResponse != null)
                            await InvokeHttpResponseHandlersAsync(new EventHttpResponse(url, client, data, response, res)).ConfigureAwait(false);

                        if (string.IsNullOrWhiteSpace(res))
                            return (null, response);

                        return (res, response);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (logEnable && ex is not TaskCanceledException)
                Serilog.Log.Error(ex, "CatchId={CatchId}, Url={Url}", "id_dd21e44c", url);

            if (EventListener.HttpResponse != null)
            {
                await InvokeHttpResponseHandlersAsync(new EventHttpResponse(
                    url,
                    null,
                    data,
                    internalServerErrorResponse,
                    ex.ToString()
                )).ConfigureAwait(false);
            }

            return (null, internalServerErrorResponse);
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion


    #region Post<T>
    public static Task<T> Post<T>(string url, string data, string cookie = null, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, int httpversion = 1, int MaxResponseContentBufferSize = 0, bool statusCodeOK = true, HttpClient httpClient = null, bool textJson = false)
    {
        return Post<T>(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"),
            cookie, timeoutSeconds, headers, encoding, proxy, IgnoreDeserializeObject, cookieContainer, useDefaultHeaders, httpversion, MaxResponseContentBufferSize, statusCodeOK, httpClient, textJson
        );
    }

    async public static Task<T> Post<T>(string url, HttpContent data, string cookie = null, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, int httpversion = 1, int MaxResponseContentBufferSize = 0, bool statusCodeOK = true, HttpClient httpClient = null, bool textJson = false)
    {
        RecyclableMemoryStream msm = null;

        try
        {
            T result = default;

            var req = await BasePostReaderAsync(async e =>
            {
                try
                {
                    msm = PoolInvk.msm.GetStream();

                    using (var byteBuf = new BufferPool())
                    {
                        int bytesRead;
                        var memBuf = byteBuf.Memory;

                        while ((bytesRead = await e.stream.ReadAsync(memBuf, e.ct).ConfigureAwait(false)) > 0)
                            msm.Write(memBuf.Span.Slice(0, bytesRead));
                    }

                    msm.Position = 0;

                    var encdg = encoding != default ? encoding : Encoding.UTF8;

                    if (textJson && encdg == Encoding.UTF8)
                        result = System.Text.Json.JsonSerializer.Deserialize<T>(msm, jsonTextOptions);
                    else
                    {
                        using (var streamReader = new JsonStreamReaderPool(msm, encdg, leaveOpen: true))
                        {
                            using (var jsonReader = new JsonTextReader(streamReader)
                            {
                                ArrayPool = NewtonsoftPool.Array
                            })
                            {
                                var serializer = IgnoreDeserializeObject
                                    ? Newtonsoft.Json.JsonSerializer.Create(newtonsoftIgnoreErrorsSettings)
                                    : Newtonsoft.Json.JsonSerializer.CreateDefault();

                                result = serializer.Deserialize<T>(jsonReader);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (logEnable)
                        Serilog.Log.Error(ex, "CatchId={CatchId}, Url={Url}", "id_gqpu49cz", url);
                }
            },
                url, data, cookie, MaxResponseContentBufferSize, timeoutSeconds, headers, proxy, httpversion, cookieContainer, useDefaultHeaders, statusCodeOK, httpClient
            ).ConfigureAwait(false);

            return result;
        }
        finally
        {
            msm?.Dispose();
        }
    }
    #endregion

    #region BasePostReaderAsync
    async public static Task<(bool success, HttpResponseMessage response)> BasePostReaderAsync(Func<(Stream stream, CancellationToken ct), Task> action, string url, HttpContent data, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, int httpversion = 1, CookieContainer cookieContainer = null, bool useDefaultHeaders = true, bool statusCodeOK = true, HttpClient httpClient = null)
    {
        var client = FriendlyHttp.MessageClient(
            httpversion switch
            {
                2 => "http2",
                3 => "http3",
                _ => "base"
            },
            Handler(url, proxy, cookieContainer),
            out bool disposeHttpClient,
            MaxResponseContentBufferSize,
            httpClient
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Version = httpversion switch
                {
                    2 => HttpVersion.Version20,
                    3 => HttpVersion.Version30,
                    _ => HttpVersion.Version11
                },
                Content = data
            };

            DefaultRequestHeaders(url, req, cookie, null, headers, useDefaultHeaders);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds))))
            {
                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    using (HttpContent content = response.Content)
                    {
                        if (EventListener.HttpResponse != null)
                        {
                            if (statusCodeOK && response.StatusCode != HttpStatusCode.OK)
                            {
                                await InvokeHttpResponseHandlersAsync(new EventHttpResponse(url, client, data, response, string.Empty)).ConfigureAwait(false);
                                return (false, response);
                            }

                            await content.LoadIntoBufferAsync().ConfigureAwait(false);

                            string result = await content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                            await InvokeHttpResponseHandlersAsync(new EventHttpResponse(url, client, data, response, result)).ConfigureAwait(false);
                        }
                        else
                        {
                            if (statusCodeOK && response.StatusCode != HttpStatusCode.OK)
                                return (false, response);
                        }

                        await using (var stream = await content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
                        {
                            await action.Invoke((stream, cts.Token));
                            return (true, response);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (logEnable && ex is not TaskCanceledException)
                Serilog.Log.Error(ex, "CatchId={CatchId}, Url={Url}", "id_35f7be5e", url);

            if (EventListener.HttpResponse != null)
            {
                await InvokeHttpResponseHandlersAsync(new EventHttpResponse(
                    url,
                    null,
                    data,
                    internalServerErrorResponse,
                    ex.ToString()
                )).ConfigureAwait(false);
            }

            return (false, internalServerErrorResponse);
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion


    #region Download
    async public static Task<byte[]> Download(string url, string cookie = null, string referer = null, int timeoutSeconds = 60, long MaxResponseContentBufferSize = 50_000_000, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, bool statusCodeOK = true, bool useDefaultHeaders = true)
    {
        return (await BaseDownload(url, cookie, referer, timeoutSeconds, MaxResponseContentBufferSize, headers, proxy, statusCodeOK, useDefaultHeaders).ConfigureAwait(false)).array;
    }
    #endregion

    #region BaseDownload
    async public static Task<(byte[] array, HttpResponseMessage response)> BaseDownload(string url, string cookie = null, string referer = null, int timeoutSeconds = 60, long MaxResponseContentBufferSize = 50_000_000, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null, bool statusCodeOK = true, bool useDefaultHeaders = true)
    {
        var client = FriendlyHttp.MessageClient(
            "base",
            Handler(url, proxy),
            out bool disposeHttpClient,
            MaxResponseContentBufferSize
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = HttpVersion.Version11
            };

            DefaultRequestHeaders(url, req, cookie, referer, headers, useDefaultHeaders);

            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(20, timeoutSeconds))))
            {
                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    if (statusCodeOK && response.StatusCode != HttpStatusCode.OK)
                        return (null, response);

                    using (HttpContent content = response.Content)
                    {
                        byte[] res = await content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                        if (res == null || res.Length == 0)
                            return (null, response);

                        return (res, response);
                    }
                }
            }
        }
        catch
        {
            return (null, internalServerErrorResponse);
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion

    #region DownloadFile
    async public static Task<bool> DownloadFile(string url, string path, int timeoutSeconds = 20, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null)
    {
        try
        {
            using (var handler = Handler(url, proxy))
            {
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                    bool setDefaultUseragent = true;

                    if (headers != null)
                    {
                        foreach (var item in headers)
                        {
                            if (item.name.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                                setDefaultUseragent = false;

                            if (!client.DefaultRequestHeaders.Contains(item.name))
                                client.DefaultRequestHeaders.Add(item.name, item.val);
                        }
                    }

                    if (setDefaultUseragent)
                        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                    await using (var stream = await client.GetStreamAsync(url))
                    {
                        await using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, PoolInvk.bufferSize, options: FileOptions.Asynchronous))
                        {
                            await stream.CopyToAsync(fileStream);
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region DownloadToStream
    async public static Task<bool> DownloadToStream(Stream ms, string url, int timeoutSeconds = 20, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null)
    {
        try
        {
            using (var handler = Handler(url, proxy))
            {
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

                    bool setDefaultUseragent = true;

                    if (headers != null)
                    {
                        foreach (var item in headers)
                        {
                            if (item.name.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                                setDefaultUseragent = false;

                            if (!client.DefaultRequestHeaders.Contains(item.name))
                                client.DefaultRequestHeaders.Add(item.name, item.val);
                        }
                    }

                    if (setDefaultUseragent)
                        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                    using (HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false))
                    {
                        if (response.StatusCode != HttpStatusCode.OK)
                            return false;

                        using (HttpContent content = response.Content)
                        {
                            await content.CopyToAsync(ms);
                            ms.Position = 0;
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            return false;
        }
    }
    #endregion


    #region Utilities
    static bool HasUserAgent(IReadOnlyList<HeadersModel> headers)
    {
        if (headers == null)
            return false;

        foreach (var h in headers)
        {
            if (h.name.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
    #endregion
}
