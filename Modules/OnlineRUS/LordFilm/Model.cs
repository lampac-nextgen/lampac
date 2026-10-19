using System.Collections.Generic;

namespace LordFilm;

/// <summary>Результат разбора плеера nextembed.ws — либо фильм, либо сериал.</summary>
public class EmbedModel
{
    public MovieItem movie { get; set; }

    public SeasonItem[] seasons { get; set; }

    public bool IsEmpty => movie == null && (seasons == null || seasons.Length == 0);
}

public class MovieItem
{
    public string hls { get; set; }

    public string dash { get; set; }

    public string voicename { get; set; }

    /// <summary>Названия дорожек озвучки по порядку (индекс = номер дорожки в master.m3u8).</summary>
    public List<string> audio { get; set; }

    public List<Cc> cc { get; set; }
}

public class SeasonItem
{
    public int season { get; set; }

    public List<EpisodeItem> episodes { get; set; }
}

public class EpisodeItem
{
    public string episode { get; set; }

    public string hls { get; set; }

    public string dash { get; set; }

    public string voicename { get; set; }

    /// <summary>Названия дорожек озвучки по порядку (индекс = номер дорожки в master.m3u8).</summary>
    public List<string> audio { get; set; }

    public List<Cc> cc { get; set; }
}

public class Cc
{
    public string url { get; set; }

    public string name { get; set; }
}

/// <summary>Строка выдачи поиска сайта.</summary>
public class SearchItem
{
    public string uri { get; set; }

    public string name { get; set; }

    public string year { get; set; }

    public string poster { get; set; }
}

#region nextembed dto
public class NeAudio
{
    public string[] names { get; set; }
}

public class NeCc
{
    public string url { get; set; }

    public string name { get; set; }
}

public class NeSeason
{
    public int season { get; set; }

    public bool blocked { get; set; }

    public NeEpisode[] episodes { get; set; }
}

public class NeEpisode
{
    public string episode { get; set; }

    public string hls { get; set; }

    public string dasha { get; set; }

    public string dash { get; set; }

    public NeAudio audio { get; set; }

    public NeCc[] cc { get; set; }
}
#endregion
