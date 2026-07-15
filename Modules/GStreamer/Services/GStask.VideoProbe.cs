using Gst;
using System;
using System.Threading;

namespace GStreamer.Services;

public partial class GStask
{
    Pad videoStartProbePad;
    ulong videoStartProbeId;
    Gst.PadProbeCallback videoStartProbeCallback;
    ulong videoStartProbeRequestedSeconds;

    Pad videoSegmentClipProbePad;
    ulong videoSegmentClipProbeId;
    Gst.PadProbeCallback videoSegmentClipProbeCallback;
    ulong videoSegmentStart = ulong.MaxValue;
    ulong videoSegmentRequestedStart = ulong.MaxValue;

    void InstallVideoStartProbe(ulong requestedNs)
    {
        RemoveVideoStartProbe();

        videoStartProbeRequestedSeconds = requestedNs;

        using var mq = bin?.GetByName("mq");
        videoStartProbePad = mq?.GetStaticPad("src_0");

        if (videoStartProbePad == null)
            return;

        videoStartProbeCallback = OnVideoStartProbe;
        videoStartProbeId = videoStartProbePad.AddProbe(
            PadProbeType.EventDownstream | PadProbeType.Buffer,
            videoStartProbeCallback
        );
    }

    void RemoveVideoStartProbe()
    {
        if (videoStartProbePad != null && videoStartProbeId != 0)
        {
            try
            {
                videoStartProbePad.RemoveProbe(videoStartProbeId);
            }
            catch { }
        }

        ClearVideoStartProbeState();

        try
        {
            videoStartProbePad?.Dispose();
        }
        catch { }

        videoStartProbePad = null;
    }

    void ClearVideoStartProbeState()
    {
        videoStartProbeId = 0;
        videoStartProbeCallback = null;
        videoStartProbeRequestedSeconds = 0;
    }

    void InstallVideoSegmentClipProbe(ulong requestedStart)
    {
        RemoveVideoSegmentClipProbe();

        videoSegmentRequestedStart = requestedStart;
        videoSegmentStart = requestedStart;

        bool passthroughVideo = !IsVideoTranscoded;
        bool passthroughH264 = passthroughVideo && probe.IsH264;
        bool passthroughH265 = passthroughVideo && probe.IsH265;

        if (!passthroughH264 && !passthroughH265)
            return;

        using var timestamper = bin?.GetByName("video_timestamper");
        videoSegmentClipProbePad = timestamper?.GetStaticPad("src");

        if (videoSegmentClipProbePad == null)
            return;

        videoSegmentClipProbeCallback = OnVideoSegmentClipProbe;
        videoSegmentClipProbeId = videoSegmentClipProbePad.AddProbe(
            PadProbeType.EventDownstream | PadProbeType.Buffer,
            videoSegmentClipProbeCallback
        );
    }

    void RemoveVideoSegmentClipProbe()
    {
        if (videoSegmentClipProbePad != null && videoSegmentClipProbeId != 0)
        {
            try
            {
                videoSegmentClipProbePad.RemoveProbe(videoSegmentClipProbeId);
            }
            catch { }
        }

        videoSegmentClipProbeId = 0;
        videoSegmentClipProbeCallback = null;
        videoSegmentStart = ulong.MaxValue;
        videoSegmentRequestedStart = ulong.MaxValue;

        try
        {
            videoSegmentClipProbePad?.Dispose();
        }
        catch { }

        videoSegmentClipProbePad = null;
    }

    PadProbeReturn OnVideoSegmentClipProbe(Pad pad, PadProbeInfo info)
    {
        if ((info.Type & PadProbeType.EventDownstream) != 0)
        {
            using var ev = info.GetEvent();

            if (ev?.Type == EventType.FlushStart)
            {
                videoSegmentStart = videoSegmentRequestedStart;
            }
            else if (TryGetTimeSegment(
                ev,
                out ulong segmentStart,
                out _
            ))
            {
                videoSegmentStart = videoSegmentRequestedStart != ulong.MaxValue &&
                    segmentStart < videoSegmentRequestedStart
                        ? videoSegmentRequestedStart
                        : segmentStart;
            }

            return PadProbeReturn.Ok;
        }

        if ((info.Type & PadProbeType.Buffer) == 0)
            return PadProbeReturn.Ok;

        using var buffer = info.GetBuffer();
        if (buffer == null)
            return PadProbeReturn.Ok;

        ulong pts = buffer.Handle.GetPts();

        if (videoSegmentStart == ulong.MaxValue)
            return PadProbeReturn.Ok;

        return pts != ulong.MaxValue && pts < videoSegmentStart
            ? PadProbeReturn.Drop
            : PadProbeReturn.Ok;
    }

    PadProbeReturn OnVideoStartProbe(Pad pad, PadProbeInfo info)
    {
        if ((info.Type & PadProbeType.EventDownstream) != 0)
        {
            using var ev = info.GetEvent();

            if (TryGetSegmentClockTime(ev, out ulong segmentTime) &&
                IsAcceptableVideoStartClockTime(segmentTime))
            {
                ApplyVideoStartClockTime(segmentTime);
            }

            return PadProbeReturn.Ok;
        }

        if ((info.Type & PadProbeType.Buffer) == 0)
            return PadProbeReturn.Ok;

        using var buffer = info.GetBuffer();

        if (!TryGetBufferClockTime(buffer, out ulong presentationTime))
            return PadProbeReturn.Ok;

        if (!IsAcceptableVideoStartClockTime(presentationTime))
            return PadProbeReturn.Ok;

        ApplyVideoStartClockTime(presentationTime);

        ClearVideoStartProbeState();
        return PadProbeReturn.Remove;
    }

    bool TryGetSegmentClockTime(Event ev, out ulong clockTime)
    {
        clockTime = 0;

        if (!TryGetTimeSegment(ev, out ulong start, out ulong time))
            return false;

        clockTime = time != ulong.MaxValue ? time : start;

        return clockTime != ulong.MaxValue;
    }

    static bool TryGetBufferClockTime(Gst.Buffer buffer, out ulong clockTime)
    {
        clockTime = 0;

        if (buffer == null)
            return false;

        ulong pts = buffer.Handle.GetPts();
        if (pts != ulong.MaxValue)
        {
            clockTime = pts;
            return true;
        }

        ulong dts = buffer.Handle.GetDts();
        if (dts != ulong.MaxValue)
        {
            clockTime = dts;
            return true;
        }

        return false;
    }

    bool IsAcceptableVideoStartClockTime(ulong clockTime)
    {
        ulong requested = videoStartProbeRequestedSeconds;
        ulong maxBackDiff = cueTimeline?.MaxDurationNs ??
            SecondsToClockTime(Math.Max(1, conf.segment_seconds));

        return requested <= maxBackDiff ||
               clockTime >= requested - maxBackDiff;
    }

    void ApplyVideoStartClockTime(ulong clockTime)
    {
        Volatile.Write(ref positionSeekSeconds, clockTime);
        Volatile.Write(ref positionSeconds, clockTime);
        mp4Reader.SetTimelineOffsetNs(clockTime);
    }

    static bool TryGetTimeSegment(
        Event ev,
        out ulong start,
        out ulong time
    )
    {
        start = ulong.MaxValue;
        time = ulong.MaxValue;

        if (ev == null || ev.Type != EventType.Segment)
            return false;

        try
        {
            using var segment = Gst.Segment.New();
            ev.CopySegment(segment);

            if (segment.Format != Format.Time)
                return false;

            start = segment.Start;
            time = segment.Time;
            return true;
        }
        catch
        {
            start = ulong.MaxValue;
            time = ulong.MaxValue;
            return false;
        }
    }
}
