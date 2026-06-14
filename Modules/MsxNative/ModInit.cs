using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;

namespace MsxNative;

public class ModInit : IModuleLoaded
{
    public void Loaded(InitspaceModel baseconf)
    {
        EventListener.Middleware += Middleware;
        EventListener.BadInitialization += BadInitialization;

        EventListener.SisiChannels += SisiAPI.Channels;
        EventListener.SisiPlaylistResult += SisiAPI.PlaylistResult;
        EventListener.SisiOnResult += SisiAPI.OnResult;
    }

    public void Dispose()
    {
        EventListener.Middleware -= Middleware;
        EventListener.BadInitialization -= BadInitialization;

        EventListener.SisiChannels -= SisiAPI.Channels;
        EventListener.SisiPlaylistResult -= SisiAPI.PlaylistResult;
        EventListener.SisiOnResult -= SisiAPI.OnResult;
    }


    bool Middleware(bool first, EventMiddleware e)
    {
        if (first &&
            CoreInit.conf.accsdb.enable &&
            Utilities.IsMsxPlayer(e.httpContext) &&
            e.httpContext.Request.Path.Value == "/sisi")
        {
            var requestInfo = e.httpContext.Features.Get<RequestModel>();
            requestInfo.IsAnonymousRequest = true;
            return true;
        }

        return true;
    }

    ActionResult BadInitialization(EventBadInitialization e)
    {
        if (Utilities.IsMsxPlayer(e.httpContext))
        {
            e.init.rhub = false;
            e.init.streamproxy = true;
        }

        return default;
    }
}
