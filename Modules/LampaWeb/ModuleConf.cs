using Shared.Models.Module;
using System.Collections.Generic;

namespace LampaWeb
{
    public class CustomPlugin
    {
        public string url { get; set; }

        public string name { get; set; }

        public string author { get; set; }
    }

    public class ModuleConf : ModuleBaseConf
    {
        public bool autoupdate { get; set; }

        public string git { get; set; }

        public string tree { get; set; }

        public int intervalupdate { get; set; }

        public string index { get; set; }

        public string path { get; set; }

        public bool basetag { get; set; }

        public InitPlugins initPlugins { get; set; } = new InitPlugins();


        public Dictionary<string, string> appReplace { get; set; }

        public Dictionary<string, string> cssReplace { get; set; }
    }

    public class InitPlugins
    {
        public bool pirate_store { get; set; }

        public bool jacred { get; set; }

        public bool dlna { get; set; }

        public bool tracks { get; set; }

        public bool transcoding { get; set; }

        public bool tmdbProxy { get; set; }

        public bool cubProxy { get; set; }

        public bool online { get; set; }

        public bool catalog { get; set; }

        public bool sisi { get; set; }

        public bool torrserver { get; set; }

        public bool backup { get; set; }


        public bool sync { get; set; }

        public bool bookmark { get; set; }

        public bool timecode { get; set; }

        public List<CustomPlugin> customPlugins { get; set; }
    }
}
