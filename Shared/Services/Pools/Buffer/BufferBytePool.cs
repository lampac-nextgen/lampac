using Shared.Services.Native;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools;

public sealed class BufferBytePool : IDisposable
{
    #region pool
    public const int sizeTiny = 32 * 1024;         // 32Kb
    public const int sizeExtraSmall = 256 * 1024;  // 256Kb
    public const int sizeSmall = 2 * 1024 * 1024;  // 2Mb
    public const int sizeMedium = 8 * 1024 * 1024; // 8Mb
    public const int sizeLarge = 20 * 1024 * 1024; // 20Mb

    static readonly ConcurrentDictionary<byte, BufferPoolInfo<byte>> pool = new ConcurrentDictionary<byte, BufferPoolInfo<byte>>
    {
        [1] = new BufferPoolInfo<byte>(sizeTiny, CoreInit.conf.pool.BufferByteTinyMaxCount),             // хеш операции, aes, crypto, kit, base64
        [2] = new BufferPoolInfo<byte>(sizeExtraSmall, CoreInit.conf.pool.BufferByteExtraSmallMaxCount), // proxyimg rsize, big base64
        [3] = new BufferPoolInfo<byte>(sizeSmall, CoreInit.conf.pool.BufferByteSmallMaxCount),
        [4] = new BufferPoolInfo<byte>(sizeMedium, CoreInit.conf.pool.BufferByteMediumMaxCount),
        [5] = new BufferPoolInfo<byte>(sizeLarge, CoreInit.conf.pool.BufferByteLargeMaxCount)
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

    private NativeBuffer<byte> _nbuf;
    private int _index;
    private byte _typepool;
    private int _disposed;

    public BufferBytePool(int capacity)
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
            _nbuf = new NativeBuffer<byte>(capacity);
    }

    public ReadOnlySpan<byte> WrittenSpan
        => Span.Slice(0, _index);

    public void Advance(int count)
    {
        _index += count;
    }

    public void Ensure(int sizeHint)
    {
        _nbuf.Ensure(sizeHint);
    }

    public Span<byte> Span
        => _nbuf.Memory.Span;

    public Memory<byte> Memory
        => _nbuf.Memory;

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
