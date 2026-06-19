using Microsoft.IO;
using Shared.Services.Pools;
using System;
using System.Buffers.Binary;
using System.IO;

namespace GStreamer;

public readonly record struct Segment(RecyclableMemoryStream audio, RecyclableMemoryStream video);

public class Mp4BoxReader
{
    readonly Action<byte[]> _onInit;
    readonly Action<Segment> _onSegment;

    MemoryStream _init = new();
    MemoryStream _pending = new MemoryStream(128 * 1024);

    RecyclableMemoryStream _audioPart;
    RecyclableMemoryStream _videoPart;

    bool _initDone;
    uint _lastMoofTrackId;

    const uint VideoTrackId = 1;
    const uint AudioTrackId = 2;

    const uint BoxStyp = ((uint)'s' << 24) | ((uint)'t' << 16) | ((uint)'y' << 8) | 'p';
    const uint BoxMoof = ((uint)'m' << 24) | ((uint)'o' << 16) | ((uint)'o' << 8) | 'f';
    const uint BoxMdat = ((uint)'m' << 24) | ((uint)'d' << 16) | ((uint)'a' << 8) | 't';
    const uint BoxTraf = ((uint)'t' << 24) | ((uint)'r' << 16) | ((uint)'a' << 8) | 'f';
    const uint BoxTfhd = ((uint)'t' << 24) | ((uint)'f' << 16) | ((uint)'h' << 8) | 'd';

    public Mp4BoxReader(Action<byte[]> onInit, Action<Segment> onSegment)
    {
        _onInit = onInit;
        _onSegment = onSegment;
    }

    public void ResetSegment()
    {
        if (_videoPart != null)
            _videoPart.Dispose();
        _videoPart = PoolInvk.msm.GetStream();

        if (_audioPart != null)
            _audioPart.Dispose();
        _audioPart = PoolInvk.msm.GetStream();

        _lastMoofTrackId = 0;
    }

    public void SeekReset()
    {
        _initDone = false;
        _init.SetLength(0);
        _pending.SetLength(0);
    }

    public void Push(Gst.Buffer buffer, int size)
    {
        int position = (int)_pending.Length;
        _pending.SetLength(position + size);

        if (!_pending.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
            throw new InvalidOperationException("MemoryStream buffer is not accessible.");

        Span<byte> dst = segment.Array.AsSpan(
            segment.Offset + position,
            size
        );

        nuint copied = buffer.Extract(0, dst);
        if (copied == 0)
        {
            // откатываем Length назад, ничего не записали
            _pending.SetLength(position);
            return;
        }

        // на всякий случай, если Extract скопировал меньше.
        if ((int)copied != size)
            _pending.SetLength(position + (int)copied);

        _pending.Position = 0;

        while (TryReadBox(_pending, out uint type, out ReadOnlySpan<byte> box))
        {
            if (!_initDone && (type == BoxStyp || type == BoxMoof))
            {
                _initDone = true;
                _init.Position = 0;
                _onInit(_init.ToArray());
            }

            if (!_initDone)
            {
                // ждем ftyp/moov/free и похожие init-box'ы
                // mdat до init это провал
                if (type == BoxMdat)
                    throw new InvalidOperationException("Bad init");

                _init.Write(box);
                continue;
            }

            if (type == BoxMoof)
            {
                _lastMoofTrackId = GetMoofTrackId(box);

                if (_lastMoofTrackId == AudioTrackId)
                    _audioPart.Write(box);
                else if (_lastMoofTrackId == VideoTrackId)
                    _videoPart.Write(box);

                continue;
            }

            if (type == BoxMdat)
            {
                if (_lastMoofTrackId == AudioTrackId)
                    _audioPart.Write(box);
                else if (_lastMoofTrackId == VideoTrackId)
                    _videoPart.Write(box);

                if (_audioPart.Length > 0 && _videoPart.Length > 0)
                {
                    _videoPart.Position = 0;
                    _audioPart.Position = 0;

                    _onSegment(new Segment(_audioPart, _videoPart));
                    break;
                }

                continue;
            }

            // styp, sidx и прочие box'ы игнорируем
        }

        CompactPending(); // ужасный метод для hot path, но я куст и пока будет так
    }

    static uint GetMoofTrackId(ReadOnlySpan<byte> moof)
    {
        if (moof.Length < 8)
            return 0;

        int pos = 8;
        int end = moof.Length;

        while (pos + 8 <= end)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(
                moof.Slice(pos, 4)
            );

            uint type = BinaryPrimitives.ReadUInt32BigEndian(
                moof.Slice(pos + 4, 4)
            );

            if (size == 1 || size == 0)
                return 0;

            if (size < 8 || pos + size > end)
                return 0;

            if (type == BoxTraf)
            {
                uint trackId = GetTrafTrackId(
                    moof,
                    pos + 8,
                    pos + (int)size
                );

                if (trackId != 0)
                    return trackId;
            }

            pos += (int)size;
        }

        return 0;
    }

    static uint GetTrafTrackId(ReadOnlySpan<byte> data, int start, int end)
    {
        int pos = start;

        while (pos + 8 <= end)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(
                data.Slice(pos, 4)
            );

            uint type = BinaryPrimitives.ReadUInt32BigEndian(
                data.Slice(pos + 4, 4)
            );

            if (size < 8 || pos + size > end)
                return 0;

            if (type == BoxTfhd)
            {
                if (size >= 16 && pos + 16 <= pos + (int)size)
                {
                    return BinaryPrimitives.ReadUInt32BigEndian(
                        data.Slice(pos + 12, 4)
                    );
                }

                return 0;
            }

            pos += (int)size;
        }

        return 0;
    }

    static bool TryReadBox(MemoryStream ms, out uint type, out ReadOnlySpan<byte> box)
    {
        type = 0;
        box = default;

        long start = ms.Position;
        long available = ms.Length - start;

        if (available < 8)
            return false;

        if (!ms.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
            throw new InvalidOperationException("MemoryStream buffer is not accessible.");

        var buffer = segment.Array.AsSpan(
            segment.Offset + (int)start,
            (int)available
        );

        ulong size = BinaryPrimitives.ReadUInt32BigEndian(buffer[..4]);
        type = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));

        if (size == 0)
            return false;

        int headerSize = 8;

        if (size == 1)
        {
            if (available < 16)
                return false;

            size = BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(8, 8));
            headerSize = 16;
        }

        if (size < (ulong)headerSize)
            return false;

        if ((ulong)available < size)
            return false;

        box = buffer[..(int)size];
        ms.Position = start + (long)size;

        return true;
    }

    void CompactPending()
    {
        int pos = (int)_pending.Position;
        int len = (int)_pending.Length;
        int rest = len - pos;

        if (rest <= 0)
        {
            _pending.SetLength(0);
            _pending.Position = 0;
            return;
        }

        // если ничего не прочитали, компактить нечего
        if (pos == 0)
            return;

        if (!_pending.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
            throw new InvalidOperationException("MemoryStream buffer is not accessible.");

        Buffer.BlockCopy(
            segment.Array,
            segment.Offset + pos,
            segment.Array,
            segment.Offset,
            rest
        );

        _pending.SetLength(rest);
        _pending.Position = 0;
    }

    public void Dispose()
    {
        _audioPart?.Dispose();
        _audioPart = null;

        _videoPart?.Dispose();
        _videoPart = null;

        _pending?.Dispose();
        _pending = null;

        _init?.Dispose();
        _init = null;
    }
}
