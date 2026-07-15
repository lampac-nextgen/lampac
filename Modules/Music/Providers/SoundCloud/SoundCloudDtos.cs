namespace Music;

public class SoundCloudCredentials
{
    public string access_token { get; set; }
    public string refresh_token { get; set; }
    public string token_type { get; set; }
    public string scope { get; set; }
    public string username { get; set; }
    public string user_urn { get; set; }
    public long? expires_at_unix { get; set; }
}

public class SoundCloudTokenResponse
{
    public string access_token { get; set; }
    public string refresh_token { get; set; }
    public string token_type { get; set; }
    public string scope { get; set; }
    public int? expires_in { get; set; }
}

public class SoundCloudCollectionDto<T>
{
    public List<T> collection { get; set; } = new();
    public string next_href { get; set; }
    public string query_urn { get; set; }
}

public class SoundCloudNestedCollectionDto<T>
{
    public List<T> collection { get; set; } = new();
}

public class SoundCloudChartSelectionDto
{
    public string urn { get; set; }
    public string title { get; set; }
    public SoundCloudNestedCollectionDto<SoundCloudPlaylistDto> items { get; set; }
}

public class SoundCloudUserDto
{
    public string urn { get; set; }
    public string username { get; set; }
    public string avatar_url { get; set; }
    public string permalink_url { get; set; }
}

public class SoundCloudTrackDto
{
    public string urn { get; set; }
    public string title { get; set; }
    public string artwork_url { get; set; }
    public string permalink_url { get; set; }
    public string stream_url { get; set; }
    public string access { get; set; }
    public int? duration { get; set; }
    public int? full_duration { get; set; }
    public string published_at { get; set; }
    public SoundCloudUserDto user { get; set; }
    public SoundCloudMediaDto media { get; set; }
}

public class SoundCloudPlaylistDto
{
    public string urn { get; set; }
    public string title { get; set; }
    public string artwork_url { get; set; }
    public string permalink_url { get; set; }
    public int? track_count { get; set; }
    public string published_at { get; set; }
    public string display_date { get; set; }
    public int? duration { get; set; }
    public string genre { get; set; }
    public SoundCloudUserDto user { get; set; }
    public List<SoundCloudTrackDto> tracks { get; set; } = new();
}

public class SoundCloudMediaDto
{
    public List<SoundCloudTranscodingDto> transcodings { get; set; } = new();
}

public class SoundCloudTranscodingDto
{
    public string url { get; set; }
    public string preset { get; set; }
    public bool? snipped { get; set; }
    public SoundCloudFormatDto format { get; set; }
}

public class SoundCloudFormatDto
{
    public string protocol { get; set; }
    public string mime_type { get; set; }
}

public class SoundCloudMatchPayload
{
    public string track_id { get; set; }
    public string urn { get; set; }
    public string permalink_url { get; set; }
    public string title { get; set; }
    public string artist_name { get; set; }
    public int? duration_ms { get; set; }
    public string track_authorization { get; set; }
}
