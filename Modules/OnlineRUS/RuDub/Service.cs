using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RuDub;

public static class RudubConf
{
    public const string DefaultHost = "https://play26.ru-dub.xyz";
    public const string DefaultTracker = "https://r4.rudub.world";
    public const string DefaultPlayer = "https://player.ladonyvesna2005.info";

    public const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public const string Accept = "text/html,application/xhtml+xml,*/*;q=0.8";
    public const string AcceptLang = "ru-RU,ru;q=0.9";

    public static readonly TimeSpan CatalogTTL = TimeSpan.FromHours(6);
    public static readonly TimeSpan ShowTTL = TimeSpan.FromMinutes(90);
    public static readonly TimeSpan NotFoundTTL = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan NodeDeadTTL = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan NodeAliveTTL = TimeSpan.FromMinutes(20);

    public static readonly HashSet<string> BadNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cdn1.anivids.link"
    };

    public const int MaxListPages = 10;

    public const int SeasonWorkers = 6;

    public const int MaxInflight = 12;

    public const int FetchTimeoutSeconds = 15;
}

public static class RudubRe
{
    public static readonly Regex CatalogRow = new Regex(
        "<a href=\"https?://[^\"]+?/([a-z0-9][a-z0-9\\-]*)/\"\\s*>([^<]*)<span>(\\d+)</span></a>",
        RegexOptions.Compiled);

    public static readonly Regex EpisodeCard = new Regex(
        "href=\"https?://[^\"]+?/video/(\\d+)\"[\\s\\S]{0,400}?poster__meta[^>]*>\\s*(\\d+)\\s*сезон,\\s*(\\d+)\\s*сери",
        RegexOptions.Compiled);

    public static readonly Regex Iframe = new Regex("<iframe[^>]+src=\"([^\"]+)\"", RegexOptions.Compiled);

    public static readonly Regex PlaylistSpan = new Regex(
        "<span[^>]+data=\"([^\"]+)\"[^>]*>\\s*(\\d+)\\s*сери", RegexOptions.Compiled);

    public static readonly Regex Filename = new Regex("event-filename=\"([^\"]*)\"", RegexOptions.Compiled);

    public static readonly Regex PlayerSrc = new Regex(
        "^(https?://[\\w.-]+(?::\\d+)?)/index\\.php\\?v=/([^\"&]+)", RegexOptions.Compiled);

    public static readonly Regex PlayerHost = new Regex(
        "https://play\\d*\\.ru-?dub\\.[a-z]{2,6}", RegexOptions.Compiled);

    public static readonly Regex Variant = new Regex(
        "^(https?://[^\\r\\n]+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    public static readonly Regex ReleaseQuality = new Regex(
        "(?:HD)?(2160|1080|720|480)[pр]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex Hash = new Regex(
        "^s\\d{1,4}/[A-Za-z0-9]{8,64}$", RegexOptions.Compiled);
}

public static class RudubUtil
{
    public static string NormalizeHost(string raw)
    {
        string h = (raw ?? string.Empty).Trim().TrimEnd('/');
        if (h.Length == 0)
            return string.Empty;

        if (!h.Contains("://"))
            h = "https://" + h;

        return h;
    }

    public static string Host(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        int start = url.IndexOf("//", StringComparison.Ordinal);
        start = start < 0 ? 0 : start + 2;

        int end = url.IndexOf('/', start);
        if (end < 0)
            end = url.Length;

        return url.Substring(start, end - start);
    }

    public static string NormalizeTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);

        foreach (char ch in raw.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    public static string SlugKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string v = raw.Trim().ToLowerInvariant();
        var sb = new StringBuilder(v.Length);

        foreach (char ch in v)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                sb.Append(ch);
        }

        string outp = sb.ToString();

        if (outp.Length > 6 && outp.EndsWith("serial", StringComparison.Ordinal))
            outp = outp.Substring(0, outp.Length - 6);

        return outp;
    }

    public static string SanitizeSlug(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string slug = raw.Trim().ToLowerInvariant();

        if (slug.Length == 0 || slug.Length > 120)
            return string.Empty;

        foreach (char ch in slug)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-')
                continue;

            return string.Empty;
        }

        return slug;
    }

    public static string Unescape(string v)
    {
        if (string.IsNullOrEmpty(v))
            return v;

        return UnescapeOnce(UnescapeOnce(v));
    }

    static string UnescapeOnce(string v)
    {
        return v
            .Replace("&quot;", "\"")
            .Replace("&#039;", "'")
            .Replace("&apos;", "'")
            .Replace("&laquo;", "«")
            .Replace("&raquo;", "»")
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&");
    }

    public static string QualityFromRelease(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return string.Empty;

        var m = RudubRe.ReleaseQuality.Match(filename);
        return m.Success ? m.Groups[1].Value + "p" : string.Empty;
    }

    public static string QualityLabel(string raw)
    {
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "2160p":
            case "2160":
                return "2160p";
            case "1080p":
            case "1080":
                return "1080p";
            case "720p":
            case "720":
                return "720p";
            case "480p":
            case "480":
                return "480p";
        }

        return string.Empty;
    }

    public static int QualityRank(string label)
    {
        switch (label)
        {
            case "2160p":
                return 4;
            case "1080p":
                return 3;
            case "720p":
                return 2;
            case "480p":
                return 1;
        }

        return 0;
    }

    public static string First(string body, Regex re)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        var m = re.Match(body);
        return m.Success && m.Groups.Count > 1 ? m.Groups[1].Value : null;
    }
}

public static class RudubStore
{
    class Probe
    {
        public bool playable;
        public DateTime expires;
    }

    static readonly object _lock = new object();
    static readonly SemaphoreSlim _catalogGate = new SemaphoreSlim(1, 1);
    static readonly SemaphoreSlim _inflight = new SemaphoreSlim(RudubConf.MaxInflight, RudubConf.MaxInflight);

    static List<RudubCatalogItem> _catalog;
    static DateTime _catalogExpires;

    static readonly Dictionary<string, RudubShow> _shows = new Dictionary<string, RudubShow>();
    static readonly Dictionary<string, Probe> _probes = new Dictionary<string, Probe>();

    static string _host;
    static string _player = RudubConf.DefaultPlayer;

    #region Ворота
    public static async Task<bool> Enter()
    {
        return await _inflight.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }

    public static void Leave()
    {
        _inflight.Release();
    }
    #endregion

    #region Хосты
    public static string GetHost(string configured)
    {
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(_host))
                return _host;
        }

        string host = RudubUtil.NormalizeHost(configured);
        if (host.Length == 0)
            host = RudubConf.DefaultHost;

        lock (_lock)
        {
            if (string.IsNullOrEmpty(_host))
                _host = host;

            return _host;
        }
    }

    public static void SetHost(string host)
    {
        lock (_lock)
        {
            _host = host;
        }
    }

    public static void ResetForMove(string host)
    {
        lock (_lock)
        {
            _host = host;
            _catalog = null;
            _catalogExpires = DateTime.MinValue;
            _shows.Clear();
            _probes.Clear();
        }
    }

    public static string Player
    {
        get
        {
            lock (_lock)
            {
                return string.IsNullOrEmpty(_player) ? RudubConf.DefaultPlayer : _player;
            }
        }
        set
        {
            string host = (value ?? string.Empty).Trim().TrimEnd('/');
            if (host.Length == 0)
                return;

            lock (_lock)
            {
                _player = host;
            }
        }
    }
    #endregion

    #region Каталог
    public static List<RudubCatalogItem> Catalog()
    {
        lock (_lock)
        {
            if (_catalog != null && _catalog.Count > 0 && DateTime.Now < _catalogExpires)
                return _catalog;

            return null;
        }
    }

    public static List<RudubCatalogItem> StaleCatalog()
    {
        lock (_lock)
        {
            return _catalog != null && _catalog.Count > 0 ? _catalog : null;
        }
    }

    public static void SetCatalog(List<RudubCatalogItem> items)
    {
        lock (_lock)
        {
            _catalog = items;
            _catalogExpires = DateTime.Now.Add(RudubConf.CatalogTTL);
        }
    }

    public static async Task<bool> EnterCatalogGate()
    {
        return await _catalogGate.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
    }

    public static void LeaveCatalogGate()
    {
        _catalogGate.Release();
    }
    #endregion

    #region Дерево сериала
    public static RudubShow GetShow(string slug)
    {
        lock (_lock)
        {
            if (_shows.TryGetValue(slug, out RudubShow show) && DateTime.Now < show.expires)
                return show;

            return null;
        }
    }

    public static void SetShow(string slug, RudubShow show)
    {
        lock (_lock)
        {
            if (_shows.Count > 512)
            {
                DateTime now = DateTime.Now;

                foreach (string key in _shows.Where(i => now >= i.Value.expires).Select(i => i.Key).ToList())
                    _shows.Remove(key);
            }

            _shows[slug] = show;
        }
    }
    #endregion

    #region Проба
    public static bool? GetProbe(string slug)
    {
        lock (_lock)
        {
            if (_probes.TryGetValue(slug, out Probe probe) && DateTime.Now < probe.expires)
                return probe.playable;

            return null;
        }
    }

    public static void SetProbe(string slug, bool playable)
    {
        lock (_lock)
        {
            if (_probes.Count > 2048)
            {
                DateTime now = DateTime.Now;

                foreach (string key in _probes.Where(i => now >= i.Value.expires).Select(i => i.Key).ToList())
                    _probes.Remove(key);
            }

            _probes[slug] = new Probe()
            {
                playable = playable,
                expires = DateTime.Now.Add(playable ? RudubConf.ShowTTL : RudubConf.NotFoundTTL)
            };
        }
    }
    #endregion

    #region Узлы CDN
    public enum NodeState
    {
        Unknown,
        Dead,
        Alive
    }

    static readonly Dictionary<string, NodeState> _nodes = new Dictionary<string, NodeState>();
    static readonly Dictionary<string, DateTime> _nodeExpires = new Dictionary<string, DateTime>();

    public static NodeState GetNode(string node)
    {
        lock (_lock)
        {
            if (_nodeExpires.TryGetValue(node, out DateTime expires) && DateTime.Now < expires)
                return _nodes[node];

            return NodeState.Unknown;
        }
    }

    public static void SetNode(string node, NodeState state)
    {
        lock (_lock)
        {
            if (_nodeExpires.Count > 256)
            {
                DateTime now = DateTime.Now;

                foreach (string key in _nodeExpires.Where(i => now >= i.Value).Select(i => i.Key).ToList())
                {
                    _nodeExpires.Remove(key);
                    _nodes.Remove(key);
                }
            }

            _nodes[node] = state;
            _nodeExpires[node] = DateTime.Now.Add(state == NodeState.Dead ? RudubConf.NodeDeadTTL : RudubConf.NodeAliveTTL);
        }
    }

    public static bool IsBadNode(string node)
        => !string.IsNullOrEmpty(node) && RudubConf.BadNodes.Contains(node);
    #endregion
}

public struct RuDubInvoke
{
    #region RuDubInvoke
    OnlinesSettings init;
    HttpHydra httpHydra;

    public RuDubInvoke(OnlinesSettings init, HttpHydra httpHydra)
    {
        this.init = init;
        this.httpHydra = httpHydra;
    }
    #endregion

    #region Seasons
    async public Task<List<RudubSeason>> Seasons(string title, string original_title, bool checksearch)
    {
        var item = await PickShow(title, original_title);
        if (item == null)
            return null;

        var cached = RudubStore.GetShow(item.slug);
        if (cached != null)
            return cached.seasons.Count > 0 ? cached.seasons : null;

        if (checksearch)
        {
            bool? probe = RudubStore.GetProbe(item.slug);

            if (probe.HasValue && !probe.Value)
                return null;

            var cards = await FetchShowPage(item.slug, 1);
            if (cards == null || cards.Count == 0)
                return null;

            if (!probe.HasValue)
            {
                bool playable = await ProbeSeason(item.slug, cards[0]);
                RudubStore.SetProbe(item.slug, playable);

                if (!playable)
                    return null;
            }

            var nums = cards.Select(i => i.season).Distinct().OrderBy(i => i);

            var probeSeasons = new List<RudubSeason>();
            foreach (int num in nums)
                probeSeasons.Add(new RudubSeason() { num = num });

            return probeSeasons;
        }

        var show = await ResolveShow(item.slug);
        return show != null && show.seasons.Count > 0 ? show.seasons : null;
    }
    #endregion

    #region Season
    async public Task<RudubSeason> Season(string title, string original_title, int seasonNum)
    {
        var item = await PickShow(title, original_title);
        if (item == null)
            return null;

        var show = await ResolveShow(item.slug);
        if (show == null)
            return null;

        return show.seasons.FirstOrDefault(i => i.num == seasonNum);
    }
    #endregion

    #region Streams
    async public Task<RudubStream> Streams(string hash, string quality, string ua)
    {
        if (string.IsNullOrEmpty(hash) || !RudubRe.Hash.IsMatch(hash.Trim()))
            return null;

        hash = hash.Trim();

        string player = RudubStore.Player;
        string label = RudubUtil.QualityLabel(quality);
        RudubStream last = null;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            string body = await Fetch($"{player}/vid.php?v=/{hash}", $"{player}/", ua);
            if (body == null)
                continue;

            var variants = ParseVariants(body, label);
            if (variants.Count == 0)
                continue;

            var stream = new RudubStream()
            {
                player = player,
                variants = variants
            };

            string node = RudubUtil.Host(stream.variants[0].url);

            if (RudubStore.IsBadNode(node))
                continue;

            last = stream;

            if (RudubStore.GetNode(node) == RudubStore.NodeState.Dead)
                continue;

            if (RudubStore.GetNode(node) == RudubStore.NodeState.Alive)
                return stream;

            string probe = await Fetch(stream.variants[0].url, $"{player}/", ua);

            if (probe != null && probe.Contains("#EXTM3U"))
            {
                RudubStore.SetNode(node, RudubStore.NodeState.Alive);
                return stream;
            }

            RudubStore.SetNode(node, RudubStore.NodeState.Dead);
        }

        return last;
    }
    #endregion

    #region Каталог и подбор сериала
    async Task<RudubCatalogItem> PickShow(string title, string original_title)
    {
        var items = await EnsureCatalog();
        if (items == null)
            return null;

        return Pick(items, title, original_title);
    }

    static RudubCatalogItem Pick(List<RudubCatalogItem> items, string title, string original_title)
    {
        string wantName = RudubUtil.NormalizeTitle(title);
        string wantSlug = RudubUtil.SlugKey(original_title);
        string wantSlugRu = RudubUtil.SlugKey(title);

        if (wantName.Length == 0 && wantSlug.Length == 0)
            return null;

        RudubCatalogItem byName = null, bySlug = null;

        foreach (var item in items)
        {
            bool nameHit = wantName.Length > 0 && RudubUtil.NormalizeTitle(item.name) == wantName;
            string slugKey = RudubUtil.SlugKey(item.slug);
            bool slugHit = slugKey.Length > 0 && (slugKey == wantSlug || (wantSlug.Length == 0 && slugKey == wantSlugRu));

            if (nameHit && slugHit)
                return item;

            if (nameHit && byName == null)
                byName = item;

            if (slugHit && bySlug == null)
                bySlug = item;
        }

        return byName ?? bySlug;
    }

    async Task<List<RudubCatalogItem>> EnsureCatalog()
    {
        var items = RudubStore.Catalog();
        if (items != null)
            return items;

        await RudubStore.EnterCatalogGate();

        try
        {
            items = RudubStore.Catalog();
            if (items != null)
                return items;

            string host = ResolveHost();
            string body = await Fetch($"{host}/catalogue.html", $"{host}/");

            if (body == null)
            {
                string next = await DiscoverHost();
                if (next != null)
                    body = await Fetch($"{next}/catalogue.html", $"{next}/");
            }

            if (body == null)
            {
                return RudubStore.StaleCatalog();
            }

            var parsed = ParseCatalog(body);
            if (parsed.Count == 0)
                return RudubStore.StaleCatalog();

            RudubStore.SetCatalog(parsed);
            return parsed;
        }
        finally
        {
            RudubStore.LeaveCatalogGate();
        }
    }

    static List<RudubCatalogItem> ParseCatalog(string html)
    {
        var matches = RudubRe.CatalogRow.Matches(html);
        if (matches.Count == 0)
            return new List<RudubCatalogItem>();

        var outp = new List<RudubCatalogItem>(matches.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in matches)
        {
            string slug = m.Groups[1].Value.Trim();
            string name = RudubUtil.Unescape(m.Groups[2].Value).Trim();

            if (slug.Length == 0 || name.Length == 0)
                continue;

            if (!seen.Add(slug))
                continue;

            int.TryParse(m.Groups[3].Value, out int episodes);

            outp.Add(new RudubCatalogItem() { slug = slug, name = name, episodes = episodes });
        }

        return outp;
    }
    #endregion

    #region Дерево сериала
    async Task<RudubShow> ResolveShow(string slug)
    {
        var cached = RudubStore.GetShow(slug);
        if (cached != null)
            return cached.seasons.Count > 0 ? cached : null;

        var candidates = await CollectSeasonCards(slug);

        if (candidates == null || candidates.Count == 0)
        {
            RudubStore.SetShow(slug, new RudubShow() { expires = DateTime.Now.Add(RudubConf.NotFoundTTL) });
            return null;
        }

        var nums = candidates.Keys.OrderBy(i => i).ToList();
        var results = new RudubSeason[nums.Count];

        var gate = new SemaphoreSlim(RudubConf.SeasonWorkers, RudubConf.SeasonWorkers);

        var self = this;

        try
        {
            var tasks = new Task[nums.Count];

            for (int i = 0; i < nums.Count; i++)
            {
                int idx = i;

                tasks[idx] = Task.Run(async () =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);

                    try
                    {
                        results[idx] = await self.ResolveSeason(slug, nums[idx], candidates[nums[idx]]);
                    }
                    finally
                    {
                        gate.Release();
                    }
                });
            }

            await Task.WhenAll(tasks);
        }
        finally
        {
            gate.Dispose();
        }

        var seasons = new List<RudubSeason>(nums.Count);

        foreach (var season in results)
        {
            if (season != null && season.episodes.Count > 0)
                seasons.Add(season);
        }

        var show = new RudubShow()
        {
            seasons = seasons,
            expires = DateTime.Now.Add(seasons.Count > 0 ? RudubConf.ShowTTL : RudubConf.NotFoundTTL)
        };

        RudubStore.SetShow(slug, show);
        return seasons.Count > 0 ? show : null;
    }

    async Task<bool> ProbeShow(string slug)
    {
        var cards = await FetchShowPage(slug, 1);
        if (cards == null || cards.Count == 0)
            return false;

        return await ProbeSeason(slug, cards[0]);
    }

    async Task<bool> ProbeSeason(string slug, RudubCard card)
    {
        var season = await ResolveSeason(slug, card.season, new List<string>() { card.episodeID });
        return season != null && season.episodes.Count > 0;
    }

    async Task<Dictionary<int, List<string>>> CollectSeasonCards(string slug)
    {
        var cards = new Dictionary<int, List<string>>();
        bool fetched = false;

        for (int page = 1; page <= RudubConf.MaxListPages; page++)
        {
            var body = await FetchShowPage(slug, page);
            if (body == null || body.Count == 0)
                break;

            fetched = true;

            foreach (var card in body)
            {
                if (!cards.TryGetValue(card.season, out List<string> list))
                    cards[card.season] = list = new List<string>(3);

                if (list.Count < 3)
                    list.Add(card.episodeID);
            }

            if (HaveAllSeasons(cards))
                break;
        }

        return fetched ? cards : null;
    }

    static bool HaveAllSeasons(Dictionary<int, List<string>> cards)
    {
        if (cards.Count == 0)
            return false;

        int max = cards.Keys.Max();

        for (int n = 1; n <= max; n++)
        {
            if (!cards.TryGetValue(n, out List<string> list) || list.Count == 0)
                return false;
        }

        return true;
    }

    async Task<List<RudubCard>> FetchShowPage(string slug, int page)
    {
        string host = ResolveHost();
        string target = page > 1 ? $"{host}/{slug}/page/{page}/" : $"{host}/{slug}/";

        string body = await Fetch(target, $"{host}/");
        if (body == null)
            return null;

        return ParseCards(body);
    }

    static List<RudubCard> ParseCards(string html)
    {
        var matches = RudubRe.EpisodeCard.Matches(html);
        if (matches.Count == 0)
            return new List<RudubCard>();

        var outp = new List<RudubCard>(matches.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in matches)
        {
            string id = m.Groups[1].Value;
            if (!seen.Add(id))
                continue;

            int.TryParse(m.Groups[2].Value, out int season);
            int.TryParse(m.Groups[3].Value, out int episode);

            if (season <= 0)
                continue;

            outp.Add(new RudubCard() { season = season, episode = episode, episodeID = id });
        }

        return outp;
    }

    async Task<RudubSeason> ResolveSeason(string slug, int seasonNum, List<string> episodeIDs)
    {
        string host = ResolveHost();

        foreach (string id in episodeIDs)
        {
            string page = await Fetch($"{host}/{slug}/video/{id}", $"{host}/");
            if (page == null)
                continue;

            var frame = ParseIframe(page);

            if (frame == null)
                continue;

            RudubStore.Player = frame.Value.player;

            string body = await Fetch($"{frame.Value.player}/index.php?v=/{frame.Value.hash}&playlist", $"{host}/");

            if (body == null)
            {
                return new RudubSeason()
                {
                    num = seasonNum,
                    episodes = new List<RudubEpisode>() { new RudubEpisode() { num = 1, hash = frame.Value.hash } }
                };
            }

            var episodes = ParsePlaylist(body);
            string quality = RudubUtil.QualityFromRelease(RudubUtil.First(body, RudubRe.Filename));

            if (episodes.Count == 0)
                episodes = new List<RudubEpisode>() { new RudubEpisode() { num = 1, hash = frame.Value.hash } };

            return new RudubSeason() { num = seasonNum, quality = quality, episodes = episodes };
        }

        return null;
    }

    static (string player, string hash)? ParseIframe(string html)
    {
        foreach (Match m in RudubRe.Iframe.Matches(html))
        {
            string src = m.Groups[1].Value.Trim();

            if (src.StartsWith("//", StringComparison.Ordinal))
                src = "https:" + src;

            var pm = RudubRe.PlayerSrc.Match(src);
            if (!pm.Success)
                continue;

            string hash = pm.Groups[2].Value.TrimStart('/');

            if (!RudubRe.Hash.IsMatch(hash))
                continue;

            return (pm.Groups[1].Value.TrimEnd('/'), hash);
        }

        return null;
    }

    static List<RudubEpisode> ParsePlaylist(string html)
    {
        var matches = RudubRe.PlaylistSpan.Matches(html);
        if (matches.Count == 0)
            return new List<RudubEpisode>();

        var outp = new List<RudubEpisode>(matches.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in matches)
        {
            string hash = m.Groups[1].Value.Trim().TrimStart('/');

            if (!RudubRe.Hash.IsMatch(hash))
                continue;

            if (!seen.Add(hash))
                continue;

            int.TryParse(m.Groups[2].Value, out int num);
            if (num <= 0)
                continue;

            outp.Add(new RudubEpisode() { num = num, hash = hash });
        }

        outp.Sort((a, b) => a.num.CompareTo(b.num));
        return outp;
    }
    #endregion

    #region Варианты качества
    static List<RudubVariant> ParseVariants(string body, string releaseQuality)
    {
        var matches = RudubRe.Variant.Matches(body);
        if (matches.Count == 0)
            return new List<RudubVariant>();

        string top = releaseQuality;
        if (string.IsNullOrEmpty(top))
            top = "1080p";

        var outp = new List<RudubVariant>(matches.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in matches)
        {
            string u = m.Groups[1].Value.Trim();

            if (!u.StartsWith("http", StringComparison.Ordinal))
                continue;

            if (!seen.Add(u))
                continue;

            string label;

            if (u.Contains("/fhd.mp4/"))
                label = top;
            else if (u.Contains("/hd.mp4/"))
                label = "720p";
            else if (u.Contains("/sd.mp4/"))
                label = "480p";
            else
                label = "auto";

            outp.Add(new RudubVariant() { label = label, url = u });
        }

        var sorted = outp.OrderByDescending(i => RudubUtil.QualityRank(i.label)).ToList();
        return sorted;
    }
    #endregion

    #region Хосты
    string ResolveHost()
    {
        return RudubStore.GetHost(init.host);
    }

    async Task<string> DiscoverHost()
    {
        string tracker = RudubConf.DefaultTracker;
        string body = await Fetch($"{tracker}/", $"{tracker}/");
        if (body == null)
            return null;

        var m = RudubRe.PlayerHost.Match(body);
        if (!m.Success)
            return null;

        string found = m.Value.TrimEnd('/');

        if (found != ResolveHost())
        {
            RudubStore.ResetForMove(found);
        }

        return found;
    }
    #endregion

    #region Fetch
    async Task<string> Fetch(string target, string referer, string ua = null)
    {
        if (!await RudubStore.Enter())
            return null;

        string agent = string.IsNullOrEmpty(ua) ? RudubConf.UA : ua;

        try
        {
            var headers = string.IsNullOrEmpty(referer)
                ? HeadersModel.Init(
                    ("user-agent", agent),
                    ("accept", RudubConf.Accept),
                    ("accept-language", RudubConf.AcceptLang))
                : HeadersModel.Init(
                    ("user-agent", agent),
                    ("accept", RudubConf.Accept),
                    ("accept-language", RudubConf.AcceptLang),
                    ("referer", referer));

            return await httpHydra.Get(
                target,
                addheaders: headers,
                useDefaultHeaders: false,
                safety: true
            );
        }
        finally
        {
            RudubStore.Leave();
        }
    }
    #endregion
}
