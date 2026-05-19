using Shared.Services;
using System.Text;
using System.Text.Json.Serialization;

namespace Shared.Models.Templates;

public record MovieTplItem(string voiceOrQuality, string link, string method, string stream, StreamQualityTpl streamquality, SubtitleTpl subtitles, string voice_name, string year, string details, string quality, VastConf vast, IReadOnlyList<HeadersModel> headers, int? hls_manifest_timeout, SegmentTpl segments, string subtitles_call);

public class MovieTpl : ITplResult
{
    string title, original_title;

    public VoiceTpl vtpl { get; set; }

    public List<MovieTplItem> data { get; set; }


    public MovieTpl(string title) : this(title, null, 15) { }

    public MovieTpl(string title, string original_title) : this(title, original_title, 15) { }

    public MovieTpl(string title, string original_title, int capacity) : this(title, original_title, null, capacity) { }

    public MovieTpl(string title, string original_title, VoiceTpl vtpl) : this(title, original_title, vtpl, 15) { }

    public MovieTpl(string title, string original_title, VoiceTpl vtpl, int capacity)
    {
        this.vtpl = vtpl;
        this.title = title;
        this.original_title = original_title;
        data = new List<MovieTplItem>(capacity);
    }


    public void Append(string voiceOrQuality, string link, string method = "play", string stream = null, StreamQualityTpl streamquality = null, SubtitleTpl subtitles = null, string voice_name = null, string year = null, string details = null, string quality = null, VastConf vast = null, IReadOnlyList<HeadersModel> headers = null, int? hls_manifest_timeout = null, SegmentTpl segments = null, string subtitles_call = null)
    {
        if (!string.IsNullOrEmpty(voiceOrQuality) && !string.IsNullOrEmpty(link))
            data.Add(new MovieTplItem(voiceOrQuality, link, method, stream, streamquality, subtitles, voice_name, year, details, quality, vast, headers, hls_manifest_timeout, segments, subtitles_call));
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


    public void Reverse()
    {
        data.Reverse();
    }

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
            foreach (var i in data)
            {
                var vast = i.vast ?? CoreInit.conf.vast;

                html.Append("<div class=\"videos__item videos__movie selector ");
                if (firstjson)
                    html.Append("focused");
                html.Append("\" ");

                html.Append("media=\"\" ");

                html.Append("data-json='");
                UtilsTpl.WriteJson(html, utf8Buf, new MovieDto
                (
                    i.method,
                    i.link,
                    i.stream,
                    Http.NormalizeHeaders(i.headers),
                    i.streamquality?.ToObject(emptyToNull: true),
                    i.subtitles?.ToObject(emptyToNull: true),
                    i.subtitles_call,
                    i.voiceOrQuality,
                    i.streamquality?.MaxQuality() ?? i.quality,
                    i.voice_name,
                    i.details,
                    int.TryParse(i.year, out int _year) ? _year : 0,
                    $"{title ?? original_title} ({i.voiceOrQuality})",
                    i.hls_manifest_timeout,
                    vast?.url != null ? vast : null,
                    i.segments?.ToObject()
                ), MovieJsonContext.Default.MovieDto);
                html.Append("'>");

                html.Append("<div class=\"videos__item-imgbox videos__movie-imgbox\"></div><div class=\"videos__item-title\">");
                UtilsTpl.HtmlEncode(i.voiceOrQuality, html);
                html.Append("</div></div>");

                if (!string.IsNullOrEmpty(i.quality))
                {
                    if (i.quality.EndsWith("p"))
                        html.Append($"<!--{i.quality}-->");
                    else
                        html.Append($"<!--{i.quality}p-->");
                }

                firstjson = false;
            }
        }

        html.Append("</div>");

        return html;
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

        var arr = new MovieDto[data.Count];

        for (int idx = 0; idx < data.Count; idx++)
        {
            var i = data[idx];
            var vast = i.vast ?? CoreInit.conf.vast;

            arr[idx] = new MovieDto(
                i.method,
                i.link,
                i.stream,
                Http.NormalizeHeaders(i.headers),
                i.streamquality?.ToObject(emptyToNull: true),
                i.subtitles?.ToObject(emptyToNull: true),
                i.subtitles_call,
                i.voiceOrQuality,
                i.streamquality?.MaxQuality() ?? i.quality,
                (i.voice_name == null && i.details == null) ? null : (i.voice_name + i.details),
                null,
                int.TryParse(i.year, out int _year) ? _year : 0,
                $"{title ?? original_title} ({i.voiceOrQuality})",
                i.hls_manifest_timeout,
                vast?.url != null ? vast : null,
                i.segments?.ToObject()
            );
        }

        using (var utf8Buf = new BufferWriterPool<byte>(largePool: true))
        {
            UtilsTpl.WriteJson(json, utf8Buf, new MovieResponseDto
            (
                vtpl?.ToObject(emptyToNull: true),
                arr
            ), MovieJsonContext.Default.MovieResponseDto);
        }

        return json;
    }
}


[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
)]
[JsonSerializable(typeof(MovieDto))]
[JsonSerializable(typeof(MovieResponseDto))]
public partial class MovieJsonContext : JsonSerializerContext
{
}

public record MovieDto
{
    public string method { get; }
    public string url { get; }
    public string stream { get; }
    public Dictionary<string, string> headers { get; }
    public Dictionary<string, string> quality { get; }
    public IReadOnlyList<SubtitleDto> subtitles { get; }
    public string subtitles_call { get; }
    public string translate { get; }
    public string maxquality { get; }
    public string voice_name { get; }
    public string details { get; }
    public int year { get; }
    public string title { get; }
    public int? hls_manifest_timeout { get; }
    public VastConf vast { get; }
    public Dictionary<string, IReadOnlyList<SegmentDto>> segments { get; }

    [JsonConstructor]
    public MovieDto(
        string method,
        string url,
        string stream,
        Dictionary<string, string> headers,
        Dictionary<string, string> quality,
        IReadOnlyList<SubtitleDto> subtitles,
        string subtitles_call,
        string translate,
        string maxquality,
        string voice_name,
        string details,
        int year,
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
        this.translate = translate;
        this.maxquality = maxquality;
        this.voice_name = voice_name;
        this.details = details;
        this.year = year;
        this.title = title;
        this.hls_manifest_timeout = hls_manifest_timeout;
        this.vast = vast;
        this.segments = segments;
    }
}

public record MovieResponseDto
{
    public string type { get; }
    public IReadOnlyList<VoiceDto> voice { get; }
    public MovieDto[] data { get; }

    [JsonConstructor]
    public MovieResponseDto(IReadOnlyList<VoiceDto> voice, MovieDto[] data)
    {
        type = "movie";
        this.voice = voice;
        this.data = data;
    }
}
