using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;

namespace PornHub;

public class ModuleConf
{
    public SisiSettings PornHub { get; set; } = new SisiSettings("PornHub", "https://rt.pornhub.com", streamproxy: true, rch_access: "apk,cors", stream_access: "apk")
    {
        httpversion = 2,
        displayindex = 11,
        streamproxy_preview = true,
        rchstreamproxy = "web,cors",
        headers = HeadersModel.Init(
            Http.defaultFullHeaders,
            ("cookie", "platform=pc; accessAgeDisclaimerPH=1"),
            ("sec-fetch-dest", "document"),
            ("sec-fetch-site", "same-origin"),
            ("sec-fetch-mode", "navigate")
        ).ToDictionary(),
        headers_image = HeadersModel.Init(
            ("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5"),
            ("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/534.57.2 (KHTML, like Gecko) Version/5.1.7 Safari/534.57.2"),
            ("Cache-Control", "max-age=0"),
            ("referer", "https://rt.pornhub.com/"),
            ("sec-fetch-dest", "document"),
            ("sec-fetch-mode", "navigate"),
            ("sec-fetch-site", "cross-site"),
            ("sec-fetch-user", "?1")
        ).ToDictionary(),
        headers_stream = HeadersModel.Init(
            ("Accept", "*/*"),
            ("origin", "https://rt.pornhub.com"),
            ("referer", "https://rt.pornhub.com/"),
            ("sec-fetch-dest", "empty"),
            ("sec-fetch-mode", "cors"),
            ("sec-fetch-site", "cross-site")
        ).ToDictionary()
    };

    public SisiSettings PornHubPremium { get; set; } = new SisiSettings("PornHubPremium", "https://rt.pornhubpremium.com", enable: false, stream_access: "apk,cors")
    {
        httpversion = 2,
        displayindex = 10,
        rchstreamproxy = "web",
        headers = HeadersModel.Init(
            ("sec-fetch-dest", "document"),
            ("sec-fetch-site", "none"),
            ("sec-fetch-user", "?1"),
            ("upgrade-insecure-requests", "1")
        ).ToDictionary(),
        headers_image = HeadersModel.Init(
            ("Accept", "image/jpeg,image/png,image/*;q=0.8,*/*;q=0.5"),
            ("User-Agent", "Mozilla/5.0 (Windows NT 6.2; WOW64) AppleWebKit/534.57.2 (KHTML, like Gecko) Version/5.1.7 Safari/534.57.2"),
            ("Cache-Control", "max-age=0")
        ).ToDictionary()
    };

}
