using System.Text.Json.Serialization;

namespace Music;

public class MusicProviderRef
{
    public string provider { get; set; }
    public string external_id { get; set; }
}

public class MusicImage
{
    public string url { get; set; }
    public int? width { get; set; }
    public int? height { get; set; }
}

public class MusicArtist
{
    public string id { get; set; }
    public string name { get; set; }
    public string sort_name { get; set; }
    public string country { get; set; }
    public string description { get; set; }
    public int? search_score { get; set; }
    public List<MusicImage> images { get; set; } = new();
    public List<MusicProviderRef> provider_refs { get; set; } = new();
    public List<MusicAlbum> albums { get; set; } = new();
    public List<MusicBrowseSection> sections { get; set; } = new();
}

public class MusicAlbum
{
    public string id { get; set; }
    public string title { get; set; }
    public string artist_id { get; set; }
    public string artist_name { get; set; }
    public string lookup_query { get; set; }
    public int? year { get; set; }
    public string date { get; set; }
    public string type { get; set; }
    public string release_id { get; set; }
    public string description { get; set; }
    public int? search_score { get; set; }
    public List<string> secondary_types { get; set; } = new();
    public List<MusicImage> images { get; set; } = new();
    public List<MusicProviderRef> provider_refs { get; set; } = new();
    public List<MusicTrack> tracks { get; set; } = new();
}

public class MusicTrack
{
    public string id { get; set; }
    public string title { get; set; }
    public string artist_id { get; set; }
    public string artist_name { get; set; }
    public List<string> artists { get; set; } = new();
    public string album_id { get; set; }
    public string album_title { get; set; }
    public int? duration_ms { get; set; }
    public int? track_number { get; set; }
    public int? disc_number { get; set; }
    public string date { get; set; }
    public int? search_score { get; set; }
    public List<MusicImage> images { get; set; } = new();
    public List<MusicProviderRef> provider_refs { get; set; } = new();

    // добавлено автопродолжением очереди; не влияет на резолв источника
    public bool auto_radio { get; set; }
}

public class MusicAudioMatch
{
    public string provider_id { get; set; }
    public string id { get; set; }
    public string title { get; set; }
    public List<string> artists { get; set; } = new();
    public string album_title { get; set; }
    public int? duration_ms { get; set; }
    public string payload { get; set; }

    // выбран пользователем вручную: обходит эвристики релевантности
    // и не перезаписывается авто-подбором
    public bool pinned { get; set; }
}

public class MusicLyricsResponse
{
    public bool available { get; set; }
    public bool retry { get; set; }
    public string message { get; set; }
    public string source { get; set; }
    public string source_mode { get; set; }
    public bool synced { get; set; }
    public List<MusicLyricsLine> lines { get; set; } = new();
    public string plain { get; set; }
}

public class MusicLyricsLine
{
    public int time_ms { get; set; }
    public string text { get; set; }
}

public class MusicPlaybackSource
{
    public string provider_id { get; set; }
    public string url { get; set; }
    public string external_url { get; set; }
    public string mime_type { get; set; }
    public int? bitrate { get; set; }
    public string quality { get; set; }
    public Dictionary<string, string> headers { get; set; } = new();
    [JsonIgnore]
    public string proxy_url { get; set; }
    [JsonIgnore]
    public string proxy_username { get; set; }
    [JsonIgnore]
    public string proxy_password { get; set; }
}

public class MusicAuthState
{
    public string provider_id { get; set; }
    public string provider_name { get; set; }
    public bool authenticated { get; set; }
    public bool requires_auth { get; set; }
    public bool token_ready { get; set; }
    public bool web_ready { get; set; }
    public string mode { get; set; }
    public string message { get; set; }
}

public class YandexMusicCredentials
{
    public string ym_token { get; set; }
    public string ym_session_id { get; set; }
    public string ym_sessionid2 { get; set; }
    public string ym_yandexuid { get; set; }
    public string ym_yandex_login { get; set; }
    public string ym_l_token { get; set; }
}

public class YandexMusicMatchPayload
{
    public string track_id { get; set; }
    public string album_id { get; set; }
    public string album_title { get; set; }
    public int? bitrate_kbps { get; set; }
    public string codec { get; set; }
}

public class MusicProviderDescriptor
{
    public string id { get; set; }
    public string name { get; set; }
    public string type { get; set; }
    public bool enabled { get; set; }
    public bool requires_auth { get; set; }
    public List<string> capabilities { get; set; } = new();
}

public class MusicSection
{
    public string id { get; set; }
    public string title { get; set; }
    public string endpoint { get; set; }
    public string type { get; set; }
}

public class MusicBrowseSection
{
    public string id { get; set; }
    public string title { get; set; }
    public string type { get; set; }
    public string source_provider { get; set; }
    public bool has_more { get; set; }
    public string next_page { get; set; }
    public List<MusicAlbum> albums { get; set; } = new();
    public List<MusicArtist> artists { get; set; } = new();
    public List<MusicTrack> tracks { get; set; } = new();
}

public class MusicSearchResult
{
    public List<MusicArtist> artists { get; set; } = new();
    public List<MusicAlbum> albums { get; set; } = new();
    public List<MusicTrack> tracks { get; set; } = new();
}

public class MusicSearchResponse
{
    public string query { get; set; }
    public string status { get; set; }
    public string metadata_provider { get; set; }
    public bool metadata_pending { get; set; }
    public List<string> audio_providers { get; set; } = new();
    public List<MusicArtist> artists { get; set; } = new();
    public List<MusicAlbum> albums { get; set; } = new();
    public List<MusicTrack> tracks { get; set; } = new();
    public List<MusicBrowseSection> search_sections { get; set; } = new();
}

public class MusicRecentlyPlayedItem
{
    public MusicTrack track { get; set; }
    public DateTime played_at { get; set; }
}

public class MusicUserPlaylistSummary
{
    public string id { get; set; }
    public string title { get; set; }
    public int track_count { get; set; }
    public List<MusicImage> images { get; set; } = new();
    public MusicUserPlaylistSource source { get; set; }
}

public class MusicUserPlaylistSource
{
    public string type { get; set; }
    public string url { get; set; }
    public string user_id { get; set; }
    public string playlist_id { get; set; }
    public string title { get; set; }
}

public class MusicUserPlaylistImportResult
{
    public bool available { get; set; }
    public string message { get; set; }
    public string playlist_id { get; set; }
    public string title { get; set; }
    public int track_count { get; set; }
    public bool truncated { get; set; }
    public List<MusicTrack> tracks { get; set; } = new();
    public MusicUserPlaylistSource source { get; set; }
}

public class MusicHomeResponse
{
    public string title { get; set; }
    public string status { get; set; }
    public string version { get; set; }
    public List<MusicSection> sections { get; set; } = new();
    public List<MusicProviderDescriptor> metadata_providers { get; set; } = new();
    public List<MusicProviderDescriptor> audio_providers { get; set; } = new();
    public List<MusicProviderDescriptor> auth_providers { get; set; } = new();
    public List<MusicRecentlyPlayedItem> recently_played { get; set; } = new();
    public List<MusicUserPlaylistSummary> user_playlists { get; set; } = new();
    public List<MusicBrowseSection> browse_sections { get; set; } = new();
}

public class MusicPlayResponse
{
    public bool available { get; set; }
    public string message { get; set; }
    public string track_id { get; set; }
    public MusicAudioMatch selected_match { get; set; }
    public List<MusicPlaybackSource> sources { get; set; } = new();
}

public class MusicMatchesResponse
{
    public bool available { get; set; }
    public string message { get; set; }
    public string track_id { get; set; }
    public MusicAudioMatch selected_match { get; set; }
    public List<MusicAudioMatch> matches { get; set; } = new();
}

public class MusicRadioRequest
{
    public List<MusicTrack> seeds { get; set; } = new();
    public List<MusicTrack> exclude { get; set; } = new();
}

public class MusicRadioResponse
{
    public bool available { get; set; }
    public string message { get; set; }
    public List<MusicTrack> tracks { get; set; } = new();
}
