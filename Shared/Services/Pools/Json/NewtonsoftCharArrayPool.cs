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
    [ThreadStatic]
    private static char[] _threadSmall;
    public static readonly int sizeSmall = 32 * 1024;

    public static readonly int sizeMedium = 128 * 1024; // 128 char = 256byte
    static readonly ConcurrentBag<char[]> _poolMedium = new();

    public static readonly int sizeLarge = 256 * 1024;
    static readonly ConcurrentBag<char[]> _poolLarge = new();

    static int mediumMaxCount
        => CoreInit.conf.pool.NewtonsoftCharMediumMaxCount;
    static int largeMaxCount
        => CoreInit.conf.pool.NewtonsoftCharLargeMaxCount;
    #endregion

    #region OpenStat
    public static int FreeMedium
        => _poolMedium.Count;

    public static int FreeLarge
        => _poolLarge.Count;

    static long _disposeCount;
    public static long DisposeCount
        => Interlocked.Read(ref _disposeCount);
    #endregion

    public NewtonsoftCharArrayPool(ArrayPool<char> pool = null, bool clearOnReturn = false)
    {
    }

    public char[] Rent(int minimumLength)
    {
        if (sizeSmall >= minimumLength && CoreInit.conf.lowMemoryMode)
        {
            return (_threadSmall ??= new char[sizeSmall]);
        }
        else if (sizeMedium >= minimumLength && mediumMaxCount > _poolMedium.Count)
        {
            if (_poolMedium.TryTake(out char[] _array))
                return _array;

            return new char[sizeMedium];
        }
        else if (sizeLarge >= minimumLength && largeMaxCount > _poolLarge.Count)
        {
            if (_poolLarge.TryTake(out char[] _array))
                return _array;

            return new char[sizeLarge];
        }
        else
        {
            return new char[minimumLength];
        }
    }

    public void Return(char[] array)
    {
        if (array == null)
            return;

        if (array.Length == sizeSmall)
        {
            // ThreadStatic
        }
        else if (array.Length == sizeMedium)
        {
            if (mediumMaxCount > _poolMedium.Count)
                _poolMedium.Add(array);
            else
                Interlocked.Increment(ref _disposeCount);
        }
        else if (array.Length == sizeLarge)
        {
            if (largeMaxCount > _poolLarge.Count)
                _poolLarge.Add(array);
            else
                Interlocked.Increment(ref _disposeCount);
        }
        else
        {
            Interlocked.Increment(ref _disposeCount);
        }
    }
}
