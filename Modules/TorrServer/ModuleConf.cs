using Shared.Models.Module;

namespace TorrServer
{
    public class ModuleConf : ModuleBaseConf
    {
        public string releases { get; set; }

        public bool rdb { get; set; }

        public string defaultPasswd { get; set; }

        public int tsport { get; set; }

        public int group { get; set; }

        public bool checkfile { get; set; }
    }
}
