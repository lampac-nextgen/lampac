using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Models.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace KubikVKube;

/// <summary>
/// Вся сетевая работа и разбор: сайт kubikvkube.com, VideoHub, запасной плеер /lat/.
/// Методы Parse*/Build* — чистые (без сети), их можно гонять на сохранённом HTML.
/// </summary>
public class KubikService
{
    public const string SiteOrigin = "https://kubikvkube.com";

    readonly ModuleConf init;
    readonly HttpHydra http;

    /// <summary>Причина последней неудачи (уходит в сообщение об ошибке)</summary>
    public string LastError { get; private set; }

    static string Snip(string s)
        => string.IsNullOrEmpty(s) ? "пусто" : Regex.Replace(s.Length > 90 ? s.Substring(0, 90) : s, "\\s+", " ");

    public KubikService(ModuleConf init, HttpHydra http)
    {
        this.init = init;
        this.http = http;
    }

    string Site => (init.host ?? SiteOrigin).TrimEnd('/');

    #region Нормализация
    static readonly Regex rxNotWord = new Regex("[^\\p{L}\\p{N}+]+", RegexOptions.Compiled);

    public static string Norm(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string s = WebUtility.HtmlDecode(value).ToLowerInvariant().Replace('ё', 'е');
        return rxNotWord.Replace(s, " ").Trim();
    }

    public static bool IsKubikVoice(string name)
        => Norm(name).Contains("кубик в кубе") || Norm(name).Contains("kubik");

    public static string PrettyVoice(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Кубик в Кубе";

        name = WebUtility.HtmlDecode(name).Trim();
        return Regex.Replace(name, "кубик\\s+в\\s+кубе", "Кубик в Кубе", RegexOptions.IgnoreCase);
    }

    static string Abs(string url, string baseUrl = null)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        url = url.Replace("\\/", "/").Replace("&amp;", "&");
        if (!url.StartsWith("//"))
            return url;

        // протокол-относительная ссылка: берём схему зеркала плеера (по умолчанию https)
        string scheme = baseUrl != null && baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "http:" : "https:";
        return scheme + url;
    }
    #endregion

    #region Поиск по сайту
    static readonly Regex rxCard = new Regex(
        "<a class=\"item__title[^\"]*\"\\s+href=\"(?<href>[^\"]+)\"[^>]*>(?<title>[^<]+)</a>\\s*<div class=\"item__year\">\\s*(?<year>[0-9]{4})",
        RegexOptions.Compiled);

    static readonly Regex rxCardImg = new Regex("<img src=\"(?<img>[^\"]+)\"", RegexOptions.Compiled);

    public static string RelHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        href = WebUtility.HtmlDecode(href).Trim();
        var m = Regex.Match(href, "^(?:https?:)?//[^/]+/(.+)$");
        if (m.Success)
            href = m.Groups[1].Value;

        href = href.TrimStart('/');

        // только карточки вида раздел/123-slug.html
        if (!Regex.IsMatch(href, "^(?:series|movie|cartoon|animated-series)/[0-9]+-[^/?#]+\\.html$"))
            return null;

        return href;
    }

    public static bool IsSerialHref(string href)
        => href != null && (href.StartsWith("series/") || href.StartsWith("animated-series/"));

    public static List<SiteSearchItem> ParseSearch(string html)
    {
        var result = new List<SiteSearchItem>();
        if (string.IsNullOrEmpty(html))
            return result;

        // каждая карточка начинается с <div class="item expand-link ...">
        string[] blocks = html.Split(new[] { "<div class=\"item expand-link" }, StringSplitOptions.None);

        for (int i = 1; i < blocks.Length; i++)
        {
            var m = rxCard.Match(blocks[i]);
            if (!m.Success)
                continue;

            string href = RelHref(m.Groups["href"].Value);
            if (href == null || result.Any(r => r.href == href))
                continue;

            var img = rxCardImg.Match(blocks[i]);

            result.Add(new SiteSearchItem
            {
                href = href,
                title = WebUtility.HtmlDecode(m.Groups["title"].Value).Trim(),
                year = int.TryParse(m.Groups["year"].Value, out int y) ? y : 0,
                serial = IsSerialHref(href),
                img = img.Success ? img.Groups["img"].Value : null
            });
        }

        return result;
    }

    public async Task<List<SiteSearchItem>> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return new List<SiteSearchItem>();

        string url = $"{Site}/index.php?do=search&subaction=search&story={HttpUtility.UrlEncode(query.Trim())}";
        string html = await http.Get(url, addheaders: HeadersModel.Init(
            ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("accept-language", "ru-RU,ru;q=0.9"),
            ("referer", Site + "/")
        )).ConfigureAwait(false);

        return ParseSearch(html);
    }

    /// <summary>
    /// Кандидаты из выдачи по убыванию похожести. Чужие типы (фильм/сериал) и годы
    /// дальше чем на год отбрасываются; совпадение названия обязательно.
    /// </summary>
    public static List<SiteSearchItem> RankCandidates(IEnumerable<SiteSearchItem> items, string title, string original_title, int year, int serial)
    {
        string nt = Norm(title), no = Norm(original_title);

        var scored = new List<(SiteSearchItem item, int score)>();

        foreach (var item in items)
        {
            if (serial == 0 && item.serial)
                continue;

            if (serial == 1 && !item.serial)
                continue;

            string ni = Norm(item.title);
            int score;

            if (ni.Length == 0)
                continue;

            if (ni == nt || (no.Length > 0 && ni == no))
                score = 10;
            else if (nt.Length > 3 && (ni.StartsWith(nt + " ") || nt.StartsWith(ni + " ")))
                score = 4;
            else
                continue;

            if (year > 0 && item.year > 0)
            {
                int dy = Math.Abs(year - item.year);
                if (dy == 0)
                    score += 5;
                else if (dy == 1)
                    score += 2;
                else
                    continue;
            }

            scored.Add((item, score));
        }

        return scored.OrderByDescending(s => s.score).Select(s => s.item).ToList();
    }
    #endregion

    #region Страница релиза
    public static SitePage ParsePage(string html, string href)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("page__player"))
            return null;

        var page = new SitePage { href = href, serial = IsSerialHref(href) };

        var h1 = Regex.Match(html, "<h1>\\s*([^<]+?)\\s*</h1>");
        if (h1.Success)
        {
            string t = WebUtility.HtmlDecode(h1.Groups[1].Value).Trim();
            var ty = Regex.Match(t, "^(.*?)\\s*\\(([0-9]{4})\\)\\s*$");
            if (ty.Success)
            {
                page.title = ty.Groups[1].Value.Trim();
                page.year = int.Parse(ty.Groups[2].Value);
            }
            else
            {
                page.title = t;
            }
        }

        var orig = Regex.Match(html, "<h2 class=\"title_en\">\\s*([^<]*?)\\s*</h2>");
        if (orig.Success)
            page.original_title = WebUtility.HtmlDecode(orig.Groups[1].Value).Trim();

        if (page.year == 0)
        {
            var y = Regex.Match(html, "Год выхода:</span>\\s*([0-9]{4})");
            if (y.Success)
                page.year = int.Parse(y.Groups[1].Value);
        }

        // основной плеер: <video-player data-title-id="KP" data-publisher-id="2646" priority-voice="…">
        int vp = html.IndexOf("<video-player", StringComparison.Ordinal);
        if (vp >= 0)
        {
            int end = html.IndexOf('>', vp);
            string tag = end > vp ? html.Substring(vp, end - vp) : html.Substring(vp);

            var kp = Regex.Match(tag, "data-title-id=\"([0-9]+)\"");
            if (kp.Success)
                page.kinopoisk_id = long.Parse(kp.Groups[1].Value);

            var pub = Regex.Match(tag, "data-publisher-id=\"([0-9]+)\"");
            if (pub.Success)
                page.publisher = int.Parse(pub.Groups[1].Value);

            var pv = Regex.Match(tag, "priority-voice=\"([^\"]*)\"");
            if (pv.Success)
                page.priority_voice = WebUtility.HtmlDecode(pv.Groups[1].Value).Trim();
        }

        // запасной плеер: <iframe src|data-src="//host/lat/ID?season=1&episode=1&voice=10&adult_mode=2">
        var lat = Regex.Match(html, "<iframe[^>]+?(?:data-src|src)=\"(?<host>(?:https?:)?//[^\"/]+)/lat/(?<id>[0-9]+)(?<q>[^\"]*)\"");
        if (lat.Success)
        {
            page.lat_host = Abs(lat.Groups["host"].Value).TrimEnd('/');
            page.lat_id = int.Parse(lat.Groups["id"].Value);

            var voice = Regex.Match(lat.Groups["q"].Value, "[?&](?:amp;)?voice=([0-9]+)");
            if (voice.Success)
                page.lat_voice = int.Parse(voice.Groups[1].Value);
        }

        // заливка в VK-сообществе студии: <iframe src="https://vkvideo.ru/video_ext.php?oid=-237092588&id=456239019&hash=…">
        var vk = Regex.Match(html, "<iframe[^>]+?(?:data-src|src)=\"(?:https?:)?//(?:vkvideo\\.ru|vk\\.com|vk\\.ru)/video_ext\\.php\\?(?<q>[^\"]+)\"");
        if (vk.Success)
        {
            string q = WebUtility.HtmlDecode(vk.Groups["q"].Value);
            var oid = Regex.Match(q, "(?:^|&)oid=(-?[0-9]+)");
            var id = Regex.Match(q, "(?:^|&)id=([0-9]+)");
            var hash = Regex.Match(q, "(?:^|&)hash=([0-9a-f]+)");

            if (oid.Success && id.Success)
                page.vk_video = $"{oid.Groups[1].Value}_{id.Groups[1].Value}" + (hash.Success ? "_" + hash.Groups[1].Value : string.Empty);
        }

        if (page.kinopoisk_id == 0 && page.lat_id == 0 && page.vk_video == null)
            return null;

        return page;
    }

    public async Task<SitePage> GetPage(string href)
    {
        href = RelHref(href);
        if (href == null)
            return null;

        string html = await http.Get($"{Site}/{href}", addheaders: HeadersModel.Init(
            ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("accept-language", "ru-RU,ru;q=0.9"),
            ("referer", Site + "/")
        )).ConfigureAwait(false);

        return ParsePage(html, href);
    }

    /// <summary>Находит страницу релиза: поиск по названию → проверка по Кинопоиску/году/оригинальному названию</summary>
    public async Task<(SitePage page, List<SiteSearchItem> found)> Resolve(string title, string original_title, int year, int serial, long kinopoisk_id)
    {
        var found = new List<SiteSearchItem>();
        var queries = new List<string>();

        foreach (string q in new[] { title, original_title })
        {
            if (!string.IsNullOrWhiteSpace(q) && !queries.Any(x => Norm(x) == Norm(q)))
                queries.Add(q.Trim());
        }

        var tried = new HashSet<string>();
        SitePage fallback = null;
        int pageFetches = 0;

        foreach (string q in queries)
        {
            var items = await Search(q).ConfigureAwait(false);

            foreach (var it in items)
            {
                if (!found.Any(f => f.href == it.href))
                    found.Add(it);
            }

            foreach (var cand in RankCandidates(items, title, original_title, year, serial))
            {
                if (!tried.Add(cand.href) || pageFetches >= 4)
                    continue;

                pageFetches++;
                var page = await GetPage(cand.href).ConfigureAwait(false);
                if (page == null)
                    continue;

                if (kinopoisk_id > 0 && page.kinopoisk_id > 0)
                {
                    if (page.kinopoisk_id == kinopoisk_id)
                        return (page, found);

                    continue; // другой фильм с тем же названием
                }

                bool origOk = !string.IsNullOrEmpty(original_title) && Norm(page.original_title) == Norm(original_title);
                bool yearOk = year <= 0 || page.year <= 0 || Math.Abs(page.year - year) <= 1;

                if (origOk && yearOk)
                    return (page, found);

                if (yearOk && fallback == null)
                    fallback = page;
            }
        }

        return (fallback, found);
    }
    #endregion

    #region VideoHub
    IReadOnlyList<HeadersModel> VhApiHeaders => HeadersModel.Init(
        ("accept", "application/json, text/plain, */*"),
        ("origin", Site),
        ("referer", Site + "/"),
        ("sec-fetch-dest", "empty"),
        ("sec-fetch-mode", "cors"),
        ("sec-fetch-site", "cross-site")
    );

    public static IReadOnlyList<HeadersModel> VhStreamHeaders => HeadersModel.Init(
        Http.defaultFullHeaders,
        ("accept", "*/*"),
        ("origin", "https://player.cdnvideohub.com"),
        ("referer", "https://player.cdnvideohub.com/"),
        ("sec-fetch-dest", "empty"),
        ("sec-fetch-mode", "cors"),
        ("sec-fetch-site", "cross-site")
    );

    public async Task<VhPlaylist> GetVhPlaylist(long kinopoisk_id, int pub)
    {
        if (kinopoisk_id <= 0)
            return null;

        string url = $"{init.vhapi.TrimEnd('/')}/player/sv/playlist?pub={(pub > 0 ? pub : init.vhpub)}&aggr=kp&id={kinopoisk_id}";
        string json = await http.Get(url, addheaders: VhApiHeaders).ConfigureAwait(false);
        return ParseVhPlaylist(json);
    }

    public static VhPlaylist ParseVhPlaylist(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
            return null;

        try
        {
            var pl = JsonConvert.DeserializeObject<VhPlaylist>(json);
            if (pl?.items == null)
                return null;

            pl.items = pl.items.Where(i => i != null && !string.IsNullOrEmpty(i.vkId)).ToList();
            return pl;
        }
        catch
        {
            return null;
        }
    }

    public async Task<VhStream> GetVhStream(string vk)
    {
        if (string.IsNullOrWhiteSpace(vk) || !Regex.IsMatch(vk, "^[0-9]+$"))
            return null;

        string json = await http.Get($"{init.vhapi.TrimEnd('/')}/player/sv/video/{vk}", addheaders: VhApiHeaders).ConfigureAwait(false);
        return ParseVhVideo(json);
    }

    public static VhStream ParseVhVideo(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
            return null;

        VhVideo video;
        try { video = JsonConvert.DeserializeObject<VhVideo>(json); }
        catch { return null; }

        var src = video?.sources;
        if (src == null)
            return null;

        var stream = new VhStream { hls = string.IsNullOrWhiteSpace(src.hlsUrl) ? null : src.hlsUrl };

        void add(string q, string url)
        {
            if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("http") && !stream.mp4.Any(m => m.Key == q))
                stream.mp4.Add(new KeyValuePair<string, string>(q, url));
        }

        add("2160p", src.mpeg4kUrl);
        add("1440p", src.mpeg2kUrl);
        add("1440p", src.mpegQhdUrl);
        add("1080p", src.mpegFullHdUrl);
        add("720p", src.mpegHighUrl);
        add("480p", src.mpegMediumUrl);
        add("360p", src.mpegLowUrl);
        add("240p", src.mpegLowestUrl);

        if (stream.hls == null && stream.mp4.Count == 0)
            return null;

        return stream;
    }
    #endregion

    #region VK Видео
    const int VkClientId = 52461373;

    static string vkToken;
    static DateTime vkTokenExpires;
    static readonly SemaphoreSlim vkTokenLock = new SemaphoreSlim(1, 1);

    public static IReadOnlyList<HeadersModel> VkStreamHeaders => HeadersModel.Init(
        Http.defaultFullHeaders,
        ("accept", "*/*"),
        ("origin", "https://vkvideo.ru"),
        ("referer", "https://vkvideo.ru/")
    );

    // content-type не задаём: Lampac сам ставит его для формы, второй заголовок ломает запрос
    IReadOnlyList<HeadersModel> VkApiHeaders => HeadersModel.Init(
        ("accept", "application/json, text/plain, */*"),
        ("origin", "https://vkvideo.ru"),
        ("referer", "https://vkvideo.ru/")
    );

    /// <summary>Анонимный токен VK (как у веб-плеера vkvideo.ru), живёт часами — кэшируем на процесс</summary>
    async Task<string> GetVkToken(bool force = false)
    {
        if (!force && vkToken != null && vkTokenExpires > DateTime.UtcNow)
            return vkToken;

        if (!await vkTokenLock.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false))
            return null;

        try
        {
            if (!force && vkToken != null && vkTokenExpires > DateTime.UtcNow)
                return vkToken;

            string data = $"client_secret=o557NLIkAErNhakXrQ7A&client_id={VkClientId}&scopes=audio_anonymous%2Cvideo_anonymous%2Cphotos_anonymous%2Cprofile_anonymous&isApiOauthAnonymEnabled=false&version=1&app_id=6287487";
            string json = await http.Post(init.vktoken, data, addheaders: VkApiHeaders).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
            {
                LastError = "vk:token:" + Snip(json);
                return null;
            }

            var root = JObject.Parse(json);
            string token = root["data"]?["access_token"]?.ToString();
            if (string.IsNullOrEmpty(token))
            {
                LastError = "vk:token:" + Snip(json);
                return null;
            }

            long expires = root["data"]?["expires"]?.Value<long?>() ?? 0;
            vkToken = token;
            vkTokenExpires = expires > 0
                ? DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime.AddHours(-1)
                : DateTime.UtcNow.AddHours(6);

            return vkToken;
        }
        catch (Exception ex)
        {
            LastError = "vk:token:" + ex.GetType().Name;
            return null;
        }
        finally
        {
            vkTokenLock.Release();
        }
    }

    public async Task<VhStream> GetVkStream(string video)
    {
        if (string.IsNullOrWhiteSpace(video) || !Regex.IsMatch(video, "^-?[0-9]+_[0-9]+(_[0-9a-f]+)?$"))
            return null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await GetVkToken(force: attempt > 0).ConfigureAwait(false);
            if (token == null)
                return null;

            string json = await http.Post($"{init.vkapi.TrimEnd('/')}/method/video.get?v=5.264&client_id={VkClientId}",
                $"videos={video}&access_token={HttpUtility.UrlEncode(token)}", addheaders: VkApiHeaders).ConfigureAwait(false);

            var root = ParseVk(json);
            if (root?.error != null && (root.error.error_code == 5 || root.error.error_code == 1116))
            {
                LastError = $"vk:api:{root.error.error_code} {root.error.error_msg}";
                continue; // токен протух — берём новый
            }

            if (root == null)
            {
                LastError = "vk:api:" + Snip(json);
                return null;
            }

            if (root.error != null)
            {
                LastError = $"vk:api:{root.error.error_code} {root.error.error_msg}";
                return null;
            }

            var stream = VkStreamFrom(root);
            if (stream == null)
                LastError = "vk:нет файлов (видео удалено или закрыто)";

            return stream;
        }

        return null;
    }

    public static VkRoot ParseVk(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{"))
            return null;

        try { return JsonConvert.DeserializeObject<VkRoot>(json); }
        catch { return null; }
    }

    public static VhStream VkStreamFrom(VkRoot root)
    {
        var files = root?.response?.items?.FirstOrDefault()?.files;
        if (files == null || files.Count == 0)
            return null;

        string f(string key) => files.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v) && v.StartsWith("http") ? v : null;

        var stream = new VhStream { hls = f("hls") ?? f("hls_ondemand") ?? f("hls_fmp4") };

        foreach (var q in new[] { "2160", "1440", "1080", "720", "480", "360", "240" })
        {
            string url = f("mp4_" + q);
            if (url != null)
                stream.mp4.Add(new KeyValuePair<string, string>(q + "p", url));
        }

        return stream.hls == null && stream.mp4.Count == 0 ? null : stream;
    }
    #endregion

    #region Запасной плеер /lat/
    public IEnumerable<string> LatHosts(string pageHost)
    {
        var hosts = new List<string>();

        if (!string.IsNullOrWhiteSpace(pageHost))
            hosts.Add(pageHost.TrimEnd('/'));

        if (init.lathosts != null)
        {
            foreach (string h in init.lathosts)
            {
                if (!string.IsNullOrWhiteSpace(h) && !hosts.Contains(h.TrimEnd('/')))
                    hosts.Add(h.TrimEnd('/'));
            }
        }

        return hosts;
    }

    public static IReadOnlyList<HeadersModel> LatStreamHeaders(string latHost) => HeadersModel.Init(
        Http.defaultFullHeaders,
        ("accept", "*/*"),
        ("origin", latHost),
        ("referer", latHost + "/"),
        ("sec-fetch-dest", "empty"),
        ("sec-fetch-mode", "cors"),
        ("sec-fetch-site", "same-site")
    );

    public static string LatPageUrl(string latHost, int latId, int season, int episode, int voice)
    {
        string url = $"{latHost}/lat/{latId}";
        if (season > 0 && episode > 0 && voice > 0)
            return url + $"?season={season}&episode={episode}&voice={voice}&adult_mode=2";

        return url + "?adult_mode=2";
    }

    /// <summary>Страница плеера; host — зеркало, которое ответило</summary>
    public async Task<(LatPlayerData data, string host, string error)> GetLatPlayer(string pageHost, int latId, int season = 0, int episode = 0, int voice = 0)
    {
        if (latId <= 0)
            return (null, null, "lat:id");

        var errors = new List<string>();

        foreach (string host in LatHosts(pageHost))
        {
            string html = await http.Get(LatPageUrl(host, latId, season, episode, voice), addheaders: HeadersModel.Init(
                ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("referer", Site + "/"),
                ("sec-fetch-dest", "iframe"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "cross-site")
            )).ConfigureAwait(false);

            if (string.IsNullOrEmpty(html))
            {
                errors.Add($"{new Uri(host).Host}:нет ответа");
                continue;
            }

            if (IsLatBlocked(html))
            {
                errors.Add($"{new Uri(host).Host}:геоблок");
                continue;
            }

            var data = ParseLatPlayer(html);
            if (data == null)
            {
                errors.Add($"{new Uri(host).Host}:нет playerData");
                continue;
            }

            return (data, host, null);
        }

        return (null, null, "lat:" + (errors.Count > 0 ? string.Join(",", errors) : "нет зеркал"));
    }

    /// <summary>Заглушка «404 Not Found» с проверкой isFramed — плеер отказал (обычно не-РФ IP)</summary>
    public static bool IsLatBlocked(string html)
        => html.Length < 4096 && html.Contains("isFramed", StringComparison.Ordinal) && html.Contains("404 Not Found", StringComparison.Ordinal);

    public static LatPlayerData ParseLatPlayer(string html)
    {
        string json = ExtractAssignedJson(html, "window.playerData");
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            var data = JsonConvert.DeserializeObject<LatPlayerData>(json);
            return data?.config == null && data?.playlist == null ? null : data;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> GetLatHls(string latHost, int latId, int videoId, int season, int episode, int voice)
    {
        if (string.IsNullOrWhiteSpace(latHost) || latId <= 0)
            return null;

        // быстрый путь: /videos.php отдаёт свежую подписанную ссылку
        if (videoId > 0)
        {
            string json = await http.Get($"{latHost}/videos.php?id={videoId}", addheaders: HeadersModel.Init(
                ("accept", "application/json, text/plain, */*"),
                ("referer", $"{latHost}/lat/{latId}"),
                ("x-requested-with", "XMLHttpRequest")
            )).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith("{"))
            {
                try
                {
                    var v = JsonConvert.DeserializeObject<LatVideo>(json);
                    string hls = Abs(string.IsNullOrWhiteSpace(v?.video) ? v?.video_new : v.video, latHost);
                    if (!string.IsNullOrWhiteSpace(hls))
                        return hls;
                }
                catch { }
            }
        }

        // запасной путь: страница плеера на нужной серии
        var page = await GetLatPlayer(latHost, latId, season, episode, voice).ConfigureAwait(false);
        string cfg = page.data?.config?.video;
        if (string.IsNullOrWhiteSpace(cfg))
            cfg = page.data?.config?.video_new;

        return string.IsNullOrWhiteSpace(cfg) ? null : Abs(cfg, latHost);
    }

    public static string LatVoiceName(LatPlayerData data, int voiceId)
    {
        if (data?.voices != null && data.voices.TryGetValue(voiceId.ToString(), out JToken value) && value != null)
        {
            if (value.Type == JTokenType.String)
            {
                string text = value.Value<string>();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            else if (value.Type == JTokenType.Object)
            {
                string text = value.Value<string>("name") ?? value.Value<string>("voice_name") ?? value.Value<string>("title");
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        var cur = data?.playlist?.serial?.current;
        if (cur != null && cur.voiceId == voiceId && !string.IsNullOrWhiteSpace(cur.voiceName))
            return cur.voiceName;

        return null;
    }

    public static string ExtractAssignedJson(string html, string marker)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        int from = 0;
        while (from < html.Length)
        {
            int idx = html.IndexOf(marker, from, StringComparison.Ordinal);
            if (idx < 0)
                return null;

            int i = idx + marker.Length;
            while (i < html.Length && char.IsWhiteSpace(html[i]))
                i++;

            if (i >= html.Length || html[i] != '=')
            {
                from = idx + marker.Length;
                continue;
            }

            int start = html.IndexOf('{', i);
            if (start < 0)
                return null;

            int depth = 0;
            char quote = '\0';
            bool esc = false;

            for (int j = start; j < html.Length; j++)
            {
                char c = html[j];

                if (quote != '\0')
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == quote) quote = '\0';
                    continue;
                }

                if (c == '"' || c == '\'') { quote = c; continue; }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                    return html.Substring(start, j - start + 1);
            }

            return null;
        }

        return null;
    }
    #endregion

    #region Каталог
    /// <summary>
    /// Ключ озвучки, общий для всех плееров: «HDrezka Studio» и «HDRezka Studio»,
    /// «RedHeadSound» и «Red Head Sound» — одна озвучка.
    /// </summary>
    public static string VoiceKey(string name)
        => Norm(name).Replace(" ", string.Empty);

    static readonly string[] PlayerOrder = { "vh", "vk", "lat" };

    public static int PlayerRank(string player)
    {
        int i = Array.IndexOf(PlayerOrder, player);
        return i < 0 ? 9 : i;
    }

    /// <summary>Сводит все плееры в один список дорожек (плеер · озвучка · сезон · серия)</summary>
    public static List<KubikTrack> BuildTracks(SitePage page, VhPlaylist vh, LatPlayerData lat, bool onlyKubik)
    {
        var tracks = new List<KubikTrack>();
        var seen = new HashSet<string>();

        if (vh?.items != null)
        {
            // у части старых релизов VideoHub не подписывает студию (только «Многоголосый»);
            // если подписанного Кубика нет — берём неподписанные дорожки, честно помечая их
            bool vhHasKubik = vh.items.Any(i => IsKubikVoice(i.voiceStudio));

            foreach (var item in vh.items)
            {
                bool unnamed = string.IsNullOrWhiteSpace(item.voiceStudio);
                string name = unnamed
                    ? (string.IsNullOrWhiteSpace(item.voiceType) ? "Озвучка" : item.voiceType) + " · без подписи"
                    : item.voiceStudio;

                if (onlyKubik && !IsKubikVoice(name) && !(unnamed && !vhHasKubik))
                    continue;

                int s = vh.isSerial ? item.season : 0;
                int e = vh.isSerial ? item.episode : 0;

                if (vh.isSerial && (s <= 0 || e <= 0))
                    continue;

                string key = VoiceKey(name);
                if (!seen.Add($"vh:{key}:{s}:{e}"))
                    continue;

                tracks.Add(new KubikTrack
                {
                    player = "vh",
                    voice = key,
                    voice_name = PrettyVoice(name),
                    season = s,
                    episode = e,
                    vk = item.vkId
                });
            }
        }

        if (lat != null)
        {
            var list = lat.playlist?.serial?.list;

            if (list != null && list.Count > 0 && list.Any(l => l != null && l.Count > 0))
            {
                int startSeason = lat.playlist?.current?.startSeason > 0 ? lat.playlist.current.startSeason : 1;

                for (int si = 0; si < list.Count; si++)
                {
                    if (list[si] == null)
                        continue;

                    int season = startSeason + si;

                    foreach (var ep in list[si])
                    {
                        if (ep == null || ep.num <= 0 || ep.voices == null)
                            continue;

                        foreach (var v in ep.voices)
                        {
                            if (v == null || v.voice_id <= 0 || v.video_id <= 0)
                                continue;

                            string name = LatVoiceName(lat, v.voice_id);
                            if (string.IsNullOrWhiteSpace(name))
                                name = page != null && v.voice_id == page.lat_voice ? "Кубик в Кубе" : $"Озвучка {v.voice_id}";

                            bool kubik = IsKubikVoice(name) || (page != null && page.lat_voice > 0 && v.voice_id == page.lat_voice);
                            if (onlyKubik && !kubik)
                                continue;

                            string key = VoiceKey(name);
                            if (!seen.Add($"lat:{key}:{season}:{ep.num}"))
                                continue;

                            tracks.Add(new KubikTrack
                            {
                                player = "lat",
                                voice = key,
                                voice_name = PrettyVoice(name),
                                season = season,
                                episode = ep.num,
                                lat_video = v.video_id,
                                lat_voice = v.voice_id
                            });
                        }
                    }
                }
            }
            else if (lat.config?.video_id > 0)
            {
                // фильм в запасном плеере
                int voiceId = lat.playlist?.serial?.current?.voiceId ?? page?.lat_voice ?? 0;
                string name = LatVoiceName(lat, voiceId) ?? "Кубик в Кубе";

                if (!onlyKubik || IsKubikVoice(name) || (page != null && voiceId == page.lat_voice))
                {
                    tracks.Add(new KubikTrack
                    {
                        player = "lat",
                        voice = VoiceKey(name),
                        voice_name = PrettyVoice(name),
                        lat_video = lat.config.video_id,
                        lat_voice = voiceId
                    });
                }
            }
        }

        // заливка студии в VK — только для фильмов (в сериалах сайт её не использует)
        if (page?.vk_video != null && !(vh?.isSerial == true) && !(page.serial))
        {
            tracks.Add(new KubikTrack
            {
                player = "vk",
                voice = VoiceKey("Кубик в Кубе"),
                voice_name = "Кубик в Кубе",
                vk_video = page.vk_video
            });
        }

        return tracks;
    }

    /// <summary>Краткая сводка озвучек VideoHub для сообщений об ошибке</summary>
    public static string VhVoicesSummary(VhPlaylist vh)
    {
        if (vh?.items == null || vh.items.Count == 0)
            return "пусто";

        return string.Join(", ", vh.items
            .GroupBy(i => string.IsNullOrWhiteSpace(i.voiceStudio) ? (i.voiceType ?? "?") : i.voiceStudio)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select(g => $"{g.Key}×{g.Count()}"));
    }

    /// <summary>
    /// Порядок озвучек: приоритетная со страницы сайта → остальные озвучки Кубика →
    /// прочие студии по числу серий (в сезоне), затем по имени
    /// </summary>
    public static List<string> OrderVoices(IEnumerable<KubikTrack> tracks, string priorityVoice)
    {
        string pv = VoiceKey(priorityVoice);

        return tracks
            .GroupBy(t => t.voice)
            .OrderBy(g => pv.Length > 0 && g.Key == pv ? 0 : 1)
            .ThenBy(g => g.Any(t => IsKubikVoice(t.voice_name)) ? 0 : 1)
            .ThenByDescending(g => g.Select(t => (t.season, t.episode)).Distinct().Count())
            .ThenBy(g => DisplayName(g))
            .Select(g => g.Key)
            .ToList();
    }

    /// <summary>Имя озвучки для списка: как у VideoHub, иначе как у VK/запасного плеера</summary>
    public static string DisplayName(IEnumerable<KubikTrack> sameVoice)
        => sameVoice.OrderBy(t => PlayerRank(t.player)).First().voice_name;

    /// <summary>Основная дорожка серии в озвучке: VideoHub → VK → запасной плеер</summary>
    public static KubikTrack Primary(IEnumerable<KubikTrack> sameVoiceEpisode)
        => sameVoiceEpisode.OrderBy(t => PlayerRank(t.player)).First();

    /// <summary>
    /// Та же серия в другом плеере: строго та же озвучка. Для озвучки Кубика допускается
    /// и другая его версия (18+/обычная) — лучше она, чем пустой экран.
    /// </summary>
    public static KubikTrack FindAlternative(IEnumerable<KubikTrack> tracks, KubikTrack primary, string player)
    {
        if (primary.player == player)
            return primary;

        var same = tracks.Where(t => t.player == player && t.season == primary.season && t.episode == primary.episode).ToList();

        return same.FirstOrDefault(t => t.voice == primary.voice)
            ?? (IsKubikVoice(primary.voice_name) ? same.FirstOrDefault(t => IsKubikVoice(t.voice_name)) : null);
    }
    #endregion
}
