using Newtonsoft.Json;
using Shared.Models.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Gencit;

public class GencitService
{
    public const string Referer = "https://web.kino.bz/";

    private readonly ModuleConf init;
    private readonly HttpHydra httpHydra;
    private readonly IReadOnlyList<HeadersModel> pageHeaders;
    private readonly IReadOnlyList<HeadersModel> apiHeaders;

    public string LastError { get; private set; }

    public GencitService(ModuleConf init, HttpHydra httpHydra)
    {
        this.init = init;
        this.httpHydra = httpHydra;
        pageHeaders = HeadersModel.Init(
            ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("referer", Referer)
        );
        apiHeaders = HeadersModel.Init(
            ("accept", "application/json,text/plain,*/*")
        );
    }

    public async Task<GencitApiData> ResolvePlaylist(long kinopoiskId, string imdbId)
    {
        LastError = null;
        string kpError = null;

        if (kinopoiskId > 0)
        {
            var data = await GetApiData(kinopoiskId.ToString()).ConfigureAwait(false);
            if (data?.playlist_id > 0 && (data.kinopoisk_id <= 0 || data.kinopoisk_id == kinopoiskId))
                return data;

            kpError = LastError ?? "api:kp:not_found";
        }

        string imdb = NormalizeImdb(imdbId);
        if (!string.IsNullOrWhiteSpace(imdb))
        {
            var data = await GetApiData(imdb).ConfigureAwait(false);
            if (data?.playlist_id > 0)
            {
                string responseImdb = NormalizeImdb(data.imdb_id);
                if (string.IsNullOrWhiteSpace(responseImdb) || responseImdb == imdb)
                    return data;
            }

            string imdbError = LastError ?? "api:imdb:not_found";
            LastError = string.IsNullOrWhiteSpace(kpError)
                ? imdbError
                : $"{kpError}; {imdbError}";
            return null;
        }

        LastError = kpError ?? "external_id";
        return null;
    }

    private async Task<GencitApiData> GetApiData(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            LastError = "api:id";
            return null;
        }

        string apiHost = init.api_host?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(apiHost))
        {
            LastError = "api:host";
            return null;
        }

        string uri = $"{apiHost}/api/{Uri.EscapeDataString(externalId)}";
        string json;

        try
        {
            json = await httpHydra.Get(uri, addheaders: apiHeaders).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = $"api:http:{ex.GetType().Name}";
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            LastError = "api:empty";
            return null;
        }

        try
        {
            var data = JsonConvert.DeserializeObject<GencitApiData>(json);
            if (data == null)
            {
                LastError = "api:null";
                return null;
            }

            if (data.playlist_id <= 0)
            {
                LastError = "api:playlist";
                return null;
            }

            LastError = null;
            return data;
        }
        catch (Exception ex)
        {
            LastError = $"api:json:{ex.GetType().Name}";
            return null;
        }
    }

    private static string NormalizeImdb(string imdbId)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
            return null;

        string value = imdbId.Trim().ToLowerInvariant();
        if (value.StartsWith("tt", StringComparison.Ordinal))
            return value;

        return long.TryParse(value, out _) ? $"tt{value}" : value;
    }

    public async Task<GencitPageData> GetPage(int playlistId, short season = 0, short episode = 0, int voiceId = 0)
    {
        LastError = null;

        if (playlistId <= 0)
        {
            LastError = "playlist";
            return null;
        }

        string uri = BuildPageUrl(playlistId, season, episode, voiceId);
        string html;

        try
        {
            html = await httpHydra.Get(uri, addheaders: pageHeaders).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = $"http:{ex.GetType().Name}";
            return null;
        }

        if (string.IsNullOrWhiteSpace(html))
        {
            LastError = "html:empty";
            return null;
        }

        if (IsBlockedPage(html))
        {
            LastError = $"html:blocked:{html.Length}";
            return null;
        }

        string playerJson = ExtractAssignedJson(html, "window.playerData");
        if (string.IsNullOrWhiteSpace(playerJson))
        {
            LastError = $"playerData:marker:{html.Length}";
            return null;
        }

        GencitPlayerData player;
        try
        {
            player = JsonConvert.DeserializeObject<GencitPlayerData>(playerJson);
        }
        catch (Exception ex)
        {
            LastError = $"playerData:json:{ex.GetType().Name}";
            return null;
        }

        if (player == null)
        {
            LastError = "playerData:null";
            return null;
        }

        if (player.config == null)
        {
            LastError = "playerData:config";
            return null;
        }

        GencitAdsConfig ads = null;
        string adsJson = ExtractAssignedJson(html, "window.adsConfig");
        if (!string.IsNullOrWhiteSpace(adsJson))
        {
            try { ads = JsonConvert.DeserializeObject<GencitAdsConfig>(adsJson); }
            catch { }
        }

        return new GencitPageData
        {
            player = player,
            ads = ads
        };
    }

    public async Task<GencitVideoData> GetVideo(int playlistId, int videoId)
    {
        LastError = null;

        if (videoId <= 0)
        {
            LastError = "video_id";
            return null;
        }

        string host = init.host.TrimEnd('/');
        string uri = $"{host}/videos.php?id={videoId}";
        var headers = HeadersModel.Init(
            ("accept", "application/json,text/plain,*/*"),
            ("referer", playlistId > 0 ? $"{host}/lat/{playlistId}" : $"{host}/")
        );

        string json;
        try
        {
            json = await httpHydra.Get(uri, addheaders: headers).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LastError = $"video:http:{ex.GetType().Name}";
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            LastError = "video:empty";
            return null;
        }

        try
        {
            var video = JsonConvert.DeserializeObject<GencitVideoData>(json);
            if (video == null)
            {
                LastError = "video:null";
                return null;
            }

            return video;
        }
        catch (Exception ex)
        {
            LastError = $"video:json:{ex.GetType().Name}";
            return null;
        }
    }

    public string GetHls(GencitPageData page)
        => NormalizeHls(page?.player?.config?.h, page?.player?.config?.video, page?.player?.config?.video_new);

    public string GetHls(GencitVideoData video)
        => NormalizeHls(video?.h, video?.video, video?.video_new);

    private static string NormalizeHls(string encoded, string hls, string fallback)
    {
        // The player decodes h before playback; video can contain an invalid signature.
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            try
            {
                string base64 = encoded.Replace('-', '+').Replace('_', '/');
                byte[] data = Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
                const string key = "xu0vKTL4wJBuU2j7";
                for (int i = 0; i < data.Length; i++)
                    data[i] ^= (byte)key[i % key.Length];

                hls = Encoding.UTF8.GetString(data);
                return Uri.TryCreate(hls, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")
                    ? hls
                    : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(hls))
            hls = fallback;

        if (string.IsNullOrWhiteSpace(hls))
            return null;

        if (hls.StartsWith("//", StringComparison.Ordinal))
            hls = "https:" + hls;

        return hls;
    }

    private string BuildPageUrl(int playlistId, short season, short episode, int voiceId)
    {
        string uri = $"{init.host.TrimEnd('/')}/lat/{playlistId}";
        if (season > 0 && episode != 0 && voiceId > 0)
        {
            uri += $"?season={season}&episode={episode}&voice={voiceId}";
        }

        return uri;
    }

    private static bool IsBlockedPage(string html)
        => !string.IsNullOrEmpty(html)
            && html.Length < 2048
            && html.Contains("404 Not Found", StringComparison.OrdinalIgnoreCase)
            && html.Contains("isFramed", StringComparison.OrdinalIgnoreCase);

    public static string ExtractAssignedJson(string html, string marker)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(marker))
            return null;

        int searchFrom = 0;
        while (searchFrom < html.Length)
        {
            int markerIndex = html.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (markerIndex < 0)
                return null;

            int cursor = markerIndex + marker.Length;
            while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
                cursor++;

            if (cursor >= html.Length || html[cursor] != '=')
            {
                searchFrom = markerIndex + marker.Length;
                continue;
            }

            cursor++;
            while (cursor < html.Length && char.IsWhiteSpace(html[cursor]))
                cursor++;

            int start = html.IndexOf('{', cursor);
            if (start < 0)
                return null;

            int depth = 0;
            char quote = '\0';
            bool escaped = false;

            for (int i = start; i < html.Length; i++)
            {
                char ch = html[i];

                if (quote != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (ch == quote)
                        quote = '\0';

                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                    continue;
                }

                if (ch != '}')
                    continue;

                depth--;
                if (depth == 0)
                    return html.Substring(start, i - start + 1);
            }

            return null;
        }

        return null;
    }
}
