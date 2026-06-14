using Microsoft.AspNetCore.Http;
using Shared.Models.Events;
using System;
using System.Threading.Tasks;

namespace Core.Middlewares;

public class ModuleAsync
{
    private readonly RequestDelegate _next;
    private readonly bool first;

    public ModuleAsync(RequestDelegate next, bool first)
    {
        _next = next;
        this.first = first;
    }

    async public Task InvokeAsync(HttpContext httpContext)
    {
        var middlewareEvent = new EventMiddleware(first, httpContext);

        foreach (Func<bool, EventMiddleware, Task<bool>> handler in EventListener.MiddlewareAsync.GetInvocationList())
        {
            bool next = await handler(first, middlewareEvent);
            if (!next)
                return;
        }

        await _next(httpContext);
    }
}
