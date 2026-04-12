using Shared.Models.Base;
using System;

namespace Rezka
{
    public class RezkaSettings : BaseSettings, ICloneable
    {
        public RezkaSettings(string plugin, string host, bool streamproxy = false)
        {
            enable = true;
            this.plugin = plugin;
            this.streamproxy = streamproxy;

            if (host != null)
                this.host = host.StartsWith("http") ? host : Decrypt(host);
        }


        public bool premium { get; set; }

        public bool reserve { get; set; }

        public string uacdn { get; set; }

        public bool forceua { get; set; }

        public bool? ajax { get; set; }


        public RezkaSettings Clone()
        {
            return (RezkaSettings)MemberwiseClone();
        }

        object ICloneable.Clone()
        {
            return MemberwiseClone();
        }
    }
}
