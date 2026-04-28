using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace Shared.Services.Pools;

public static class StringBuilderPool
{
    #region static
    public static readonly StringBuilder EmptyHtml = new StringBuilder();

    public static readonly StringBuilder EmptyJsonObject = new StringBuilder("{}");

    public static readonly StringBuilder EmptyJsonArray = new StringBuilder("[]");
    #endregion

    #region pool
    [ThreadStatic]
    private static StringBuilder _threadSmall;

    static readonly ConcurrentBag<StringBuilder> _poolSmall = new();
    static readonly ConcurrentBag<StringBuilder> _poolLarge = new();

    static readonly int capacity = 32 * 1024; // 1 char == 2 byte (64кб, ниже LOH лимита ~85кб)
    static readonly int rentSmall = 128 * 1024;
    static readonly int rentLarge = 2 * 1024 * 1024;
    #endregion

    #region OpenStat
    public static int FreeSmall
        => _poolSmall.Count;

    public static int FreeLarge
        => _poolLarge.Count;

    static long _disposeCount;
    public static long DisposeCount
        => Interlocked.Read(ref _disposeCount);
    #endregion

    public static StringBuilder Rent()
    {
        if (_poolLarge.TryTake(out var sb))
            return sb;

        return new StringBuilder(capacity);
    }

    public static StringBuilder RentSmall()
    {
        if (_poolSmall.TryTake(out var sb))
            return sb;

        return new StringBuilder(capacity);
    }

    public static StringBuilder ThreadInstance
    {
        get
        {
            if (CoreInit.conf.lowMemoryMode)
                return new StringBuilder();

            var sb = (_threadSmall ??= new StringBuilder(1024));
            sb.Clear();
            return sb;
        }
    }

    public static void Return(StringBuilder sb)
    {
        if (sb == null)
            return;

        int smallMaxCount = CoreInit.conf.pool.StringBuilderSmallMaxCount;
        int largeMaxCount = CoreInit.conf.pool.StringBuilderLargeMaxCount;

        if (rentSmall >= sb.Capacity && smallMaxCount > _poolSmall.Count)
        {
            sb.Clear();
            _poolSmall.Add(sb);
        }
        else if (rentLarge >= sb.Capacity && largeMaxCount > _poolLarge.Count && CoreInit.conf.lowMemoryMode == false)
        {
            sb.Clear();
            _poolLarge.Add(sb);
        }
        else
        {
            Interlocked.Increment(ref _disposeCount);
        }
    }
}
