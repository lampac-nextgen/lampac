using Newtonsoft.Json;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

namespace Shared.Services.Pools.Json;

public static class NewtonsoftPool
{
    public static readonly IArrayPool<char> Array = new NewtonsoftCharArrayPool();
}

public class NewtonsoftCharArrayPool : IArrayPool<char>
{
    #region pool
    const int maxSize = 256 * 1024;

    static readonly ConcurrentDictionary<int, ConcurrentBag<char[]>> pool = new ConcurrentDictionary<int, ConcurrentBag<char[]>>
    {
        [4 * 1024] = new ConcurrentBag<char[]>(),
        [16 * 1024] = new ConcurrentBag<char[]>(),
        [32 * 1024] = new ConcurrentBag<char[]>(),
        [128 * 1024] = new ConcurrentBag<char[]>(),
        [maxSize] = new ConcurrentBag<char[]>()
    };
    #endregion

    #region OpenStat
    public static int FreeCurrent
        => pool.Sum(i => i.Value.Count);

    static long _disposeCount;
    public static long DisposeCount
        => Interlocked.Read(ref _disposeCount);
    #endregion

    public NewtonsoftCharArrayPool(ArrayPool<char> pool = null, bool clearOnReturn = false)
    {
    }

    public char[] Rent(int sizeHint)
    {
        if (maxSize >= sizeHint)
        {
            foreach (var p in pool)
            {
                if (p.Key >= sizeHint)
                {
                    if (!p.Value.TryTake(out char[] buf))
                        buf = new char[p.Key];

                    return buf;
                }
            }
        }

        return new char[sizeHint];
    }

    public void Return(char[] array)
    {
        if (array == null)
            return;

        if (array.Length < 4096 || array.Length > maxSize)
        {
            Interlocked.Increment(ref _disposeCount);
            return;
        }

        foreach (var p in pool)
        {
            if (p.Key == array.Length)
            {
                p.Value.Add(array);
                return;
            }
        }

        Interlocked.Increment(ref _disposeCount);
    }
}
