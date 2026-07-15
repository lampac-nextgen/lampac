using System;
using System.Collections.Generic;

namespace GStreamer.Models;

public readonly record struct CueSegment(ulong StartNs, ulong EndNs)
{
    public ulong DurationNs => EndNs - StartNs;
}

public sealed class CueTimeline
{
    readonly CueSegment[] segments;

    public CueTimeline(CueSegment[] segments, ulong timestampScaleNs)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Length == 0)
            throw new ArgumentException("Cue timeline is empty.", nameof(segments));

        this.segments = segments;
        TimestampScaleNs = timestampScaleNs;

        ulong maxDurationNs = 0;

        foreach (CueSegment segment in segments)
        {
            if (segment.EndNs <= segment.StartNs)
                throw new ArgumentException("Cue timeline contains an invalid segment.", nameof(segments));

            if (segment.DurationNs > maxDurationNs)
                maxDurationNs = segment.DurationNs;
        }

        MaxDurationNs = maxDurationNs;
    }

    public IReadOnlyList<CueSegment> Segments => segments;

    public int Count => segments.Length;

    public ulong TimestampScaleNs { get; }

    public ulong MaxDurationNs { get; }

    public bool TryGetSegment(int index, out CueSegment segment)
    {
        if ((uint)index < (uint)segments.Length)
        {
            segment = segments[index];
            return true;
        }

        segment = default;
        return false;
    }
}
