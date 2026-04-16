using Shared.Models.Base;

namespace PizdatoeHD
{
    public class ModuleConf : BaseSettings
    {
        public ModuleConf(string plugin, string host)
        {
            this.plugin = plugin;
            this.host = host;
        }

        public bool premium { get; set; }

        public bool imitationHuman { get; set; }
    }
}
