using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.PlaywrightCore;

namespace NextHUB
{
    public class SisiApi : IModuleSisi
    {
        public List<SisiModuleItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, SisiEventsModel args)
        {
            var channels = new List<SisiModuleItem>(50);

            foreach (string inFile in Directory.GetFiles($"{ModInit.modpath}/sites", "*.yaml"))
            {
                try
                {
                    string plugin = Path.GetFileNameWithoutExtension(inFile);
                    if (!args.lgbt && plugin == "gayporntube")
                        continue;

                    var init = Root.goInit(plugin);
                    if (init == null)
                        continue;

                    if (init.debug)
                        Console.WriteLine("\n" + JsonConvert.SerializeObject(init, Formatting.Indented));

                    if (PlaywrightBrowser.Status == PlaywrightStatus.disabled || init.rhub)
                    {
                        if (init.priorityBrowser != "http" || (init.view != null && init.view.viewsource == false))
                            continue;
                    }

                    channels.Add(new SisiModuleItem(init.displayname, init, $"nexthub?plugin={CrypTo.EncryptQuery(plugin)}", myinit: init));
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "CatchId={CatchId}", "id_852ccf67");
                    Console.WriteLine($"NextHUB: error DeserializeObject {inFile}\n {ex.Message}");
                }
            }

            return channels;
        }
    }
}
