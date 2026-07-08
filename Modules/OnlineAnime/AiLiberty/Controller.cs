using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Text.Json;

namespace AiLiberty;

public class AiLibertyController : BaseOnlineController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public AiLibertyController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/ailiberty")]
    async public Task<ActionResult> Index(string title, string uri, string s, bool rjson = false, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        if (string.IsNullOrEmpty(uri))
        {
            if (string.IsNullOrWhiteSpace(title))
                return OnError();

            #region Поиск
            var cache = await InvokeCacheResult<List<(string title, string year, string uri, string img)>>($"ailiberty:search:{title}", TimeSpan.FromHours(4), async e =>
            {
                var catalog = new List<(string title, string year, string uri, string img)>();

                string searchUrl = $"{init.host}/?search={HttpUtility.UrlEncode(title)}";

                await httpHydra.GetSpan(searchUrl, html =>
                {
                    var matches = Regex.Matches(html.ToString(), "<a[^>]+href=\"https?://[^/]+/releases/([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
                    foreach (Match m in matches)
                    {
                        string uriId = m.Groups[1].Value;
                        string innerHtml = m.Groups[2].Value;

                        string t = Regex.Match(innerHtml, "<h3[^>]*>([^<]+)</h3>", RegexOptions.Singleline).Groups[1].Value.Trim();
                        string img = Regex.Match(innerHtml, "<img[^>]+src=\"([^\"]+)\"", RegexOptions.Singleline).Groups[1].Value;
                        if (!img.StartsWith("http") && !string.IsNullOrEmpty(img))
                            img = init.host + (img.StartsWith("/") ? img : "/" + img);

                        string year = Regex.Match(innerHtml, "<div[^>]+text-gray-500[^>]*>.*?([0-9]{4}).*?</div>", RegexOptions.Singleline).Groups[1].Value;

                        if (!string.IsNullOrEmpty(t))
                        {
                            catalog.Add((t, year, uriId, img));
                        }
                    }
                });

                if (catalog.Count == 0)
                    return e.Fail("catalog");

                return e.Success(catalog);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (cache.Value != null && cache.Value.Count == 0)
                return OnError();

            if (!similar && cache.Value != null && cache.Value.Count == 1)
                return LocalRedirect(accsArgs($"/lite/ailiberty?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&uri={HttpUtility.UrlEncode(cache.Value[0].uri)}"));

            return ContentTpl(cache, () =>
            {
                var stpl = new SimilarTpl(cache.Value.Count);

                foreach (var res in cache.Value)
                {
                    stpl.Append(
                        res.title,
                        res.year,
                        string.Empty,
                        $"{host}/lite/ailiberty?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&uri={HttpUtility.UrlEncode(res.uri)}",
                        PosterApi.Size(res.img)
                    );
                }

                return stpl;
            });
            #endregion
        }
        else
        {
            #region Серии
            var cache = await InvokeCacheResult<List<PlayerJsItem>>($"ailiberty:playlist:{uri}", TimeSpan.FromSeconds(0), async e =>
            {
                List<PlayerJsItem> items = null;

                await httpHydra.GetSpan($"{init.host}/releases/{uri}", html =>
                {
                    var match = Regex.Match(html.ToString(), "file\\s*:\\s*(\\[.*?\\])\\s*,?\\s*default_quality", RegexOptions.Singleline);
                    if (!match.Success)
                        match = Regex.Match(html.ToString(), "file\\s*:\\s*(\\[.*?\\])\\s*\\}", RegexOptions.Singleline);

                    if (match.Success)
                    {
                        try
                        {
                            items = JsonSerializer.Deserialize<List<PlayerJsItem>>(match.Groups[1].Value);
                        }
                        catch { }
                    }
                });

                if (items == null || items.Count == 0)
                    return e.Fail("episodes");

                return e.Success(items);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (cache?.Value == null || cache.Value.Count == 0)
                return OnError("empty cache");

            return ContentTpl(cache, () =>
            {
                var etpl = new EpisodeTpl(cache.Value.Count);

                foreach (var episode in cache.Value)
                {
                    string name = string.IsNullOrEmpty(episode.title) ? "Серия" : episode.title;
                    string episodeNumber = Regex.Match(name, "([0-9]+)").Groups[1].Value;
                    if (string.IsNullOrEmpty(episodeNumber))
                        episodeNumber = "1";

                    var streams = new StreamQualityTpl();
                    int index = cache.Value.IndexOf(episode);

                    // Parse stream qualities just to know which ones exist
                    var qualities = Regex.Matches(episode.file, "\\[(360p|480p|720p|1080p)\\]([^\\[\\,]+)");
                    var qDict = new Dictionary<string, bool>();
                    foreach (Match q in qualities)
                        qDict[q.Groups[1].Value] = true;

                    string pLink(string q) => accsArgs($"{host}/lite/ailiberty/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&index={index}&q={q}");

                    if (qDict.ContainsKey("1080p")) streams.Append(pLink("1080p"), "1080p");
                    if (qDict.ContainsKey("720p")) streams.Append(pLink("720p"), "720p");
                    if (qDict.ContainsKey("480p")) streams.Append(pLink("480p"), "480p");
                    if (qDict.ContainsKey("360p")) streams.Append(pLink("360p"), "360p");

                    var first = streams.Firts();
                    if (first != null)
                    {
                        etpl.Append(
                            name,
                            title,
                            s ?? "1",
                            episodeNumber,
                            first.link,
                            streamquality: streams
                        );
                    }
                }

                return etpl;
            });
            #endregion
        }
    }

    #region Video
    [HttpGet, Staticache(manually: true)]
    [Route("lite/ailiberty/video.m3u8")]
    async public Task<ActionResult> Video(string uri, int index, string q)
    {
        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        string qLink = null;
        await httpHydra.GetSpan($"{init.host}/releases/{uri}", html =>
        {
            var match = Regex.Match(html.ToString(), "file\\s*:\\s*(\\[.*?\\])\\s*,?\\s*default_quality", RegexOptions.Singleline);
            if (!match.Success)
                match = Regex.Match(html.ToString(), "file\\s*:\\s*(\\[.*?\\])\\s*\\}", RegexOptions.Singleline);

            if (match.Success)
            {
                try
                {
                    var items = JsonSerializer.Deserialize<List<PlayerJsItem>>(match.Groups[1].Value);
                    if (items != null && items.Count > index)
                    {
                        var episode = items[index];
                        var qualities = Regex.Matches(episode.file, "\\[(360p|480p|720p|1080p)\\]([^\\[\\,]+)");
                        foreach (Match mq in qualities)
                        {
                            if (mq.Groups[1].Value == q)
                            {
                                qLink = mq.Groups[2].Value;
                                break;
                            }
                        }
                        if (string.IsNullOrEmpty(qLink) && qualities.Count > 0)
                            qLink = qualities[0].Groups[2].Value;
                    }
                }
                catch { }
            }
        });

        if (string.IsNullOrEmpty(qLink))
            return OnError("Stream not found");

        if (!qLink.StartsWith("http"))
            qLink = init.host + (qLink.StartsWith("/") ? qLink : "/" + qLink);

        string link = HostStreamProxy(qLink);

        return RedirectToPlay(link);
    }
    #endregion
}
