using Microsoft.IO;
using Shared.Services.Pools;
using System;
using System.Buffers.Binary;
using System.IO;

namespace GStreamer;

public readonly record struct Segment(
    RecyclableMemoryStream audio,
    RecyclableMemoryStream video,
    double startSeconds
);

public class Mp4BoxReader
{
    readonly Action<byte[]> _onInit;
    readonly Action<Segment> _onSegment;

    MemoryStream _init = new();
    MemoryStream _pending = new MemoryStream(128 * 1024);

    RecyclableMemoryStream _audioPart;
    RecyclableMemoryStream _videoPart;

    bool _initDone;

    // Track ID из последнего moof: следующий mdat принадлежит этому треку
    uint _lastMoofTrackId;

    // Timescale треков из moov/trak/mdia/mdhd
    uint _videoTimescale;
    uint _audioTimescale;

    // Локальное начало сегмента и смещение timeline после seek
    double _segmentStartSeconds = -1;
    double _tfdtOffsetSeconds;

    const uint VideoTrackId = 1;
    const uint AudioTrackId = 2;

    const uint BoxStyp = ((uint)'s' << 24) | ((uint)'t' << 16) | ((uint)'y' << 8) | 'p';
    const uint BoxMoov = ((uint)'m' << 24) | ((uint)'o' << 16) | ((uint)'o' << 8) | 'v';
    const uint BoxMoof = ((uint)'m' << 24) | ((uint)'o' << 16) | ((uint)'o' << 8) | 'f';
    const uint BoxMdat = ((uint)'m' << 24) | ((uint)'d' << 16) | ((uint)'a' << 8) | 't';
    const uint BoxTrak = ((uint)'t' << 24) | ((uint)'r' << 16) | ((uint)'a' << 8) | 'k';
    const uint BoxTkhd = ((uint)'t' << 24) | ((uint)'k' << 16) | ((uint)'h' << 8) | 'd';
    const uint BoxMdia = ((uint)'m' << 24) | ((uint)'d' << 16) | ((uint)'i' << 8) | 'a';
    const uint BoxMdhd = ((uint)'m' << 24) | ((uint)'d' << 16) | ((uint)'h' << 8) | 'd';
    const uint BoxTraf = ((uint)'t' << 24) | ((uint)'r' << 16) | ((uint)'a' << 8) | 'f';
    const uint BoxTfhd = ((uint)'t' << 24) | ((uint)'f' << 16) | ((uint)'h' << 8) | 'd';
    const uint BoxTfdt = ((uint)'t' << 24) | ((uint)'f' << 16) | ((uint)'d' << 8) | 't';

    public Mp4BoxReader(Action<byte[]> onInit, Action<Segment> onSegment)
    {
        _onInit = onInit;
        _onSegment = onSegment;
    }

    /// <summary>
    /// Освобождает предыдущие части и начинает собирать новый сегмент
    /// </summary>
    public void ResetSegment()
    {
        if (_videoPart != null)
            _videoPart.Dispose();

        _videoPart = PoolInvk.msm.GetStream();

        if (_audioPart != null)
            _audioPart.Dispose();

        _audioPart = PoolInvk.msm.GetStream();

        _lastMoofTrackId = 0;
        _segmentStartSeconds = -1;
    }

    /// <summary>
    /// Сбрасывает парсер после seek и задаёт смещение новой timeline
    /// </summary>
    public void SeekReset(double seconds = 0)
    {
        _initDone = false;

        _videoTimescale = 0;
        _audioTimescale = 0;

        _segmentStartSeconds = -1;

        _tfdtOffsetSeconds =
            double.IsFinite(seconds) && seconds > 0
                ? seconds
                : 0;

        _init.SetLength(0);
        _init.Position = 0;

        _pending.SetLength(0);
        _pending.Position = 0;
    }

    /// <summary>
    /// Разбирает полные MP4 box и собирает audio/video сегмент
    /// </summary>
    public void Push(Gst.Buffer buffer, int size)
    {
        // Дописываем новые байты в конец незавершённого потока
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
            // Ничего не скопировано — возвращаем прежнюю длину
            _pending.SetLength(position);
            return;
        }

        // Учитываем частичное копирование Gst.Buffer
        if ((int)copied != size)
            _pending.SetLength(position + (int)copied);

        _pending.Position = 0;

        // Разбираем только полностью полученные MP4 box
        while (TryReadBox(
            _pending,
            out uint type,
            out Span<byte> box
        ))
        {
            // Первый styp/moof завершает накопление init-сегмента
            if (!_initDone && (type == BoxStyp || type == BoxMoof))
            {
                _initDone = true;

                byte[] init = _init.ToArray();

                // Timescale нужен для перевода tfdt в секунды
                _videoTimescale = GetTrackTimescale(
                    init,
                    VideoTrackId
                );

                _audioTimescale = GetTrackTimescale(
                    init,
                    AudioTrackId
                );

                _onInit(init);
            }

            if (!_initDone)
            {
                if (type == BoxMdat) // mdat до _initDone это провал
                    throw new InvalidOperationException("Bad init");

                // До первого media fragment собираем ftyp/moov/free и другие init-box
                _init.Write(box);
                continue;
            }

            if (type == BoxMoof)
            {
                // Из traf читаем track_ID и decode time текущего fragment
                _lastMoofTrackId = GetMoofTrackId(
                    box,
                    out ulong? decodeTime
                );

                uint timescale = 0;

                if (_lastMoofTrackId == AudioTrackId)
                {
                    timescale = _audioTimescale;
                }
                else if (_lastMoofTrackId == VideoTrackId)
                {
                    timescale = _videoTimescale;

                    // Сохраняем локальное начало video fragment до изменения tfdt
                    if (_videoTimescale > 0 && decodeTime.HasValue)
                    {
                        _segmentStartSeconds =
                            (double)decodeTime.Value / _videoTimescale;
                    }
                }

                // После seek добавляем абсолютное смещение к tfdt обоих треков
                if (_tfdtOffsetSeconds > 0 && timescale > 0)
                {
                    ShiftTfdt(
                        box,
                        timescale,
                        _tfdtOffsetSeconds
                    );
                }

                if (_lastMoofTrackId == AudioTrackId)
                {
                    _audioPart.Write(box);
                }
                else if (_lastMoofTrackId == VideoTrackId)
                {
                    _videoPart.Write(box);
                }

                continue;
            }

            if (type == BoxMdat)
            {
                // mdat относится к track_ID из предыдущего moof
                if (_lastMoofTrackId == AudioTrackId)
                    _audioPart.Write(box);
                else if (_lastMoofTrackId == VideoTrackId)
                    _videoPart.Write(box);

                // Сегмент готов после получения обеих частей
                if (_audioPart.Length > 0 && _videoPart.Length > 0)
                {
                    _videoPart.Position = 0;
                    _audioPart.Position = 0;

                    _onSegment(new Segment(
                        _audioPart,
                        _videoPart,
                        _segmentStartSeconds
                    ));

                    break;
                }

                continue;
            }

            // styp, sidx и остальные служебные box здесь не нужны
        }

        // Сохраняем неполный box для следующего Push
        CompactPending(); // ужасный метод для hot path, но я куст и пока будет так
    }

    /// <summary>
    /// Переводит смещение из секунд в units трека и ищет tfdt внутри moof
    /// </summary>
    static bool ShiftTfdt(
        Span<byte> moof,
        uint timescale,
        double offsetSeconds
    )
    {
        if (moof.Length < 8 || timescale == 0 || offsetSeconds <= 0)
            return false;

        double offsetValue = offsetSeconds * timescale;

        if (!double.IsFinite(offsetValue) ||
            offsetValue <= 0 ||
            offsetValue > ulong.MaxValue)
        {
            return false;
        }

        ulong offset = (ulong)Math.Round(offsetValue);

        // Пропускаем заголовок moof и ищем вложенный traf
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
                return false;

            if (size < 8 || pos + size > end)
                return false;

            if (type == BoxTraf)
            {
                return ShiftTrafTfdt(
                    moof,
                    pos + 8,
                    pos + (int)size,
                    offset
                );
            }

            pos += (int)size;
        }

        return false;
    }

    /// <summary>
    /// Находит tfdt внутри traf и увеличивает baseMediaDecodeTime
    /// </summary>
    static bool ShiftTrafTfdt(
        Span<byte> data,
        int start,
        int end,
        ulong offset
    )
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

            if (size == 1 || size == 0)
                return false;

            if (size < 8 || pos + size > end)
                return false;

            if (type == BoxTfdt)
            {
                if (size < 16)
                    return false;

                byte version = data[pos + 8];

                // tfdt version 1 хранит 64-битное значение
                if (version == 1)
                {
                    if (size < 20)
                        return false;

                    ulong value = BinaryPrimitives.ReadUInt64BigEndian(
                        data.Slice(pos + 12, 8)
                    );

                    if (ulong.MaxValue - value < offset)
                        return false;

                    BinaryPrimitives.WriteUInt64BigEndian(
                        data.Slice(pos + 12, 8),
                        value + offset
                    );

                    return true;
                }

                // tfdt version 0 хранит 32-битное значение
                if (version == 0)
                {
                    uint value = BinaryPrimitives.ReadUInt32BigEndian(
                        data.Slice(pos + 12, 4)
                    );

                    ulong result = value + offset;

                    if (result > uint.MaxValue)
                        return false;

                    BinaryPrimitives.WriteUInt32BigEndian(
                        data.Slice(pos + 12, 4),
                        (uint)result
                    );

                    return true;
                }

                return false;
            }

            pos += (int)size;
        }

        return false;
    }

    /// <summary>
    /// Ищет traf внутри moof и возвращает track_ID вместе с его tfdt
    /// </summary>
    static uint GetMoofTrackId(
        ReadOnlySpan<byte> moof,
        out ulong? decodeTime
    )
    {
        decodeTime = null;

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
                    pos + (int)size,
                    out decodeTime
                );

                if (trackId != 0)
                    return trackId;
            }

            pos += (int)size;
        }

        return 0;
    }

    /// <summary>
    /// Читает track_ID из tfhd и decode time из tfdt одного traf
    /// </summary>
    static uint GetTrafTrackId(
        ReadOnlySpan<byte> data,
        int start,
        int end,
        out ulong? decodeTime
    )
    {
        decodeTime = null;

        uint trackId = 0;
        int pos = start;

        while (pos + 8 <= end)
        {
            uint size = BinaryPrimitives.ReadUInt32BigEndian(
                data.Slice(pos, 4)
            );

            uint type = BinaryPrimitives.ReadUInt32BigEndian(
                data.Slice(pos + 4, 4)
            );

            if (size == 1 || size == 0)
                return 0;

            if (size < 8 || pos + size > end)
                return 0;

            if (type == BoxTfhd && size >= 16)
            {
                // tfhd: size + type + version/flags + track_ID
                trackId = BinaryPrimitives.ReadUInt32BigEndian(
                    data.Slice(pos + 12, 4)
                );
            }
            else if (type == BoxTfdt && size >= 16)
            {
                byte version = data[pos + 8];

                if (version == 1)
                {
                    if (size >= 20)
                    {
                        decodeTime = BinaryPrimitives.ReadUInt64BigEndian(
                            data.Slice(pos + 12, 8)
                        );
                    }
                }
                else if (version == 0)
                {
                    decodeTime = BinaryPrimitives.ReadUInt32BigEndian(
                        data.Slice(pos + 12, 4)
                    );
                }
            }

            pos += (int)size;
        }

        return trackId;
    }

    /// <summary>
    /// Находит нужный trak по track_ID и возвращает его mdhd timescale
    /// </summary>
    static uint GetTrackTimescale(
        ReadOnlySpan<byte> init,
        uint requiredTrackId
    )
    {
        int pos = 0;

        // Верхний уровень init: ищем moov
        while (TryReadBox(
            init,
            ref pos,
            out uint type,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type != BoxMoov)
                continue;

            int moovPos = 8;

            // Внутри moov перебираем trak
            while (TryReadBox(
                box,
                ref moovPos,
                out uint childType,
                out ReadOnlySpan<byte> child
            ))
            {
                if (childType != BoxTrak)
                    continue;

                uint trackId = 0;
                uint timescale = 0;

                int trakPos = 8;

                // tkhd содержит ID, mdia/mdhd — timescale этого же трека
                while (TryReadBox(
                    child,
                    ref trakPos,
                    out uint trakType,
                    out ReadOnlySpan<byte> trakBox
                ))
                {
                    if (trakType == BoxTkhd)
                    {
                        // Положение track_ID зависит от версии full box
                        int offset =
                            trakBox.Length > 8 && trakBox[8] == 1
                                ? 28
                                : 20;

                        if (trakBox.Length >= offset + 4)
                        {
                            trackId = BinaryPrimitives.ReadUInt32BigEndian(
                                trakBox.Slice(offset, 4)
                            );
                        }
                    }
                    else if (trakType == BoxMdia)
                    {
                        timescale = GetMdiaTimescale(trakBox);
                    }
                }

                if (trackId == requiredTrackId)
                    return timescale;
            }
        }

        return 0;
    }

    /// <summary>
    /// Ищет mdhd внутри mdia и читает timescale трека
    /// </summary>
    static uint GetMdiaTimescale(ReadOnlySpan<byte> mdia)
    {
        int pos = 8;

        while (TryReadBox(
            mdia,
            ref pos,
            out uint type,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type != BoxMdhd)
                continue;

            // Положение timescale зависит от версии mdhd
            int offset =
                box.Length > 8 && box[8] == 1
                    ? 28
                    : 20;

            if (box.Length >= offset + 4)
            {
                return BinaryPrimitives.ReadUInt32BigEndian(
                    box.Slice(offset, 4)
                );
            }
        }

        return 0;
    }

    /// <summary>
    /// Читает один MP4 box из span и передвигает position только при успехе
    /// </summary>
    static bool TryReadBox(
        ReadOnlySpan<byte> data,
        ref int position,
        out uint type,
        out ReadOnlySpan<byte> box
    )
    {
        type = 0;
        box = default;

        int start = position;

        if (start < 0 || start + 8 > data.Length)
            return false;

        ulong size = BinaryPrimitives.ReadUInt32BigEndian(
            data.Slice(start, 4)
        );

        type = BinaryPrimitives.ReadUInt32BigEndian(
            data.Slice(start + 4, 4)
        );

        int headerSize = 8;

        // size == 1 означает расширенный 64-битный размер
        if (size == 1)
        {
            if (start + 16 > data.Length)
                return false;

            size = BinaryPrimitives.ReadUInt64BigEndian(
                data.Slice(start + 8, 8)
            );

            headerSize = 16;
        }
        // size == 0 означает box до конца переданного span
        else if (size == 0)
        {
            size = (ulong)(data.Length - start);
        }

        if (size < (ulong)headerSize || size > int.MaxValue)
            return false;

        int boxSize = (int)size;

        if (boxSize > data.Length - start)
            return false;

        box = data.Slice(start, boxSize);
        position = start + boxSize;

        return true;
    }

    /// <summary>
    /// Читает один полный MP4 box из накопительного MemoryStream
    /// </summary>
    static bool TryReadBox(
        MemoryStream ms,
        out uint type,
        out Span<byte> box
    )
    {
        type = 0;
        box = default;

        long start = ms.Position;
        long available = ms.Length - start;

        if (available < 8)
            return false;

        if (!ms.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
            throw new InvalidOperationException("MemoryStream buffer is not accessible.");

        Span<byte> buffer = segment.Array.AsSpan(
            segment.Offset + (int)start,
            (int)available
        );

        ulong size = BinaryPrimitives.ReadUInt32BigEndian(buffer[..4]);
        type = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));

        // Потоковый box неизвестного размера здесь разобрать нельзя
        if (size == 0)
            return false;

        int headerSize = 8;

        if (size == 1)
        {
            if (available < 16)
                return false;

            size = BinaryPrimitives.ReadUInt64BigEndian(
                buffer.Slice(8, 8)
            );

            headerSize = 16;
        }

        if (size < (ulong)headerSize)
            return false;

        // Неполный box остаётся в _pending до следующего Push
        if ((ulong)available < size)
            return false;

        box = buffer[..(int)size];
        ms.Position = start + (long)size;

        return true;
    }

    /// <summary>
    /// Удаляет разобранные байты и переносит неполный box в начало буфера
    /// </summary>
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