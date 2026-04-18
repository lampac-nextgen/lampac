using Shared.Models.Base;
using System;

namespace Collaps
{
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


        public bool dash { get; set; }


        public ModuleConf Clone()
        {
            return (ModuleConf)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return MemberwiseClone();
        }
    }
}
