using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System;

namespace Potok;

public class ModInit : IModuleLoaded
{
    public static string modpath;

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;
        EventListener.BadInitialization += BadInitialization;
    }

    public void Dispose()
    {
        EventListener.BadInitialization -= BadInitialization;
    }

    ActionResult BadInitialization(EventBadInitialization e)
    {
        if (IsPotok(e.httpContext))
        {
            e.init.rhub = false;
            e.init.streamproxy = true;
        }

        return default;
    }

    static bool IsPotok(HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue("initial", out StringValues initial) && initial.Count > 0)
            return initial[0].StartsWith("potok", StringComparison.OrdinalIgnoreCase);

        return false;
    }
}
