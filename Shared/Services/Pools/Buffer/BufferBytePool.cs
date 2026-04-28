using Shared.Services.Native;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools;

public sealed class BufferBytePool : IDisposable
{
    #region pool
    public static readonly int sizeLowMemory = 512 * 1024; // 512Kb
    static readonly ConcurrentBag<NativeBuffer<byte>> _poolLowMemory = new();

    public static readonly int sizeSmall = 2 * 1024 * 1024; // 2Mb
    static readonly ConcurrentBag<NativeBuffer<byte>> _poolSmall = new();

    public static readonly int sizeMedium = 8 * 1024 * 1024; // 8Mb
    static readonly ConcurrentBag<NativeBuffer<byte>> _poolMedium = new();

    public static readonly int sizeLarge = 20 * 1024 * 1024; // 20Mb
    static readonly ConcurrentBag<NativeBuffer<byte>> _poolLarge = new();
    #endregion

    #region OpenStat
    public static int FreeSmall
        => _poolSmall.Count;

    public static int FreeMedium
        => _poolMedium.Count;

    public static int FreeLarge
        => _poolLarge.Count;

    public static long DisposeCount
        => Interlocked.Read(ref _disposeCount);

    static long _disposeCount;
    #endregion

    private NativeBuffer<byte> _nbuf;
    private int _index;
    private byte _typepool;
    private int _disposed;

    public BufferBytePool(int capacity)
    {
        var pool = CoreInit.conf.pool;

        if (CoreInit.conf.lowMemoryMode)
        {
            if (sizeLowMemory >= capacity && pool.BufferByteSmallMaxCount > _poolLowMemory.Count)
            {
                _typepool = 5;
                if (!_poolLowMemory.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<byte>(sizeLowMemory);
            }
            else
            {
                _nbuf = new NativeBuffer<byte>(capacity);
            }
        }
        else
        {
            if (sizeSmall >= capacity && pool.BufferByteSmallMaxCount > _poolSmall.Count)
            {
                _typepool = 1;
                if (!_poolSmall.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<byte>(sizeSmall);
            }
            else if (sizeMedium >= capacity && pool.BufferByteMediumMaxCount > _poolMedium.Count)
            {
                _typepool = 2;
                if (!_poolMedium.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<byte>(sizeMedium);
            }
            else if (sizeLarge >= capacity && pool.BufferByteLargeMaxCount > _poolLarge.Count)
            {
                _typepool = 3;
                if (!_poolLarge.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<byte>(sizeLarge);
            }
            else
            {
                _nbuf = new NativeBuffer<byte>(capacity);
            }
        }
    }

    public ReadOnlySpan<byte> WrittenSpan
        => Span.Slice(0, _index);

    public void Advance(int count)
    {
        _index += count;
    }

    public Span<byte> Span
        => _nbuf.Memory.Span;

    public Memory<byte> Memory
        => _nbuf.Memory;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_nbuf.IsExpires)
        {
            ((IDisposable)_nbuf).Dispose();
            return;
        }

        switch (_typepool)
        {
            case 1:
                _poolSmall.Add(_nbuf);
                break;
            case 2:
                _poolMedium.Add(_nbuf);
                break;
            case 3:
                _poolLarge.Add(_nbuf);
                break;
            case 5:
                _poolLowMemory.Add(_nbuf);
                break;
            default:
                int bufferSize = _nbuf.Memory.Length;
                Interlocked.Increment(ref _disposeCount);
                ((IDisposable)_nbuf).Dispose();
                Serilog.Log.Error(
                    "dispose buffer size. CatchId={CatchId} Size={BufferSize}",
                    "id_KEFYxYdE",
                    bufferSize
                );
                break;
        }
    }
}
