using Shared.Models.Module;
using System.Collections.Generic;

namespace LampaWeb.Models
{
    public class ModuleConf : ModuleBaseConf
    {
        public bool autoupdate { get; set; }

        public string git { get; set; }

        public string tree { get; set; }

        public int intervalupdate { get; set; }

        public string index { get; set; }

        public string path { get; set; }

        public bool basetag { get; set; }

        public InitPlugins initPlugins { get; set; } = new();

        public List<LampaPlugin> customPlugins { get; set; }

        public WidgetsConf widgets { get; set; } = new();

        public Dictionary<string, string> appReplace { get; set; }

        public Dictionary<string, string> cssReplace { get; set; }
    }
}
