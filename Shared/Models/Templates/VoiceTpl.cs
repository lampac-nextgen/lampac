using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models.Templates;

public class VoiceTpl
{
    private readonly int _capacity;

    public List<VoiceDto> data { get; set; }

    public VoiceTpl(int capacity = 20)
    {
        _capacity = capacity;
    }

    public bool IsEmpty
        => data == null || data.Count == 0;

    public void Append(string name, bool active, string link)
    {
        if (!string.IsNullOrEmpty(name))
        {
            data ??= new List<VoiceDto>(_capacity);
            data.Add(new VoiceDto(link, active, name));
        }
    }

    public string ToHtml()
    {
        if (IsEmpty)
            return string.Empty;

        var sb = StringBuilderPool.Rent();

        try
        {
            WriteTo(sb);
            return sb.ToString();
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }

    public void WriteTo(StringBuilder sb)
    {
        if (IsEmpty)
            return;

        sb.Append("<div class=\"videos__line\">");

        using (var utf8Buf = new BufferWriterPool<byte>(BufferWriterPoolType.Small))
        {
            using (var jsonWriter = new Utf8JsonWriter(utf8Buf, UtilsTpl.jsonWriterOptions))
            {
                foreach (var i in data)
                {
                    sb.Append("<div class=\"videos__button selector ");
                    if (i.active)
                        sb.Append("active");

                    sb.Append("\" data-json='");
                    UtilsTpl.WriteJson(sb, utf8Buf, jsonWriter, i, VoiceJsonContext.Default.VoiceDto);
                    sb.Append("'>");

                    UtilsTpl.HtmlEncode(i.name, sb);

                    sb.Append("</div>");
                }
            }
        }

        sb.Append("</div>");
    }

    public IReadOnlyList<VoiceDto> ToObject(bool emptyToNull = false)
    {
        if (IsEmpty)
            return emptyToNull ? null : Array.Empty<VoiceDto>();

        return data;
    }
}


[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
)]
[JsonSerializable(typeof(VoiceDto))]
public partial class VoiceJsonContext : JsonSerializerContext
{
}

public class VoiceDto
{
    public string method => "link";
    public string url { get; }
    public bool active { get; }
    public string name { get; }

    [JsonConstructor]
    [Newtonsoft.Json.JsonConstructor]
    public VoiceDto(string url, bool active, string name)
    {
        this.url = url;
        this.active = active;
        this.name = name;
    }
}
