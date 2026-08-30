using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Vibix;

public class VibixController : BaseOnlineController
{
    public VibixController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vibix")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, short s = -1, bool rjson = false, string voice = null)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (string.IsNullOrEmpty(imdb_id) && kinopoisk_id == 0)
            return OnError();

        var cache = await InvokeCacheResult<List<Item>>(ipkey($"vibix:{imdb_id}:{kinopoisk_id}"), 20, async e =>
        {
            string json = await black_magic(imdb_id, kinopoisk_id);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            List<Item> root = null;

            try
            {
                root = JsonConvert.DeserializeObject<List<Item>>(json);
            }
            catch { }

            if (root == null || root.Count == 0)
                return e.Fail("root", refresh_proxy: true);

            return e.Success(root);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        if (cache.Value.First().file != null)
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, 1);

            foreach (var movie in cache.Value)
            {
                if (movie.voices == null)
                {
                    movie.voices = new Dictionary<string, List<StreamQualityDto>>();

                    foreach (Match qualityMatch in Regex.Matches(movie.file, @"\[(?<q>480|720|1080)p\](?<items>.*?)(?=,\[(?:480|720|1080)p\]|$)", RegexOptions.Singleline))
                    {
                        string items = qualityMatch.Groups["items"].Value;

                        foreach (Match voiceMatch in Regex.Matches(items, @"\{(?<voice>[^}]+)\}(?<file>https?://[^,\t\[\;{ ]+)", RegexOptions.Singleline))
                        {
                            string movieVoice = voiceMatch.Groups["voice"].Value;
                            string file = voiceMatch.Groups["file"].Value;

                            if (!movie.voices.TryGetValue(movieVoice, out var streams))
                            {
                                streams = new List<StreamQualityDto>();
                                movie.voices[movieVoice] = streams;
                            }

                            streams.Insert(0, new StreamQualityDto(
                                $"{host}/lite/vibix/video.m3u8?id={EncryptQuery(file)}",
                                qualityMatch.Groups["q"].Value + "p"
                            ));
                        }
                    }
                }

                if (movie.voices.Count == 0)
                    continue;

                foreach (var v in movie.voices)
                {
                    if (v.Value.Count > 0)
                    {
                        mtpl.Append(
                            v.Key ?? movie.title,
                            accsArgs(v.Value[0].link),
                            streamquality: new StreamQualityTpl(v.Value, linkPredicate: accsArgs),
                            vast: init.vast
                        );
                    }
                }
            }

            return ContentTpl(mtpl);
            #endregion
        }
        else
        {
            #region Сериал
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);
            string enc_imdb_id = HttpUtility.UrlEncode(imdb_id);
            string serialQuery = $"rjson={rjson}&kinopoisk_id={kinopoisk_id}&imdb_id={enc_imdb_id}&title={enc_title}&original_title={enc_original_title}";

            if (s == -1)
            {
                var tpl = new SeasonTpl(cache.Value.Count);

                foreach (var season in cache.Value)
                {
                    if (int.TryParse(Regex.Match(season.title, "([0-9]+)$").Groups[1].Value, out int _s) && _s > 0)
                    {
                        tpl.Append(
                            $"{_s} сезон",
                            $"{host}/lite/vibix?{serialQuery}&s={_s}",
                            _s
                        );
                    }
                }

                return ContentTpl(tpl);
            }
            else
            {
                var season = cache.Value.FirstOrDefault(i => i.title?.EndsWith($" {s}") == true);
                var episodes = season?.folder?
                    .Where(i => i != null)
                    .OrderBy(i => SortNumber(i.title))
                    .ThenBy(i => i.title, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (episodes == null || episodes.Count == 0)
                    return ContentTpl(new EpisodeTpl());

                foreach (var episode in episodes)
                    PrepareEpisode(episode);

                var voices = new List<string>();
                var seenVoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var episode in episodes)
                {
                    if (episode.voices == null)
                        continue;

                    foreach (string voiceName in episode.voices.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(voiceName) && seenVoices.Add(voiceName))
                            voices.Add(voiceName);
                    }
                }

                string selectedVoice = voice;
                string activeVoice = selectedVoice;

                if (string.IsNullOrEmpty(activeVoice))
                    activeVoice = GetEpisodeDefaultVoice(episodes.FirstOrDefault());

                if (!string.IsNullOrEmpty(activeVoice) && !seenVoices.Contains(activeVoice))
                    activeVoice = null;

                VoiceTpl vtpl = null;
                string seasonLink = $"{host}/lite/vibix?{serialQuery}&s={s}";

                if (voices.Count > 0)
                {
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

                var etpl = new EpisodeTpl(vtpl, episodes.Count);

                foreach (var episode in episodes)
                {
                    List<StreamQualityDto> streams = episode.streams;

                    if (!string.IsNullOrEmpty(selectedVoice))
                    {
                        if (episode.voices == null ||
                            !episode.voices.TryGetValue(selectedVoice, out streams) ||
                            streams == null ||
                            streams.Count == 0)
                        {
                            continue;
                        }
                    }

                    if (string.IsNullOrEmpty(episode.title) || streams == null || streams.Count == 0)
                        continue;

                    etpl.Append(
                        episode.title,
                        title ?? original_title,
                        s,
                        Regex.Match(episode.title, "([0-9]+)").Groups[1].Value,
                        accsArgs(streams[0].link),
                        streamquality: new StreamQualityTpl(streams, linkPredicate: accsArgs),
                        vast: init.vast
                    );
                }

                return ContentTpl(etpl);
            }
            #endregion
        }
    }


    #region Video
    [HttpGet, Staticache(manually: true)]
    [Route("lite/vibix/video.m3u8")]
    async public Task<ActionResult> Video(string id)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        string uri = DecryptQuery(id);
        if (string.IsNullOrEmpty(uri))
            return OnError();

        string origin = Regex.Match(uri, "^(https?://[^/]+)").Groups[1].Value;

        var headers = HeadersModel.Init(
            ("accept", "*/*"),
            ("origin", origin),
            ("referer", $"{origin}/"),
            ("sec-fetch-dest", "empty"),
            ("sec-fetch-mode", "cors"),
            ("sec-fetch-site", "same-site")
        );

        JObject root = await InvokeCache(ipkey($"vibix:{uri}"), 20, async
            () => await httpHydra.Get<JObject>(uri, addheaders: headers)
        );

        if (!root.TryGetValue("p", out JToken pToken) || !root.TryGetValue("v", out JToken vToken))
            return OnError();

        int version = vToken.Value<int>();
        string payload = pToken.Value<string>();

        if (string.IsNullOrEmpty(payload) || version != 1)
            return OnError();

        string data = new string(payload.Reverse().ToArray());
        byte[] decoded = Convert.FromBase64String(PadBase64(data));

        const string ApiDecoderKey = "RySdvcyu5iTUxn97vn4HwoniwgxaCynA";

        byte[] key = Encoding.ASCII.GetBytes(ApiDecoderKey);
        for (int i = 0; i < decoded.Length; i++)
            decoded[i] = (byte)(decoded[i] ^ key[i % key.Length]);

        string m3u8 = Encoding.UTF8.GetString(decoded);
        m3u8 = Regex.Replace(m3u8, "(https://[^\n\r]+)", u => HostStreamProxy(u.Value, headers));

        return Content(m3u8, "application/vnd.apple.mpegurl");
    }
    #endregion

    #region EpisodeVoices
    void PrepareEpisode(Item episode)
    {
        if (episode == null || (episode.streams != null && episode.voices != null))
            return;

        episode.voices = new Dictionary<string, List<StreamQualityDto>>(StringComparer.OrdinalIgnoreCase);

        var sources = episode.folder?
            .Where(i => i != null && !string.IsNullOrWhiteSpace(i.file))
            .ToList();

        if (sources?.Count > 0)
        {
            Item nativeSource = sources[0];

            foreach (var source in sources)
            {
                var inlineVoices = ExtractVoices(source.file);

                if (inlineVoices.Count > 0)
                {
                    foreach (string voiceName in inlineVoices)
                        AddVoice(episode.voices, voiceName, BuildStreams(source.file, voiceName));
                }
                else if (!string.IsNullOrWhiteSpace(source.title))
                {
                    AddVoice(episode.voices, source.title.Trim(), BuildStreams(source.file));
                }
            }

            string taggedDefault = GetDefaultVoice(nativeSource.file);
            episode.streams = BuildStreams(nativeSource.file, taggedDefault);
        }
        else
        {
            var inlineVoices = ExtractVoices(episode.file);

            foreach (string voiceName in inlineVoices)
                AddVoice(episode.voices, voiceName, BuildStreams(episode.file, voiceName));

            string taggedDefault = GetDefaultVoice(episode.file);
            episode.streams = BuildStreams(episode.file, taggedDefault);
        }
    }

    static string GetEpisodeDefaultVoice(Item episode)
    {
        if (episode == null)
            return null;

        var nativeSource = episode.folder?
            .FirstOrDefault(i => i != null && !string.IsNullOrWhiteSpace(i.file));

        if (nativeSource != null)
        {
            string taggedDefault = GetDefaultVoice(nativeSource.file);
            return !string.IsNullOrEmpty(taggedDefault)
                ? taggedDefault
                : nativeSource.title?.Trim();
        }

        return GetDefaultVoice(episode.file);
    }

    List<StreamQualityDto> BuildStreams(string file, string voice = null)
    {
        var streams = new List<StreamQualityDto>(3);

        if (string.IsNullOrWhiteSpace(file))
            return streams;

        foreach (string q in new string[] { "1080", "720", "480" })
        {
            Match qualityMatch = Regex.Match(
                file,
                $@"\[{q}p?\](?<items>.*?)(?=,\[(?:480|720|1080)p?\]|$)",
                RegexOptions.Singleline
            );

            if (!qualityMatch.Success)
                continue;

            string items = qualityMatch.Groups["items"].Value;
            Match selected;

            if (!string.IsNullOrEmpty(voice))
            {
                selected = Regex.Match(
                    items,
                    @"\{" + Regex.Escape(voice) + @"\}(?<file>https?://[^,\t\[\;{ ]+)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                );
            }
            else
            {
                selected = Regex.Match(
                    items,
                    @"(?:\{[^}]+\})?(?<file>https?://[^,\t\[\;{ ]+)",
                    RegexOptions.Singleline
                );
            }

            if (!selected.Success)
                continue;

            streams.Add(new StreamQualityDto(
                $"{host}/lite/vibix/video.m3u8?id={EncryptQuery(selected.Groups["file"].Value)}",
                $"{q}p"
            ));
        }

        return streams;
    }

    static List<string> ExtractVoices(string file)
    {
        var voices = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(file))
            return voices;

        foreach (Match qualityMatch in Regex.Matches(
            file,
            @"\[(?:480|720|1080)p?\](?<items>.*?)(?=,\[(?:480|720|1080)p?\]|$)",
            RegexOptions.Singleline))
        {
            foreach (Match voiceMatch in Regex.Matches(
                qualityMatch.Groups["items"].Value,
                @"\{(?<voice>[^}]+)\}(?<file>https?://[^,\t\[\;{ ]+)",
                RegexOptions.Singleline))
            {
                string voiceName = voiceMatch.Groups["voice"].Value.Trim();

                if (!string.IsNullOrEmpty(voiceName) && seen.Add(voiceName))
                    voices.Add(voiceName);
            }
        }

        return voices;
    }

    static string GetDefaultVoice(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;

        Match qualityMatch = Regex.Match(
            file,
            @"\[(?:480|720|1080)p?\](?<items>.*?)(?=,\[(?:480|720|1080)p?\]|$)",
            RegexOptions.Singleline
        );

        if (!qualityMatch.Success)
            return null;

        Match firstStream = Regex.Match(
            qualityMatch.Groups["items"].Value,
            @"(?:\{(?<voice>[^}]+)\})?(?<file>https?://[^,\t\[\;{ ]+)",
            RegexOptions.Singleline
        );

        if (!firstStream.Success)
            return null;

        string voiceName = firstStream.Groups["voice"].Value.Trim();
        return string.IsNullOrEmpty(voiceName) ? null : voiceName;
    }

    static void AddVoice(Dictionary<string, List<StreamQualityDto>> voices, string voiceName, List<StreamQualityDto> streams)
    {
        if (string.IsNullOrWhiteSpace(voiceName) || streams == null || streams.Count == 0)
            return;

        if (!voices.ContainsKey(voiceName))
            voices[voiceName] = streams;
    }
    #endregion


    #region black_magic
    async Task<string> black_magic(string imdb_id, long kinopoisk_id)
    {
        try
        {
            using (var browser = new PlaywrightBrowser(init.priorityBrowser))
            {
                var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data, headers: init.headers).ConfigureAwait(false);
                if (page == null)
                    return null;

                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        if (route.Request.Url.StartsWith("https://coldfilm.ink"))
                        {
                            string target = kinopoisk_id > 0
                                ? $"data-type=\"kp\" data-id=\"{kinopoisk_id}\""
                                : $"data-type=\"imdb\" data-id=\"{imdb_id}\"";

                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                Body = $@"<html lang=""ru"">
                                    <head>
                                        <meta charset=""UTF-8"">
                                        <script src=""https://graphicslab.io/sdk/v2/rendex-sdk.min.js""></script>
                                    </head>
                                    <body>
                                        <ins data-publisher-id=""674784070"" {target}></ins>
                                    </body>
                                </html>"
                            });
                        }
                        else
                        {
                            if (route.Request.Url.Contains("/embed.js"))
                            {
                                await route.FulfillAsync(new RouteFulfillOptions
                                {
                                    Body = System.IO.File.ReadAllText($"{ModInit.path}/embed.js")
                                });
                            }
                            else
                            {
                                if (!Regex.IsMatch(route.Request.Url, "(kinescopecdn|graphicslab|coldfilm)\\.") ||
                                    route.Request.Url.Contains("/index.m3u8"))
                                {
                                    await route.AbortAsync();
                                    return;
                                }

                                if (await PlaywrightBase.AbortOrCache(page, route))
                                    return;

                                await route.ContinueAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex, "{Class} {CatchId}", "Vibix", "id_t2yqc1oa");
                    }
                });

                PlaywrightBase.GotoAsync(page, "https://coldfilm.ink/");

                var frame = page.FrameLocator("iframe[src*='kinescopecdn.net']");

                await frame.Locator("#playerjsfile").WaitForAsync(new()
                {
                    Timeout = 10000
                });

                return await frame.Locator("#playerjsfile").TextContentAsync();
            }
        }
        catch { return null; }
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

    static string PadBase64(string value)
    {
        int mod = value.Length % 4;
        if (mod == 0)
            return value;

        return value + new string('=', 4 - mod);
    }
}
