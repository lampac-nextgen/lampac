using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services.HTTP;
using Shared.Services.Pools;
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

    [HttpGet, Staticache(manually: true)]
    [Route("lite/moonanime")]
    async public Task<ActionResult> Index(string imdb_id, string title, string original_title, long animeid, string t, short s = -1, bool rjson = false, bool similar = false)
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
                string sArhc = s.ToString();

                #region Перевод
                var vtpl = new VoiceTpl();
                string activTranslate = t;

                foreach (var voices in root)
                {
                    foreach (var voice in voices)
                    {
                        foreach (var season in voice.Value)
                        {
                            if (season.Key != sArhc)
                                continue;

                            if (string.IsNullOrEmpty(activTranslate))
                                activTranslate = voice.Key;

                            vtpl.Append(
                                voice.Key,
                                activTranslate == voice.Key,
                                $"{host}/lite/moonanime?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&animeid={animeid}&s={s}&t={HttpUtility.UrlEncode(voice.Key)}"
                            );
                        }
                    }
                }
                #endregion

                var etpl = new EpisodeTpl(vtpl);

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
                                string vod = folder.vod;
                                if (string.IsNullOrEmpty(vod))
                                    continue;

                                string link = $"{host}/lite/moonanime/video?vod={HttpUtility.UrlEncode(vod)}&title={enc_title}&original_title={enc_original_title}";

                                etpl.Append(
                                    $"{folder.episode} серия",
                                    title,
                                    s,
                                    folder.episode,
                                    link,
                                    "call",
                                    streamlink: accsArgs($"{link.Replace("/video", "/video.m3u8")}&play=true")
                                );
                            }
                        }
                    }
                }

                return ContentTpl(etpl);
            }
            #endregion
        }
    }

    #region Video
    [HttpGet, Staticache(manually: true)]
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
            string js = null;
            (string file, string subtitle) data = (null, null);

            await PlaywrightHttp.GetSpan(init.plugin, vod, html =>
            {
                js = Decode(Rx.Slice(html, "=atob(\"", "\");"));
            }, httpHeaders(init), proxy_data);

            if (js == null)
                return e.Fail("js decode", refresh_proxy: true);

            string key = Regex.Match(js, "var k=\"([^\"]+)\"").Groups[1].Value;
            string file = Regex.Match(js, "file:([\t ]+)?_0xd\\(\"([^\"]+)\"\\)").Groups[2].Value;

            data.file = _0xd(file, key);
            if (string.IsNullOrEmpty(data.file) || !data.file.StartsWith("http"))
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

        var headers_stream = HeadersModel.Init(
            ("accept", "*/*"),
            ("accept-language", "ru,en;q=0.9,en-GB;q=0.8,en-US;q=0.7"),
            ("dnt", "1"),
            ("priority", "u=1, i"),
            ("sec-ch-ua", "\"Chromium\";v=\"130\", \"Microsoft Edge\";v=\"130\", \"Not?A_Brand\";v=\"99\""),
            ("sec-ch-ua-mobile", "?0"),
            ("sec-ch-ua-platform", "\"Windows\""),
            ("sec-fetch-dest", "empty"),
            ("sec-fetch-mode", "cors"),
            ("sec-fetch-site", "cross-site"),
            ("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0")
        );

        string file = HostStreamProxy(cache.Value.file, headers: headers_stream);

        if (play)
            return RedirectToPlay(file);

        return ContentTo(VideoTpl.ToJson(
            "play",
            file,
            (title ?? original_title),
            subtitles: subtitles,
            vast: init.vast,
            httpContext: HttpContext,
            headers: init.streamproxy ? null : headers_stream
        ));
    }
    #endregion

    #region Decode _0xd
    static string Decode(ReadOnlySpan<char> base64Input)
    {
        try
        {
            int capacity = Encoding.UTF8.GetMaxByteCount(base64Input.Length);

            using (var nbuf = new BufferBytePool(capacity))
            {
                if (!Convert.TryFromBase64Chars(base64Input, nbuf.Span, out int bytesWritten))
                    return null;

                const int KeySize = 32;
                const int HeaderSize = 1 + KeySize;

                if (bytesWritten < HeaderSize)
                    return null;

                Span<byte> data = nbuf.Span.Slice(0, bytesWritten);

                byte state = data[0];
                ReadOnlySpan<byte> key = data.Slice(1, KeySize);
                Span<byte> payload = data.Slice(HeaderSize, bytesWritten - HeaderSize);

                for (int i = 0; i < payload.Length; i++)
                {
                    byte encrypted = payload[i];
                    byte keyByte = key[i % KeySize];

                    payload[i] = (byte)(encrypted ^ keyByte ^ state);
                    state = (byte)((encrypted + keyByte) & 0xFF);
                }

                return Encoding.UTF8.GetString(payload);
            }
        }
        catch
        {
            return null;
        }
    }

    public static string _0xd(string file, string key)
    {
        try
        {
            ReadOnlySpan<byte> keyByte = Encoding.UTF8.GetBytes(key);
            int capacity = Encoding.UTF8.GetMaxByteCount(file.Length);

            using (var nbuf = new BufferBytePool(capacity))
            {
                if (!Convert.TryFromBase64Chars(file, nbuf.Span, out int bytesWritten))
                    return null;

                Span<byte> data = nbuf.Span.Slice(0, bytesWritten);

                for (int i = 0; i < data.Length; i++)
                    data[i] = (byte)(data[i] ^ keyByte[i % keyByte.Length]);

                return Encoding.UTF8.GetString(data);
            }
        }
        catch
        {
            return null;
        }
    }
    #endregion
}
