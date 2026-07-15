using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Music;

public static class MusicStreamRelayService
{
    static readonly HttpClient musicStreamClient = CreateMusicStreamClient();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HttpClient> proxyClients = new();

    // failOnUpstreamError: не транслировать «мёртвую ссылку» (403/404/410) клиенту,
    // а вернуть null — вызывающий пере-резолвит источник по fallback-параметрам
    public static async Task<ActionResult> RelayAsync(HttpContext httpContext, MusicPlaybackSource source, bool failOnUpstreamError = false)
    {
        if (httpContext == null || source == null || string.IsNullOrWhiteSpace(source.url))
            return failOnUpstreamError ? null : new StatusCodeResult(404);

        using var request = new HttpRequestMessage(HttpMethod.Get, source.url);

        if (source.headers != null)
        {
            foreach (var header in source.headers)
            {
                if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
                    continue;

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (httpContext.Request.Headers.TryGetValue("Range", out var range) && !string.IsNullOrWhiteSpace(range.ToString()))
            request.Headers.TryAddWithoutValidation("Range", range.ToString());

        HttpClient client = GetClient(source);
        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch when (failOnUpstreamError)
        {
            return null;
        }

        using (response)
        {
            if (failOnUpstreamError && IsDeadUpstreamStatus(response.StatusCode))
                return null;

            httpContext.Response.StatusCode = (int)response.StatusCode;
            httpContext.Response.Headers.Remove("transfer-encoding");

            CopyResponseHeader(httpContext, "Accept-Ranges", response.Headers, response.Content.Headers);
            CopyResponseHeader(httpContext, "Content-Range", response.Headers, response.Content.Headers);
            CopyResponseHeader(httpContext, "Content-Length", response.Headers, response.Content.Headers);
            CopyResponseHeader(httpContext, "Content-Type", response.Headers, response.Content.Headers);
            CopyResponseHeader(httpContext, "Cache-Control", response.Headers, response.Content.Headers);
            CopyResponseHeader(httpContext, "Expires", response.Headers, response.Content.Headers);
            CopyResponseHeader(httpContext, "Last-Modified", response.Headers, response.Content.Headers);
            CopyResponseHeader(httpContext, "ETag", response.Headers, response.Content.Headers);

            if (!string.IsNullOrWhiteSpace(source.mime_type))
                httpContext.Response.ContentType = source.mime_type;

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(httpContext.RequestAborted);
                await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // клиент ушёл (перемотка/стоп/смена трека) — штатный путь
            }
            catch (IOException)
            {
                // upstream оборвал середину стрима: рвём соединение, чтобы клиент
                // сделал range-retry, а не принял обрыв за конец файла
                httpContext.Abort();
            }

            return new EmptyResult();
        }
    }

    static bool IsDeadUpstreamStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone;

    static HttpClient GetClient(MusicPlaybackSource source)
    {
        var proxy = CreateMusicStreamProxy(source);
        if (proxy == null)
            return musicStreamClient;

        // клиент на прокси кэшируется: без этого каждый range-запрос платит
        // новый TLS-хендшейк через прокси; кардинальность ключей — от конфига
        string key = $"{source.proxy_url}|{source.proxy_username}|{source.proxy_password}";

        return proxyClients.GetOrAdd(key, _ =>
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                Proxy = proxy,
                UseProxy = true
            };

            handler.ServerCertificateCustomValidationCallback += (_, _, _, _) => true;

            return new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        });
    }

    static HttpClient CreateMusicStreamClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        };

        handler.ServerCertificateCustomValidationCallback += (_, _, _, _) => true;

        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    static void CopyResponseHeader(HttpContext httpContext, string headerName, HttpResponseHeaders responseHeaders, HttpContentHeaders contentHeaders)
    {
        if (responseHeaders.TryGetValues(headerName, out var headerValues) || contentHeaders.TryGetValues(headerName, out headerValues))
            httpContext.Response.Headers[headerName] = headerValues.ToArray();
    }

    static WebProxy CreateMusicStreamProxy(MusicPlaybackSource source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.proxy_url))
            return null;

        if (!Uri.TryCreate(source.proxy_url, UriKind.Absolute, out var proxyUri))
            return null;

        NetworkCredential credentials = null;
        if (!string.IsNullOrWhiteSpace(source.proxy_username))
            credentials = new NetworkCredential(source.proxy_username, source.proxy_password ?? string.Empty);

        return new WebProxy(proxyUri, false, null, credentials);
    }
}
