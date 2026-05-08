using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
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
               host,
               init,
               httpHydra,
               streamfile => HostStreamProxy(streamfile)
            );
        };
    }


    [HttpGet]
    [Staticache]
    [Route("lite/flixcdn")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int t = -1, int s = -1, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(init?.token))
            return OnError();

    rhubFallback:
        var cache = await InvokeCacheResult<SearchItem>($"flixcdn:search:{imdb_id}:{kinopoisk_id}:{title}:{similar}", TimeSpan.FromHours(4), async e =>
        {
            var search = await oninvk.SearchByTitle(imdb_id, kinopoisk_id, title, original_title, similar);
            if (search == null)
                return e.Fail("SearchByTitle", refresh_proxy: true);

            return e.Success(search);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return ContentTpl(cache, () =>
        {
            var result = cache.Value;

            if (result.similar != null)
                return result.similar;

            if (result.type is "movie" or "cartoon")
            {
                #region Фильм
                var mtpl = new MovieTpl(title, original_title, result.translations.Count);

                foreach (var voice in result.translations)
                {
                    mtpl.Append(
                        voice.title,
                        $"{host}/lite/flixcdn/stream?iframe={EncryptQuery(result.iframe_url)}&t={voice.id}",
                        "call",
                        vast: init.vast
                    );
                }

                return mtpl;
                #endregion
            }
            else
            {
                #region Сериал
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);

                if (s == -1)
                {
                    var tpl = new SeasonTpl();
                    var hash = new HashSet<int>();

                    foreach (var voice in result.translations.OrderBy(s => s.season))
                    {
                        if (hash.Add(voice.season))
                        {
                            tpl.Append(
                                $"{voice.season} сезон",
                                $"{host}/lite/flixcdn?similar={similar}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&t={t}&s={voice.season}",
                                voice.season
                            );
                        }
                    }

                    return tpl;
                }
                else
                {
                    #region Перевод
                    var vtpl = new VoiceTpl();
                    var tmpVoice = new HashSet<int>(20);

                    foreach (var voice in result.translations.Where(i => i.season == s))
                    {
                        if (tmpVoice.Add(voice.id))
                        {
                            if (t == -1)
                                t = voice.id;

                            vtpl.Append(
                                voice.title,
                                t == voice.id,
                                $"{host}/lite/flixcdn?similar={similar}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&t={voice.id}&s={s}"
                            );
                        }
                    }
                    #endregion

                    var etpl = new EpisodeTpl(vtpl);
                    string sArhc = s.ToString();

                    var targetVoice = result.translations.FirstOrDefault(i => i.season == s && i.id == t);
                    if (targetVoice == null)
                        return default;

                    for (int e = 1; e <= targetVoice.episode; e++)
                    {
                        string link = $"{host}/lite/flixcdn/stream?iframe={EncryptQuery(result.iframe_url)}&t={t}&s={s}&e={e}";

                        etpl.Append(
                            $"Серия {e}",
                            title ?? original_title,
                            sArhc,
                            e.ToString(),
                            link,
                            "call",
                            streamlink: $"{link}&play=true",
                            vast: init.vast
                        );
                    }

                    return etpl;
                }
                #endregion
            }
        });
    }


    [HttpGet]
    [Route("lite/flixcdn/stream")]
    async public Task<ActionResult> Stream(string iframe, int t, int s = 0, int e = 0, bool play = false)
    {
        iframe = DecryptQuery(iframe);
        if (string.IsNullOrEmpty(iframe))
            return OnError();

        if (await IsRequestBlocked(rch_check: false))
            return badInitMsg;

        var cache = await InvokeCacheResult<string>(ipkey($"flixcdn:stream:{iframe}:{t}:{s}:{e}"), 10, async result =>
        {
            string file = null;
            string iframeUrl = oninvk.BuildIframeUrl(iframe, t, s, e);

            try
            {
                using (var browser = new Firefox())
                {
                    var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data, headers: init.headers).ConfigureAwait(false);
                    if (page == null)
                        return result.Fail("page");

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (browser.completionSource.Task.IsCompleted ||
                                route.Request.Url.Contains("mc.yandex.ru") ||
                                route.Request.Url.Contains("/videos/"))
                            {
                                await route.AbortAsync();
                                return;
                            }

                            if (route.Request.Url.StartsWith("https://flixcdn.live"))
                            {
                                await route.FulfillAsync(new RouteFulfillOptions
                                {
                                    Body = PlaywrightBase.IframeHtml(iframeUrl)
                                });
                            }
                            else
                            {
                                if (route.Request.Url.Contains("&cuid="))
                                {
                                    await route.ContinueAsync();

                                    var response = await page.WaitForResponseAsync(route.Request.Url);
                                    browser.completionSource.SetResult(response != null
                                        ? await response.TextAsync()
                                        : null);
                                    return;
                                }
                                else
                                {
                                    if (await PlaywrightBase.AbortOrCache(page, route))
                                        return;

                                    await route.ContinueAsync();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "Flixcdn", "id_erdbn91q");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, "https://flixcdn.live/");
                    file = await browser.WaitPageResult().ConfigureAwait(false);
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(file))
                return result.Fail("file", refresh_proxy: true);

            file = file.Replace("\\", "");
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
            hls_manifest_timeout: (int)TimeSpan.FromSeconds(20).TotalMilliseconds,
            httpContext: HttpContext
        ));
    }
}
