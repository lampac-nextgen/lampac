using GStreamer.Models;
using Shared.Services.Pools;
using System;
using System.Linq;
using System.Text;

namespace GStreamer.Services;

public partial class GStask
{
    string CreatePipelineArgs(ProbeInfo probe)
    {
        var sb = StringBuilderPool.ThreadInstance;
        double gstVersion = ModInit.conf.gst_version;

        AppendSource(sb, gstVersion);
        AppendDemuxer(sb, probe);

        AppendVideo(sb, probe);
        AppendAudio(sb, probe);
        AppendSubtitles(sb, probe, gstVersion);

        AppendMux(sb);
        AppendOutputSink(sb, gstVersion);

        return sb.ToString();
    }

    void AppendSource(StringBuilder sb, double gstVersion)
    {
        string retryOptions = gstVersion >= 1.26
            ? "retry-backoff-factor=0.5 retry-backoff-max=10"
            : string.Empty;

        string blockSizeOption = conf.souphttpsrc_max_mb > 0
            ? $"blocksize={SourceThrottleBlockSize}"
            : string.Empty;

        sb.AppendLine($$"""
        souphttpsrc
            name={{SourceElementName}}
            location="{{sourceUrl}}"
            is-live=false
            keep-alive=true
            timeout=90
            retries=5
            {{blockSizeOption}}
            {{retryOptions}} !
        """);
    }

    static void AppendDemuxer(StringBuilder sb, ProbeInfo probe)
    {
        string demuxer = probe.IsAVI ? "avidemux" : "matroskademux";

        sb.AppendLine($$"""
        {{demuxer}}
            name=d
        multiqueue
            name=mq
            use-buffering=false
            max-size-buffers=5
            max-size-bytes=0
            max-size-time=0
        """);
    }

    void AppendVideo(StringBuilder sb, ProbeInfo probe)
    {
        sb.AppendLine("""
        d.video_0 !
        mq.sink_0
        """);

        if (IsVideoTranscoded)
        {
            AppendTranscodeToH264(sb, probe);
            return;
        }

        if (probe.IsH264)
        {
            sb.AppendLine("""
            mq.src_0 !
            h264parse
                config-interval=0 !
            h264timestamper
                name=video_timestamper !
            video/x-h264,
                stream-format=avc,
                alignment=au !
            mux.video_0
            """);
        }
        else if (probe.IsH265)
        {
            sb.AppendLine("""
            mq.src_0 !
            h265parse
                config-interval=0 !
            h265timestamper
                name=video_timestamper !
            video/x-h265,
                stream-format=hvc1,
                alignment=au !
            mux.video_0
            """);
        }
        else if (probe.IsAV1)
        {
            sb.AppendLine("""
            mq.src_0 !
            av1parse !
            video/x-av1,
                stream-format=obu-stream,
                alignment=tu !
            mux.video_0
            """);
        }
        else if (probe.IsVP9)
        {
            sb.AppendLine("""
            mq.src_0 !
            vp9parse !
            video/x-vp9,
                alignment=frame !
            mux.video_0
            """);
        }
        else
        {
            throw new NotSupportedException("Unsupported video codec");
        }
    }

    void AppendTranscodeToH264(StringBuilder sb, ProbeInfo probe)
    {
        int segmentSeconds = Math.Max(1, conf.segment_seconds);
        bool toneMapHdr = conf.hdr_to_sdr && probe.Video?.IsHdr == true;
        string useOpenCl = conf.useGpu ? "true" : "false";
        string videoFilter = toneMapHdr
            ? probe.Video.VideoTransfer == VideoTransfer.Hlg
                ? $"hdrtonemap transfer=hlg use-opencl={useOpenCl}"
                : $"hdrtonemap transfer=pq use-opencl={useOpenCl}"
            : null;

        int frameRateNum = probe.Video?.FrameRateNum ?? 0;
        int frameRateDen = probe.Video?.FrameRateDen ?? 0;

        int keyIntMax = frameRateNum > 0 && frameRateDen > 0
            ? Math.Max(1, (int)Math.Round((double)frameRateNum * segmentSeconds / frameRateDen))
            : 25 * segmentSeconds;

        bool useHardwareEncoder = conf.useGpu && conf.hardwareAcceleration;
        if (useHardwareEncoder)
            HardwareVideoBackend.Initialize();

        string encoderPipeline = useHardwareEncoder
            ? HardwareVideoBackend.CreateH264Pipeline(
                probe.Video?.Width ?? 0,
                probe.Video?.Height ?? 0,
                conf.video_bitrate,
                keyIntMax
            )
            : null;

        if (encoderPipeline == null)
        {
            string videoConverter = toneMapHdr ? string.Empty : "videoconvert !";
            string x264SpeedPreset = conf.x264Ultrafast ? "ultrafast" : "veryfast";

            encoderPipeline = $$"""
            {{videoConverter}}
            video/x-raw,
                format=I420 !
            x264enc
                name=video_encoder
                tune=zerolatency
                speed-preset={{x264SpeedPreset}}
                bitrate={{conf.video_bitrate}}
                key-int-max={{keyIntMax}}
                bframes=0
                byte-stream=false !
            video/x-h264,
                profile=main,
                stream-format=avc,
                alignment=au !
            """;
        }

        string filterPipeline = videoFilter == null
            ? string.Empty
            : $"{videoFilter} !";

        sb.AppendLine($$"""
        mq.src_0 !
        decodebin !
        {{filterPipeline}}
        {{encoderPipeline}}
        h264parse
            config-interval=0 !
        h264timestamper !
        video/x-h264,
            profile=main,
            stream-format=avc,
            alignment=au !
        mux.video_0
        """);
    }

    void AppendAudio(StringBuilder sb, ProbeInfo probe)
    {
        var selectedAudio = probe.Tracks.FirstOrDefault(track =>
            track.Type == "audio" &&
            track.Index == audioIndex
        );

        int aacChannels = AacChannels(selectedAudio);
        int aacSamplerate = AacSamplerate(selectedAudio);

        int bitrate = conf.aac_bitrate * 1000;
        if (aacChannels > 2)
            bitrate = bitrate * 2;

        sb.AppendLine($$"""
        d.audio_{{audioIndex}} !
        mq.sink_1
        """);

        if (selectedAudio?.IsAAC == true)
        {
            sb.AppendLine("""
            mq.src_1 !
            aacparse !
            audio/mpeg,
                mpegversion=4,
                stream-format=raw !
            mux.audio_0
            """);
        }
        else
        {
            sb.AppendLine($$"""
            mq.src_1 !
            decodebin !
            audioconvert
                dithering=none
                noise-shaping=none !
            audioresample
                quality=2
                sinc-filter-mode=full !
            audio/x-raw,
                format=F32LE,
                layout=interleaved,
                rate={{aacSamplerate}},
                channels={{aacChannels}} !
            avenc_aac
                bitrate={{bitrate}} !
            aacparse !
            audio/mpeg,
                mpegversion=4,
                stream-format=raw,
                rate={{aacSamplerate}},
                channels={{aacChannels}} !
            mux.audio_0
            """);
        }
    }

    void AppendSubtitles(StringBuilder sb, ProbeInfo probe, double gstVersion)
    {
        if (!conf.subtitles)
            return;

        subtitleTracks.Clear();

        string appsinkBackPressure = gstVersion >= 1.28
            ? "leaky-type=none"
            : "drop=false";

        foreach (var track in probe.Tracks)
        {
            if (track.Type != "subtitle")
                continue;

            if (track.Codec is not ("text" or "subrip" or "utf8" or "ass" or "ssa"))
                continue;

            subtitleTracks.Add(track);

            if (!subtitles.ContainsKey(track.Index))
                subtitles[track.Index] = new SubtitleStore();

            string subparse = track.Codec is "ass" or "ssa"
                ? "ssaparse !"
                : string.Empty;

            sb.AppendLine($$"""
            d.{{track.PadName}} !
            queue
                max-size-buffers=16
                max-size-bytes=0
                max-size-time=0 !
            {{subparse}}
            webvttenc !
            appsink
                name=subs_{{track.Index}}
                emit-signals=false
                sync=false
                async=false
                max-buffers=16
                {{appsinkBackPressure}}
                wait-on-eos=false
            """);
        }
    }

    void AppendMux(StringBuilder sb)
    {
        ulong fragmentDurationMs = cueTimeline != null
            ? 1UL
            : checked((ulong)Math.Max(1, conf.segment_seconds) * 1000UL);

        sb.AppendLine($$"""
        mp4mux
            name=mux
            fragment-mode=dash-or-mss
            fragment-duration={{fragmentDurationMs}}
            streamable=true !
        """);
    }

    static void AppendOutputSink(StringBuilder sb, double gstVersion)
    {
        string appsinkBackPressure = gstVersion >= 1.28
            ? "leaky-type=none"
            : "drop=false";

        sb.AppendLine($$"""
        appsink
            name=out
            emit-signals=false
            sync=false
            max-buffers=1
            {{appsinkBackPressure}}
            wait-on-eos=false
        """);
    }


    #region Audio helpers
    int AacChannels(TrackInfo track)
    {
        int channels = conf.aac_channels > 0
            ? conf.aac_channels
            : (track?.Channels ?? 2);

        if (channels <= 0)
            return 2;

        return Math.Clamp(channels, 1, 8);
    }

    int AacSamplerate(TrackInfo track)
    {
        int rate = conf.aac_samplerate > 0
            ? conf.aac_samplerate
            : (track?.Rate ?? 48000);

        if (rate <= 0)
            return 48000;

        int best = AacEncoderRates[0];
        int bestDistance = Math.Abs(rate - best);

        for (int i = 1; i < AacEncoderRates.Length; i++)
        {
            int current = AacEncoderRates[i];
            int distance = Math.Abs(rate - current);

            if (distance >= bestDistance)
                continue;

            best = current;
            bestDistance = distance;
        }

        return best;
    }

    static readonly int[] AacEncoderRates =
    [
        7350,
        8000,
        11025,
        12000,
        16000,
        22050,
        24000,
        32000,
        44100,
        48000,
        64000,
        88200,
        96000
    ];
    #endregion
}
