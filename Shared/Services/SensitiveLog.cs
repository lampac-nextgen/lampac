using System.Text.RegularExpressions;

namespace Shared.Services;

public static class SensitiveLog
{
    // Redact before truncation: even a shortened bearer URL must not enter a log.
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Regex.Replace(text, @"(/torbox/series/external/)[^/\s?#]+", "$1[redacted]", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"([?&]apikey=)[^&\s\""'<>#]*", "$1[redacted]", RegexOptions.IgnoreCase);
    }
}
