using Microsoft.AspNetCore.Mvc;

namespace Music;

public class PlaybackController : BaseController
{
    [HttpGet]
    [Route("music/matches")]
    async public Task<ActionResult> Matches(string id, string provider, string audio_provider, string playback_mode, string title, string artist_name, string album_title, int? duration_ms, string date, string query)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var track = await MusicPlaybackService.ResolveRequestTrackAsync(id, provider, title, artist_name, album_title, duration_ms, date);

        var result = string.IsNullOrWhiteSpace(query)
            ? await MusicResolver.GetMatchesAsync(track, audio_provider, playback_mode, profileId)
            : await MusicResolver.GetMatchesByQueryAsync(track, audio_provider, query, playback_mode);

        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpPost]
    [Route("music/match/select")]
    async public Task<ActionResult> SelectMatch(string id, string provider, string audio_provider, string playback_mode, string match_id, string title, string artist_name, string album_title, int? duration_ms, string date, string query)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var track = await MusicPlaybackService.ResolveRequestTrackAsync(id, provider, title, artist_name, album_title, duration_ms, date);
        bool saved = await MusicResolver.SelectMatchAsync(track, audio_provider, match_id, playback_mode, profileId, query);

        return ContentTo(MusicJson.Serialize(new
        {
            saved,
            track_id = id,
            audio_provider,
            match_id
        }));
    }

    [HttpPost]
    [Route("music/match/reset")]
    async public Task<ActionResult> ResetMatch(string id, string provider, string playback_mode, string title, string artist_name, string album_title, int? duration_ms, string date)
    {
        var track = await MusicPlaybackService.ResolveRequestTrackAsync(id, provider, title, artist_name, album_title, duration_ms, date);
        bool reset = track != null && await MusicSourceMatchService.DeleteAsync(track.id, playback_mode);

        return ContentTo(MusicJson.Serialize(new
        {
            reset,
            track_id = id
        }));
    }

    [HttpGet]
    [Route("music/play")]
    async public Task<ActionResult> Play(string id, string provider, string audio_provider, string stream_mode, string playback_mode, string title, string artist_name, string album_title, int? duration_ms, string date)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var track = await MusicPlaybackService.ResolveRequestTrackAsync(id, provider, title, artist_name, album_title, duration_ms, date);
        var result = await MusicPlaybackService.ResolveTrackAsync(track, audio_provider, stream_mode, playback_mode, profileId, HttpContext.RequestAborted);
        MusicPlaybackService.PrepareStreamSources(host, track, provider, audio_provider, stream_mode, playback_mode, result);
        return ContentTo(MusicJson.Serialize(result));
    }

    [HttpGet]
    [Route("music/stream")]
    async public Task<ActionResult> Stream(string ticket, string id, string provider, string audio_provider, string quality, string stream_mode, string playback_mode, string title, string artist_name, string album_title, int? duration_ms, string date)
    {
        if (MusicStreamTicketService.TryGet(ticket, out var ticketSource))
        {
            // тикет жив, но upstream-ссылка внутри могла умереть (403/404/410 —
            // googlevideo TTL/IP-привязка): не транслируем смерть клиенту, а
            // падаем в пере-резолв по fallback-параметрам ниже
            var relayed = await MusicStreamRelayService.RelayAsync(HttpContext, ticketSource, failOnUpstreamError: !string.IsNullOrWhiteSpace(id));
            if (relayed != null)
                return relayed;
        }

        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        var track = await MusicPlaybackService.ResolveRequestTrackAsync(id, provider, title, artist_name, album_title, duration_ms, date);
        var result = await MusicPlaybackService.ResolveTrackAsync(track, audio_provider, stream_mode, playback_mode, profileId, HttpContext.RequestAborted);

        if (track == null || result?.available != true || result.sources.Count == 0)
            return StatusCode(404);

        var source = MusicPlaybackService.SelectSource(result.sources, quality, stream_mode);
        if (source == null || string.IsNullOrWhiteSpace(source.url))
            return StatusCode(404);

        return await MusicStreamRelayService.RelayAsync(HttpContext, source);
    }

    [HttpPost]
    [Route("music/history/mark")]
    async public Task<ActionResult> MarkHistory(string id, string provider, string title, string artist_name, string album_title, int? duration_ms, string date, bool count_play = false)
    {
        var track = await MusicPlaybackService.ResolveRequestTrackAsync(id, provider, title, artist_name, album_title, duration_ms, date);
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);

        if (track != null)
        {
            await MusicPlaybackHistoryService.SaveAsync(profileId, track);

            // статистику инкрементим только по честному прослушиванию:
            // mark дёргается и для обновления payload (смена источника),
            // без флага это накручивало бы счётчики
            if (count_play)
                await MusicStatsService.IncrementPlayAsync(profileId, track);
        }

        return ContentTo(MusicJson.Serialize(new
        {
            saved = track != null,
            track_id = id
        }));
    }

    [HttpPost]
    [Route("music/history/remove")]
    async public Task<ActionResult> RemoveHistory(string id)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);
        bool removed = await MusicPlaybackHistoryService.RemoveAsync(profileId, id);

        return ContentTo(MusicJson.Serialize(new
        {
            removed,
            track_id = id
        }));
    }

    [HttpGet]
    [Route("music/playlist.m3u")]
    async public Task<ActionResult> Playlist(string ids, string provider, string audio_provider, string stream_mode, string playback_mode, string playlist_strategy)
    {
        string profileId = MusicProfileIdentity.Resolve(requestInfo, Request);

        string playlist = await MusicPlaylistService.BuildAsync(new MusicPlaylistRequest
        {
            ids = ids,
            provider = provider,
            audio_provider = audio_provider,
            stream_mode = stream_mode,
            playback_mode = playback_mode,
            playlist_strategy = playlist_strategy,
            profile_id = profileId,
            host = host,
            uid = Request.Query["uid"].FirstOrDefault(),
            account_email = Request.Query["account_email"].FirstOrDefault(),
            cancellation_token = HttpContext.RequestAborted
        });

        return Content(playlist, "audio/x-mpegurl; charset=utf-8");
    }
}
