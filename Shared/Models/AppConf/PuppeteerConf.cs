using Shared.Models.Browser;

namespace Shared.Models.AppConf;

public class PuppeteerConf
{
    public bool enable { get; set; }

    public KeepopenContext context { get; set; }

    public bool Headless { get; set; }

    public bool DEV { get; set; }

    public bool consoleLog { get; set; }

    public bool Devtools { get; set; }

    public string executablePath { get; set; }

    public string[] Args { get; set; }

    public string NODE_OPTIONS { get; set; }
}
