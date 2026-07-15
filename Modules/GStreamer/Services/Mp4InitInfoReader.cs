using GStreamer.Models;
using System;
using System.Buffers.Binary;
using System.Globalization;

namespace GStreamer.Services;

internal static class Mp4InitInfoReader
{
    public static HlsVariantInfo Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return null;

        try
        {
            return ReadCore(data);
        }
        catch
        {
            return null;
        }
    }

    static HlsVariantInfo ReadCore(ReadOnlySpan<byte> data)
    {
        if (!TryFindChild(data, 0, data.Length, "moov", out Box moov))
            return null;

        string videoCodec = null;
        string audioCodec = null;
        string videoRange = null;
        int width = 0;
        int height = 0;

        int cursor = moov.PayloadStart;
        while (TryReadBox(data, ref cursor, moov.End, out Box box))
        {
            if (box.Type != "trak")
                continue;

            TrackResult track = ReadTrack(data, box);
            if (track == null || string.IsNullOrEmpty(track.Codec))
                continue;

            if (track.IsVideo && videoCodec == null)
            {
                videoCodec = track.Codec;
                videoRange = track.VideoRange;
                width = track.Width;
                height = track.Height;
            }
            else if (!track.IsVideo && audioCodec == null)
            {
                audioCodec = track.Codec;
            }
        }

        string codecs = videoCodec;
        if (!string.IsNullOrEmpty(audioCodec))
            codecs = string.IsNullOrEmpty(codecs) ? audioCodec : codecs + "," + audioCodec;

        return new HlsVariantInfo
        {
            Codecs = codecs,
            VideoRange = videoRange,
            Width = width,
            Height = height
        };
    }

    static TrackResult ReadTrack(ReadOnlySpan<byte> data, Box trak)
    {
        if (!TryFindChild(data, trak.PayloadStart, trak.End, "mdia", out Box mdia) ||
            !TryFindChild(data, mdia.PayloadStart, mdia.End, "hdlr", out Box hdlr) ||
            hdlr.PayloadStart > hdlr.End - 12)
            return null;

        string handler = FourCc(data.Slice(hdlr.PayloadStart + 8, 4));
        bool isVideo = handler == "vide";
        if (!isVideo && handler != "soun")
            return null;

        if (!TryFindChild(data, mdia.PayloadStart, mdia.End, "minf", out Box minf) ||
            !TryFindChild(data, minf.PayloadStart, minf.End, "stbl", out Box stbl) ||
            !TryFindChild(data, stbl.PayloadStart, stbl.End, "stsd", out Box stsd) ||
            stsd.PayloadStart > stsd.End - 8)
            return null;

        int entryCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(stsd.PayloadStart + 4, 4)));
        int cursor = stsd.PayloadStart + 8;
        for (int i = 0; i < entryCount && TryReadBox(data, ref cursor, stsd.End, out Box entry); i++)
        {
            TrackResult result = isVideo ? ReadVideoEntry(data, entry) : ReadAudioEntry(data, entry);
            if (result != null)
                return result;
        }

        return null;
    }

    static TrackResult ReadVideoEntry(ReadOnlySpan<byte> data, Box entry)
    {
        int fixedEnd = entry.PayloadStart + 78;
        if (fixedEnd > entry.End)
            return null;

        string configType;
        switch (entry.Type)
        {
            case "avc1":
            case "avc3": configType = "avcC"; break;
            case "hvc1":
            case "hev1": configType = "hvcC"; break;
            case "av01": configType = "av1C"; break;
            case "vp09": configType = "vpcC"; break;
            default: return null;
        }

        if (!TryFindChild(data, fixedEnd, entry.End, configType, out Box config))
            return null;

        ReadOnlySpan<byte> payload = data.Slice(config.PayloadStart, config.End - config.PayloadStart);
        string codec = entry.Type switch
        {
            "avc1" or "avc3" => ReadAvcCodec(entry.Type, payload),
            "hvc1" or "hev1" => ReadHevcCodec(entry.Type, payload),
            "av01" => ReadAv1Codec(payload),
            "vp09" => ReadVp9Codec(payload),
            _ => null
        };

        if (codec == null)
            return null;

        string range = null;
        if (TryFindChild(data, fixedEnd, entry.End, "colr", out Box colr))
            range = ReadVideoRange(data.Slice(colr.PayloadStart, colr.End - colr.PayloadStart));

        return new TrackResult
        {
            IsVideo = true,
            Codec = codec,
            VideoRange = range,
            Width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(entry.PayloadStart + 24, 2)),
            Height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(entry.PayloadStart + 26, 2))
        };
    }

    static TrackResult ReadAudioEntry(ReadOnlySpan<byte> data, Box entry)
    {
        if (entry.Type != "mp4a" || entry.PayloadStart > entry.End - 28)
            return null;

        ushort version = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(entry.PayloadStart + 8, 2));
        int childStart = entry.PayloadStart + (version == 1 ? 44 : version == 2 ? 64 : 28);
        if (childStart > entry.End || !TryFindChild(data, childStart, entry.End, "esds", out Box esds))
            return null;

        string codec = ReadAacCodec(data.Slice(esds.PayloadStart, esds.End - esds.PayloadStart));
        return codec == null ? null : new TrackResult { Codec = codec };
    }

    static string ReadAvcCodec(string sampleEntry, ReadOnlySpan<byte> avcC)
    {
        if (avcC.Length < 4)
            return null;

        return string.Create(CultureInfo.InvariantCulture, $"{sampleEntry}.{avcC[1]:X2}{avcC[2]:X2}{avcC[3]:X2}");
    }

    static string ReadHevcCodec(string sampleEntry, ReadOnlySpan<byte> hvcC)
    {
        if (hvcC.Length < 13)
            return null;

        byte profileByte = hvcC[1];
        int profileSpace = profileByte >> 6;
        char profileSpaceChar = profileSpace switch { 1 => 'A', 2 => 'B', 3 => 'C', _ => '\0' };
        int profileIdc = profileByte & 0x1f;
        uint compatibility = ReverseBits(BinaryPrimitives.ReadUInt32BigEndian(hvcC.Slice(2, 4)));
        char tier = (profileByte & 0x20) == 0 ? 'L' : 'H';

        string result = sampleEntry + "." + (profileSpaceChar == '\0' ? "" : profileSpaceChar.ToString()) +
            profileIdc.ToString(CultureInfo.InvariantCulture) + "." + compatibility.ToString(CultureInfo.InvariantCulture) +
            "." + tier + hvcC[12].ToString(CultureInfo.InvariantCulture);

        int lastConstraint = 11;
        while (lastConstraint >= 6 && hvcC[lastConstraint] == 0)
            lastConstraint--;
        for (int i = 6; i <= lastConstraint; i++)
            result += "." + hvcC[i].ToString("X2", CultureInfo.InvariantCulture);

        return result;
    }

    static string ReadAv1Codec(ReadOnlySpan<byte> av1C)
    {
        if (av1C.Length < 3 || (av1C[0] & 0x80) == 0)
            return null;

        int profile = av1C[1] >> 5;
        int level = av1C[1] & 0x1f;
        char tier = (av1C[2] & 0x80) == 0 ? 'M' : 'H';
        int bitDepth = (av1C[2] & 0x40) == 0 ? 8 : profile == 2 && (av1C[2] & 0x20) != 0 ? 12 : 10;
        return string.Create(CultureInfo.InvariantCulture, $"av01.{profile}.{level:00}{tier}.{bitDepth:00}");
    }

    static string ReadVp9Codec(ReadOnlySpan<byte> vpcC)
    {
        int offset = vpcC.Length >= 7 && vpcC[0] <= 1 && vpcC[1] == 0 && vpcC[2] == 0 && vpcC[3] == 0 ? 4 : 0;
        if (vpcC.Length < offset + 3)
            return null;

        int bitDepth = vpcC[offset + 2] > 16 ? vpcC[offset + 2] >> 4 : vpcC[offset + 2];
        return string.Create(CultureInfo.InvariantCulture, $"vp09.{vpcC[offset]:00}.{vpcC[offset + 1]:00}.{bitDepth:00}");
    }

    static string ReadAacCodec(ReadOnlySpan<byte> esds)
    {
        for (int i = Math.Min(4, esds.Length); i < esds.Length - 2; i++)
        {
            if (esds[i] != 0x05)
                continue;

            int cursor = i + 1;
            if (!TryReadDescriptorLength(esds, ref cursor, out int length) || length < 2 || cursor > esds.Length - length)
                continue;

            int objectType = esds[cursor] >> 3;
            if (objectType == 31)
                objectType = 32 + ((esds[cursor] & 0x07) << 3) + (esds[cursor + 1] >> 5);

            return "mp4a.40." + objectType.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    static bool TryReadDescriptorLength(ReadOnlySpan<byte> data, ref int cursor, out int length)
    {
        length = 0;
        for (int i = 0; i < 4 && cursor < data.Length; i++)
        {
            byte value = data[cursor++];
            length = (length << 7) | (value & 0x7f);
            if ((value & 0x80) == 0)
                return true;
        }
        return false;
    }

    static string ReadVideoRange(ReadOnlySpan<byte> colr)
    {
        if (colr.Length < 10)
            return null;

        string type = FourCc(colr.Slice(0, 4));
        if (type != "nclx" && type != "nclc")
            return null;

        ushort transfer = BinaryPrimitives.ReadUInt16BigEndian(colr.Slice(6, 2));
        return transfer switch
        {
            16 => "PQ",
            18 => "HLG",
            1 or 4 or 5 or 6 or 7 or 8 or 13 or 14 or 15 => "SDR",
            _ => null
        };
    }

    static uint ReverseBits(uint value)
    {
        value = ((value & 0x55555555u) << 1) | ((value >> 1) & 0x55555555u);
        value = ((value & 0x33333333u) << 2) | ((value >> 2) & 0x33333333u);
        value = ((value & 0x0f0f0f0fu) << 4) | ((value >> 4) & 0x0f0f0f0fu);
        value = ((value & 0x00ff00ffu) << 8) | ((value >> 8) & 0x00ff00ffu);
        return (value << 16) | (value >> 16);
    }

    static bool TryFindChild(ReadOnlySpan<byte> data, int start, int end, string type, out Box result)
    {
        int cursor = start;
        while (TryReadBox(data, ref cursor, end, out Box box))
        {
            if (box.Type == type)
            {
                result = box;
                return true;
            }
        }

        result = default;
        return false;
    }

    static bool TryReadBox(ReadOnlySpan<byte> data, ref int cursor, int end, out Box box)
    {
        box = default;
        if (cursor < 0 || end > data.Length || cursor > end - 8)
        {
            cursor = end;
            return false;
        }

        int start = cursor;
        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(start, 4));
        string type = FourCc(data.Slice(start + 4, 4));
        int headerSize = 8;
        ulong size = size32;
        if (size32 == 1)
        {
            if (start > end - 16)
            {
                cursor = end;
                return false;
            }
            size = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(start + 8, 8));
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            size = (ulong)(end - start);
        }

        if (size < (ulong)headerSize || size > (ulong)(end - start) || size > int.MaxValue)
        {
            cursor = end;
            return false;
        }

        int boxEnd = start + (int)size;
        box = new Box(type, start + headerSize, boxEnd);
        cursor = boxEnd;
        return true;
    }

    static string FourCc(ReadOnlySpan<byte> value)
        => new string(new[] { (char)value[0], (char)value[1], (char)value[2], (char)value[3] });

    readonly struct Box
    {
        public Box(string type, int payloadStart, int end)
        {
            Type = type;
            PayloadStart = payloadStart;
            End = end;
        }

        public string Type { get; }
        public int PayloadStart { get; }
        public int End { get; }
    }

    sealed class TrackResult
    {
        public bool IsVideo { get; set; }
        public string Codec { get; set; }
        public string VideoRange { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
