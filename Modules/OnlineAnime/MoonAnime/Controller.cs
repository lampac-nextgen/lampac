using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace MoonAnime;

public class MoonAnimeController : BaseOnlineController
{
    public MoonAnimeController() : base(ModInit.conf) { }

    [HttpGet]
    [Staticache]
    [Route("lite/moonanime")]
    async public Task<ActionResult> Index(string imdb_id, string title, string original_title, long animeid, string t, int s = -1, bool rjson = false, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(init.token))
            return OnError("token", statusCode: 401, gbcache: false);

        if (animeid == 0)
        {
        #region Поиск
        rhubFallback:
            var cache = await InvokeCacheResult<List<(string title, string year, long id, string poster)>>($"moonanime:search:{imdb_id}:{title}:{original_title}", TimeSpan.FromHours(4), async e =>
            {
                async Task<SearchRoot> goSearch(string arg)
                {
                    if (string.IsNullOrEmpty(arg.Split("=")?[1]))
                        return null;

                    var search = await httpHydra.Get<SearchRoot>($"{init.host}/api/2.0/titles?api_key={init.token}&limit=20" + arg, safety: true);
                    if (search?.anime_list == null || search.anime_list.Count == 0)
                        return null;

                    return search;
                }

                SearchRoot search =
                    await goSearch($"&imdbid={imdb_id}") ??
                    await goSearch($"&japanese_title={HttpUtility.UrlEncode(original_title)}") ??
                    await goSearch($"&title={HttpUtility.UrlEncode(title)}");

                if (search == null)
                    return e.Fail("search", refresh_proxy: true);

                var catalog = new List<(string title, string year, long id, string poster)>();

                foreach (var anime in search.anime_list)
                {
                    string _titl = anime.title;
                    int year = anime.year;

                    if (string.IsNullOrEmpty(_titl))
                        continue;

                    catalog.Add((_titl, year.ToString(), anime.id, anime.poster));
                }

                if (catalog.Count == 0)
                    return e.Fail("catalog");

                proxyManager?.Success();
                return e.Success(catalog);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            if (!similar && cache.Value.Count == 1)
                return LocalRedirect(accsArgs($"/lite/moonanime?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&animeid={cache.Value[0].id}"));

            var stpl = new SimilarTpl(cache.Value.Count);
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            foreach (var res in cache.Value)
            {
                stpl.Append(
                    res.title,
                    res.year,
                    string.Empty,
                    $"{host}/lite/moonanime?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&animeid={res.id}",
                    PosterApi.Size(res.poster)
                );
            }

            return ContentTpl(stpl);
            #endregion
        }
        else
        {
        #region Серии
        rhubFallback:
            var cache = await InvokeCacheResult<List<Dictionary<string, Dictionary<string, List<Episode>>>>>($"moonanime:playlist:{animeid}", 30, async e =>
            {
                var root = await httpHydra.Get<List<Dictionary<string, Dictionary<string, List<Episode>>>>>($"{init.host}/api/2.0/title/{animeid}/videos?api_key={init.token}", safety: true);
                if (root == null || root.Count == 0)
                    return e.Fail("root", refresh_proxy: true);

                return e.Success(root);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            var root = cache.Value;
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (s == -1)
            {
                var tpl = new SeasonTpl();
                var temp = new HashSet<string>();

                foreach (var voices in root)
                {
                    foreach (var voice in voices)
                    {
                        foreach (var season in voice.Value)
                        {
                            if (temp.Add(season.Key))
                            {
                                tpl.Append(
                                    $"{season.Key} сезон",
                                    $"{host}/lite/moonanime?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&animeid={animeid}&s={season.Key}",
                                    season.Key
                                );
                            }
                        }
                    }
                }

                if (temp.Count == 0)
                    return OnError("seasons");

                return ContentTpl(tpl);
            }
            else
            {
                #region Перевод
                var vtpl = new VoiceTpl();
                string activTranslate = t;
                bool hasTranslate = false;

                foreach (var voices in root)
                {
                    foreach (var voice in voices)
                    {
                        foreach (var season in voice.Value)
                        {
                            if (season.Key != s.ToString())
                                continue;

                            if (string.IsNullOrEmpty(activTranslate))
                                activTranslate = voice.Key;

                            hasTranslate = true;
                            vtpl.Append(
                                voice.Key,
                                activTranslate == voice.Key,
                                $"{host}/lite/moonanime?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&animeid={animeid}&s={s}&t={HttpUtility.UrlEncode(voice.Key)}"
                            );
                        }
                    }
                }

                if (!hasTranslate)
                    return OnError("translate");
                #endregion

                var etpl = new EpisodeTpl(vtpl);
                string sArhc = s.ToString();
                bool hasEpisode = false;

                foreach (var voices in root)
                {
                    foreach (var voice in voices)
                    {
                        if (voice.Key != activTranslate)
                            continue;

                        foreach (var season in voice.Value)
                        {
                            if (season.Key != sArhc)
                                continue;

                            foreach (var folder in season.Value)
                            {
                                int episode = folder.episode;
                                string vod = folder.vod;
                                if (string.IsNullOrEmpty(vod))
                                    continue;

                                string link = $"{host}/lite/moonanime/video?vod={HttpUtility.UrlEncode(vod)}&title={enc_title}&original_title={enc_original_title}";
                                string streamlink = accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true");

                                hasEpisode = true;
                                etpl.Append(
                                    $"{episode} серия",
                                    title,
                                    sArhc,
                                    episode.ToString(),
                                    link,
                                    "call",
                                    streamlink: streamlink
                                );
                            }
                        }
                    }
                }

                if (!hasEpisode)
                    return OnError("episodes");

                return ContentTpl(etpl);
            }
            #endregion
        }
    }

    #region Video
    [HttpGet]
    [Route("lite/moonanime/video")]
    [Route("lite/moonanime/video.m3u8")]
    async public Task<ActionResult> Video(string vod, bool play, string title, string original_title)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (string.IsNullOrEmpty(init.token))
            return OnError("token", statusCode: 401, gbcache: false);

        rhubFallback:
        var cache = await InvokeCacheResult<(string file, string subtitle)>($"moonanime:vod:{vod}", 30, async e =>
        {
            (string file, string subtitle) data = (null, null);

            string iframe = await PlaywrightBrowser.Get(init, vod, httpHeaders(init), proxy_data);
            if (string.IsNullOrEmpty(iframe))
                return e.Fail("iframe", refresh_proxy: true);

            string js = Decode(Rx.Slice(iframe, "=atob(\"", "\");").ToString());
            if (js == null)
                return e.Fail("js decode");

            data.file = _0xd(Regex.Match(js, "file:([\t ]+)?_0xd\\(\"([^\"]+)\"\\)").Groups[2].Value);
            if (string.IsNullOrEmpty(data.file))
                return e.Fail("file");

            return e.Success(data);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var subtitles = new SubtitleTpl();
        if (!string.IsNullOrEmpty(cache.Value.subtitle))
            subtitles.Append("По умолчанию", cache.Value.subtitle);

        string file = HostStreamProxy(cache.Value.file, headers: HeadersModel.Init(
            ("accept", "*/*"),
            ("accept-language", "ru,en;q=0.9,en-GB;q=0.8,en-US;q=0.7"),
            ("dnt", "1"),
            //("origin", CrypTo.DecodeBase64("aHR0cDovL2xhbXBhLm14")),
            ("priority", "u=1, i"),
            ("sec-ch-ua", "\"Chromium\";v=\"130\", \"Microsoft Edge\";v=\"130\", \"Not?A_Brand\";v=\"99\""),
            ("sec-ch-ua-mobile", "?0"),
            ("sec-ch-ua-platform", "\"Windows\""),
            ("sec-fetch-dest", "empty"),
            ("sec-fetch-mode", "cors"),
            ("sec-fetch-site", "cross-site"),
            ("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0")
        ));

        if (play)
            return RedirectToPlay(file);

        return ContentTo(VideoTpl.ToJson(
            "play",
            file,
            (title ?? original_title),
            subtitles: subtitles,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }
    #endregion


    #region Decode _0xd
    public static string Decode(string base64Input)
    {
        try
        {
            byte[] data = Convert.FromBase64String(base64Input);

            if (data.Length < 32)
                return null;

            byte[] key = new byte[32];
            Array.Copy(data, 0, key, 0, 32);

            byte[] output = new byte[data.Length - 32];

            for (int i = 0; i < output.Length; i++)
                output[i] = (byte)(data[i + 32] ^ key[i % 32]);

            return Encoding.UTF8.GetString(output);
        }
        catch { return null; }
    }

    public static string _0xd(string e)
    {
        try
        {
            const string k = "mAnK";

            byte[] bytes = Convert.FromBase64String(e);
            byte[] result = new byte[bytes.Length];

            for (int i = 0; i < bytes.Length; i++)
                result[i] = (byte)(bytes[i] ^ k[i % k.Length]);

            return Encoding.UTF8.GetString(result);
        }
        catch { return null; }
    }
    #endregion
}
