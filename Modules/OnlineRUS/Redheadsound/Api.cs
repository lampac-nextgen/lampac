using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Redheadsound;

/// <summary>Озвучка серии в плеере ladoni.</summary>
public class LatVoice
{
    public long video_id { get; set; }

    public int voice_id { get; set; }
}

public class LatEpisode
{
    public int num { get; set; }

    public List<LatVoice> voices { get; set; } = new List<LatVoice>();
}

/// <summary>Страница плеера ladoni: window.playerData.</summary>
public class LatPage
{
    public string video { get; set; }

    public int season { get; set; }

    public int episode { get; set; }

    public int voice { get; set; }

    public Dictionary<int, string> voices { get; set; } = new Dictionary<int, string>();

    /// <summary>Сезоны по порядку (индекс 0 — сезон 1 плеера). Пусто — фильм.</summary>
    public List<List<LatEpisode>> seasons { get; set; } = new List<List<LatEpisode>>();
}

/// <summary>Ссылка на плеер ladoni из карточки сайта.</summary>
public class LatRef
{
    public string host { get; set; }

    public int id { get; set; }

    public bool adult { get; set; }

    public SiteCard card { get; set; }
}

/// <summary>Один сезон в Lampa → где он лежит в ladoni.</summary>
public class LatSeason
{
    public LatRef lat { get; set; }

    public int latSeason { get; set; }

    public List<LatEpisode> episodes { get; set; }

    public Dictionary<int, string> voices { get; set; }
}

/// <summary>
/// Плеер ladoni без браузера: страница /lat/{id}?season=&episode=&voice= сама содержит
/// подписанный m3u8 (playerData.config.video), список сезонов, серий и озвучек.
/// Страницу ladoni отдаёт только российским IP — запросы идут через прокси модуля.
/// </summary>
public partial class RedheadsoundController
{
    static readonly Regex latRefRx = new Regex("((?:https?:)?//[^/\"'\\s]+)/lat/(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex playerDataRx = new Regex("window\\.playerData\\s*=\\s*(\\{.*?\\});\\s*</script>", RegexOptions.Compiled | RegexOptions.Singleline);
    static readonly Regex rhsNameRx = new Regex("red\\s*head", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>t для объединённой озвучки RHS (TelegramBot ищет её по t=1 и имени).</summary>
    const int RhsT = 1;
    const int OtherT = 1000;

    bool HttpMode => (init.ladonimode ?? "http").Equals("http", StringComparison.OrdinalIgnoreCase);

    /// <summary>Хосты плеера, встреченные на карточках сайта: только к ним ходит параметр lh (защита от SSRF).</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> seenLatHosts = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

    static LatRef ParseLat(SiteCard card)
    {
        var m = latRefRx.Match(card?.lat ?? "");
        if (!m.Success)
            return null;

        // движок /lat/ бывает с протокол-относительной ссылкой (//host/lat/…)
        string lhost = m.Groups[1].Value.StartsWith("//") ? "https:" + m.Groups[1].Value : m.Groups[1].Value;
        seenLatHosts.TryAdd(lhost, 0);

        return new LatRef
        {
            host = lhost,
            id = int.Parse(m.Groups[2].Value),
            adult = card.lat.Contains("adult_mode=1"),
            card = card
        };
    }

    string SiteReferer() => (aliveRoot.root ?? SiteRoots().FirstOrDefault() ?? "https://redheadsound3.top") + "/";

    /// <summary>Страница плеера. voice = 0 — озвучка по умолчанию. Кэш 3 мин (m3u8 подписан временем).</summary>
    async Task<LatPage> LoadLat(string lhost, int id, int s, int e, int voice, bool adult)
    {
        string url = $"{lhost}/lat/{id}";
        var q = new List<string>();
        if (s > 0) q.Add($"season={s}");
        if (e > 0) q.Add($"episode={e}");
        if (voice > 0) q.Add($"voice={voice}");
        if (adult) q.Add("adult_mode=1");
        if (q.Count > 0)
            url += "?" + string.Join("&", q);

        string memkey = "redheadsound:lat:" + url;
        if (hybridCache.TryGetValue(memkey, out LatPage cached) && cached != null)
            return cached;

        string html = await httpHydra.Get(url, addheaders: HeadersModel.Init(
            ("referer", SiteReferer()),
            ("sec-fetch-dest", "iframe")
        ));

        var page = ParseLatPage(html);
        if (page == null)
        {
            Console.WriteLine($"Redheadsound ladoni: {url} — нет данных плеера ({(html == null ? "нет ответа" : html.Length + " байт, заглушка")}); ladoni пускает только российские IP — нужен прокси в секции Redheadsound");
            return null;
        }

        Dbg($"lat {url} -> s{page.season}e{page.episode} voice {page.voice} {page.video}");
        hybridCache.Set(memkey, page, DateTime.Now.AddMinutes(Math.Max(1, init.ladonicache)), inmemory: true);
        return page;
    }

    internal static LatPage ParseLatPage(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = playerDataRx.Match(html);
        if (!m.Success)
            return null;

        try
        {
            var d = JObject.Parse(m.Groups[1].Value);
            var page = new LatPage { video = d.SelectToken("config.video")?.ToString() };

            if (d["voices"] is JObject vs)
            {
                foreach (var p in vs.Properties())
                {
                    if (int.TryParse(p.Name, out int vid))
                        page.voices[vid] = p.Value?.ToString();
                }
            }

            var cur = d.SelectToken("playlist.serial.current");
            if (cur != null)
            {
                page.season = cur.Value<int?>("season") ?? 0;
                page.episode = cur.Value<int?>("episode") ?? 0;
                page.voice = cur.Value<int?>("voiceId") ?? 0;
            }

            if (d.SelectToken("playlist.serial.list") is JArray list)
            {
                foreach (var season in list.OfType<JArray>())
                {
                    var eps = new List<LatEpisode>();
                    foreach (var ep in season.OfType<JObject>())
                    {
                        var le = new LatEpisode { num = ep.Value<int?>("num") ?? 0 };
                        if (ep["voices"] is JArray evs)
                        {
                            foreach (var v in evs.OfType<JObject>())
                                le.voices.Add(new LatVoice { video_id = v.Value<long?>("video_id") ?? 0, voice_id = v.Value<int?>("voice_id") ?? 0 });
                        }

                        if (le.num != 0)
                            eps.Add(le);
                    }
                    page.seasons.Add(eps);
                }
            }

            return string.IsNullOrEmpty(page.video) && page.seasons.Count == 0 ? null : page;
        }
        catch
        {
            return null;
        }
    }

    static bool IsRhs(Dictionary<int, string> voices, int id) =>
        voices != null && voices.TryGetValue(id, out string n) && n != null && rhsNameRx.IsMatch(n);

    /// <summary>Озвучка серии для t: RHS — первая из озвучек RHS у этой серии, иначе конкретный id.</summary>
    static int VoiceFor(LatEpisode ep, Dictionary<int, string> voices, int t)
    {
        if (t == RhsT)
            return ep.voices.Select(v => v.voice_id).FirstOrDefault(id => IsRhs(voices, id));

        int id = t - OtherT;
        return ep.voices.Any(v => v.voice_id == id) ? id : 0;
    }

    /// <summary>Сезоны Lampa по всем плеерам ladoni с карточек сайта.</summary>
    async Task<SortedDictionary<int, LatSeason>> LatSeasons(List<SiteCard> cards)
    {
        var result = new SortedDictionary<int, LatSeason>();
        var refs = cards.Select(ParseLat).Where(r => r != null).GroupBy(r => r.id).Select(g => g.First()).Take(4).ToList();

        var pages = await Task.WhenAll(refs.Select(async r => (r, page: await LoadLat(r.host, r.id, 0, 0, 0, r.adult))));

        foreach (var (r, page) in pages)
        {
            if (page == null || page.seasons.Count == 0)
                continue;

            for (int i = 0; i < page.seasons.Count; i++)
            {
                if (page.seasons[i].Count == 0)
                    continue;

                // плеер с одним сезоном на карточке «5 сезон» — это 5 сезон в Lampa
                int lampa = page.seasons.Count == 1 && r.card.season > 1 ? r.card.season : i + 1;
                if (!result.ContainsKey(lampa))
                    result[lampa] = new LatSeason { lat = r, latSeason = i + 1, episodes = page.seasons[i], voices = page.voices };
            }
        }

        return result;
    }

    (string link, string stream) LatLink(LatRef r, int s, int e, int voice)
    {
        string q = $"?lat={r.id}&s={s}&e={e}&v={voice}" + (r.adult ? "&a=1" : "") + (r.host.Contains("://ladoni.pro") ? "" : "&lh=" + Uri.EscapeDataString(r.host));
        return ($"{host}/lite/redheadsound/ladoni{q}", accsArgs($"{host}/lite/redheadsound/ladoni.m3u8{q}&play=true"));
    }

    /// <summary>Lampa: фильм/сезоны/озвучки/серии из плеера ladoni.</summary>
    async Task<ActionResult> IndexHttp(List<SiteCard> cards, string title, string original_title, int serial, int t, int s, bool rjson, string defaultargs)
    {
        var seasons = await LatSeasons(cards);

        if (serial == 1 && seasons.Count == 0)
            return OnError("ladoni", refresh_proxy: true);

        if (serial == 0 || seasons.Count == 0)
        {
            #region Фильм
            var r = cards.Select(ParseLat).FirstOrDefault(x => x != null);
            if (r == null)
                return OnError("ladoni");

            var page = await LoadLat(r.host, r.id, 0, 0, 0, r.adult);
            if (page == null || string.IsNullOrEmpty(page.video))
                return OnError("ladoni", refresh_proxy: true);

            var mtpl = new MovieTpl(title, original_title, 1);
            var (link, stream) = LatLink(r, 0, 0, 0);
            string vname = page.voices.TryGetValue(page.voice, out string n) && !string.IsNullOrEmpty(n) ? n : VoiceName;
            mtpl.Append(vname, link, "call", stream, voice_name: "плеер сайта");
            return ContentTpl(mtpl);
            #endregion
        }

        if (s == -1)
        {
            var stpl = new SeasonTpl(seasons.Count);
            foreach (int i in seasons.Keys)
                stpl.Append($"{i} сезон", $"{host}/lite/redheadsound?rjson={rjson}&s={i}{defaultargs}", i.ToString());

            return ContentTpl(stpl);
        }

        if (!seasons.TryGetValue(s, out var ls))
            return OnError("season");

        // озвучки сезона: RHS (все её варианты одной строкой) первой, остальные — по числу серий
        bool hasRhs = ls.episodes.Any(ep => ep.voices.Any(v => IsRhs(ls.voices, v.voice_id)));
        var others = ls.episodes.SelectMany(ep => ep.voices.Select(v => v.voice_id))
            .Where(id => !IsRhs(ls.voices, id))
            .GroupBy(id => id)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

        if (!init.ladoniallvoices)
            others.Clear();

        if (t == -1)
            t = hasRhs ? RhsT : (others.Count > 0 ? OtherT + others[0] : RhsT);

        var vtpl = new VoiceTpl(others.Count + 1);
        if (hasRhs)
            vtpl.Append(VoiceName, t == RhsT, $"{host}/lite/redheadsound?rjson={rjson}&s={s}&t={RhsT}{defaultargs}");

        foreach (int id in others)
        {
            string name = ls.voices.TryGetValue(id, out string vn) && !string.IsNullOrEmpty(vn) ? vn : $"Озвучка {id}";
            vtpl.Append(name, t == OtherT + id, $"{host}/lite/redheadsound?rjson={rjson}&s={s}&t={OtherT + id}{defaultargs}");
        }

        string voiceLabel = t == RhsT ? VoiceSite : (ls.voices.TryGetValue(t - OtherT, out string tn) ? tn : "");

        var etpl = new EpisodeTpl(vtpl);
        foreach (var ep in ls.episodes.OrderBy(x => x.num))
        {
            int voice = VoiceFor(ep, ls.voices, t);
            if (voice == 0 || ep.num <= 0)
                continue;

            var (link, stream) = LatLink(ls.lat, ls.latSeason, ep.num, voice);
            etpl.Append(
                $"{ep.num} серия",
                title ?? original_title,
                s.ToString(),
                ep.num.ToString(),
                link,
                "call",
                voice_name: voiceLabel,
                streamlink: stream
            );
        }

        return ContentTpl(etpl);
    }

    /// <summary>Поток серии: страница плеера → master m3u8 → качества.</summary>
    async Task<VideoResult> LatStream(string lhost, int lat, int s, int e, int v, bool adult)
    {
        var page = await LoadLat(lhost, lat, s, e, v, adult);
        if (page == null || string.IsNullOrEmpty(page.video))
            return null;

        if (s > 0 && e > 0 && page.seasons.Count > 0 && (page.season != s || page.episode != e))
        {
            Console.WriteLine($"Redheadsound ladoni: просили s{s}e{e}, плеер отдал s{page.season}e{page.episode} — {lat}");
            return null;
        }

        string origin = Regex.Match(page.video, "^https?://[^/]+").Value;
        if (string.IsNullOrEmpty(origin))
            origin = lhost;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["origin"] = lhost,
            ["referer"] = lhost + "/",
            ["user-agent"] = Shared.Services.Http.UserAgent
        };

        var result = new VideoResult { watch = new StreamData { headers = headers } };

        string master = await httpHydra.Get(page.video, addheaders: HeadersModel.Init(("origin", lhost), ("referer", lhost + "/")));
        if (master != null && master.Contains("#EXT-X-STREAM-INF"))
        {
            var lines = master.Split('\n').Select(l => l.Trim()).ToList();
            var byQ = new SortedDictionary<int, string>(Comparer<int>.Create((a, b) => b.CompareTo(a)));
            for (int i = 0; i < lines.Count - 1; i++)
            {
                if (!lines[i].StartsWith("#EXT-X-STREAM-INF"))
                    continue;

                string u = lines.Skip(i + 1).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#"));
                if (string.IsNullOrEmpty(u))
                    continue;

                if (!u.StartsWith("http"))
                    u = u.StartsWith("/") ? origin + u : page.video.Substring(0, page.video.LastIndexOf('/') + 1) + u;

                var rm = Regex.Match(lines[i], "RESOLUTION=\\d+x(\\d+)");
                int h = rm.Success ? int.Parse(rm.Groups[1].Value) : 0;
                if (h == 0)
                {
                    var um = Regex.Match(u, "/(\\d{3,4})/[^/]*\\.m3u8");
                    h = um.Success ? int.Parse(um.Groups[1].Value) : 0;
                }

                if (h >= 1440 && !init.m4s)
                    continue;

                if (h > 0 && !byQ.ContainsKey(h))
                    byQ[h] = u;
            }

            foreach (var kv in byQ)
                result.streams.Add(new QualityLink { link = kv.Value, quality = $"{kv.Key}p" });
        }
        else
        {
            Dbg($"master {(master == null ? "нет ответа" : "без вариантов")}: {page.video}");
        }

        if (result.streams.Count == 0)
            result.streams.Add(new QualityLink { link = page.video, quality = "auto" });

        return result;
    }
}
