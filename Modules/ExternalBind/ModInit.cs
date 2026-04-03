using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ExternalBind
{
    public class ModInit : IModuleLoaded
    {
        static readonly HashSet<string> ExternalBindPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/lite/filmixpro",
            "/lite/rhs/bind",
            "/lite/kinopubpro",
            "/lite/getstv/bind",
            "/lite/vokinotk",
            "/lite/iptvonline/bind",
        };

        static readonly Func<bool, EventMiddleware, Task<bool>> MiddlewareHandler = OnMiddleware;

        public void Loaded(InitspaceModel initspace)
        {
            EventListener.Middleware += MiddlewareHandler;
        }

        public void Dispose()
        {
            EventListener.Middleware -= MiddlewareHandler;
        }

        static Task<bool> OnMiddleware(bool first, EventMiddleware e)
        {
            var requestInfo = e.httpContext.Features.Get<RequestModel>();
            var path = e.httpContext.Request.Path.Value;
            if (requestInfo != null && path != null && ExternalBindPaths.Contains(path))
                requestInfo.IsLocalIp = true;

            return Task.FromResult(true);
        }
    }
}
