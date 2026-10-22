using System.Collections.Generic;

namespace AnimeLib;

public class ApiResponse<T>
{
    public T data { get; set; }
}

public class AnimeLibTokenState
{
    public string token { get; set; }
    public string refresh_token { get; set; }
    public long refresh_time { get; set; }
}

public class DataSearch
{
    public string rus_name { get; set; }
    public string eng_name { get; set; }
    public string slug_url { get; set; }
    public string releaseDate { get; set; }
    public Сover cover { get; set; }
}

public class Episode
{
    public int id { get; set; }
    public string name { get; set; }
    public string number { get; set; }
    public string season { get; set; }
}

public class EpisodeDetails
{
    public Player[] players { get; set; }
}

public class VideoConstants
{
    public VideoServer[] videoServers { get; set; }
}

public class VideoServer
{
    public string id { get; set; }
    public string url { get; set; }
}

public class Player
{
    public string player { get; set; }

    public string video_domain { get; set; }

    public PlayerTeam team { get; set; }

    public Video video { get; set; }
}

public class PlayerTeam
{
    public string name { get; set; }
}

public class Quality
{
    public string href { get; set; }

    public int quality { get; set; }
}

public class Video
{
    public List<Quality> quality { get; set; }
}

public class Сover
{
    public string @default { get; set; }
}

public class TokenResponse
{
    public string access_token { get; set; }
    public string refresh_token { get; set; }
}
