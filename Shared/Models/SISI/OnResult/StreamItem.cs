using Shared.Models.SISI.Base;

namespace Shared.Models.SISI.OnResult;

public class StreamItem
{
    public Dictionary<string, string> qualitys { get; set; }

    public Dictionary<string, string> qualitys_proxy { get; set; }

    public IReadOnlyList<PlaylistItem> recomends { get; set; }
}
