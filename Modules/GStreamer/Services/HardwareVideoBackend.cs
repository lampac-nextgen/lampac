using Gst;
using GStreamer.Models;
using System;
using System.Linq;

namespace GStreamer.Services;

internal static class HardwareVideoBackend
{
    const ulong ProbeTimeoutNs = 5_000_000_000UL;

    static readonly object sync = new();
    static readonly Candidate[] candidates =
    [
        new(
            "Direct3D11 + Media Foundation",
            ["d3d11upload", "d3d11convert", "mfh264enc"],
            static (bitrate, keyIntMax) => $$"""
            d3d11upload !
            d3d11convert !
            video/x-raw(memory:D3D11Memory),
                format=NV12 !
            mfh264enc
                name=video_encoder
                bitrate={{bitrate}}
                gop-size={{keyIntMax}}
                low-latency=true
                rc-mode=cbr !
            """
        ),
        new(
            "NVIDIA NVENC",
            ["videoconvert", "nvh264enc"],
            static (bitrate, keyIntMax) => $$"""
            videoconvert !
            video/x-raw,
                format=NV12 !
            nvh264enc
                name=video_encoder
                bitrate={{bitrate}}
                gop-size={{keyIntMax}}
                bframes=0
                zerolatency=true
                rc-mode=cbr !
            """
        ),
        new(
            "Intel Quick Sync",
            ["videoconvert", "qsvh264enc"],
            static (bitrate, keyIntMax) => $$"""
            videoconvert !
            video/x-raw,
                format=NV12 !
            qsvh264enc
                name=video_encoder
                bitrate={{bitrate}}
                gop-size={{keyIntMax}}
                b-frames=0
                low-latency=true
                rate-control=cbr !
            """
        ),
        new(
            "AMD AMF",
            ["videoconvert", "amfh264enc"],
            static (bitrate, keyIntMax) => $$"""
            videoconvert !
            video/x-raw,
                format=NV12 !
            amfh264enc
                name=video_encoder
                bitrate={{bitrate}}
                gop-size={{keyIntMax}}
                b-frames=0
                usage=low-latency
                preset=speed
                rate-control=cbr !
            """
        ),
        new(
            "Direct3D12",
            ["videoconvert", "d3d12h264enc"],
            static (bitrate, keyIntMax) => $$"""
            videoconvert !
            video/x-raw,
                format=NV12 !
            d3d12h264enc
                name=video_encoder
                bitrate={{bitrate}}
                gop-size={{keyIntMax}}
                rate-control=cbr !
            """
        )
    ];

    static bool initialized;
    static Candidate selected;
    static int maxWidth;
    static int maxHeight;

    public static string Name => selected?.Name;

    public static void Initialize()
    {
        lock (sync)
        {
            if (initialized)
                return;

            initialized = true;

            foreach (var candidate in candidates)
            {
                if (!candidate.Elements.All(HasFactory))
                    continue;

                if (Probe(candidate, 3840, 2160))
                {
                    Select(candidate, 3840, 2160);
                    break;
                }

                if (Probe(candidate, 1920, 1080))
                {
                    Select(candidate, 1920, 1080);
                    break;
                }
            }
        }

        if (selected == null)
        {
            Console.WriteLine("GStreamer hardware video backend: software fallback");

            Serilog.Log.Information(
                "GStreamer hardware video backend is unavailable; using software transcoding."
            );
        }
        else
        {
            Console.WriteLine(
                $"GStreamer hardware video backend: {selected.Name}, tested up to {maxWidth}x{maxHeight}"
            );

            Serilog.Log.Information(
                "GStreamer hardware video backend enabled. Backend={Backend}, MaximumTestedResolution={Width}x{Height}",
                selected.Name,
                maxWidth,
                maxHeight
            );
        }
    }

    public static string CreateH264Pipeline(int width, int height, int bitrate, int keyIntMax)
    {
        var candidate = selected;

        if (candidate == null ||
            width < 128 || height < 128 ||
            (width & 1) != 0 || (height & 1) != 0 ||
            width > maxWidth || height > maxHeight)
        {
            return null;
        }

        return candidate.Build(bitrate, keyIntMax);
    }

    static bool Probe(Candidate candidate, int width, int height)
    {
        int bufferSize = checked(width * height * 3 / 2);
        string pipelineArgs = $$"""
        fakesrc
            num-buffers=2
            sizetype=fixed
            sizemax={{bufferSize}}
            do-timestamp=true
            format=time !
        video/x-raw,
            format=NV12,
            width={{width}},
            height={{height}},
            framerate=30/1 !
        {{candidate.Build(14_000, 270)}}
        h264parse !
        video/x-h264,
            profile=main,
            stream-format=avc,
            alignment=au !
        fakesink
            sync=false
        """;

        Pipeline pipeline = null;
        Bus bus = null;

        try
        {
            pipeline = (Pipeline)Gst.Functions.ParseLaunch(pipelineArgs);
            bus = pipeline.GetBus();

            if (bus == null || pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
                return false;

            using var message = bus.TimedPopFiltered(
                ProbeTimeoutNs,
                MessageType.Error | MessageType.Eos
            );

            return BusReader.GetType(message) == BusReader.Eos;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                pipeline?.SetState(State.Null);
            }
            catch { }

            bus?.Dispose();
            pipeline?.Dispose();
        }
    }

    static bool HasFactory(string elementName)
    {
        using var factory = ElementFactory.Find(elementName);
        return factory != null;
    }

    static void Select(Candidate candidate, int width, int height)
    {
        selected = candidate;
        maxWidth = width;
        maxHeight = height;
    }

    sealed record Candidate(
        string Name,
        string[] Elements,
        Func<int, int, string> Build
    );
}
