using Microsoft.IO;
using Shared.Services.Pools;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace GStreamer;

public readonly struct Segment
{
    readonly Action<Stream> _writeTo;

    public readonly long length;
    public readonly ulong startNs;
    public readonly ulong endNs;

    internal Segment(
        long length,
        ulong startNs,
        ulong endNs,
        Action<Stream> writeTo
    )
    {
        this.length = length;
        this.startNs = startNs;
        this.endNs = endNs;
        _writeTo = writeTo ?? throw new ArgumentNullException(nameof(writeTo));
    }

    public void WriteTo(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (_writeTo == null)
            throw new InvalidOperationException("Segment writer is not initialized.");

        _writeTo(output);
    }
}

/// <summary>
/// Собирает отдельные однодорожечные fragments mp4mux в один HLS fMP4 segment:
///
///     [styp/emsg/free]
///     moof
///         mfhd
///         traf video (один или несколько trun)
///         traf audio (один или несколько trun)
///     mdat
///         video payload
///         audio payload
/// </summary>
public sealed class Mp4BoxReader : IDisposable
{
    const uint BoxStyp = 0x73747970;
    const uint BoxSidx = 0x73696478;
    const uint BoxEmsg = 0x656D7367;
    const uint BoxFree = 0x66726565;
    const uint BoxPrft = 0x70726674;
    const uint BoxMoov = 0x6D6F6F76;
    const uint BoxMoof = 0x6D6F6F66;
    const uint BoxMdat = 0x6D646174;
    const uint BoxMfhd = 0x6D666864;
    const uint BoxTraf = 0x74726166;
    const uint BoxTfhd = 0x74666864;
    const uint BoxTfdt = 0x74666474;
    const uint BoxTrun = 0x7472756E;
    const uint BoxTrak = 0x7472616B;
    const uint BoxTkhd = 0x746B6864;
    const uint BoxMdia = 0x6D646961;
    const uint BoxMdhd = 0x6D646864;
    const uint BoxHdlr = 0x68646C72;
    const uint BoxMvex = 0x6D766578;
    const uint BoxTrex = 0x74726578;
    const uint BoxMfra = 0x6D667261;

    bool _sourceMfraDone;
    bool _sourceFinalMoovDone;

    const uint HandlerVideo = 0x76696465; // vide
    const uint HandlerAudio = 0x736F756E; // soun

    const uint TfhdBaseDataOffsetPresent = 0x000001;
    const uint TfhdSampleDescriptionIndexPresent = 0x000002;
    const uint TfhdDefaultSampleDurationPresent = 0x000008;
    const uint TfhdDefaultSampleSizePresent = 0x000010;
    const uint TfhdDefaultSampleFlagsPresent = 0x000020;
    const uint TfhdDefaultBaseIsMoof = 0x020000;

    const uint TrunDataOffsetPresent = 0x000001;
    const uint TrunFirstSampleFlagsPresent = 0x000004;
    const uint TrunSampleDurationPresent = 0x000100;
    const uint TrunSampleSizePresent = 0x000200;
    const uint TrunSampleFlagsPresent = 0x000400;
    const uint TrunCompositionOffsetPresent = 0x000800;

    const ulong GstSecond = 1_000_000_000UL;
    const uint MaxRunSamplePreallocation = 4096;
    const int VideoPayloadBlockSize = 16 * 1024;
    const int AudioPayloadBlockSize = 4 * 1024;

    static readonly RecyclableMemoryStreamManager VideoPayloadPool =
        CreatePayloadPool(VideoPayloadBlockSize, 42L * 1024 * 1024);

    static readonly RecyclableMemoryStreamManager AudioPayloadPool =
        CreatePayloadPool(AudioPayloadBlockSize, 2L * 1024 * 1024);

    static RecyclableMemoryStreamManager CreatePayloadPool(
        int blockSize,
        long maximumSmallPoolFreeBytes
    )
    {
        return new RecyclableMemoryStreamManager(
            new RecyclableMemoryStreamManager.Options(
                blockSize,
                1024 * 1024,
                10 * 1024 * 1024,
                maximumSmallPoolFreeBytes,
                0
            )
            {
                AggressiveBufferReturn = false
            }
        );
    }

    readonly Action<ReadOnlyMemory<byte>> _onInit;
    readonly Action<Segment> _onSegment;
    readonly int _segmentSeconds;
    readonly int _segmentDiff;
    readonly bool _cueMode;

    readonly MemoryStream _init = new();
    readonly MemoryStream _sourceMoof = new(16 * 1024);
    readonly MemoryStream _sourceStyp = new(128);
    readonly MemoryStream _deferred = new(64 * 1024);

    readonly List<Fragment> _video = new();
    readonly List<Fragment> _audio = new();

    readonly byte[] _readBuffer = new byte[64 * 1024];
    readonly byte[] _boxHeader = new byte[16];

    RecyclableMemoryStream _sourcePayload;
    RecyclableMemoryStream _prefix;

    Fragment _pending;
    byte[] _styp;

    int _headerLength;
    int _headerRequired = 8;
    uint _boxType;
    ulong _boxRemaining;
    Target _target;

    bool _initDone;
    bool _moovDone;
    long _sourcePayloadFromMoof;
    int _deferredStart;

    TrackInfo _videoTrack;
    TrackInfo _audioTrack;

    uint _videoSampleDurationHint;
    uint _videoSampleDurationHintTimescale;
    uint _audioSampleDurationHint;
    uint _audioSampleDurationHintTimescale;

    ulong _tfdtOffsetNs;
    uint _sequence = 1;

    ulong _lastVideoEndTime;
    int _completedVideoSegmentsCount;

    bool _hasTargetSegment;
    ulong _targetSegmentStartNs;
    ulong _targetSegmentEndNs;
    ulong _targetToleranceNs;

    enum Target
    {
        None,
        Init,
        Moof,
        Payload,
        Styp,
        Prefix
    }

    readonly record struct Trex(
        uint DescriptionIndex,
        uint Duration,
        uint Size,
        uint Flags
    );

    readonly record struct TrackInfo(
        uint Id,
        uint Timescale,
        Trex Trex
    );

    readonly record struct TrexEntry(uint TrackId, Trex Value);

    readonly record struct Sample(
        uint Duration,
        uint Size,
        uint Flags,
        uint CompositionOffset
    );

    sealed class Run
    {
        public Run(int sampleCapacity)
        {
            Samples = new List<Sample>(sampleCapacity);
        }

        public byte Version;
        public bool HasCompositionOffset;
        public bool HasInferredDuration;
        public int? SourceDataOffset;
        public ulong Duration;
        public ulong DataSize;
        public long PayloadOffset;
        public long OutputOffset;
        public bool StartsWithSync;
        public readonly List<Sample> Samples;

        public int SampleCount => Samples.Count;
    }

    sealed class Fragment : IDisposable
    {
        public uint TrackId;
        public uint Timescale;
        public uint SampleDescriptionIndex;
        public ulong DecodeTime;
        public ulong Duration;
        public bool StartsWithSync;
        public bool HasInferredDuration;
        public uint SampleDescriptionIndexOverride;
        public readonly List<Run> Runs = new();
        public RecyclableMemoryStream Payload;

        public ulong EndTime => checked(DecodeTime + Duration);

        public int SampleCount
        {
            get
            {
                int count = 0;

                foreach (Run run in Runs)
                    count = checked(count + run.SampleCount);

                return count;
            }
        }

        public void Dispose()
        {
            Payload?.Dispose();
            Payload = null;
        }
    }

    public Mp4BoxReader(
        Action<ReadOnlyMemory<byte>> onInit,
        Action<Segment> onSegment,
        int segmentSeconds,
        int segmentDiff,
        bool cueMode = false
    )
    {
        _onInit = onInit ?? throw new ArgumentNullException(nameof(onInit));
        _onSegment = onSegment ?? throw new ArgumentNullException(nameof(onSegment));

        if (segmentSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segmentSeconds),
                segmentSeconds,
                "Segment duration must be greater than zero."
            );
        }

        _segmentSeconds = segmentSeconds;
        _segmentDiff = segmentDiff;
        _cueMode = cueMode;
    }

    public void SeekReset()
    {
        SeekReset(0UL);
    }

    public void SeekReset(ulong offsetNs)
    {
        _initDone = false;
        _moovDone = false;
        _sourceMfraDone = false;
        _sourceFinalMoovDone = false;
        _videoTrack = default;
        _audioTrack = default;
        _tfdtOffsetNs = offsetNs == ulong.MaxValue ? 0 : offsetNs;
        _sequence = 1;
        _styp = null;

        _lastVideoEndTime = 0;
        _completedVideoSegmentsCount = 0;

        _hasTargetSegment = false;
        _targetSegmentStartNs = 0;
        _targetSegmentEndNs = 0;
        _targetToleranceNs = 0;

        Reset(_init);
        Reset(_sourceMoof);
        Reset(_sourceStyp);
        ResetDeferred();

        ClearSource();
        ClearFragments(_video);
        ClearFragments(_audio);
        ResetPrefix();
        ResetBox();
    }

    public void SetTimelineOffsetNs(ulong offsetNs)
    {
        _tfdtOffsetNs = offsetNs == ulong.MaxValue
            ? 0
            : offsetNs;
    }

    public void SetTargetSegment(ulong startNs, ulong endNs, ulong toleranceNs)
    {
        if (!_cueMode)
            return;

        if (endNs <= startNs)
            throw new ArgumentOutOfRangeException(nameof(endNs));

        _targetSegmentStartNs = startNs;
        _targetSegmentEndNs = endNs;
        _targetToleranceNs = toleranceNs;
        _hasTargetSegment = true;
    }

    public void Push(Gst.Buffer buffer, int size)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (size <= 0)
            return;

        if (TryProcessDeferred())
        {
            AppendGstBufferToDeferred(buffer, 0, size);
            return;
        }

        int sourceOffset = 0;

        while (sourceOffset < size)
        {
            int requested = Math.Min(_readBuffer.Length, size - sourceOffset);
            int copied = (int)buffer.Extract(
                (nuint)sourceOffset,
                _readBuffer.AsSpan(0, requested)
            );

            if (copied <= 0)
                return;

            int consumed = Process(
                _readBuffer.AsSpan(0, copied),
                out bool completed
            );

            sourceOffset += copied;

            if (!completed)
                continue;

            if (consumed < copied)
                AppendDeferred(_readBuffer.AsSpan(consumed, copied - consumed));

            if (sourceOffset < size)
                AppendGstBufferToDeferred(buffer, sourceOffset, size - sourceOffset);

            return;
        }
    }

    public bool TryProcessDeferred()
    {
        if (TryBuildSegment())
            return true;

        int deferredEnd = checked((int)_deferred.Length);

        if ((uint)_deferredStart > (uint)deferredEnd)
            throw new InvalidOperationException("Deferred buffer range is invalid.");

        int length = deferredEnd - _deferredStart;

        if (length == 0)
        {
            ResetDeferred();
            return false;
        }

        ReadOnlySpan<byte> data;
        byte[] copy = null;

        if (_deferred.TryGetBuffer(out ArraySegment<byte> segment) && segment.Array != null)
        {
            data = segment.Array.AsSpan(segment.Offset + _deferredStart, length);
        }
        else
        {
            copy = _deferred.ToArray();
            data = copy.AsSpan(_deferredStart, length);
        }

        int consumed = Process(data, out bool completed);

        if (completed)
        {
            KeepDeferred(length, consumed);
            return true;
        }

        if (consumed != length)
        {
            throw new InvalidOperationException(
                $"MP4 parser consumed {consumed} of {length} deferred bytes."
            );
        }

        ResetDeferred();
        return false;
    }

    public bool TryBuildEndOfStreamRemainder()
    {
        if (_cueMode && !_hasTargetSegment)
            return false;

        int videoCount = _video.Count;
        int audioCount = _audio.Count;

        if (videoCount == 0 && audioCount == 0)
            return false;

        if (videoCount > 0 && !_video[0].StartsWithSync)
            return false;

        // Для финального AV-хвоста также стараемся не уносить audio далеко за videoEnd.
        // Если после split остаётся audio-only tail, он очищается после выдачи последнего
        // video segment и не превращается в отдельный HLS segment без видео.
        bool finalVideoSegment = videoCount > 0;

        if (videoCount > 0 && audioCount > 0)
        {
            ulong videoEnd = _video[videoCount - 1].EndTime;
            TryPrepareAudioForVideoEnd(videoEnd, out audioCount);
        }

        BuildSegment(
            videoCount,
            audioCount,
            allowSingleTrack: true
        );

        if (finalVideoSegment && _video.Count == 0 && _audio.Count > 0)
            ClearFragments(_audio);

        return true;
    }

    int Process(ReadOnlySpan<byte> data, out bool segmentCompleted)
    {
        segmentCompleted = false;
        int position = 0;

        while (position < data.Length)
        {
            if (_headerLength < _headerRequired)
            {
                int count = Math.Min(
                    _headerRequired - _headerLength,
                    data.Length - position
                );

                data.Slice(position, count).CopyTo(
                    _boxHeader.AsSpan(_headerLength, count)
                );

                _headerLength += count;
                position += count;

                if (_headerLength < _headerRequired)
                    break;

                if (_headerRequired == 8)
                {
                    uint size32 = BinaryPrimitives.ReadUInt32BigEndian(
                        _boxHeader.AsSpan(0, 4)
                    );

                    _boxType = BinaryPrimitives.ReadUInt32BigEndian(
                        _boxHeader.AsSpan(4, 4)
                    );

                    if (size32 == 1)
                    {
                        _headerRequired = 16;
                        continue;
                    }

                    if (size32 == 0)
                        throw new NotSupportedException("Top-level box size=0 is not supported.");

                    BeginBox(size32, 8);
                }
                else
                {
                    BeginBox(
                        BinaryPrimitives.ReadUInt64BigEndian(_boxHeader.AsSpan(8, 8)),
                        16
                    );
                }

                if (_boxRemaining == 0)
                {
                    bool ready = CompleteBox();
                    ResetBox();

                    if (ready)
                    {
                        segmentCompleted = true;
                        break;
                    }
                }

                continue;
            }

            int countBody = (int)Math.Min(
                (ulong)(data.Length - position),
                _boxRemaining
            );

            if (countBody <= 0)
                break;

            Write(data.Slice(position, countBody));
            position += countBody;
            _boxRemaining -= (ulong)countBody;

            if (_boxRemaining != 0)
                continue;

            bool completed = CompleteBox();
            ResetBox();

            if (completed)
            {
                segmentCompleted = true;
                break;
            }
        }

        return position;
    }

    void BeginBox(ulong size, int headerSize)
    {
        if (size < (ulong)headerSize)
            throw new InvalidDataException("Invalid MP4 box size.");

        if ((_boxType == BoxMoof || _boxType == BoxMdat) && size > int.MaxValue)
            throw new InvalidDataException("moof/mdat is too large.");

        _boxRemaining = size - (ulong)headerSize;
        _target = Target.None;

        if (!_initDone && (_boxType == BoxStyp || _boxType == BoxMoof))
            CompleteInit();

        if (!_initDone)
        {
            if (_boxType == BoxMdat)
            {
                throw new InvalidDataException(
                    "mdat appeared before init was completed."
                );
            }

            if (_boxType == BoxMfra)
            {
                throw new InvalidDataException(
                    "mfra appeared before init was completed."
                );
            }

            _target = Target.Init;
            Write(_boxHeader.AsSpan(0, headerSize));
            return;
        }

        // После rewritten terminal moov никаких новых MP4 boxes
        // в append-only представлении appsink уже быть не должно
        if (_sourceFinalMoovDone)
        {
            throw new InvalidDataException(
                $"Unexpected top-level MP4 box after terminal moov: " +
                $"{FourCC(_boxType)}."
            );
        }

        if (_sourceMfraDone && _boxType != BoxMoov)
        {
            throw new InvalidDataException(
                $"Only the rewritten terminal moov is allowed after mfra; " +
                $"got {FourCC(_boxType)}."
            );
        }

        switch (_boxType)
        {
            case BoxMoof:
                {
                    if (_pending != null)
                    {
                        throw new InvalidDataException(
                            "A new moof appeared before the previous mdat."
                        );
                    }

                    Reset(_sourceMoof);
                    _sourcePayloadFromMoof = 0;

                    _target = Target.Moof;
                    Write(_boxHeader.AsSpan(0, headerSize));
                    return;
                }

            case BoxMdat:
                {
                    if (_pending == null)
                    {
                        throw new InvalidDataException(
                            "mdat does not follow a supported moof."
                        );
                    }

                    _sourcePayload?.Dispose();
                    _sourcePayload = CreatePayloadStream(_pending.TrackId);

                    _sourcePayloadFromMoof = checked(
                        _sourcePayloadFromMoof + headerSize
                    );

                    _target = Target.Payload;
                    return;
                }

            case BoxSidx:
                {
                    // Source sidx offsets become invalid after fragment merging
                    // Не сохраняем box, но учитываем его размер, если он почему-то расположен между moof и mdat
                    if (_pending != null)
                    {
                        _sourcePayloadFromMoof = checked(
                            _sourcePayloadFromMoof + (long)size
                        );
                    }

                    return;
                }

            case BoxStyp:
                {
                    if (_pending != null)
                    {
                        throw new InvalidDataException(
                            "styp cannot appear between moof and mdat."
                        );
                    }

                    Reset(_sourceStyp);

                    _target = Target.Styp;
                    Write(_boxHeader.AsSpan(0, headerSize));
                    return;
                }

            case BoxEmsg:
            case BoxFree:
            case BoxPrft:
                {
                    if (_pending != null)
                    {
                        _sourcePayloadFromMoof = checked(
                            _sourcePayloadFromMoof + (long)size
                        );
                    }

                    EnsurePrefix();

                    _target = Target.Prefix;
                    Write(_boxHeader.AsSpan(0, headerSize));
                    return;
                }

            case BoxMfra:
                {
                    if (_pending != null)
                    {
                        throw new InvalidDataException(
                            "mfra cannot appear between moof and mdat."
                        );
                    }

                    if (_sourceMfraDone)
                        throw new InvalidDataException("Duplicate terminal mfra.");

                    // mfra содержит file-level index с абсолютными offsets исходного GStreamer MP4-потока
                    // После объединения и пересегментации offsets недействительны
                    //
                    // Target.None: тело box будет полностью прочитано и отброшено
                    return;
                }

            case BoxMoov:
                {
                    if (!_sourceMfraDone)
                    {
                        throw new InvalidDataException(
                            "Unexpected moov after init."
                        );
                    }

                    if (_sourceFinalMoovDone)
                    {
                        throw new InvalidDataException(
                            "Duplicate terminal moov."
                        );
                    }

                    if (_pending != null)
                    {
                        throw new InvalidDataException(
                            "Final moov cannot appear between moof and mdat."
                        );
                    }

                    // Это rewritten moov от non-streamable mp4mux
                    // Исходный init segment уже передан через _onInit
                    // Повторный moov для HLS segments не нужен
                    //
                    // Target.None: полностью прочитать и отбросить
                    return;
                }

            default:
                {
                    throw new InvalidDataException(
                        $"Unsupported top-level MP4 box after init: " +
                        $"{FourCC(_boxType)}."
                    );
                }
        }
    }

    void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        switch (_target)
        {
            case Target.Init:
                _init.Write(data);
                break;
            case Target.Moof:
                _sourceMoof.Write(data);
                break;
            case Target.Payload:
                _sourcePayload.Write(data);
                break;
            case Target.Styp:
                _sourceStyp.Write(data);
                break;
            case Target.Prefix:
                _prefix.Write(data);
                break;
        }
    }

    bool CompleteBox()
    {
        switch (_boxType)
        {
            case BoxStyp:
                if (_styp == null && _sourceStyp.Length > 0)
                    _styp = _sourceStyp.ToArray();

                Reset(_sourceStyp);
                return false;

            case BoxMfra:
                _sourceMfraDone = true;
                return false;

            case BoxMoov:
                if (_initDone)
                {
                    if (!_sourceMfraDone || _sourceFinalMoovDone)
                        throw new InvalidDataException("Unexpected moov after init.");

                    _sourceFinalMoovDone = true;
                    return false;
                }

                _moovDone = true;
                return false;

            case BoxMoof:
                CompleteMoof();
                return false;

            case BoxMdat:
                CompleteMdat();
                return TryBuildSegment();

            default:
                return false;
        }
    }

    void CompleteInit()
    {
        if (!_moovDone || _init.Length == 0)
            throw new InvalidDataException("Incomplete MP4 initialization.");

        if (!_init.TryGetBuffer(out ArraySegment<byte> buffer) || buffer.Array == null)
            throw new InvalidOperationException("MP4 init buffer is not accessible.");

        var init = new ReadOnlyMemory<byte>(
            buffer.Array,
            buffer.Offset,
            checked((int)_init.Length)
        );

        if (!TryParseInit(
            init.Span,
            out _videoTrack,
            out _audioTrack,
            out string error
        ))
        {
            throw new InvalidDataException(
                $"Unable to parse MP4 initialization: {error}"
            );
        }

        _initDone = true;

        try
        {
            _onInit(init);
        }
        finally
        {
            Reset(_init);
            _init.Capacity = 0;
        }
    }

    void CompleteMoof()
    {
        ReadOnlySpan<byte> moof = GetSpan(_sourceMoof);

        if (!TryParseMoof(
            moof,
            _videoTrack,
            _audioTrack,
            _videoSampleDurationHintTimescale == _videoTrack.Timescale
                ? _videoSampleDurationHint
                : 0,
            _audioSampleDurationHintTimescale == _audioTrack.Timescale
                ? _audioSampleDurationHint
                : 0,
            out Fragment fragment,
            out string error
        ))
        {
            throw new InvalidDataException($"Unable to parse source moof: {error}");
        }

        ResolvePreviousInferredDuration(fragment);

        uint durationHint = fragment.HasInferredDuration
            ? 0
            : LastSampleDuration(fragment);

        if (durationHint != 0)
        {
            if (fragment.TrackId == _videoTrack.Id)
            {
                _videoSampleDurationHint = durationHint;
                _videoSampleDurationHintTimescale = fragment.Timescale;
            }
            else if (fragment.TrackId == _audioTrack.Id)
            {
                _audioSampleDurationHint = durationHint;
                _audioSampleDurationHintTimescale = fragment.Timescale;
            }
        }

        _pending = fragment;
        _sourcePayloadFromMoof = _sourceMoof.Length;
    }

    void ResolvePreviousInferredDuration(Fragment current)
    {
        List<Fragment> fragments = current.TrackId == _videoTrack.Id
            ? _video
            : current.TrackId == _audioTrack.Id
                ? _audio
                : null;

        if (fragments == null || fragments.Count == 0)
            return;

        Fragment previous = fragments[^1];
        if (!previous.HasInferredDuration)
            return;

        Run inferredRun = null;

        for (int i = previous.Runs.Count - 1; i >= 0; i--)
        {
            if (previous.Runs[i].HasInferredDuration)
            {
                inferredRun = previous.Runs[i];
                break;
            }
        }

        if (inferredRun == null || inferredRun.Samples.Count == 0)
            throw new InvalidDataException("Inferred sample duration marker is missing.");

        int sampleIndex = inferredRun.Samples.Count - 1;
        Sample sample = inferredRun.Samples[sampleIndex];
        ulong durationBeforeSample = previous.Duration - sample.Duration;
        ulong sampleStart = checked(previous.DecodeTime + durationBeforeSample);

        if (current.DecodeTime < sampleStart)
        {
            throw new InvalidDataException(
                $"Inferred sample moves decode time backwards: " +
                $"track={current.TrackId}, previous_tfdt={previous.DecodeTime}, " +
                $"previous_duration={previous.Duration}, sample_duration={sample.Duration}, " +
                $"sample_start={sampleStart}, next_tfdt={current.DecodeTime}, " +
                $"runs={previous.Runs.Count}, samples={previous.SampleCount}."
            );
        }

        ulong exactDuration = current.DecodeTime - sampleStart;
        if (exactDuration > uint.MaxValue)
            throw new InvalidDataException("Inferred sample duration exceeds UInt32.");

        uint exactDuration32 = (uint)exactDuration;
        inferredRun.Samples[sampleIndex] = sample with { Duration = exactDuration32 };
        inferredRun.Duration = checked(inferredRun.Duration - sample.Duration + exactDuration);
        previous.Duration = checked(previous.Duration - sample.Duration + exactDuration);

        foreach (Run run in previous.Runs)
            run.HasInferredDuration = false;

        previous.HasInferredDuration = false;

        if (exactDuration32 != 0 && current.TrackId == _videoTrack.Id)
        {
            _videoSampleDurationHint = exactDuration32;
            _videoSampleDurationHintTimescale = current.Timescale;
        }
        else if (exactDuration32 != 0)
        {
            _audioSampleDurationHint = exactDuration32;
            _audioSampleDurationHintTimescale = current.Timescale;
        }
    }

    static uint LastSampleDuration(Fragment fragment)
    {
        for (int runIndex = fragment.Runs.Count - 1; runIndex >= 0; runIndex--)
        {
            List<Sample> samples = fragment.Runs[runIndex].Samples;

            for (int sampleIndex = samples.Count - 1; sampleIndex >= 0; sampleIndex--)
            {
                uint duration = samples[sampleIndex].Duration;
                if (duration != 0)
                    return duration;
            }
        }

        return 0;
    }

    void CompleteMdat()
    {
        if (_pending == null || _sourcePayload == null)
            throw new InvalidDataException("Completed mdat has no source moof.");

        AttachPayload(
            _pending,
            _sourcePayload,
            _sourcePayloadFromMoof
        );

        _sourcePayload = null; // ownership moved to fragment

        if (_pending.TrackId == _videoTrack.Id)
            _video.Add(_pending);
        else if (_pending.TrackId == _audioTrack.Id)
            _audio.Add(_pending);
        else
            throw new InvalidDataException($"Unsupported track_ID={_pending.TrackId}.");

        _pending = null;
        _sourcePayloadFromMoof = 0;
        Reset(_sourceMoof);
    }

    RecyclableMemoryStream CreatePayloadStream(uint trackId)
    {
        if (trackId == _videoTrack.Id)
            return VideoPayloadPool.GetStream();

        if (trackId == _audioTrack.Id)
            return AudioPayloadPool.GetStream();

        return PoolInvk.msm.GetStream();
    }

    static void AttachPayload(
        Fragment fragment,
        RecyclableMemoryStream payload,
        long payloadFromMoof
    )
    {
        long expected = 0;

        foreach (Run run in fragment.Runs)
        {
            long offset = run.SourceDataOffset.HasValue
                ? checked((long)run.SourceDataOffset.Value - payloadFromMoof)
                : expected;

            if (offset != expected)
            {
                throw new InvalidDataException(
                    $"Non-contiguous source mdat: expected={expected}, actual={offset}."
                );
            }

            if (run.DataSize > long.MaxValue)
                throw new InvalidDataException("trun payload is too large.");

            run.PayloadOffset = offset;
            expected = checked(offset + (long)run.DataSize);
        }

        if (expected != payload.Length)
        {
            throw new InvalidDataException(
                $"Source mdat size mismatch: trun={expected}, mdat={payload.Length}."
            );
        }

        payload.Position = 0;
        fragment.Payload = payload;
    }

    bool TryBuildSegment()
    {
        int videoCount = SelectVideoCount();
        if (videoCount == 0)
            return false;

        ulong videoEnd = _video[videoCount - 1].EndTime;

        if (!TryPrepareAudioForVideoEnd(videoEnd, out int audioCount))
            return false;

        BuildSegment(videoCount, audioCount);
        return true;
    }

    double _debugLastDiff;

    int SelectVideoCount()
    {
        if (_video.Count == 0)
            return 0;

        if (!_video[0].StartsWithSync)
        {
            throw new InvalidDataException(
                $"Video segment starts with a non-sync sample at " +
                $"{(double)_video[0].DecodeTime / _videoTrack.Timescale:F6}s."
            );
        }

        if (_cueMode)
            return SelectCueVideoCount();

        ulong target = ToUnits(_segmentSeconds, _videoTrack.Timescale);
        bool takeFirstSyncBoundary = false;

        if (_completedVideoSegmentsCount > 0 && _segmentDiff > 0)
        {
            // ожидаемая позиция видео сегмента браузером
            ulong expectedEnd = checked(
                (ulong)_completedVideoSegmentsCount *
                (ulong)_segmentSeconds *
                _videoTrack.Timescale
            );

            // на сколько реальная позиция видео сегмента должна быть выше expectedEnd
            ulong diff = checked(
                (ulong)(_segmentSeconds + _segmentDiff) *
                _videoTrack.Timescale
            );

            // реальная позиция видео сегмента выше expectedEnd
            if (_lastVideoEndTime > expectedEnd)
            {
                ulong ahead = _lastVideoEndTime - expectedEnd;

                if (ahead >= diff)
                {
                    takeFirstSyncBoundary = true;

                    if (ModInit.conf.debugType == "mp4box-diff")
                    {
                        double lastDiff = (double)(ahead - diff) / _videoTrack.Timescale;

                        if (lastDiff != _debugLastDiff)
                        {
                            _debugLastDiff = lastDiff;
                            Console.WriteLine($"diff: {lastDiff:F3}s");
                        }
                    }
                }
            }
        }

        ulong duration = 0;
        int selectedCount = 0;

        // Нужен один fragment look-ahead:
        // следующий сегмент должен начинаться с sync sample
        for (int i = 0; i + 1 < _video.Count; i++)
        {
            duration = checked(duration + _video[i].Duration);

            if (!takeFirstSyncBoundary)
            {
                if (duration >= target && _video[i + 1].StartsWithSync)
                    return i + 1;

                continue;
            }

            if (duration <= target)
            {
                if (_video[i + 1].StartsWithSync)
                {
                    selectedCount = i + 1;

                    if (duration == target)
                        return selectedCount;
                }

                continue;
            }

            // Есть допустимая граница <= target
            if (selectedCount > 0)
                return selectedCount;

            // До target sync-границ не было:
            // ждём первую допустимую границу выше target
            if (_video[i + 1].StartsWithSync)
                return i + 1;
        }

        return 0;
    }

    int SelectCueVideoCount()
    {
        if (!_hasTargetSegment || _video.Count < 2)
            return 0;

        long firstPresentationTime = FirstPresentationTime(_video[0]);
        ulong targetDurationNs = _targetSegmentEndNs - _targetSegmentStartNs;

        for (int i = 1; i < _video.Count; i++)
        {
            Fragment boundary = _video[i];

            if (!boundary.StartsWithSync)
                continue;

            long boundaryPresentationTime = FirstPresentationTime(boundary);

            if (boundaryPresentationTime <= firstPresentationTime)
                throw new InvalidDataException("Cue presentation timeline is not increasing.");

            ulong durationNs = ToNanoseconds(
                checked((ulong)(boundaryPresentationTime - firstPresentationTime)),
                _videoTrack.Timescale
            );

            if (durationNs < targetDurationNs &&
                targetDurationNs - durationNs > _targetToleranceNs)
            {
                continue;
            }

            if (durationNs > targetDurationNs &&
                durationNs - targetDurationNs > _targetToleranceNs)
            {
                throw new InvalidDataException(
                    $"Cue sync boundary duration is {durationNs}, " +
                    $"expected {targetDurationNs}."
                );
            }

            return i;
        }

        return 0;
    }

    static long FirstPresentationTime(Fragment fragment)
    {
        if (fragment.Runs.Count == 0 || fragment.Runs[0].Samples.Count == 0)
            throw new InvalidDataException("Video fragment has no first sample.");

        Run run = fragment.Runs[0];
        Sample sample = run.Samples[0];
        long compositionOffset = run.Version == 1
            ? unchecked((int)sample.CompositionOffset)
            : sample.CompositionOffset;

        return checked((long)fragment.DecodeTime + compositionOffset);
    }

    bool TryPrepareAudioForVideoEnd(ulong videoEnd, out int audioCount)
    {
        audioCount = 0;

        if (_audio.Count == 0)
            return false;

        ulong targetAudioEnd = ConvertTimeCeiling(
            videoEnd,
            _videoTrack.Timescale,
            _audioTrack.Timescale
        );

        for (int i = 0; i < _audio.Count; i++)
        {
            Fragment fragment = _audio[i];

            if (fragment.EndTime < targetAudioEnd)
            {
                audioCount = i + 1;
                continue;
            }

            int samplesToKeep = CountSamplesCovering(fragment, targetAudioEnd);

            if (samplesToKeep <= 0)
            {
                audioCount = i;
                return audioCount > 0;
            }

            int totalSamples = fragment.SampleCount;

            if (samplesToKeep >= totalSamples)
            {
                audioCount = i + 1;
                return true;
            }

            SplitFragment(_audio, i, samplesToKeep);
            audioCount = i + 1;
            return true;
        }

        return false;
    }

    static int CountSamplesCovering(Fragment fragment, ulong targetDecodeTime)
    {
        if (targetDecodeTime <= fragment.DecodeTime)
            return 0;

        ulong cursor = fragment.DecodeTime;
        int count = 0;

        foreach (Run run in fragment.Runs)
        {
            foreach (Sample sample in run.Samples)
            {
                cursor = checked(cursor + sample.Duration);
                count++;

                if (cursor >= targetDecodeTime)
                    return count;
            }
        }

        return count;
    }

    void SplitFragment(List<Fragment> fragments, int index, int firstSampleCount)
    {
        Fragment source = fragments[index];
        int totalSamples = source.SampleCount;

        if (firstSampleCount <= 0 || firstSampleCount >= totalSamples)
            return;

        Fragment head = null;
        Fragment tail = null;

        try
        {
            head = SliceFragment(source, 0, firstSampleCount);
            tail = SliceFragment(source, firstSampleCount, totalSamples - firstSampleCount);
        }
        catch
        {
            head?.Dispose();
            tail?.Dispose();
            throw;
        }

        source.Dispose();
        fragments[index] = head;
        fragments.Insert(index + 1, tail);
    }

    Fragment SliceFragment(Fragment source, int startSample, int sampleCount)
    {
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        ulong decodeTime = GetDecodeTimeForSample(source, startSample);

        var result = new Fragment
        {
            TrackId = source.TrackId,
            Timescale = source.Timescale,
            SampleDescriptionIndex = source.SampleDescriptionIndex,
            DecodeTime = decodeTime,
            SampleDescriptionIndexOverride = source.SampleDescriptionIndexOverride,
            Payload = CreatePayloadStream(source.TrackId)
        };

        try
        {
            int endSample = checked(startSample + sampleCount);
            int globalSample = 0;

            foreach (Run run in source.Runs)
            {
                int runStart = globalSample;
                int runEnd = checked(runStart + run.SampleCount);

                int sliceStart = Math.Max(startSample, runStart);
                int sliceEnd = Math.Min(endSample, runEnd);

                if (sliceStart < sliceEnd)
                {
                    int localStart = sliceStart - runStart;
                    int localCount = sliceEnd - sliceStart;
                    ulong sourceOffsetInRun = SumSampleSizes(run.Samples, 0, localStart);
                    ulong payloadLength = SumSampleSizes(run.Samples, localStart, localCount);

                    if (sourceOffsetInRun > long.MaxValue || payloadLength > long.MaxValue)
                        throw new InvalidDataException("Audio split payload is too large.");

                    long sourceOffset = checked(
                        run.PayloadOffset + (long)sourceOffsetInRun
                    );

                    long outputOffset = result.Payload.Length;
                    Run slicedRun = CloneRunSlice(run, localStart, localCount, outputOffset);

                    CopyPayloadRange(
                        source.Payload,
                        sourceOffset,
                        payloadLength,
                        result.Payload
                    );

                    result.Duration = checked(result.Duration + slicedRun.Duration);
                    result.Runs.Add(slicedRun);
                }

                globalSample = runEnd;
            }

            if (result.Runs.Count == 0 || result.Duration == 0 || result.Payload.Length == 0)
                throw new InvalidDataException("Audio split produced an empty fragment.");

            result.StartsWithSync = result.Runs[0].StartsWithSync;
            result.Payload.Position = 0;
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    static ulong GetDecodeTimeForSample(Fragment fragment, int sampleIndex)
    {
        if (sampleIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleIndex));

        ulong decodeTime = fragment.DecodeTime;
        int remaining = sampleIndex;

        foreach (Run run in fragment.Runs)
        {
            foreach (Sample sample in run.Samples)
            {
                if (remaining == 0)
                    return decodeTime;

                decodeTime = checked(decodeTime + sample.Duration);
                remaining--;
            }
        }

        if (remaining == 0)
            return decodeTime;

        throw new ArgumentOutOfRangeException(nameof(sampleIndex));
    }

    static Run CloneRunSlice(Run source, int startSample, int sampleCount, long payloadOffset)
    {
        var result = new Run(sampleCount)
        {
            Version = source.Version,
            HasCompositionOffset = source.HasCompositionOffset,
            PayloadOffset = payloadOffset
        };

        for (int i = 0; i < sampleCount; i++)
        {
            Sample sample = source.Samples[startSample + i];
            result.Samples.Add(sample);
            result.Duration = checked(result.Duration + sample.Duration);
            result.DataSize = checked(result.DataSize + sample.Size);
        }

        if (result.Samples.Count == 0 || result.Duration == 0 || result.DataSize == 0)
            throw new InvalidDataException("Empty trun slice.");

        result.StartsWithSync = IsSyncSample(result.Samples[0].Flags);
        return result;
    }

    static ulong SumSampleSizes(List<Sample> samples, int start, int count)
    {
        ulong size = 0;

        for (int i = 0; i < count; i++)
            size = checked(size + samples[start + i].Size);

        return size;
    }

    static void CopyPayloadRange(
        RecyclableMemoryStream source,
        long offset,
        ulong length,
        Stream destination
    )
    {
        if (length == 0)
            return;

        if (length > long.MaxValue)
            throw new InvalidDataException("Payload range is too large.");

        long count = (long)length;
        ReadOnlySequence<byte> sequence = source.GetReadOnlySequence();

        if (offset < 0 ||
            count > sequence.Length ||
            offset > sequence.Length - count)
        {
            throw new EndOfStreamException("Unexpected end of fragment payload.");
        }

        foreach (ReadOnlyMemory<byte> block in sequence.Slice(offset, count))
            destination.Write(block.Span);
    }

    void BuildSegment(int videoCount, int audioCount, bool allowSingleTrack = false)
    {
        bool hasVideo = videoCount > 0;
        bool hasAudio = audioCount > 0;

        if (!hasVideo && !hasAudio)
        {
            throw new InvalidOperationException(
                "Segment contains no fragments."
            );
        }

        if (!allowSingleTrack && (!hasVideo || !hasAudio))
        {
            throw new InvalidOperationException(
                "Regular segment must contain video and audio."
            );
        }

        if (hasVideo)
            ValidateTrack(_video, videoCount);

        if (hasAudio)
            ValidateTrack(_audio, audioCount);

        long payloadLength = 0;

        if (hasVideo)
            AssignOffsets(_video, videoCount, ref payloadLength);

        if (hasAudio)
            AssignOffsets(_audio, audioCount, ref payloadLength);

        long videoTrafSize = hasVideo
            ? GetTrafSize(_video, videoCount)
            : 0;

        long audioTrafSize = hasAudio
            ? GetTrafSize(_audio, audioCount)
            : 0;

        long moofSize64 = checked(
            8L +
            16L +
            videoTrafSize +
            audioTrafSize
        );

        if (moofSize64 > uint.MaxValue)
            throw new InvalidDataException("Combined moof is too large.");

        uint moofSize = (uint)moofSize64;

        int mdatHeaderSize =
            checked((ulong)payloadLength + 8UL) <= uint.MaxValue
                ? 8
                : 16;

        Fragment first = hasVideo
            ? _video[0]
            : _audio[0];

        Fragment last = hasVideo
            ? _video[videoCount - 1]
            : _audio[audioCount - 1];

        byte[] styp = _styp;
        RecyclableMemoryStream prefix = _prefix;
        uint sequence = _sequence++;
        ulong tfdtOffsetNs = _tfdtOffsetNs;

        long segmentLength = checked(
            (long)(styp?.Length ?? 0) +
            (prefix?.Length ?? 0L) +
            moofSize +
            mdatHeaderSize +
            payloadLength
        );

        bool writerActive = true;

        void WriteSegment(Stream output)
        {
            if (!writerActive)
            {
                throw new InvalidOperationException(
                    "Segment writer can only be used during the segment callback."
                );
            }

            if (styp != null)
                output.Write(styp);

            Append(prefix, output);

            WriteHeader(output, moofSize, BoxMoof);
            WriteMfhd(output, sequence);

            if (hasVideo)
            {
                WriteTraf(
                    output,
                    _video,
                    videoCount,
                    moofSize,
                    mdatHeaderSize,
                    tfdtOffsetNs
                );
            }

            if (hasAudio)
            {
                WriteTraf(
                    output,
                    _audio,
                    audioCount,
                    moofSize,
                    mdatHeaderSize,
                    tfdtOffsetNs
                );
            }

            WriteMdatHeader(
                output,
                checked((ulong)payloadLength),
                mdatHeaderSize
            );

            if (hasVideo)
                AppendPayloads(_video, videoCount, output);

            if (hasAudio)
                AppendPayloads(_audio, audioCount, output);
        }

        try
        {
            _onSegment(new Segment(
                segmentLength,
                AddClockTime(_tfdtOffsetNs, ToNanoseconds(first.DecodeTime, first.Timescale)),
                AddClockTime(_tfdtOffsetNs, ToNanoseconds(last.EndTime, last.Timescale)),
                WriteSegment
            ));

            if (_cueMode)
                _hasTargetSegment = false;
        }
        finally
        {
            writerActive = false;
        }

        if (hasVideo)
        {
            _lastVideoEndTime = last.EndTime;
            _completedVideoSegmentsCount++;
            Remove(_video, videoCount);
        }

        if (hasAudio)
            Remove(_audio, audioCount);

        ResetPrefix();
    }

    static void ValidateTrack(List<Fragment> fragments, int count)
    {
        Fragment first = fragments[0];
        ulong expected = first.EndTime;

        for (int i = 1; i < count; i++)
        {
            Fragment current = fragments[i];

            if (current.TrackId != first.TrackId ||
                current.Timescale != first.Timescale ||
                current.SampleDescriptionIndex != first.SampleDescriptionIndex ||
                current.DecodeTime != expected)
            {
                throw new InvalidDataException(
                    $"Track {first.TrackId} fragments cannot be merged into one traf."
                );
            }

            expected = current.EndTime;
        }
    }

    static void AssignOffsets(
        List<Fragment> fragments,
        int count,
        ref long outputOffset
    )
    {
        for (int i = 0; i < count; i++)
        {
            Fragment fragment = fragments[i];
            long baseOffset = outputOffset;

            foreach (Run run in fragment.Runs)
                run.OutputOffset = checked(baseOffset + run.PayloadOffset);

            outputOffset = checked(outputOffset + fragment.Payload.Length);
        }
    }

    static long GetTrafSize(List<Fragment> fragments, int count)
    {
        long tfhdSize = fragments[0].SampleDescriptionIndexOverride == 0
            ? 16L
            : 20L;

        long size = 8L + tfhdSize + 20L;

        for (int i = 0; i < count; i++)
        {
            foreach (Run run in fragments[i].Runs)
                size = checked(size + GetTrunSize(run));
        }

        return size;
    }

    static long GetTrunSize(Run run)
    {
        int fieldsPerSample = run.HasCompositionOffset ? 4 : 3;
        return checked(20L + (long)run.SampleCount * fieldsPerSample * 4L);
    }

    static void WriteTraf(
        Stream output,
        List<Fragment> fragments,
        int count,
        uint moofSize,
        int mdatHeaderSize,
        ulong tfdtOffsetNs
    )
    {
        long size64 = GetTrafSize(fragments, count);

        if (size64 > uint.MaxValue)
            throw new InvalidDataException("Combined traf is too large.");

        Fragment first = fragments[0];

        WriteHeader(output, (uint)size64, BoxTraf);
        WriteTfhd(output, first.TrackId, first.SampleDescriptionIndexOverride);

        WriteTfdt(
            output,
            AddTfdtOffset(first.DecodeTime, first.Timescale, tfdtOffsetNs)
        );

        for (int i = 0; i < count; i++)
        {
            foreach (Run run in fragments[i].Runs)
            {
                long dataOffset = checked(
                    (long)moofSize +
                    mdatHeaderSize +
                    run.OutputOffset
                );

                if (dataOffset < int.MinValue || dataOffset > int.MaxValue)
                    throw new InvalidDataException("trun.data_offset exceeds Int32.");

                WriteTrun(output, run, (int)dataOffset);
            }
        }
    }

    static void WriteTrun(Stream output, Run run, int dataOffset)
    {
        if (run.SampleCount == 0)
            throw new InvalidDataException("Invalid trun sample count.");

        long size64 = GetTrunSize(run);

        if (size64 > uint.MaxValue)
            throw new InvalidDataException("trun is too large.");

        uint flags =
            TrunDataOffsetPresent |
            TrunSampleDurationPresent |
            TrunSampleSizePresent |
            TrunSampleFlagsPresent;

        if (run.HasCompositionOffset)
            flags |= TrunCompositionOffsetPresent;

        byte version = run.HasCompositionOffset
            ? run.Version
            : (byte)0;

        WriteHeader(output, (uint)size64, BoxTrun);

        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(
            header.Slice(0, 4),
            ((uint)version << 24) | flags
        );
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)run.SampleCount);
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(8, 4), dataOffset);
        output.Write(header);

        Span<byte> sampleBytes = stackalloc byte[16];

        foreach (Sample sample in run.Samples)
        {
            BinaryPrimitives.WriteUInt32BigEndian(sampleBytes.Slice(0, 4), sample.Duration);
            BinaryPrimitives.WriteUInt32BigEndian(sampleBytes.Slice(4, 4), sample.Size);
            BinaryPrimitives.WriteUInt32BigEndian(sampleBytes.Slice(8, 4), sample.Flags);

            if (run.HasCompositionOffset)
            {
                BinaryPrimitives.WriteUInt32BigEndian(
                    sampleBytes.Slice(12, 4),
                    sample.CompositionOffset
                );
                output.Write(sampleBytes);
            }
            else
            {
                output.Write(sampleBytes.Slice(0, 12));
            }
        }
    }

    static bool TryParseMoof(
        ReadOnlySpan<byte> moof,
        TrackInfo videoTrack,
        TrackInfo audioTrack,
        uint videoDurationHint,
        uint audioDurationHint,
        out Fragment fragment,
        out string error
    )
    {
        fragment = null;
        error = null;

        int root = 0;

        if (!TryReadBox(
            moof,
            ref root,
            out uint rootType,
            out int moofHeader,
            out ReadOnlySpan<byte> moofBox
        ) || rootType != BoxMoof || root != moof.Length)
        {
            error = "buffer does not contain exactly one moof";
            return false;
        }

        int position = moofHeader;
        int trafCount = 0;

        while (TryReadBox(
            moofBox,
            ref position,
            out uint type,
            out int headerSize,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type != BoxTraf)
                continue;

            trafCount++;

            if (trafCount > 1)
            {
                error = "source moof must contain one traf";
                return false;
            }

            if (!TryParseTraf(
                box,
                headerSize,
                videoTrack,
                audioTrack,
                videoDurationHint,
                audioDurationHint,
                out fragment,
                out error
            ))
            {
                return false;
            }
        }

        if (fragment == null)
        {
            error = "traf was not found";
            return false;
        }

        return true;
    }

    static bool TryParseTraf(
        ReadOnlySpan<byte> traf,
        int trafHeader,
        TrackInfo videoTrack,
        TrackInfo audioTrack,
        uint videoDurationHint,
        uint audioDurationHint,
        out Fragment fragment,
        out string error
    )
    {
        fragment = null;
        error = null;

        uint trackId = 0;
        uint sampleDescriptionIndex = 0;
        bool hasSampleDescriptionIndex = false;
        uint defaultDuration = 0;
        uint defaultSize = 0;
        uint defaultFlags = 0;
        bool hasDefaultFlags = false;
        ulong decodeTime = 0;
        bool hasTfhd = false;
        bool hasTfdt = false;

        int position = trafHeader;

        while (TryReadBox(
            traf,
            ref position,
            out uint type,
            out int headerSize,
            out ReadOnlySpan<byte> box
        ))
        {
            switch (type)
            {
                case BoxTfhd:
                    if (hasTfhd ||
                        !TryReadTfhd(
                            box,
                            headerSize,
                            out trackId,
                            out sampleDescriptionIndex,
                            out hasSampleDescriptionIndex,
                            out defaultDuration,
                            out defaultSize,
                            out defaultFlags,
                            out hasDefaultFlags,
                            out error
                        ))
                    {
                        error ??= "invalid or duplicate tfhd";
                        return false;
                    }

                    hasTfhd = true;
                    break;

                case BoxTfdt:
                    if (hasTfdt || !TryReadTfdt(box, headerSize, out decodeTime))
                    {
                        error = "invalid or duplicate tfdt";
                        return false;
                    }

                    hasTfdt = true;
                    break;

                case BoxTrun:
                    // Разбирается вторым проходом после tfhd/trex defaults.
                    break;

                default:
                    error = $"unsupported box {FourCC(type)} inside traf";
                    return false;
            }
        }

        if (!hasTfhd || !hasTfdt)
        {
            error = "tfhd/tfdt was not found";
            return false;
        }

        uint timescale;
        Trex trex;
        uint durationHint;
        bool defaultDurationIsHint = false;

        if (trackId == videoTrack.Id)
        {
            timescale = videoTrack.Timescale;
            trex = videoTrack.Trex;
            durationHint = videoDurationHint;
        }
        else if (trackId == audioTrack.Id)
        {
            timescale = audioTrack.Timescale;
            trex = audioTrack.Trex;
            durationHint = audioDurationHint;
        }
        else
        {
            error = $"unsupported track_ID={trackId}";
            return false;
        }

        if (timescale == 0)
        {
            error = $"timescale is zero for track_ID={trackId}";
            return false;
        }

        uint effectiveSampleDescriptionIndex = hasSampleDescriptionIndex
            ? sampleDescriptionIndex
            : trex.DescriptionIndex;

        if (effectiveSampleDescriptionIndex == 0)
        {
            error =
                $"sample_description_index is zero for track_ID={trackId}";

            return false;
        }

        if (defaultDuration == 0)
            defaultDuration = trex.Duration;

        if (defaultDuration == 0)
        {
            defaultDuration = durationHint;
            defaultDurationIsHint = defaultDuration != 0;
        }

        if (defaultSize == 0)
            defaultSize = trex.Size;

        if (!hasDefaultFlags)
            defaultFlags = trex.Flags;

        uint sampleDescriptionIndexOverride =
            hasSampleDescriptionIndex &&
            sampleDescriptionIndex != trex.DescriptionIndex
                ? sampleDescriptionIndex
                : 0;

        var result = new Fragment
        {
            TrackId = trackId,
            Timescale = timescale,
            SampleDescriptionIndex = effectiveSampleDescriptionIndex,
            DecodeTime = decodeTime,
            SampleDescriptionIndexOverride = sampleDescriptionIndexOverride
        };

        ulong duration = 0;
        position = trafHeader;

        while (TryReadBox(
            traf,
            ref position,
            out uint type,
            out int headerSize,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type != BoxTrun)
                continue;

            if (!TryNormalizeTrun(
                box,
                headerSize,
                defaultDuration,
                defaultSize,
                defaultFlags,
                defaultDurationIsHint,
                out Run run,
                out error
            ))
            {
                result.Dispose();
                return false;
            }

            duration = checked(duration + run.Duration);
            result.HasInferredDuration |= run.HasInferredDuration;
            result.Runs.Add(run);
        }

        if (result.Runs.Count == 0 || duration == 0)
        {
            result.Dispose();
            error = "trun/duration was not found";
            return false;
        }

        result.Duration = duration;
        result.StartsWithSync = result.Runs[0].StartsWithSync;
        fragment = result;
        return true;
    }

    static bool TryReadTfhd(
        ReadOnlySpan<byte> box,
        int headerSize,
        out uint trackId,
        out uint sampleDescriptionIndex,
        out bool hasSampleDescriptionIndex,
        out uint defaultDuration,
        out uint defaultSize,
        out uint defaultFlags,
        out bool hasDefaultFlags,
        out string error
    )
    {
        trackId = 0;
        sampleDescriptionIndex = 0;
        hasSampleDescriptionIndex = false;
        defaultDuration = 0;
        defaultSize = 0;
        defaultFlags = 0;
        hasDefaultFlags = false;
        error = null;

        if (box.Length < headerSize + 8)
        {
            error = "tfhd is too small";
            return false;
        }

        uint versionFlags = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(headerSize, 4)
        );

        uint flags = versionFlags & 0x00FF_FFFF;

        if ((flags & TfhdBaseDataOffsetPresent) != 0)
        {
            error = "tfhd.base-data-offset-present is not supported";
            return false;
        }

        trackId = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(headerSize + 4, 4)
        );

        int cursor = headerSize + 8;

        hasSampleDescriptionIndex =
            (flags & TfhdSampleDescriptionIndexPresent) != 0;

        if (hasSampleDescriptionIndex &&
            !ReadUInt32(box, ref cursor, out sampleDescriptionIndex))
        {
            error = "invalid tfhd sample_description_index";
            return false;
        }

        if ((flags & TfhdDefaultSampleDurationPresent) != 0 &&
            !ReadUInt32(box, ref cursor, out defaultDuration))
        {
            error = "invalid tfhd default_sample_duration";
            return false;
        }

        if ((flags & TfhdDefaultSampleSizePresent) != 0 &&
            !ReadUInt32(box, ref cursor, out defaultSize))
        {
            error = "invalid tfhd default_sample_size";
            return false;
        }

        hasDefaultFlags =
            (flags & TfhdDefaultSampleFlagsPresent) != 0;

        if (hasDefaultFlags &&
            !ReadUInt32(box, ref cursor, out defaultFlags))
        {
            error = "invalid tfhd default_sample_flags";
            return false;
        }

        if (cursor != box.Length || trackId == 0)
        {
            error = "invalid tfhd body";
            return false;
        }

        return true;
    }

    static void WriteTfhd(
        Stream output,
        uint trackId,
        uint sampleDescriptionIndexOverride
    )
    {
        bool hasSampleDescriptionIndex = sampleDescriptionIndexOverride != 0;
        int size = hasSampleDescriptionIndex ? 20 : 16;
        Span<byte> tfhd = stackalloc byte[20];

        uint flags = TfhdDefaultBaseIsMoof;

        if (hasSampleDescriptionIndex)
            flags |= TfhdSampleDescriptionIndexPresent;

        BinaryPrimitives.WriteUInt32BigEndian(tfhd[..4], (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(tfhd.Slice(4, 4), BoxTfhd);
        BinaryPrimitives.WriteUInt32BigEndian(tfhd.Slice(8, 4), flags);
        BinaryPrimitives.WriteUInt32BigEndian(tfhd.Slice(12, 4), trackId);

        if (hasSampleDescriptionIndex)
            BinaryPrimitives.WriteUInt32BigEndian(tfhd.Slice(16, 4), sampleDescriptionIndexOverride);

        output.Write(tfhd[..size]);
    }

    static bool TryNormalizeTrun(
        ReadOnlySpan<byte> box,
        int headerSize,
        uint defaultDuration,
        uint defaultSize,
        uint defaultFlags,
        bool defaultDurationIsHint,
        out Run run,
        out string error
    )
    {
        run = null;
        error = null;

        if (box.Length < headerSize + 8)
        {
            error = "trun is too small";
            return false;
        }

        uint versionFlags = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(headerSize, 4)
        );

        byte version = (byte)(versionFlags >> 24);
        uint flags = versionFlags & 0x00FF_FFFF;
        uint sampleCount = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(headerSize + 4, 4)
        );

        if (version > 1)
        {
            error = "unsupported trun version";
            return false;
        }

        int cursor = headerSize + 8;
        int? sourceDataOffset = null;

        if ((flags & TrunDataOffsetPresent) != 0)
        {
            if (box.Length - cursor < 4)
            {
                error = "invalid trun data_offset";
                return false;
            }

            sourceDataOffset = BinaryPrimitives.ReadInt32BigEndian(
                box.Slice(cursor, 4)
            );
            cursor += 4;
        }

        bool hasFirstSampleFlags =
            (flags & TrunFirstSampleFlagsPresent) != 0;

        uint firstSampleFlags = defaultFlags;

        if (hasFirstSampleFlags &&
            !ReadUInt32(box, ref cursor, out firstSampleFlags))
        {
            error = "invalid trun first_sample_flags";
            return false;
        }

        bool hasDuration = (flags & TrunSampleDurationPresent) != 0;
        bool hasSize = (flags & TrunSampleSizePresent) != 0;
        bool hasSampleFlags = (flags & TrunSampleFlagsPresent) != 0;
        bool hasCompositionOffset = (flags & TrunCompositionOffsetPresent) != 0;

        if (!hasDuration && defaultDuration == 0)
        {
            error =
                $"sample duration is absent " +
                $"(sample_count={sampleCount}, trun_flags=0x{flags:X6})";
            return false;
        }

        if (!hasSize && defaultSize == 0)
        {
            error = "sample size is absent";
            return false;
        }

        int sampleCapacity = sampleCount <= MaxRunSamplePreallocation
            ? (int)sampleCount
            : 0;

        var result = new Run(sampleCapacity)
        {
            Version = version,
            HasCompositionOffset = hasCompositionOffset,
            HasInferredDuration = !hasDuration && defaultDurationIsHint,
            SourceDataOffset = sourceDataOffset
        };

        for (uint i = 0; i < sampleCount; i++)
        {
            uint sampleDuration = defaultDuration;
            uint sampleSize = defaultSize;
            uint sampleFlags = defaultFlags;
            uint compositionOffset = 0;

            if (hasDuration && !ReadUInt32(box, ref cursor, out sampleDuration))
            {
                error = "invalid trun sample_duration";
                return false;
            }

            if (hasSize && !ReadUInt32(box, ref cursor, out sampleSize))
            {
                error = "invalid trun sample_size";
                return false;
            }

            if (hasSampleFlags)
            {
                if (!ReadUInt32(box, ref cursor, out sampleFlags))
                {
                    error = "invalid trun sample_flags";
                    return false;
                }
            }
            else if (i == 0 && hasFirstSampleFlags)
            {
                sampleFlags = firstSampleFlags;
            }

            if (hasCompositionOffset &&
                !ReadUInt32(box, ref cursor, out compositionOffset))
            {
                error = "invalid trun composition_time_offset";
                return false;
            }

            result.Samples.Add(
                new Sample(
                    sampleDuration,
                    sampleSize,
                    sampleFlags,
                    compositionOffset
                )
            );

            result.Duration = checked(result.Duration + sampleDuration);
            result.DataSize = checked(result.DataSize + sampleSize);
        }

        if (cursor != box.Length)
        {
            error =
                $"invalid trun body length: parsed={cursor}, actual={box.Length}, " +
                $"sample_count={sampleCount}, flags=0x{flags:X6}";
            return false;
        }

        if (sampleCount == 0)
        {
            error = $"empty trun (flags=0x{flags:X6})";
            return false;
        }

        if (result.Duration == 0)
        {
            error =
                $"trun duration is zero (sample_count={sampleCount}, " +
                $"flags=0x{flags:X6})";
            return false;
        }

        if (result.DataSize == 0)
        {
            error =
                $"trun data size is zero (sample_count={sampleCount}, " +
                $"flags=0x{flags:X6})";
            return false;
        }

        result.StartsWithSync = IsSyncSample(result.Samples[0].Flags);
        run = result;
        return true;
    }

    static bool IsSyncSample(uint flags)
    {
        const uint NonSync = 0x00010000;
        uint dependsOn = (flags >> 24) & 0x03;

        return (flags & NonSync) == 0 && dependsOn != 1;
    }

    static bool TryReadTfdt(
        ReadOnlySpan<byte> box,
        int headerSize,
        out ulong decodeTime
    )
    {
        decodeTime = 0;

        if (box.Length < headerSize + 8)
            return false;

        byte version = box[headerSize];
        int offset = headerSize + 4;

        if (version == 1)
        {
            if (box.Length < offset + 8)
                return false;

            decodeTime = BinaryPrimitives.ReadUInt64BigEndian(box.Slice(offset, 8));
            return true;
        }

        if (version == 0)
        {
            if (box.Length < offset + 4)
                return false;

            decodeTime = BinaryPrimitives.ReadUInt32BigEndian(box.Slice(offset, 4));
            return true;
        }

        return false;
    }

    static bool TryParseInit(
        ReadOnlySpan<byte> init,
        out TrackInfo video,
        out TrackInfo audio,
        out string error
    )
    {
        video = default;
        audio = default;
        error = null;

        if (!FindBox(
            init,
            BoxMoov,
            out ReadOnlySpan<byte> moov,
            out int moovHeader
        ))
        {
            error = "moov was not found";
            return false;
        }

        uint videoId = 0;
        uint videoTimescale = 0;
        uint audioId = 0;
        uint audioTimescale = 0;
        var trex = new List<TrexEntry>(2);

        int position = moovHeader;

        while (TryReadBox(
            moov,
            ref position,
            out uint type,
            out int header,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type == BoxTrak)
            {
                if (!TryReadTrack(
                    box,
                    header,
                    out uint trackId,
                    out uint timescale,
                    out uint handler
                ))
                {
                    error = "invalid trak/tkhd/mdia/mdhd/hdlr";
                    return false;
                }

                if (handler == HandlerVideo)
                {
                    if (videoId != 0)
                    {
                        error = "multiple video tracks in mp4mux output";
                        return false;
                    }

                    videoId = trackId;
                    videoTimescale = timescale;
                }
                else if (handler == HandlerAudio)
                {
                    if (audioId != 0)
                    {
                        error = "multiple audio tracks in mp4mux output";
                        return false;
                    }

                    audioId = trackId;
                    audioTimescale = timescale;
                }

                continue;
            }

            if (type != BoxMvex)
                continue;

            int mvexPosition = header;

            while (TryReadBox(
                box,
                ref mvexPosition,
                out uint childType,
                out int childHeader,
                out ReadOnlySpan<byte> child
            ))
            {
                if (childType != BoxTrex)
                    continue;

                if (!TryReadTrex(
                    child,
                    childHeader,
                    out TrexEntry entry
                ))
                {
                    error = "invalid trex";
                    return false;
                }

                trex.Add(entry);
            }
        }

        if (videoId == 0 || videoTimescale == 0)
        {
            error = "video track was not found through hdlr=vide";
            return false;
        }

        if (audioId == 0 || audioTimescale == 0)
        {
            error = "audio track was not found through hdlr=soun";
            return false;
        }

        if (!TryFindTrex(
            trex,
            videoId,
            out Trex videoTrex,
            out error
        ))
        {
            return false;
        }

        if (!TryFindTrex(
            trex,
            audioId,
            out Trex audioTrex,
            out error
        ))
        {
            return false;
        }

        video = new TrackInfo(
            videoId,
            videoTimescale,
            videoTrex
        );

        audio = new TrackInfo(
            audioId,
            audioTimescale,
            audioTrex
        );

        return true;
    }

    static bool TryReadTrack(
        ReadOnlySpan<byte> trak,
        int trakHeader,
        out uint trackId,
        out uint timescale,
        out uint handler
    )
    {
        trackId = 0;
        timescale = 0;
        handler = 0;

        int position = trakHeader;

        while (TryReadBox(
            trak,
            ref position,
            out uint type,
            out int header,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type == BoxTkhd)
            {
                trackId = ReadTkhdTrackId(box, header);
                continue;
            }

            if (type != BoxMdia)
                continue;

            int mdiaPosition = header;

            while (TryReadBox(
                box,
                ref mdiaPosition,
                out uint mdiaType,
                out int mdiaHeader,
                out ReadOnlySpan<byte> child
            ))
            {
                if (mdiaType == BoxMdhd)
                    timescale = ReadMdhdTimescale(child, mdiaHeader);
                else if (mdiaType == BoxHdlr)
                    handler = ReadHandlerType(child, mdiaHeader);
            }
        }

        return true;
    }

    static uint ReadTkhdTrackId(ReadOnlySpan<byte> box, int header)
    {
        if (box.Length <= header)
            return 0;

        int offset = box[header] switch
        {
            1 => header + 20,
            0 => header + 12,
            _ => -1
        };

        return offset >= 0 && box.Length >= offset + 4
            ? BinaryPrimitives.ReadUInt32BigEndian(box.Slice(offset, 4))
            : 0;
    }

    static uint ReadMdhdTimescale(ReadOnlySpan<byte> box, int header)
    {
        if (box.Length <= header)
            return 0;

        int offset = box[header] switch
        {
            1 => header + 20,
            0 => header + 12,
            _ => -1
        };

        return offset >= 0 && box.Length >= offset + 4
            ? BinaryPrimitives.ReadUInt32BigEndian(box.Slice(offset, 4))
            : 0;
    }

    static uint ReadHandlerType(ReadOnlySpan<byte> box, int header)
    {
        // FullBox(4) + pre_defined(4) + handler_type(4)
        int offset = header + 8;

        return box.Length >= offset + 4
            ? BinaryPrimitives.ReadUInt32BigEndian(box.Slice(offset, 4))
            : 0;
    }

    static bool TryReadTrex(
        ReadOnlySpan<byte> box,
        int header,
        out TrexEntry entry
    )
    {
        entry = default;

        // FullBox(4), track_ID, description index, duration, size, flags
        if (box.Length != header + 24)
            return false;

        uint versionFlags = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(header, 4)
        );

        // trex version 0, flags 0.
        if (versionFlags != 0)
            return false;

        uint trackId = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(header + 4, 4)
        );

        uint descriptionIndex = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(header + 8, 4)
        );

        uint duration = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(header + 12, 4)
        );

        uint size = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(header + 16, 4)
        );

        uint flags = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(header + 20, 4)
        );

        if (trackId == 0 || descriptionIndex == 0)
            return false;

        entry = new TrexEntry(
            trackId,
            new Trex(
                descriptionIndex,
                duration,
                size,
                flags
            )
        );

        return true;
    }

    static bool TryFindTrex(
        List<TrexEntry> entries,
        uint trackId,
        out Trex value,
        out string error
    )
    {
        value = default;
        error = null;

        bool found = false;

        foreach (TrexEntry entry in entries)
        {
            if (entry.TrackId != trackId)
                continue;

            if (found)
            {
                error = $"duplicate trex for track_ID={trackId}";
                return false;
            }

            value = entry.Value;
            found = true;
        }

        if (!found)
        {
            error = $"trex was not found for track_ID={trackId}";
            return false;
        }

        if (value.DescriptionIndex == 0)
        {
            error =
                $"trex.default_sample_description_index is zero " +
                $"for track_ID={trackId}";

            return false;
        }

        return true;
    }

    static bool FindBox(
        ReadOnlySpan<byte> data,
        uint requiredType,
        out ReadOnlySpan<byte> result,
        out int headerSize
    )
    {
        result = default;
        headerSize = 0;
        int position = 0;

        while (position < data.Length)
        {
            int start = position;

            if (!TryReadBox(
                data,
                ref position,
                out uint type,
                out int header,
                out _
            ))
            {
                return false;
            }

            if (type != requiredType)
                continue;

            result = data.Slice(start, position - start);
            headerSize = header;
            return true;
        }

        return false;
    }

    static bool TryReadBox(
        ReadOnlySpan<byte> data,
        ref int position,
        out uint type,
        out int headerSize,
        out ReadOnlySpan<byte> box
    )
    {
        type = 0;
        headerSize = 0;
        box = default;

        int start = position;

        if ((uint)start > (uint)data.Length || data.Length - start < 8)
            return false;

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(start, 4));
        type = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(start + 4, 4));

        ulong size = size32;
        headerSize = 8;

        if (size32 == 1)
        {
            if (data.Length - start < 16)
                return false;

            size = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(start + 8, 8));
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            size = (ulong)(data.Length - start);
        }

        if (size < (ulong)headerSize ||
            size > int.MaxValue ||
            size > (ulong)(data.Length - start))
        {
            return false;
        }

        int length = (int)size;
        box = data.Slice(start, length);
        position = start + length;
        return true;
    }

    static bool ReadUInt32(
        ReadOnlySpan<byte> data,
        ref int position,
        out uint value
    )
    {
        value = 0;

        if (position < 0 || data.Length - position < 4)
            return false;

        value = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position, 4));
        position += 4;
        return true;
    }

    static ulong ToUnits(int seconds, uint timescale)
    {
        if (seconds <= 0)
            throw new InvalidDataException("Invalid segment duration.");

        if (timescale == 0)
            throw new InvalidDataException("Invalid timescale.");

        return checked((ulong)seconds * timescale);
    }

    static ulong ConvertTimeCeiling(
        ulong value,
        uint fromTimescale,
        uint toTimescale
    )
    {
        if (fromTimescale == 0 || toTimescale == 0)
            throw new InvalidDataException("Invalid timescale.");

        UInt128 numerator = (UInt128)value * toTimescale;
        UInt128 result = (numerator + fromTimescale - 1) / fromTimescale;

        if (result > ulong.MaxValue)
            throw new InvalidDataException("Timeline value is too large.");

        return (ulong)result;
    }

    static ulong AddTfdtOffset(ulong value, uint timescale, ulong offsetNs)
    {
        if (offsetNs == 0)
            return value;

        if (timescale == 0)
            throw new InvalidDataException("Invalid timescale.");

        UInt128 units = ((UInt128)offsetNs * timescale + GstSecond / 2) / GstSecond;

        if (units > ulong.MaxValue)
            throw new InvalidDataException("Invalid tfdt offset.");

        return checked(value + (ulong)units);
    }

    static void WriteTfdt(Stream output, ulong decodeTime)
    {
        Span<byte> box = stackalloc byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(box.Slice(0, 4), 20);
        BinaryPrimitives.WriteUInt32BigEndian(box.Slice(4, 4), BoxTfdt);
        BinaryPrimitives.WriteUInt32BigEndian(box.Slice(8, 4), 0x01000000);
        BinaryPrimitives.WriteUInt64BigEndian(box.Slice(12, 8), decodeTime);
        output.Write(box);
    }

    static void WriteMfhd(Stream output, uint sequence)
    {
        Span<byte> box = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(box.Slice(0, 4), 16);
        BinaryPrimitives.WriteUInt32BigEndian(box.Slice(4, 4), BoxMfhd);
        BinaryPrimitives.WriteUInt32BigEndian(box.Slice(8, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(box.Slice(12, 4), sequence);
        output.Write(box);
    }

    static void WriteHeader(Stream output, uint size, uint type)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(0, 4), size);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), type);
        output.Write(header);
    }

    static void WriteMdatHeader(Stream output, ulong payloadLength, int headerSize)
    {
        if (headerSize == 8)
        {
            WriteHeader(output, checked((uint)(payloadLength + 8UL)), BoxMdat);
            return;
        }

        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), BoxMdat);
        BinaryPrimitives.WriteUInt64BigEndian(header.Slice(8, 8), payloadLength + 16UL);
        output.Write(header);
    }

    static void AppendPayloads(
        List<Fragment> fragments,
        int count,
        Stream output
    )
    {
        for (int i = 0; i < count; i++)
            Append(fragments[i].Payload, output);
    }

    static void Append(Stream source, Stream destination)
    {
        if (source == null || source.Length == 0)
            return;

        long position = source.Position;
        source.Position = 0;
        source.CopyTo(destination);
        source.Position = position;
    }

    static void Remove(List<Fragment> fragments, int count)
    {
        for (int i = 0; i < count; i++)
            fragments[i].Dispose();

        fragments.RemoveRange(0, count);
    }

    static void ClearFragments(List<Fragment> fragments)
    {
        foreach (Fragment fragment in fragments)
            fragment.Dispose();

        fragments.Clear();
    }

    static ReadOnlySpan<byte> GetSpan(MemoryStream stream)
    {
        if (!stream.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
            throw new InvalidOperationException("MemoryStream buffer is not accessible.");

        return segment.Array.AsSpan(segment.Offset, checked((int)stream.Length));
    }

    static void Reset(MemoryStream stream)
    {
        stream.SetLength(0);
        stream.Position = 0;
    }

    void EnsurePrefix()
    {
        _prefix ??= PoolInvk.msm.GetStream();
    }

    void ResetPrefix()
    {
        _prefix?.Dispose();
        _prefix = null;
    }

    void ClearSource()
    {
        _pending?.Dispose();
        _pending = null;

        _sourcePayload?.Dispose();
        _sourcePayload = null;

        _sourcePayloadFromMoof = 0;
        Reset(_sourceMoof);
    }

    void ResetBox()
    {
        _headerLength = 0;
        _headerRequired = 8;
        _boxType = 0;
        _boxRemaining = 0;
        _target = Target.None;
    }

    void ResetDeferred()
    {
        _deferredStart = 0;
        Reset(_deferred);
    }

    void KeepDeferred(int length, int consumed)
    {
        if (length < 0 ||
            (uint)consumed > (uint)length ||
            checked(_deferredStart + length) != _deferred.Length)
        {
            throw new InvalidOperationException("Deferred buffer range is invalid.");
        }

        if (consumed == length)
        {
            ResetDeferred();
            return;
        }

        _deferredStart = checked(_deferredStart + consumed);
        _deferred.Position = 0;
    }

    void AppendDeferred(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        EnsureDeferredAppendCapacity(data.Length);

        if (!_deferred.TryGetBuffer(out ArraySegment<byte> segment) ||
            segment.Array == null)
        {
            throw new InvalidOperationException("Deferred buffer is not accessible.");
        }

        int start = checked((int)_deferred.Length);
        data.CopyTo(segment.Array.AsSpan(segment.Offset + start, data.Length));

        _deferred.SetLength(checked(start + data.Length));
        _deferred.Position = 0;
    }

    void EnsureDeferredAppendCapacity(int count)
    {
        if (count <= 0)
            return;

        int end = checked((int)_deferred.Length);

        if ((uint)_deferredStart > (uint)end)
            throw new InvalidOperationException("Deferred buffer range is invalid.");

        if (count <= _deferred.Capacity - end)
            return;

        int activeLength = end - _deferredStart;

        if (_deferredStart != 0)
        {
            if (!_deferred.TryGetBuffer(out ArraySegment<byte> segment) ||
                segment.Array == null)
            {
                throw new InvalidOperationException("Deferred buffer is not accessible.");
            }

            if (activeLength != 0)
            {
                Buffer.BlockCopy(
                    segment.Array,
                    segment.Offset + _deferredStart,
                    segment.Array,
                    segment.Offset,
                    activeLength
                );
            }

            _deferredStart = 0;
            _deferred.SetLength(activeLength);
            _deferred.Position = 0;
            end = activeLength;

            if (count <= _deferred.Capacity - end)
                return;
        }

        int requiredEnd = checked(end + count);
        long doubled = (long)_deferred.Capacity * 2;
        int newCapacity = requiredEnd;

        if (doubled > newCapacity)
        {
            newCapacity = doubled > Array.MaxLength
                ? Math.Max(requiredEnd, Array.MaxLength)
                : (int)doubled;
        }

        _deferred.Capacity = newCapacity;
    }

    void AppendGstBufferToDeferred(
        Gst.Buffer buffer,
        int offset,
        int count
    )
    {
        if (count <= 0)
            return;

        EnsureDeferredAppendCapacity(count);

        if (!_deferred.TryGetBuffer(out ArraySegment<byte> segment) ||
            segment.Array == null)
        {
            throw new InvalidOperationException("Deferred buffer is not accessible.");
        }

        int start = checked((int)_deferred.Length);
        int written = 0;

        while (written < count)
        {
            int requested = Math.Min(_readBuffer.Length, count - written);
            int copied = (int)buffer.Extract(
                (nuint)checked(offset + written),
                segment.Array.AsSpan(
                    segment.Offset + start + written,
                    requested
                )
            );

            if (copied <= 0)
                break;

            written += copied;
        }

        _deferred.SetLength(checked(start + written));
        _deferred.Position = 0;
    }

    static string FourCC(uint type)
    {
        Span<char> value = stackalloc char[4];
        value[0] = (char)(type >> 24);
        value[1] = (char)(type >> 16);
        value[2] = (char)(type >> 8);
        value[3] = (char)type;
        return new string(value);
    }

    public void Dispose()
    {
        ResetPrefix();
        ClearSource();
        ClearFragments(_video);
        ClearFragments(_audio);

        _deferred.Dispose();
        _sourceMoof.Dispose();
        _sourceStyp.Dispose();
        _init.Dispose();
    }

    static ulong ToNanoseconds(ulong value, uint timescale)
    {
        if (timescale == 0)
            throw new InvalidDataException("Invalid timescale.");

        UInt128 result = ((UInt128)value * GstSecond + timescale / 2) / timescale;

        if (result > ulong.MaxValue)
            throw new InvalidDataException("Timeline value is too large.");

        return (ulong)result;
    }

    static ulong AddClockTime(ulong left, ulong right)
    {
        return ulong.MaxValue - left < right
            ? ulong.MaxValue
            : left + right;
    }

}
