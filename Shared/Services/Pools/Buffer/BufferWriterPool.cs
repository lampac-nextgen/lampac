using Shared.Services.Native;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools;

public sealed class BufferWriterPool<T> : IBufferWriter<T>, IDisposable where T : unmanaged
{
    #region pool
    public static readonly int sizePool = 1 * 1024 * 1024;
    static readonly ConcurrentBag<NativeBuffer<T>> _pool = new();

    public static readonly int sizeLargePool = 10 * 1024 * 1024;
    static readonly ConcurrentBag<NativeBuffer<T>> _poolLarge = new();

    static int smailMaxCount
        => CoreInit.conf.pool.BufferWriterSmallMaxCount;
    static int largeMaxCount
        => CoreInit.conf.pool.BufferWriterLargeMaxCount;
    #endregion

    #region OpenStat
    public static int Free
        => _pool.Count;

    public static int FreeLarge
        => _poolLarge.Count;

    static long _disposeCount;
    public static long DisposeCount
        => Interlocked.Read(ref _disposeCount);
    #endregion

    private NativeBuffer<T> _nbuf;
    private int _index;
    private int _disposed;
    private bool _large;

    public BufferWriterPool(bool largePool = false)
    {
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
            if (_large && largeMaxCount > _poolLarge.Count && CoreInit.conf.lowMemoryMode == false)
            {
                if (!_poolLarge.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<T>(sizeLargePool);
            }
            else if (smailMaxCount > _pool.Count)
            {
                if (!_pool.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<T>(sizePool);
            }
            else
            {
                const int minrent = 128 * 1024;
                if (minrent > sizeHint)
                    sizeHint = minrent;

                _nbuf = new NativeBuffer<T>(sizeHint);
            }
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

        if (_nbuf.IsExpires)
        {
            ((IDisposable)_nbuf).Dispose();
            return;
        }
        else
        {
            if (_nbuf.Memory.Length == sizePool)
                _pool.Add(_nbuf);
            else if (_nbuf.Memory.Length == sizeLargePool)
                _poolLarge.Add(_nbuf);
            else
            {
                int bufferSize = _nbuf.Memory.Length;
                Interlocked.Increment(ref _disposeCount);
                ((IDisposable)_nbuf).Dispose();

                Serilog.Log.Error(
                    "dispose buffer size. CatchId={CatchId} Size={BufferSize}",
                    "id_exzytjlp",
                    bufferSize
                );
            }
        }
    }
}
