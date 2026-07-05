using Shared.Models.AppConf;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared;

namespace LampacApk;

public class ModInit : IModuleLoaded
{
    public static string ModulePath { get; private set; }

    public void Loaded(InitspaceModel baseconf)
    {
        ModulePath = baseconf.path;

        CoreInit.conf.WAF.limit_map.Insert(0, new WafLimitRootMap(
            "^/(lampac|android)\\.apk",
            new WafLimitMap { limit = 1, second = 5 }
        ));
    }

    public void Dispose()
    {
    }
}
