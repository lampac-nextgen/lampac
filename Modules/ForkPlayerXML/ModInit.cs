using Microsoft.AspNetCore.Mvc;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Threading.Tasks;

namespace ForkXML;

public class ModInit : IModuleLoaded
{
    public void Loaded(InitspaceModel baseconf)
    {
        EventListener.Middleware += Middleware;
        EventListener.BadInitialization += BadInitialization;

        EventListener.CatalogChannels += CatalogAPI.Channels;
        EventListener.CatalogList += CatalogAPI.List;
        EventListener.CatalogCard += CatalogAPI.Card;

        EventListener.SisiChannels += SisiAPI.Channels;
        EventListener.SisiPlaylistResult += SisiAPI.PlaylistResult;
        EventListener.SisiOnResult += SisiAPI.OnResult;

        EventListener.OnlineChannels += OnlineAPI.Channels;
        EventListener.OnlineContentTpl += OnlineAPI.ContentTpl;
        EventListener.VideoTpl += OnlineAPI.VideoTpl;
    }

    public void Dispose()
    {
        EventListener.Middleware -= Middleware;
        EventListener.BadInitialization -= BadInitialization;

        EventListener.CatalogChannels -= CatalogAPI.Channels;
        EventListener.CatalogList -= CatalogAPI.List;
        EventListener.CatalogCard -= CatalogAPI.Card;

        EventListener.SisiChannels -= SisiAPI.Channels;
        EventListener.SisiPlaylistResult -= SisiAPI.PlaylistResult;
        EventListener.SisiOnResult -= SisiAPI.OnResult;

        EventListener.OnlineChannels -= OnlineAPI.Channels;
        EventListener.OnlineContentTpl -= OnlineAPI.ContentTpl;
        EventListener.VideoTpl -= OnlineAPI.VideoTpl;
    }


    bool Middleware(bool first, EventMiddleware e)
    {
        if (Utilities.IsForkPlayer(e.httpContext) && e.httpContext.Request.Path.Value == "/")
        {
            string args = Utilities.ClearArgs(e.httpContext.Request.Query);
            e.httpContext.Response.Redirect("/fxml" + (!string.IsNullOrEmpty(args) ? $"?{args.Substring(0, 1)}" : string.Empty));
            return false;
        }

        return true;
    }

    ActionResult BadInitialization(EventBadInitialization e)
    {
        if (Utilities.IsForkPlayer(e.httpContext))
        {
#warning костыль для rchstreamproxy, надо орентироваться на &platform=

            /*
             * пример реализации
             * 
            string platform = "apk"; // cors, web
            if (e.init.rchstreamproxy != null && e.init.rchstreamproxy.Contains(platform))
            {
                e.init.rhub = false;
                e.init.streamproxy = true;
            }
            else
            {
                e.init.rhub = false;
                e.init.rchstreamproxy = null;
            }
            */

            e.init.rhub = false;
            e.init.streamproxy = true;
        }

        return default;
    }
}
