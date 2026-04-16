using System.Net;
using System.Text.Json.Serialization;

namespace Shared.Models.Proxy
{
    public class ProxyLinkModel
    {
        public ProxyLinkModel()
        {
            ex = DateTime.Today.AddDays(2);
        }

        public ProxyLinkModel(string reqip, List<HeadersModel> headers, WebProxy proxy, string uri, string plugin = null, bool verifyip = true, DateTime ex = default, object userdata = null)
        {
            this.ex = ex;
            this.reqip = reqip;
            this.headers = headers;
            this.proxy = proxy;
            this.userdata = userdata;
            this.uri = uri;
            this.plugin = plugin;
            this.verifyip = verifyip;

            if (this.ex == default)
                this.ex = DateTime.Today.AddDays(2);
        }

        [JsonIgnore]
        public DateTime ex { get; set; }

        public string reqip { get; set; }

        public List<HeadersModel> headers { get; set; }

        [JsonIgnore]
        public WebProxy proxy { get; set; }

        [JsonIgnore]
        public object userdata { get; set; }

        public string uri { get; set; }

        public string plugin { get; set; }

        public bool verifyip { get; set; }

        public bool md5 { get; set; }
    }
}
