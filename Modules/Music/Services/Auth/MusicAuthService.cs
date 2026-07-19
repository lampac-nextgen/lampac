namespace Music;

public static class MusicAuthService
{
    public static async Task<List<MusicAuthState>> GetStatesAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var result = new List<MusicAuthState>();

        foreach (var provider in MusicProviderRegistry.AuthProviders.Where(i => i.Enabled))
            result.Add(await provider.GetStateAsync(profileId, cancellationToken));

        return result;
    }

    public static Task<bool> SaveAsync(string provider, string payload, string profileId, CancellationToken cancellationToken = default)
    {
        var auth = MusicProviderRegistry.GetAuthProvider(provider);
        return auth == null ? Task.FromResult(false) : auth.SaveAsync(payload, profileId, cancellationToken);
    }

    public static Task LogoutAsync(string provider, string profileId, CancellationToken cancellationToken = default)
    {
        var auth = MusicProviderRegistry.GetAuthProvider(provider);
        return auth == null ? Task.CompletedTask : auth.LogoutAsync(profileId, cancellationToken);
    }
}
