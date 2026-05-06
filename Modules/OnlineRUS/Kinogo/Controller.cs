using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Shared;
using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services.RxEnumerate;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Kinogo;

public class KinogoController : BaseOnlineController
{
    public KinogoController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("lite/kinogo")]
    async public Task<ActionResult> Index(string title, string original_title, int year, bool rjson, string href, bool similar, int s = -1, int t = -1)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        #region search
        if (string.IsNullOrEmpty(href))
        {
            if (string.IsNullOrEmpty(title))
                return OnError("search params");

            reset_search:
            var search = await InvokeCacheResult<SearchModel>($"kinogo:search:{title}:{year}", TimeSpan.FromHours(4), async e =>
            {
                string search = rch?.enable == true
                    ? await rch.Get($"{init.host}/search/{title}")
                    : await PlaywrightBrowser.Get(init, $"{init.host}/search/{title}", proxy: proxy_data);

                var result = SearchResult(search, title, year);

                if (result == null)
                    return e.Fail("search-result", refresh_proxy: true);

                return e.Success(result);
            });

            if (similar || string.IsNullOrEmpty(search.Value?.link))
                return ContentTpl(search, () => search.Value.similar);

            if (string.IsNullOrEmpty(search.Value?.link))
            {
                if (IsRhubFallback(search))
                    goto reset_search;

                return OnError();
            }

            href = search.Value?.link;
        }
        #endregion

        if (string.IsNullOrEmpty(href))
            return OnError("href");

        #region embed
        reset_embed:

        var cache = await InvokeCacheResult<List<PlaylistItem>>(ipkey(href), 20, async e =>
        {
            string embedUrl = null;
            string targetHref = $"{init.host}/{href}";

            string iframe = rch?.enable == true
               ? await rch.Get(init.cors(targetHref))
               : await PlaywrightBrowser.Get(init, init.cors(targetHref));

            if (iframe == null)
                return e.Fail("iframe", refresh_proxy: true);

            string iframeUri = Regex.Match(iframe, "<iframe [^>]+data-src=\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrEmpty(iframeUri))
                return e.Fail("iframeUri", refresh_proxy: true);

            embedUrl = iframeUri.StartsWith("//") ? $"https:{iframeUri}" : iframeUri;

            if (string.IsNullOrEmpty(embedUrl))
                return e.Fail("embedUrl", refresh_proxy: true);

            var embedHeaders = httpHeaders(init, HeadersModel.Init("referer", targetHref));

            string embedHtml = rch?.enable == true
                ? await rch.Get(init.cors(embedUrl), embedHeaders)
                : await PlaywrightBrowser.Get(init, init.cors(embedUrl), headers: embedHeaders, proxy_data);

            if (embedHtml == null)
                return e.Fail("embed");

            string fileEncode = Regex.Match(embedHtml, "\"file\":\"([^\"]+)\"").Groups[1].Value;
            if (string.IsNullOrEmpty(fileEncode))
                return e.Fail("fileEncode");

            string playlistJson = await DecodeFile(init, fileEncode);
            if (string.IsNullOrEmpty(playlistJson))
                return e.Fail("playlistJson");

            try
            {
                var playlist = JsonConvert.DeserializeObject<List<PlaylistItem>>(playlistJson);
                if (playlist == null || playlist.Count == 0)
                    return e.Fail("playlist");

                return e.Success(playlist);
            }
            catch
            {
                return e.Fail("DeserializeObject");
            }
        });

        if (IsRhubFallback(cache))
            goto reset_embed;
        #endregion

        return ContentTpl(cache,
            () => BuildResult(cache.Value, title, original_title, year, s, t, rjson, href)
        );
    }


    #region BuildResult
    ITplResult BuildResult(List<PlaylistItem> playlist, string title, string original_title, int year, int s, int t, bool rjson, string href)
    {
        if (!string.IsNullOrEmpty(playlist.FirstOrDefault()?.file))
        {
            var mtpl = new MovieTpl(title, original_title);

            foreach (var source in playlist)
            {
                string voice = source.title;
                string file = source.file;

                if (string.IsNullOrEmpty(voice) || string.IsNullOrEmpty(file))
                    continue;

                if (file.StartsWith("//"))
                    file = "https:" + file;

                #region subtitle
                var subtitles = new SubtitleTpl();
                string _subs = source.subtitle;
                if (!string.IsNullOrEmpty(_subs))
                {
                    var match = new Regex("\\[([^\\]]+)\\]([^\\[\\,]+)").Match(_subs);
                    while (match.Success)
                    {
                        string srt = match.Groups[2].Value;
                        if (srt.StartsWith("//"))
                            srt = "https:" + srt;

                        subtitles.Append(match.Groups[1].Value, HostStreamProxy(srt));
                        match = match.NextMatch();
                    }
                }
                #endregion

                mtpl.Append(
                    Regex.Replace(voice, "<[^>]+>", ""),
                    HostStreamProxy(file),
                    subtitles: subtitles,
                    vast: init.vast
                );
            }

            return mtpl;
        }
        else
        {
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);
            string enc_href = HttpUtility.UrlEncode(href);

            if (s == -1)
            {
                var tpl = new SeasonTpl(playlist.Count);
                foreach (var season in playlist)
                {
                    string _s = Regex.Match(season.title ?? string.Empty, " ([0-9]+)$").Groups[1].Value;
                    if (!string.IsNullOrEmpty(_s))
                    {
                        tpl.Append(
                            $"{_s} сезон",
                            $"{host}/lite/kinogo?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&year={year}&href={enc_href}&s={_s}",
                            _s
                        );
                    }
                }

                return tpl;
            }
            else
            {
                var episodes = playlist.First(i => (i.title ?? string.Empty).EndsWith($" {s}")).folder;

                #region Перевод
                var vtpl = new VoiceTpl();
                var hashSet = new HashSet<int>();

                foreach (var episode in episodes)
                {
                    if (episode.folder == null)
                        continue;

                    foreach (var voice in episode.folder)
                    {
                        int voice_id = voice.voice_id;
                        if (hashSet.Add(voice_id))
                        {
                            if (t == -1)
                                t = voice_id;

                            vtpl.Append(
                                voice.title,
                                t == voice_id,
                                $"{host}/lite/kinogo?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&year={year}&href={enc_href}&s={s}&t={voice_id}"
                            );
                        }
                    }
                }
                #endregion

                var etpl = new EpisodeTpl(vtpl, episodes.Count);

                foreach (var episode in episodes)
                {
                    string name = episode.title;
                    string file = episode.folder?.FirstOrDefault(i => i.voice_id == t)?.file;

                    if (string.IsNullOrEmpty(file))
                        continue;

                    if (file.StartsWith("//"))
                        file = "https:" + file;

                    #region subtitle
                    var subtitles = new SubtitleTpl();
                    string _subs = episode.subtitle;

                    if (!string.IsNullOrEmpty(_subs))
                    {
                        var match = new Regex("\\[([^\\]]+)\\]([^\\[\\,]+)").Match(_subs);
                        while (match.Success)
                        {
                            string srt = match.Groups[2].Value;
                            if (srt.StartsWith("//"))
                                srt = "https:" + srt;

                            subtitles.Append(match.Groups[1].Value, HostStreamProxy(srt));
                            match = match.NextMatch();
                        }
                    }
                    #endregion

                    string stream = HostStreamProxy(file);
                    etpl.Append(
                        name,
                        title ?? original_title,
                        s.ToString(),
                        Regex.Match(name,
                        " ([0-9]+)$").Groups[1].Value,
                        stream,
                        subtitles: subtitles,
                        vast: init.vast
                    );
                }

                return etpl;
            }
        }
    }
    #endregion

    #region SearchResult
    SearchModel SearchResult(ReadOnlySpan<char> html, string title, int year)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Matches("<div id=\"[0-9]+\" class=\"shortstory\">(.*?)<div class=\"shortstory__meta\">", html, 0, RegexOptions.Singleline);
        if (rx.Count == 0)
            return null;

        string stitle = StringConvert.SearchName(title);

        var similar = new SimilarTpl(rx.Count);

        string link = null;

        foreach (var row in rx.Rows())
        {
            string href = row.Match("<a href=\"https?://[^/]+/([^\"#]+)");
            if (string.IsNullOrEmpty(href))
                continue;

            string name = row.Match("<h2>([^<]+)</h2>");
            if (string.IsNullOrEmpty(name))
                continue;

            string blockYear = row.Match("Год выпуска:</b><a[^>]*>([0-9]{4})");

            string img = row.Match("<img\\s+data-src=\"([^\"]+)\"");
            if (!string.IsNullOrEmpty(img))
                img = ModInit.conf.host + img;

            string uri = $"{host}/lite/kinogo?href={HttpUtility.UrlEncode(href)}";
            similar.Append(name, blockYear, string.Empty, uri, PosterApi.Size(img));

            if (StringConvert.SearchName(name).Contains(stitle) && blockYear == year.ToString())
                link = href;
        }

        if (string.IsNullOrEmpty(link) && similar.IsEmpty)
            return null;

        return new SearchModel()
        {
            link = link,
            similar = similar
        };
    }
    #endregion

    #region DecodeFile
    async Task<string> DecodeFile(OnlinesSettings init, string fileEncode)
    {
        try
        {
            using (var browser = new PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin).ConfigureAwait(false);
                if (page == null)
                    return null;

                var html = $@"<!doctype html>
                        <html lang=""ru"">
                          <body>
                            <script>
                              {ModInit.playerjs}
                              Cinemar({{""file"":""{fileEncode}""}})
                            </script>
                          </body>
                        </html>";

                await page.SetContentAsync(html).ConfigureAwait(false);

                return await page.EvaluateAsync<string>("() => JSON.stringify(window.playlist)");
            }
        }
        catch { return null; }
    }
    #endregion
}
