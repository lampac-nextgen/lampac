using Newtonsoft.Json;
using System.Web;

namespace Shared.Models.Base;

public class BaseSettings : Iproxy, Istreamproxy, Icors, Igroup, ICloneable
{
    bool _enable;

    public bool enable
    {
        get
        {
            if (CoreInit.conf.defaultOn == "enabled")
                return enabled;

            return _enable;
        }
        set
        {
            _enable = value;
        }
    }

    public bool enabled { get; set; }

    public bool spider { get; set; } = true;


    public bool kit { get; set; } = true;

    public bool IsKitConf { get; set; }

    public bool IsCloneable { get; set; }

    public string plugin { get; set; }

    public int group { get; set; }

    public bool group_hide { get; set; } = true;

    public bool rhub { get; set; }

    public bool rhub_safety { get; set; } = true;

    public bool rhub_streamproxy { get; set; }

    public bool rhub_fallback { get; set; }

    public string[] rhub_geo_disable { get; set; }

    public string[] geo_hide { get; set; }

    /// <summary>
    /// Список устройств которым выводить источник не зависимво от rhub
    /// </summary>
    public string client_type { get; set; }

    /// <summary>
    /// Список устройств которым выводить источник при включеном rhub
    /// </summary>
    public string rch_access { get; set; }

    public string RchAccessNotSupport(bool nocheck = false)
    {
        if (string.IsNullOrWhiteSpace(rch_access))
            return null;

        if (nocheck == false)
        {
            // rch выключен
            // разрешен fallback
            // указан webcorshost или включен corseu
            if (!rhub || rhub_fallback || !string.IsNullOrWhiteSpace(webcorshost) || corseu)
                return null;
        }

        int mask = 0;

        if (!rch_access.Contains("apk", StringComparison.Ordinal))
            mask |= 1;

        if (!rch_access.Contains("cors", StringComparison.Ordinal))
            mask |= 2;

        if (!rch_access.Contains("web", StringComparison.Ordinal))
            mask |= 4;

        return mask switch
        {
            0 => null,
            1 => "apk",
            2 => "cors",
            3 => "apk,cors",
            4 => "web",
            5 => "apk,web",
            6 => "cors,web",
            7 => "apk,cors,web",
            _ => null
        };
    }

    public bool rip { get; set; }

    public int cache_time { get; set; }

    public string displayname { get; set; }

    public int displayindex { get; set; }

    public string overridehost { get; set; }

    public string[] overridehosts { get; set; }

    public string overridepasswd { get; set; }

    public string host { get; set; }

    public string apihost { get; set; }

    public string scheme { get; set; }

    public bool hls { get; set; }

    public string login { get; set; }

    public string passwd { get; set; }

    public string cookie { get; set; }

    public string token { get; set; }

    #region headers
    [JsonIgnore]
    IReadOnlyDictionary<string, string> _headers;

    [JsonProperty("headers",
        ObjectCreationHandling = ObjectCreationHandling.Replace,   // ← заменить, а не дополнять
        NullValueHandling = NullValueHandling.Ignore               // ← не затирать null-ом
    )]
    public IReadOnlyDictionary<string, string> headers
    {
        get => _headers;
        set
        {
            if (value == null)
                return;

            _headers = value;
            headersList = HeadersModel.InitOrNull(_headers);
        }
    }

    [JsonIgnore]
    public IReadOnlyList<HeadersModel> headersList
    {
        get;
        private set;
    }
    #endregion

    #region headers_stream / headers_image
    [JsonProperty("headers_stream",
        ObjectCreationHandling = ObjectCreationHandling.Replace,   // ← заменить, а не дополнять
        NullValueHandling = NullValueHandling.Ignore               // ← не затирать null-ом
    )]
    public IReadOnlyDictionary<string, string> headers_stream { get; set; }

    [JsonProperty("headers_image",
        ObjectCreationHandling = ObjectCreationHandling.Replace,   // ← заменить, а не дополнять
        NullValueHandling = NullValueHandling.Ignore               // ← не затирать null-ом
    )]
    public IReadOnlyDictionary<string, string> headers_image { get; set; }
    #endregion

    public VastConf vast { get; set; }

    public string priorityBrowser { get; set; }

    public int httptimeout { get; set; } = 8;

    public int httpversion { get; set; } = 1;


    #region proxy
    public bool useproxy { get; set; }

    public string globalnameproxy { get; set; }

    public ProxySettings proxy { get; set; }

    public bool useproxystream { get; set; }

    public bool streamproxy { get; set; }

    public bool streamproxy_preview { get; set; }

    public bool apnstream { get; set; }

    public string[] geostreamproxy { get; set; }

    public string rchstreamproxy { get; set; }

    public ApnConf apn { get; set; }

    public bool qualitys_proxy { get; set; }

    public bool url_reserve { get; set; }

    public string stream_access { get; set; }

    public string StreamAccessNotSupport(bool nocheck = false)
    {
        if (string.IsNullOrWhiteSpace(stream_access))
            return null;

        if (nocheck == false)
        {
            if (CoreInit.conf.serverproxy.forced_apn && !string.IsNullOrWhiteSpace(CoreInit.conf?.apn?.host))
                return null;

            if (rhub && !rhub_streamproxy && !rhub_fallback && rhub_geo_disable == null) { }
            else
            {
                if (streamproxy || apnstream || qualitys_proxy || geostreamproxy != null || rchstreamproxy != null)
                    return null;
            }
        }

        int mask = 0;

        if (!stream_access.Contains("apk", StringComparison.Ordinal))
            mask |= 1;

        if (!stream_access.Contains("cors", StringComparison.Ordinal))
            mask |= 2;

        if (!stream_access.Contains("web", StringComparison.Ordinal))
            mask |= 4;

        return mask switch
        {
            0 => null,
            1 => "apk",
            2 => "cors",
            3 => "apk,cors",
            4 => "web",
            5 => "apk,web",
            6 => "cors,web",
            7 => "apk,cors,web",
            _ => null
        };
    }
    #endregion

    #region cors
    public bool corseu { get; set; }

    public string webcorshost { get; set; }

    public bool IsCorsRequest()
    {
        string crhost = !string.IsNullOrWhiteSpace(webcorshost) ? webcorshost : corseu ? CoreInit.conf.corsehost : null;
        if (string.IsNullOrWhiteSpace(crhost))
            return false;

        return true;
    }

    public string cors(string uri, IReadOnlyList<HeadersModel> headers = null, RequestModel requestInfo = null)
    {
        string crhost = !string.IsNullOrWhiteSpace(webcorshost) ? webcorshost : corseu ? CoreInit.conf.corsehost : null;
        if (string.IsNullOrWhiteSpace(crhost) || string.IsNullOrWhiteSpace(uri))
            return uri;

        crhost = crhost.Trim();

        if (crhost.Contains("{encode_uri}") || crhost.Contains("{uri}"))
            return crhost.Replace("{encode_uri}", HttpUtility.UrlEncode(uri)).Replace("{uri}", uri);

        if (crhost.Contains("{payload}"))
        {
            return crhost.Replace("{payload}", CrypTo.Base64(System.Text.Json.JsonSerializer.Serialize(new
            {
                u = uri,
                p = plugin,
                h = headers?.ToDictionary(),
                t = "cors",
                i = requestInfo.user_uid
            })));
        }

        return $"{crhost}/{uri}";
    }
    #endregion

    public string imgcorshost { get; set; }


    public string Decrypt(ReadOnlySpan<char> data)
        => BaseDecrypt(data);

    public static string BaseDecrypt(ReadOnlySpan<char> data)
    {
        if (data.IsEmpty)
            return null;

        return string.Create(data.Length, data, static (span, source) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = (char)(source[i] - 3);
            }
        });
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}
