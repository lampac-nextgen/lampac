using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Aladin;

/// <summary>
/// HLS URI rewriting for the native stream endpoint.
/// </summary>
internal static class AladinHls
{
    static readonly Regex UriAttribute = new(@"URI=(""([^""]+)""|'([^']+)')", RegexOptions.Compiled);
    static readonly Regex Resolution = new(@"RESOLUTION=\d+x(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex HighLabel = new(@"(?:NAME|VIDEO)=""?(?:1440p?|2160p?|2K|4K|UHD)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex UltraLabel = new(@"(?:NAME|VIDEO)=""?(?:2160p?|4K|UHD)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Rewrite(string text, Func<string, string> wrap, int maxHeight = 2160)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var output = new StringBuilder(text.Length + 256);
        bool skipVariantUri = false;

        foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            string stripped = line.Trim();

            if (stripped.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal) ||
                stripped.StartsWith("#EXT-X-I-FRAME-STREAM-INF", StringComparison.Ordinal))
            {
                var height = Resolution.Match(stripped);
                bool shouldSkip = height.Success && int.Parse(height.Groups[1].Value) > maxHeight;
                shouldSkip |= maxHeight <= 1080 && HighLabel.IsMatch(stripped);
                shouldSkip |= maxHeight <= 1440 && UltraLabel.IsMatch(stripped);

                if (shouldSkip)
                {
                    skipVariantUri = stripped.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal);
                    continue;
                }
            }

            if (skipVariantUri)
            {
                if (stripped.Length == 0 || stripped.StartsWith("#", StringComparison.Ordinal))
                    continue;

                skipVariantUri = false;
                continue;
            }

            if (stripped.Length > 0 && !stripped.StartsWith("#", StringComparison.Ordinal))
            {
                output.Append(wrap(stripped)).Append('\n');
            }
            else if (stripped.StartsWith("#", StringComparison.Ordinal) && line.Contains("URI=", StringComparison.Ordinal))
            {
                output.Append(UriAttribute.Replace(line, match =>
                {
                    string uri = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                    char quote = match.Groups[1].Value[0];
                    return $"URI={quote}{wrap(uri)}{quote}";
                })).Append('\n');
            }
            else
            {
                output.Append(line).Append('\n');
            }
        }

        return output.ToString();
    }

    public static string Resolve(string baseUrl, string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return uri;

        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, uri, out var resolved))
            return resolved.ToString();

        return uri;
    }

    public static string ContentType(CurlResponse response, bool playlist)
    {
        string type = response?.Header("content-type");
        if (!string.IsNullOrEmpty(type))
            return type.Split(';')[0].Trim();

        return playlist ? "application/vnd.apple.mpegurl" : "application/octet-stream";
    }

    public static bool IsPlaylist(byte[] bytes, CurlResponse response)
    {
        if (response?.Header("content-type")?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (bytes == null || bytes.Length < 7)
            return false;

        return bytes[0] == (byte)'#' && bytes[1] == (byte)'E' && bytes[2] == (byte)'X' &&
               bytes[3] == (byte)'T' && bytes[4] == (byte)'M' && bytes[5] == (byte)'3' &&
               bytes[6] == (byte)'U';
    }
}
