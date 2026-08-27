namespace Music;

internal static class MusicIsrc
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = new char[12];
        int length = 0;

        foreach (char current in value.Trim())
        {
            if (current == '-' || char.IsWhiteSpace(current))
                continue;

            if (!char.IsAsciiLetterOrDigit(current) || length >= normalized.Length)
                return null;

            normalized[length++] = char.ToUpperInvariant(current);
        }

        if (length != normalized.Length
            || !char.IsAsciiLetter(normalized[0])
            || !char.IsAsciiLetter(normalized[1]))
        {
            return null;
        }

        for (int i = 5; i < normalized.Length; i++)
        {
            if (!char.IsAsciiDigit(normalized[i]))
                return null;
        }

        return new string(normalized);
    }
}
