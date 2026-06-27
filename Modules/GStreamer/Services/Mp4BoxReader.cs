using Microsoft.IO;
using Shared.Services.Pools;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace GStreamer;

public readonly record struct Segment(
    RecyclableMemoryStream data,
    double startSeconds
);

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

    readonly Action<byte[]> _onInit;
    readonly Action<Segment> _onSegment;
    readonly double _segmentSeconds;

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
    RecyclableMemoryStream _segment;

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

    TrackInfo _videoTrack;
    TrackInfo _audioTrack;

    double _tfdtOffsetSeconds;
    uint _sequence = 1;

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
        public byte Version;
        public bool HasCompositionOffset;
        public int? SourceDataOffset;
        public ulong Duration;
        public ulong DataSize;
        public long PayloadOffset;
        public long OutputOffset;
        public bool StartsWithSync;
        public readonly List<Sample> Samples = new();

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
        public byte[] Tfhd;
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
        Action<byte[]> onInit,
        Action<Segment> onSegment,
        double segmentSeconds
    )
    {
        _onInit = onInit ?? throw new ArgumentNullException(nameof(onInit));
        _onSegment = onSegment ?? throw new ArgumentNullException(nameof(onSegment));

        if (!double.IsFinite(segmentSeconds) || segmentSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segmentSeconds),
                segmentSeconds,
                "Segment duration must be greater than zero."
            );
        }

        _segmentSeconds = segmentSeconds;
    }

    public void ResetSegment()
    {
        _segment?.Dispose();
        _segment = null;
    }

    public void SeekReset(double seconds = 0)
    {
        _initDone = false;
        _moovDone = false;
        _videoTrack = default;
        _audioTrack = default;
        _tfdtOffsetSeconds = double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
        _sequence = 1;
        _styp = null;

        Reset(_init);
        Reset(_sourceMoof);
        Reset(_sourceStyp);
        Reset(_deferred);

        ClearSource();
        ClearFragments(_video);
        ClearFragments(_audio);
        ResetPrefix();
        ResetBox();
        ResetSegment();
    }

    public void Push(Gst.Buffer buffer, int size)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (size <= 0)
            return;

        if (TryProcessDeferred())
        {
            AppendGstBuffer(buffer, 0, size, _deferred);
            _deferred.Position = 0;
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
                _deferred.Write(_readBuffer.AsSpan(consumed, copied - consumed));

            if (sourceOffset < size)
                AppendGstBuffer(buffer, sourceOffset, size - sourceOffset, _deferred);

            _deferred.Position = 0;
            return;
        }
    }

    public bool TryProcessDeferred()
    {
        if (TryBuildSegment())
            return true;

        if (_deferred.Length == 0)
            return false;

        int length = checked((int)_deferred.Length);
        ReadOnlySpan<byte> data;
        byte[] copy = null;

        if (_deferred.TryGetBuffer(out ArraySegment<byte> segment) && segment.Array != null)
        {
            data = segment.Array.AsSpan(segment.Offset, length);
        }
        else
        {
            copy = _deferred.ToArray();
            data = copy;
        }

        int consumed = Process(data, out bool completed);

        if (completed)
        {
            KeepDeferred(data, consumed);
            return true;
        }

        if (consumed != length)
        {
            throw new InvalidOperationException(
                $"MP4 parser consumed {consumed} of {length} deferred bytes."
            );
        }

        Reset(_deferred);
        return false;
    }

    public bool TryBuildEndOfStreamRemainder()
    {
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

            if (TryPrepareAudioForVideoEnd(videoEnd, out int selectedAudioCount))
                audioCount = selectedAudioCount;
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

        // mp4mux writes init first and starts media with styp or moof.
        if (!_initDone && (_boxType == BoxStyp || _boxType == BoxMoof))
            CompleteInit();

        if (!_initDone)
        {
            if (_boxType == BoxMdat)
                throw new InvalidDataException("mdat appeared before init was completed.");

            _target = Target.Init;
            Write(_boxHeader.AsSpan(0, headerSize));
            return;
        }

        switch (_boxType)
        {
            case BoxMoof:
                if (_pending != null)
                    throw new InvalidDataException("A new moof appeared before the previous mdat.");

                Reset(_sourceMoof);
                _sourcePayloadFromMoof = 0;
                _target = Target.Moof;
                Write(_boxHeader.AsSpan(0, headerSize));
                return;

            case BoxMdat:
                if (_pending == null)
                    throw new InvalidDataException("mdat does not follow a supported moof.");

                _sourcePayload?.Dispose();
                _sourcePayload = PoolInvk.msm.GetStream();
                _sourcePayloadFromMoof = checked(
                    _sourcePayloadFromMoof + headerSize
                );
                _target = Target.Payload;
                return;

            case BoxSidx:
                // Source sidx offsets become invalid after fragment merging.
                if (_pending != null)
                    _sourcePayloadFromMoof = checked(
                        _sourcePayloadFromMoof + (long)size
                    );
                return;

            case BoxStyp:
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

            case BoxEmsg:
            case BoxFree:
            case BoxPrft:
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

            default:
                throw new InvalidDataException(
                    $"Unsupported top-level MP4 box after init: {FourCC(_boxType)}."
                );
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

            case BoxMoov:
                if (_initDone)
                    throw new InvalidDataException("Unexpected moov after init.");

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

        byte[] init = _init.ToArray();

        if (!TryParseInit(
            init,
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
        _onInit(init);
    }

    void CompleteMoof()
    {
        ReadOnlySpan<byte> moof = GetSpan(_sourceMoof);

        if (!TryParseMoof(
            moof,
            _videoTrack,
            _audioTrack,
            out Fragment fragment,
            out string error
        ))
        {
            throw new InvalidDataException($"Unable to parse source moof: {error}");
        }

        _pending = fragment;
        _sourcePayloadFromMoof = _sourceMoof.Length;
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

        ulong target = ToUnits(_segmentSeconds, _videoTrack.Timescale);
        ulong duration = 0;

        // Нужен один fragment look-ahead: следующий segment должен начинаться с sync sample.
        for (int i = 0; i + 1 < _video.Count; i++)
        {
            duration = checked(duration + _video[i].Duration);

            if (duration >= target && _video[i + 1].StartsWithSync)
                return i + 1;
        }

        return 0;
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

    static void SplitFragment(List<Fragment> fragments, int index, int firstSampleCount)
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

    static Fragment SliceFragment(Fragment source, int startSample, int sampleCount)
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
            Tfhd = source.Tfhd,
            Payload = PoolInvk.msm.GetStream()
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
        var result = new Run
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
        Stream source,
        long offset,
        ulong length,
        Stream destination
    )
    {
        if (length == 0)
            return;

        if (length > long.MaxValue)
            throw new InvalidDataException("Payload range is too large.");

        long oldPosition = source.Position;
        byte[] buffer = new byte[64 * 1024];
        ulong remaining = length;

        try
        {
            source.Position = offset;

            while (remaining > 0)
            {
                int requested = (int)Math.Min((ulong)buffer.Length, remaining);
                int read = source.Read(buffer.AsSpan(0, requested));

                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of fragment payload.");

                destination.Write(buffer.AsSpan(0, read));
                remaining -= (uint)read;
            }
        }
        finally
        {
            source.Position = oldPosition;
        }
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

        ResetSegment();
        _segment = PoolInvk.msm.GetStream();

        if (_styp != null)
            _segment.Write(_styp);

        Append(_prefix, _segment);

        WriteHeader(_segment, moofSize, BoxMoof);
        WriteMfhd(_segment, _sequence++);

        if (hasVideo)
        {
            WriteTraf(
                _segment,
                _video,
                videoCount,
                moofSize,
                mdatHeaderSize
            );
        }

        if (hasAudio)
        {
            WriteTraf(
                _segment,
                _audio,
                audioCount,
                moofSize,
                mdatHeaderSize
            );
        }

        WriteMdatHeader(
            _segment,
            checked((ulong)payloadLength),
            mdatHeaderSize
        );

        if (hasVideo)
            AppendPayloads(_video, videoCount, _segment);

        if (hasAudio)
            AppendPayloads(_audio, audioCount, _segment);

        Fragment first = hasVideo
            ? _video[0]
            : _audio[0];

        double startSeconds =
            (double)first.DecodeTime /
            first.Timescale;

        _segment.Position = 0;

        _onSegment(
            new Segment(
                _segment,
                startSeconds
            )
        );

        if (hasVideo)
            Remove(_video, videoCount);

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
        long size = 8L + fragments[0].Tfhd.Length + 20L;

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

    void WriteTraf(
        Stream output,
        List<Fragment> fragments,
        int count,
        uint moofSize,
        int mdatHeaderSize
    )
    {
        long size64 = GetTrafSize(fragments, count);

        if (size64 > uint.MaxValue)
            throw new InvalidDataException("Combined traf is too large.");

        Fragment first = fragments[0];

        WriteHeader(output, (uint)size64, BoxTraf);
        output.Write(first.Tfhd);
        WriteTfdt(
            output,
            AddTfdtOffset(first.DecodeTime, first.Timescale, _tfdtOffsetSeconds)
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
        if (run.SampleCount == 0 || run.SampleCount > uint.MaxValue)
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

        if (trackId == videoTrack.Id)
        {
            timescale = videoTrack.Timescale;
            trex = videoTrack.Trex;
        }
        else if (trackId == audioTrack.Id)
        {
            timescale = audioTrack.Timescale;
            trex = audioTrack.Trex;
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

        if (defaultDuration == 0)
            defaultDuration = trex.Duration;

        if (defaultSize == 0)
            defaultSize = trex.Size;

        if (!hasDefaultFlags)
            defaultFlags = trex.Flags;

        byte[] tfhd = BuildCanonicalTfhd(
            trackId,
            hasSampleDescriptionIndex && sampleDescriptionIndex != trex.DescriptionIndex
                ? sampleDescriptionIndex
                : 0
        );

        var result = new Fragment
        {
            TrackId = trackId,
            Timescale = timescale,
            SampleDescriptionIndex = effectiveSampleDescriptionIndex,
            DecodeTime = decodeTime,
            Tfhd = tfhd
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
                out Run run,
                out error
            ))
            {
                result.Dispose();
                return false;
            }

            duration = checked(duration + run.Duration);
            result.Runs.Add(run);
        }

        if (result.Runs.Count == 0 || duration == 0)
        {
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

    static byte[] BuildCanonicalTfhd(uint trackId, uint sampleDescriptionIndexOverride)
    {
        bool hasSampleDescriptionIndex = sampleDescriptionIndexOverride != 0;
        int size = hasSampleDescriptionIndex ? 20 : 16;
        byte[] tfhd = new byte[size];

        uint flags = TfhdDefaultBaseIsMoof;

        if (hasSampleDescriptionIndex)
            flags |= TfhdSampleDescriptionIndexPresent;

        BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(0, 4), (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(4, 4), BoxTfhd);
        BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(8, 4), flags);
        BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(12, 4), trackId);

        if (hasSampleDescriptionIndex)
            BinaryPrimitives.WriteUInt32BigEndian(tfhd.AsSpan(16, 4), sampleDescriptionIndexOverride);

        return tfhd;
    }

    static bool TryNormalizeTrun(
        ReadOnlySpan<byte> box,
        int headerSize,
        uint defaultDuration,
        uint defaultSize,
        uint defaultFlags,
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
            error = "sample duration is absent";
            return false;
        }

        if (!hasSize && defaultSize == 0)
        {
            error = "sample size is absent";
            return false;
        }

        var result = new Run
        {
            Version = version,
            HasCompositionOffset = hasCompositionOffset,
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

        if (cursor != box.Length || sampleCount == 0 || result.Duration == 0 || result.DataSize == 0)
        {
            error = "invalid trun body";
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

        video = new TrackInfo(
            videoId,
            videoTimescale,
            FindTrex(trex, videoId)
        );

        audio = new TrackInfo(
            audioId,
            audioTimescale,
            FindTrex(trex, audioId)
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

        // FullBox(4), track_ID, description index, duration, size, flags.
        if (box.Length < header + 24)
            return false;

        uint trackId = BinaryPrimitives.ReadUInt32BigEndian(
            box.Slice(header + 4, 4)
        );

        if (trackId == 0)
            return false;

        entry = new TrexEntry(
            trackId,
            new Trex(
                BinaryPrimitives.ReadUInt32BigEndian(box.Slice(header + 8, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(box.Slice(header + 12, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(box.Slice(header + 16, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(box.Slice(header + 20, 4))
            )
        );

        return true;
    }

    static Trex FindTrex(List<TrexEntry> entries, uint trackId)
    {
        foreach (TrexEntry entry in entries)
        {
            if (entry.TrackId == trackId)
                return entry.Value;
        }

        return default;
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

    static ulong ToUnits(double seconds, uint timescale)
    {
        double value = seconds * timescale;

        if (!double.IsFinite(value) || value < 0 || value > ulong.MaxValue)
            throw new InvalidDataException("Invalid timeline value.");

        return (ulong)Math.Ceiling(value);
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

    static ulong AddTfdtOffset(ulong value, uint timescale, double seconds)
    {
        if (seconds <= 0)
            return value;

        double units = seconds * timescale;

        if (!double.IsFinite(units) || units < 0 || units > ulong.MaxValue)
            throw new InvalidDataException("Invalid tfdt offset.");

        return checked(value + (ulong)Math.Round(units));
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

    void KeepDeferred(ReadOnlySpan<byte> data, int consumed)
    {
        int count = data.Length - consumed;

        if (count <= 0)
        {
            Reset(_deferred);
            return;
        }

        if (!_deferred.TryGetBuffer(out ArraySegment<byte> segment) ||
            segment.Array == null)
        {
            throw new InvalidOperationException("Deferred buffer is not accessible.");
        }

        Buffer.BlockCopy(
            segment.Array,
            segment.Offset + consumed,
            segment.Array,
            segment.Offset,
            count
        );

        _deferred.SetLength(count);
        _deferred.Position = count;
    }

    void AppendGstBuffer(
        Gst.Buffer buffer,
        int offset,
        int count,
        Stream destination
    )
    {
        while (count > 0)
        {
            int requested = Math.Min(_readBuffer.Length, count);
            int copied = (int)buffer.Extract(
                (nuint)offset,
                _readBuffer.AsSpan(0, requested)
            );

            if (copied <= 0)
                return;

            destination.Write(_readBuffer.AsSpan(0, copied));
            offset += copied;
            count -= copied;
        }
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
        ResetSegment();
        ResetPrefix();
        ClearSource();
        ClearFragments(_video);
        ClearFragments(_audio);

        _deferred.Dispose();
        _sourceMoof.Dispose();
        _sourceStyp.Dispose();
        _init.Dispose();
    }
}
