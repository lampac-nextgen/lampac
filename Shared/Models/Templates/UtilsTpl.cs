using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Shared.Models.Templates;

public static class UtilsTpl
{
    #region HtmlEncode
    public static void HtmlEncode(ReadOnlySpan<char> value, StringBuilder sb)
    {
        foreach (var c in value)
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
    #endregion

    #region WriteJson
    static readonly JsonWriterOptions _jsonWriterOptions = new JsonWriterOptions
    {
        Indented = false,
        SkipValidation = true
    };

    public static void WriteJson<T>(StringBuilder sb, BufferWriterPool<byte> utf8Buf, T value, JsonTypeInfo<T> options)
    {
        utf8Buf.SetLength(0);

        using (var writer = new Utf8JsonWriter(utf8Buf, _jsonWriterOptions))
            JsonSerializer.Serialize(writer, value, options);

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
