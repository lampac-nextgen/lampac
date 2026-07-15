using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Music;

// OAuth credentials: парсинг payload, нормализация и статус токена.
public static partial class SoundCloudSupport
{
    public static Dictionary<string, string> ParseSavePayload(string payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(payload))
            return result;

        foreach (var part in payload.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int sep = part.IndexOf('=');
            if (sep <= 0)
                continue;

            string key = WebUtility.UrlDecode(part.Substring(0, sep));
            string value = WebUtility.UrlDecode(part[(sep + 1)..]);

            if (!string.IsNullOrWhiteSpace(key))
                result[key] = value;
        }

        return result;
    }

    public static SoundCloudCredentials NormalizeCredentials(SoundCloudCredentials credentials)
    {
        if (credentials == null)
            return null;

        credentials.access_token = NormalizeValue(credentials.access_token);
        credentials.refresh_token = NormalizeValue(credentials.refresh_token);
        credentials.token_type = NormalizeValue(credentials.token_type);
        credentials.scope = NormalizeValue(credentials.scope);
        credentials.username = NormalizeValue(credentials.username);
        credentials.user_urn = NormalizeValue(credentials.user_urn);

        return credentials;
    }

    public static bool HasAccessToken(SoundCloudCredentials credentials)
        => !string.IsNullOrWhiteSpace(NormalizeCredentials(credentials)?.access_token);

    public static bool IsTokenExpired(SoundCloudCredentials credentials)
    {
        var normalized = NormalizeCredentials(credentials);
        if (normalized?.expires_at_unix == null || normalized.expires_at_unix <= 0)
            return false;

        return normalized.expires_at_unix <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public static string BuildStateMessage(SoundCloudCredentials credentials)
    {
        if (!HasOAuthConfig())
            return "SoundCloud OAuth is not configured.";

        if (!HasAccessToken(credentials))
            return "SoundCloud is not connected.";

        return IsTokenExpired(credentials)
            ? "SoundCloud token is saved, but expired."
            : "SoundCloud is connected.";
    }

    public static string BuildClientCredentialsPayload()
    {
        if (!HasClientCredentials())
            return string.Empty;

        var parts = new List<string>
        {
            "grant_type=client_credentials",
            $"client_id={Uri.EscapeDataString(ModInit.conf.soundcloud_client_id)}",
            $"client_secret={Uri.EscapeDataString(ModInit.conf.soundcloud_client_secret)}"
        };

        return string.Join("&", parts);
    }
}
