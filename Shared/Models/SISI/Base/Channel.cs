namespace Shared.Models.SISI.Base;

public class Channel
{
    public IReadOnlyList<MenuItem> menu { get; set; }

    public IReadOnlyList<PlaylistItem> list { get; set; }

    public int total_pages { get; set; }
}
