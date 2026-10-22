using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Redheadsound;

/// <summary>
/// Собственный плеер сайта RHS (ladoni.pro/lat/{id}).
/// Он открывается только внутри iframe на странице RHS, а m3u8 появляется после нажатия Play,
/// поэтому поток берём из Chromium: открываем карточку, ставим нужную серию, жмём Play и ловим m3u8.
/// </summary>
public partial class RedheadsoundController
{
    public const string VoiceName = "Red Head Sound";

    /// <summary>Подпись серии; совпадает с прежним именем озвучки, чтобы старые подписки TelegramBot находили серии.</summary>
    public const string VoiceSite = "Red Head Sound · сайт";

    static readonly Regex cardLinkRx = new Regex("href=\"(https?://[^\"]+?/(\\d+-[^\"/?#]+?)\\.html)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex latRx = new Regex("(?:https?:)?//[^\"'\\s<>]+?/lat/\\d+[^\"'\\s<>]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex cidRx = new Regex("^\\d+-[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    static readonly Regex latFrameRx = new Regex("/lat/\\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex otherPlayersRx = new Regex("(cdnvideohub\\.com|rutube\\.ru|rtbcdn\\.ru|stravers|apbugall)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex adsRx = new Regex("(adfox|/vast|vast\\.|vast2|doubleclick|googlesyndication|googletagmanager|google-analytics|mc\\.yandex|an\\.yandex|yandex\\.ru/ads|adriver|betweendigital|mediametrics|top-fwz|liveinternet|counter\\.|/metrika|i-trailer|adsbygoogle|onclck|pushwoosh)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly ConcurrentDictionary<string, Task<VideoResult>> ladoniInflight = new ConcurrentDictionary<string, Task<VideoResult>>();

    #region SiteCards
    /// <summary>Поиск на сайте RHS. Каждая карточка — фильм или сезон; из неё берём сезон, последнюю серию и iframe плееров.</summary>
    async Task<List<SiteCard>> SiteCards(string title, string original_title, int year)
    {
        string query = !string.IsNullOrWhiteSpace(title) ? title : original_title;
        string memKey = $"redheadsound:cards:{Norm(query)}:{year}";

        if (hybridCache.TryGetValue(memKey, out List<SiteCard> cached) && cached != null)
            return cached;

        var result = new List<SiteCard>();

        try
        {
            // сайт RHS меняет адреса (redheadsound.top → redheadsound3.top …): перебираем зеркала,
            // рабочее запоминаем, чтобы не ждать мёртвые на каждом запросе
            string root = null, html = null;
            foreach (string r in SiteRoots())
            {
                var refHeader = HeadersModel.Init(("referer", r + "/"));
                html = await httpHydra.Get($"{r}/index.php?do=search&subaction=search&story={HttpUtility.UrlEncode(query)}", addheaders: refHeader);
                if (html == null)
                {
                    Dbg($"site {r}: no answer");
                    continue;
                }

                // POST-поиск не используем: через прокси он виснет до таймаута, а GET отдаёт ту же выдачу
                root = r;
                aliveRoot = (r, DateTime.Now);
                break;
            }

            if (root == null || string.IsNullOrEmpty(html))
            {
                if (init.debug)
                    Console.WriteLine($"Redheadsound site: search empty ({query}), mirrors: {string.Join(", ", SiteRoots())}");

                return result;
            }

            var links = cardLinkRx.Matches(html)
                .Select(m => (url: WebUtility.HtmlDecode(m.Groups[1].Value), cid: m.Groups[2].Value))
                .Where(l => cidRx.IsMatch(l.cid))
                .GroupBy(l => l.cid)
                .Select(g => g.First())
                .Take(10)
                .ToList();

            var tasks = links.Select(l => LoadCard(l.url, l.cid, root)).ToList();
            var loaded = await Task.WhenAll(tasks);

            string stitle = Norm(title);
            string sorig = Norm(original_title);

            foreach (var card in loaded)
            {
                if (card == null)
                    continue;

                string name = Norm(card.name);
                if (!TitleMatch(name, stitle) && !TitleMatch(name, sorig))
                    continue;

                if (year > 0 && card.season <= 1)
                {
                    // год в заголовке фильма/первого сезона должен совпадать (±1)
                    var ym = Regex.Match(card.name ?? "", "\\b(19|20)\\d{2}\\b");
                    if (ym.Success && Math.Abs(int.Parse(ym.Value) - year) > 1)
                        continue;
                }

                result.Add(card);
                hybridCache.Set($"redheadsound:card:{card.cid}", card, DateTime.Now.AddHours(12), inmemory: true);
            }

            if (init.debug)
            {
                Console.WriteLine($"Redheadsound site: {query} -> {links.Count} links, {result.Count} cards");
                foreach (var c in result)
                    Console.WriteLine($"   {c.cid} | {c.name} | season {c.season} | episodes {c.episodes} | lat {c.lat ?? "-"}");
            }
        }
        catch (Exception ex)
        {
            if (init.debug)
                Console.WriteLine("Redheadsound site error: " + ex.Message);
        }

        hybridCache.Set(memKey, result, cacheTime(result.Count > 0 ? 30 : 10), inmemory: true);
        return result;
    }

    static (string root, DateTime time) aliveRoot;

    /// <summary>Адреса сайта по порядку: последний рабочий, host из init.conf, затем зеркала.</summary>
    List<string> SiteRoots()
    {
        var list = new List<string>();
        if (aliveRoot.root != null && aliveRoot.time > DateTime.Now.AddMinutes(-30))
            list.Add(aliveRoot.root);

        if (!string.IsNullOrWhiteSpace(init.host))
            list.Add(init.host.TrimEnd('/'));

        if (init.mirrors != null)
            list.AddRange(init.mirrors.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.TrimEnd('/')));

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool TitleMatch(string name, string wanted)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(wanted) || !name.StartsWith(wanted))
            return false;

        // «Пацаны 5 сезон» — да, «Пацаны: Мексика» — нет
        string rest = name.Substring(wanted.Length);
        return rest.Length == 0 || Regex.IsMatch(rest, "^(\\d|сезон|серия|серии|фильм|сериал|мультфильм|movie|season)");
    }

    async Task<SiteCard> LoadCard(string url, string cid, string root)
    {
        try
        {
            string html = await httpHydra.Get(url, addheaders: HeadersModel.Init(("referer", root + "/")));
            if (string.IsNullOrEmpty(html))
                return null;

            string h1 = Regex.Match(html, "<h1[^>]*>([\\s\\S]*?)</h1>", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(h1))
                h1 = Regex.Match(html, "<title>([^<]+)</title>", RegexOptions.IgnoreCase).Groups[1].Value;

            string name = Regex.Replace(WebUtility.HtmlDecode(h1 ?? ""), "<[^>]+>", " ");
            name = Regex.Replace(name, "\\s+", " ").Trim();

            var card = new SiteCard { cid = cid, url = url, name = name };

            var sm = Regex.Match(name, "(\\d+)\\s*(?:-?й\\s*)?сезон", RegexOptions.IgnoreCase);
            if (!sm.Success)
                sm = Regex.Match(name, "сезон\\s*(\\d+)", RegexOptions.IgnoreCase);
            if (sm.Success)
                card.season = int.Parse(sm.Groups[1].Value);

            card.episodes = LastEpisode(name);

            // в заголовке серии нет — смотрим <title> и описание страницы
            if (card.episodes == 0)
            {
                string meta = Regex.Match(html, "<title>([^<]+)</title>", RegexOptions.IgnoreCase).Groups[1].Value + " " +
                              Regex.Match(html, "<meta[^>]+name=\"description\"[^>]+content=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                card.episodes = LastEpisode(WebUtility.HtmlDecode(meta));
            }

            // номер серии в адресах ссылок: «…-pacany-5-sezon-8-seriya-final-2026.html» → 5 сезон, 8 серия
            if (card.season > 0)
            {
                foreach (Match um in Regex.Matches(html, "-(\\d+)-sezon-(\\d+)-seri", RegexOptions.IgnoreCase))
                {
                    int ss = int.Parse(um.Groups[1].Value), ee = int.Parse(um.Groups[2].Value);
                    if (ss == card.season && ee > card.episodes && ee < 1000)
                        card.episodes = ee;
                }
            }

            var lm = latRx.Match(html);
            if (lm.Success)
            {
                string lat = WebUtility.HtmlDecode(lm.Value);
                card.lat = lat.StartsWith("//") ? "https:" + lat : lat;
            }

            return card;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>«8 серия», «1-8 серия», «серии 1–10» → последняя вышедшая серия, 0 — не нашли.</summary>
    static int LastEpisode(string text)
    {
        int max = 0;
        foreach (Match em in Regex.Matches(text ?? "", "(\\d+)(?:\\s*[-–—]\\s*(\\d+))?\\s*сери", RegexOptions.IgnoreCase))
        {
            int n = int.Parse(em.Groups[2].Success ? em.Groups[2].Value : em.Groups[1].Value);
            if (n > max && n < 1000)
                max = n;
        }

        return max;
    }

    async Task<SiteCard> CardForAsync(string cid)
    {
        if (hybridCache.TryGetValue($"redheadsound:card:{cid}", out SiteCard card) && card != null)
            return card;

        // после рестарта кэша нет: ищем зеркало, где карточка открывается, и запоминаем его
        foreach (string r in SiteRoots())
        {
            string url = $"{r}/{cid}.html";
            string html = await httpHydra.Get(url, addheaders: HeadersModel.Init(("referer", r + "/")));
            if (html == null)
            {
                Dbg($"site {r}: no answer");
                continue;
            }

            aliveRoot = (r, DateTime.Now);
            return await LoadCard(url, cid, r) ?? new SiteCard { cid = cid, url = url };
        }

        return new SiteCard { cid = cid, url = $"{SiteRoots().FirstOrDefault() ?? "https://redheadsound3.top"}/{cid}.html" };
    }
    #endregion

    #region Ladoni
    [HttpGet, Staticache(manually: true)]
    [Route("lite/redheadsound/ladoni")]
    [Route("lite/redheadsound/ladoni.m3u8")]
    [Route("lite/redheadsound/play")]
    [Route("lite/redheadsound/play.m3u8")]
    async public Task<ActionResult> Ladoni(string cid, int s = 0, int e = 0, bool play = false, int lat = 0, int v = 0, int a = 0, string lh = null)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (lat > 0)
        {
            // плеер ladoni без браузера: страница плеера уже содержит m3u8
            string lhost = !string.IsNullOrEmpty(lh) && seenLatHosts.ContainsKey(lh) ? lh : "https://ladoni.pro";
            if (s < 0 || e < 0 || v < 0)
                return OnError();

            string memkey = $"redheadsound:latstream:{lhost}:{lat}:{s}:{e}:{v}:{a}";
            if (!hybridCache.TryGetValue(memkey, out VideoResult hcache) || hcache?.streams == null || hcache.streams.Count == 0)
            {
                hcache = await LatStream(lhost, lat, s, e, v, a == 1);
                if (hcache == null || hcache.streams.Count == 0)
                    return OnError("ladoni stream", refresh_proxy: true);

                hybridCache.Set(memkey, hcache, DateTime.Now.AddMinutes(Math.Max(1, init.ladonicache)), inmemory: true);
            }

            return Reply(hcache, play);
        }

        if (string.IsNullOrEmpty(cid) || !cidRx.IsMatch(cid) || s < 0 || e < 0)
            return OnError();

        if (Chromium.Status == PlaywrightStatus.disabled)
            return OnError("chromium disabled");

        var cache = await LadoniStream(cid, s, e);
        if (cache == null)
            return OnError("ladoni stream", refresh_proxy: true);

        return Reply(cache, play);
    }

    /// <summary>Поток плеера ladoni (с кэшем и одним браузером на одинаковые запросы). null — не поймали.</summary>
    async Task<VideoResult> LadoniStream(string cid, int s, int e)
    {
        if (string.IsNullOrEmpty(cid) || !cidRx.IsMatch(cid) || Chromium.Status == PlaywrightStatus.disabled)
            return null;

        string memkey = $"redheadsound:ladoni:{cid}:{s}:{e}";

        if (hybridCache.TryGetValue(memkey, out VideoResult cache) && cache?.streams != null && cache.streams.Count > 0)
            return cache;

        var card = await CardForAsync(cid);

        // Lampa часто дёргает ссылку дважды (call + m3u8) — один браузер на оба запроса
        var task = ladoniInflight.GetOrAdd(memkey, _ => StreamLadoni(card, s, e));
        try
        {
            cache = await task;
        }
        finally
        {
            ladoniInflight.TryRemove(memkey, out _);
        }

        if (cache == null || cache.streams.Count == 0)
            return null;

        hybridCache.Set(memkey, cache, DateTime.Now.AddMinutes(Math.Max(1, init.ladonicache)), inmemory: true);
        return cache;
    }

    /// <summary>Ставит season/episode в адрес плеера ladoni, остальные параметры сохраняет.</summary>
    static string LatTarget(string src, int season, int episode)
    {
        if (string.IsNullOrEmpty(src) || episode <= 0)
            return src;

        string baseUrl = src.Split('?')[0];
        string query = src.Contains('?') ? src.Substring(src.IndexOf('?') + 1) : "";

        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("season=") && !p.StartsWith("episode="))
            .ToList();

        parts.Insert(0, $"episode={episode}");
        if (season > 0)
            parts.Insert(0, $"season={season}");

        return baseUrl + "?" + string.Join("&", parts);
    }

    static int LatParam(string src, string name)
    {
        var m = Regex.Match(src ?? "", $"[?&]{name}=(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    void Dbg(string msg)
    {
        if (init.debug)
            Console.WriteLine("Redheadsound ladoni: " + msg);
    }

    const string jsFindLat = @"() => {
        const f = [...document.querySelectorAll('iframe')].find(x => /\/lat\/\d+/.test(x.getAttribute('src') || '') || /\/lat\/\d+/.test(x.getAttribute('data-src') || ''));
        if (!f) return null;
        return f.getAttribute('src') && /\/lat\/\d+/.test(f.getAttribute('src')) ? f.src : f.getAttribute('data-src');
    }";

    // показать iframe на весь экран поверх всего: клик по центру попадёт в плеер, даже если вкладка скрыта
    const string jsShowLat = @"(src) => {
        let f = [...document.querySelectorAll('iframe')].find(x => /\/lat\/\d+/.test(x.getAttribute('src') || '') || /\/lat\/\d+/.test(x.getAttribute('data-src') || ''));
        if (!f) return 'no iframe';
        f.setAttribute('loading', 'eager');
        f.setAttribute('allow', 'autoplay; fullscreen; encrypted-media');
        let hidden = f.getClientRects().length === 0;
        if (hidden) document.body.appendChild(f);
        f.style.cssText = 'position:fixed;left:0;top:0;width:100vw;height:100vh;z-index:2147483647;display:block;visibility:visible;opacity:1;border:0;margin:0';
        if (src && f.src !== src) { f.src = src; return 'src set' + (hidden ? ', moved' : ''); }
        if (!f.getAttribute('src') && f.getAttribute('data-src')) { f.src = f.getAttribute('data-src'); return 'data-src'; }
        return hidden ? 'moved' : 'ok';
    }";

    // тексты выпадающих списков и элементов с озвучками/качествами внутри плеера
    const string jsPlayerUi = @"() => {
        const out = [];
        for (const s of document.querySelectorAll('select')) out.push('select[' + [...s.options].map(o => o.text.trim()).join(', ') + ']');
        const rx = /(translat|voice|dub|audio|озвуч|перевод|quality|качеств|season|сезон|episode|серия)/i;
        for (const el of document.querySelectorAll('[class],[id],[data-title]')) {
            const key = (el.className && el.className.baseVal === undefined ? el.className : '') + ' ' + (el.id || '');
            if (!rx.test(key)) continue;
            const t = (el.innerText || el.getAttribute('data-title') || '').replace(/\s+/g, ' ').trim();
            if (t && t.length < 160 && !out.includes(t)) out.push(t);
            if (out.length > 25) break;
        }
        try { if (window.Playerjs || window.pljssglobal) out.push('playerjs'); } catch (e) {}
        try { if (window.Hls) out.push('hls.js'); } catch (e) {}
        return out.join(' | ').slice(0, 1200) || '(пусто)';
    }";

    // запасной «клик» внутри плеера: Playerjs, video.js, обычный video
    const string jsPlayInFrame = @"() => {
        let n = 0;
        const sel = ['[class*=""play""]', '[id*=""play""]', '[aria-label*=""Play""]', '[aria-label*=""Воспроизв""]', '.vjs-big-play-button', 'pjsdiv[fid]', 'button'];
        for (const s of sel) { for (const el of [...document.querySelectorAll(s)].slice(0, 3)) { try { el.click(); n++; } catch (e) {} } }
        for (const v of document.querySelectorAll('video')) { try { v.muted = true; const p = v.play(); if (p && p.catch) p.catch(() => {}); n++; } catch (e) {} }
        return n;
    }";

    /// <summary>Сумма #EXTINF медиаплейлиста в секундах (0 — не плейлист или мастер).</summary>
    static double PlaylistDuration(string body)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("#EXTM3U"))
            return 0;

        double sum = 0;
        foreach (Match m in Regex.Matches(body, "#EXTINF:\\s*([0-9.]+)"))
        {
            if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                sum += d;
        }
        return sum;
    }

    async Task<VideoResult> StreamLadoni(SiteCard card, int s, int e)
    {
        var captured = new List<(string url, Dictionary<string, string> headers)>();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        object sync = new object();

        string mode = (init.ladonimode ?? "card").ToLowerInvariant();
        bool fake = mode == "fake" && !string.IsNullOrEmpty(card.lat);

        string cardKey = Uri.TryCreate(card.url, UriKind.Absolute, out var cu) ? cu.GetLeftPart(UriPartial.Path) : card.url;
        int timeout = Math.Clamp(init.ladonitimeout, 10, 90);
        var deadline = DateTime.Now.AddSeconds(timeout);

        Dbg($"start {card.url} s={s} e={e} mode={(fake ? "fake" : "card")} lat={card.lat ?? "-"}");

        try
        {
            using (var browser = new PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, proxy: proxy_data).ConfigureAwait(false);
                if (page == null)
                {
                    Dbg("Chromium page is null");
                    return null;
                }

                if (init.debug)
                {
                    page.Response += (_, r) =>
                    {
                        try
                        {
                            string u = r.Url;
                            if (Regex.IsMatch(u, "(/lat/|responce|response|jmap|\\.m3u8|/movies/|/api/)", RegexOptions.IgnoreCase))
                                Console.WriteLine($"Redheadsound ladoni: <- {r.Status} {u}");
                        }
                        catch { }
                    };
                }

                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        string url = route.Request.Url;
                        string rtype = route.Request.ResourceType;

                        if (fake && Uri.TryCreate(url, UriKind.Absolute, out var ru) && ru.GetLeftPart(UriPartial.Path).Equals(cardKey, StringComparison.OrdinalIgnoreCase) && rtype == "document" && route.Request.Frame == page.MainFrame)
                        {
                            string src = LatTarget(card.lat, s, e);
                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                ContentType = "text/html; charset=utf-8",
                                Body = $"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"referrer\" content=\"unsafe-url\"></head><body style=\"margin:0;background:#000\"><iframe src=\"{HttpUtility.HtmlAttributeEncode(src)}\" style=\"position:fixed;left:0;top:0;width:100vw;height:100vh;border:0\" allow=\"autoplay; fullscreen; encrypted-media\" allowfullscreen></iframe></body></html>"
                            });
                            return;
                        }

                        string path = url.Split('?')[0];

                        if (route.Request.Method == "GET" && path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                        {
                            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            try
                            {
                                foreach (var h in await route.Request.AllHeadersAsync())
                                    headers[h.Key.ToLowerInvariant()] = h.Value;
                            }
                            catch
                            {
                                foreach (var h in route.Request.Headers)
                                    headers[h.Key.ToLowerInvariant()] = h.Value;
                            }

                            // m3u8 принимаем только из плеера ladoni: на странице есть и другие плееры
                            // (cdnvideohub → rutube), которые по клику крутят случайные ролики
                            string furl = "";
                            try { furl = route.Request.Frame?.Url ?? ""; } catch { }
                            if (!fake && !latFrameRx.IsMatch(furl))
                            {
                                Dbg($"m3u8 other player ({furl}) skip {url}");
                                await route.AbortAsync();
                                return;
                            }

                            // реклама ladoni тоже приходит как m3u8 (ролик на несколько минут) —
                            // смотрим плейлист: мастер пропускаем дальше (проверим вариант), короткий ролик отбрасываем
                            string pbody = null;
                            IAPIResponse presp = null;
                            try
                            {
                                presp = await route.FetchAsync();
                                pbody = await presp.TextAsync();
                            }
                            catch (Exception fex)
                            {
                                Dbg("m3u8 fetch: " + fex.Message);
                            }

                            double dur = PlaylistDuration(pbody);
                            bool master = pbody != null && pbody.Contains("#EXT-X-STREAM-INF");
                            bool accept = pbody != null && !master && dur >= Math.Max(0, init.ladonimindur);

                            Dbg($"m3u8 {(master ? "master" : $"{dur:0}s")} {(accept ? "OK" : "skip")} {url}");

                            if (presp != null)
                            {
                                try { await route.FulfillAsync(new RouteFulfillOptions { Response = presp }); }
                                catch { try { await route.ContinueAsync(); } catch { } }
                            }
                            else
                            {
                                await route.ContinueAsync();
                            }

                            if (!accept)
                                return;

                            bool first;
                            lock (sync)
                            {
                                first = captured.Count == 0;
                                if (!captured.Any(c => c.url == url))
                                    captured.Add((url, headers));
                            }

                            if (first)
                            {
                                Dbg("headers " + JsonConvert.SerializeObject(headers));

                                // даём плееру ещё пару секунд: за первым плейлистом часто идут остальные качества
                                _ = Task.Delay(2500).ContinueWith(_ => done.TrySetResult(true));
                            }

                            return;
                        }

                        if (done.Task.IsCompleted ||
                            otherPlayersRx.IsMatch(url) ||
                            rtype is "image" or "media" or "font" ||
                            Regex.IsMatch(path, "\\.(ts|m4s|mp4|aac|vtt|srt|jpe?g|png|gif|webp|ico|woff2?)$", RegexOptions.IgnoreCase) ||
                            (init.ladoniabortads && adsRx.IsMatch(url)))
                        {
                            await route.AbortAsync();
                            return;
                        }

                        if (init.debug && Regex.IsMatch(url, "(/lat/|responce|response|jmap)", RegexOptions.IgnoreCase))
                            Console.WriteLine($"Redheadsound ladoni: -> {route.Request.Method} {url}");

                        await route.ContinueAsync();
                    }
                    catch (Exception ex)
                    {
                        Dbg("route error: " + ex.Message);
                    }
                });

                try
                {
                    await page.GotoAsync(card.url, new PageGotoOptions
                    {
                        Timeout = Math.Min(25, timeout) * 1000,
                        WaitUntil = WaitUntilState.DOMContentLoaded
                    });
                }
                catch (Exception ex)
                {
                    Dbg("goto: " + ex.Message);
                }

                Dbg("page " + page.Url);

                // 1. найти iframe ladoni (он может появиться не сразу — его рисует скрипт сайта)
                string src = null;
                while (DateTime.Now < deadline && !done.Task.IsCompleted)
                {
                    try
                    {
                        src = await page.EvaluateAsync<string>(jsFindLat);
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(src))
                        break;

                    await Task.WhenAny(done.Task, Task.Delay(500));
                }

                if (string.IsNullOrEmpty(src) && !done.Task.IsCompleted)
                {
                    Dbg("ladoni iframe not found on page");
                    return null;
                }

                Dbg("iframe " + src);

                // 2. какие season пробовать: сначала как на карточке сайта, потом номер сезона из Lampa
                var seasons = new List<int>();
                if (e > 0)
                {
                    int orig = LatParam(src, "season");
                    if (orig > 0)
                        seasons.Add(orig);
                    if (s > 0 && !seasons.Contains(s))
                        seasons.Add(s);
                    if (seasons.Count == 0)
                        seasons.Add(0);
                }
                else
                {
                    seasons.Add(-1);
                }

                int clicks = Math.Clamp(init.ladoniclicks, 1, 20);

                for (int si = 0; si < seasons.Count && captured.Count == 0 && !done.Task.IsCompleted && DateTime.Now < deadline; si++)
                {
                    string target = seasons[si] == -1 ? null : LatTarget(src, seasons[si], e);

                    try
                    {
                        string r = await page.EvaluateAsync<string>(jsShowLat, target);
                        Dbg($"show iframe: {r}; target {target ?? "as is"}");
                    }
                    catch (Exception ex)
                    {
                        Dbg("show iframe error: " + ex.Message);
                    }

                    // ждём, пока фрейм ladoni загрузится с нужным адресом
                    IFrame frame = null;
                    var frameDeadline = DateTime.Now.AddSeconds(10);
                    while (DateTime.Now < frameDeadline && DateTime.Now < deadline && !done.Task.IsCompleted)
                    {
                        frame = page.Frames.FirstOrDefault(f => Regex.IsMatch(f.Url ?? "", "/lat/\\d+") && (target == null || (LatParam(f.Url, "episode") == e && LatParam(f.Url, "season") == LatParam(target, "season"))));
                        if (frame != null)
                            break;

                        await Task.WhenAny(done.Task, Task.Delay(400));
                    }

                    if (frame == null)
                    {
                        Dbg("ladoni frame not loaded: " + string.Join(" | ", page.Frames.Select(f => f.Url)));
                        continue;
                    }

                    try
                    {
                        await frame.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new FrameWaitForLoadStateOptions { Timeout = 8000 });
                    }
                    catch { }

                    // ladoni вместо плеера может отдать заглушку «404 Not Found» (HTTP 200) — кликать бессмысленно
                    string ftext = "";
                    try
                    {
                        ftext = await frame.EvaluateAsync<string>("() => document.title + ' | ' + (document.body ? document.body.innerText.replace(/\\s+/g, ' ').slice(0, 200) : '')");
                    }
                    catch { }

                    if (Regex.IsMatch(ftext ?? "", "404 Not Found|Доступ запрещен|Access denied", RegexOptions.IgnoreCase))
                    {
                        Console.WriteLine($"Redheadsound ladoni: плеер вместо видео отдал заглушку ({ftext.Trim()}) — {frame.Url}");
                        continue;
                    }

                    if (init.debug)
                    {
                        try
                        {
                            Dbg($"frame {frame.Url} :: {ftext}");

                            // что предлагает сам плеер: озвучки, качества, сезоны
                            string ui = await frame.EvaluateAsync<string>(jsPlayerUi);
                            Dbg("player ui: " + ui);
                        }
                        catch { }
                    }

                    // 3. Play: настоящий клик мышью по центру (isTrusted), потом запасной клик скриптом
                    int perSeason = seasons.Count > 1 && si == 0 ? Math.Max(2, clicks / 2) : clicks;

                    for (int c = 0; c < perSeason && captured.Count == 0 && !done.Task.IsCompleted && DateTime.Now < deadline; c++)
                    {
                        try
                        {
                            var size = await page.EvaluateAsync<int[]>("() => [window.innerWidth, window.innerHeight]");
                            await page.Mouse.ClickAsync(size[0] / 2, size[1] / 2);
                            Dbg($"click {c + 1} at {size[0] / 2}x{size[1] / 2}");
                        }
                        catch (Exception ex)
                        {
                            Dbg("mouse click error: " + ex.Message);
                        }

                        await Task.WhenAny(done.Task, Task.Delay(2500));
                        if (done.Task.IsCompleted || captured.Count > 0)
                            break;

                        try
                        {
                            int n = await frame.EvaluateAsync<int>(jsPlayInFrame);
                            Dbg($"js play: {n} elements");
                        }
                        catch (Exception ex)
                        {
                            Dbg("js play error: " + ex.Message);
                        }

                        await Task.WhenAny(done.Task, Task.Delay(2000));
                        if (captured.Count > 0)
                            break;
                    }
                }

                if (captured.Count > 0 && !done.Task.IsCompleted)
                    await Task.WhenAny(done.Task, Task.Delay(3000));

                done.TrySetResult(true);
            }
        }
        catch (Exception ex)
        {
            Dbg("error: " + ex.Message);
        }

        List<(string url, Dictionary<string, string> headers)> list;
        lock (sync)
            list = captured.ToList();

        if (list.Count == 0)
        {
            Dbg("m3u8 not captured");
            return null;
        }

        return await BuildLadoniResult(list);
    }

    static int UrlQuality(string url)
    {
        var m = Regex.Match(url.Split('?')[0], "/(\\d{3,4})p?/[^/]*\\.m3u8$", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    async Task<VideoResult> BuildLadoniResult(List<(string url, Dictionary<string, string> headers)> list)
    {
        var watch = new StreamData();
        foreach (var h in list[0].headers)
        {
            if (h.Key.StartsWith(":"))
                continue;

            watch.headers[h.Key] = h.Value;
        }

        var result = new VideoResult { watch = watch };

        var fetchHeaders = new Dictionary<string, string>();
        foreach (var k in new[] { "accept", "origin", "referer", "user-agent", "accept-language", "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site" })
        {
            if (watch.headers.TryGetValue(k, out string v))
                fetchHeaders[k] = v;
        }

        async Task<string> fetch(string url)
        {
            try
            {
                return await httpHydra.Get(url, newheaders: HeadersModel.Init(fetchHeaders), useDefaultHeaders: false, safety: true);
            }
            catch
            {
                return null;
            }
        }

        // мастер-плейлист (#EXT-X-STREAM-INF) отдаём как есть — качества внутри
        foreach (var c in list)
        {
            if (UrlQuality(c.url) > 0)
                continue;

            string body = await fetch(c.url);
            Dbg($"check {c.url} -> {(body == null ? "null" : body.Length + " bytes")}");

            if (body != null && body.Contains("#EXT-X-STREAM-INF"))
            {
                result.streams.Add(new QualityLink { link = c.url, quality = "auto" });
                return result;
            }
        }

        var byQuality = new Dictionary<int, string>();
        foreach (var c in list)
        {
            int q = UrlQuality(c.url);
            if (!byQuality.ContainsKey(q))
                byQuality[q] = c.url;
        }

        // ladoni: …/{contentId}/{quality}/index.m3u8 — проверяем соседние качества
        string sample = list.Select(c => c.url).FirstOrDefault(u => UrlQuality(u) > 0);
        if (init.ladoniqualities && sample != null)
        {
            var candidates = new[] { 2160, 1440, 1080, 720, 480, 360 }
                .Where(q => !byQuality.ContainsKey(q) && (init.m4s || q < 1440))
                .ToList();

            var checks = candidates.Select(async q =>
            {
                string u = Regex.Replace(sample, "/\\d{3,4}(p?)/([^/?]*\\.m3u8)", $"/{q}$1/$2");
                string body = await fetch(u);
                return (q, u, ok: body != null && body.Contains("#EXTM3U"));
            });

            foreach (var r in await Task.WhenAll(checks))
            {
                Dbg($"quality {r.q}: {(r.ok ? "ok" : "no")}");
                if (r.ok)
                    byQuality[r.q] = r.u;
            }
        }

        foreach (var kv in byQuality.OrderByDescending(k => k.Key))
            result.streams.Add(new QualityLink { link = kv.Value, quality = kv.Key > 0 ? $"{kv.Key}p" : "auto" });

        return result;
    }
    #endregion
}
