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

    // moof держим целиком: внутри него нужны tfhd/tfdt
    MemoryStream _moof = new(16 * 1024);

    // Остаток Gst.Buffer после уже готового сегмента
    MemoryStream _deferred = new(64 * 1024);

    // Переиспользуемый буфер извлечения данных из Gst.Buffer
    readonly byte[] _readBuffer = new byte[64 * 1024];

    // Заголовок обычного box занимает 8 байт, extended-size box — 16 байт
    readonly byte[] _boxHeader = new byte[16];
    int _boxHeaderLength;
    int _boxHeaderRequired = 8;

    uint _currentBoxType;
    ulong _currentBoxRemaining;
    BoxTarget _currentTarget;

    RecyclableMemoryStream _audioPart;
    RecyclableMemoryStream _videoPart;

    bool _initDone;

    // Полный moov был получен и записан в _init
    bool _moovCompleted;

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

    enum BoxTarget
    {
        None,
        Init,
        Moof,
        Audio,
        Video
    }

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
        // Если предыдущий запрос прервался посреди mdat, его начало уже было записано в старый stream
        // Остаток этого mdat нужно отбросить
        if (_currentBoxType == BoxMdat && _currentBoxRemaining > 0)
            _currentTarget = BoxTarget.None;

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
        _moovCompleted = false;

        _videoTimescale = 0;
        _audioTimescale = 0;

        _lastMoofTrackId = 0;
        _segmentStartSeconds = -1;

        _tfdtOffsetSeconds =
            double.IsFinite(seconds) && seconds > 0
                ? seconds
                : 0;

        _init.SetLength(0);
        _init.Position = 0;

        _moof.SetLength(0);
        _moof.Position = 0;

        _deferred.SetLength(0);
        _deferred.Position = 0;

        ResetBoxState();
    }

    /// <summary>
    /// Потоково разбирает MP4 box:
    /// init и mdat пишет сразу, целиком накапливает только moof
    /// </summary>
    public void Push(Gst.Buffer buffer, int size)
    {
        // На случай прямого вызова Push без предварительного TryProcessDeferred сохраняем прежнее безопасное поведение
        if (TryProcessDeferred())
        {
            // Deferred уже сформировал Segment, поэтому весь новый Gst.Buffer относится к следующим сегментам
            AppendGstBufferToDeferred(
                buffer,
                0,
                size
            );

            _deferred.Position = 0;
            return;
        }

        int sourceOffset = 0;

        while (sourceOffset < size)
        {
            int requested = Math.Min(
                _readBuffer.Length,
                size - sourceOffset
            );

            nuint copiedValue = buffer.Extract(
                (nuint)sourceOffset,
                _readBuffer.AsSpan(0, requested)
            );

            int copied = (int)copiedValue;

            if (copied <= 0)
                return;

            int consumed = ProcessBytes(
                _readBuffer.AsSpan(0, copied),
                out bool segmentCompleted
            );

            sourceOffset += copied;

            if (segmentCompleted)
            {
                // Сохраняем только байты ПОСЛЕ готового сегмента
                if (consumed < copied)
                {
                    _deferred.Write(
                        _readBuffer.AsSpan(
                            consumed,
                            copied - consumed
                        )
                    );
                }

                if (sourceOffset < size)
                {
                    AppendGstBufferToDeferred(
                        buffer,
                        sourceOffset,
                        size - sourceOffset
                    );
                }

                _deferred.Position = 0;
                return;
            }

        }
    }

    /// <summary>
    /// Разбирает переданный кусок и возвращает число использованных байт
    /// </summary>
    int ProcessBytes(
        ReadOnlySpan<byte> data,
        out bool segmentCompleted
    )
    {
        segmentCompleted = false;
        int position = 0;

        while (position < data.Length)
        {
            // Сначала набираем 8 или 16 байт заголовка текущего box
            if (_boxHeaderLength < _boxHeaderRequired)
            {
                int copy = Math.Min(
                    _boxHeaderRequired - _boxHeaderLength,
                    data.Length - position
                );

                data.Slice(position, copy).CopyTo(
                    _boxHeader.AsSpan(_boxHeaderLength, copy)
                );

                _boxHeaderLength += copy;
                position += copy;

                if (_boxHeaderLength < _boxHeaderRequired)
                    break;

                if (_boxHeaderRequired == 8)
                {
                    uint size32 = BinaryPrimitives.ReadUInt32BigEndian(
                        _boxHeader.AsSpan(0, 4)
                    );

                    _currentBoxType = BinaryPrimitives.ReadUInt32BigEndian(
                        _boxHeader.AsSpan(4, 4)
                    );

                    if (size32 == 1)
                    {
                        _boxHeaderRequired = 16;
                        continue;
                    }

                    if (size32 == 0)
                    {
                        throw new NotSupportedException(
                            "MP4 box with size=0 cannot be parsed before end of stream."
                        );
                    }

                    BeginBox(size32, 8);
                }
                else
                {
                    ulong size64 = BinaryPrimitives.ReadUInt64BigEndian(
                        _boxHeader.AsSpan(8, 8)
                    );

                    BeginBox(size64, 16);
                }

                if (_currentBoxRemaining == 0)
                {
                    bool completed = CompleteBox();
                    ResetBoxState();

                    if (completed)
                    {
                        segmentCompleted = true;
                        break;
                    }
                }

                continue;
            }

            int bodySize = (int)Math.Min(
                (ulong)(data.Length - position),
                _currentBoxRemaining
            );

            if (bodySize <= 0)
                break;

            WriteCurrentBoxData(
                data.Slice(position, bodySize)
            );

            position += bodySize;
            _currentBoxRemaining -= (ulong)bodySize;

            if (_currentBoxRemaining == 0)
            {
                bool completed = CompleteBox();
                ResetBoxState();

                if (completed)
                {
                    segmentCompleted = true;
                    break;
                }
            }
        }

        return position;
    }

    /// <summary>
    /// Проверяет размер, завершает init при первом styp/moof
    /// и выбирает поток назначения текущего box
    /// </summary>
    void BeginBox(ulong size, int headerSize)
    {
        if (size < (ulong)headerSize)
            throw new InvalidDataException("Invalid MP4 box size.");

        if (_currentBoxType == BoxMoof && size > int.MaxValue)
            throw new InvalidDataException("moof is too large.");

        _currentBoxRemaining = size - (ulong)headerSize;
        _currentTarget = BoxTarget.None;

        // Первый media box завершает init. Сам styp/moof в init не входит
        if (!_initDone && (_currentBoxType == BoxStyp || _currentBoxType == BoxMoof))
            CompleteInit();

        if (!_initDone)
        {
            if (_currentBoxType == BoxMdat) // mdat до _initDone это провал
                throw new InvalidOperationException("Bad init");

            _currentTarget = BoxTarget.Init;
        }
        else if (_currentBoxType == BoxMoof)
        {
            _moof.SetLength(0);
            _moof.Position = 0;
            _currentTarget = BoxTarget.Moof;
        }
        else if (_currentBoxType == BoxMdat)
        {
            if (_lastMoofTrackId == AudioTrackId)
                _currentTarget = BoxTarget.Audio;
            else if (_lastMoofTrackId == VideoTrackId)
                _currentTarget = BoxTarget.Video;
            else
            {
                throw new InvalidDataException(
                    $"MP4 mdat does not follow a supported moof. " +
                    $"Current track_ID={_lastMoofTrackId}."
                );
            }
        }

        // Заголовок является частью box и тоже сразу идёт в нужный поток
        WriteCurrentBoxData(
            _boxHeader.AsSpan(0, headerSize)
        );
    }

    /// <summary>
    /// Пишет кусок текущего box непосредственно в его поток назначения
    /// </summary>
    void WriteCurrentBoxData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return;

        switch (_currentTarget)
        {
            case BoxTarget.Init:
                _init.Write(data);
                break;

            case BoxTarget.Moof:
                _moof.Write(data);
                break;

            case BoxTarget.Audio:
                _audioPart.Write(data);
                break;

            case BoxTarget.Video:
                _videoPart.Write(data);
                break;
        }
    }

    /// <summary>
    /// Завершает текущий box. Возвращает true, когда готов весь Segment
    /// </summary>
    bool CompleteBox()
    {
        if (_currentBoxType == BoxMoov)
        {
            if (_initDone)
                throw new InvalidDataException("Unexpected moov after MP4 initialization.");

            _moovCompleted = true;
            return false;
        }

        if (_currentBoxType == BoxMoof)
        {
            CompleteMoof();
            return false;
        }

        if (_currentBoxType != BoxMdat)
            return false;

        // Не позволяем следующему mdat унаследовать старый track_ID
        _lastMoofTrackId = 0;

        if (_audioPart.Length <= 0 || _videoPart.Length <= 0)
            return false;

        _videoPart.Position = 0;
        _audioPart.Position = 0;

        _onSegment(new Segment(
            _audioPart,
            _videoPart,
            _segmentStartSeconds
        ));

        return true;
    }

    void CompleteInit()
    {
        if (!_moovCompleted)
            throw new InvalidDataException("MP4 initialization is incomplete: moov box was not found.");

        if (_init.Length <= 0)
            throw new InvalidDataException("MP4 initialization is empty.");

        byte[] init = _init.ToArray();

        uint videoTimescale = GetTrackTimescale(
            init,
            VideoTrackId
        );

        uint audioTimescale = GetTrackTimescale(
            init,
            AudioTrackId
        );

        if (videoTimescale == 0)
            throw new InvalidDataException($"Video track {VideoTrackId} or its mdhd timescale was not found in moov.");

        if (audioTimescale == 0)
            throw new InvalidDataException($"Audio track {AudioTrackId} or its mdhd timescale was not found in moov.");

        _videoTimescale = videoTimescale;
        _audioTimescale = audioTimescale;
        _initDone = true;

        _onInit(init);
    }

    /// <summary>
    /// Разбирает полностью накопленный moof:
    /// изменяет tfdt и только после определения track_ID пишет его в audio/video
    /// </summary>
    void CompleteMoof()
    {
        if (!_moof.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
            throw new InvalidOperationException("moof buffer is not accessible.");

        Span<byte> box = segment.Array.AsSpan(
            segment.Offset,
            (int)_moof.Length
        );


        uint trackId = GetMoofTrackId(
            box,
            out ulong? decodeTime
        );

        if (trackId == 0)
            throw new InvalidDataException("MP4 moof does not contain a readable tfhd track_ID.");

        if (trackId != VideoTrackId && trackId != AudioTrackId)
        {
            throw new InvalidDataException(
                $"Unsupported MP4 track_ID {trackId}. " +
                $"Expected video={VideoTrackId} or audio={AudioTrackId}."
            );
        }

        _lastMoofTrackId = trackId;

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

        _moof.SetLength(0);
        _moof.Position = 0;

    }

    /// <summary>
    /// Сбрасывает состояние заголовка для следующего top-level box
    /// </summary>
    void ResetBoxState()
    {
        _boxHeaderLength = 0;
        _boxHeaderRequired = 8;

        _currentBoxType = 0;
        _currentBoxRemaining = 0;
        _currentTarget = BoxTarget.None;
    }

    /// <summary>
    /// Оставляет в _deferred только неиспользованный хвост
    /// Вызывается лишь на границе уже готового сегмента
    /// </summary>
    void KeepDeferredRemainder(
        ReadOnlySpan<byte> data,
        int consumed
    )
    {
        int rest = data.Length - consumed;

        if (rest <= 0)
        {
            _deferred.SetLength(0);
            _deferred.Position = 0;
            return;
        }

        if (!_deferred.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array == null)
            throw new InvalidOperationException("Deferred buffer is not accessible.");

        System.Buffer.BlockCopy(
            segment.Array,
            segment.Offset + consumed,
            segment.Array,
            segment.Offset,
            rest
        );

        _deferred.SetLength(rest);
        _deferred.Position = rest;
    }

    /// <summary>
    /// Добавляет оставшуюся часть Gst.Buffer в редкий deferred-хвост
    /// </summary>
    void AppendGstBufferToDeferred(
        Gst.Buffer buffer,
        int offset,
        int count
    )
    {
        while (count > 0)
        {
            int requested = Math.Min(
                _readBuffer.Length,
                count
            );

            nuint copiedValue = buffer.Extract(
                (nuint)offset,
                _readBuffer.AsSpan(0, requested)
            );

            int copied = (int)copiedValue;

            if (copied <= 0)
                return;

            _deferred.Write(
                _readBuffer.AsSpan(0, copied)
            );

            offset += copied;
            count -= copied;

        }
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

        int rootPosition = 0;

        if (!TryReadBox(
            moof,
            ref rootPosition,
            out uint rootType,
            out int moofHeaderSize,
            out ReadOnlySpan<byte> moofBox
        ))
        {
            return false;
        }

        // _moof должен содержать ровно один полный moof
        if (rootType != BoxMoof ||
            rootPosition != moof.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> children = moofBox.Slice(
            moofHeaderSize
        );

        int position = 0;

        while (position < children.Length)
        {
            int childStart = position;

            if (!TryReadBox(
                children,
                ref position,
                out uint childType,
                out int childHeaderSize,
                out ReadOnlySpan<byte> child
            ))
            {
                return false;
            }

            if (childType != BoxTraf)
                continue;

            // Смещения переводим обратно относительно начала moof
            int trafStart = moofHeaderSize + childStart;
            int trafEnd = trafStart + child.Length;

            return ShiftTrafTfdt(
                moof,
                trafStart + childHeaderSize,
                trafEnd,
                offset
            );
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
        if (start < 0 ||
            end < start ||
            end > data.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> traf = data.Slice(
            start,
            end - start
        );

        int position = 0;

        while (position < traf.Length)
        {
            int childStart = position;

            if (!TryReadBox(
                traf,
                ref position,
                out uint type,
                out int headerSize,
                out ReadOnlySpan<byte> box
            ))
            {
                return false;
            }

            if (type != BoxTfdt)
                continue;

            // FullBox:
            // header + version/flags(4) + baseMediaDecodeTime
            if (box.Length < headerSize + 8)
                return false;

            int fullBoxOffset =
                start +
                childStart +
                headerSize;

            byte version = data[fullBoxOffset];

            int valueOffset = fullBoxOffset + 4;

            if (version == 1)
            {
                if (box.Length < headerSize + 12)
                    return false;

                ulong value = BinaryPrimitives.ReadUInt64BigEndian(
                    data.Slice(valueOffset, 8)
                );

                if (ulong.MaxValue - value < offset)
                    return false;

                BinaryPrimitives.WriteUInt64BigEndian(
                    data.Slice(valueOffset, 8),
                    value + offset
                );

                return true;
            }

            if (version == 0)
            {
                uint value = BinaryPrimitives.ReadUInt32BigEndian(
                    data.Slice(valueOffset, 4)
                );

                ulong result = value + offset;

                if (result > uint.MaxValue)
                    return false;

                BinaryPrimitives.WriteUInt32BigEndian(
                    data.Slice(valueOffset, 4),
                    (uint)result
                );

                return true;
            }

            return false;
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

        int rootPosition = 0;

        if (!TryReadBox(
            moof,
            ref rootPosition,
            out uint rootType,
            out int moofHeaderSize,
            out ReadOnlySpan<byte> moofBox
        ))
        {
            return 0;
        }

        if (rootType != BoxMoof ||
            rootPosition != moof.Length)
        {
            return 0;
        }

        ReadOnlySpan<byte> children = moofBox.Slice(
            moofHeaderSize
        );

        int position = 0;

        while (position < children.Length)
        {
            int childStart = position;

            if (!TryReadBox(
                children,
                ref position,
                out uint childType,
                out int childHeaderSize,
                out ReadOnlySpan<byte> child
            ))
            {
                return 0;
            }

            if (childType != BoxTraf)
                continue;

            int trafStart = moofHeaderSize + childStart;
            int trafEnd = trafStart + child.Length;

            uint trackId = GetTrafTrackId(
                moof,
                trafStart + childHeaderSize,
                trafEnd,
                out decodeTime
            );

            if (trackId != 0)
                return trackId;
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

        if (start < 0 ||
            end < start ||
            end > data.Length)
        {
            return 0;
        }

        uint trackId = 0;

        ReadOnlySpan<byte> traf = data.Slice(
            start,
            end - start
        );

        int position = 0;

        while (position < traf.Length)
        {
            if (!TryReadBox(
                traf,
                ref position,
                out uint type,
                out int headerSize,
                out ReadOnlySpan<byte> box
            ))
            {
                return 0;
            }

            if (type == BoxTfhd)
            {
                // FullBox:
                // header + version/flags(4) + track_ID(4)
                if (box.Length < headerSize + 8)
                    return 0;

                trackId = BinaryPrimitives.ReadUInt32BigEndian(
                    box.Slice(headerSize + 4, 4)
                );
            }
            else if (type == BoxTfdt)
            {
                if (box.Length < headerSize + 8)
                    return 0;

                byte version = box[headerSize];

                // После version/flags
                int valueOffset = headerSize + 4;

                if (version == 1)
                {
                    if (box.Length < headerSize + 12)
                        return 0;

                    decodeTime = BinaryPrimitives.ReadUInt64BigEndian(
                        box.Slice(valueOffset, 8)
                    );
                }
                else if (version == 0)
                {
                    decodeTime = BinaryPrimitives.ReadUInt32BigEndian(
                        box.Slice(valueOffset, 4)
                    );
                }
                else
                {
                    return 0;
                }
            }
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
        int position = 0;

        while (TryReadBox(
            init,
            ref position,
            out uint type,
            out int boxHeaderSize,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type != BoxMoov)
                continue;

            int moovPosition = boxHeaderSize;

            while (TryReadBox(
                box,
                ref moovPosition,
                out uint childType,
                out int childHeaderSize,
                out ReadOnlySpan<byte> child
            ))
            {
                if (childType != BoxTrak)
                    continue;

                uint trackId = 0;
                uint timescale = 0;

                int trakPosition = childHeaderSize;

                while (TryReadBox(
                    child,
                    ref trakPosition,
                    out uint trakType,
                    out int trakBoxHeaderSize,
                    out ReadOnlySpan<byte> trakBox
                ))
                {
                    if (trakType == BoxTkhd)
                    {
                        if (trakBox.Length <= trakBoxHeaderSize)
                            continue;

                        byte version = trakBox[trakBoxHeaderSize];

                        int trackIdOffset;

                        if (version == 1)
                        {
                            // version/flags(4) +
                            // creation(8) + modification(8)
                            trackIdOffset = trakBoxHeaderSize + 20;
                        }
                        else if (version == 0)
                        {
                            // version/flags(4) +
                            // creation(4) + modification(4)
                            trackIdOffset = trakBoxHeaderSize + 12;
                        }
                        else
                        {
                            continue;
                        }

                        if (trakBox.Length >= trackIdOffset + 4)
                        {
                            trackId = BinaryPrimitives.ReadUInt32BigEndian(
                                trakBox.Slice(trackIdOffset, 4)
                            );
                        }
                    }
                    else if (trakType == BoxMdia)
                    {
                        timescale = GetMdiaTimescale(
                            trakBox
                        );
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
        int rootPosition = 0;

        if (!TryReadBox(
            mdia,
            ref rootPosition,
            out uint rootType,
            out int mdiaHeaderSize,
            out ReadOnlySpan<byte> mdiaBox
        ))
        {
            return 0;
        }

        if (rootType != BoxMdia ||
            rootPosition != mdia.Length)
        {
            return 0;
        }

        int position = mdiaHeaderSize;

        while (TryReadBox(
            mdiaBox,
            ref position,
            out uint type,
            out int headerSize,
            out ReadOnlySpan<byte> box
        ))
        {
            if (type != BoxMdhd)
                continue;

            if (box.Length <= headerSize)
                return 0;

            byte version = box[headerSize];

            int timescaleOffset;

            if (version == 1)
            {
                // version/flags(4) +
                // creation(8) + modification(8)
                timescaleOffset = headerSize + 20;
            }
            else if (version == 0)
            {
                // version/flags(4) +
                // creation(4) + modification(4)
                timescaleOffset = headerSize + 12;
            }
            else
            {
                return 0;
            }

            if (box.Length < timescaleOffset + 4)
                return 0;

            return BinaryPrimitives.ReadUInt32BigEndian(
                box.Slice(timescaleOffset, 4)
            );
        }

        return 0;
    }

    /// <summary>
    /// Читает один MP4 box и возвращает фактический размер заголовка:
    /// 8 байт для обычного box, 16 байт для extended-size box
    /// </summary>
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

        if ((uint)start > (uint)data.Length ||
            data.Length - start < 8)
        {
            return false;
        }

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(
            data.Slice(start, 4)
        );

        type = BinaryPrimitives.ReadUInt32BigEndian(
            data.Slice(start + 4, 4)
        );

        ulong size = size32;
        headerSize = 8;

        // extended-size box
        if (size32 == 1)
        {
            if (data.Length - start < 16)
                return false;

            size = BinaryPrimitives.ReadUInt64BigEndian(
                data.Slice(start + 8, 8)
            );

            headerSize = 16;
        }
        // Внутри уже готового parent span размер до конца parent известен
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

        int boxSize = (int)size;

        box = data.Slice(start, boxSize);
        position = start + boxSize;

        return true;
    }

    /// <summary>
    /// Возвращает true, если deferred сформировал полный Segment
    /// Возвращает false, если deferred пуст или для завершения Segment требуется следующий Gst.Buffer
    ///
    /// Исключение означает повреждённый поток либо нарушение внутреннего состояния парсера, продолжать разбор небезопасно
    /// </summary>
    public bool TryProcessDeferred()
    {
        if (_deferred.Length <= 0)
            return false;

        int length = (int)_deferred.Length;

        ReadOnlySpan<byte> data;

        // Для созданного нами MemoryStream этот путь должен работать всегда
        // ToArray оставляем как безопасный fallback, чтобы отсутствие открытого внутреннего массива не убивало задачу
        byte[] copy = null;

        if (_deferred.TryGetBuffer(out ArraySegment<byte> segment) && segment.Array != null)
        {
            data = segment.Array.AsSpan(
                segment.Offset,
                length
            );
        }
        else
        {
            copy = _deferred.ToArray();
            data = copy;
        }

        int consumed = ProcessBytes(
            data,
            out bool segmentCompleted
        );

        if (segmentCompleted)
        {
            KeepDeferredRemainder(
                data,
                consumed
            );

            return true;
        }

        /*
         * Обычный сценарий нехватки данных:
         * ProcessBytes поглотил весь deferred, а частичный box сохранил в _boxHeader, _moof, _audioPart, _videoPart либо в состоянии mdat.
         *
         * Теперь можно читать следующий Gst.Buffer.
         */
        if (consumed == length)
        {
            _deferred.SetLength(0);
            _deferred.Position = 0;

            return false;
        }

        /*
         * ProcessBytes без готового Segment обязан поглощать весь span.
         * Если он остановился раньше, часть входных данных останется необработанной, но состояние парсера уже изменено. 
         * Продолжение с новым Gst.Buffer нарушит порядок байтов.
         */
        throw new InvalidOperationException(
            $"MP4 parser consumed only {consumed} of " +
            $"{length} deferred bytes without completing a segment."
        );
    }

    public void Dispose()
    {
        _audioPart?.Dispose();
        _audioPart = null;

        _videoPart?.Dispose();
        _videoPart = null;

        _deferred?.Dispose();
        _deferred = null;

        _moof?.Dispose();
        _moof = null;

        _init?.Dispose();
        _init = null;
    }
}
