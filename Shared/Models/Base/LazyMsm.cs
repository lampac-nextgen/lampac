using Microsoft.IO;

namespace Shared.Models.Base;

public class LazyMsm : IDisposable
{
    private RecyclableMemoryStream _stream;

    public RecyclableMemoryStream Stream
        => _stream ??= PoolInvk.msm.GetStream();

    public bool IsEmpty
        => _stream == null || _stream.Length == 0;

    public void Dispose()
        => _stream?.Dispose();
}
