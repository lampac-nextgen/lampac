using Shared.Models.Base;
using System;

namespace Zetflix
{
    public class ModuleConf : BaseSettings
    {
        public ModuleConf(string plugin, string host, bool enable = true, bool streamproxy = false, bool rip = false)
        {
            this.enable = enable;
            this.plugin = plugin;
            this.streamproxy = streamproxy;
            this.rip = rip;

            if (host != null)
                this.host = host.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? host : Decrypt(host);
        }

        public bool browser_keepopen { get; set; }
    }
}
