using System.Buffers;

namespace Shared.Services.Pools;

public class ChunkBufferWriter<T> : IBufferWriter<T> where T : struct
{
    readonly IBufferWriter<T> writer;

    public ChunkBufferWriter(IBufferWriter<T> writer)
    {
        this.writer = writer;
    }

    public void Advance(int count)
    {
        writer.Advance(count);
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        return writer.GetMemory(ChunkSizeHint(sizeHint));
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        return writer.GetSpan(ChunkSizeHint(sizeHint));
    }

    static int ChunkSizeHint(int sizeHint) => sizeHint switch
    {
        <= 4 * 1024 => 4 * 1024,
        <= 16 * 1024 => 16 * 1024,
        <= 32 * 1024 => 32 * 1024,
        <= 64 * 1024 => 64 * 1024,
        <= 128 * 1024 => 128 * 1024,
        <= 256 * 1024 => 256 * 1024,
        _ => sizeHint
    };
}
