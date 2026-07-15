using GStreamer.Models;
using Shared.Models.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace GStreamer.Services;

static class MatroskaCueReader
{
    const ulong EbmlId = 0x1A45DFA3;
    const ulong SegmentId = 0x18538067;
    const ulong SeekHeadId = 0x114D9B74;
    const ulong SeekId = 0x4DBB;
    const ulong SeekEntryId = 0x53AB;
    const ulong SeekPositionId = 0x53AC;
    const ulong InfoId = 0x1549A966;
    const ulong TimestampScaleId = 0x2AD7B1;
    const ulong TracksId = 0x1654AE6B;
    const ulong TrackEntryId = 0xAE;
    const ulong TrackNumberId = 0xD7;
    const ulong TrackTypeId = 0x83;
    const ulong CuesId = 0x1C53BB6B;
    const ulong CuePointId = 0xBB;
    const ulong CueTimeId = 0xB3;
    const ulong CueTrackPositionsId = 0xB7;
    const ulong CueTrackId = 0xF7;
    const ulong CueClusterPositionId = 0xF1;

    const ulong DefaultTimestampScaleNs = 1_000_000;
    const int PrefixLength = 4 * 1024 * 1024;
    const int MaxMetadataLength = 8 * 1024 * 1024;
    const int MaxCuesLength = 64 * 1024 * 1024;
    const int MaxCuePoints = 1_000_000;

    readonly record struct Element(
        ulong Id,
        int Offset,
        int DataOffset,
        ulong Size,
        bool UnknownSize
    )
    {
        public int HeaderLength => DataOffset - Offset;

        public ulong EndOffset => checked((ulong)DataOffset + Size);
    }

    public static async Task<CueTimeline> Read(
        string sourceUrl,
        long? contentLength,
        long durationNs
    )
    {
        if (string.IsNullOrEmpty(sourceUrl) || durationNs <= 0)
            return null;

        try
        {
            int prefixLength = contentLength is > 0
                ? (int)Math.Min(contentLength.Value, PrefixLength)
                : PrefixLength;

            byte[] prefix = await ReadRange(sourceUrl, 0, prefixLength).ConfigureAwait(false);
            if (prefix == null || !TryFindSegment(prefix, out Element segment))
                return null;

            long segmentDataOffset = segment.DataOffset;
            var seekPositions = new Dictionary<ulong, ulong>();

            ulong timestampScaleNs = DefaultTimestampScaleNs;
            ulong videoTrackNumber = 0;

            ParseSegmentPrefix(
                prefix,
                segment.DataOffset,
                seekPositions,
                ref timestampScaleNs,
                ref videoTrackNumber
            );

            if (videoTrackNumber == 0 && seekPositions.TryGetValue(TracksId, out ulong tracksPosition))
            {
                byte[] tracks = await ReadElement(
                    sourceUrl,
                    segmentDataOffset,
                    tracksPosition,
                    TracksId,
                    MaxMetadataLength,
                    contentLength
                ).ConfigureAwait(false);

                if (tracks != null && TryReadElement(tracks, 0, out Element tracksElement))
                    videoTrackNumber = ParseVideoTrackNumber(tracks, tracksElement);
            }

            if (seekPositions.TryGetValue(InfoId, out ulong infoPosition))
            {
                byte[] info = await ReadElement(
                    sourceUrl,
                    segmentDataOffset,
                    infoPosition,
                    InfoId,
                    MaxMetadataLength,
                    contentLength
                ).ConfigureAwait(false);

                if (info != null && TryReadElement(info, 0, out Element infoElement))
                    ParseTimestampScale(info, infoElement, ref timestampScaleNs);
            }

            if (videoTrackNumber == 0 ||
                timestampScaleNs == 0 ||
                !seekPositions.TryGetValue(CuesId, out ulong cuesPosition))
            {
                return null;
            }

            byte[] cues = await ReadElement(
                sourceUrl,
                segmentDataOffset,
                cuesPosition,
                CuesId,
                MaxCuesLength,
                contentLength
            ).ConfigureAwait(false);

            if (cues == null || !TryReadElement(cues, 0, out Element cuesElement))
                return null;

            return ParseCueTimeline(
                cues,
                cuesElement,
                videoTrackNumber,
                timestampScaleNs,
                checked((ulong)durationNs)
            );
        }
        catch
        {
            return null;
        }
    }

    static void ParseSegmentPrefix(
        ReadOnlySpan<byte> data,
        int offset,
        Dictionary<ulong, ulong> seekPositions,
        ref ulong timestampScaleNs,
        ref ulong videoTrackNumber
    )
    {
        while (offset < data.Length && TryReadElement(data, offset, out Element element))
        {
            if (element.UnknownSize || element.EndOffset > (ulong)data.Length)
                return;

            switch (element.Id)
            {
                case SeekHeadId:
                    ParseSeekHead(data, element, seekPositions);
                    break;

                case InfoId:
                    ParseTimestampScale(data, element, ref timestampScaleNs);
                    break;

                case TracksId:
                    videoTrackNumber = ParseVideoTrackNumber(data, element);
                    break;
            }

            offset = checked((int)element.EndOffset);
        }
    }

    static void ParseSeekHead(
        ReadOnlySpan<byte> data,
        Element seekHead,
        Dictionary<ulong, ulong> positions
    )
    {
        int offset = seekHead.DataOffset;
        int end = checked((int)seekHead.EndOffset);

        while (offset < end && TryReadElement(data, offset, out Element seek))
        {
            if (seek.Id != SeekId || seek.UnknownSize || seek.EndOffset > (ulong)end)
            {
                offset = NextOffset(seek, end);
                continue;
            }

            ulong targetId = 0;
            ulong position = 0;
            bool hasPosition = false;
            int childOffset = seek.DataOffset;
            int childEnd = checked((int)seek.EndOffset);

            while (childOffset < childEnd && TryReadElement(data, childOffset, out Element child))
            {
                if (child.UnknownSize || child.EndOffset > (ulong)childEnd)
                    break;

                if (child.Id == SeekEntryId && child.Size is > 0 and <= 4)
                    targetId = ReadUnsigned(data, child);
                else if (child.Id == SeekPositionId && child.Size is > 0 and <= 8)
                {
                    position = ReadUnsigned(data, child);
                    hasPosition = true;
                }

                childOffset = checked((int)child.EndOffset);
            }

            if (targetId != 0 && hasPosition)
                positions.TryAdd(targetId, position);

            offset = checked((int)seek.EndOffset);
        }
    }

    static void ParseTimestampScale(
        ReadOnlySpan<byte> data,
        Element info,
        ref ulong timestampScaleNs
    )
    {
        int offset = info.DataOffset;
        int end = checked((int)info.EndOffset);

        while (offset < end && TryReadElement(data, offset, out Element child))
        {
            if (child.UnknownSize || child.EndOffset > (ulong)end)
                return;

            if (child.Id == TimestampScaleId && child.Size is > 0 and <= 8)
            {
                ulong value = ReadUnsigned(data, child);
                if (value > 0)
                    timestampScaleNs = value;

                return;
            }

            offset = checked((int)child.EndOffset);
        }
    }

    static ulong ParseVideoTrackNumber(ReadOnlySpan<byte> data, Element tracks)
    {
        int offset = tracks.DataOffset;
        int end = checked((int)tracks.EndOffset);

        while (offset < end && TryReadElement(data, offset, out Element entry))
        {
            if (entry.UnknownSize || entry.EndOffset > (ulong)end)
                return 0;

            if (entry.Id == TrackEntryId)
            {
                ulong trackNumber = 0;
                ulong trackType = 0;
                int childOffset = entry.DataOffset;
                int childEnd = checked((int)entry.EndOffset);

                while (childOffset < childEnd && TryReadElement(data, childOffset, out Element child))
                {
                    if (child.UnknownSize || child.EndOffset > (ulong)childEnd)
                        break;

                    if (child.Id == TrackNumberId && child.Size is > 0 and <= 8)
                        trackNumber = ReadUnsigned(data, child);
                    else if (child.Id == TrackTypeId && child.Size is > 0 and <= 8)
                        trackType = ReadUnsigned(data, child);

                    childOffset = checked((int)child.EndOffset);
                }

                if (trackType == 1 && trackNumber > 0)
                    return trackNumber;
            }

            offset = checked((int)entry.EndOffset);
        }

        return 0;
    }

    static CueTimeline ParseCueTimeline(
        ReadOnlySpan<byte> data,
        Element cues,
        ulong videoTrackNumber,
        ulong timestampScaleNs,
        ulong durationNs
    )
    {
        var cueTimes = new List<ulong>();
        int offset = cues.DataOffset;
        int end = checked((int)cues.EndOffset);

        while (offset < end && TryReadElement(data, offset, out Element point))
        {
            if (point.UnknownSize || point.EndOffset > (ulong)end)
                return null;

            if (point.Id == CuePointId &&
                TryReadVideoCueTime(data, point, videoTrackNumber, out ulong cueTime))
            {
                UInt128 timeNs = (UInt128)cueTime * timestampScaleNs;
                if (timeNs < durationNs && timeNs <= ulong.MaxValue)
                    cueTimes.Add((ulong)timeNs);

                if (cueTimes.Count > MaxCuePoints)
                    return null;
            }

            offset = checked((int)point.EndOffset);
        }

        if (cueTimes.Count < 2)
            return null;

        cueTimes.Sort();

        var segments = new List<CueSegment>(cueTimes.Count);
        ulong previous = cueTimes[0];

        for (int i = 1; i < cueTimes.Count; i++)
        {
            ulong current = cueTimes[i];
            if (current <= previous)
                continue;

            segments.Add(new CueSegment(previous, current));
            previous = current;
        }

        if (durationNs > previous)
            segments.Add(new CueSegment(previous, durationNs));

        return segments.Count > 0
            ? new CueTimeline(segments.ToArray(), timestampScaleNs)
            : null;
    }

    static bool TryReadVideoCueTime(
        ReadOnlySpan<byte> data,
        Element point,
        ulong videoTrackNumber,
        out ulong cueTime
    )
    {
        cueTime = 0;
        bool hasCueTime = false;
        bool hasVideoPosition = false;
        int offset = point.DataOffset;
        int end = checked((int)point.EndOffset);

        while (offset < end && TryReadElement(data, offset, out Element child))
        {
            if (child.UnknownSize || child.EndOffset > (ulong)end)
                return false;

            if (child.Id == CueTimeId && child.Size is > 0 and <= 8)
            {
                cueTime = ReadUnsigned(data, child);
                hasCueTime = true;
            }
            else if (child.Id == CueTrackPositionsId &&
                     HasVideoCuePosition(data, child, videoTrackNumber))
            {
                hasVideoPosition = true;
            }

            offset = checked((int)child.EndOffset);
        }

        return hasCueTime && hasVideoPosition;
    }

    static bool HasVideoCuePosition(
        ReadOnlySpan<byte> data,
        Element positions,
        ulong videoTrackNumber
    )
    {
        ulong track = 0;
        bool hasClusterPosition = false;
        int offset = positions.DataOffset;
        int end = checked((int)positions.EndOffset);

        while (offset < end && TryReadElement(data, offset, out Element child))
        {
            if (child.UnknownSize || child.EndOffset > (ulong)end)
                return false;

            if (child.Id == CueTrackId && child.Size is > 0 and <= 8)
                track = ReadUnsigned(data, child);
            else if (child.Id == CueClusterPositionId && child.Size is > 0 and <= 8)
            {
                ReadUnsigned(data, child);
                hasClusterPosition = true;
            }

            offset = checked((int)child.EndOffset);
        }

        return track == videoTrackNumber && hasClusterPosition;
    }

    static async Task<byte[]> ReadElement(
        string sourceUrl,
        long segmentDataOffset,
        ulong relativePosition,
        ulong expectedId,
        int maxLength,
        long? contentLength
    )
    {
        if (relativePosition > long.MaxValue ||
            segmentDataOffset > long.MaxValue - (long)relativePosition)
        {
            return null;
        }

        long absoluteOffset = segmentDataOffset + (long)relativePosition;
        byte[] header = await ReadRange(sourceUrl, absoluteOffset, 16).ConfigureAwait(false);

        if (header == null ||
            !TryReadElement(header, 0, out Element element) ||
            element.Id != expectedId ||
            element.UnknownSize ||
            element.Size > (ulong)maxLength)
        {
            return null;
        }

        ulong totalLength = checked((ulong)element.HeaderLength + element.Size);
        if (totalLength > int.MaxValue)
            return null;

        if (contentLength is > 0 &&
            (absoluteOffset < 0 ||
             absoluteOffset > contentLength.Value ||
             totalLength > (ulong)(contentLength.Value - absoluteOffset)))
        {
            return null;
        }

        return await ReadRange(
            sourceUrl,
            absoluteOffset,
            checked((int)totalLength)
        ).ConfigureAwait(false);
    }

    static async Task<byte[]> ReadRange(string sourceUrl, long offset, int length)
    {
        if (offset < 0 || length <= 0 || offset > long.MaxValue - length)
            return null;

        byte[] result = GC.AllocateUninitializedArray<byte>(length);
        int totalRead = 0;

        var headers = new List<HeadersModel>(2)
        {
            new("Accept-Encoding", "identity"),
            new("Range", $"bytes={offset}-{offset + length - 1}")
        };

        var request = await Http.BaseGetReaderAsync(
            async value =>
            {
                while (totalRead < result.Length)
                {
                    int read = await value.stream.ReadAsync(
                        result.AsMemory(totalRead, result.Length - totalRead),
                        value.ct
                    ).ConfigureAwait(false);

                    if (read == 0)
                        break;

                    totalRead += read;
                }
            },
            sourceUrl,
            MaxResponseContentBufferSize: length,
            timeoutSeconds: 45,
            headers: headers,
            statusCodeOK: false
        ).ConfigureAwait(false);

        HttpStatusCode status = request.response?.StatusCode ?? (HttpStatusCode)0;
        if (!request.success ||
            totalRead != length ||
            (status != HttpStatusCode.PartialContent &&
             !(offset == 0 && status == HttpStatusCode.OK)))
        {
            return null;
        }

        if (status == HttpStatusCode.PartialContent)
        {
            var range = request.response.Content?.Headers?.ContentRange;
            if (range?.From != offset)
                return null;
        }

        return result;
    }

    static bool TryFindSegment(ReadOnlySpan<byte> data, out Element segment)
    {
        segment = default;
        int offset = 0;

        if (!TryReadElement(data, offset, out Element ebml) ||
            ebml.Id != EbmlId ||
            ebml.UnknownSize ||
            ebml.EndOffset > (ulong)data.Length)
        {
            return false;
        }

        offset = checked((int)ebml.EndOffset);

        if (!TryReadElement(data, offset, out segment) || segment.Id != SegmentId)
        {
            segment = default;
            return false;
        }

        return true;
    }

    static bool TryReadElement(ReadOnlySpan<byte> data, int offset, out Element element)
    {
        element = default;

        if ((uint)offset >= (uint)data.Length ||
            !TryReadVintLength(data[offset], 4, out int idLength) ||
            data.Length - offset < idLength + 1)
        {
            return false;
        }

        ulong id = 0;
        for (int i = 0; i < idLength; i++)
            id = (id << 8) | data[offset + i];

        int sizeOffset = offset + idLength;
        if (!TryReadVintLength(data[sizeOffset], 8, out int sizeLength) ||
            data.Length - sizeOffset < sizeLength)
        {
            return false;
        }

        byte marker = (byte)(0x80 >> (sizeLength - 1));
        ulong size = (ulong)(data[sizeOffset] & (marker - 1));

        for (int i = 1; i < sizeLength; i++)
            size = (size << 8) | data[sizeOffset + i];

        int valueBits = sizeLength * 7;
        ulong unknownValue = valueBits == 56
            ? 0x00FF_FFFF_FFFF_FFFFUL
            : (1UL << valueBits) - 1;

        int dataOffset = sizeOffset + sizeLength;

        element = new Element(
            id,
            offset,
            dataOffset,
            size,
            size == unknownValue
        );

        return true;
    }

    static bool TryReadVintLength(byte first, int maxLength, out int length)
    {
        length = 1;
        byte mask = 0x80;

        while (length <= maxLength && (first & mask) == 0)
        {
            mask >>= 1;
            length++;
        }

        return length <= maxLength;
    }

    static ulong ReadUnsigned(ReadOnlySpan<byte> data, Element element)
    {
        if (element.Size is 0 or > 8 || element.EndOffset > (ulong)data.Length)
            throw new InvalidDataException("Invalid EBML unsigned integer.");

        ulong value = 0;
        int end = checked((int)element.EndOffset);

        for (int i = element.DataOffset; i < end; i++)
            value = (value << 8) | data[i];

        return value;
    }

    static int NextOffset(Element element, int limit)
    {
        if (element.UnknownSize || element.EndOffset > (ulong)limit)
            return limit;

        return checked((int)element.EndOffset);
    }
}
