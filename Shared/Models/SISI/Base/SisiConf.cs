using Shared.Models.Module;

namespace Shared.Models.SISI.Base
{
    public class SisiConf : ModuleBaseConf
    {
        public bool rsize { get; set; }

        public string rsize_host { get; set; }

        public string bypass_host { get; set; }

        public string[] rsize_disable { get; set; }

        public string[] proxyimg_disable { get; set; }

        public int heightPicture { get; set; }

        public int widthPicture { get; set; }
    }
}
