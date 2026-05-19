using Microsoft.IO;
using System.Text;

namespace Shared.Services.Pools;

public static class OwnerTo
{
    public static void Span(RecyclableMemoryStream msm, Encoding encoding, Action<ReadOnlySpan<char>> spanAction)
    {
        try
        {
            if (encoding == null || msm == null || msm.Length == 0)
                return;

            // ASCII: 1 byte -> 1 char
            // UTF-8: 2 byte -> 1 char
            // msm.Length выше или равен charCount
            int charCount = (int)msm.Length;

            using (var nbuf = new BufferCharPool(charCount))
            {
                var reader = new BufferReader(msm, encoding);
                int actual = reader.Read(nbuf.Span);
                if (actual > 0)
                    spanAction(nbuf.Span.Slice(0, actual));
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "OwnerTo", "id_1hrr99su");
        }
    }


    sealed class BufferReader
    {
        [ThreadStatic]
        private static byte[] _thread;

        readonly RecyclableMemoryStream _stream;
        readonly Decoder _decoder;

        public BufferReader(RecyclableMemoryStream stream, Encoding encoding)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _decoder = (encoding ?? Encoding.UTF8).GetDecoder();
        }

        public int Read(Span<char> destination)
        {
            if (destination.IsEmpty)
                return 0;

            int totalChars = 0;
            bool reachedEof = false;

            Span<byte> buff = _thread ??= new byte[CoreInit.conf.lowMemoryMode ? 4096 : PoolInvk.bufferSize];

            while (totalChars < destination.Length)
            {
                int bytesRead = _stream.Read(buff);
                if (bytesRead <= 0)
                {
                    reachedEof = true;
                    break;
                }

                int charsDecoded = _decoder.GetChars(
                    buff.Slice(0, bytesRead),
                    destination.Slice(totalChars),
                    flush: false);

                totalChars += charsDecoded;
            }

            if (reachedEof)
            {
                int flushedChars = _decoder.GetChars(
                    ReadOnlySpan<byte>.Empty,
                    destination.Slice(totalChars),
                    flush: true);

                totalChars += flushedChars;
            }

            return totalChars;
        }
    }
}
