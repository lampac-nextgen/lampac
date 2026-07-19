namespace Music;

public static class MusicStreamModeService
{
    public const string Auto = "auto";
    public const string Best = "best";
    public const string Low = "low";
    public const string PreferM4a = "m4a";
    public const string PreferWebm = "webm";

    public static string Normalize(string mode)
    {
        mode = mode?.Trim().ToLowerInvariant();

        return mode switch
        {
            Best => Best,
            Low => Low,
            PreferM4a => PreferM4a,
            PreferWebm => PreferWebm,
            _ => Auto
        };
    }

    public static List<MusicPlaybackSource> Order(IEnumerable<MusicPlaybackSource> sources, string mode)
    {
        if (sources == null)
            return new List<MusicPlaybackSource>();

        mode = Normalize(mode);
        var list = sources.Where(i => i != null && !string.IsNullOrWhiteSpace(i.url)).ToList();

        return mode switch
        {
            Best => list
                .OrderByDescending(GetBitrate)
                .ThenByDescending(IsMp4Like)
                .ToList(),

            Low => list
                .OrderBy(GetBitrate)
                .ThenByDescending(IsMp4Like)
                .ToList(),

            PreferM4a => list
                .OrderByDescending(IsMp4Like)
                .ThenByDescending(GetBitrate)
                .ToList(),

            PreferWebm => list
                .OrderByDescending(IsWebmLike)
                .ThenByDescending(GetBitrate)
                .ToList(),

            _ => list
                .OrderByDescending(IsMp4Like)
                .ThenByDescending(GetBitrate)
                .ToList()
        };
    }

    static int GetBitrate(MusicPlaybackSource source)
        => source?.bitrate ?? 0;

    static bool IsMp4Like(MusicPlaybackSource source)
    {
        string mime = source?.mime_type?.ToLowerInvariant() ?? string.Empty;
        string quality = source?.quality?.ToLowerInvariant() ?? string.Empty;
        return mime.Contains("mp4") || quality.Contains("m4a") || quality.Contains("mp4");
    }

    static bool IsWebmLike(MusicPlaybackSource source)
    {
        string mime = source?.mime_type?.ToLowerInvariant() ?? string.Empty;
        string quality = source?.quality?.ToLowerInvariant() ?? string.Empty;
        return mime.Contains("webm") || quality.Contains("webm");
    }
}
