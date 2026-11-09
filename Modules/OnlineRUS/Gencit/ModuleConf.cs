using Shared.Models.Base;
using System;

namespace Gencit;

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

    public string api_host { get; set; } = "https://malsed.site";

    public ModuleConf Clone()
        => (ModuleConf)MemberwiseClone();

    object ICloneable.Clone()
        => MemberwiseClone();
}
