using Shared.Models.AppConf;
using System.Collections.Generic;

namespace Online
{
    public class ModuleConf : OnlineConf
    {
        public string findkp { get; set; }

        public bool spider { get; set; }

        public string spiderName { get; set; }

        public string component { get; set; }

        public string description { get; set; }

        public bool showquality { get; set; }

        public bool showExtraTitle { get; set; }

        public string apn { get; set; }

        public Dictionary<string, string> appReplace { get; set; }
    }
}
