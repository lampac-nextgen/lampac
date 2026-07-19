using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Music;

public class ApiController : BaseController
{
    [HttpGet]
    [AllowAnonymous]
    [Route("music.js")]
    [Route("music/js/{token}")]
    public ActionResult MusicJS(string token)
    {
        SetHeadersNoCache();

        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "music.js", false)
            .Replace("{localhost}", host)
            .Replace("{client_debug_enabled}", ModInit.conf?.client_debug_enabled == true ? "true" : "false");

        return Content(plugin, "application/javascript; charset=utf-8");
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("music/clientlog")]
    public ActionResult ClientLog(string event_name, string state, string track_id, string message)
    {
        static string cut(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }

        if (ModInit.conf?.client_debug_enabled != true)
            return ContentTo(MusicJson.Serialize(new { ok = true, logged = false }));

        Console.WriteLine($"[MusicClient] event={cut(event_name, 64)} track={cut(track_id, 96)} state={cut(state, 6000)} message={cut(message, 400)}");
        return ContentTo(MusicJson.Serialize(new { ok = true, logged = true }));
    }

    [HttpGet]
    [Route("music")]
    [Route("music/home")]
    async public Task<ActionResult> Home()
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var result = await MusicCatalogService.GetHomeAsync(profileId);
        MusicImageProxyService.Apply(this, result);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/playlists")]
    async public Task<ActionResult> Playlists()
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var result = await MusicUserPlaylistService.ListAsync(profileId, HttpContext.RequestAborted);
        foreach (var playlist in result)
            MusicImageProxyService.Apply(this, playlist);

        return ContentTo(MusicJson.Serialize(new { available = true, playlists = result }));
    }

    [HttpGet]
    [Route("music/playlists/tracks")]
    async public Task<ActionResult> PlaylistTracks(string id)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var tracks = await MusicUserPlaylistService.GetTracksAsync(profileId, id, HttpContext.RequestAborted);
        foreach (var track in tracks)
            MusicImageProxyService.Apply(this, track);

        return ContentTo(MusicJson.Serialize(new { available = true, playlist_id = id, tracks }));
    }

    [HttpPost]
    [Route("music/playlists/create")]
    async public Task<ActionResult> PlaylistCreate(string title)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        string playlistId = await MusicUserPlaylistService.CreateAsync(profileId, title, HttpContext.RequestAborted);
        return ContentTo(MusicJson.Serialize(new { created = playlistId != null, playlist_id = playlistId, title }));
    }

    [HttpPost]
    [Route("music/playlists/delete")]
    async public Task<ActionResult> PlaylistDelete(string id)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        bool removed = await MusicUserPlaylistService.DeleteAsync(profileId, id, HttpContext.RequestAborted);
        return ContentTo(MusicJson.Serialize(new { removed, playlist_id = id }));
    }

    [HttpPost]
    [Route("music/playlists/import")]
    [Route("music/playlists/import/soundcloud")] // legacy-роут для старого клиентского JS
    async public Task<ActionResult> PlaylistImport(string url)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var result = await MusicUserPlaylistService.ImportAsync(profileId, url, HttpContext.RequestAborted);
        foreach (var track in result?.tracks ?? new List<MusicTrack>())
            MusicImageProxyService.Apply(this, track);

        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpPost]
    [Route("music/playlists/sync")]
    async public Task<ActionResult> PlaylistSync(string id)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var result = await MusicUserPlaylistService.SyncAsync(profileId, id, HttpContext.RequestAborted);
        foreach (var track in result?.tracks ?? new List<MusicTrack>())
            MusicImageProxyService.Apply(this, track);

        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpPost]
    [Route("music/playlists/track/add")]
    async public Task<ActionResult> PlaylistTrackAdd(string id, string track)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);

        MusicTrack parsed = null;
        try
        {
            parsed = string.IsNullOrWhiteSpace(track) ? null : MusicJson.Deserialize<MusicTrack>(track);
        }
        catch
        {
        }

        bool saved = await MusicUserPlaylistService.AddTrackAsync(profileId, id, parsed, HttpContext.RequestAborted);
        var playlist = saved ? await MusicUserPlaylistService.GetSummaryAsync(profileId, id, HttpContext.RequestAborted) : null;
        if (playlist != null)
            MusicImageProxyService.Apply(this, playlist);

        return ContentTo(MusicJson.Serialize(new { saved, playlist_id = id, playlist }));
    }

    [HttpPost]
    [Route("music/playlists/track/remove")]
    async public Task<ActionResult> PlaylistTrackRemove(string id, string track_id)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        bool removed = await MusicUserPlaylistService.RemoveTrackAsync(profileId, id, track_id, HttpContext.RequestAborted);
        var playlist = removed ? await MusicUserPlaylistService.GetSummaryAsync(profileId, id, HttpContext.RequestAborted) : null;
        if (playlist != null)
            MusicImageProxyService.Apply(this, playlist);

        return ContentTo(MusicJson.Serialize(new { removed, playlist_id = id, track_id, playlist }));
    }

    [HttpPost]
    [Route("music/playlists/track/move")]
    async public Task<ActionResult> PlaylistTrackMove(string id, string track_id, int position)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        bool moved = await MusicUserPlaylistService.MoveTrackAsync(profileId, id, track_id, position, HttpContext.RequestAborted);
        var playlist = moved ? await MusicUserPlaylistService.GetSummaryAsync(profileId, id, HttpContext.RequestAborted) : null;
        if (playlist != null)
            MusicImageProxyService.Apply(this, playlist);

        return ContentTo(MusicJson.Serialize(new { moved, playlist_id = id, track_id, position, playlist }));
    }

    [HttpGet]
    [Route("music/stats/top")]
    async public Task<ActionResult> StatsTop(int? limit)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var result = await MusicStatsService.GetTopAsync(profileId, limit ?? 30, HttpContext.RequestAborted);
        MusicImageProxyService.Apply(this, result);

        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpPost]
    [Route("music/radio")]
    async public Task<ActionResult> Radio(string seeds, string exclude, int? limit)
    {
        static List<MusicTrack> parseTracks(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new List<MusicTrack>();

            try
            {
                return MusicJson.Deserialize<List<MusicTrack>>(value) ?? new List<MusicTrack>();
            }
            catch
            {
                return new List<MusicTrack>();
            }
        }

        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var result = await MusicRadioService.GetAsync(profileId, new MusicRadioRequest
        {
            seeds = parseTracks(seeds),
            exclude = parseTracks(exclude)
        }, limit ?? 20, HttpContext.RequestAborted);

        foreach (var track in result?.tracks ?? new List<MusicTrack>())
            MusicImageProxyService.Apply(this, track);

        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/lyrics")]
    async public Task<ActionResult> Lyrics(string title, string artist_name, string album_title, int? duration_ms, string youtube_id)
    {
        var result = await MusicLyricsService.GetAsync(title, artist_name, album_title, duration_ms, youtube_id, HttpContext.RequestAborted);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/providers")]
    public ActionResult Providers()
    {
        var result = new
        {
            metadata = MusicProviderRegistry.DescribeMetadata(),
            audio = MusicProviderRegistry.DescribeAudio(),
            auth = MusicProviderRegistry.DescribeAuth()
        };

        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/section")]
    async public Task<ActionResult> Section(string id)
    {
        var result = await MusicCatalogService.GetBrowseSectionAsync(id);
        if (result == null)
            return ContentTo(MusicJson.Serialize(new { available = false, message = "Section not found." }));

        MusicImageProxyService.Apply(this, result);
        return ContentTo(MusicJson.Serialize(result));
    }
}
