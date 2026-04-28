using Shared.Services.Native;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools;

public sealed class BufferCharPool : IDisposable
{
    #region pool
    public static readonly int sizeLowMemory = 512 * 1024; // 1Mb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolLowMemory = new();

    public static readonly int sizeSmall = 1024 * 1024; // 2Mb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolSmall = new();

    public static readonly int sizeMedium = 4 * 1024 * 1024; // 8Mb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolMedium = new();

    public static readonly int sizeLarge = 10 * 1024 * 1024; // 20Mb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolLarge = new();
    #endregion

    #region OpenStat
    public static int FreeSmall
        => _poolSmall.Count;

    public static int FreeMedium
        => _poolMedium.Count;

    public static int FreeLarge
        => _poolLarge.Count;

    static long _disposeCountStat;
    public static long DisposeCount
        => Interlocked.Read(ref _disposeCountStat);
    #endregion

    private NativeBuffer<char> _nbuf;
    private int _index;
    private byte _typepool;
    private int _disposed;

    public BufferCharPool(int capacity)
    {
        var pool = CoreInit.conf.pool;

        if (CoreInit.conf.lowMemoryMode)
        {
            if (sizeLowMemory >= capacity && pool.BufferCharSmallMaxCount > _poolLowMemory.Count)
            {
                _typepool = 5;
                if (!_poolLowMemory.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<char>(sizeLowMemory);
            }
            else
            {
                _nbuf = new NativeBuffer<char>(capacity);
            }
        }
        else
        {
            if (sizeSmall >= capacity && pool.BufferCharSmallMaxCount > _poolSmall.Count)
            {
                _typepool = 1;
                if (!_poolSmall.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<char>(sizeSmall);
            }
            else if (sizeMedium >= capacity && pool.BufferCharMediumMaxCount > _poolMedium.Count)
            {
                _typepool = 2;
                if (!_poolMedium.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<char>(sizeMedium);
            }
            else if (sizeLarge >= capacity && pool.BufferCharLargeMaxCount > _poolLarge.Count)
            {
                _typepool = 3;
                if (!_poolLarge.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<char>(sizeLarge);
            }
            else
            {
                _nbuf = new NativeBuffer<char>(capacity);
            }
        }
    }

    public Span<char> Span
        => _nbuf.Memory.Span;

    public ReadOnlySpan<char> WrittenSpan
        => Span.Slice(0, _index);

    public void Advance(int count)
    {
        _index += count;
    }

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
                Interlocked.Increment(ref _disposeCountStat);
                ((IDisposable)_nbuf).Dispose();
                Serilog.Log.Error(
                    "dispose buffer size. CatchId={CatchId} Size={BufferSize}",
                    "id_yEYWayZF",
                    bufferSize
                );
                break;
        }
    }
}
