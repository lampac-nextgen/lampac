using System.Collections.Generic;

namespace Redheadsound;

/// <summary>Заголовки, с которыми плеер запросил m3u8. Их повторяет stream-proxy.</summary>
public class StreamData
{
    public Dictionary<string, string> headers { get; set; } = new Dictionary<string, string>();
}

/// <summary>Карточка сайта RHS: одна карточка = фильм или один сезон.</summary>
public class SiteCard
{
    /// <summary>Хвост адреса без .html: 47727-pacany-5-sezon-2026.</summary>
    public string cid { get; set; }

    public string url { get; set; }

    public string name { get; set; }

    /// <summary>0 — фильм или сезон не указан.</summary>
    public int season { get; set; }

    /// <summary>Последняя вышедшая серия по заголовку карточки, 0 — неизвестно.</summary>
    public int episodes { get; set; }

    /// <summary>iframe ladoni из HTML карточки, если он там есть.</summary>
    public string lat { get; set; }
}
