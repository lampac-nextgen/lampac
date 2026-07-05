using Gst;
using System;
using System.Runtime.InteropServices;

namespace GStreamer.Models;

public class BusReader
{
    public static readonly uint Error = Convert.ToUInt32(MessageType.Error);
    public static readonly uint Eos = Convert.ToUInt32(MessageType.Eos);
    public static readonly uint AsyncDone = Convert.ToUInt32(MessageType.AsyncDone);

    [StructLayout(LayoutKind.Sequential)]
    struct GstMiniObjectRaw
    {
        public UIntPtr Type;
        public int Refcount;
        public int Lockstate;
        public uint Flags;

        public IntPtr Copy;
        public IntPtr Dispose;
        public IntPtr Free;

        public uint NQData;
        public IntPtr QData;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GstMessageRaw
    {
        public GstMiniObjectRaw MiniObject;
        public uint Type;
    }

    static readonly int MessageTypeOffset =
        (int)Marshal.OffsetOf<GstMessageRaw>(nameof(GstMessageRaw.Type));

    public static uint GetType(Message msg)
    {
        if (msg == null)
            return 0;

        var ptr = msg.Handle.DangerousGetHandle();

        if (ptr == IntPtr.Zero)
            return 0;

        return unchecked((uint)Marshal.ReadInt32(ptr, MessageTypeOffset));
    }
}
