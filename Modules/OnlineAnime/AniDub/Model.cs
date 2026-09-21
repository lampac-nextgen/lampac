using System;
using System.Collections.Generic;

namespace AniDub;

// Вид контента карточки. Подпись в выдаче — НЕ всегда категория: у части
// тайтлов там стоит «1 сезон», и тогда вид неизвестен.
public enum AnidubKind
{
    Unknown = 0,
    Movie = 1,
    Serial = 2
}

public class AnidubCard
{
    public string url { get; set; }
    public string rus { get; set; }
    public string orig { get; set; }
    public int year { get; set; }
    public string cat { get; set; }
    public AnidubKind kind { get; set; }
}

public class AnidubEpisode
{
    public int num { get; set; }

    // Серия либо на общем плеере (hash), либо вставкой sibnet (sib) — вместе
    // они не встречаются.
    public string hash { get; set; }
    public string sib { get; set; }
}

public class AnidubSeason
{
    public int num { get; set; }
    public string name { get; set; }
    public string url { get; set; }
    public string quality { get; set; }
    public List<AnidubEpisode> episodes { get; set; } = new List<AnidubEpisode>();
}

public class AnidubShow
{
    public int season { get; set; }
    public string quality { get; set; }
    public List<AnidubEpisode> episodes { get; set; } = new List<AnidubEpisode>();
    public DateTime expires { get; set; }
}

public class AnidubVariant
{
    public string label { get; set; }
    public string url { get; set; }
}

public class AnidubStream
{
    public string player { get; set; }
    public List<AnidubVariant> variants { get; set; } = new List<AnidubVariant>();
}
