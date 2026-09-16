using Jint;
using System.Text.Json;

public static class Extensions
{
    public static Dictionary<string, string> ToDictionary(this IEnumerable<HeadersModel> headers)
    {
        if (headers == null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
            result.TryAdd(h.name, h.val);

        return result;
    }

    /// <summary>
    /// Кестрел допускает в значениях заголовков только ASCII 32..126,
    /// всё остальное (кириллица в uri, управляющие символы) percent-кодируем.
    /// </summary>
    public static string ToHeaderValue(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        bool safe = true;
        foreach (char c in value)
        {
            if (c < 32 || c > 126)
            {
                safe = false;
                break;
            }
        }

        if (safe)
            return value;

        var sb = new System.Text.StringBuilder(value.Length + 16);
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            if (b < 32 || b > 126)
                sb.Append('%').Append(b.ToString("X2"));
            else
                sb.Append((char)b);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Заголовки исходящего запроса одной строкой для debug-заголовка PX-ReqHeaders.
    /// </summary>
    public static string ToDebugHeaderValue(this System.Net.Http.HttpRequestMessage request)
    {
        if (request == null)
            return null;

        var sb = new System.Text.StringBuilder();

        void append(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            foreach (var h in headers)
            {
                if (sb.Length > 0)
                    sb.Append(" | ");

                sb.Append(h.Key).Append(": ").Append(string.Join(", ", h.Value));
            }
        }

        append(request.Headers);

        if (request.Content?.Headers != null)
            append(request.Content.Headers);

        return sb.ToString().ToHeaderValue();
    }

    public static string ToLowerAndTrim(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        ReadOnlySpan<char> span = input.AsSpan().Trim();

        if (span.Length < 256)
        {
            Span<char> buffer = stackalloc char[span.Length];

            for (int i = 0; i < span.Length; i++)
                buffer[i] = char.ToLowerInvariant(span[i]);

            if (buffer.SequenceEqual(input))
                return input;

            return new string(buffer);
        }
        else
        {
            return string.Create(span.Length, span, (dest, src) =>
            {
                for (int i = 0; i < src.Length; i++)
                {
                    dest[i] = char.ToLowerInvariant(src[i]);
                }
            });
        }
    }

    public static T Invoke<T>(this Engine engine, string propertyName, params object[] arguments) where T : class
    {
        var result = engine.Invoke(propertyName, arguments);
        if (result == null || result.IsNull() || result.IsUndefined())
            return default;

        if (typeof(T) == typeof(string))
            return (T)(object)result.AsString();

        return JsonSerializer.Deserialize<T>(result.AsString());
    }

    async public static Task<T> InvokeAsync<T>(this Engine engine, string propertyName, params object[] arguments) where T : class
    {
        var result = await engine.InvokeAsync(propertyName, arguments);
        if (result == null || result.IsNull() || result.IsUndefined())
            return default;

        if (typeof(T) == typeof(string))
            return (T)(object)result.AsString();

        return JsonSerializer.Deserialize<T>(result.AsString());
    }
}
