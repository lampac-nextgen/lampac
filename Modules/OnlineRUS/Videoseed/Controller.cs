using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Videoseed;

public class VideoseedController : BaseOnlineController
{
    public VideoseedController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/videoseed")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, short year, short s = -1, bool rjson = false, short serial = -1, string voice = null)
    {
        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
            return OnError();

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (string.IsNullOrEmpty(init.token))
            return OnError();

        var cache = await InvokeCacheResult<Data>($"videoseed:view:{serial}:{kinopoisk_id}:{imdb_id}:{original_title}:{year}", TimeSpan.FromHours(4), async e =>
        {
            var data =
                await goSearch(serial, kinopoisk_id > 0, $"&kp={kinopoisk_id}") ??
                await goSearch(serial, !string.IsNullOrEmpty(imdb_id), $"&imdb={HttpUtility.UrlEncode(imdb_id)}") ??
                await goSearch(serial, !string.IsNullOrEmpty(original_title), $"&q={HttpUtility.UrlEncode(original_title)}&release_year_from={year - 1}&release_year_to={year + 1}");

            if (data == null)
                return e.Fail("search_data", refresh_proxy: true);

            if (data?.seasons == null && string.IsNullOrEmpty(data?.iframe))
                return e.Fail("empty_embed", refresh_proxy: true);

            return e.Success(data);
        });

        return ContentTpl(cache, () =>
        {
            if (cache.Value.seasons != null)
            {
                #region Сериал
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);
                string enc_imdb_id = HttpUtility.UrlEncode(imdb_id);
                string serialQuery = $"rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={enc_imdb_id}&title={enc_title}&original_title={enc_original_title}&year={year}&serial=1";

                if (s == -1)
                {
                    var tpl = new SeasonTpl(cache.Value.seasons.Count);

                    foreach (var season in cache.Value.seasons
                        .OrderBy(i => SortNumber(i.Key))
                        .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        tpl.Append(
                            $"{season.Key} сезон",
                            $"{host}/lite/videoseed?{serialQuery}&s={season.Key}",
                            season.Key
                        );
                    }

                    return tpl;
                }
                else
                {
                    var season = cache.Value.seasons
                        .FirstOrDefault(i => i.Key == s.ToString() || SortNumber(i.Key) == s)
                        .Value;

                    var videos = season?.videos;
                    if (videos == null || videos.Count == 0)
                        return default;

                    string seasonLink = $"{host}/lite/videoseed?{serialQuery}&s={s}";
                    var translations = season.translation_iframe?.Count > 0
                        ? season.translation_iframe
                        : cache.Value.translation_iframe;

                    VoiceTpl vtpl = null;
                    string selectedVoice = voice;
                    string activeVoice = selectedVoice;

                    if (string.IsNullOrEmpty(activeVoice))
                    {
                        activeVoice = videos
                            .OrderBy(i => SortNumber(i.Key))
                            .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault()
                            .Value?
                            .short_translation;
                    }

                    if (translations?.Count > 0)
                    {
                        var voices = translations
                            .Select(i => i.Value?.short_name ?? i.Value?.name ?? i.Key)
                            .Where(i => !string.IsNullOrEmpty(i))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (voices.Count > 0)
                        {
                            if (!voices.Any(i => string.Equals(i, activeVoice, StringComparison.OrdinalIgnoreCase)))
                                activeVoice = null;

                            vtpl = new VoiceTpl(voices.Count);

                            foreach (string voiceName in voices)
                            {
                                vtpl.Append(
                                    voiceName,
                                    string.Equals(activeVoice, voiceName, StringComparison.OrdinalIgnoreCase),
                                    $"{seasonLink}&voice={HttpUtility.UrlEncode(voiceName)}"
                                );
                            }
                        }
                    }

                    var etpl = new EpisodeTpl(vtpl, videos.Count);

                    foreach (var video in videos
                        .OrderBy(i => SortNumber(i.Key))
                        .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (video.Value == null || string.IsNullOrEmpty(video.Value.iframe))
                            continue;

                        string selectedIframe = video.Value.iframe;
                        string fallbackVoice = selectedVoice;

                        if (!string.IsNullOrEmpty(selectedVoice) && video.Value.translation_iframe?.Count > 0)
                        {
                            var translation = FindTranslation(video.Value.translation_iframe, selectedVoice);

                            if (!string.IsNullOrEmpty(translation?.iframe))
                            {
                                selectedIframe = translation.iframe;
                                fallbackVoice = null;
                            }
                        }

                        string link = accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(selectedIframe)}");

                        if (!string.IsNullOrEmpty(fallbackVoice))
                            link += $"&voice={HttpUtility.UrlEncode(fallbackVoice)}";

                        string streamlink = link + (link.Contains('?') ? "&play=true" : "?play=true");

                        etpl.Append(
                            $"{video.Key} серия",
                            title ?? original_title,
                            s,
                            video.Key,
                            link + "#.m3u8",
                            "call",
                            streamlink: streamlink,
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
                var mtpl = new MovieTpl(title, original_title, 1);

                if (cache.Value.translation_iframe?.Count > 0)
                {
                    foreach (var translation in cache.Value.translation_iframe)
                    {
                        string translationVoice = translation.Value.short_name;

                        mtpl.Append(
                            translationVoice ?? translation.Value.name ?? translation.Key,
                            accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(cache.Value.iframe)}") + $"&voice={HttpUtility.UrlEncode(translationVoice)}" + "#.m3u8",
                            "call",
                            vast: init.vast
                        );
                    }
                }
                else
                {
                    mtpl.Append(
                        "По-умолчанию",
                        accsArgs($"{host}/lite/videoseed/video/{AesTo.Encrypt(cache.Value.iframe)}") + "#.m3u8",
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
    [Route("lite/videoseed/video/{*iframe}")]
    async public Task<ActionResult> Video(string iframe, string voice, bool play = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        iframe = AesTo.Decrypt(iframe);
        if (string.IsNullOrEmpty(iframe))
            return OnError();

        var cache = await InvokeCacheResult<string>($"videoseed:video:{iframe}:{proxyManager?.CurrentProxyIp}", 20, async e =>
        {
            var headers = httpHeaders(init);

            try
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data, headers: headers?.ToDictionary()).ConfigureAwait(false);
                    if (page == null)
                        return e.Fail("page");

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (route.Request.Url.Contains("videoseed.tv"))
                            {
                                await route.FulfillAsync(new RouteFulfillOptions
                                {
                                    Body = PlaywrightBase.IframeHtml(iframe)
                                });
                            }
                            else if (route.Request.Url == iframe)
                            {
                                string html = null;
                                await route.ContinueAsync();

                                var response = await page.WaitForResponseAsync(route.Request.Url);
                                if (response != null)
                                    html = await response.TextAsync();

                                browser.SetPageResult(html);
                                return;
                            }
                            else
                            {
                                await route.ContinueAsync();
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "Videoseed", "id_m1o18qjn");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, "https://videoseed.tv");

                    string html = await browser.WaitPageResult().ConfigureAwait(false);
                    if (html == null)
                        return e.Fail("wait_page_result", refresh_proxy: true);

                    string file = Regex.Match(html, "Playerjs\\(\"([^\"]+)").Groups[1].Value;
                    if (string.IsNullOrEmpty(file) || file.Length <= 2)
                        return e.Fail("playerjs_file", refresh_proxy: true);

                    string cleaned = Regex.Replace(file.Substring(2), @"\|\|\|[^=\|]+==", string.Empty);
                    if (cleaned.Contains("|||"))
                        cleaned = Regex.Replace(cleaned, @"\|\|\|[^=\|]+==", string.Empty);

                    string json = CrypTo.DecodeBase64(cleaned);
                    if (string.IsNullOrEmpty(json) || !json.Contains(".m3u8"))
                        return e.Fail("json");

                    return e.Success(json);
                }
            }
            catch
            {
                return e.Fail("exception");
            }
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string location;

        if (!string.IsNullOrEmpty(voice))
        {
            location = Regex.Match(
                cache.Value,
                "\\{" + Regex.Escape(voice) + "\\} ?(https?://[^\\;\\{\"\n\r\t ]+\\.m3u8)",
                RegexOptions.IgnoreCase
            ).Groups[1].Value;

            if (string.IsNullOrEmpty(location))
                return OnError("voice_location");
        }
        else
        {
            location = Regex.Match(cache.Value, "(https?://[^\\;\\{\"\n\r\t ]+\\.m3u8)").Groups[1].Value;
        }

        if (string.IsNullOrEmpty(location))
            return OnError("location");

        string referer = Regex.Match(iframe, "(^https?://[^/]+)").Groups[1].Value;
        var headers_stream = httpHeaders(init.host, HeadersModel.JoinReadOnly(HeadersModel.Init("referer", referer), init.headers_stream));
        string stream = HostStreamProxy(location, headers: headers_stream);

        if (play)
            return RedirectToPlay(stream);

        return ContentTo(VideoTpl.ToJson(
            "play",
            stream,
            "auto",
            vast: init.vast,
            httpContext: HttpContext
        ));
    }
    #endregion

    #region goSearch
    async Task<Data> goSearch(short serial, bool isOk, string arg)
    {
        if (!isOk)
            return null;

        var root = await httpHydra.Get<Root>($"{init.apihost}/apiv2.php?item={(serial == 1 ? "serial" : "movie")}&token={init.token}" + arg, safety: true);

        if (root?.data == null || root.data.Count == 0 || root.status == "error")
        {
            proxyManager?.Refresh();
            return null;
        }

        bool exactId = arg.StartsWith("&kp=", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("&imdb=", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("&tmdb=", StringComparison.OrdinalIgnoreCase);

        if (serial == 1 && exactId && root.data.Count > 1)
            return MergeSerialData(root.data);

        return root.data.FirstOrDefault();
    }
    #endregion

    #region SerialData
    static Data MergeSerialData(List<Data> items)
    {
        if (items == null || items.Count == 0)
            return null;

        if (items.Count == 1)
            return items[0];

        var merged = new Data
        {
            seasons = new Dictionary<string, Season>(StringComparer.OrdinalIgnoreCase),
            translation_iframe = new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var item in items)
        {
            if (item == null)
                continue;

            if (string.IsNullOrEmpty(merged.iframe) && !string.IsNullOrEmpty(item.iframe))
                merged.iframe = item.iframe;

            if (item.total_videos > merged.total_videos)
                merged.total_videos = item.total_videos;

            MergeTranslations(merged.translation_iframe, item.translation_iframe);

            if (item.seasons == null)
                continue;

            foreach (var seasonPair in item.seasons)
            {
                if (seasonPair.Value == null)
                    continue;

                if (!merged.seasons.TryGetValue(seasonPair.Key, out var targetSeason))
                {
                    targetSeason = new Season
                    {
                        total_videos = seasonPair.Value.total_videos,
                        videos = new Dictionary<string, Episode>(StringComparer.OrdinalIgnoreCase),
                        translation_iframe = new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase)
                    };

                    merged.seasons[seasonPair.Key] = targetSeason;
                }
                else if (seasonPair.Value.total_videos > targetSeason.total_videos)
                {
                    targetSeason.total_videos = seasonPair.Value.total_videos;
                }

                MergeTranslations(targetSeason.translation_iframe, seasonPair.Value.translation_iframe);

                if (seasonPair.Value.videos == null)
                    continue;

                foreach (var videoPair in seasonPair.Value.videos)
                {
                    if (videoPair.Value == null)
                        continue;

                    if (!targetSeason.videos.TryGetValue(videoPair.Key, out var targetEpisode) || targetEpisode == null)
                    {
                        targetSeason.videos[videoPair.Key] = videoPair.Value;
                        continue;
                    }

                    MergeEpisode(targetEpisode, videoPair.Value);
                }
            }
        }

        if (merged.seasons.Count == 0)
            merged.seasons = null;

        if (merged.translation_iframe.Count == 0)
            merged.translation_iframe = null;

        return merged;
    }

    static void MergeEpisode(Episode target, Episode source)
    {
        if (target == null || source == null)
            return;

        AddEpisodeTranslation(target, target);
        AddEpisodeTranslation(target, source);

        if (string.IsNullOrEmpty(target.iframe) && !string.IsNullOrEmpty(source.iframe))
            target.iframe = source.iframe;

        if (string.IsNullOrEmpty(target.translations_id) && !string.IsNullOrEmpty(source.translations_id))
            target.translations_id = source.translations_id;

        if (string.IsNullOrEmpty(target.short_translation) && !string.IsNullOrEmpty(source.short_translation))
            target.short_translation = source.short_translation;

        if (source.translation_iframe?.Count > 0)
        {
            target.translation_iframe ??= new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase);
            MergeTranslations(target.translation_iframe, source.translation_iframe);
        }
    }

    static void AddEpisodeTranslation(Episode target, Episode source)
    {
        if (target == null || source == null || string.IsNullOrEmpty(source.iframe))
            return;

        string voiceName = source.short_translation;
        if (string.IsNullOrWhiteSpace(voiceName))
            return;

        target.translation_iframe ??= new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase);

        if (!target.translation_iframe.ContainsKey(voiceName))
        {
            target.translation_iframe[voiceName] = new Translation
            {
                name = voiceName,
                short_name = voiceName,
                iframe = source.iframe
            };
        }
    }

    static void MergeTranslations(Dictionary<string, Translation> target, Dictionary<string, Translation> source)
    {
        if (target == null || source == null)
            return;

        foreach (var pair in source)
        {
            if (pair.Value == null)
                continue;

            if (!target.TryGetValue(pair.Key, out var current) || current == null)
            {
                target[pair.Key] = pair.Value;
                continue;
            }

            if (string.IsNullOrEmpty(current.name) && !string.IsNullOrEmpty(pair.Value.name))
                current.name = pair.Value.name;

            if (string.IsNullOrEmpty(current.short_name) && !string.IsNullOrEmpty(pair.Value.short_name))
                current.short_name = pair.Value.short_name;

            if (string.IsNullOrEmpty(current.iframe) || pair.Value.count > current.count)
            {
                if (!string.IsNullOrEmpty(pair.Value.iframe))
                    current.iframe = pair.Value.iframe;
            }

            if (pair.Value.count > current.count)
                current.count = pair.Value.count;
        }
    }

    static Translation FindTranslation(Dictionary<string, Translation> translations, string voice)
    {
        if (translations == null || string.IsNullOrEmpty(voice))
            return null;

        return translations
            .FirstOrDefault(i => string.Equals(
                i.Value?.short_name ?? i.Value?.name ?? i.Key,
                voice,
                StringComparison.OrdinalIgnoreCase
            ))
            .Value;
    }
    #endregion

    static int SortNumber(string value)
    {
        if (int.TryParse(value, out int number))
            return number;

        var match = Regex.Match(value ?? string.Empty, "[0-9]+");
        return match.Success && int.TryParse(match.Value, out number)
            ? number
            : int.MaxValue;
    }
}
