using Shared.Services.Native;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Models.Base;

public class BufferPoolInfo<T> where T : unmanaged
{
    public readonly int sizePool, maxCount;
    readonly ConcurrentBag<NativeBuffer<T>> pool;

    #region currentCount
    long _currentCount;

    public long currentCount
        => Interlocked.Read(ref _currentCount);
    #endregion

    #region disposeCount
    long _disposeCount;

    public long disposeCount
        => Interlocked.Read(ref _disposeCount);
    #endregion

    public BufferPoolInfo(int size, int maxCount)
    {
        sizePool = size;
        this.maxCount = maxCount;
        pool = new ConcurrentBag<NativeBuffer<T>>();
    }

    public NativeBuffer<T> Rent(int capacity)
    {
        if (pool.TryTake(out NativeBuffer<T> nbuf))
            return nbuf;

        if (currentCount >= maxCount)
            return new NativeBuffer<T>(capacity);

        Interlocked.Increment(ref _currentCount);

        return new NativeBuffer<T>(sizePool);
    }

    public void Return(NativeBuffer<T> nbuf)
    {
        if (nbuf.IsExpires)
        {
            Interlocked.Decrement(ref _currentCount);
            ((IDisposable)nbuf).Dispose();
            return;
        }

        if (nbuf.Memory.Length != sizePool)
        {
            Interlocked.Decrement(ref _currentCount);
            Interlocked.Increment(ref _disposeCount);
            ((IDisposable)nbuf).Dispose();
        }
        else
        {
            pool.Add(nbuf);
        }
    }
}
