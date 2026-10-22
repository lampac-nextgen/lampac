using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Gencit;

public class GencitPageData
{
    public GencitPlayerData player { get; set; }

    public GencitAdsConfig ads { get; set; }
}

public class GencitApiData
{
    public long id { get; set; }

    public int playlist_id { get; set; }

    public long kinopoisk_id { get; set; }

    public string imdb_id { get; set; }

    public int max_quality { get; set; }

    public string title { get; set; }
}

public class GencitVideoData
{
    public string h { get; set; }

    public string video { get; set; }

    public string video_new { get; set; }

    public List<JToken> cc { get; set; }

    public int duration { get; set; }

    public int with_ads { get; set; }
}

public class GencitPlayerData
{
    public Dictionary<string, JToken> voices { get; set; }

    public GencitPlayerConfig config { get; set; }

    public GencitPlaylist playlist { get; set; }
}

public class GencitPlayerConfig
{
    public string h { get; set; }

    public string video { get; set; }

    public string video_new { get; set; }

    public int video_id { get; set; }

    public string request_full { get; set; }

    public string api_base_url { get; set; }
}

public class GencitPlaylist
{
    public GencitPlaylistCurrent current { get; set; }

    public GencitSerial serial { get; set; }
}

public class GencitPlaylistCurrent
{
    public int id { get; set; }

    public string serialName { get; set; }

    public int contentType { get; set; }

    public bool singleSeason { get; set; }

    public int startSeason { get; set; }
}

public class GencitSerial
{
    public GencitSerialCurrent current { get; set; }

    public List<List<GencitEpisode>> list { get; set; }
}

public class GencitSerialCurrent
{
    public int season { get; set; }

    public int episode { get; set; }

    public int voiceId { get; set; }

    public string voiceName { get; set; }

    public bool voiceTag { get; set; }
}

public class GencitEpisode
{
    public int num { get; set; }

    public List<GencitEpisodeVoice> voices { get; set; }

    public GencitSpecialEpisode spec_ep { get; set; }
}

public class GencitEpisodeVoice
{
    public int video_id { get; set; }

    public int voice_id { get; set; }
}

public class GencitSpecialEpisode
{
    public string custom_name { get; set; }

    public int episode { get; set; }
}

public class GencitAdsConfig
{
    public GencitFilm film { get; set; }
}

public class GencitFilm
{
    public long kp_id { get; set; }

    public long imdb_id { get; set; }
}
