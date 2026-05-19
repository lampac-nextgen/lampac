using Shared.Services;
using System.Text;
using System.Text.Json.Serialization;

namespace Shared.Models.Templates;

public class EpisodeTpl : ITplResult
{
    public List<EpisodeDto> data { get; set; }

    public VoiceTpl vtpl { get; set; }


    public EpisodeTpl() : this(null, 20) { }

    public EpisodeTpl(int capacity) : this(null, capacity) { }

    public EpisodeTpl(VoiceTpl vtpl) : this(vtpl, 20) { }

    public EpisodeTpl(VoiceTpl vtpl, int capacity)
    {
        this.vtpl = vtpl;
        data = new List<EpisodeDto>(capacity);
    }


    public void Append(string name, string title, string s, string e, string link, string method = "play", StreamQualityTpl streamquality = null, SubtitleTpl subtitles = null, string streamlink = null, string voice_name = null, VastConf vast = null, IReadOnlyList<HeadersModel> headers = null, int? hls_manifest_timeout = null, SegmentTpl segments = null, string subtitles_call = null)
    {
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(link))
        {
            data.Add(new EpisodeDto(
                method,
                link,
                streamlink,
                Http.NormalizeHeaders(headers),
                streamquality?.ToObject(emptyToNull: true),
                subtitles?.ToObject(emptyToNull: true),
                subtitles_call,
                short.TryParse(s, out short _s) ? _s : (short)0,
                short.TryParse(e, out short _e) ? _e : (short)0,
                voice_name,
                name,
                $"{title} ({e} серия)",
                hls_manifest_timeout,
                vast,
                segments?.ToObject()
            ));
        }
    }

    public void Append(VoiceTpl vtpl)
    {
        this.vtpl = vtpl;
    }


    public bool IsEmpty
        => data == null || data.Count == 0;

    public int Length
        => data?.Count ?? 0;

    public object ToObject()
        => this;


    public string ToHtml()
    {
        if (IsEmpty)
            return string.Empty;

        var sb = ToBuilderHtml();

        try
        {
            return sb.ToString();
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }

    public StringBuilder ToBuilderHtml()
    {
        if (IsEmpty)
            return StringBuilderPool.EmptyHtml;

        var html = StringBuilderPool.Rent();

        bool firstjson = true;

        if (vtpl != null)
            vtpl.WriteTo(html);

        html.Append("<div class=\"videos__line\">");

        using (var utf8Buf = new BufferWriterPool<byte>())
        {
            foreach (EpisodeDto i in data)
            {
                var vast = i.vast ?? CoreInit.conf.vast;
                if (vast?.url != null)
                    i.vast = vast;

                html.Append("<div class=\"videos__item videos__movie selector ");
                if (firstjson)
                    html.Append("focused");
                html.Append("\" ");

                html.Append("media=\"\" s=\"");
                html.Append(i.s);
                html.Append("\" e=\"");
                html.Append(i.e);
                html.Append("\" ");

                html.Append("data-json='");
                UtilsTpl.WriteJson(html, utf8Buf, i, EpisodeJsonContext.Default.EpisodeDto);
                html.Append("'>");

                html.Append("<div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">");
                UtilsTpl.HtmlEncode(i.name, html);
                html.Append("</div></div>");

                firstjson = false;
            }
        }

        html.Append("</div>");

        return html;
    }

    public string ToJson(in VoiceTpl vtpl)
    {
        this.vtpl = vtpl;
        return ToJson();
    }

    public string ToJson()
    {
        if (IsEmpty)
            return string.Empty;

        var sb = ToBuilderJson();

        try
        {
            return sb.ToString();
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }

    public StringBuilder ToBuilderJson()
    {
        if (IsEmpty)
            return StringBuilderPool.EmptyJsonObject;

        var json = StringBuilderPool.Rent();

        using (var utf8Buf = new BufferWriterPool<byte>(largePool: true))
        {
            UtilsTpl.WriteJson(json, utf8Buf, new EpisodeResponseDto(
                vtpl?.ToObject(emptyToNull: true),
                data
            ), EpisodeJsonContext.Default.EpisodeResponseDto);
        }

        return json;
    }
}


[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
)]
[JsonSerializable(typeof(EpisodeDto))]
[JsonSerializable(typeof(EpisodeResponseDto))]
public partial class EpisodeJsonContext : JsonSerializerContext
{
}

public record EpisodeDto
{
    public string method { get; }
    public string url { get; }
    public string stream { get; }
    public Dictionary<string, string> headers { get; }
    public Dictionary<string, string> quality { get; }
    public IReadOnlyList<SubtitleDto> subtitles { get; }
    public string subtitles_call { get; }
    public short? s { get; }
    public short? e { get; }
    public string details { get; }
    public string name { get; }
    public string title { get; }
    public int? hls_manifest_timeout { get; }
    public VastConf vast { get; set; }
    public Dictionary<string, IReadOnlyList<SegmentDto>> segments { get; }

    [JsonConstructor]
    public EpisodeDto(
        string method,
        string url,
        string stream,
        Dictionary<string, string> headers,
        Dictionary<string, string> quality,
        IReadOnlyList<SubtitleDto> subtitles,
        string subtitles_call,
        short? s,
        short? e,
        string details,
        string name,
        string title,
        int? hls_manifest_timeout,
        VastConf vast,
        Dictionary<string, IReadOnlyList<SegmentDto>> segments)
    {
        this.method = method;
        this.url = url;
        this.stream = stream;
        this.headers = headers;
        this.quality = quality;
        this.subtitles = subtitles;
        this.subtitles_call = subtitles_call;
        this.s = s;
        this.e = e;
        this.details = details;
        this.name = name;
        this.title = title;
        this.hls_manifest_timeout = hls_manifest_timeout;
        this.vast = vast;
        this.segments = segments;
    }
}

public record EpisodeResponseDto
{
    public string type { get; }
    public IReadOnlyList<VoiceDto> voice { get; }
    public IReadOnlyList<EpisodeDto> data { get; }

    [JsonConstructor]
    public EpisodeResponseDto(IReadOnlyList<VoiceDto> voice, IReadOnlyList<EpisodeDto> data)
    {
        type = "episode";
        this.voice = voice;
        this.data = data;
    }
}
