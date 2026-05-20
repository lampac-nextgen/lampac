using Shared.Services.Native;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools;

public sealed class BufferCharPool : IDisposable
{
    #region pool
    public const int sizeTiny = 32 * 1024;         // 64Kb
    public const int sizeExtraSmall = 128 * 1024;  // 256Kb
    public const int sizeSmall = 1024 * 1024;      // 2Mb
    public const int sizeMedium = 4 * 1024 * 1024; // 8Mb
    public const int sizeLarge = 10 * 1024 * 1024; // 20Mb

    static readonly ConcurrentDictionary<byte, BufferPoolInfo<char>> pool = new ConcurrentDictionary<byte, BufferPoolInfo<char>>
    {
        [1] = new BufferPoolInfo<char>(sizeTiny, CoreInit.conf.pool.BufferCharTinyMaxCount),             // хеш операции, aes, crypto, SearchName
        [2] = new BufferPoolInfo<char>(sizeExtraSmall, CoreInit.conf.pool.BufferCharExtraSmallMaxCount), // base64, tpl
        [3] = new BufferPoolInfo<char>(sizeSmall, CoreInit.conf.pool.BufferCharSmallMaxCount),
        [4] = new BufferPoolInfo<char>(sizeMedium, CoreInit.conf.pool.BufferCharMediumMaxCount),
        [5] = new BufferPoolInfo<char>(sizeLarge, CoreInit.conf.pool.BufferCharLargeMaxCount)
    };
    #endregion

    #region OpenStat
    public static long FreeTiny
        => pool[1].currentCount;

    public static long FreeExtraSmall
        => pool[2].currentCount;

    public static long FreeSmall
        => pool[3].currentCount;

    public static long FreeMedium
        => pool[4].currentCount;

    public static long FreeLarge
        => pool[5].currentCount;

    public static long DisposeCount
        => pool.Sum(i => i.Value.disposeCount);
    #endregion

    private NativeBuffer<char> _nbuf;
    private int _index;
    private byte _typepool;
    private int _disposed;

    public BufferCharPool(int capacity)
    {
        foreach (var p in pool)
        {
            if (CoreInit.conf.lowMemoryMode && p.Value.sizePool == sizeLarge)
                continue;

            if (p.Value.sizePool >= capacity)
            {
                _typepool = p.Key;
                _nbuf = p.Value.Rent(capacity);
                return;
            }
        }

        if (_nbuf == null)
            _nbuf = new NativeBuffer<char>(capacity);
    }

    public Span<char> Span
        => _nbuf.Memory.Span;

    public ReadOnlySpan<char> WrittenSpan
        => Span.Slice(0, _index);

    public void Advance(int count)
    {
        _index += count;
    }

    public void Ensure(int sizeHint)
    {
        _nbuf.Ensure(sizeHint);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_typepool == 0)
        {
            ((IDisposable)_nbuf).Dispose();
            return;
        }

        var p = pool[_typepool];
        p.Return(_nbuf);
    }
}
