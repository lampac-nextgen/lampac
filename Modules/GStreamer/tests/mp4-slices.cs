#:sdk Microsoft.NET.Sdk.Web
#:project ../GStreamer.csproj

// dotnet run --file Modules/GStreamer/tests/mp4-slices.cs
// Runs the compiled MP4 parser/slicer without loading native GStreamer.
using GStreamer;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;

int failed = 0;
foreach (bool cue in new[] { false, true })
{
    Check($"normal audio split (cue={cue})", () => Verify(cue, new[] { new[] { S(11, 0x21), S(10, 0x22) } }));
    // mp4mux's final AAC trun can contain a positive-duration packet followed by
    // a nonempty zero-duration packet. The latter lies beyond the final video.
    Check($"zero-duration EOS tail (cue={cue})", () => Verify(cue, new[] { new[] { S(11, 0x21), S(0, 0x22) } }));
    Check($"zero-size EOS tail (cue={cue})", () => Verify(cue, new[] { new[] { S(11, 0x21), new Sample(10, Array.Empty<byte>()) } }));
    // The tail fragment still has positive duration/size, but its first sliced
    // run has zero duration or size. Check that its samples/payload survive.
    Check($"zero-duration run inside retained tail (cue={cue})", () => VerifyRetained(cue, false));
    Check($"zero-size run inside retained tail (cue={cue})", () => VerifyRetained(cue, true));
}
Check("source trun with zero total duration still rejected", () => Reject(new[] { S(0, 0x21) }, "trun duration is zero"));
Check("source trun with zero total size still rejected", () => Reject(new[] { new Sample(11, Array.Empty<byte>()) }, "trun data size is zero"));
if (args.Length > 0)
{
    Check("captured mp4mux stream", () =>
    {
        var output = Read(File.ReadAllBytes(args[0]), false);
        Require(output.Segments.Count > 0, "no output segments");
        if (args.Length > 1)
        {
            Directory.CreateDirectory(args[1]);
            File.WriteAllBytes(Path.Combine(args[1], "init.mp4"), output.Init);
            var playlist = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:3\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-MAP:URI=\"init.mp4\"\n");
            using var joined = File.Create(Path.Combine(args[1], "joined.mp4"));
            joined.Write(output.Init);
            for (int i = 0; i < output.Segments.Count; i++)
            {
                var segment = output.Segments[i];
                File.WriteAllBytes(Path.Combine(args[1], $"{i}.m4s"), segment.Bytes);
                joined.Write(segment.Bytes);
                playlist.Append(System.FormattableString.Invariant($"#EXTINF:{(segment.End - segment.Start) / 1e9:F6},\n{i}.m4s\n"));
            }
            playlist.Append("#EXT-X-ENDLIST\n");
            File.WriteAllText(Path.Combine(args[1], "index.m3u8"), playlist.ToString());
        }
        Console.WriteLine($"Captured stream: {output.Segments.Count} segments, end={output.Segments[^1].End}ns");
    });
}
Console.WriteLine($"Failures: {failed}");
Environment.ExitCode = failed == 0 ? 0 : 1;

void Check(string name, Action action)
{
    try { action(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}

static void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static Sample S(uint duration, byte value) => new(duration, new[] { value });

static void Verify(bool cue, Sample[][] audio)
{
    var result = Read(Join(Init(), Fragment(1, 0, new[] { new[] { S(10, 0x11) } }), Fragment(2, 0, audio)), cue);
    Require(result.Segments.Count == 1, "EOS must emit exactly one AV segment, not an audio-only tail");
    var segment = result.Segments[0];
    Require(segment.Start == 0 && segment.End == 10_000_000, "video timeline changed");
    Require(Payload(segment.Bytes).SequenceEqual(new byte[] { 0x11, 0x21 }), "selected video/audio payload changed");
    var samples = AudioSamples(segment.Bytes);
    Require(samples.SequenceEqual(new[] { (11u, 1u) }), "selected audio duration/size changed");
}

static void VerifyRetained(bool cue, bool zeroSize)
{
    // Two valid source runs: the split creates a degenerate sub-run in the tail.
    Sample zero = zeroSize ? new Sample(1, Array.Empty<byte>()) : S(0, 0x22);
    var data = Join(Init(), Fragment(1, 0, new[] { new[] { S(2000, 0x11) } }),
        Fragment(1, 2000, new[] { new[] { S(2000, 0x12) } }),
        Fragment(2, 0, new[] { new[] { S(2001, 0x21), zero }, new[] { S(2000, 0x23) } }));
    var result = Read(data, cue);
    Require(result.Segments.Count == 2, "expected regular segment and EOS segment");
    Require(result.Segments[0].Start == 0 && result.Segments[0].End == 2_000_000_000 &&
        result.Segments[1].Start == 2_000_000_000 && result.Segments[1].End == 4_000_000_000, "video timeline changed");
    Require(Payload(result.Segments[0].Bytes).SequenceEqual(new byte[] { 0x11, 0x21 }), "head payload changed");
    byte[] tail = zeroSize ? new byte[] { 0x12, 0x23 } : new byte[] { 0x12, 0x22, 0x23 };
    Require(Payload(result.Segments[1].Bytes).SequenceEqual(tail), "retained tail payload lost/reordered");
    Require(AudioSamples(result.Segments[1].Bytes).SequenceEqual(new[] { (zero.Duration, (uint)zero.Bytes.Length), (2000u, 1u) }),
        "retained zero-valued sample duration/size changed");
}

static void Reject(Sample[] audio, string reason)
{
    try { Read(Join(Init(), Fragment(1, 0, new[] { new[] { S(10, 0x11) } }), Fragment(2, 0, new[] { audio })), false); }
    catch (System.IO.InvalidDataException e) when (e.Message.Contains(reason)) { return; }
    throw new InvalidOperationException("malformed source accepted");
}

static Output Read(byte[] data, bool cue)
{
    var output = new Output();
    using var reader = new Mp4BoxReader(init => output.Init = init.ToArray(), segment =>
    {
        using var stream = new MemoryStream();
        segment.WriteTo(stream);
        Require(stream.Length == segment.length, "reported segment length differs from bytes");
        output.Segments.Add(new(stream.ToArray(), segment.startNs, segment.endNs));
    }, 2, 0, cue);
    if (cue) reader.SetTargetSegment(0, 2_000_000_000, 0);
    // Push requires Gst.Buffer/native libs. Bind only the byte-input boundary;
    // parsing, splitting, payload copying and serialization are real code.
    var process = typeof(Mp4BoxReader).GetMethod("Process", BindingFlags.Instance | BindingFlags.NonPublic)!
        .CreateDelegate<ProcessBytes>(reader);
    int position = 0;
    while (position < data.Length)
    {
        int used = process(data.AsSpan(position), out bool completed);
        Require(used > 0 || completed, "parser made no progress");
        position += used;
        if (cue && completed)
        {
            ulong start = (ulong)output.Segments.Count * 2_000_000_000;
            reader.SetTargetSegment(start, start + 2_000_000_000, 200_000_000);
        }
    }
    while (reader.TryBuildEndOfStreamRemainder()) { }
    return output;
}

static byte[] Init()
{
    byte[] Track(uint id, string handler) => Box("trak", Box("tkhd", Words(0, 0, 0, id)),
        Box("mdia", Box("mdhd", Words(0, 0, 0, 1000)), Box("hdlr", Words(0, 0), Encoding.ASCII.GetBytes(handler))));
    return Box("moov", Track(1, "vide"), Track(2, "soun"), Box("mvex",
        Box("trex", Words(0, 1, 1, 0, 0, 0)), Box("trex", Words(0, 2, 1, 0, 0, 0))));
}

static byte[] Fragment(uint track, uint time, Sample[][] runs)
{
    byte[] Moof(uint offset)
    {
        var truns = new List<byte[]>();
        foreach (var run in runs)
        {
            var fields = new List<uint> { 0x301, (uint)run.Length, offset };
            foreach (var sample in run)
            {
                fields.Add(sample.Duration); fields.Add((uint)sample.Bytes.Length);
                offset += (uint)sample.Bytes.Length;
            }
            truns.Add(Box("trun", Words(fields.ToArray())));
        }
        return Box("moof", Box("mfhd", Words(0, 1)), Box("traf", Box("tfhd", Words(0x020000, track)),
            Box("tfdt", Words(0, time)), Join(truns.ToArray())));
    }
    var payload = runs.SelectMany(r => r).SelectMany(s => s.Bytes).ToArray();
    return Join(Moof((uint)Moof(0).Length + 8), Box("mdat", payload));
}

static byte[] Payload(byte[] segment) => Boxes(segment).Single(b => b.Type == "mdat").Data;

static (uint Duration, uint Size)[] AudioSamples(byte[] segment)
{
    var trafs = Boxes(Boxes(segment).Single(b => b.Type == "moof").Data).Where(b => b.Type == "traf");
    var audio = trafs.Single(t => BinaryPrimitives.ReadUInt32BigEndian(Boxes(t.Data).Single(b => b.Type == "tfhd").Data.AsSpan(4)) == 2);
    var samples = new List<(uint, uint)>();
    foreach (var trun in Boxes(audio.Data).Where(b => b.Type == "trun"))
    {
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(trun.Data);
        uint count = BinaryPrimitives.ReadUInt32BigEndian(trun.Data.AsSpan(4));
        int cursor = 8 + ((flags & 1) != 0 ? 4 : 0) + ((flags & 4) != 0 ? 4 : 0);
        for (int i = 0; i < count; i++)
        {
            uint duration = BinaryPrimitives.ReadUInt32BigEndian(trun.Data.AsSpan(cursor)); cursor += 4;
            uint size = BinaryPrimitives.ReadUInt32BigEndian(trun.Data.AsSpan(cursor)); cursor += 4;
            samples.Add((duration, size));
            if ((flags & 0x400) != 0) cursor += 4;
            if ((flags & 0x800) != 0) cursor += 4;
        }
    }
    return samples.ToArray();
}

static IEnumerable<(string Type, byte[] Data)> Boxes(byte[] data)
{
    for (int p = 0; p < data.Length;)
    {
        int size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p)));
        Require(size >= 8 && size <= data.Length - p, "invalid output box bounds");
        yield return (Encoding.ASCII.GetString(data, p + 4, 4), data.AsSpan(p + 8, size - 8).ToArray());
        p += size;
    }
}

static byte[] Words(params uint[] values)
{
    var bytes = new byte[values.Length * 4];
    for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
    return bytes;
}
static byte[] Box(string type, params byte[][] data)
{
    var bytes = Join(data); return Join(Words((uint)bytes.Length + 8), Encoding.ASCII.GetBytes(type), bytes);
}
static byte[] Join(params byte[][] bytes) => bytes.SelectMany(x => x).ToArray();

delegate int ProcessBytes(ReadOnlySpan<byte> data, out bool completed);
record Sample(uint Duration, byte[] Bytes);
record EncodedSegment(byte[] Bytes, ulong Start, ulong End);
class Output
{
    public byte[] Init = Array.Empty<byte>();
    public List<EncodedSegment> Segments = new();
}
