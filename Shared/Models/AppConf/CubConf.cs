namespace Shared.Models.AppConf;

public class CubConf : Iproxy
{
    public string scheme { get; set; }

    public string domain { get; set; }

    public string mirror { get; set; }

    public string api_key { get; set; }


    public bool useproxy { get; set; }

    public bool useproxystream { get; set; }

    public string globalnameproxy { get; set; }

    public ProxySettings proxy { get; set; }
}
