using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Shared;
using Shared.Models.Base;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Corseu;

public class CorseuController : BaseController
{
    #region Routes
    [HttpGet]
    [AllowAnonymous]
    [Route("/corseu/{token}/{*url}")]
    public Task<IActionResult> Get(string token, string url)
    {
        return ExecuteAsync(new CorseuRequest
        {
            url = url + HttpContext.Request.QueryString.Value,
            auth_token = token
        });
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/corseu")]
    public Task<IActionResult> Get(string auth_token, string method, string url, string data, string headers, string browser, int? httpversion, int? timeout, string encoding, bool? defaultHeaders, bool? autoredirect, string proxy, string proxy_name, bool? headersOnly)
    {
        return ExecuteAsync(new CorseuRequest
        {
            url = url,
            method = method,
            data = data,
            browser = browser,
            httpversion = httpversion,
            timeout = timeout,
            encoding = encoding,
            defaultHeaders = defaultHeaders,
            autoredirect = autoredirect,
            proxy = proxy,
            proxy_name = proxy_name,
            headersOnly = headersOnly,
            auth_token = auth_token,
            headers = ParseHeaders(headers)
        });
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("/corseu")]
    async public Task<IActionResult> Post()
    {
        try
        {
            using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                string body = await reader.ReadToEndAsync().ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(body))
                    return BadRequest("Empty body");

                var model = JsonConvert.DeserializeObject<CorseuRequest>(body);
                if (model == null)
                    return BadRequest("Invalid body");

                return await ExecuteAsync(model);
            }
        }
        catch (JsonException)
        {
            return BadRequest("Invalid JSON");
        }
    }
    #endregion

    #region Execute
    async Task<IActionResult> ExecuteAsync(CorseuRequest model)
    {
        var init = ModInit.conf;

        if (init?.tokens == null || init.tokens.Length == 0)
            return StatusCode((int)HttpStatusCode.Unauthorized);

        if (string.IsNullOrEmpty(model?.auth_token) || !init.tokens.Contains(model.auth_token))
            return StatusCode((int)HttpStatusCode.Unauthorized);

        if (string.IsNullOrWhiteSpace(model?.url))
            return BadRequest("url is empty");

        string method = string.IsNullOrWhiteSpace(model.method) ? "GET" : model.method.ToUpperInvariant();
        string browser = string.IsNullOrWhiteSpace(model.browser) ? "http" : model.browser.ToLowerInvariant();

        var headers = model.headers != null
            ? new Dictionary<string, string>(model.headers, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        bool useDefaultHeaders = model.defaultHeaders ?? true;
        bool autoRedirect = model.autoredirect ?? true;
        bool headersOnly = model.headersOnly ?? false;
        int timeout = model.timeout.HasValue && model.timeout.Value > 5 ? model.timeout.Value : 15;
        int httpVersion = model.httpversion ?? 1;

        #region rules
        if (init?.rules != null)
        {
            foreach (var rule in init.rules)
            {
                if (rule?.headers == null || rule.headers.Count == 0)
                    continue;

                if (string.IsNullOrEmpty(rule.method) || string.IsNullOrEmpty(rule.url))
                    continue;

                if (!string.Equals(rule.method, method, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!Regex.IsMatch(model.url, rule.url, RegexOptions.IgnoreCase))
                    continue;

                var ruleHeaders = new Dictionary<string, string>(rule.headers, StringComparer.OrdinalIgnoreCase);

                if (rule.replace)
                {
                    headers = ruleHeaders;
                }
                else
                {
                    foreach (var pair in ruleHeaders)
                        headers[pair.Key] = pair.Value;
                }
            }
        }
        #endregion

        string contentType = null;
        if (headers.TryGetValue("content-type", out string ct))
        {
            contentType = ct;
            headers.Remove("content-type");
        }

        if (headers.ContainsKey("content-length"))
            headers.Remove("content-length");

        if (browser is "chromium" or "playwright")
            return await SendWithChromiumAsync(method, model.url, model.data, headers, contentType, timeout, autoRedirect, headersOnly, model.proxy, model.proxy_name);

        return await SendWithHttpClientAsync(method, model.url, model.data, headers, contentType, timeout, httpVersion, useDefaultHeaders, autoRedirect, headersOnly, model.proxy, model.proxy_name, model.encoding);
    }
    #endregion

    #region HttpClient
    async Task<IActionResult> SendWithHttpClientAsync(
        string method, string url, string data, Dictionary<string, string> headers,
        string contentType, int timeout, int httpVersion, bool useDefaultHeaders, bool autoRedirect, bool headersOnly, string encodingName,
        string proxyValue, string proxyName)
    {
        var proxyManager = CreateProxy(url, proxyValue, proxyName);

        try
        {
            var handler = Http.Handler(url, proxyManager.Get());
            handler.AllowAutoRedirect = autoRedirect;

            var client = FriendlyHttp.MessageClient(
                httpVersion switch
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
                using (var request = new HttpRequestMessage(new HttpMethod(method), url))
                {
                    request.Version = httpVersion switch
                    {
                        2 => HttpVersion.Version20,
                        3 => HttpVersion.Version30,
                        _ => HttpVersion.Version11
                    };

                    if (!string.IsNullOrEmpty(data))
                    {
                        var encoding = string.IsNullOrEmpty(encodingName)
                            ? Encoding.UTF8
                            : Encoding.GetEncoding(encodingName);

                        var content = new StringContent(data, encoding);

                        if (!string.IsNullOrEmpty(contentType))
                            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

                        request.Content = content;
                    }

                    var headersModel = headers.Count > 0 ? HeadersModel.Init(headers) : null;
                    Http.DefaultRequestHeaders(url, request, null, null, headersModel, useDefaultHeaders);

                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, timeout)));

                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                        {
                            proxyManager.Success();

                            await CopyResponseAsync(response, headersOnly).ConfigureAwait(false);
                            return new EmptyResult();
                        }
                    }
                }
            }
            finally
            {
                if (disposeHttpClient)
                    client.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            proxyManager.Refresh();
            return StatusCode((int)HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            proxyManager.Refresh();
            return StatusCode((int)HttpStatusCode.BadGateway, ex.Message);
        }
    }
    #endregion

    #region Chromium
    async Task<IActionResult> SendWithChromiumAsync(
        string method, string url, string data, Dictionary<string, string> headers,
        string contentType, int timeout, bool autoRedirect, bool headersOnly,
        string proxyValue, string proxyName)
    {
        var proxyManager = CreateProxy(url, proxyValue, proxyName);
        var proxy = proxyManager.BaseGet();

        try
        {
            if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
                return StatusCode((int)HttpStatusCode.BadGateway, "PlaywrightStatus disabled");

            var contextHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            var requestHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(contentType))
            {
                if (!requestHeaders.ContainsKey("content-type"))
                {
                    requestHeaders["content-type"] = contentType;
                    contextHeaders["content-type"] = contentType;
                }
            }

            var contextOptions = new APIRequestNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ExtraHTTPHeaders = Http.NormalizeHeaders(contextHeaders),
                Timeout = timeout * 1000
            };

            if (requestHeaders.TryGetValue("user-agent", out string _useragent))
                contextOptions.UserAgent = _useragent;

            var requestOptions = new APIRequestContextOptions
            {
                Method = method,
                Headers = requestHeaders,
                Timeout = timeout * 1000
            };

            if (!string.IsNullOrEmpty(data))
                requestOptions.DataString = data;

            if (!autoRedirect)
                requestOptions.MaxRedirects = 0;

            if (proxy.proxy != null)
            {
                contextOptions.Proxy = new Proxy
                {
                    Server = proxy.data.ip,
                    Username = proxy.data.username,
                    Password = proxy.data.password
                };
            }

            await using (var requestContext = await Chromium.playwright.APIRequest.NewContextAsync(contextOptions).ConfigureAwait(false))
            {
                var response = await requestContext.FetchAsync(url, requestOptions).ConfigureAwait(false);

                try
                {
                    HttpContext.Response.StatusCode = response.Status;

                    foreach (var header in response.HeadersArray)
                    {
                        var headerName = header.Name.ToLowerInvariant();

                        if (ShouldSkipHeader(headerName))
                            continue;

                        if (headerName == "content-type")
                            HttpContext.Response.ContentType = header.Value;

                        HttpContext.Response.Headers[header.Name] = header.Value;
                    }

                    if (headersOnly)
                    {
                        proxyManager.Success();
                        await HttpContext.Response.CompleteAsync().ConfigureAwait(false);
                        return new EmptyResult();
                    }

                    var body = await response.BodyAsync().ConfigureAwait(false);
                    if (body?.Length > 0)
                        await HttpContext.Response.Body.WriteAsync(body, 0, body.Length, HttpContext.RequestAborted).ConfigureAwait(false);

                    proxyManager.Success();
                    return new EmptyResult();
                }
                finally
                {
                    await response.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            proxyManager.Refresh();
            return StatusCode((int)HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            proxyManager.Refresh();
            return StatusCode((int)HttpStatusCode.BadGateway, ex.Message);
        }
    }
    #endregion

    #region Helpers
    Dictionary<string, string> ParseHeaders(string headers)
    {
        try
        {
            if (!string.IsNullOrEmpty(headers))
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(headers);
        }
        catch { }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    ProxyManager CreateProxy(string url, string proxyValue, string proxyName)
    {
        var model = new BaseSettings()
        {
            plugin = $"corseu:{Regex.Match(url, "https?://([^/]+)")}"
        };

        if (!string.IsNullOrEmpty(proxyValue))
        {
            model.proxy = new ProxySettings();
            model.proxy.list = [proxyValue];
        }
        else if (!string.IsNullOrEmpty(proxyName))
        {
            if (CoreInit.conf.globalproxy != null)
            {
                var settings = CoreInit.conf.globalproxy.FirstOrDefault(i => i.name == proxyName);
                if (settings?.list != null && settings.list.Length > 0)
                    model.proxy = settings;
            }
        }

        if (model.proxy != null)
            model.useproxy = true;

        return new ProxyManager("corseu", model);
    }

    async Task CopyResponseAsync(HttpResponseMessage response, bool headersOnly)
    {
        var httpResponse = HttpContext.Response;
        httpResponse.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
        {
            if (ShouldSkipHeader(header.Key))
                continue;

            httpResponse.Headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                httpResponse.ContentType = response.Content.Headers.ContentType?.ToString();
                continue;
            }

            if (ShouldSkipHeader(header.Key))
                continue;

            httpResponse.Headers[header.Key] = string.Join(", ", header.Value);
        }

        if (headersOnly)
        {
            await httpResponse.CompleteAsync().ConfigureAwait(false);
            return;
        }

        using (var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await responseStream.CopyToAsync(httpResponse.Body, HttpContext.RequestAborted).ConfigureAwait(false);
    }

    bool ShouldSkipHeader(string header)
    {
        string key = header.ToLowerInvariant();

        return key switch
        {
            "content-length" => true,
            "transfer-encoding" => true,
            "connection" => true,
            "keep-alive" => true,
            "content-disposition" => true,
            "content-encoding" => true,
            "content-security-policy" => true,
            "vary" => true,
            "alt-svc" => true,
            _ when key.StartsWith("access-control") => true,
            _ when key.StartsWith("x-") => true,
            _ => false
        };
    }
    #endregion
}
