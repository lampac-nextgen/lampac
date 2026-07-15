namespace Music;

public class SoundCloudAuthProvider : IMusicAuthProvider
{
    public string Id => SoundCloudSupport.AuthProviderId;
    public string Name => "SoundCloud";
    public bool Enabled => SoundCloudSupport.IsAuthEnabled;

    public async Task<MusicAuthState> GetStateAsync(string profileId = null, CancellationToken cancellationToken = default)
    {
        var credentials = await MusicAuthStorageService.GetAsync<SoundCloudCredentials>(profileId, Id, cancellationToken);
        credentials = SoundCloudSupport.NormalizeCredentials(credentials);

        bool hasToken = SoundCloudSupport.HasAccessToken(credentials);
        bool expired = SoundCloudSupport.IsTokenExpired(credentials);

        return new MusicAuthState
        {
            provider_id = Id,
            provider_name = Name,
            authenticated = hasToken && !expired,
            requires_auth = true,
            token_ready = hasToken,
            web_ready = hasToken,
            mode = "oauth",
            message = SoundCloudSupport.BuildStateMessage(credentials)
        };
    }

    public async Task<bool> SaveAsync(string payload, string profileId = null, CancellationToken cancellationToken = default)
    {
        var parsed = SoundCloudSupport.ParseSavePayload(payload);
        if (!parsed.TryGetValue("access_token", out var accessToken) || string.IsNullOrWhiteSpace(accessToken))
            return false;

        var credentials = await MusicAuthStorageService.GetAsync<SoundCloudCredentials>(profileId, Id, cancellationToken) ?? new SoundCloudCredentials();

        credentials.access_token = accessToken;
        credentials.refresh_token = parsed.TryGetValue("refresh_token", out var refreshToken) ? refreshToken : credentials.refresh_token;
        credentials.token_type = parsed.TryGetValue("token_type", out var tokenType) ? tokenType : credentials.token_type;
        credentials.scope = parsed.TryGetValue("scope", out var scope) ? scope : credentials.scope;
        credentials.username = parsed.TryGetValue("username", out var username) ? username : credentials.username;
        credentials.user_urn = parsed.TryGetValue("user_urn", out var userUrn) ? userUrn : credentials.user_urn;

        if (parsed.TryGetValue("expires_in", out var expiresInRaw) && long.TryParse(expiresInRaw, out var expiresInSeconds) && expiresInSeconds > 0)
            credentials.expires_at_unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresInSeconds;

        await MusicAuthStorageService.SaveAsync(profileId, Id, SoundCloudSupport.NormalizeCredentials(credentials), cancellationToken);
        return true;
    }

    public Task LogoutAsync(string profileId = null, CancellationToken cancellationToken = default)
    {
        return MusicAuthStorageService.DeleteAsync(profileId, Id, cancellationToken);
    }
}
