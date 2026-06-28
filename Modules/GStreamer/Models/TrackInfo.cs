using System;

namespace GStreamer.Models;

public sealed class TrackInfo
{
    public int Index { get; set; }
    public string PadName { get; set; }

    public string Type { get; set; }
    public string CapsName { get; set; }
    public string Codec { get; set; }

    public string Title { get; set; }
    public string Language { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Channels { get; set; }
    public int? Rate { get; set; }

    public int? FrameRateNum { get; set; }

    public int? FrameRateDen { get; set; }

    public double? FrameRate =>
        FrameRateNum.HasValue &&
        FrameRateDen.HasValue &&
        FrameRateDen.Value > 0
            ? (double)FrameRateNum.Value / FrameRateDen.Value
            : null;

    public bool IsAAC =>
        Type == "audio" &&
        CapsName == "audio/mpeg" &&
        !string.IsNullOrWhiteSpace(Codec) &&
        Codec.Contains("aac", StringComparison.OrdinalIgnoreCase);
}