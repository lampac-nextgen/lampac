using System;
using System.Collections.Generic;

namespace GStreamer.Models;

public class SubtitleStore
{
    public TrackInfo Track;
    public string Pending;
    public readonly List<SubtitleCue> Cues = new(256);
    public readonly HashSet<SubtitleCueKey> Seen = new();
}

public class SubtitleCue
{
    public double StartSeconds;
    public double EndSeconds;
    public string Settings;
    public string Text;
}

public readonly struct SubtitleCueKey : IEquatable<SubtitleCueKey>
{
    readonly int StartMs;
    readonly int EndMs;
    readonly string Text;

    public SubtitleCueKey(double startSeconds, double endSeconds, string text)
    {
        StartMs = (int)Math.Round(startSeconds * 1000d);
        EndMs = (int)Math.Round(endSeconds * 1000d);
        Text = text ?? string.Empty;
    }

    public bool Equals(SubtitleCueKey other)
    {
        return StartMs == other.StartMs &&
               EndMs == other.EndMs &&
               string.Equals(Text, other.Text, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is SubtitleCueKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(StartMs, EndMs, Text);
    }
}
