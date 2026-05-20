using Shared.Services.Native;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools;

public sealed class BufferWriterPool<T> : IBufferWriter<T>, IDisposable where T : unmanaged
{
    #region pool
    public const int sizePool = 1 * 1024 * 1024;
    public const int sizeLargePool = 10 * 1024 * 1024;

    static readonly ConcurrentDictionary<byte, BufferPoolInfo<T>> pool = new ConcurrentDictionary<byte, BufferPoolInfo<T>>
    {
        [1] = new BufferPoolInfo<T>(sizePool, CoreInit.conf.pool.BufferWriterSmallMaxCount),
        [2] = new BufferPoolInfo<T>(sizeLargePool, CoreInit.conf.pool.BufferWriterLargeMaxCount)
    };
    #endregion

    #region OpenStat
    public static long Free
        => pool[1].currentCount;

    public static long FreeLarge
        => pool[2].currentCount;

    public static long DisposeCount
        => pool.Sum(i => i.Value.disposeCount);
    #endregion

    private NativeBuffer<T> _nbuf;
    private int _index;
    private int _disposed;
    private bool _large;

    public BufferWriterPool(bool largePool = false)
    {
        if (CoreInit.conf.lowMemoryMode == false)
            _large = largePool;
    }

    public ReadOnlySpan<T> WrittenSpan
        => _nbuf.Memory.Span.Slice(0, _index);

    public ReadOnlyMemory<T> WrittenMemory
        => _nbuf.Memory.Slice(0, _index);

    public int WrittenCount => _index;

    public void Advance(int count)
    {
        _index += count;
    }

    public void SetLength(int index)
    {
        _index = index;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _nbuf.Memory.Slice(_index);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _nbuf.Memory.Span.Slice(_index);
    }

    private void Ensure(int sizeHint)
    {
        if (sizeHint <= 0)
            sizeHint = 1;

        if (_nbuf == null)
        {
            const int minSize = 128 * 1024;

            if (minSize > sizeHint)
                sizeHint = minSize;

            var p = pool[_large ? (byte)2 : (byte)1];
            _nbuf = p.Rent(sizeHint);
        }

        int newsize = _index + sizeHint;
        if (newsize <= _nbuf.Memory.Length)
            return;

        _nbuf.Ensure(newsize * 2);
    }

    public void Dispose()
    {
        if (_nbuf == null || Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var p = pool[_large ? (byte)2 : (byte)1];
        p.Return(_nbuf);
    }
}
