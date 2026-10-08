using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace KubikVKube;

#region Сайт kubikvkube.com
public class SiteSearchItem
{
    public string href { get; set; }

    public string title { get; set; }

    public int year { get; set; }

    public bool serial { get; set; }

    public string img { get; set; }
}

public class SiteSearchResult
{
    public List<SiteSearchItem> items { get; set; } = new List<SiteSearchItem>();
}

public class SitePage
{
    public string href { get; set; }

    public string title { get; set; }

    public string original_title { get; set; }

    public int year { get; set; }

    public bool serial { get; set; }

    public long kinopoisk_id { get; set; }

    public int publisher { get; set; }

    public string priority_voice { get; set; }

    public string lat_host { get; set; }

    public int lat_id { get; set; }

    public int lat_voice { get; set; }

    /// <summary>заливка в VK-сообществе студии: oid_id_hash из vkvideo.ru/video_ext.php</summary>
    public string vk_video { get; set; }
}
#endregion

#region VideoHub
public class VhPlaylist
{
    public string titleName { get; set; }

    public bool isSerial { get; set; }

    public List<VhItem> items { get; set; }
}

public class VhItem
{
    public string vkId { get; set; }

    public string voiceStudio { get; set; }

    public string voiceType { get; set; }

    public int season { get; set; }

    public int episode { get; set; }
}

public class VhVideo
{
    public long unitedVideoId { get; set; }

    public string failoverHost { get; set; }

    public VhSources sources { get; set; }
}

public class VhSources
{
    public string hlsUrl { get; set; }

    public string dashUrl { get; set; }

    public string mpeg4kUrl { get; set; }

    public string mpeg2kUrl { get; set; }

    public string mpegQhdUrl { get; set; }

    public string mpegFullHdUrl { get; set; }

    public string mpegHighUrl { get; set; }

    public string mpegMediumUrl { get; set; }

    public string mpegLowUrl { get; set; }

    public string mpegLowestUrl { get; set; }
}

public class VhStream
{
    public string hls { get; set; }

    /// <summary>качество → прямой mp4, от лучшего к худшему</summary>
    public List<KeyValuePair<string, string>> mp4 { get; set; } = new List<KeyValuePair<string, string>>();
}
#endregion

#region Запасной плеер (/lat/)
public class LatPlayerData
{
    public Dictionary<string, JToken> voices { get; set; }

    public LatConfig config { get; set; }

    public LatPlaylist playlist { get; set; }
}

public class LatConfig
{
    public string video { get; set; }

    public string video_new { get; set; }

    public int video_id { get; set; }
}

public class LatPlaylist
{
    public LatPlaylistCurrent current { get; set; }

    public LatSerial serial { get; set; }
}

public class LatPlaylistCurrent
{
    public int id { get; set; }

    public int contentType { get; set; }

    public int startSeason { get; set; }
}

public class LatSerial
{
    public LatSerialCurrent current { get; set; }

    public List<List<LatEpisode>> list { get; set; }
}

public class LatSerialCurrent
{
    public int season { get; set; }

    public int episode { get; set; }

    public int voiceId { get; set; }

    public string voiceName { get; set; }
}

public class LatEpisode
{
    public int num { get; set; }

    public List<LatEpisodeVoice> voices { get; set; }
}

public class LatEpisodeVoice
{
    public int video_id { get; set; }

    public int voice_id { get; set; }
}

public class LatVideo
{
    public string video { get; set; }

    public string video_new { get; set; }
}
#endregion

#region VK Видео (заливки студии)
public class VkRoot
{
    public VkResponse response { get; set; }

    public VkError error { get; set; }
}

public class VkError
{
    public int error_code { get; set; }

    public string error_msg { get; set; }
}

public class VkResponse
{
    public List<VkItem> items { get; set; }
}

public class VkItem
{
    public string title { get; set; }

    public long duration { get; set; }

    public Dictionary<string, string> files { get; set; }
}
#endregion

#region Единый каталог плееров
public class KubikTrack
{
    /// <summary>vh — VideoHub, vk — VK Видео студии, lat — запасной плеер</summary>
    public string player { get; set; }

    /// <summary>ключ озвучки: player|нормализованное имя</summary>
    public string voice { get; set; }

    public string voice_name { get; set; }

    public int season { get; set; }

    public int episode { get; set; }

    public string vk { get; set; }

    public int lat_video { get; set; }

    public int lat_voice { get; set; }

    public string vk_video { get; set; }
}

public class KubikCatalog
{
    public SitePage page { get; set; }

    public bool serial { get; set; }

    public string lat_host { get; set; }

    public List<KubikTrack> tracks { get; set; } = new List<KubikTrack>();

    /// <summary>почему плеер не дал данных (для диагностики)</summary>
    public string vh_error { get; set; }

    public string lat_error { get; set; }
}
#endregion
