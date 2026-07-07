using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;

namespace Shared.Services;

public class HttpHydra
{
    BaseSettings init;
    RequestModel requestInfo;
    RchClient rch;
    WebProxy proxy;
    IReadOnlyList<HeadersModel> baseHeaders;
    HttpClient httpClient;

    public HttpHydra(BaseSettings init, IReadOnlyList<HeadersModel> baseHeaders, RequestModel requestInfo, RchClient rch, WebProxy proxy)
    {
        this.init = init;
        this.requestInfo = requestInfo;
        this.baseHeaders = baseHeaders;
        this.rch = rch;

        if (init.useproxy)
            this.proxy = proxy;
    }

    public void RegisterHttp(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }


    #region Get
    public Task<T> Get<T>(string url, IReadOnlyList<HeadersModel> addheaders = null, IReadOnlyList<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool IgnoreDeserializeObject = false, bool safety = false, bool textJson = false, CookieContainer cookieContainer = null)
    {
        var headers = JoinHeaders(addheaders, newheaders);

        return IsRchEnable(safety)
            ? rch.Get<T>(init.cors(url, headers, requestInfo), headers, IgnoreDeserializeObject, useDefaultHeaders, textJson)
            : Http.Get<T>(init.cors(url, headers, requestInfo), null, null, 0, init.httptimeout, headers, IgnoreDeserializeObject, proxy, statusCodeOK, init.httpversion, cookieContainer, useDefaultHeaders: useDefaultHeaders, httpClient: httpClient, textJson: textJson);
    }

    public Task<string> Get(string url, IReadOnlyList<HeadersModel> addheaders = null, IReadOnlyList<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool safety = false, CookieContainer cookieContainer = null)
    {
        var headers = JoinHeaders(addheaders, newheaders);

        return IsRchEnable(safety)
            ? rch.Get(init.cors(url, headers, requestInfo), headers, useDefaultHeaders)
            : Http.Get(init.cors(url, headers, requestInfo), encoding, null, null, init.httptimeout, headers, 0, proxy, init.httpversion, statusCodeOK, cookieContainer: cookieContainer, useDefaultHeaders: useDefaultHeaders, httpClient: httpClient);
    }
    #endregion

    #region GetSpan
    public Task GetSpan(string url, Action<ReadOnlySpan<char>> spanAction, IReadOnlyList<HeadersModel> addheaders = null, IReadOnlyList<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, bool safety = false, CookieContainer cookieContainer = null)
    {
        var headers = JoinHeaders(addheaders, newheaders);

        return IsRchEnable(safety)
            ? rch.GetSpan(init.cors(url, headers, requestInfo), spanAction, headers, useDefaultHeaders)
            : Http.GetSpan(init.cors(url, headers, requestInfo), spanAction, default, null, null, 0, init.httptimeout, headers, proxy, statusCodeOK, init.httpversion, cookieContainer, useDefaultHeaders: useDefaultHeaders, httpClient: httpClient);
    }
    #endregion


    #region Post
    public Task<T> Post<T>(string url, string data, IReadOnlyList<HeadersModel> addheaders = null, IReadOnlyList<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool IgnoreDeserializeObject = false, bool safety = false, bool textJson = false, CookieContainer cookieContainer = null)
    {
        var headers = JoinHeaders(addheaders, newheaders);

        return IsRchEnable(safety)
            ? rch.Post<T>(init.cors(url, headers, requestInfo), data, headers, IgnoreDeserializeObject, useDefaultHeaders, textJson)
            : Http.Post<T>(init.cors(url, headers, requestInfo), data, null, init.httptimeout, headers, encoding, proxy, IgnoreDeserializeObject, cookieContainer, useDefaultHeaders, init.httpversion, 0, statusCodeOK, httpClient, textJson);
    }

    public Task<string> Post(string url, string data, IReadOnlyList<HeadersModel> addheaders = null, IReadOnlyList<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool safety = false, CookieContainer cookieContainer = null)
    {
        var headers = JoinHeaders(addheaders, newheaders);

        return IsRchEnable(safety)
            ? rch.Post(init.cors(url, headers, requestInfo), data, headers, useDefaultHeaders)
            : Http.Post(init.cors(url, headers, requestInfo), data, null, 0, init.httptimeout, headers, proxy, init.httpversion, cookieContainer, useDefaultHeaders, false, encoding, statusCodeOK, httpClient);
    }
    #endregion

    #region PostSpan
    public Task PostSpan(string url, string data, Action<ReadOnlySpan<char>> spanAction, IReadOnlyList<HeadersModel> addheaders = null, IReadOnlyList<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool safety = false, CookieContainer cookieContainer = null)
    {
        var headers = JoinHeaders(addheaders, newheaders);

        return IsRchEnable(safety)
            ? rch.PostSpan(init.cors(url, headers, requestInfo), spanAction, data, headers, useDefaultHeaders)
            : Http.PostSpan(init.cors(url, headers, requestInfo), spanAction, data, null, init.httptimeout, headers, encoding, proxy, cookieContainer, useDefaultHeaders, init.httpversion, 0, statusCodeOK, httpClient);
    }
    #endregion


    #region Helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsRchEnable(bool safety)
    {
        bool rch_enable = rch?.enable == true;
        if (rch_enable)
        {
            if (safety && init.rhub_safety)
                rch_enable = false;
        }

        return rch_enable;
    }

    IReadOnlyList<HeadersModel> JoinHeaders(IReadOnlyList<HeadersModel> addheaders = null, IReadOnlyList<HeadersModel> newheaders = null)
    {
        int baseCount = baseHeaders?.Count ?? 0;
        int newCount = newheaders?.Count ?? 0;
        int addCount = addheaders?.Count ?? 0;

        int total = baseCount + newCount + addCount;
        if (total == 0)
            return null;

        if (newCount == 0 && addCount == 0)
            return baseHeaders;

        if (baseCount == 0 && addCount == 0)
            return newheaders;

        if (baseCount == 0 && newCount == 0)
            return addheaders;

        var headers = new List<HeadersModel>(total);

        for (int i = 0; i < baseCount; i++)
            headers.Add(baseHeaders[i]);

        for (int i = 0; i < newCount; i++)
            headers.Add(newheaders[i]);

        for (int i = 0; i < addCount; i++)
            headers.Add(addheaders[i]);

        return headers;
    }
    #endregion
}
