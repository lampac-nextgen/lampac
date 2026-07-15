using System;
using System.Collections.Generic;

namespace GStreamer.Models;

public class SubtitleStore
{
    public string Pending;
    public ulong ParsedToNs;
    public readonly List<SubtitleCue> Cues = new(256);
    public readonly HashSet<SubtitleCueKey> Seen = new();
}

public class SubtitleCue
{
    public ulong StartNs;
    public ulong EndNs;
    public string Text;
}

public readonly struct SubtitleCueKey : IEquatable<SubtitleCueKey>
{
    readonly ulong StartNs;
    readonly ulong EndNs;
    readonly string Text;

    public SubtitleCueKey(ulong startNs, ulong endNs, string text)
    {
        StartNs = startNs;
        EndNs = endNs;
        Text = text ?? string.Empty;
    }

    public bool Equals(SubtitleCueKey other)
    {
        return StartNs == other.StartNs &&
               EndNs == other.EndNs &&
               string.Equals(Text, other.Text, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is SubtitleCueKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(StartNs, EndNs, Text);
    }
}
