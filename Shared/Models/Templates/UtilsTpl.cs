using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shared.Models.Templates;

public static class UtilsTpl
{
    #region HtmlEncode
    public static void HtmlEncode(ReadOnlySpan<char> value, StringBuilder sb)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    // Drop comment terminators so <!--quality--> cannot break out into markup.
    public static string HtmlComment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("--", "", StringComparison.Ordinal).Replace(">", "", StringComparison.Ordinal);
    }
    #endregion

    #region WriteJson
    public static readonly JsonWriterOptions jsonWriterOptions = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = true
    };

    public static void WriteJson<T>(StringBuilder sb, BufferWriterPool<byte> utf8Buf, T value, JsonTypeInfo<T> options)
    {
        using (var writer = new Utf8JsonWriter(utf8Buf, jsonWriterOptions))
            WriteJson(sb, utf8Buf, writer, value, options);
    }

    public static void WriteJson<T>(StringBuilder sb, BufferWriterPool<byte> utf8Buf, Utf8JsonWriter jsonWriter, T value, JsonTypeInfo<T> options)
    {
        utf8Buf.SetLength(0);
        jsonWriter.Reset(utf8Buf);

        JsonSerializer.Serialize(jsonWriter, value, options);
        jsonWriter.Flush();

        ReadOnlySpan<byte> utf8 = utf8Buf.WrittenSpan;
        if (utf8.IsEmpty)
            return;

        // UTF-8: 2 byte -> 1 char
        // структурные символы ({},": ), цифры, латиница и escape-последовательности — это ASCII, то есть 1 байт на 1 char
        // utf8.Length всегда выше или равен charCount
        int charCount = utf8.Length;

        // если json большой, то считаем точное количество символов
        if (utf8.Length > (CoreInit.conf.lowMemoryMode ? BufferCharPool.sizeMedium : BufferCharPool.sizeLarge))
            charCount = Encoding.UTF8.GetCharCount(utf8);

        using (var charBuf = new BufferCharPool(charCount))
        {
            if (Encoding.UTF8.TryGetChars(utf8, charBuf.Span, out int charsWritten) && charsWritten > 0)
                sb.Append(charBuf.Span.Slice(0, charsWritten));
        }
    }
    #endregion
}
