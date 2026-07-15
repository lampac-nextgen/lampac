using GStreamer.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GStreamer.Services;

/*
{
  "DurationNs": 5649920000000,
  "DurationSeconds": 5649,
  "Tracks": [
    {
      "Index": 0,
      "PadName": "video_0",
      "Type": "video",
      "CapsName": "video/x-h264",
      "Title": null,
      "Language": "und",
      "Width": 1920,
      "Height": 800,
      "Channels": null,
      "Rate": null,
      "FrameRateNum": 24000,
      "FrameRateDen": 1001,
      "FrameRate": 23.976023976023978
    },
    {
      "Index": 0,
      "PadName": "audio_0",
      "Type": "audio",
      "CapsName": "audio/x-ac3",
      "Title": "DUB | Невафильм",
      "Language": "ru",
      "Width": null,
      "Height": null,
      "Channels": null,
      "Rate": 48000,
      "FrameRateNum": null,
      "FrameRateDen": null,
      "FrameRate": null
    },
    {
      "Index": 1,
      "PadName": "audio_1",
      "Type": "audio",
      "CapsName": "audio/x-eac3",
      "Title": "Original",
      "Language": "en",
      "Width": null,
      "Height": null,
      "Channels": null,
      "Rate": 48000,
      "FrameRateNum": null,
      "FrameRateDen": null,
      "FrameRate": null
    }
  ],
  "Video": {
    "Index": 0,
    "PadName": "video_0",
    "Type": "video",
    "CapsName": "video/x-h264",
    "Title": null,
    "Language": "und",
    "Width": 1920,
    "Height": 800,
    "Channels": null,
    "Rate": null,
    "FrameRateNum": 24000,
    "FrameRateDen": 1001,
    "FrameRate": 23.976023976023978
  },
  "VideoCapsName": "video/x-h264",
  "IsH264": true,
  "IsH265": false,
  "IsAV1": false,
  "IsVP9": false,
  "IsVP8": false
}
*/


public static class GSProbe
{
    public static async Task<ProbeInfo> Get(string sourceUrl, int timeoutSeconds = 30)
    {
        try
        {
            string output = await DiscovererAsync(sourceUrl, timeoutSeconds);
            if (string.IsNullOrWhiteSpace(output))
                return null;

            //Console.WriteLine(output);

            return Parse(output);
        }
        catch
        {
            return null;
        }
    }

    static async Task<string> DiscovererAsync(string sourceUrl, int timeoutSeconds)
    {
        using (var process = new Process())
        {
            var stdout = new StringBuilder(64 * 1024);
            var stderr = new StringBuilder(16 * 1024);

            process.StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows()
                    ? Path.Combine(
                        ModInit.gstRootPath ?? ModInit.conf.PATH,
                        "bin",
                        "gst-discoverer-1.0.exe"
                    )
                    : "gst-discoverer-1.0",

                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,

                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process.StartInfo.Environment["LANG"] = "C.UTF-8";
            process.StartInfo.Environment["LC_ALL"] = "C.UTF-8";
            process.StartInfo.Environment["LANGUAGE"] = "en";
            process.StartInfo.Environment["GST_DEBUG_NO_COLOR"] = "1";

            process.StartInfo.ArgumentList.Add("-v");
            process.StartInfo.ArgumentList.Add("-t");
            process.StartInfo.ArgumentList.Add(
                timeoutSeconds.ToString(CultureInfo.InvariantCulture)
            );
            process.StartInfo.ArgumentList.Add(sourceUrl);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stdout.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };

            if (!process.Start())
                return null;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 3)))
            {
                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token);

                    // дочитывает остатки async stdout/stderr events
                    process.WaitForExit();
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    return null;
                }

                if (stdout.Length > 0)
                    return stdout.ToString();

                if (stderr.Length > 0) // логи на перспективу 
                    return stderr.ToString();

                return null;
            }
        }
    }

    static ProbeInfo Parse(string text)
    {
        var probe = new ProbeInfo
        {
            DurationNs = ParseDurationNs(text)
        };

        TrackInfo current = null;

        foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (TryParseContainerLine(line, out string containerName, out string containerCapsName))
            {
                probe.ContainerName = containerName;
                probe.ContainerCapsName = containerCapsName;

                // Строка container относится не к текущему audio/video stream.
                current = null;
                continue;
            }

            var stream = TryParseStreamHeader(line);
            if (stream != null)
            {
                current = stream;
                probe.Tracks.Add(current);
                continue;
            }

            if (current == null)
                continue;

            ParseTrackLine(current, line);
        }

        int video = 0;
        int audio = 0;
        int subtitle = 0;

        foreach (var track in probe.Tracks)
        {
            if (track.Type == "video")
            {
                track.Index = video;
                track.PadName = $"video_{video}";
                video++;
            }
            else if (track.Type == "audio")
            {
                track.Index = audio;
                track.PadName = $"audio_{audio}";
                audio++;
            }
            else if (track.Type == "subtitle")
            {
                track.Index = subtitle;
                track.PadName = $"subtitle_{subtitle}";
                subtitle++;
            }
        }

        return probe.Tracks.Count > 0 ? probe : null;
    }

    static TrackInfo TryParseStreamHeader(string line)
    {
        // Примеры:
        // video #1: H.264 (High Profile)
        // audio #2: AC-3 (ATSC A/52)
        // audio #3: E-AC-3 (ATSC A/52B)
        // subtitle #4: SubRip subtitle

        var match = Regex.Match(
            line,
            @"^(?<type>video|audio|subtitle|subtitles)\s+#(?<idx>\d+):\s*(?<codec>.+)$",
            RegexOptions.IgnoreCase
        );

        if (!match.Success)
            return null;

        string type = match.Groups["type"].Value.ToLowerInvariant();
        if (type == "subtitles")
            type = "subtitle";

        string codec = match.Groups["codec"].Value.Trim();

        return new TrackInfo
        {
            Index = int.Parse(match.Groups["idx"].Value, CultureInfo.InvariantCulture),
            Type = type,
            Codec = type == "subtitle" ? SubtitleCodec(codec) : codec,
            CapsName = CodecToCapsName(type, codec)
        };
    }

    static void ParseVideoMetadata(TrackInfo track, string line)
    {
        if (!string.Equals(track.Type, "video", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(line))
            return;

        track.Colorimetry ??= ReadMetadataField(line, "colorimetry");
        track.Transfer ??= ReadMetadataField(line, "transfer") ??
            ReadMetadataField(line, "transfer-characteristics") ??
            ReadMetadataField(line, "transfer-function");
        track.Primaries ??= ReadMetadataField(line, "primaries") ?? ReadMetadataField(line, "color-primaries");
        track.Matrix ??= ReadMetadataField(line, "matrix") ?? ReadMetadataField(line, "matrix-coefficients");
        track.BitDepth = track.BitDepth > 0 ? track.BitDepth :
            ReadMetadataInt(line, "bit-depth-luma") ??
            ReadMetadataInt(line, "bit-depth") ??
            ReadMetadataInt(line, "bits-per-component") ?? 0;

        if (track.BitDepth == 0 && Regex.IsMatch(line, @"\b(?:P010|10LE|10BE)\b", RegexOptions.IgnoreCase))
            track.BitDepth = 10;
        else if (track.BitDepth == 0 && Regex.IsMatch(line, @"\b(?:P012|12LE|12BE)\b", RegexOptions.IgnoreCase))
            track.BitDepth = 12;

        track.HasMasteringDisplayInfo |= HasMeaningfulMetadataField(line, "mastering-display-info") ||
            HasMeaningfulMetadataField(line, "mastering-display-metadata");
        track.HasContentLightLevel |= HasMeaningfulMetadataField(line, "content-light-level") ||
            HasMeaningfulMetadataField(line, "max-cll") ||
            HasMeaningfulMetadataField(line, "max-fall");

        if (Regex.IsMatch(line, @"\b(?:dvhe|dvh1|dvcC|dvvC)\b|dolby[\s-]*vision", RegexOptions.IgnoreCase))
        {
            track.IsDolbyVision = true;
            var match = Regex.Match(line,
                @"(?:dv[\s_-]*profile|profile)\s*(?:=|:)\s*(?:\([^)]*\))?\s*(?<value>\d+)",
                RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["value"].Value, out int profile))
                track.DolbyVisionProfile = profile;
        }

        ClassifyVideoTransfer(track);
    }

    static void ClassifyVideoTransfer(TrackInfo track)
    {
        string value = ((track.Transfer ?? "") + " " + (track.Colorimetry ?? "")).Trim().ToLowerInvariant();
        if (MatchesTransfer(value, 16, "pq", "smpte2084", "smpte-st-2084", "st2084", "bt2100-pq"))
            track.VideoTransfer = VideoTransfer.Pq;
        else if (MatchesTransfer(value, 18, "hlg", "arib-std-b67", "arib-std-b-67", "bt2100-hlg"))
            track.VideoTransfer = VideoTransfer.Hlg;
        else if (MatchesTransfer(value, 1, "bt709", "bt601", "smpte170m", "smpte240m", "gamma22", "gamma28", "srgb") ||
            IsKnownSdrNumericTransfer(value))
            track.VideoTransfer = VideoTransfer.Sdr;
        else
            track.VideoTransfer = VideoTransfer.Unknown;
    }

    static bool MatchesTransfer(string value, int numericValue, params string[] names)
    {
        foreach (string name in names)
        {
            if (Regex.IsMatch(value,
                @"(?:^|[^a-z0-9])" + Regex.Escape(name) + @"(?:$|[^a-z0-9])",
                RegexOptions.IgnoreCase))
                return true;
        }

        return Regex.IsMatch(value, @"(?:^|[^0-9])" + numericValue + @"(?:$|[^0-9])");
    }

    static bool IsKnownSdrNumericTransfer(string value)
    {
        foreach (int transfer in new[] { 4, 5, 6, 7, 8, 13, 14, 15 })
        {
            if (Regex.IsMatch(value, @"(?:^|[^0-9])" + transfer + @"(?:$|[^0-9])"))
                return true;
        }
        return false;
    }

    static string ReadMetadataField(string line, string name)
    {
        string key = Regex.Escape(name);
        var caps = Regex.Match(line,
            @"(?:^|[,\s])" + key + @"\s*=\s*(?:\([^)]*\))?\s*(?:[""'](?<quoted>[^""']+)[""']|(?<plain>[^,\s]+))",
            RegexOptions.IgnoreCase);
        if (caps.Success)
            return (caps.Groups["quoted"].Success ? caps.Groups["quoted"].Value : caps.Groups["plain"].Value).Trim();

        var label = Regex.Match(line,
            @"^\s*" + key + @"\s*:\s*(?<value>.+?)\s*$",
            RegexOptions.IgnoreCase);
        return label.Success ? label.Groups["value"].Value.Trim().Trim('"', '\'') : null;
    }

    static int? ReadMetadataInt(string line, string name)
    {
        string value = ReadMetadataField(line, name);
        if (value == null)
            return null;

        var match = Regex.Match(value, @"\d+");
        return match.Success && int.TryParse(match.Value, out int result) ? result : null;
    }

    static bool HasMeaningfulMetadataField(string line, string name)
    {
        string value = ReadMetadataField(line, name);
        if (string.IsNullOrWhiteSpace(value))
            return line.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;

        return !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    static void ParseTrackLine(TrackInfo track, string line)
    {
        ParseVideoMetadata(track, line);

        if (line.StartsWith("Width:", StringComparison.OrdinalIgnoreCase))
        {
            track.Width = ParseIntAfterColon(line);
            return;
        }

        if (line.StartsWith("Height:", StringComparison.OrdinalIgnoreCase))
        {
            track.Height = ParseIntAfterColon(line);
            return;
        }

        if (line.StartsWith("Channels:", StringComparison.OrdinalIgnoreCase))
        {
            track.Channels = ParseIntAfterColon(line);
            return;
        }

        if (line.StartsWith("Sample rate:", StringComparison.OrdinalIgnoreCase))
        {
            track.Rate = ParseIntAfterColon(line);
            return;
        }

        if (line.StartsWith("language code:", StringComparison.OrdinalIgnoreCase))
        {
            track.Language = ValueAfterColon(line);
            return;
        }

        if (line.StartsWith("language name:", StringComparison.OrdinalIgnoreCase))
        {
            track.Language ??= ValueAfterColon(line);
            return;
        }

        if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
        {
            track.Title = ValueAfterColon(line);
            return;
        }

        if (line.StartsWith("audio codec:", StringComparison.OrdinalIgnoreCase))
        {
            string codec = ValueAfterColon(line);

            if (!string.IsNullOrWhiteSpace(codec))
            {
                track.Codec = codec;
                track.CapsName ??= CodecToCapsName(track.Type, codec);
            }

            return;
        }

        if (line.StartsWith("video codec:", StringComparison.OrdinalIgnoreCase))
        {
            string codec = ValueAfterColon(line);

            if (!string.IsNullOrWhiteSpace(codec))
            {
                track.Codec = codec;
                track.CapsName ??= CodecToCapsName(track.Type, codec);
            }

            return;
        }

        if (line.StartsWith("subtitle codec:", StringComparison.OrdinalIgnoreCase))
        {
            string codec = ValueAfterColon(line);

            if (!string.IsNullOrWhiteSpace(codec))
            {
                track.Codec = SubtitleCodec(codec);
                track.CapsName = CodecToCapsName(track.Type, codec);
            }

            return;
        }

        if (line.StartsWith("Frame rate:", StringComparison.OrdinalIgnoreCase))
        {
            ParseFrameRate(track, ValueAfterColon(line));
            return;
        }
    }

    static string SubtitleCodec(string codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
            return "unknown";

        string c = codec.ToLowerInvariant();

        if (c.Contains("subpicture/x-pgs") ||
            c.Contains("pgs") ||
            c.Contains("hdmv") ||
            c.Contains("presentation graphic"))
        {
            return "pgs";
        }

        if (c.Contains("subpicture/x-dvd") ||
            c.Contains("dvd") ||
            c.Contains("vobsub"))
        {
            return "dvd";
        }

        if (c.Contains("application/x-ass") ||
            c.Contains("advanced substation") ||
            Regex.IsMatch(c, @"\bass\b", RegexOptions.IgnoreCase))
        {
            return "ass";
        }

        if (c.Contains("application/x-ssa") ||
            c.Contains("substation alpha") ||
            Regex.IsMatch(c, @"\bssa\b", RegexOptions.IgnoreCase))
        {
            return "ssa";
        }

        if (c.Contains("subrip") ||
            Regex.IsMatch(c, @"\bsrt\b", RegexOptions.IgnoreCase))
        {
            return "subrip";
        }

        if (c.Contains("utf-8") ||
            c.Contains("utf8"))
        {
            return "utf8";
        }

        if (c.Contains("webvtt") ||
            Regex.IsMatch(c, @"\bvtt\b", RegexOptions.IgnoreCase) ||
            c.Contains("timed text") ||
            c.Contains("tx3g") ||
            c.Contains("text/x-raw") ||
            c.Contains("text"))
        {
            return "text";
        }

        if (c.Contains("kate"))
            return "kate";

        return "unknown";
    }

    static long ParseDurationNs(string text)
    {
        var match = Regex.Match(
            text,
            @"Duration:\s*(?<h>\d+):(?<m>\d+):(?<s>\d+)(?:\.(?<ns>\d+))?",
            RegexOptions.IgnoreCase
        );

        if (!match.Success)
            return 0;

        long h = long.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        long m = long.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        long s = long.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);

        string nsText = match.Groups["ns"].Success
            ? match.Groups["ns"].Value
            : "";

        if (nsText.Length > 9)
            nsText = nsText[..9];

        nsText = nsText.PadRight(9, '0');

        long ns = string.IsNullOrEmpty(nsText)
            ? 0
            : long.Parse(nsText, CultureInfo.InvariantCulture);

        return
            h * 3_600_000_000_000L +
            m * 60_000_000_000L +
            s * 1_000_000_000L +
            ns;
    }

    static string CodecToCapsName(string type, string codec)
    {
        if (string.IsNullOrEmpty(codec))
            return null;

        string c = codec.ToLowerInvariant();

        if (type == "video")
        {
            if (c.Contains("h.264") || c.Contains("h264") || c.Contains("avc"))
                return "video/x-h264";

            if (c.Contains("h.265") || c.Contains("h265") || c.Contains("hevc"))
                return "video/x-h265";

            if (c.Contains("av1"))
                return "video/x-av1";

            if (c.Contains("vp9"))
                return "video/x-vp9";

            if (c.Contains("vp8"))
                return "video/x-vp8";
        }

        if (type == "audio")
        {
            if (c.Contains("e-ac-3") || c.Contains("eac3") || c.Contains("e-ac3"))
                return "audio/x-eac3";

            if (c.Contains("ac-3") || c.Contains("ac3") || c.Contains("a/52"))
                return "audio/x-ac3";

            if (c.Contains("aac"))
                return "audio/mpeg";

            if (c.Contains("opus"))
                return "audio/x-opus";

            if (c.Contains("vorbis"))
                return "audio/x-vorbis";

            if (c.Contains("flac"))
                return "audio/x-flac";

            if (c.Contains("mpeg") || c.Contains("mp3"))
                return "audio/mpeg";
        }

        if (type == "subtitle")
        {
            return SubtitleCodec(codec) switch
            {
                "text" or "subrip" or "utf8" => "text/x-raw",
                "ass" => "application/x-ass",
                "ssa" => "application/x-ssa",
                "pgs" => "subpicture/x-pgs",
                "dvd" => "subpicture/x-dvd",
                "kate" => "subtitle/x-kate",
                _ => "application/x-subtitle-unknown"
            };
        }

        return null;
    }

    static int? ParseIntAfterColon(string line)
    {
        string value = ValueAfterColon(line);

        if (int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int result))
        {
            return result;
        }

        return null;
    }

    static string ValueAfterColon(string line)
    {
        int p = line.IndexOf(':');

        if (p < 0 || p + 1 >= line.Length)
            return null;

        return line[(p + 1)..].Trim();
    }

    static void ParseFrameRate(TrackInfo track, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string[] parts = value.Split(
            '/',
            2,
            StringSplitOptions.TrimEntries
        );

        if (!int.TryParse(
                parts[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int numerator))
        {
            return;
        }

        int denominator = 1;

        if (parts.Length == 2 &&
            !int.TryParse(
                parts[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out denominator))
        {
            return;
        }

        if (numerator <= 0 || denominator <= 0)
            return;

        track.FrameRateNum = numerator;
        track.FrameRateDen = denominator;
    }

    static bool TryParseContainerLine(
        string line,
        out string containerName,
        out string containerCapsName
    )
    {
        containerName = null;
        containerCapsName = null;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        // gst-discoverer варианты:
        // container: Matroska
        // container #0: Matroska
        // container format: Matroska
        // container-format: Matroska
        var match = Regex.Match(
            line,
            @"^(?:container(?:\s+#\d+)?|container[\s-]+format)\s*:\s*(?<value>.+)$",
            RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            containerName = match.Groups["value"].Value.Trim();
            containerCapsName = ContainerToCapsName(containerName);

            return !string.IsNullOrWhiteSpace(containerName);
        }

        // На некоторых версиях/режимах -v могут встретиться caps напрямую:
        // video/x-matroska, ...
        // video/webm, ...
        containerCapsName = ContainerCapsFromCapsLine(line);
        if (containerCapsName == null)
            return false;

        containerName = containerCapsName;
        return true;
    }

    static string ContainerToCapsName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string directCaps = ContainerCapsFromCapsLine(value);
        if (directCaps != null)
            return directCaps;

        string c = value.ToLowerInvariant();

        if (c.Contains("webm"))
            return "video/webm";

        if (c.Contains("matroska") || c.Contains("x-matroska"))
            return "video/x-matroska";

        // Ниже не поддерживается текущим pipeline, но полезно для диагностики.
        if (c.Contains("quicktime") ||
            c.Contains("iso mp4") ||
            c.Contains("mpeg-4") ||
            Regex.IsMatch(c, @"\bmp4\b", RegexOptions.IgnoreCase))
        {
            return "video/quicktime";
        }

        if (c.Contains("mpeg-ts") ||
            c.Contains("mpegts") ||
            c.Contains("transport stream"))
        {
            return "video/mpegts";
        }

        if (c.Contains("avi"))
            return "video/x-msvideo";

        if (c.Contains("ogg"))
            return "application/ogg";

        if (c.Contains("flv") || c.Contains("flash video"))
            return "video/x-flv";

        return null;
    }

    static string ContainerCapsFromCapsLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string caps = value.Trim();

        int comma = caps.IndexOf(',');
        if (comma >= 0)
            caps = caps[..comma].Trim();

        caps = caps.Trim('"', '\'');

        return caps switch
        {
            "audio/x-matroska" => "audio/x-matroska",
            "video/x-matroska" => "video/x-matroska",
            "video/x-matroska-3d" => "video/x-matroska-3d",
            "audio/webm" => "audio/webm",
            "video/webm" => "video/webm",

            // Диагностические unsupported container caps.
            "video/quicktime" => "video/quicktime",
            "video/mp4" => "video/mp4",
            "video/mpegts" => "video/mpegts",
            "video/x-msvideo" => "video/x-msvideo",
            "application/ogg" => "application/ogg",
            "video/x-flv" => "video/x-flv",

            _ => null
        };
    }
}
