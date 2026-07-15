using GStreamer.Models;
using Shared.Services.Pools;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace GStreamer.Services;

public partial class GStask
{
    readonly Dictionary<int, GstApp.AppSink> subsSinks = new();
    public readonly Dictionary<int, SubtitleStore> subtitles = new();
    readonly List<TrackInfo> subtitleTracks = new();

    static readonly Regex VttCueRegex = new(
        @"(?:^|\n)(?:[^\n]*\n)?(?<start>\d{2,}:\d{2}:\d{2}[.,]\d{3})[ \t]+-->[ \t]+(?<end>\d{2,}:\d{2}:\d{2}[.,]\d{3})[^\n]*\n(?<text>.*?)(?:\n[ \t]*\n)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Singleline
    );

    public bool GetSubtitleVtt(StringBuilder sb, int subtitleIndex, int seg)
    {
        ulong fromNs;
        ulong toNs;

        if (cueTimeline?.TryGetSegment(seg, out CueSegment cueSegment) == true)
        {
            fromNs = cueSegment.StartNs;
            toNs = cueSegment.EndNs;
        }
        else
        {
            int segmentSeconds = Math.Max(1, conf.segment_seconds);
            fromNs = SegmentStartNs(seg, segmentSeconds);
            toNs = AddClockTime(fromNs, SecondsToClockTime(segmentSeconds));
        }

        sb.AppendLine("WEBVTT");
        sb.AppendLine($"X-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:{ClockTimeToMpegTs(fromNs)}");
        sb.AppendLine();

        if (!conf.subtitles)
            return true;

        if (!subtitles.TryGetValue(subtitleIndex, out var store))
            return true;

        lock (store.Cues)
        {
            // subtitle reader еще не дошел до этого сегмента
            if (store.ParsedToNs > 0 && store.ParsedToNs < toNs)
                return false;

            foreach (var cue in store.Cues)
            {
                if (cue.EndNs <= fromNs || cue.StartNs >= toNs)
                    continue;

                ulong startNs = cue.StartNs > fromNs
                    ? cue.StartNs - fromNs
                    : 0;

                ulong endNs = cue.EndNs < toNs
                    ? cue.EndNs - fromNs
                    : toNs - fromNs;

                if (endNs <= startNs)
                    continue;

                TimeSpan start = ClockTimeToTimeSpan(startNs);
                TimeSpan end = ClockTimeToTimeSpan(endNs);

                sb.AppendLine($"{start:hh\\:mm\\:ss\\.fff} --> {end:hh\\:mm\\:ss\\.fff}");
                sb.AppendLine(cue.Text);
                sb.AppendLine();
            }
        }

        return true;
    }

    void DrainSubtitles(CancellationToken ct, int readGeneration)
    {
        if (!conf.subtitles || IsDead)
            return;

        if (subsSinks.Count == 0)
            return;

        if (ct != default && ct.IsCancellationRequested)
            return;

        // pipeline был пересоздан
        if (readGeneration != Volatile.Read(ref pipelineGeneration))
            return;

        foreach (var pair in subsSinks)
        {
            int subtitleIndex = pair.Key;

            if (!subtitles.TryGetValue(subtitleIndex, out var store))
                continue;

            var subSink = pair.Value;

            while (true)
            {
                if (ct != default && ct.IsCancellationRequested)
                    return;

                if (readGeneration != Volatile.Read(ref pipelineGeneration))
                    return;

                using var sample = subSink.TryPullSample(0);
                if (sample == null)
                    break;

                using var buffer = sample.GetBuffer();
                if (buffer == null)
                    continue;

                nuint nsize = buffer.GetSize();
                if (nsize == 0)
                    continue;

                string chunk = null;
                int size = (int)nsize;

                using (var data = new BufferBytePool(size))
                {
                    int copied = (int)buffer.Extract(
                        (nuint)0,
                        data.Span.Slice(0, size)
                    );

                    if (copied <= 0)
                        continue;

                    chunk = Encoding.UTF8.GetString(data.Span.Slice(0, copied));
                    if (string.IsNullOrWhiteSpace(chunk))
                        continue;
                }

                if (chunk.IndexOf('\r') >= 0)
                    chunk = chunk.Replace("\r\n", "\n").Replace('\r', '\n');

                lock (store.Cues)
                {
                    string vtt = string.IsNullOrEmpty(store.Pending)
                        ? chunk
                        : store.Pending + chunk;

                    int consumed = 0;

                    foreach (Match match in VttCueRegex.Matches(vtt))
                    {
                        if (!TryVttClockTime(match.Groups["start"].Value, out ulong startNs) ||
                            !TryVttClockTime(match.Groups["end"].Value, out ulong endNs) ||
                            endNs <= startNs)
                        {
                            consumed = match.Index + match.Length;
                            continue;
                        }

                        string text = match.Groups["text"].Value.Trim();
                        if (text.Length == 0)
                        {
                            consumed = match.Index + match.Length;
                            continue;
                        }

                        ulong seekPosition = Volatile.Read(ref positionSeekSeconds);

                        if (seekPosition > 0)
                        {
                            ulong maxBackDiff = cueTimeline?.MaxDurationNs ??
                                SecondsToClockTime(Math.Max(1, conf.segment_seconds));
                            ulong minExpectedNs = seekPosition > maxBackDiff
                                ? seekPosition - maxBackDiff
                                : 0;

                            // После seek webvttenc может снова дать локальное время от 0
                            // Если cue явно раньше фактической позиции seek, переводим его в абсолютный timeline
                            if (startNs < minExpectedNs)
                            {
                                startNs = AddClockTime(seekPosition, startNs);
                                endNs = AddClockTime(seekPosition, endNs);
                            }
                        }

                        // позиция subtitle reader
                        if (endNs > store.ParsedToNs)
                            store.ParsedToNs = endNs;

                        var key = new SubtitleCueKey(startNs, endNs, text);

                        if (store.Seen.Add(key))
                        {
                            store.Cues.Add(new SubtitleCue
                            {
                                StartNs = startNs,
                                EndNs = endNs,
                                Text = text
                            });
                        }

                        consumed = match.Index + match.Length;
                    }

                    if (consumed > 0)
                    {
                        store.Pending = consumed < vtt.Length
                            ? vtt[consumed..]
                            : null;
                    }
                    else
                    {
                        store.Pending = vtt;
                    }

                    const int MaxPendingVttChars = 64 * 1024;

                    if (!string.IsNullOrEmpty(store.Pending) &&
                        store.Pending.Length > MaxPendingVttChars)
                    {
                        int cut = store.Pending.Length - MaxPendingVttChars;
                        int nl = store.Pending.IndexOf('\n', cut);

                        store.Pending = nl >= 0
                            ? store.Pending[(nl + 1)..]
                            : store.Pending[^MaxPendingVttChars..];
                    }
                }
            }
        }
    }

    static bool TryVttClockTime(string value, out ulong clockTime)
    {
        clockTime = 0;

        if (string.IsNullOrEmpty(value))
            return false;

        ReadOnlySpan<char> span = value.AsSpan().Trim();

        int p1 = span.IndexOf(':');
        if (p1 <= 0)
            return false;

        int p2 = span[(p1 + 1)..].IndexOf(':');
        if (p2 < 0)
            return false;

        p2 += p1 + 1;

        ReadOnlySpan<char> hSpan = span[..p1];
        ReadOnlySpan<char> mSpan = span[(p1 + 1)..p2];
        ReadOnlySpan<char> sSpan = span[(p2 + 1)..];

        int dot = sSpan.IndexOf('.');
        if (dot < 0)
            dot = sSpan.IndexOf(',');

        if (dot <= 0)
            return false;

        ReadOnlySpan<char> secSpan = sSpan[..dot];
        ReadOnlySpan<char> msSpan = sSpan[(dot + 1)..];

        if (!TryParsePositiveInt(hSpan, out int h) ||
            !TryParsePositiveInt(mSpan, out int m) ||
            !TryParsePositiveInt(secSpan, out int s))
        {
            return false;
        }

        int ms = 0;
        int mul = 100;

        for (int i = 0; i < msSpan.Length && i < 3; i++)
        {
            char c = msSpan[i];
            if (c < '0' || c > '9')
                return false;

            ms += (c - '0') * mul;
            mul /= 10;
        }

        if (m > 59 || s > 59)
            return false;

        ulong wholeSeconds = checked((ulong)h * 3600UL + (ulong)m * 60UL + (ulong)s);
        clockTime = checked(wholeSeconds * GstSecond + (ulong)ms * 1_000_000UL);
        return true;
    }

    static bool TryParsePositiveInt(ReadOnlySpan<char> span, out int value)
    {
        value = 0;

        if (span.IsEmpty)
            return false;

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            if (c < '0' || c > '9')
                return false;

            value = checked(value * 10 + c - '0');
        }

        return true;
    }

    static ulong SegmentStartNs(int seg, int segmentSeconds)
    {
        if (seg <= 0 || segmentSeconds <= 0)
            return 0;

        return checked((ulong)seg * (ulong)segmentSeconds * GstSecond);
    }

    static long ClockTimeToMpegTs(ulong clockTime)
    {
        UInt128 value = ((UInt128)clockTime * 90_000UL + GstSecond / 2) / GstSecond;

        return value > long.MaxValue
            ? long.MaxValue
            : (long)value;
    }

    static TimeSpan ClockTimeToTimeSpan(ulong clockTime)
    {
        ulong ticks = clockTime / 100UL;

        return ticks > long.MaxValue
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks((long)ticks);
    }
}
