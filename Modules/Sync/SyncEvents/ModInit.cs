using Shared.Models.Module;
using Shared.Models.Module.Interfaces;

namespace SyncEvents;

public class ModInit : IModuleLoaded
{
    public void Loaded(InitspaceModel baseconf)
    {
        NwsEvents.Start(onlyreg: false);

        // Шина живёт только при включённом BaseModule.nws — без неё /nws не смонтирован.
        if (Shared.CoreInit.conf.BaseModule.nws)
            ModuleCapabilities.Set("events", 1);
    }

    public void Dispose()
    {
        ModuleCapabilities.Remove("events");
        NwsEvents.Stop();
    }
}
