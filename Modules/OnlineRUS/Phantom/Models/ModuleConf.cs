using Shared.Models.Base;

namespace Phantom;

public class ModuleConf : BaseSettings
{
    public ModuleConf(string plugin, string apihost, string linkhost, string token, string secret_token, bool m4s)
    {
        this.plugin = plugin;
        this.token = token;
        this.secret_token = secret_token;
        this.m4s = m4s;
        this.linkhost = linkhost;
        this.apihost = apihost;
    }


    public string secret_token { get; set; }

    public string linkhost { get; set; }

    public bool m4s { get; set; }

    public bool debug { get; set; }
}
