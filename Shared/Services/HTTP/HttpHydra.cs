using System.Net;
using System.Net.Http;
using System.Text;

namespace Shared.Services
{
    public class HttpHydra
    {
        BaseSettings init;
        RequestModel requestInfo;
        RchClient rch;
        WebProxy proxy;
        List<HeadersModel> baseHeaders;
        HttpClient httpClient;

        public HttpHydra(BaseSettings init, List<HeadersModel> baseHeaders, RequestModel requestInfo, RchClient rch, WebProxy proxy)
        {
            this.init = init;
            this.requestInfo = requestInfo;
            this.rch = rch;
            this.proxy = proxy;
            this.baseHeaders = baseHeaders;
        }


        #region Get
        public Task<T> Get<T>(string url, List<HeadersModel> addheaders = null, List<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool IgnoreDeserializeObject = false, bool safety = false, bool textJson = false, CookieContainer cookieContainer = null)
        {
            var headers = JsonHeaders(addheaders, newheaders);

            return IsRchEnable(safety)
                ? rch.Get<T>(init.cors(url, headers, requestInfo), headers, IgnoreDeserializeObject, useDefaultHeaders, textJson)
                : Http.Get<T>(init.cors(url, headers, requestInfo), null, null, 0, init.httptimeout, headers, IgnoreDeserializeObject, proxy, statusCodeOK, init.httpversion, cookieContainer, useDefaultHeaders: useDefaultHeaders, httpClient: httpClient, textJson: textJson);
        }

        public Task<string> Get(string url, List<HeadersModel> addheaders = null, List<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool safety = false, CookieContainer cookieContainer = null)
        {
            var headers = JsonHeaders(addheaders, newheaders);

            return IsRchEnable(safety)
                ? rch.Get(init.cors(url, headers, requestInfo), headers, useDefaultHeaders)
                : Http.Get(init.cors(url, headers, requestInfo), encoding, null, null, init.httptimeout, headers, 0, proxy, init.httpversion, statusCodeOK, cookieContainer: cookieContainer, useDefaultHeaders: useDefaultHeaders, httpClient: httpClient);
        }
        #endregion

        #region GetSpan
        public Task GetSpan(string url, Action<ReadOnlySpan<char>> spanAction, List<HeadersModel> addheaders = null, List<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, bool safety = false, CookieContainer cookieContainer = null)
        {
            var headers = JsonHeaders(addheaders, newheaders);

            return IsRchEnable(safety)
                ? rch.GetSpan(init.cors(url, headers, requestInfo), spanAction, headers, useDefaultHeaders)
                : Http.GetSpan(init.cors(url, headers, requestInfo), spanAction, default, null, null, 0, init.httptimeout, headers, proxy, statusCodeOK, init.httpversion, cookieContainer, useDefaultHeaders: useDefaultHeaders, httpClient: httpClient);
        }
        #endregion


        #region Post
        public Task<T> Post<T>(string url, string data, List<HeadersModel> addheaders = null, List<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool IgnoreDeserializeObject = false, bool safety = false, bool textJson = false, CookieContainer cookieContainer = null)
        {
            var headers = JsonHeaders(addheaders, newheaders);

            return IsRchEnable(safety)
                ? rch.Post<T>(init.cors(url, headers, requestInfo), data, headers, IgnoreDeserializeObject, useDefaultHeaders, textJson)
                : Http.Post<T>(init.cors(url, headers, requestInfo), data, null, init.httptimeout, headers, encoding, proxy, IgnoreDeserializeObject, cookieContainer, useDefaultHeaders, init.httpversion, 0, statusCodeOK, httpClient, textJson);
        }

        public Task<string> Post(string url, string data, List<HeadersModel> addheaders = null, List<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool safety = false, CookieContainer cookieContainer = null)
        {
            var headers = JsonHeaders(addheaders, newheaders);

            return IsRchEnable(safety)
                ? rch.Post(init.cors(url, headers, requestInfo), data, headers, useDefaultHeaders)
                : Http.Post(init.cors(url, headers, requestInfo), data, null, 0, init.httptimeout, headers, proxy, init.httpversion, cookieContainer, useDefaultHeaders, false, encoding, statusCodeOK, httpClient);
        }
        #endregion

        #region PostSpan
        public Task PostSpan(string url, string data, Action<ReadOnlySpan<char>> spanAction, List<HeadersModel> addheaders = null, List<HeadersModel> newheaders = null, bool useDefaultHeaders = true, bool statusCodeOK = true, Encoding encoding = default, bool safety = false, CookieContainer cookieContainer = null)
        {
            var headers = JsonHeaders(addheaders, newheaders);

            return IsRchEnable(safety)
                ? rch.PostSpan(init.cors(url, headers, requestInfo), spanAction, data, headers, useDefaultHeaders)
                : Http.PostSpan(init.cors(url, headers, requestInfo), spanAction, data, null, init.httptimeout, headers, encoding, proxy, cookieContainer, useDefaultHeaders, init.httpversion, 0, statusCodeOK, httpClient);
        }
        #endregion


        #region RegisterHttp
        public void RegisterHttp(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }
        #endregion


        #region JsonHeaders / IsRchEnable
        List<HeadersModel> JsonHeaders(List<HeadersModel> addheaders = null, List<HeadersModel> newheaders = null)
        {
            if ((addheaders == null || addheaders.Count == 0) &&
                (newheaders == null || newheaders.Count == 0) &&
                (baseHeaders == null || baseHeaders.Count == 0))
                return null;

            int capacity = capacityHeaders(addheaders, newheaders);
            var headers = new List<HeadersModel>(capacity);

            if (baseHeaders != null && baseHeaders.Count > 0)
                headers.AddRange(baseHeaders);

            if (newheaders != null && newheaders.Count > 0)
                headers.AddRange(newheaders);

            if (addheaders != null && addheaders.Count > 0)
                headers.AddRange(addheaders);

            return headers;
        }

        int capacityHeaders(List<HeadersModel> addheaders, List<HeadersModel> newheaders)
        {
            int capacity = 0;
            if (baseHeaders != null && baseHeaders.Count > 0)
                capacity += baseHeaders.Count;

            if (newheaders != null && newheaders.Count > 0)
                capacity += newheaders.Count;

            if (addheaders != null && addheaders.Count > 0)
                capacity += addheaders.Count;

            return capacity;
        }

        bool IsRchEnable(bool safety)
        {
            bool rch_enable = rch != null && rch.enable;
            if (rch_enable)
            {
                if (safety && init.rhub_safety)
                    rch_enable = false;
            }

            return rch_enable;
        }
        #endregion
    }
}
