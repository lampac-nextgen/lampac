using Shared.Models.Base;
using System;

namespace KubikVKube;

public class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf(string plugin, string host, bool enable = true, bool streamproxy = true)
    {
        this.enable = enable;
        this.plugin = plugin;
        this.host = host;
        this.streamproxy = streamproxy;
    }

    /// <summary>API VideoHub (основной плеер сайта, «Смотреть онлайн»)</summary>
    public string vhapi { get; set; } = "https://plapi.cdnvideohub.com/api/v1";

    /// <summary>publisher-id сайта у VideoHub, если на странице его нет</summary>
    public int vhpub { get; set; } = 2646;

    /// <summary>Зеркала запасного плеера (/lat/{id}); домен со страницы пробуется первым</summary>
    public string[] lathosts { get; set; } = new[] { "https://tomion.org", "https://ylitron.pro" };

    /// <summary>true — только озвучки «Кубик в Кубе»; по умолчанию показываются все озвучки всех плееров (Кубик первым)</summary>
    public bool onlykubik { get; set; }

    /// <summary>Отдавать прямые mp4 VideoHub по качествам (2160p…360p) вместе с HLS</summary>
    public bool mp4 { get; set; } = true;

    /// <summary>API VK Видео (заливки в сообществе студии)</summary>
    public string vkapi { get; set; } = "https://api.vkvideo.ru";

    /// <summary>Выдача анонимного токена VK</summary>
    public string vktoken { get; set; } = "https://login.vk.com/?act=get_anonym_token";

    public bool usevh { get; set; } = true;

    public bool usevk { get; set; } = true;

    public bool uselat { get; set; } = true;

    public ModuleConf Clone()
        => (ModuleConf)MemberwiseClone();

    object ICloneable.Clone()
        => MemberwiseClone();
}
