using System.Text.Json;

namespace Music;

public class YandexMusicAudioProvider : IMusicAudioProvider
{
    public string Id => "yandexmusic";
    public string Name => "Yandex Music";
    public bool Enabled => true;
    public bool RequiresAuth => true;
    public bool CacheMissingMatches => false;

    public async Task<IReadOnlyList<MusicAudioMatch>> MatchTrackAsync(MusicTrack track, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (track == null)
            return Array.Empty<MusicAudioMatch>();

        var credentials = await MusicAuthStorageService.GetAsync<YandexMusicCredentials>(profileId, Id, cancellationToken);
        if (!YandexMusicSupport.HasApiToken(credentials))
            return Array.Empty<MusicAudioMatch>();

        string query = YandexMusicSupport.BuildTrackQuery(track);
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<MusicAudioMatch>();

        try
        {
            using var http = FriendlyHttp.CreateHttpClient(useCookies: false);
            YandexMusicSupport.ApplyHeaders(http, YandexMusicSupport.CreateApiHeaders(credentials));

            string url = $"https://api.music.yandex.net/search?text={HttpUtility.UrlEncode(query)}&type=track&page=0&page-size=12&nocorrect=false";

            using var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<MusicAudioMatch>();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("tracks", out var tracks) ||
                !tracks.TryGetProperty("results", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<MusicAudioMatch>();
            }

            var matches = new List<MusicAudioMatch>();

            foreach (var item in items.EnumerateArray())
            {
                string trackId = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                string title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;

                if (string.IsNullOrWhiteSpace(trackId) || string.IsNullOrWhiteSpace(title))
                    continue;

                var artists = new List<string>();
                if (item.TryGetProperty("artists", out var artistsProp) && artistsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var artist in artistsProp.EnumerateArray())
                    {
                        if (artist.TryGetProperty("name", out var nameProp))
                        {
                            var name = nameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(name))
                                artists.Add(name);
                        }
                    }
                }

                string albumTitle = null;
                string albumId = null;
                if (item.TryGetProperty("albums", out var albumsProp) && albumsProp.ValueKind == JsonValueKind.Array)
                {
                    var album = albumsProp.EnumerateArray().FirstOrDefault();
                    if (album.ValueKind != JsonValueKind.Undefined)
                    {
                        if (album.TryGetProperty("title", out var albumTitleProp))
                            albumTitle = albumTitleProp.GetString();

                        if (album.TryGetProperty("id", out var albumIdProp))
                            albumId = albumIdProp.ToString();
                    }
                }

                int? duration = null;
                if (item.TryGetProperty("durationMs", out var durationProp))
                {
                    if (durationProp.ValueKind == JsonValueKind.Number && durationProp.TryGetInt32(out var dur))
                        duration = dur;
                    else if (durationProp.ValueKind == JsonValueKind.String && int.TryParse(durationProp.GetString(), out dur))
                        duration = dur;
                }

                matches.Add(new MusicAudioMatch
                {
                    provider_id = Id,
                    id = trackId,
                    title = title,
                    artists = artists,
                    album_title = albumTitle,
                    duration_ms = duration,
                    payload = MusicJson.Serialize(new YandexMusicMatchPayload
                    {
                        track_id = trackId,
                        album_id = albumId,
                        album_title = albumTitle
                    })
                });
            }

            return YandexMusicSupport.RankMatches(track, matches).Take(5).ToList();
        }
        catch
        {
            return Array.Empty<MusicAudioMatch>();
        }
    }

    public async Task<IReadOnlyList<MusicPlaybackSource>> GetStreamsAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (match == null || string.IsNullOrWhiteSpace(match.id))
            return Array.Empty<MusicPlaybackSource>();

        var credentials = await MusicAuthStorageService.GetAsync<YandexMusicCredentials>(profileId, Id, cancellationToken);
        if (!YandexMusicSupport.HasApiToken(credentials))
            return Array.Empty<MusicPlaybackSource>();

        try
        {
            using var http = FriendlyHttp.CreateHttpClient(useCookies: false);
            YandexMusicSupport.ApplyHeaders(http, YandexMusicSupport.CreateApiHeaders(credentials));

            using var response = await http.GetAsync($"https://api.music.yandex.net/tracks/{match.id}/download-info", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<MusicPlaybackSource>();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var downloads = YandexMusicSupport.ParseDownloadInfos(json);
            var selected = downloads.FirstOrDefault();

            if (selected == null || string.IsNullOrWhiteSpace(selected.download_info_url))
                return Array.Empty<MusicPlaybackSource>();

            using var detailsResponse = await http.GetAsync(selected.download_info_url, cancellationToken);
            if (!detailsResponse.IsSuccessStatusCode)
                return Array.Empty<MusicPlaybackSource>();

            string raw = await detailsResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!YandexMusicSupport.TryParseDownloadPayload(raw, out var host, out var path, out var ts, out var s))
                return Array.Empty<MusicPlaybackSource>();

            string streamUrl = YandexMusicSupport.BuildStreamUrl(host, path, ts, s);

            return new List<MusicPlaybackSource>
            {
                new()
                {
                    provider_id = Id,
                    url = streamUrl,
                    mime_type = string.Equals(selected.codec, "aac", StringComparison.OrdinalIgnoreCase) ? "audio/aac" : "audio/mpeg",
                    bitrate = selected.bitrate_kbps,
                    quality = selected.bitrate_kbps.HasValue ? $"{(selected.codec ?? "audio").ToUpperInvariant()} {selected.bitrate_kbps}kbps" : (selected.codec ?? "audio").ToUpperInvariant(),
                    headers = new Dictionary<string, string>
                    {
                        ["Referer"] = "https://music.yandex.ru/"
                    }
                }
            };
        }
        catch
        {
            return Array.Empty<MusicPlaybackSource>();
        }
    }

    public Task<MusicPlaybackSource> TryGetPreferredStreamAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<MusicPlaybackSource>(null);
    }

    public Task<IReadOnlyList<MusicAudioMatch>> SearchMatchesByQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MusicAudioMatch>>(Array.Empty<MusicAudioMatch>());
    }

    public bool IsRelevantMatch(MusicTrack track, MusicAudioMatch match)
    {
        return track != null && match != null;
    }

    public bool ShouldValidatePinnedMatch(MusicTrack track, MusicAudioMatch match)
    {
        return false;
    }

    public IReadOnlyList<string> GetFallbackProviderIds(MusicTrack track)
    {
        return Array.Empty<string>();
    }
}

public class YandexMusicAuthProvider : IMusicAuthProvider
{
    public string Id => "yandexmusic";
    public string Name => "Yandex Music";
    public bool Enabled => true;

    public async Task<MusicAuthState> GetStateAsync(string profileId = null, CancellationToken cancellationToken = default)
    {
        var credentials = await MusicAuthStorageService.GetAsync<YandexMusicCredentials>(profileId, Id, cancellationToken);
        credentials = YandexMusicSupport.Normalize(credentials);

        bool hasToken = YandexMusicSupport.HasApiToken(credentials);
        bool hasWeb = YandexMusicSupport.HasWebAuth(credentials);

        return new MusicAuthState
        {
            provider_id = Id,
            provider_name = Name,
            authenticated = hasToken || hasWeb,
            requires_auth = true,
            token_ready = hasToken,
            web_ready = hasWeb,
            mode = "token+cookies",
            message = YandexMusicSupport.BuildStateMessage(hasToken, hasWeb)
        };
    }

    public async Task<bool> SaveAsync(string payload, string profileId = null, CancellationToken cancellationToken = default)
    {
        var parsed = YandexMusicSupport.ParseSavePayload(payload);
        if (parsed.Count == 0)
            return false;

        var credentials = await MusicAuthStorageService.GetAsync<YandexMusicCredentials>(profileId, Id, cancellationToken) ?? new YandexMusicCredentials();

        if (parsed.TryGetValue("ym_token", out var ymToken))
            credentials.ym_token = ymToken;
        if (parsed.TryGetValue("ym_session_id", out var ymSessionId))
            credentials.ym_session_id = ymSessionId;
        if (parsed.TryGetValue("ym_sessionid2", out var ymSessionId2))
            credentials.ym_sessionid2 = ymSessionId2;
        if (parsed.TryGetValue("ym_yandexuid", out var ymYandexUid))
            credentials.ym_yandexuid = ymYandexUid;
        if (parsed.TryGetValue("ym_yandex_login", out var ymYandexLogin))
            credentials.ym_yandex_login = ymYandexLogin;
        if (parsed.TryGetValue("ym_l_token", out var ymLToken))
            credentials.ym_l_token = ymLToken;

        credentials = YandexMusicSupport.Normalize(credentials);

        if (YandexMusicSupport.IsEmpty(credentials))
        {
            await MusicAuthStorageService.DeleteAsync(profileId, Id, cancellationToken);
            return true;
        }

        await MusicAuthStorageService.SaveAsync(profileId, Id, credentials, cancellationToken);
        return true;
    }

    public Task LogoutAsync(string profileId = null, CancellationToken cancellationToken = default)
        => MusicAuthStorageService.DeleteAsync(profileId, Id, cancellationToken);
}
