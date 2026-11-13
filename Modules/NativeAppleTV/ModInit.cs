using NativeAppleTV.Models;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NativeAppleTV;

/// <summary>
/// Вход для нативного приложения Lampa/Luxo на Apple TV (протокол luxo-source/1): публикует манифест
/// в /api/capabilities → source и выдаёт токены, которые ядро узнаёт как обычных пользователей accsdb.
/// </summary>
public class ModInit : IModuleLoaded
{
    public static NativeAppleTVConf conf;

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.Accsdb += AllowAuthRoutes;

    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.Accsdb -= AllowAuthRoutes;
        ModuleCapabilities.RemoveSource();
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("NativeAppleTV", new NativeAppleTVConf()
        {
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/nativeappletv/auth/poll", new WafLimitMap { limit = 5, second = 3 }),
                new("^/nativeappletv/", new WafLimitMap { limit = 10, second = 2 })
            }
        });

        ModuleCapabilities.SetSource(BuildManifest(conf));
    }

    static object BuildManifest(NativeAppleTVConf c)
    {
        var methods = new List<object>();

        foreach (string name in c.methods ?? [])
        {
            switch (name?.Trim().ToLowerInvariant())
            {
                case "code_push":
                    methods.Add(new
                    {
                        type = "code_push",
                        start = new { path = "nativeappletv/auth/start", method = "POST" },
                        poll = new { path = "nativeappletv/auth/poll", method = "POST", body = new { code = "{code}" } },
                        link_template = !string.IsNullOrWhiteSpace(c.bot_link) ? c.bot_link : null,
                        length = ChallengeStore.CodeLength,
                        ttl = c.code_ttl,
                        interval = c.poll_interval,
                        instructions = "Отсканируйте QR-код телефоном и подтвердите вход"
                    });
                    break;

                case "token":
                    methods.Add(new
                    {
                        type = "token",
                        instructions = "Вставьте токен доступа"
                    });
                    break;
            }
        }

        if (methods.Count == 0)
            return null;

        return new
        {
            protocol = "luxo-source/1",
            id = c.id,
            title = c.title,
            family = "lampac",
            ttl = 3600,
            auth = new
            {
                required = CoreInit.conf.accsdb.enable,
                methods,
                credential = new { @in = "query", name = "token" },
                verify = new { path = "nativeappletv/auth/check", method = "POST", body = new { token = "{credential}" } }
            }
        };
    }

    /// <summary>accsdb закрывает всё без токена, включая сам вход, поэтому маршруты входа открываем явно.</summary>
    static bool AllowAuthRoutes(EventAccsdb e)
    {
        string path = e.httpContext.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/nativeappletv/", StringComparison.OrdinalIgnoreCase))
            return false;

        e.requestInfo.IsAnonymousRequest = true;
        return true;
    }
}
