using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Services.Utilities;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace NinePeso;

public class NinePesoController : BaseOnlineController
{
    public NinePesoController() : base(ModInit.conf) { }

    HttpHydra _siteHttp;

    // Site pages and the availability probe need a shorter HTTP budget
    // than large API metadata responses. Use a request-local settings clone.
    HttpHydra siteHttp
    {
        get
        {
            if (_siteHttp == null)
            {
                var settings = init.Clone();
                settings.httptimeout = 8;
                _siteHttp = new HttpHydra(settings, httpHeaders(settings),
                    requestInfo, rch, proxy);
            }

            return _siteHttp;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/ninepeso")]
    public async Task<ActionResult> Index(
        string imdb_id, long kinopoisk_id, string title, string original_title,
        int serial = 0, short s = -1, string t = null, bool rjson = false,
        int year = 0, bool checksearch = false, int content_id = 0,
        string site_page = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        LiveRecord record = null;
        JObject liveMetadata = null;

        // Reuse metadata verified by live lookup for season/voice navigation.
        if (content_id > 0 &&
            hybridCache.TryGetValue(LiveRecordKey(serial, content_id),
                out LiveLookupResult cachedLive) &&
            cachedLive?.Record?.id == content_id && cachedLive.Metadata != null)
        {
            record = cachedLive.Record;
            liveMetadata = cachedLive.Metadata;
        }

        if (checksearch)
        {
            // /lite/events waits at most 10 seconds for this probe.
            // Loading the embed, full series metadata and all translations
            // here caused available sources to appear grey in Lampa.
            if (record != null)
                return Content("data-json=", "text/plain; charset=utf-8");

            // A title-only request cannot use the direct CDN ID probe.
            // Do not mark a searchable title as unavailable before the
            // full, year/metadata-validated lookup on source opening.
            if (LiveSearch.HasTitleOnlyFallback(
                imdb_id, kinopoisk_id, title, original_title))
                return Content("data-json=", "text/plain; charset=utf-8");

            try
            {
                bool found = await LiveSearch.HasPlayer(
                    init.host, imdb_id, kinopoisk_id, siteHttp);
                if (found)
                    return Content("data-json=", "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "NinePeso quick availability check failed");
            }

            return OnError("NinePeso: player not found");
        }

        List<LiveSearchCandidate> suggestions = null;
        if (record == null && !string.IsNullOrEmpty(site_page))
        {
            try
            {
                // This site page was explicitly chosen from SimilarTpl.
                var selected = await LiveSearch.FindSelectedPage(
                    init.host, init.apihost, siteHttp, httpHydra,
                    site_page, serial < 0 ? (bool?)null : serial > 0);

                if (selected?.Record == null)
                    return OnError("NinePeso: выбранный результат не подтверждён");

                serial = selected.Serial ? 1 : 0;
                RememberLive(selected, serial);
                record = selected.Record;
                liveMetadata = selected.Metadata;
                title = !string.IsNullOrWhiteSpace(record.ru_title)
                    ? record.ru_title : record.orig_title;
                original_title = record.orig_title;
                imdb_id = record.imdb_id;
                long.TryParse(record.kinopoisk_id, out kinopoisk_id);
                int.TryParse(LiveRecord.ReleaseYear(record.released), out year);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "NinePeso selected live candidate failed");
                return OnError("NinePeso: не удалось открыть выбранный вариант");
            }
        }

        if (record == null)
        {
            if (serial < 0)
                return OnError("NinePeso: missing live result page");

            // Search happens only on an actual source opening, never while
            // /online.js is being generated. Cache the resolved internal ID.
            string key = $"ninepeso:live:{init.host}:{init.apihost}:{serial}:{kinopoisk_id}:{imdb_id}:{title}:{original_title}:{year}";

            var live = await InvokeCacheResult<LiveLookupResult>(
                key, serial > 0 ? 15 : 10, async e =>
            {
                try
                {
                    var found = await LiveSearch.Find(
                        init.host, init.apihost, siteHttp, httpHydra,
                        imdb_id, kinopoisk_id, title, original_title,
                        year, serial > 0);

                    return found != null ? e.Success(found) :
                        e.Fail("NinePeso: не удалось подтвердить плеер для этого произведения");
                }
                catch (LiveMetadataException ex)
                {
                    Serilog.Log.Warning(ex, "NinePeso player found, but metadata unavailable");
                    return e.Fail("NinePeso: плеер 9 найден, но API не вернул данные");
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "NinePeso live lookup failed");
                    return e.Fail("NinePeso: ошибка поиска по сайту");
                }
            }, memory: true);

            if (!live.IsSuccess)
                return OnError(live.ErrorMsg);

            record = live.Value?.Record;
            liveMetadata = live.Value?.Metadata;
            suggestions = live.Value?.Candidates;
            if (record != null && liveMetadata != null)
                RememberLive(live.Value, serial);
        }

        if (record == null || liveMetadata == null)
        {
            var similar = BuildSimilar(host, serial, suggestions);
            return !similar.IsEmpty ? ContentTpl(similar)
                : OnError("NinePeso: title not found by live search");
        }

        if (serial > 0)
            return ContentTpl(BuildSeries(liveMetadata, record, title, original_title,
                imdb_id, kinopoisk_id, s, t, rjson, year, site_page));

        return ContentTpl(BuildMovie(liveMetadata, record, title, original_title));
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/ninepeso-spider")]
    public async Task<ActionResult> Spider(string title, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (!init.spider ||
            string.IsNullOrWhiteSpace(title) || title.Length > 150 ||
            string.IsNullOrEmpty(SearchNameTo.Convert(title)))
            return OnError();

        string key = $"ninepeso:spider:{init.host}:{SearchNameTo.Convert(title)}";
        var cache = await InvokeCacheResult<List<LiveSearchCandidate>>(
            key, 5, async e =>
        {
            try
            {
                var results = await LiveSearch.FindSpider(
                    init.host, siteHttp, rch?.enable == true, title);
                return results.Count > 0 ? e.Success(results) : e.Fail("NinePeso: no live results");
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "NinePeso live Spider lookup failed");
                return e.Fail("NinePeso: live Spider search failed");
            }
        }, memory: true);

        return ContentTpl(cache, () => BuildSimilar(host, -1, cache.Value));
    }

    private static string LiveRecordKey(int serial, int id)
        => $"ninepeso:live-record:{(serial > 0 ? 1 : 0)}:{id}";

    private void RememberLive(LiveLookupResult live, int serial)
    {
        if (live?.Record?.id is not > 0 || live.Metadata == null)
            return;

        // One bounded in-memory entry references the already-loaded JObject.
        // Avoid serializing the enormous TV metadata to disk a second time.
        hybridCache.Set(LiveRecordKey(serial, live.Record.id), live,
            cacheTime(serial > 0 ? 15 : 10), inmemory: true);
    }

    private static SimilarTpl BuildSimilar(
        string baseHost, int serial,
        List<LiveSearchCandidate> candidates)
    {
        var similar = new SimilarTpl(candidates?.Count ?? 0);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int type = serial < 0 ? -1 : serial > 0 ? 1 : 0;

        if (candidates != null)
        {
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Title) ||
                    !seen.Add(candidate.PagePath))
                    continue;

                string link = $"{baseHost}/lite/ninepeso?site_page=" +
                    $"{HttpUtility.UrlEncode(candidate.PagePath)}&serial={type}";
                similar.Append(candidate.Title, candidate.Year, string.Empty, link,
                    PosterApi.Size(candidate.Poster));
            }
        }

        return similar;
    }

    private ITplResult BuildMovie(
        JObject movie, LiveRecord record, string title, string originalTitle)
    {
        // Use the same MovieTpl as CDNvideohub, HDVB and FlixCDN.
        // Lampa's HTML parser replaces the voice with the movie title if the
        // visible label contains "1080p"/"2160p". Keep resolution separate.
        var tpl = new MovieTpl(title ?? record.ru_title,
            originalTitle ?? record.orig_title);

        if (movie?["media"] is not JArray items)
            return tpl;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var usedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MediaChoice choice in MediaChoices.Enumerate(items))
        {
            if (!seen.Add($"{choice.MediaId}:{choice.TrackNumber}"))
                continue;

            string label = DisplayVoice(choice.Name);
            if (!usedLabels.Add(label))
            {
                // Multiple streams may share a studio name; do not merge them.
                label = CleanVoice(choice.Name);
                if (!usedLabels.Add(label))
                    label = $"{label} ({choice.MediaId}:{choice.TrackNumber})";
            }

            string link = VideoUrl(choice, title ?? record.ru_title, record.id, false);
            // Lampa renders the quality map, not MovieTpl's maxquality alone.
            // Advertise the same single HLS master as for series episodes.
            StreamQualityTpl streamQuality = null;
            if (choice.QualityLabel is string quality)
            {
                streamQuality = new StreamQualityTpl(1);
                streamQuality.Append(accsArgs(link + "&play=true"), quality);
            }

            tpl.Append(label, accsArgs(link), "call",
                streamquality: streamQuality,
                quality: choice.QualityLabel,
                vast: init.vast);
        }

        return tpl;
    }

    private static string CleanVoice(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "По умолчанию";

        // "1080p" in the HTML label triggers Lampa to replace that label
        // with the movie title. The separate quality field keeps resolution.
        string voice = Regex.Replace(name.Trim(), @"\b(?:2160|1440|1080|720|480|360)p\b",
            "", RegexOptions.IgnoreCase).Trim(' ', '·', '|', '-', '/');
        return voice.Length > 0 ? voice : "По умолчанию";
    }

    private static string DisplayVoice(string name)
    {
        string voice = CleanVoice(name);
        if (voice.Contains("HDrezka Studio", StringComparison.OrdinalIgnoreCase))
            return "HDrezka Studio";
        if (voice.StartsWith("Полное дублирование", StringComparison.OrdinalIgnoreCase))
            return "Дублированный";
        if (voice.StartsWith("[EN] Original", StringComparison.OrdinalIgnoreCase) ||
            voice.Equals("Original", StringComparison.OrdinalIgnoreCase))
            return "Оригинальный";
        if (voice.StartsWith("Субтитры", StringComparison.OrdinalIgnoreCase) ||
            voice.Equals("Subtitles", StringComparison.OrdinalIgnoreCase))
            return "Субтитры";

        return voice;
    }

    private ITplResult BuildSeries(
        JObject data, LiveRecord record,
        string title, string originalTitle, string imdbId,
        long kinopoiskId, short season, string translation, bool rjson,
        int year, string sitePage)
    {
        if (data?["seasons"] is not JArray seasons)
            return default;

        string fullTitle = title ?? record.ru_title ?? originalTitle;
        string query =
            $"serial=1&rjson={rjson}&year={year}&content_id={record.id}&kinopoisk_id={kinopoiskId}" +
            $"&imdb_id={HttpUtility.UrlEncode(imdbId)}" +
            $"&title={HttpUtility.UrlEncode(fullTitle)}" +
            $"&original_title={HttpUtility.UrlEncode(originalTitle)}" +
            (string.IsNullOrEmpty(sitePage)
                ? "" : $"&site_page={HttpUtility.UrlEncode(sitePage)}");

        if (season < 0)
        {
            // SeasonTpl exposes the maximum available resolution for the series.
            // Source quality belongs to each actual media, not to every season.
            int maxQuality = seasons.OfType<JObject>()
                .SelectMany(x => (x["episodes"] as JArray ?? new JArray()).OfType<JObject>())
                .SelectMany(x => MediaChoices.Enumerate(x["media"] as JArray))
                .Select(x => x.Quality).DefaultIfEmpty().Max();
            var tpl = new SeasonTpl(maxQuality > 0 ? $"{maxQuality}p" : null, seasons.Count);
            foreach (JObject item in seasons.OfType<JObject>()
                .OrderBy(x => ParseNumber(x["num"])))
            {
                short number = ParseNumber(item["num"]);
                if (number <= 0)
                    continue;

                tpl.Append($"{number} сезон",
                    $"{host}/lite/ninepeso?{query}&s={number}", number);
            }

            return tpl;
        }

        JObject selectedSeason = seasons.OfType<JObject>()
            .FirstOrDefault(x => ParseNumber(x["num"]) == season);
        if (selectedSeason?["episodes"] is not JArray episodes)
            return default;

        var orderedEpisodes = episodes.OfType<JObject>()
            .OrderBy(x => ParseNumber(x["num"])).ToList();

        // Collect voice IDs from all episodes, not just the first.
        // This includes additional nested audio tracks as distinct voices.
        var voiceNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var episodeChoices = new List<(JObject Episode, List<MediaChoice> Choices)>(
            orderedEpisodes.Count);
        foreach (JObject episode in orderedEpisodes)
        {
            // Parse nested media/audio tracks once per episode, then reuse the
            // choices when building the selected translation's episode links.
            var choices = MediaChoices.Enumerate(episode["media"] as JArray).ToList();
            episodeChoices.Add((episode, choices));
            foreach (MediaChoice choice in choices)
                voiceNames.TryAdd(choice.Key, choice.Name);
        }

        if (voiceNames.Count == 0)
            return default;

        string selectedVoice = !string.IsNullOrEmpty(translation) &&
            voiceNames.ContainsKey(translation) ? translation : voiceNames.Keys.First();

        string seasonUrl = $"{host}/lite/ninepeso?{query}&s={season}";
        VoiceTpl voices = null;
        if (voiceNames.Count > 1)
        {
            voices = new VoiceTpl(voiceNames.Count);
            foreach (var voice in voiceNames)
            {
                voices.Append(voice.Value, voice.Key == selectedVoice,
                    $"{seasonUrl}&t={HttpUtility.UrlEncode(voice.Key)}");
            }
        }

        var episodeTpl = new EpisodeTpl(voices, orderedEpisodes.Count);
        foreach (var (episode, choices) in episodeChoices)
        {
            short number = ParseNumber(episode["num"]);
            if (number <= 0)
                continue;

            MediaChoice selected = choices
                .FirstOrDefault(x => x.Key == selectedVoice);

            // Never silently switch an episode to another voice.
            if (selected == null)
                continue;

            string link = VideoUrl(selected, fullTitle, record.id, true);
            // One advertised master stream; do not invent lower-resolution renditions.
            StreamQualityTpl streamQuality = null;
            if (selected.QualityLabel is string quality)
            {
                streamQuality = new StreamQualityTpl(1);
                streamQuality.Append(accsArgs(link + "&play=true"), quality);
            }
            episodeTpl.Append(
                $"{number} серия",
                fullTitle,
                season,
                number,
                accsArgs(link),
                "call",
                streamquality: streamQuality,
                streamlink: accsArgs(link + "&play=true"),
                voice_name: voiceNames[selectedVoice],
                vast: init.vast);
        }

        return episodeTpl;
    }

    private string VideoUrl(MediaChoice choice, string title, int contentId, bool serial)
    {
        string url = $"{host}/lite/ninepeso/video.m3u8?id={choice.MediaId}" +
            $"&title={HttpUtility.UrlEncode(title)}";

        if (choice.TrackNumber > 0)
        {
            url += serial ? $"&seriesId={contentId}" : $"&movieId={contentId}";
            url += $"&track={choice.TrackNumber}";
        }

        return url;
    }

    private static short ParseNumber(JToken value)
        => short.TryParse(value?.ToString(), out short result) ? result : (short)-1;

    [HttpGet, Staticache(manually: true)]
    [Route("lite/ninepeso/video.m3u8")]
    public async Task<ActionResult> Video(
        long id, string title, bool play = false,
        int seriesId = 0, int movieId = 0, int track = 0)
    {
        if (id <= 0)
            return OnError("id");

        // A normal HLS URL can be returned without an RCH connection.
        // Nested tracks make a fresh API request and need the standard RCH check.
        if (await IsRequestBlocked(rch: true, rch_check: track > 0))
            return badInitMsg;

        // Even without an upstream request, apply the connected client's
        // native access policy before HostStreamProxy inspects init.rhub.
        if (track <= 0 && rch?.IsNotSupport(out string rchError) == true)
            return ShowError(rchError);

        string stream = $"{init.apihost.TrimEnd('/')}/api/stream/{id}/hls.m3u8";
        if (track > 0)
        {
            if ((seriesId > 0) == (movieId > 0))
                return OnError("NinePeso: invalid track context");

            // Nested tracks use separate signed HLS paths, not the normal
            // /api/stream/{mediaId}/ master. Resolve a fresh URL at play time.
            string endpoint = seriesId > 0 ? "tv-series" : "movies";
            int contentId = seriesId > 0 ? seriesId : movieId;
            var data = await httpHydra.Get<JObject>($"{init.apihost.TrimEnd('/')}/api/{endpoint}/{contentId}/");
            if (data == null)
                return OnError("NinePeso: track metadata unavailable");

            JArray[] sources = seriesId > 0
                ? data["seasons"] is JArray seasons
                    ? seasons.OfType<JObject>()
                        .SelectMany(x => (x["episodes"] as JArray
                            ?? new JArray()).OfType<JObject>())
                        .Select(x => x["media"] as JArray)
                        .Where(x => x != null).ToArray()
                    : Array.Empty<JArray>()
                : data["media"] is JArray movieMedia
                    ? new[] { movieMedia } : Array.Empty<JArray>();

            string path = null;
            foreach (JArray media in sources)
            {
                foreach (JObject item in media.OfType<JObject>())
                {
                    if (item.Value<long?>("id") != id)
                        continue;

                    JObject selected = (item["audio_tracks"] as JArray)?
                        .OfType<JObject>()
                        .FirstOrDefault(x => x.Value<int?>("track_num") == track);
                    path = selected?.Value<string>("stream_path");
                    break;
                }

                if (path != null)
                    break;
            }

            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !(uri.Host == "videobalanser.cc" ||
                  uri.Host.EndsWith(".videobalanser.cc", StringComparison.OrdinalIgnoreCase)) ||
                !uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                return OnError("NinePeso: invalid track URL");

            stream = uri.AbsoluteUri;
        }

        string link = HostStreamProxy(stream);
        if (play)
            return RedirectToPlay(link);

        // HostStreamProxy obeys streamproxy, serverproxy, APN, geo/RCH rules,
        // and may return the original stream even when streamproxy=true.
        bool directStream = string.Equals(link, stream, StringComparison.Ordinal);

        return ContentTo(VideoTpl.ToJson(
            "play",
            link,
            title,
            vast: init.vast,
            httpContext: HttpContext,
            headers: directStream ? httpHeaders(init.apihost, init.headers_stream) : null
        ));
    }
}
