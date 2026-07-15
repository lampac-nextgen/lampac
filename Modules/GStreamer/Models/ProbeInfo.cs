using System.Collections.Generic;
using System.Linq;

namespace GStreamer.Models;

public sealed class ProbeInfo
{
    public long DurationNs { get; set; }

    public int DurationSeconds =>
        (int)(DurationNs > 0 ? DurationNs / 1_000_000_000.0 : 0);

    public string ContainerName { get; set; }

    public string ContainerCapsName { get; set; }

    public bool IsMatroskaOrWebM =>
        ContainerCapsName == "audio/x-matroska" ||
        ContainerCapsName == "video/x-matroska" ||
        ContainerCapsName == "video/x-matroska-3d" ||
        ContainerCapsName == "audio/webm" ||
        ContainerCapsName == "video/webm";

    public bool IsAVI
        => ContainerCapsName == "video/x-msvideo";

    public List<TrackInfo> Tracks { get; } = new();

    public TrackInfo Video =>
        Tracks.FirstOrDefault(x =>
            x.Type == "video" ||
            x.CapsName == "video/x-h264" ||
            x.CapsName == "video/x-h265" ||
            x.CapsName == "video/x-av1" ||
            x.CapsName == "video/x-vp9" ||
            x.CapsName == "video/x-vp8"
        );

    public string VideoCapsName
        => Video?.CapsName;

    public bool IsH264
        => VideoCapsName == "video/x-h264";

    public bool IsH265
        => VideoCapsName == "video/x-h265";

    public bool IsAV1
        => VideoCapsName == "video/x-av1";

    public bool IsVP9
        => VideoCapsName == "video/x-vp9";

    public bool IsVP8
        => VideoCapsName == "video/x-vp8";
}
