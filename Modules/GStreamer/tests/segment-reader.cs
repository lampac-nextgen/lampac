#:sdk Microsoft.NET.Sdk.Web
#:project ../GStreamer.csproj
#:property PublishAot=false

// .NET 10 self-check: dotnet run --file Modules/GStreamer/tests/segment-reader.cs
// Real reader decisions and Cue validation, with deterministic cache/seek/I/O doubles.
// Not a native GStreamer pipeline, deferred-buffer or concurrent-lifecycle integration test.
using GStreamer;
using GStreamer.Models;
using GStreamer.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

string originalDirectory = Environment.CurrentDirectory;
string directory = Path.Combine(Path.GetTempPath(), "gst-reader-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
Environment.CurrentDirectory = directory;
int failures = 0;
try
{
    Check("normal adjacent", () => Read(New(241), 242));
    Check("normal forward gap", () =>
    {
        var h = New(241);
        Read(h, 243);
        Require(h.SeekCount == 0, "unneeded seek");
    });
    Check("cache/seek sequence 242 -> 241 -> 243", () =>
    {
        var h = New(-1);
        Read(h, 242);
        Read(h, 241);
        Read(h, 243);
    });
    Check("cache/seek sequence 253 -> 252 -> 254", () =>
    {
        var h = New(-1);
        Read(h, 253);
        Read(h, 252);
        Read(h, 254);
    });
    Check("cached gap fallback seek failure", () =>
    {
        var h = New(241, 242);
        h.FailSeek = true;
        Require(!h.EnsureClientSegment(243, default), "seek failure ignored");
        Require(h.Outputs.Count == 0, "consumed after failed seek");
    });
    Check("cached next before missing requested", () => Read(New(241, 242), 243));
    Check("two cached intermediates", () => Read(New(241, 242, 243), 244));
    Check("cached middle after a real read", () => Read(New(240, 242), 243));
    Check("equal durations cannot silently mislabel content", () =>
    {
        var h = New(241, 242);
        h.EqualDurations = true;
        Read(h, 243);
    });
    Check("target cache hit leaves physical reader alone", () =>
    {
        var h = New(241, 243);
        Require(h.EnsureClientSegment(243, default), "cache not served");
        Require(h.ReaderIndex == 241 && h.PhysicalNext == 242, "cache advanced reader");
        Require(h.SeekCount == 0 && h.Outputs.Count == 0, "cache triggered I/O");
    });
    Check("cache hit then cache hole", () =>
    {
        var h = New(240, 241, 242);
        Require(h.EnsureClientSegment(242, default), "cache not served");
        Read(h, 243);
    });
    Check("backward missing seeks", () =>
    {
        var h = New(250);
        Read(h, 243);
        Require(h.SeekCount == 1, "missing seek");
    });
    Check("distant forward seeks", () =>
    {
        var h = New(230);
        Read(h, 243);
        Require(h.SeekCount == 1, "missing seek");
    });
    Check("failed seek returns false without reading", () =>
    {
        var h = New(250);
        h.FailSeek = true;
        Require(!h.EnsureClientSegment(243, default), "seek failure ignored");
        Require(h.Outputs.Count == 0, "read after seek failure");
    });
    Check("cancellation prevents forward read", () =>
    {
        var h = New(241);
        using var c = new CancellationTokenSource();
        c.Cancel();
        Require(!h.EnsureClientSegment(243, c.Token), "cancel ignored");
        Require(h.Outputs.Count == 0 && h.SeekCount == 0, "I/O after cancellation");
    });
    Check("cancellation during cached intermediate avoids seek", () =>
    {
        var h = New(241, 242);
        using var c = new CancellationTokenSource();
        h.CancelOnCacheHit = c;
        Require(!h.EnsureClientSegment(243, c.Token), "cancel ignored");
        Require(h.SeekCount == 0 && h.Outputs.Count == 0, "I/O after cancellation");
    });
    Check("invalid and ended requests", () =>
    {
        var h = New(241);
        Require(!h.EnsureClientSegment(-1, default), "negative index accepted");
        Require(!h.EnsureClientSegment(300, default), "past end accepted");
        Require(h.SeekCount == 0 && h.Outputs.Count == 0, "invalid request triggered I/O");
    });
    Check("death during cached intermediate avoids seek", () =>
    {
        var h = New(241, 242);
        h.DieOnCacheHit = true;
        Require(!h.EnsureClientSegment(243, default), "dead task continued");
        Require(h.SeekCount == 0 && h.Outputs.Count == 0, "I/O after task death");
    });
    Check("non-Cue forward cache hole", () =>
    {
        var h = new Harness(241, new[] { 242 }, cueMode: false);
        Read(h, 243);
    });
}
finally
{
    Environment.CurrentDirectory = originalDirectory;
    Directory.Delete(directory, recursive: true);
}
return failures == 0 ? 0 : 1;

void Check(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
static void Require(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}
static Harness New(int cursor, params int[] cached) => new(cursor, cached);
static void Read(Harness h, int index)
{
    Require(h.EnsureClientSegment(index, default), "returned false");
    Require(h.Outputs[index] == index, $"content from {h.Outputs[index]} mislabeled {index}");
    Require(h.ReaderIndex == index, "reader index incorrect");
}

sealed class Harness : GStask
{
    // Keep the simulated physical position independent of the requested index.
    public int PhysicalNext { get; private set; }
    public bool FailSeek, EqualDurations, DieOnCacheHit;
    public int SeekCount;
    public CancellationTokenSource? CancelOnCacheHit;
    public readonly Dictionary<int, int> Outputs = new();
    readonly HashSet<int> cache;
    static readonly FieldInfo cursor = Reflection.Field(typeof(GStask), "readerSegmentIndex");
    public int ReaderIndex
    {
        get => (int)(cursor.GetValue(this) ?? throw new InvalidOperationException("Missing reader index."));
        private set => cursor.SetValue(this, value);
    }

    public Harness(int readerIndex, int[] cached, bool cueMode = true)
        : base(new ProbeInfo(), new ModuleConf { segment_buffer = 6, segment_seconds = 6 },
            "https://media.example/video", 1, "reader-check", 0, null,
            cueMode ? new CueTimeline(Enumerable.Range(0, 300)
                .Select(i => new CueSegment((ulong)i * 20_000_000_000, (ulong)(i + 1) * 20_000_000_000))
                .ToArray(), 1_000_000) : null)
    {
        ReaderIndex = readerIndex;
        PhysicalNext = readerIndex + 1;
        cache = new(cached);
    }

    protected override bool SegmentFileReady(int index) => cache.Contains(index);
    protected override bool SeekSegment(int index)
    {
        SeekCount++;
        if (FailSeek) return false;
        PhysicalNext = index;
        return true;
    }
    ulong Duration(int index) => EqualDurations ? 10_386_000_000UL : index switch
    {
        242 => 10_427_000_000UL,
        243 => 10_385_000_000UL,
        253 => 1_918_000_000UL,
        _ => 10_386_000_000UL
    };
    public override bool EnsureSegment(int index, CancellationToken ct, int audio = 0)
    {
        if (cache.Contains(index))
        {
            CancelOnCacheHit?.Cancel();
            if (DieOnCacheHit) Dispose();
            return true;
        }
        if (ct.IsCancellationRequested || IsDead) return false;
        int consumed = cueTimeline == null ? 1 : CueCheck.Select(new[] { Duration(PhysicalNext),
            Duration(PhysicalNext + 1), Duration(PhysicalNext + 2) }, Duration(index));
        if (consumed == 0) return false;
        Outputs[index] = PhysicalNext;
        PhysicalNext += consumed;
        cache.Add(index);
        ReaderIndex = index;
        return true;
    }
}

static class Reflection
{
    public static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name);
    public static object Get(object value, string name) => Field(value.GetType(), name).GetValue(value)
        ?? throw new InvalidOperationException($"Null fixture field: {name}");
    public static void Set(object value, string name, object fieldValue) => Field(value.GetType(), name).SetValue(value, fieldValue);
}

static class CueCheck
{
    // Seed parsed fragments, then call the real Cue validator. Reflection is limited to
    // private fixture state; no source parsing, copied decisions or native Gst.Buffer needed.
    static readonly Type reader = typeof(Mp4BoxReader);
    static Type Nested(string name) => reader.GetNestedType(name, BindingFlags.NonPublic)
        ?? throw new TypeLoadException(name);
    static object Create(string name, params object[] args) => Activator.CreateInstance(Nested(name), args)
        ?? throw new InvalidOperationException($"Cannot create fixture: {name}");
    static readonly MethodInfo select = reader.GetMethod("SelectCueVideoCount", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(reader.FullName, "SelectCueVideoCount");

    public static int Select(ulong[] durations, ulong expected)
    {
        using var r = new Mp4BoxReader(_ => { }, _ => { }, 6, 0, cueMode: true);
        r.SetTargetSegment(0, expected, 1_000_000);
        Reflection.Set(r, "_videoTrack", Create("TrackInfo", 1U, 1_000_000_000U, Create("Trex", 0U, 0U, 0U, 0U)));
        var fragments = (IList)Reflection.Get(r, "_video");
        ulong pts = 0;
        foreach (ulong duration in durations.Prepend(0UL))
        {
            pts += duration;
            object fragment = Create("Fragment");
            Reflection.Set(fragment, "StartsWithSync", true);
            Reflection.Set(fragment, "DecodeTime", pts);
            object run = Create("Run", 1);
            ((IList)Reflection.Get(run, "Samples")).Add(Create("Sample", 1U, 1U, 0U, 0U));
            ((IList)Reflection.Get(fragment, "Runs")).Add(run);
            fragments.Add(fragment);
        }
        try { return (int)(select.Invoke(r, null) ?? throw new InvalidOperationException("No Cue result.")); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
