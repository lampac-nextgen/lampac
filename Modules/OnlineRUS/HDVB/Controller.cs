using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace HDVB;

public class HDVBController : BaseOnlineController
{
    public HDVBController() : base(ModInit.conf) { }

    [HttpGet]
    [Staticache]
    [Route("lite/hdvb")]
    async public Task<ActionResult> Index(long kinopoisk_id, string title, string original_title, int t = -1, int s = -1, bool rjson = false, bool similar = false)
    {
        if (similar || kinopoisk_id == 0)
            return await RouteSpiderSearch(title, rjson);

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        reset:

        #region search
        List<Video> data = await search(kinopoisk_id);
        if (data == null || data.Count == 0)
        {
            if (init.rhub && init.rhub_fallback)
            {
                init.rhub = false;
                goto reset;
            }

            return OnError();
        }
        #endregion

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        if (data.First().type == "movie")
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, data.Count);

            foreach (var m in data)
            {
                string iframe = fixframe(init.host, m.iframe_url);
                mtpl.Append(
                    m.translator,
                    $"{host}/lite/hdvb/video?kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&iframe={HttpUtility.UrlEncode(iframe)}",
                    "call",
                    accsArgs($"{host}/lite/hdvb/video.m3u8?kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&iframe={HttpUtility.UrlEncode(iframe)}&play=true")
                );
            }

            return ContentTpl(mtpl);
            #endregion
        }
        else
        {
            #region Сериал
            if (s == -1)
            {
                var tpl = new SeasonTpl();
                var tmp_season = new HashSet<string>();

                foreach (var voice in data)
                {
                    foreach (var season in voice.serial_episodes ?? new List<Season>())
                    {
                        if (!season.season_number.HasValue)
                            continue;

                        string season_name = $"{season.season_number.Value} сезон";
                        if (tmp_season.Add(season_name))
                        {
                            tpl.Append(
                                season_name,
                                $"{host}/lite/hdvb?rjson={rjson}&serial=1&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&s={season.season_number.Value}",
                                season.season_number.Value
                            );
                        }
                    }
                }

                return ContentTpl(tpl);
            }
            else
            {
                #region Перевод
                var vtpl = new VoiceTpl();

                for (int i = 0; i < data.Count; i++)
                {
                    if (data[i].serial_episodes?.FirstOrDefault(i => i.season_number == s) == null)
                        continue;

                    if (t == -1)
                        t = i;

                    vtpl.Append(
                        data[i].translator,
                        t == i,
                        $"{host}/lite/hdvb?rjson={rjson}&serial=1&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&s={s}&t={i}"
                    );
                }
                #endregion

                var etpl = new EpisodeTpl(vtpl);
                string iframe = HttpUtility.UrlEncode(fixframe(init.host, data[t].iframe_url));
                string translator = HttpUtility.UrlEncode(data[t].translator);

                foreach (int episode in data[t].serial_episodes.FirstOrDefault(i => i.season_number == s).episodes ?? new List<int>())
                {
                    string link = $"{host}/lite/hdvb/serial?title={enc_title}&original_title={enc_original_title}&iframe={iframe}&t={translator}&s={s}&e={episode}";
                    string streamlink = accsArgs($"{link.Replace("/serial", "/serial.m3u8")}&play=true");

                    etpl.Append(
                        $"{episode} серия",
                        title ?? original_title,
                        s.ToString(),
                        episode.ToString(),
                        link,
                        "call",
                        streamlink: streamlink
                    );
                }

                return ContentTpl(etpl);
            }
            #endregion
        }
    }


    #region Video
    [HttpGet]
    [Route("lite/hdvb/video")]
    [Route("lite/hdvb/video.m3u8")]
    async public Task<ActionResult> Video(string iframe, string title, string original_title, bool play)
    {
        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        if (rch != null)
        {
            if (rch.IsNotConnected())
            {
                if (init.rhub_fallback && play)
                    rch.Disabled();
                else
                    return ContentTo(rch.connectionMsg);
            }

            if (!play && rch.IsRequiredConnected())
                return ContentTo(rch.connectionMsg);

            if (rch.IsNotSupport(out string rch_error))
                return ShowError(rch_error);
        }

        var cache = await InvokeCacheResult<string>(ipkey($"video:view:video:{iframe}"), 20, async e =>
        {
            var header = HeadersModel.Init(
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                ("sec-fetch-dest", "iframe"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "cross-site")
            );

        reset:

            string urim3u8 = null;
            string vid = "vid11", href = null, csrftoken = null, file = null;

            await httpHydra.GetSpan(iframe, addheaders: header, spanAction: html =>
            {
                href = Rx.Match(html, "\"href\":\"([^\"]+)\"");
                csrftoken = Rx.Match(html, "\"key\":\"([^\"]+)\"")?.Replace("\\", "");

                file = Rx.Match(html, "\"file\":\"([^\"]+)\"")?.Replace("\\", "");
                if (file != null)
                {
                    file = Regex.Replace(file, "^/playlist/", "/");
                    file = Regex.Replace(file, "\\.txt$", "");
                }
            });

            if (!string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(file) && !string.IsNullOrWhiteSpace(csrftoken))
            {
                string origin = Regex.Match(iframe, "(https?://[^/]+)").Groups[1].Value;

                header = HeadersModel.Init(
                    ("accept", "*/*"),
                    ("origin", origin),
                    ("referer", $"{origin}/"),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "same-site"),
                    ("x-csrf-token", csrftoken)
                );

                urim3u8 = await httpHydra.Post($"https://{vid}.{href}/playlist/{file}.txt", "", addheaders: header);

                if (urim3u8 != null && !urim3u8.Contains("/index.m3u8"))
                {
                    file = Regex.Match(urim3u8, "\"file\":\"([^\"]+)\"").Groups[1].Value.Replace("\\", "");
                    file = Regex.Replace(file, "^/playlist/", "/");
                    file = Regex.Replace(file, "\\.txt$", "");

                    if (!string.IsNullOrEmpty(file))
                        urim3u8 = await httpHydra.Post($"https://{vid}.{href}/playlist/{file}.txt", "", addheaders: header);
                }
            }

            if (string.IsNullOrEmpty(urim3u8))
            {
                if (init.rhub && init.rhub_fallback)
                {
                    init.rhub = false;
                    goto reset;
                }

                return e.Fail("m3u8", refresh_proxy: true);
            }

            return e.Success(urim3u8);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string m3u8 = HostStreamProxy(cache.Value);

        if (play)
            return RedirectToPlay(m3u8);

        var headers_stream = init.streamproxy ? null : httpHeaders(init.host, init.headers_stream);

        return ContentTo(VideoTpl.ToJson(
            "play",
            m3u8,
            (title ?? original_title),
            vast: init.vast,
            headers: headers_stream,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region Serial
    [HttpGet]
    [Route("lite/hdvb/serial")]
    [Route("lite/hdvb/serial.m3u8")]
    async public Task<ActionResult> Serial(string iframe, string t, string s, string e, string title, string original_title, bool play)
    {
        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        if (rch != null)
        {
            if (rch.IsNotConnected())
            {
                if (init.rhub_fallback && play)
                    rch.Disabled();
                else
                    return ContentTo(rch.connectionMsg);
            }

            if (!play && rch.IsRequiredConnected())
                return ContentTo(rch.connectionMsg);

            if (rch.IsNotSupport(out string rch_error))
                return ShowError(rch_error);
        }

        var cache = await InvokeCacheResult<string>(ipkey($"video:view:serial:{iframe}:{t}:{s}:{e}"), 20, async result =>
        {
            string vid = "vid11";

            #region playlist
            string mkey_playlist = $"video:view:playlist:{iframe}";
            if (!hybridCache.TryGetValue(mkey_playlist, out (List<Folder> playlist, string href, List<HeadersModel> header) cache))
            {
                cache.header = HeadersModel.Init(
                    ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7"),
                    ("sec-fetch-dest", "iframe"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "cross-site")
                );

            reset_playlist:

                string href = null, csrftoken = null, file = null;

                await httpHydra.GetSpan(iframe, addheaders: cache.header, spanAction: html =>
                {
                    href = Rx.Match(html, "\"href\":\"([^\"]+)\"");
                    csrftoken = Rx.Match(html, "\"key\":\"([^\"]+)\"")?.Replace("\\", "");

                    file = Rx.Match(html, "\"file\":\"([^\"]+)\"")?.Replace("\\", "");

                    if (file != null)
                    {
                        file = Regex.Replace(file, "^/playlist/", "/");
                        file = Regex.Replace(file, "\\.txt$", "");
                    }
                });

                if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(csrftoken))
                {
                    if (init.rhub && init.rhub_fallback)
                    {
                        init.rhub = false;
                        goto reset_playlist;
                    }

                    return result.Fail("playlist:init", refresh_proxy: true);
                }

                string origin = Regex.Match(iframe, "(https?://[^/]+)").Groups[1].Value;

                cache.header = HeadersModel.Init(
                    ("accept", "*/*"),
                    ("origin", origin),
                    ("referer", $"{origin}/"),
                    ("sec-fetch-dest", "empty"),
                    ("sec-fetch-mode", "cors"),
                    ("sec-fetch-site", "same-site"),
                    ("x-csrf-token", csrftoken)
                );

                cache.playlist = await httpHydra.Post<List<Folder>>($"https://{vid}.{href}/playlist/{file}.txt", "", addheaders: cache.header, IgnoreDeserializeObject: true);

                if (cache.playlist != null && cache.playlist.Count > 0)
                {
                    cache.href = href;
                    hybridCache.Set(mkey_playlist, cache, cacheTime(40));
                }
                else
                {
                    if (init.rhub && init.rhub_fallback)
                    {
                        init.rhub = false;
                        goto reset_playlist;
                    }

                    return result.Fail("playlist", refresh_proxy: true);
                }
            }
            #endregion

            #region episode
            if (cache.playlist == null || cache.playlist.Count == 0)
                return result.Fail("playlist:empty");

            reset_episode:

            string episode = cache.playlist
                .FirstOrDefault(i => i.id == s)?.folder
                .FirstOrDefault(i => i.episode == e)?.folder
                .FirstOrDefault(i => i.title == t)?.file;

            string urim3u8 = null;

            if (!string.IsNullOrEmpty(episode))
            {
                episode = Regex.Replace(episode, "^/playlist/", "/");
                episode = Regex.Replace(episode, "\\.txt$", "");

                urim3u8 = await httpHydra.Post($"https://{vid}.{cache.href}/playlist/{episode}.txt", "", addheaders: cache.header);
            }

            if (string.IsNullOrEmpty(urim3u8) || !urim3u8.Contains("/index.m3u8"))
            {
                if (init.rhub && init.rhub_fallback)
                {
                    init.rhub = false;
                    goto reset_episode;
                }

                return result.Fail("episode", refresh_proxy: true);
            }

            return result.Success(urim3u8);
            #endregion
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string m3u8 = HostStreamProxy(cache.Value);

        if (play)
            return Redirect(m3u8);

        var headers_stream = init.streamproxy ? null : httpHeaders(init.host, init.headers_stream);

        return ContentTo(VideoTpl.ToJson(
            "play",
            m3u8,
            (title ?? original_title),
            vast: init.vast,
            headers: headers_stream,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region SpiderSearch
    [HttpGet]
    [Route("lite/hdvb-search")]
    async public Task<ActionResult> RouteSpiderSearch(string title, bool rjson = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError();

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        rhubFallback:
        var cache = await InvokeCacheResult<List<Video>>($"hdvb:search:{title}", TimeSpan.FromHours(4), async e =>
        {
            var newheaders = HeadersModel.Init(Http.defaultFullHeaders);
            var root = await httpHydra.Get<List<Video>>($"{init.cors(init.apihost)}/api/videos.json?token={init.token}&title={HttpUtility.UrlEncode(title)}", safety: true, newheaders: newheaders);

            if (root == null || root.Count == 0)
                return e.Fail("results");

            return e.Success(root);
        });

        if (IsRhubFallback(cache, safety: true))
            goto rhubFallback;

        return ContentTpl(cache, () =>
        {
            var hash = new HashSet<long>(cache.Value.Count);
            var stpl = new SimilarTpl(cache.Value.Count);

            foreach (var j in cache.Value)
            {
                var kinopoisk_id = j.kinopoisk_id;
                if (kinopoisk_id > 0 && !hash.Contains((long)kinopoisk_id))
                {
                    hash.Add((long)kinopoisk_id);
                    stpl.Append(
                        j.title_ru ?? j.title_en,
                        (j.year ?? 0).ToString(),
                        string.Empty,
                        $"{host}/lite/hdvb?kinopoisk_id={kinopoisk_id}",
                        PosterApi.Size(j.poster)
                    );
                }
            }

            return stpl;
        });
    }
    #endregion


    #region search
    async ValueTask<List<Video>> search(long kinopoisk_id)
    {
        string memKey = $"hdvb:view:{kinopoisk_id}";

        if (!hybridCache.TryGetValue(memKey, out List<Video> root))
        {
            var newheaders = HeadersModel.Init(Http.defaultFullHeaders);
            root = await httpHydra.Get<List<Video>>($"{init.cors(init.apihost)}/api/videos.json?token={init.token}&id_kp={kinopoisk_id}", safety: true, newheaders: newheaders);

            if (root == null)
            {
                proxyManager?.Refresh();
                return null;
            }

            proxyManager?.Success();

            hybridCache.Set(memKey, root, TimeSpan.FromHours(4), inmemory: false);
        }

        if (root.Count == 0)
            return null;

        return root;
    }
    #endregion


    static string fixframe(string _h, string iframe)
    {
        return Regex.Replace(iframe, "^https?://[^/]+", _h);
    }
}
