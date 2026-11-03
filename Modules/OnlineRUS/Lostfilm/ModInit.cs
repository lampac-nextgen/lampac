using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace Lostfilm;

public class ModInit : IModuleLoaded, IModuleOnline
{
    public static LostfilmSettings conf;

    /// <summary>
    /// Источник регистрируется ТОЛЬКО при наличии внешнего TorrServer (conf.torrs / auth_torrs).
    /// Без TorrServer стримить torrent-контент нечем — модуль деградирует молча, не ломая выдачу.
    /// </summary>
    public List<ModuleOnlineItem> Invoke(HttpContext httpContext, RequestModel requestInfo, string host, OnlineEventsModel args)
    {
        var online = new List<ModuleOnlineItem>();

        bool tsEnable = (conf?.torrs != null && conf.torrs.Length > 0)
            || (conf?.auth_torrs != null && conf.auth_torrs.Count > 0);

        if (tsEnable)
            online.Add(new(conf, "lostfilm", "LostFilm"));

        return online;
    }

    public void Loaded(InitspaceModel baseconf)
    {
        CoreInit.conf.online.with_search.Add("lostfilm");

        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.OnlineApiQuality += onlineApiQuality;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.OnlineApiQuality -= onlineApiQuality;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("Lostfilm", new LostfilmSettings("Lostfilm", "https://www.lostfilm.tv")
        {
            enable = true,
            enabled = true,
            displayindex = 552,
            displayname = "LostFilm",
            imgHost = "https://static.lostfilm.top",
            ua = LostfilmSettings.UA153,
            cookie = "",
            login = "",
            passwd = ""
        });
    }

    string onlineApiQuality(EventOnlineApiQuality e)
    {
        return e.balanser switch
        {
            "lostfilm" => " ~ 1080p",
            _ => null
        };
    }
}

/// <summary>
/// Настройки модуля Lostfilm. Домены/зеркала/креды НЕ хардкодятся в бизнес-логике —
/// всё берётся из секции "Lostfilm" init.conf (или из значений конструктора по умолчанию).
/// </summary>
public class LostfilmSettings : BaseSettings, ICloneable
{
    /// <summary>UA для ВСЕХ запросов к lostfilm.tv (проверено live 2026-09-16).</summary>
    public const string UA153 = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36";

    public LostfilmSettings(string plugin, string host)
    {
        enable = true;
        this.plugin = plugin;

        if (host != null)
            this.host = host.StartsWith("http") ? host : Decrypt(host);
    }

    /// <summary>Хост статики/постеров (иконки поиска на сайте отдаются относительными путями).</summary>
    public string imgHost { get; set; } = "https://static.lostfilm.top";

    /// <summary>Внешние TorrServer в формате PidTor (строка = host, массив = несколько).</summary>
    public string[] torrs { get; set; }

    /// <summary>TorrServer с авторизацией (формат PidTor).</summary>
    public List<PidTorAuthTS> auth_torrs { get; set; }

    /// <summary>UA (единая константа для всех запросов к сайту).</summary>
    public string ua { get; set; } = UA153;


    public LostfilmSettings Clone()
    {
        return (LostfilmSettings)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}
