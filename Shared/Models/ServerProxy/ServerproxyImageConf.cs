namespace Shared.Models.ServerProxy;

public class ServerproxyImageConf : Iproxy
{
    public bool enable { get; set; }


    public bool cache { get; set; }

    public bool cache_rsize { get; set; }

    public int cache_time { get; set; }


    public bool useproxy { get; set; }

    public bool useproxystream { get; set; }

    public string globalnameproxy { get; set; }

    public ProxySettings proxy { get; set; }
}
