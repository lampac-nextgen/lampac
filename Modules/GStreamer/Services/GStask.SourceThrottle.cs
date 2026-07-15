using Gst;
using System;
using System.Diagnostics;
using System.Threading;

namespace GStreamer.Services;

public partial class GStask
{
    const string SourceElementName = "http_source";
    const int SourceThrottleBlockSize = 64 * 1024;
    const long SourceThrottleBurstTicksDivisor = 10;

    Pad sourceThrottlePad;
    ulong sourceThrottleProbeId;
    Gst.PadProbeCallback sourceThrottleProbeCallback;
    long sourceThrottleDeadline;

    void InstallSourceThrottleProbe()
    {
        RemoveSourceThrottleProbe();

        if (conf.souphttpsrc_max_mb <= 0)
            return;

        try
        {
            using var source = bin?.GetByName(SourceElementName);
            sourceThrottlePad = source?.GetStaticPad("src");

            if (sourceThrottlePad == null)
                return;

            sourceThrottleProbeCallback = OnSourceThrottleProbe;
            sourceThrottleProbeId = sourceThrottlePad.AddProbe(
                PadProbeType.EventDownstream | PadProbeType.Buffer,
                sourceThrottleProbeCallback
            );

            if (sourceThrottleProbeId == 0)
                RemoveSourceThrottleProbe();
        }
        catch
        {
            RemoveSourceThrottleProbe();
        }
    }

    void RemoveSourceThrottleProbe()
    {
        Volatile.Write(ref sourceThrottleDeadline, 0);

        if (sourceThrottlePad != null && sourceThrottleProbeId != 0)
        {
            try
            {
                sourceThrottlePad.RemoveProbe(sourceThrottleProbeId);
            }
            catch { }
        }

        sourceThrottleProbeId = 0;
        sourceThrottleProbeCallback = null;

        try
        {
            sourceThrottlePad?.Dispose();
        }
        catch { }

        sourceThrottlePad = null;
    }

    PadProbeReturn OnSourceThrottleProbe(Pad pad, PadProbeInfo info)
    {
        if ((info.Type & PadProbeType.EventDownstream) != 0)
        {
            using var ev = info.GetEvent();

            if (ev?.Type is EventType.FlushStart or EventType.Segment)
                Volatile.Write(ref sourceThrottleDeadline, 0);

            return PadProbeReturn.Ok;
        }

        if ((info.Type & PadProbeType.Buffer) == 0)
            return PadProbeReturn.Ok;

        using var buffer = info.GetBuffer();
        nuint size = buffer?.GetSize() ?? 0;

        if (size > 0)
            ThrottleSourceBuffer((ulong)size);

        return PadProbeReturn.Ok;
    }

    void ThrottleSourceBuffer(ulong size)
    {
        long bytesPerSecond = (long)conf.souphttpsrc_max_mb * 1024 * 1024;
        if (bytesPerSecond <= 0)
            return;

        long intervalTicks = Math.Max(
            1,
            (long)Math.Ceiling(size * (double)Stopwatch.Frequency / bytesPerSecond)
        );

        long now = Stopwatch.GetTimestamp();
        long deadline = Volatile.Read(ref sourceThrottleDeadline);
        long burstTicks = Stopwatch.Frequency / SourceThrottleBurstTicksDivisor;

        if (deadline == 0 || deadline < now - burstTicks)
            deadline = now;

        deadline = long.MaxValue - deadline < intervalTicks
            ? long.MaxValue
            : deadline + intervalTicks;

        Volatile.Write(ref sourceThrottleDeadline, deadline);

        while (Volatile.Read(ref sourceThrottleDeadline) == deadline)
        {
            long remainingTicks = deadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
                return;

            int sleepMilliseconds = (int)Math.Min(
                int.MaxValue,
                remainingTicks * 1000L / Stopwatch.Frequency
            );

            if (sleepMilliseconds <= 0)
                return;

            Thread.Sleep(sleepMilliseconds);
        }
    }
}
