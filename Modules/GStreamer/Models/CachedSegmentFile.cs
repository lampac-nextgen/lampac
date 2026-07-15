using Microsoft.Win32.SafeHandles;
using System;

namespace GStreamer.Models;

public readonly struct CachedSegmentFile : IDisposable
{
    public readonly long Length;
    public readonly SafeFileHandle Handle;

    public CachedSegmentFile(long length, SafeFileHandle handle)
    {
        Length = length;
        Handle = handle;
    }

    public bool IsValid =>
        Length > 0 &&
        Handle is { IsInvalid: false, IsClosed: false };

    public void Dispose()
    {
        Handle?.Dispose();
    }
}
