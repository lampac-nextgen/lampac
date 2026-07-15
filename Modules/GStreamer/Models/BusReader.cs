using Gst;
using System;
using System.Runtime.InteropServices;

namespace GStreamer.Models;

public class BusReader
{
    [DllImport("libgstreamer-1.0-0.dll", EntryPoint = "gst_message_parse_error", CallingConvention = CallingConvention.Cdecl)]
    static extern void gst_message_parse_error(
        IntPtr message,
        out GLib.Internal.ErrorOwnedHandle error,
        out GLib.Internal.NullableUtf8StringOwnedHandle debug
    );

    public static readonly uint Error = Convert.ToUInt32(MessageType.Error);
    public static readonly uint Eos = Convert.ToUInt32(MessageType.Eos);
    public static readonly uint AsyncDone = Convert.ToUInt32(MessageType.AsyncDone);
    public static readonly uint StateChanged = Convert.ToUInt32(MessageType.StateChanged);

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

    public static bool TryParseError(Message msg, out string error, out string debug)
    {
        error = null;
        debug = null;

        if (msg == null || GetType(msg) != Error)
            return false;

        IntPtr handle = msg.Handle.DangerousGetHandle();
        if (handle == IntPtr.Zero)
            return false;

        GLib.Internal.ErrorOwnedHandle errorHandle = null;
        GLib.Internal.NullableUtf8StringOwnedHandle debugHandle = null;
        GLib.GException exception = null;

        try
        {
            gst_message_parse_error(handle, out errorHandle, out debugHandle);

            if (errorHandle != null && !errorHandle.IsInvalid)
            {
                exception = new GLib.GException(errorHandle);
                errorHandle = null;
                error = exception.Message;
            }

            if (debugHandle != null && !debugHandle.IsInvalid)
                debug = debugHandle.ConvertToString();

            return !string.IsNullOrEmpty(error) || !string.IsNullOrEmpty(debug);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            exception?.Dispose();
            errorHandle?.Dispose();
            debugHandle?.Dispose();
        }
    }
}
