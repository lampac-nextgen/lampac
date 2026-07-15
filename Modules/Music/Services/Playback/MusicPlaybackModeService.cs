namespace Music;

public static class MusicPlaybackModeService
{
    public const string Audio = "audio";
    public const string Video = "video";

    public static string Normalize(string mode)
    {
        mode = mode?.Trim().ToLowerInvariant();

        return mode switch
        {
            Video => Video,
            _ => Audio
        };
    }

    public static bool IsVideo(string mode)
        => Normalize(mode) == Video;
}
