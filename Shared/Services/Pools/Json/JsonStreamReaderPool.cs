using System.Text;
using System.Threading;

namespace Shared.Services.Pools.Json;

public class JsonStreamReaderPool : TextReader, IDisposable
{
    [ThreadStatic]
    private static byte[] _byteInstance;

    [ThreadStatic]
    private static char[] _charInstance;

    private byte[] _byteLowInstance;
    private char[] _charLowInstance;

    readonly Stream _stream;
    readonly bool _leaveOpen;
    bool _checkedPreamble;

    Decoder _decoder;

    int _byteLen;
    int _bytePos;

    int _charLen;
    int _charPos;

    bool _isFinalBlock;
    int _disposed; // 0 = alive, 1 = disposed

    public Span<byte> ByteBuffer
    {
        get
        {
            if (CoreInit.conf.lowMemoryMode)
                return _byteLowInstance ??= new byte[1024];

            return _byteInstance ??= new byte[PoolInvk.bufferSize];
        }
    }

    public Span<char> CharBuffer
    {
        get
        {
            if (CoreInit.conf.lowMemoryMode)
                return _charLowInstance ??= new char[1025];

            return _charInstance ??= new char[Encoding.UTF8.GetMaxCharCount(PoolInvk.bufferSize)];
        }
    }

    public JsonStreamReaderPool(Stream stream, Encoding encoding, bool leaveOpen)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        if (!stream.CanRead)
            throw new ArgumentException("Stream is not readable.", nameof(stream));

        if (encoding == null)
            encoding = Encoding.UTF8;

        _decoder = encoding.GetDecoder();

        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public override int Peek()
    {
        ThrowIfDisposed();

        if (!EnsureCharData())
            return -1;

        return CharBuffer[_charPos];
    }

    public override int Read()
    {
        ThrowIfDisposed();

        if (!EnsureCharData())
            return -1;

        return CharBuffer[_charPos++];
    }

    public override int Read(char[] buffer, int index, int count)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        if (index < 0 || count < 0 || buffer.Length - index < count)
            throw new ArgumentOutOfRangeException();

        ThrowIfDisposed();

        if (count == 0)
            return 0;

        int totalRead = 0;

        while (count > 0)
        {
            if (!EnsureCharData())
                break;

            int available = _charLen - _charPos;
            int toCopy = Math.Min(available, count);

            CharBuffer.Slice(_charPos, toCopy).CopyTo(buffer.AsSpan(index, toCopy));

            _charPos += toCopy;
            index += toCopy;
            count -= toCopy;
            totalRead += toCopy;
        }

        return totalRead;
    }

    bool EnsureCharData()
    {
        SkipUtf8BomIfNeeded();

        if (_charPos < _charLen)
            return true;

        _charPos = 0;
        _charLen = 0;

        while (true)
        {
            if (_bytePos >= _byteLen && !_isFinalBlock)
            {
                _byteLen = _stream.Read(ByteBuffer);
                _bytePos = 0;
                _isFinalBlock = _byteLen == 0;
            }

            int bytesAvailable = _byteLen - _bytePos;
            if (bytesAvailable < 0)
                bytesAvailable = 0;

            _charLen = _decoder.GetChars(
                ByteBuffer.Slice(_bytePos, bytesAvailable),
                CharBuffer,
                _isFinalBlock
            );

            _bytePos = _byteLen;

            if (_charLen > 0)
                return true;

            if (_isFinalBlock)
                return false;
        }
    }

    private void SkipUtf8BomIfNeeded()
    {
        if (_checkedPreamble)
            return;

        _checkedPreamble = true;

        if (_stream.CanSeek)
        {
            long pos = _stream.Position;

            Span<byte> probe = stackalloc byte[3];
            int read = _stream.Read(probe);

            if (read == 3 && probe[0] == 0xEF && probe[1] == 0xBB && probe[2] == 0xBF)
            {
                // BOM найден, оставляем позицию после него
                return;
            }

            // BOM нет — откатываемся назад
            _stream.Position = pos;
            return;
        }

        // Для non-seek stream читаем в основной буфер
        _byteLen = _stream.Read(ByteBuffer);
        _bytePos = 0;

        if (_byteLen >= 3 &&
            ByteBuffer[0] == 0xEF &&
            ByteBuffer[1] == 0xBB &&
            ByteBuffer[2] == 0xBF)
        {
            _bytePos = 3;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (!_leaveOpen)
                _stream.Dispose();
        }

        base.Dispose(disposing);
    }

    void ThrowIfDisposed()
    {
        if (_disposed == 1)
            throw new ObjectDisposedException(nameof(JsonStreamReaderPool));
    }
}
