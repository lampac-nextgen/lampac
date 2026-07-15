using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using Shared.Services.Utilities;

namespace Music;

internal sealed class YandexDownloadInfo
{
    public string download_info_url { get; set; }
    public int? bitrate_kbps { get; set; }
    public string codec { get; set; }
}

internal static class YandexMusicSupport
{
    public const string ProviderId = "yandexmusic";
    public const string Salt = "XGRlBW9FXlekgbPrRHuSiA";

    static readonly Dictionary<string, string> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ym_token"] = "ym_token",
        ["token"] = "ym_token",
        ["oauth"] = "ym_token",

        ["ym_session_id"] = "ym_session_id",
        ["session_id"] = "ym_session_id",
        ["Session_id"] = "ym_session_id",

        ["ym_sessionid2"] = "ym_sessionid2",
        ["sessionid2"] = "ym_sessionid2",

        ["ym_yandexuid"] = "ym_yandexuid",
        ["yandexuid"] = "ym_yandexuid",

        ["ym_yandex_login"] = "ym_yandex_login",
        ["yandex_login"] = "ym_yandex_login",
        ["login"] = "ym_yandex_login",

        ["ym_l_token"] = "ym_l_token",
        ["l_token"] = "ym_l_token",
        ["L"] = "ym_l_token"
    };

    public static bool HasApiToken(YandexMusicCredentials credentials)
        => !string.IsNullOrWhiteSpace(credentials?.ym_token);

    public static bool HasWebAuth(YandexMusicCredentials credentials)
    {
        if (credentials == null)
            return false;

        bool hasSession = !string.IsNullOrWhiteSpace(credentials.ym_session_id) ||
                          !string.IsNullOrWhiteSpace(credentials.ym_sessionid2);

        return hasSession &&
               !string.IsNullOrWhiteSpace(credentials.ym_yandexuid) &&
               !string.IsNullOrWhiteSpace(credentials.ym_yandex_login) &&
               !string.IsNullOrWhiteSpace(credentials.ym_l_token);
    }

    public static YandexMusicCredentials Normalize(YandexMusicCredentials credentials)
    {
        credentials ??= new YandexMusicCredentials();

        credentials.ym_token = NormalizeValue(credentials.ym_token);
        credentials.ym_session_id = NormalizeValue(credentials.ym_session_id);
        credentials.ym_sessionid2 = NormalizeValue(credentials.ym_sessionid2);
        credentials.ym_yandexuid = NormalizeValue(credentials.ym_yandexuid);
        credentials.ym_yandex_login = NormalizeValue(credentials.ym_yandex_login);
        credentials.ym_l_token = NormalizeValue(credentials.ym_l_token);

        return credentials;
    }

    public static bool IsEmpty(YandexMusicCredentials credentials)
    {
        credentials = Normalize(credentials);

        return string.IsNullOrWhiteSpace(credentials.ym_token) &&
               string.IsNullOrWhiteSpace(credentials.ym_session_id) &&
               string.IsNullOrWhiteSpace(credentials.ym_sessionid2) &&
               string.IsNullOrWhiteSpace(credentials.ym_yandexuid) &&
               string.IsNullOrWhiteSpace(credentials.ym_yandex_login) &&
               string.IsNullOrWhiteSpace(credentials.ym_l_token);
    }

    public static Dictionary<string, string> CreateApiHeaders(YandexMusicCredentials credentials)
    {
        credentials = Normalize(credentials);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36",
            ["Referer"] = "https://music.yandex.ru/"
        };

        if (HasApiToken(credentials))
            headers["Authorization"] = $"OAuth {credentials.ym_token}";

        return headers;
    }

    public static Dictionary<string, string> CreateWebHeaders(YandexMusicCredentials credentials)
    {
        credentials = Normalize(credentials);
        if (!HasWebAuth(credentials))
            return null;

        var cookie = new List<string>();

        if (!string.IsNullOrWhiteSpace(credentials.ym_session_id))
            cookie.Add($"Session_id={credentials.ym_session_id}");

        if (!string.IsNullOrWhiteSpace(credentials.ym_sessionid2))
            cookie.Add($"sessionid2={credentials.ym_sessionid2}");

        cookie.Add($"yandexuid={credentials.ym_yandexuid}");
        cookie.Add($"yandex_login={credentials.ym_yandex_login}");
        cookie.Add($"L={credentials.ym_l_token}");

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "*/*",
            ["Accept-Language"] = "ru-RU,ru;q=0.9,en;q=0.8",
            ["Cache-Control"] = "no-cache",
            ["Pragma"] = "no-cache",
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36",
            ["Referer"] = "https://music.yandex.ru/",
            ["Cookie"] = string.Join("; ", cookie)
        };
    }

    public static void ApplyHeaders(HttpClient http, Dictionary<string, string> headers)
    {
        http.DefaultRequestHeaders.Clear();

        foreach (var header in headers)
            http.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
    }

    public static string BuildTrackQuery(MusicTrack track)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(track?.artist_name))
            parts.Add(track.artist_name);

        if (!string.IsNullOrWhiteSpace(track?.title))
            parts.Add(track.title);

        if (!string.IsNullOrWhiteSpace(track?.album_title))
            parts.Add(track.album_title);

        return string.Join(" ", parts.Where(i => !string.IsNullOrWhiteSpace(i))).Trim();
    }

    public static List<MusicAudioMatch> RankMatches(MusicTrack query, IEnumerable<MusicAudioMatch> matches)
    {
        string queryTitle = NormalizeText(query?.title);
        string queryArtist = NormalizeText(query?.artist_name);
        string queryAlbum = NormalizeText(query?.album_title);

        return matches
            .Select(match =>
            {
                int score = 0;
                string title = NormalizeText(match.title);
                string album = NormalizeText(match.album_title);
                var artists = match.artists.Select(NormalizeText).ToList();

                if (title == queryTitle)
                    score += 6;
                else if (!string.IsNullOrWhiteSpace(queryTitle) && title.Contains(queryTitle))
                    score += 4;

                if (!string.IsNullOrWhiteSpace(queryArtist) && artists.Any(a => a == queryArtist))
                    score += 4;
                else if (!string.IsNullOrWhiteSpace(queryArtist) && artists.Any(a => a.Contains(queryArtist) || queryArtist.Contains(a)))
                    score += 2;

                if (!string.IsNullOrWhiteSpace(queryAlbum) && album == queryAlbum)
                    score += 2;

                if (query.duration_ms.HasValue && match.duration_ms.HasValue)
                {
                    int diff = Math.Abs(query.duration_ms.Value - match.duration_ms.Value);
                    if (diff <= 3000)
                        score += 3;
                    else if (diff <= 10000)
                        score += 1;
                }

                return new { match, score };
            })
            .OrderByDescending(i => i.score)
            .ThenBy(i => i.match.title)
            .Select(i => i.match)
            .ToList();
    }

    public static IReadOnlyList<YandexDownloadInfo> ParseDownloadInfos(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<YandexDownloadInfo>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return Array.Empty<YandexDownloadInfo>();

        var infos = new List<YandexDownloadInfo>();

        foreach (var item in result.EnumerateArray())
        {
            string url = TryGetString(item, "downloadInfoUrl");
            if (string.IsNullOrWhiteSpace(url))
                continue;

            infos.Add(new YandexDownloadInfo
            {
                download_info_url = url,
                bitrate_kbps = TryGetInt(item, "bitrateInKbps"),
                codec = TryGetString(item, "codec")
            });
        }

        return infos
            .OrderByDescending(i => string.Equals(i.codec, "mp3", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(i => i.bitrate_kbps ?? 0)
            .ToList();
    }

    public static string BuildStreamUrl(string host, string path, string ts, string s)
    {
        string pathForSign = path?.TrimStart('/') ?? string.Empty;
        string sign = CrypTo.md5(Salt + pathForSign + s);
        return $"https://{host}/get-mp3/{sign}/{ts}{path}";
    }

    public static bool TryParseDownloadPayload(string raw, out string host, out string path, out string ts, out string s)
    {
        host = null;
        path = null;
        ts = null;
        s = null;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();

        try
        {
            if (raw.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(raw);
                host = TryGetString(doc.RootElement, "host");
                path = TryGetString(doc.RootElement, "path");
                ts = TryGetString(doc.RootElement, "ts");
                s = TryGetString(doc.RootElement, "s");
            }
            else
            {
                var xml = XDocument.Parse(raw);
                host = xml.Root?.Element("host")?.Value;
                path = xml.Root?.Element("path")?.Value;
                ts = xml.Root?.Element("ts")?.Value;
                s = xml.Root?.Element("s")?.Value;
            }
        }
        catch
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(host) &&
               !string.IsNullOrWhiteSpace(path) &&
               !string.IsNullOrWhiteSpace(ts) &&
               !string.IsNullOrWhiteSpace(s);
    }

    public static Dictionary<string, string> ParseSavePayload(string payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        payload = payload.Trim();

        try
        {
            if (payload.StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(payload);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (!KeyMap.TryGetValue(property.Name, out var key))
                        continue;

                    result[key] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.ToString();
                }

                return result;
            }
        }
        catch
        {
        }

        var form = HttpUtility.ParseQueryString(payload
            .Replace("\r", "&")
            .Replace("\n", "&")
            .Replace(";", "&"));
        foreach (string rawKey in form.AllKeys)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || !KeyMap.TryGetValue(rawKey, out var key))
                continue;

            result[key] = form[rawKey];
        }

        return result;
    }

    public static string BuildStateMessage(bool hasToken, bool hasWeb)
    {
        if (hasToken && hasWeb)
            return "Token и web cookies сохранены.";

        if (hasToken)
            return "Token сохранён. Web cookies для подборок и web-разделов ещё не заданы.";

        if (hasWeb)
            return "Web cookies сохранены. OAuth token для API и playback ещё не задан.";

        return "Token and cookies are required for full functionality.";
    }

    static string NormalizeValue(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            .ToArray());
    }

    static string TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    static int? TryGetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number))
            return number;

        return null;
    }
}
