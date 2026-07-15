using Microsoft.AspNetCore.Mvc;

namespace Music;

public class AuthController : BaseController
{
    [HttpGet]
    [Route("music/auth/state")]
    async public Task<ActionResult> State()
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var result = await MusicAuthService.GetStatesAsync(profileId);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpPost]
    [Route("music/auth/save")]
    async public Task<ActionResult> Save(string provider, string payload)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        bool result = await MusicAuthService.SaveAsync(provider, payload, profileId);
        return ContentTo(MusicJson.Serialize(new
        {
            provider,
            saved = result
        }));
    }

    [HttpPost]
    [Route("music/auth/logout")]
    async public Task<ActionResult> Logout(string provider)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        await MusicAuthService.LogoutAsync(provider, profileId);
        return ContentTo(MusicJson.Serialize(new
        {
            provider,
            logged_out = true
        }));
    }
}
