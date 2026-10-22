using Shared.Models.Base;
using System;

namespace LordFilm;

public class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf(string plugin, string host, bool enable = true, bool streamproxy = false)
    {
        this.enable = enable;
        this.plugin = plugin;
        this.streamproxy = streamproxy;

        if (host != null)
            this.host = host.StartsWith("http") ? host : Decrypt(host);
    }


    /// <summary>true — отдавать DASH (до 1080p), false — HLS (~640p).</summary>
    public bool dash { get; set; }

    /// <summary>true — показывать и HLS, и DASH отдельными пунктами.</summary>
    public bool both { get; set; }


    public ModuleConf Clone()
    {
        return (ModuleConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}
