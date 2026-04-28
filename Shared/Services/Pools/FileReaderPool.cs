using System.Collections.Concurrent;

namespace Shared.Services.Pools;

public static class FileReaderPool
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<FileStream>> _pool = new();

    public static FileStream Rent(string filePath)
    {
        var bag = _pool.GetOrAdd(filePath, _ => new ConcurrentBag<FileStream>());

        if (bag.TryTake(out var fs) && fs.CanRead)
        {
            fs.Seek(0, SeekOrigin.Begin);
            return fs;
        }

        return new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: PoolInvk.bufferSize,
            options: FileOptions.SequentialScan);
    }

    public static void Return(string filePath, FileStream fs)
    {
        if (fs == null)
            return;

        var bag = _pool.GetOrAdd(filePath, _ => new ConcurrentBag<FileStream>());
        bag.Add(fs);
    }
}
