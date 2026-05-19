using Microsoft.AspNetCore.Http;
using Shared.Models.Events;
using Shared.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models.Templates;

public static class VideoTpl
{
    public static string ToJson(string method, string url, string title, StreamQualityTpl streamquality = null, SubtitleTpl subtitles = null, string quality = null, VastConf vast = null, IReadOnlyList<HeadersModel> headers = null, int? hls_manifest_timeout = null, SegmentTpl segments = null, string subtitles_call = null, HttpContext httpContext = null)
    {
        var _vast = vast ?? CoreInit.conf.vast;

        var md = new VideoDto(
            title,
            method,
            url,
            Http.NormalizeHeaders(headers),
            streamquality?.ToObject(emptyToNull: true)
                ?? new Dictionary<string, string>(
                    [new KeyValuePair<string, string>(quality ?? "auto", url)]
                ),
            subtitles?.ToObject(emptyToNull: true),
            subtitles_call,
            hls_manifest_timeout,
            _vast?.url != null ? _vast : null,
            segments?.ToObject()
        );

        if (EventListener.VideoTpl != null)
        {
            var em = new EventVideoTpl(md, httpContext);
            foreach (Func<EventVideoTpl, string> handler in EventListener.VideoTpl.GetInvocationList())
            {
                var eventResult = handler(em);
                if (eventResult != null)
                    return eventResult;
            }
        }

        return JsonSerializer.Serialize(md, VideoJsonContext.Default.VideoDto);
    }
}


[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
)]
[JsonSerializable(typeof(VideoDto))]
public partial class VideoJsonContext : JsonSerializerContext
{
}

public record VideoDto
{
    public string title { get; }
    public string method { get; }
    public string url { get; }
    public Dictionary<string, string> headers { get; }
    public Dictionary<string, string> quality { get; }
    public IReadOnlyList<SubtitleDto> subtitles { get; }
    public string subtitles_call { get; }
    public int? hls_manifest_timeout { get; }
    public VastConf vast { get; }
    public Dictionary<string, IReadOnlyList<SegmentDto>> segments { get; }

    [JsonConstructor]
    public VideoDto(
        string title,
        string method,
        string url,
        Dictionary<string, string> headers,
        Dictionary<string, string> quality,
        IReadOnlyList<SubtitleDto> subtitles,
        string subtitles_call,
        int? hls_manifest_timeout,
        VastConf vast,
        Dictionary<string, IReadOnlyList<SegmentDto>> segments)
    {
        this.method = method;
        this.url = url;
        this.headers = headers;
        this.quality = quality;
        this.subtitles = subtitles;
        this.subtitles_call = subtitles_call;
        this.title = title;
        this.hls_manifest_timeout = hls_manifest_timeout;
        this.vast = vast;
        this.segments = segments;
    }
}
