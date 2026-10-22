using Shared.Models.Base;

namespace Redheadsound;

public class ModuleConf : BaseSettings
{
    public ModuleConf(string plugin, string host)
    {
        this.plugin = plugin;
        this.host = host;
    }

    /// <summary>Зеркала сайта RHS, если host не отвечает (перебираются по порядку).</summary>
    public string[] mirrors { get; set; } = new[] { "https://redheadsound3.top", "https://redheadsound1.top", "https://redheadsound2.top", "https://redheadsound.top" };

    public bool m4s { get; set; } = true;

    public bool debug { get; set; }

    /// <summary>card — открыть настоящую карточку сайта; fake — подставная карточка с одним iframe ladoni (нужен lat в HTML).</summary>
    public string ladonimode { get; set; } = "http";

    /// <summary>Сколько секунд ждать m3u8 от плеера сайта.</summary>
    public int ladonitimeout { get; set; } = 60;

    /// <summary>Сколько раз нажимать Play.</summary>
    public int ladoniclicks { get; set; } = 6;

    /// <summary>Резать рекламу (adfox, vast и т.п.) в браузере.</summary>
    public bool ladoniabortads { get; set; } = true;

    /// <summary>Проверять и добавлять соседние качества (/720/, /1080/ …) к пойманному m3u8.</summary>
    public bool ladoniqualities { get; set; } = true;

    /// <summary>Минуты жизни пойманной ссылки (она подписана временем).</summary>
    public int ladonicache { get; set; } = 3;

    /// <summary>Минимальная длина видео (сек.): более короткие m3u8 считаются рекламой плеера.</summary>
    public int ladonimindur { get; set; } = 600;

    /// <summary>Показывать все озвучки плеера ladoni (LostFilm, Кубик в Кубе …), а не только RHS.</summary>
    public bool ladoniallvoices { get; set; } = true;
}
