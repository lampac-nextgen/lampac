using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System;
using System.Threading.Tasks;

namespace Potok;

public class ModInit : IModuleLoaded
{
    public static string modpath;

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;
        EventListener.BadInitialization += BadInitialization;
        EventListener.Middleware += Middleware;
    }

    public void Dispose()
    {
        EventListener.BadInitialization -= BadInitialization;
        EventListener.Middleware -= Middleware;
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

    static bool Middleware(bool first, EventMiddleware e)
    {
        if (e.httpContext.Request.Query.TryGetValue("initial", out var initial) && initial.Count > 0 && initial[0] == "potok")
        {
            var builder = new QueryBuilder();

            foreach (var kv in QueryHelpers.ParseQuery(e.httpContext.Request.QueryString.Value))
            {
                if (string.Equals(kv.Key, "rjson", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var value in kv.Value)
                    builder.Add(kv.Key, value);
            }

            builder.Add("rjson", "true");
            e.httpContext.Request.QueryString = builder.ToQueryString();
        }

        return true;
    }
}
