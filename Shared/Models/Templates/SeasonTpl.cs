using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models.Templates;

public class SeasonTpl : ITplResult
{
    private readonly int _capacity;

    public List<SeasonDto> data { get; set; }

    public string quality { get; set; }

    public VoiceTpl vtpl { get; set; }


    public SeasonTpl() : this(null, null, 10) { }

    public SeasonTpl(int capacity) : this(null, null, capacity) { }

    public SeasonTpl(string quality) : this(null, quality, 10) { }

    public SeasonTpl(string quality, int capacity) : this(null, quality, capacity) { }

    public SeasonTpl(VoiceTpl vtpl) : this(vtpl, null, 10) { }

    public SeasonTpl(VoiceTpl vtpl, int capacity) : this(vtpl, null, capacity) { }

    public SeasonTpl(VoiceTpl vtpl, string quality, int capacity)
    {
        _capacity = capacity;
        this.vtpl = vtpl;
        this.quality = quality;
    }


    public void Append(string name, string link, string id)
    {
        int s = -1;

        if (string.IsNullOrEmpty(id))
            s = 0;
        else
        {
            s = id switch
            {
                "1" => 1,
                "2" => 2,
                "3" => 3,
                "4" => 4,
                "5" => 5,
                "6" => 6,
                "7" => 7,
                "8" => 8,
                "9" => 9,
                "10" => 10,
                _ => -1
            };

            if (s == -1)
                int.TryParse(id, out s);
        }

        Append(name, link, s);
    }

    public void Append(string name, string link, int id)
    {
        if (!string.IsNullOrEmpty(name))
        {
            data ??= new List<SeasonDto>(_capacity);
            data.Add(new SeasonDto(link, name, id));
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

    public string Type
       => "season";

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

        string q = UtilsTpl.HtmlComment(quality);
        if (q.Length > 0)
        {
            html.Append("<!--q:");
            html.Append(q);
            html.Append("-->");
        }

        using (var utf8Buf = new BufferWriterPool<byte>(BufferWriterPoolType.Small))
        {
            using (var jsonWriter = new Utf8JsonWriter(utf8Buf, UtilsTpl.jsonWriterOptions))
            {
                foreach (var i in data)
                {
                    html.Append("<div class=\"videos__item videos__season selector ");
                    if (firstjson)
                        html.Append("focused");
                    html.Append("\" ");

                    html.Append("data-json='");
                    UtilsTpl.WriteJson(html, utf8Buf, jsonWriter, i, SeasonJsonContext.Default.SeasonDto);
                    html.Append("'>");

                    html.Append("<div class=\"videos__season-layers\"></div><div class=\"videos__item-imgbox videos__season-imgbox\"><div class=\"videos__item-title videos__season-title\">");
                    UtilsTpl.HtmlEncode(i.name, html);
                    html.Append("</div></div></div>");

                    firstjson = false;
                }
            }
        }

        html.Append("</div>");

        return html;
    }

    public string ToJson()
    {
        if (IsEmpty)
            return "{}";

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

        using (var utf8Buf = new BufferWriterPool<byte>(BufferWriterPoolType.Large))
        {
            UtilsTpl.WriteJson(json, utf8Buf, new SeasonResponseDto
            (
                quality,
                vtpl?.ToObject(emptyToNull: true),
                data
            ), SeasonJsonContext.Default.SeasonResponseDto);
        }

        return json;
    }
}


[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
)]
[JsonSerializable(typeof(SeasonDto))]
[JsonSerializable(typeof(SeasonResponseDto))]
public partial class SeasonJsonContext : JsonSerializerContext
{
}

public class SeasonDto
{
    public string method => "link";
    public int id { get; }
    public string url { get; }
    public string name { get; }

    [JsonConstructor]
    [Newtonsoft.Json.JsonConstructor]
    public SeasonDto(string url, string name, int id)
    {
        this.id = id;
        this.url = url;
        this.name = name;
    }
}

public class SeasonResponseDto
{
    public string type => "season";
    public string maxquality { get; }
    public IReadOnlyList<VoiceDto> voice { get; }
    public IReadOnlyList<SeasonDto> data { get; }

    [JsonConstructor]
    [Newtonsoft.Json.JsonConstructor]
    public SeasonResponseDto(string maxquality, IReadOnlyList<VoiceDto> voice, IReadOnlyList<SeasonDto> data)
    {
        this.maxquality = maxquality;
        this.voice = voice;
        this.data = data;
    }
}
