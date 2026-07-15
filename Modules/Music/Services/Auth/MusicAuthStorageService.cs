using Microsoft.EntityFrameworkCore;

namespace Music;

public static class MusicAuthStorageService
{
    public static async Task<T> GetAsync<T>(string profileId, string providerId, CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return null;

        using var db = MusicContext.Create();
        string normalizedProfileId = NormalizeProfileId(profileId);

        var row = await db.auth_credentials
            .Where(i => i.provider_id == providerId && i.profile_id == normalizedProfileId)
            .OrderByDescending(i => i.updated)
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null && normalizedProfileId != string.Empty)
        {
            row = await db.auth_credentials
                .Where(i => i.provider_id == providerId && i.profile_id == string.Empty)
                .OrderByDescending(i => i.updated)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return row == null ? null : MusicJson.Deserialize<T>(row.payload);
    }

    public static async Task SaveAsync<T>(string profileId, string providerId, T payload, CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(providerId) || payload == null)
            return;

        using var db = MusicContext.Create();
        string normalizedProfileId = NormalizeProfileId(profileId);
        var existing = await db.auth_credentials
            .FirstOrDefaultAsync(i => i.profile_id == normalizedProfileId && i.provider_id == providerId, cancellationToken);

        if (existing == null)
        {
            db.auth_credentials.Add(new MusicAuthCredentialSqlModel
            {
                profile_id = normalizedProfileId,
                provider_id = providerId,
                payload = MusicJson.Serialize(payload),
                updated = DateTime.UtcNow
            });
        }
        else
        {
            existing.profile_id = normalizedProfileId;
            existing.payload = MusicJson.Serialize(payload);
            existing.updated = DateTime.UtcNow;
            db.auth_credentials.Update(existing);
        }

        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));

        try
        {
            if (await semaphore.WaitAsync())
                await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task DeleteAsync(string profileId, string providerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return;

        using var db = MusicContext.Create();
        string normalizedProfileId = NormalizeProfileId(profileId);
        var existing = await db.auth_credentials
            .Where(i => i.provider_id == providerId && (i.profile_id == normalizedProfileId || i.profile_id == string.Empty))
            .ToListAsync(cancellationToken);

        if (existing.Count == 0)
            return;

        db.auth_credentials.RemoveRange(existing);

        var semaphore = new SemaphorManager(MusicContext.semaphoreKey, TimeSpan.FromSeconds(20));

        try
        {
            if (await semaphore.WaitAsync())
                await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    static string NormalizeProfileId(string profileId)
        => string.IsNullOrWhiteSpace(profileId) ? "global" : profileId.Trim().ToLowerInvariant();
}
