using Shared.Models.Module;
using Shared.Models.Module.Interfaces;

namespace WebLog
{
    public class ModInit : IModuleLoaded
    {
        public static string modpath;

        public void Loaded(InitspaceModel baseconf)
        {
            modpath = baseconf.path;
        }

        public void Dispose()
        {
        }
    }
}
