using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lamson;

public class ModInit : IModuleLoaded
{
    public void Loaded(InitspaceModel conf)
    {
        EventListener.Middleware += Invoke;
        EventListener.MiddlewareAsync += InvokeAsync;
    }

    public void Dispose()
    {
        EventListener.Middleware -= Invoke;
        EventListener.MiddlewareAsync -= InvokeAsync;
    }


    public static bool Invoke(bool first, EventMiddleware e)
    {
        var httpContext = e.httpContext;
        var requestInfo = httpContext.Features.Get<RequestModel>();
        if (first || requestInfo.IsLocalRequest || requestInfo.IsAnonymousRequest)
            return true;

        if (Regex.IsMatch(httpContext.Request.Path.Value, "^/(kinogram|porngram|lamson)"))
            httpContext.Response.Headers["X-Lamson"] = "Middleware was here";

        return true;
    }


    async public static Task<bool> InvokeAsync(bool first, EventMiddleware e)
    {
        if (!first)
            return true;

        var httpContext = e.httpContext;

        if (httpContext.Request.Path.Value.StartsWith("/lamson", StringComparison.OrdinalIgnoreCase))
        {
            string token = httpContext.Request.Query["token"];
            if (string.IsNullOrWhiteSpace(token))
            {
                httpContext.Response.ContentType = "application/json; charset=utf-8";
                await httpContext.Response.WriteAsync("[{\"error\":\"token == null\"}]", httpContext.RequestAborted);
                return false;
            }

            bool isvip = true; // (await Http.Get($"http://myapi.com/vip?token={token}")) == "OK";
            if (isvip)
                return true;
        }

        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync("[{\"error\":\"not vip\"}]", httpContext.RequestAborted);
        return false;
    }
}
