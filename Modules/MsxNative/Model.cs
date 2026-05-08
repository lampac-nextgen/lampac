using Newtonsoft.Json;

namespace MsxNative;

public class MsxItem
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string title { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string icon { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string iconSize { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string image { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string action { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string label { get; set; }
}
