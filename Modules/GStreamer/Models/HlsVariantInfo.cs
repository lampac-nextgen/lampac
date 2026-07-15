namespace GStreamer.Models;

public sealed class HlsVariantInfo
{
    public string Codecs { get; set; }
    public string VideoRange { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
}
