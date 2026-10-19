using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace LordFilm;

public class LordFilmController : BaseOnlineController<ModuleConf>
{
    static LordFilmSession session;
    static readonly object sessionLock = new object();

    public LordFilmController() : base(ModInit.conf)
    {
        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("dash"))
                i.dash = c.dash;

            if (j.ContainsKey("both"))
                i.both = c.both;

            return i;
        };
    }

    LordFilmSession Session()
    {
        var current = session;

        if (current != null && current.MatchHost(init.host))
            return current;

        lock (sessionLock)
        {
            if (session == null || !session.MatchHost(init.host))
                session = new LordFilmSession(init.host, Http.UserAgent);

            return session;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/lordfilm")]
    async public Task<ActionResult> Index(string title, string original_title, string uri = null, short serial = 0, short year = 0, short s = -1, bool rjson = false, bool similar = false)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(uri))
            return OnError();

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        #region Поиск
        if (string.IsNullOrWhiteSpace(uri))
        {
            var search = await InvokeCacheResult<List<SearchItem>>($"lordfilm:search:{title}:{year}:{serial}", 240, textJson: true, onget: async e =>
            {
                var items = await Session().Search(title);

                if ((items == null || items.Count == 0) && !string.IsNullOrWhiteSpace(original_title) && original_title != title)
                    items = await Session().Search(original_title);

                if (items == null || items.Count == 0)
                    return e.Fail("search");

                return e.Success(items);
            });

            if (!search.IsSuccess)
                return OnError(search.ErrorMsg);

            var best = LordFilmParser.PickBest(search.Value, title, year, serial);

            if (similar || best == null)
                return ContentTpl(BuildSimilar(search.Value, title, original_title, serial, year, rjson));

            uri = best.uri;
        }
        #endregion

        var cache = await InvokeCacheResult<EmbedModel>($"lordfilm:view:{uri}", 20, textJson: true, onget: async e =>
        {
            var md = await Session().Embed(uri);

            if (md == null || md.IsEmpty)
                return e.Fail("embed");

            return e.Success(md);
        });

        return ContentTpl(cache, () => Tpl(cache.Value, title, original_title, uri, serial, year, s, rjson));
    }


    [HttpGet, Staticache(manually: true)]
    [Route("lite/lordfilm-search")]
    async public Task<ActionResult> RouteSearch(string title, string original_title, short serial = 0, short year = 0, bool rjson = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError("title", gbcache: false);

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        var search = await InvokeCacheResult<List<SearchItem>>($"lordfilm:search:{title}:{year}:{serial}", 240, textJson: true, onget: async e =>
        {
            var items = await Session().Search(title);

            if ((items == null || items.Count == 0) && !string.IsNullOrWhiteSpace(original_title) && original_title != title)
                items = await Session().Search(original_title);

            if (items == null || items.Count == 0)
                return e.Fail("search");

            return e.Success(items);
        });

        if (!search.IsSuccess)
            return ContentTpl(new SimilarTpl(0));

        return ContentTpl(BuildSimilar(search.Value, title, original_title, serial, year, rjson));
    }


    [HttpGet, AllowAnonymous]
    [Route("lite/lordfilm/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string names = null)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (!IsAllowedUri(uri))
            return OnError();

        string master = await Session().Fetch(uri);
        if (string.IsNullOrEmpty(master))
            return OnError();

        // Технические имена дорожек (rus0, ukr4, eng6) заменяем на названия озвучек
        if (!string.IsNullOrEmpty(names))
            master = LordFilmParser.RenameAudio(master, names);

        // CDN отдаёт сегменты только тому IP, который запросил master, поэтому весь плейлист
        // (и вложенные m3u8) обязан идти через прокси Lampac — принудительно, независимо от streamproxy
        master = LordFilmParser.ProxyUrls(master, u => HostStreamProxy(u, force_streamproxy: true));

        return Content(master, "application/vnd.apple.mpegurl");
    }


    [HttpGet, AllowAnonymous]
    [Route("lite/lordfilm/video.mpd")]
    async public Task<ActionResult> VideoDash(string uri, string names = null)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (!IsAllowedUri(uri))
            return OnError();

        string mpd = await Session().Fetch(uri);
        if (string.IsNullOrEmpty(mpd))
            return OnError();

        // Технические языки дорожек (rus0, ukr4, eng6) заменяем на названия озвучек
        if (!string.IsNullOrEmpty(names))
            mpd = LordFilmParser.RenameDashAudio(mpd, names);

        // Сегменты CDN привязаны к IP сервера: <BaseURL> переводим на /proxy-dash/ (аналог ProxyAPI.Dash),
        // иначе плеер пойдёт в CDN напрямую со своего IP и получит 410
        mpd = Regex.Replace(mpd, "<BaseURL>([^<]+)</BaseURL>", m =>
        {
            string enc = ProxyLink.Encrypt(m.Groups[1].Value, requestInfo.IP, forceMd5: true);
            return $"<BaseURL>{host}/proxy-dash/{enc}/</BaseURL>";
        });

        return Content(mpd, "application/dash+xml");
    }


    #region Similar
    SimilarTpl BuildSimilar(List<SearchItem> items, string title, string original_title, short serial, short year, bool rjson)
    {
        var stpl = new SimilarTpl(items.Count);

        foreach (var item in items)
        {
            stpl.Append(
                item.name,
                item.year,
                string.Empty,
                $"{host}/lite/lordfilm?rjson={rjson}&uri={HttpUtility.UrlEncode(item.uri)}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&serial={serial}&year={year}",
                Poster(item.poster)
            );
        }

        return stpl;
    }

    ITplResult Tpl(EmbedModel md, string title, string original_title, string uri, short serial, short year, short s, bool rjson)
    {
        if (md == null)
            return null;

        if (md.movie != null)
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, 1);

            foreach (var st in Streams(md.movie.voicename, md.movie.hls, md.movie.dash, md.movie.audio))
                mtpl.Append(st.label, st.link, subtitles: Subtitles(md.movie.cc), vast: init.vast);

            return mtpl.IsEmpty ? null : mtpl;
            #endregion
        }

        if (md.seasons == null || md.seasons.Length == 0)
            return null;

        string enc_uri = HttpUtility.UrlEncode(uri);
        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        if (s == -1)
        {
            #region Сезоны
            var tpl = new SeasonTpl(md.seasons.Length);

            foreach (var season in md.seasons)
            {
                tpl.Append(
                    $"{season.season} сезон",
                    $"{host}/lite/lordfilm?rjson={rjson}&uri={enc_uri}&title={enc_title}&original_title={enc_original_title}&serial={serial}&year={year}&s={season.season}",
                    season.season.ToString()
                );
            }

            return tpl;
            #endregion
        }
        else
        {
            #region Серии
            var season = md.seasons.FirstOrDefault(i => i.season == s);

            if (season?.episodes == null)
                return null;

            var etpl = new EpisodeTpl(season.episodes.Count);

            foreach (var episode in season.episodes)
            {
                var streams = Streams(episode.voicename, episode.hls, episode.dash, episode.audio);

                if (streams.Count == 0)
                    continue;

                // При both: true не дублируем серии: один пункт на серию, а HLS/DASH — как выбор качества в плеере
                StreamQualityTpl quality = null;

                if (streams.Count > 1)
                {
                    quality = new StreamQualityTpl(streams.Count);

                    foreach (var st in streams)
                        quality.Append(st.link, st.label);
                }

                etpl.Append(
                    episode.voicename ?? "По умолчанию",
                    title ?? original_title,
                    s,
                    episode.episode,
                    streams[0].link,
                    streamquality: quality,
                    subtitles: Subtitles(episode.cc),
                    vast: init.vast
                );
            }

            return etpl.IsEmpty ? null : etpl;
            #endregion
        }
    }
    #endregion

    #region Helpers
    /// <summary>Варианты потока (HLS и/или DASH) с учётом init.conf: dash / both.</summary>
    List<(string label, string link)> Streams(string voicename, string hls, string dash, List<string> audio)
    {
        var list = new List<(string label, string link)>();

        if (init.both)
        {
            if (!string.IsNullOrEmpty(hls))
                list.Add(("HLS", StreamUrl(hls, audio, "m3u8")));

            if (!string.IsNullOrEmpty(dash))
                list.Add(("DASH 1080p", StreamUrl(dash, audio, "mpd")));

            return list;
        }

        string stream = init.dash ? (dash ?? hls) : hls;

        if (string.IsNullOrEmpty(stream))
            stream = hls ?? dash;

        if (!string.IsNullOrEmpty(stream))
        {
            bool mpd = stream.Contains(".mpd", StringComparison.OrdinalIgnoreCase);
            list.Add((voicename ?? "По умолчанию", StreamUrl(stream, audio, mpd ? "mpd" : "m3u8")));
        }

        return list;
    }

    /// <summary>
    /// Ссылка на поток. HLS идёт через свой маршрут (нормальные подписи дорожек), DASH — через свой
    /// маршрут (подписи дорожек в манифесте). Суффикс <c>#.m3u8</c> / <c>#.mpd</c> — подсказка типа плееру.
    /// </summary>
    string StreamUrl(string stream, List<string> audio, string ext)
    {
        if (string.IsNullOrEmpty(stream))
            return null;

        string names = (audio != null && audio.Count > 0)
            ? $"&names={Uri.EscapeDataString(string.Join("|", audio))}"
            : string.Empty;

        // accsArgs обязателен: маршрут /lite/... проходит через проверку доступа, иначе плеер получит accsdb
        if (ext == "mpd")
            return accsArgs($"{host}/lite/lordfilm/video.mpd?uri={Uri.EscapeDataString(stream)}{names}") + "#.mpd";

        return accsArgs($"{host}/lite/lordfilm/video.m3u8?uri={Uri.EscapeDataString(stream)}{names}") + "#.m3u8";
    }

    SubtitleTpl Subtitles(List<Cc> cc)
    {
        if (cc == null || cc.Count == 0)
            return null;

        var subtitles = new SubtitleTpl(cc.Count);

        foreach (var item in cc)
        {
            if (string.IsNullOrEmpty(item?.url) || string.IsNullOrEmpty(item?.name))
                continue;

            subtitles.Append(item.name, HostStreamProxy(item.url, force_streamproxy: true));
        }

        return subtitles;
    }

    string Poster(string img)
    {
        if (string.IsNullOrWhiteSpace(img))
            return null;

        if (img.StartsWith("//", StringComparison.Ordinal))
            return "https:" + img;

        if (img.StartsWith("/", StringComparison.Ordinal))
            return init.host + img;

        return img;
    }

    /// <summary>
    /// Маршруты потоков анонимные (иначе accsdb режет плеер), поэтому принимаем только ссылки CDN —
    /// чтобы сервер нельзя было использовать как открытый прокси.
    /// </summary>
    static bool IsAllowedUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return false;

        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)
            return false;

        return u.Host.Equals("interkh.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.EndsWith(".interkh.com", StringComparison.OrdinalIgnoreCase);
    }
    #endregion
}
