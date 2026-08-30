using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace FlixCDN;

public class FlixCDNController : BaseOnlineController
{
    FlixCDNInvoke oninvk;

    public FlixCDNController() : base(ModInit.conf)
    {
        requestInitialization = () =>
        {
            oninvk = new FlixCDNInvoke
            (
               init,
               httpHydra,
               streamfile => HostStreamProxy(streamfile)
            );
        };
    }


    [HttpGet, Staticache(manually: true)]
    [Route("lite/flixcdn")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, short year, int t = -1, short s = -1, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (kinopoisk_id <= 0)
            return OnError("kinopoisk_id");

        var cache = await InvokeCacheResult<PlayerPayload>($"flixcdn:player:{kinopoisk_id}", TimeSpan.FromHours(1), async e =>
        {
            var player = await oninvk.GetPlayer(kinopoisk_id);
            if (player == null || player.id <= 0)
                return e.Fail("player", refresh_proxy: true);

            return e.Success(player);
        });

        return ContentTpl(cache, () =>
        {
            var result = cache.Value;
            var voices = GetVoices(result);

            if (!result.is_serial)
            {
                #region Фильм
                var mtpl = new MovieTpl(title, original_title, voices.Count);

                foreach (var voice in voices)
                {
                    mtpl.Append(
                        voice.title,
                        $"{host}/lite/flixcdn/stream?kinopoisk_id={kinopoisk_id}&id={result.id}&t={voice.id}",
                        "call",
                        vast: init.vast
                    );
                }

                return mtpl;
                #endregion
            }

            #region Сериал
            var seasons = GetSeasons(result);
            if (seasons.Count == 0)
                return default;

            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (s == -1)
            {
                var stpl = new SeasonTpl();

                foreach (var season in seasons)
                {
                    stpl.Append(
                        $"{season.Key} сезон",
                        $"{host}/lite/flixcdn?kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&year={year}&t={t}&s={season.Key}",
                        season.Key
                    );
                }

                return stpl;
            }

            if (!seasons.TryGetValue(s, out var seasonEpisodes) || seasonEpisodes == null || seasonEpisodes.Length == 0)
                return default;

            var seasonVoices = voices
                .Where(v => AvailableEpisodeCount(seasons, v, s) > 0)
                .ToList();

            if (seasonVoices.Count == 0)
                return default;

            if (t <= 0 || !seasonVoices.Any(v => v.id == t))
            {
                var nativeVoice = seasonVoices.FirstOrDefault(v => v.id == result.translate);
                t = nativeVoice?.id ?? seasonVoices[0].id;
            }

            var vtpl = new VoiceTpl();

            foreach (var voice in seasonVoices)
            {
                vtpl.Append(
                    voice.title,
                    t == voice.id,
                    $"{host}/lite/flixcdn?kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&year={year}&t={voice.id}&s={s}"
                );
            }

            var targetVoice = seasonVoices.FirstOrDefault(v => v.id == t);
            if (targetVoice == null)
                return default;

            int availableEpisodes = AvailableEpisodeCount(seasons, targetVoice, s);
            var etpl = new EpisodeTpl(vtpl);

            foreach (int episode in seasonEpisodes.Take(availableEpisodes))
            {
                if (episode <= 0 || episode > short.MaxValue)
                    continue;

                short episodeNumber = (short)episode;
                string link = $"{host}/lite/flixcdn/stream?kinopoisk_id={kinopoisk_id}&id={result.id}&t={t}&s={s}&e={episodeNumber}";

                etpl.Append(
                    $"Серия {episodeNumber}",
                    title ?? original_title,
                    s,
                    episodeNumber,
                    link,
                    "call",
                    streamlink: $"{link}&play=true",
                    vast: init.vast
                );
            }

            return etpl;
            #endregion
        });
    }


    [HttpGet, Staticache(manually: true)]
    [Route("lite/flixcdn/stream")]
    async public Task<ActionResult> Stream(long kinopoisk_id, int id, int t, short s = 0, short e = 0, bool play = false)
    {
        if (await IsRequestBlocked(rch_check: false))
            return badInitMsg;

        if (kinopoisk_id <= 0 || id <= 0 || t <= 0)
            return OnError();

        var cache = await InvokeCacheResult<string>(ipkey($"flixcdn:files:{kinopoisk_id}:{id}:{t}:{s}:{e}"), 10, async result =>
        {
            string file = await oninvk.GetPlayerFile(kinopoisk_id, id, t, s, e);
            if (string.IsNullOrWhiteSpace(file))
                return result.Fail("files", refresh_proxy: true);

            return result.Success(file);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var streamquality = oninvk.GetStreamQualityTpl(cache.Value);
        var first = streamquality.Firts();

        if (first == null)
            return OnError();

        if (play)
            return RedirectToPlay(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "auto",
            streamquality: streamquality,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }


    static List<PlayerTranslation> GetVoices(PlayerPayload player)
    {
        var voices = player?.translations?
            .Where(v => v != null && v.id > 0)
            .GroupBy(v => v.id)
            .Select(g => g.First())
            .ToList() ?? new List<PlayerTranslation>();

        if (player?.translate > 0 && !voices.Any(v => v.id == player.translate))
        {
            voices.Insert(0, new PlayerTranslation
            {
                id = player.translate,
                title = player.translateTitle ?? "Перевод",
                episodes_qty = TotalEpisodes(GetSeasons(player))
            });
        }

        return voices;
    }


    static SortedDictionary<short, int[]> GetSeasons(PlayerPayload player)
    {
        var seasons = new SortedDictionary<short, int[]>();

        if (player?.seasons_episodes != null)
        {
            foreach (var item in player.seasons_episodes)
            {
                if (!short.TryParse(item.Key, out short season) || season <= 0 || item.Value == null || item.Value.Length == 0)
                    continue;

                seasons[season] = item.Value.Where(e => e > 0).ToArray();
            }
        }

        if (seasons.Count == 0 && player?.season > 0 && player.episodes?.Length > 0)
            seasons[player.season] = player.episodes.Where(e => e > 0).ToArray();

        return seasons;
    }


    static int AvailableEpisodeCount(SortedDictionary<short, int[]> seasons, PlayerTranslation voice, short targetSeason)
    {
        if (voice == null || !seasons.TryGetValue(targetSeason, out var targetEpisodes))
            return 0;

        int totalAvailable = voice.episodes_qty > 0
            ? voice.episodes_qty
            : TotalEpisodes(seasons);

        foreach (var season in seasons)
        {
            if (season.Key == targetSeason)
                return Math.Min(Math.Max(totalAvailable, 0), targetEpisodes.Length);

            totalAvailable -= season.Value?.Length ?? 0;
        }

        return 0;
    }


    static int TotalEpisodes(SortedDictionary<short, int[]> seasons)
    {
        int total = 0;

        foreach (var season in seasons)
            total += season.Value?.Length ?? 0;

        return total;
    }
}
