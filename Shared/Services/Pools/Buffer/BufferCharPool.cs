using Shared.Services.Native;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools;

public sealed class BufferCharPool : IDisposable
{
    #region pool
    public const int sizeExtraSmall = 32 * 1024; // 64kb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolExtraSmall = new();

    public const int sizeSmall = 1024 * 1024; // 2Mb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolSmall = new();

    public const int sizeMedium = 4 * 1024 * 1024; // 8Mb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolMedium = new();

    public const int sizeLarge = 10 * 1024 * 1024; // 20Mb
    static readonly ConcurrentBag<NativeBuffer<char>> _poolLarge = new();
    #endregion

    #region OpenStat
    public static int FreeExtraSmall
        => _poolExtraSmall.Count;

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

        if (sizeExtraSmall >= capacity)
        {
            _typepool = 1;
            if (!_poolExtraSmall.TryTake(out _nbuf))
                _nbuf = new NativeBuffer<char>(sizeExtraSmall);
        }
        else if (sizeSmall >= capacity)
        {
            if (pool.BufferCharSmallMaxCount > _poolSmall.Count)
            {
                _typepool = 2;
                if (!_poolSmall.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<char>(sizeSmall);
            }
            else
            {
                _nbuf = new NativeBuffer<char>(capacity);
            }
        }
        else if (sizeMedium >= capacity)
        {
            if (pool.BufferCharMediumMaxCount > _poolMedium.Count)
            {
                _typepool = 3;
                if (!_poolMedium.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<char>(sizeMedium);
            }
            else
            {
                _nbuf = new NativeBuffer<char>(capacity);
            }
        }
        else if (sizeLarge >= capacity)
        {
            if (CoreInit.conf.lowMemoryMode == false && pool.BufferCharLargeMaxCount > _poolLarge.Count)
            {
                _typepool = 4;
                if (!_poolLarge.TryTake(out _nbuf))
                    _nbuf = new NativeBuffer<char>(sizeLarge);
            }
            else
            {
                _nbuf = new NativeBuffer<char>(capacity);
            }
        }
        else
        {
            _nbuf = new NativeBuffer<char>(capacity);
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
                _poolExtraSmall.Add(_nbuf);
                break;
            case 2:
                _poolSmall.Add(_nbuf);
                break;
            case 3:
                _poolMedium.Add(_nbuf);
                break;
            case 4:
                _poolLarge.Add(_nbuf);
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
