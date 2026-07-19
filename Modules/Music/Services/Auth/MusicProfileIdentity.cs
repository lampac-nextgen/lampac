using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using Shared.Services.Utilities;

namespace Music;

public static class MusicProfileIdentity
{
    public static string Resolve(RequestModel requestInfo, HttpRequest request)
    {
        string profileId = requestInfo?.user_uid;

        if (string.IsNullOrWhiteSpace(profileId) && request != null)
        {
            profileId = request.Query["uid"].FirstOrDefault() ?? request.Query["account_email"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(profileId) && request.HasFormContentType)
            {
                try
                {
                    profileId = request.Form["uid"].FirstOrDefault() ?? request.Form["account_email"].FirstOrDefault();
                }
                catch
                {
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(profileId))
            return profileId.Trim().ToLowerInvariant();

        string fingerprint = $"{requestInfo?.IP}|{requestInfo?.UserAgent}".Trim('|');
        if (!string.IsNullOrWhiteSpace(fingerprint))
            return $"anon:{CrypTo.md5(fingerprint)}";

        return "global";
    }
}
