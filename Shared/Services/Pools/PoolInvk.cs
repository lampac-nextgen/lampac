using Microsoft.IO;

namespace Shared.Services.Pools;

public static class PoolInvk
{
    public static readonly RecyclableMemoryStreamManager msm = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options
    (
        blockSize: 81920,                              // small blocks (80 КБ)
        largeBufferMultiple: 1024 * 1024,              // ступень роста 1 MB
        maximumBufferSize: 10 * 1024 * 1024,           // не кешируем >10 MB
        maximumSmallPoolFreeBytes: 128L * 1024 * 1024, // максимальный размер пула small blocks (128 MB)
        maximumLargePoolFreeBytes: 32L * 1024 * 1024   // общий размер пула largeBufferMultiple/maximumBufferSize (32 MB)
    )
    {
        AggressiveBufferReturn = false
    });


    static int? _bufferSize;

    public static int bufferSize
    {
        get
        {
            if (_bufferSize.HasValue)
                return _bufferSize.Value;

            int size = CoreInit.conf.pool.BufferSize > 0
                ? CoreInit.conf.pool.BufferSize
                : 81920;

            if (CoreInit.conf.lowMemoryMode) // max 32Kb
                size = Math.Min(size, 32768);

            if (4096 > size)
                size = 4096;

            _bufferSize = size;
            return size;
        }
    }
}
