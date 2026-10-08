using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace KubikVKube;

public class KubikVKubeController : BaseOnlineController<ModuleConf>
{
    /// <summary>Зеркала /lat/, встреченные на страницах сайта (только к ним разрешены запросы из /video)</summary>
    static readonly ConcurrentDictionary<string, byte> knownLatHosts = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

    KubikService service;

    public KubikVKubeController() : base(ModInit.conf)
    {
        requestInitialization = () =>
        {
            service = new KubikService(init, httpHydra);
        };
    }

    #region Index
    [HttpGet, Staticache(manually: true)]
    [Route("lite/kubikvkube")]
    async public Task<ActionResult> Index(string title, string original_title, int year, long kinopoisk_id, int serial = -1, string href = null,
                                          int s = -1, string t = null, bool rjson = false, bool similar = false, bool checksearch = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        href = KubikService.RelHref(href);

        #region поиск страницы на сайте
        if (href == null)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(original_title))
                return OnError("title");

            string skey = $"kubikvkube:v2:resolve:{KubikService.Norm(title)}:{KubikService.Norm(original_title)}:{year}:{serial}:{kinopoisk_id}";

            var resolve = await InvokeCacheResult<ResolveResult>(skey, TimeSpan.FromHours(6), async e =>
            {
                var r = await service.Resolve(title, original_title, year, serial, kinopoisk_id).ConfigureAwait(false);

                if (r.page == null && r.found.Count == 0)
                    return e.Fail("not_found");

                return e.Success(new ResolveResult { href = r.page?.href, found = r.found });
            });

            if (!resolve.IsSuccess)
                return OnError(resolve.ErrorMsg);

            if (similar || string.IsNullOrEmpty(resolve.Value.href))
            {
                if (checksearch)
                    return OnError("not_found");

                var stpl = new SimilarTpl(resolve.Value.found.Count);
                foreach (var item in resolve.Value.found)
                {
                    string img = string.IsNullOrEmpty(item.img) ? null : (item.img.StartsWith("http") ? item.img : $"{KubikService.SiteOrigin}{item.img}");
                    stpl.Append(item.title, item.year > 0 ? item.year.ToString() : string.Empty, item.serial ? "Сериал" : "Фильм",
                        IndexLink(item.title, original_title, item.year, kinopoisk_id, item.serial ? 1 : 0, item.href, -1, null, rjson), img);
                }

                return ContentTpl(stpl);
            }

            href = resolve.Value.href;
        }
        #endregion

        #region каталог обоих плееров
        var catalog = await InvokeCacheResult<KubikCatalog>($"kubikvkube:v2:catalog:{href}", TimeSpan.FromMinutes(30), async e =>
        {
            var page = await service.GetPage(href).ConfigureAwait(false);
            if (page == null)
                return e.Fail("page", refresh_proxy: true);

            if (!string.IsNullOrEmpty(page.lat_host))
                knownLatHosts.TryAdd(page.lat_host, 0);

            var vhTask = init.usevh && page.kinopoisk_id > 0
                ? service.GetVhPlaylist(page.kinopoisk_id, page.publisher)
                : Task.FromResult<VhPlaylist>(null);

            var latTask = init.uselat && page.lat_id > 0
                ? service.GetLatPlayer(page.lat_host, page.lat_id)
                : Task.FromResult<(LatPlayerData, string, string)>((null, null, page.lat_id > 0 ? "lat:выключен" : "lat:нет на странице"));

            await Task.WhenAll(vhTask, latTask).ConfigureAwait(false);

            var vh = vhTask.Result;
            var lat = latTask.Result;

            var result = new KubikCatalog
            {
                page = page,
                lat_host = lat.Item2 ?? page.lat_host,
                serial = page.serial || vh?.isSerial == true || lat.Item1?.playlist?.serial?.list?.Count > 0,
                tracks = KubikService.BuildTracks(page, vh, lat.Item1, init.onlykubik),
                vh_error = page.kinopoisk_id <= 0 ? "vh:нет на странице" : vh == null ? "vh:плейлист недоступен" : $"vh:[{KubikService.VhVoicesSummary(vh)}]",
                lat_error = lat.Item3
            };

            if (result.tracks.Count == 0)
                return e.Fail($"Нет доступных озвучек ({result.vh_error}; {result.lat_error ?? "lat:ok"})", refresh_proxy: true);

            return e.Success(result);
        });

        if (!catalog.IsSuccess)
            return OnError(catalog.ErrorMsg);
        #endregion

        var cat = catalog.Value;
        string vtitle = title ?? cat.page.title ?? original_title;

        if (cat.serial)
            return ContentTpl(SerialTpl(cat, vtitle, original_title, year, kinopoisk_id, href, s, t, rjson));

        return ContentTpl(MovieTpl(cat, vtitle, original_title));
    }
    #endregion

    #region Video
    /// <summary>
    /// Поток серии/фильма. p — порядок плееров (vh,vk,lat): если первый не отдал поток,
    /// берётся следующий, пользователь ничего не нажимает.
    /// </summary>
    [HttpGet, Staticache(manually: true)]
    [Route("lite/kubikvkube/video")]
    async public Task<ActionResult> Video(string p = null, string vk = null, string vkv = null, string lh = null, int lp = 0, int lv = 0, int lt = 0,
                                          int s = 0, int e = 0, string title = null, bool play = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (!string.IsNullOrEmpty(vk) && !Regex.IsMatch(vk, "^[0-9]{1,24}$"))
            vk = null;

        if (!string.IsNullOrEmpty(vkv) && !Regex.IsMatch(vkv, "^-?[0-9]{1,20}_[0-9]{1,20}(_[0-9a-f]{1,40})?$"))
            vkv = null;

        lh = AllowedLatHost(lh);

        var order = (p ?? "vh,vk,lat").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x == "vh" || x == "vk" || x == "lat").Distinct().ToList();

        string url = null;
        StreamQualityTpl quality = null;
        IReadOnlyList<HeadersModel> headers = null;
        var errors = new List<string>();

        foreach (string player in order)
        {
            if (url != null)
                break;

            if (player == "vh" || player == "vk")
            {
                string id = player == "vh" ? vk : vkv;
                if (string.IsNullOrEmpty(id))
                    continue;

                var res = await InvokeCacheResult<VhStream>($"kubikvkube:v2:{player}:{id}", TimeSpan.FromMinutes(20), async c =>
                {
                    var stream = player == "vh"
                        ? await service.GetVhStream(id).ConfigureAwait(false)
                        : await service.GetVkStream(id).ConfigureAwait(false);

                    return stream == null ? c.Fail(service.LastError ?? $"{player}:video") : c.Success(stream);
                });

                if (!res.IsSuccess)
                {
                    errors.Add(res.ErrorMsg);
                    continue;
                }

                headers = player == "vh" ? KubikService.VhStreamHeaders : KubikService.VkStreamHeaders;
                quality = null;

                if (init.mp4 && res.Value.mp4.Count > 0)
                {
                    quality = new StreamQualityTpl();
                    foreach (var q in res.Value.mp4)
                        quality.Append(HostStreamProxy(q.Value, headers), q.Key);
                }

                url = !string.IsNullOrEmpty(res.Value.hls)
                    ? HostStreamProxy(res.Value.hls, headers)
                    : quality?.Firts()?.link;

                if (url != null && quality != null && !string.IsNullOrEmpty(res.Value.hls))
                    quality.Insert(url, "auto");
            }
            else if (player == "lat")
            {
                if (lh == null || lp <= 0)
                    continue;

                var lat = await InvokeCacheResult<string>($"kubikvkube:v2:lat:{lh}:{lp}:{lv}:{s}:{e}:{lt}", TimeSpan.FromMinutes(20), async c =>
                {
                    string hls = await service.GetLatHls(lh, lp, lv, s, e, lt).ConfigureAwait(false);
                    return string.IsNullOrEmpty(hls) ? c.Fail("lat:video") : c.Success(hls);
                });

                if (!lat.IsSuccess)
                {
                    errors.Add(lat.ErrorMsg);
                    continue;
                }

                headers = KubikService.LatStreamHeaders(lh);
                quality = null;
                url = HostStreamProxy(lat.Value, headers);
            }
        }

        if (string.IsNullOrEmpty(url))
            return OnError(errors.Count > 0 ? string.Join("; ", errors) : "video");

        if (play)
            return RedirectToPlay(url);

        return ContentTo(VideoTpl.ToJson(
            "play",
            url,
            title ?? "Кубик в Кубе",
            streamquality: quality,
            vast: init.vast,
            headers: init.streamproxy ? null : headers,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region Шаблоны
    ITplResult MovieTpl(KubikCatalog cat, string title, string original_title)
    {
        var mtpl = new MovieTpl(title, original_title);

        foreach (string voice in KubikService.OrderVoices(cat.tracks, cat.page.priority_voice))
        {
            var same = cat.tracks.Where(x => x.voice == voice).ToList();
            var track = KubikService.Primary(same);
            string name = KubikService.DisplayName(same);
            string link = VideoLink(cat, track, title);

            mtpl.Append(name, link, "call", stream: accsArgs($"{link}&play=true"), voice_name: name, vast: init.vast);
        }

        return mtpl;
    }

    ITplResult SerialTpl(KubikCatalog cat, string title, string original_title, int year, long kinopoisk_id, string href, int s, string t, bool rjson)
    {
        var seasons = cat.tracks.Where(x => x.season > 0).Select(x => x.season).Distinct().OrderBy(x => x).ToList();

        if (s == -1)
        {
            var stpl = new SeasonTpl(seasons.Count);
            foreach (int season in seasons)
                stpl.Append($"{season} сезон", IndexLink(title, original_title, year, kinopoisk_id, 1, href, season, t, rjson), season);

            return stpl;
        }

        var inSeason = cat.tracks.Where(x => x.season == s).ToList();
        if (inSeason.Count == 0)
            return new EpisodeTpl();

        var voices = KubikService.OrderVoices(inSeason, cat.page.priority_voice);
        if (string.IsNullOrEmpty(t) || !voices.Contains(t))
            t = voices[0];

        var vtpl = new VoiceTpl(voices.Count);
        foreach (string voice in voices)
            vtpl.Append(KubikService.DisplayName(inSeason.Where(x => x.voice == voice)), voice == t, IndexLink(title, original_title, year, kinopoisk_id, 1, href, s, voice, rjson));

        var episodes = inSeason.Where(x => x.voice == t)
            .GroupBy(x => x.episode)
            .OrderBy(g => g.Key)
            .Select(g => KubikService.Primary(g))
            .ToList();

        string voiceName = KubikService.DisplayName(inSeason.Where(x => x.voice == t));
        var etpl = new EpisodeTpl(vtpl, episodes.Count);

        foreach (var track in episodes)
        {
            string link = VideoLink(cat, track, title);

            etpl.Append(
                $"{track.episode} серия",
                title ?? original_title,
                s.ToString(),
                track.episode.ToString(),
                link,
                "call",
                streamlink: accsArgs($"{link}&play=true"),
                voice_name: voiceName,
                vast: init.vast
            );
        }

        return etpl;
    }
    #endregion

    #region Ссылки
    string IndexLink(string title, string original_title, int year, long kinopoisk_id, int serial, string href, int s, string t, bool rjson)
    {
        return $"{host}/lite/kubikvkube?rjson={rjson.ToString().ToLowerInvariant()}" +
               $"&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}" +
               $"&year={year}&kinopoisk_id={kinopoisk_id}&serial={serial}&href={HttpUtility.UrlEncode(href)}&s={s}" +
               (string.IsNullOrEmpty(t) ? string.Empty : $"&t={HttpUtility.UrlEncode(t)}") + UidQuery();
    }

    string VideoLink(KubikCatalog cat, KubikTrack track, string title)
    {
        // основной плеер дорожки + та же серия в остальных плеерах как резерв
        var chain = new List<KubikTrack> { track };
        foreach (string player in new[] { "vh", "vk", "lat" })
        {
            if (player == track.player)
                continue;

            var alt = KubikService.FindAlternative(cat.tracks, track, player);
            if (alt != null)
                chain.Add(alt);
        }

        string link = $"{host}/lite/kubikvkube/video?title={HttpUtility.UrlEncode(title)}&s={track.season}&e={track.episode}";
        var players = new List<string>();

        foreach (var t in chain)
        {
            if (t.player == "vh" && !string.IsNullOrEmpty(t.vk))
            {
                link += $"&vk={t.vk}";
                players.Add("vh");
            }
            else if (t.player == "vk" && !string.IsNullOrEmpty(t.vk_video))
            {
                link += $"&vkv={t.vk_video}";
                players.Add("vk");
            }
            else if (t.player == "lat" && !string.IsNullOrEmpty(cat.lat_host) && cat.page.lat_id > 0)
            {
                link += $"&lh={HttpUtility.UrlEncode(cat.lat_host)}&lp={cat.page.lat_id}&lv={t.lat_video}&lt={t.lat_voice}";
                players.Add("lat");
            }
        }

        return link + "&p=" + string.Join(",", players) + UidQuery();
    }

    string UidQuery()
        => string.IsNullOrEmpty(requestInfo?.user_uid) ? string.Empty : $"&uid={HttpUtility.UrlEncode(requestInfo.user_uid)}";

    string AllowedLatHost(string lh)
    {
        if (string.IsNullOrWhiteSpace(lh))
            return null;

        lh = lh.Trim().TrimEnd('/');
        if (!Regex.IsMatch(lh, "^https?://[a-z0-9.-]+(:[0-9]+)?$", RegexOptions.IgnoreCase))
            return null;

        if (knownLatHosts.ContainsKey(lh))
            return lh;

        if (init.lathosts != null && init.lathosts.Any(h => string.Equals(h?.TrimEnd('/'), lh, StringComparison.OrdinalIgnoreCase)))
            return lh;

        return null;
    }
    #endregion
}

public class ResolveResult
{
    public string href { get; set; }

    public List<SiteSearchItem> found { get; set; } = new List<SiteSearchItem>();
}
