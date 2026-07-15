using Microsoft.Win32.SafeHandles;
using Shared.Services.Pools;
using System;
using System.IO;

namespace GStreamer.Services;

internal sealed class RandomAccessWriteStream : Stream
{
    readonly SafeFileHandle handle;
    readonly BufferPool buffer;

    long fileOffset;
    int bufferedBytes;
    bool disposed;

    public RandomAccessWriteStream(string path, long preallocationSize)
    {
        buffer = new BufferPool();

        try
        {
            handle = File.OpenHandle(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                FileOptions.SequentialScan,
                preallocationSize
            );
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !disposed;
    public override long Length => Position;

    public override long Position
    {
        get
        {
            ThrowIfDisposed();
            return checked(fileOffset + bufferedBytes);
        }
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        ThrowIfDisposed();
        FlushBuffer();
    }

    public override void Write(byte[] source, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        Write(source.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> source)
    {
        ThrowIfDisposed();

        while (!source.IsEmpty)
        {
            Span<byte> destination = buffer.Span;

            if (bufferedBytes == 0 && source.Length >= destination.Length)
            {
                RandomAccess.Write(handle, source, fileOffset);
                fileOffset = checked(fileOffset + source.Length);
                return;
            }

            int copyLength = Math.Min(source.Length, destination.Length - bufferedBytes);
            source[..copyLength].CopyTo(destination[bufferedBytes..]);
            bufferedBytes += copyLength;
            source = source[copyLength..];

            if (bufferedBytes == destination.Length)
                FlushBuffer();
        }
    }

    public override void WriteByte(byte value)
    {
        ThrowIfDisposed();

        Span<byte> destination = buffer.Span;
        destination[bufferedBytes++] = value;

        if (bufferedBytes == destination.Length)
            FlushBuffer();
    }

    void FlushBuffer()
    {
        if (bufferedBytes == 0)
            return;

        int writeLength = bufferedBytes;
        RandomAccess.Write(handle, buffer.Span[..writeLength], fileOffset);
        fileOffset = checked(fileOffset + writeLength);
        bufferedBytes = 0;
    }

    void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(RandomAccessWriteStream));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposed)
            return;

        try
        {
            if (disposing)
                FlushBuffer();
        }
        finally
        {
            disposed = true;

            if (disposing)
            {
                try
                {
                    handle.Dispose();
                }
                finally
                {
                    buffer.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();
}
