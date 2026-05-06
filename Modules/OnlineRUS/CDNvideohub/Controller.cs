using Microsoft.AspNetCore.Mvc;
using Shared.Attributes;
using Shared.Services.RxEnumerate;
using System.Net.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Shared;
using Shared.Models.Templates;
using Shared.Services;

namespace CDNvideohub;

public class CDNvideohubController : BaseOnlineController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public CDNvideohubController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet]
    [Staticache]
    [Route("lite/cdnvideohub")]
    async public Task<ActionResult> Index(string title, string original_title, long kinopoisk_id, string t, int s = -1, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        rhubFallback:
        var cache = await InvokeCacheResult<RootObject>($"cdnvideohub:view:{kinopoisk_id}", TimeSpan.FromHours(4), async e =>
        {
            var root = await httpHydra.Get<RootObject>($"{init.host}/api/v1/player/sv/playlist?pub=12&aggr=kp&id={kinopoisk_id}");

            if (root?.items == null)
                return e.Fail("root", refresh_proxy: true);

            if (root.items.Length == 0)
                return e.Fail("video");

            return e.Success(root);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return ContentTpl(cache, () =>
        {
            if (cache.Value.isSerial)
            {
                #region Сериал
                string defaultargs = $"&rjson={rjson}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}";

                if (s == -1)
                {
                    #region Сезоны
                    var tpl = new SeasonTpl();
                    var hash = new HashSet<int>();

                    foreach (var video in cache.Value.items.OrderBy(i => i.season))
                    {
                        int season = video.season;

                        if (hash.Add(season))
                        {
                            tpl.Append(
                                $"{season} сезон",
                                $"{host}/lite/cdnvideohub?s={season}{defaultargs}",
                                season
                            );
                        }
                    }

                    return tpl;
                    #endregion
                }
                else
                {
                    #region Перевод
                    var vtpl = new VoiceTpl();
                    var tmpVoice = new HashSet<string>();

                    foreach (var video in cache.Value.items)
                    {
                        if (video.season != s)
                            continue;

                        string voice_studio = video.voiceStudio;
                        if (string.IsNullOrEmpty(voice_studio) || tmpVoice.Contains(voice_studio))
                            continue;

                        tmpVoice.Add(voice_studio);

                        if (string.IsNullOrEmpty(t))
                            t = voice_studio;

                        vtpl.Append(
                            voice_studio,
                            t == voice_studio,
                            $"{host}/lite/cdnvideohub?s={s}&t={HttpUtility.UrlEncode(voice_studio)}{defaultargs}"
                        );
                    }
                    #endregion

                    var etpl = new EpisodeTpl(vtpl);
                    var tmpEpisode = new HashSet<int>();

                    foreach (var video in cache.Value.items.OrderBy(i => i.episode))
                    {
                        if (video.season != s || video.voiceStudio != t)
                            continue;

                        string vkId = video.vkId;
                        if (string.IsNullOrEmpty(vkId))
                            continue;

                        int episode = video.episode;

                        if (tmpEpisode.Contains(episode))
                            continue;

                        tmpEpisode.Add(episode);

                        string link = $"{host}/lite/cdnvideohub/video.m3u8?vkId={vkId}&title={HttpUtility.UrlEncode(title)}";

                        etpl.Append(
                            $"{episode} серия",
                            title ?? original_title,
                            s.ToString(),
                            episode.ToString(),
                            link,
                            "call",
                            streamlink: accsArgs($"{link}&play=true"),
                            vast: init.vast
                        );
                    }

                    return etpl;
                }
                #endregion
            }
            else
            {
                #region Фильм
                var mtpl = new MovieTpl(title, original_title);

                foreach (var video in cache.Value.items)
                {
                    mtpl.Append(
                        video.voiceStudio ?? video.voiceType,
                        accsArgs($"{host}/lite/cdnvideohub/video.m3u8?vkId={video.vkId}&title={HttpUtility.UrlEncode(title)}"),
                        "call",
                        vast: init.vast
                    );
                }

                return mtpl;
                #endregion
            }
        });
    }


    #region Video
    [HttpGet]
    [Route("lite/cdnvideohub/video.m3u8")]
    async public Task<ActionResult> Video(string vkId, string title, bool play)
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

    rhubFallback:

        var cache = await InvokeCacheResult<string>(ipkey($"cdnvideohub:video:{vkId}"), 20, async e =>
        {
            string hls = null;

            await httpHydra.GetSpan($"{init.host}/api/v1/player/sv/video/{vkId}", iframe =>
            {
                hls = Rx.Slice(iframe, "\"hlsUrl\":\"", "\"").ToString();
            });

            if (string.IsNullOrEmpty(hls))
                return e.Fail("hls", refresh_proxy: true);

            return e.Success(hls.Replace("u0026", "&").Replace("\\", ""));
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string link = HostStreamProxy(cache.Value);

        if (play)
            return RedirectToPlay(link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            link,
            title,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }
    #endregion
}
