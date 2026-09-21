using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AniDub;

public static class AnidubConf
{
    public const string DefaultHost = "https://online.anidub.com";
    public const string DefaultPlayer = "https://player.ladonyvesna2005.info";

    // Запасной источник: у большей части каталога общего плеера нет вовсе, а
    // серии разложены вставками sibnet прямо на странице тайтла.
    public const string SibnetHost = "https://video.sibnet.ru";

    // Признак sibnet-серии в ссылке на воспроизведение: h=sib_<videoid>.
    public const string SibnetPrefix = "sib_";

    public const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public const string Accept = "text/html,application/xhtml+xml,*/*;q=0.8";
    public const string AcceptLang = "ru-RU,ru;q=0.9";

    public static readonly TimeSpan SearchTTL = TimeSpan.FromHours(6);
    public static readonly TimeSpan ShowTTL = TimeSpan.FromMinutes(90);
    public static readonly TimeSpan NotFoundTTL = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan NodeDeadTTL = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan NodeAliveTTL = TimeSpan.FromMinutes(20);

    public static readonly HashSet<string> BadNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cdn1.anivids.link"
    };

    // Сколько результатов поиска разворачиваем в сезоны. Больше шести — это уже
    // однофамильцы, а каждый кандидат стоит запроса.
    public const int MaxCandidates = 6;

    public const int SeasonWorkers = 6;

    public const int MaxInflight = 12;
}

public static class AnidubRe
{
    // Карточка выдачи: ссылка, «Рус / Original», «2010 • Аниме Фильмы».
    public static readonly Regex CardHref = new Regex(
        "href=\"(https?://[^\"]+?/\\d+-[^\"]+\\.html)\"", RegexOptions.Compiled);

    public static readonly Regex CardTitle = new Regex(
        "class=\"th-title\">([^<]+)<", RegexOptions.Compiled);

    public static readonly Regex CardSub = new Regex(
        "class=\"th-subtitle[^\"]*\">([\\s\\S]{0,300}?)</div>", RegexOptions.Compiled);

    public static readonly Regex Year = new Regex("(\\d{4})", RegexOptions.Compiled);

    // «Сезон 1, серии 150 из 150» на странице тайтла.
    public static readonly Regex SeasonPage = new Regex("Сезон\\s*(\\d+)", RegexOptions.Compiled);

    // Хвосты в названии: «[26 из 26]», «[TV-1 + TV-2]».
    public static readonly Regex Bracket = new Regex("\\[[^\\]]*\\]", RegexOptions.Compiled);

    // Пометка вида в скобках: «Играй, эуфониум! (фильм)» — это маркер, а не
    // часть названия, и в карточке TMDB его нет.
    public static readonly Regex ParenKind = new Regex(
        "(?i)\\((?:фильм|movie|тв|tv|ova|ona|спешл|special)[^)]*\\)", RegexOptions.Compiled);

    public static readonly Regex Tag = new Regex("<[^>]*>", RegexOptions.Compiled);

    // Адрес тайтла: только «/{id}-{slug}.html» на своём хосте.
    public static readonly Regex TitlePath = new Regex(
        "^/\\d+-[a-z0-9_-]+\\.html$", RegexOptions.Compiled);

    // Номер сезона в названии тайтла: «ТВ-2», «TV-2», «2 сезон», «Season 2».
    public static readonly Regex[] SeasonTitle = new Regex[]
    {
        new Regex("(?i)(?:тв|tv)\\s*[-–—]?\\s*(\\d{1,2})\\b", RegexOptions.Compiled),
        new Regex("(?i)(\\d{1,2})\\s*(?:-?(?:й|ый|nd|rd|th))?\\s*(?:сезон|season)", RegexOptions.Compiled),
        new Regex("(?i)(?:сезон|season)\\s*(\\d{1,2})", RegexOptions.Compiled)
    };

    // Плеер тот же, что у RuDub: страница серии, список серий, master-плейлист.
    public static readonly Regex Iframe = new Regex("<iframe[^>]+src=\"([^\"]+)\"", RegexOptions.Compiled);

    public static readonly Regex PlaylistSpan = new Regex(
        "<span[^>]+data=\"([^\"]+)\"[^>]*>\\s*(\\d+)\\s*сери", RegexOptions.Compiled);

    // Плеер отдаёт список серий не разметкой, а JSON'ом window.PLAYER_CONTEXT:
    //   "episodes":[{"link":"/s2/<hash>","key":"...","label":"1 серия",...},...]
    public static readonly Regex ContextEpisode = new Regex(
        "\"link\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]{0,400}?\"label\"\\s*:\\s*\"(\\d+)\\s*сери", RegexOptions.Compiled);

    public static readonly Regex Filename = new Regex("event-filename=\"([^\"]*)\"", RegexOptions.Compiled);

    public static readonly Regex PlayerSrc = new Regex(
        "^(https?://[\\w.-]+(?::\\d+)?)/index\\.php\\?v=/([^\"&]+)", RegexOptions.Compiled);

    public static readonly Regex Variant = new Regex(
        "^(https?://[^\\r\\n]+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    public static readonly Regex ReleaseQuality = new Regex(
        "(?:HD)?(2160|1080|720|480)[pр]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex Hash = new Regex(
        "^s\\d{1,4}/[A-Za-z0-9]{8,64}$", RegexOptions.Compiled);

    // Серия на sibnet: <span data="...shell.php?videoid=3460549">Серия 1</span>.
    // Подпись бывает и «Серия N», и «N серия», поэтому цифру берём как первую
    // после открывающего тега, а не по шаблону подписи.
    public static readonly Regex SibnetSpan = new Regex(
        "<span[^>]+data=\"https?://video\\.sibnet\\.ru/shell\\.php\\?videoid=(\\d+)\"[^>]*>[^0-9]{0,20}(\\d+)", RegexOptions.Compiled);

    public static readonly Regex SibnetId = new Regex("^\\d{4,12}$", RegexOptions.Compiled);

    // Путь к файлу в шелле вставки: player.src([{src: "/v/<hash>/<id>.mp4", ...}]).
    public static readonly Regex SibnetFile = new Regex("src:\\s*\"([^\"]+\\.mp4)\"", RegexOptions.Compiled);

    // Разрешение sibnet называет в имени релиза: «[AniDub]_ShiroBako_[02]_[720p_x264_Aac]».
    public static readonly Regex SibnetTitle = new Regex(
        "property=\"og:title\"\\s+content=\"([^\"]*)\"", RegexOptions.Compiled);
}

public static class AnidubUtil
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

    // AniDub печатает iframe уже экранированным: «?v=/s10/hash&amp;playlist».
    public static string UnescapeAmp(string v)
        => string.IsNullOrEmpty(v) ? v : v.Replace("&amp;", "&");

    public static string Collapse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        bool space = false;

        foreach (char ch in raw)
        {
            if (char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    // Текст без разметки — по нему ищутся «Сезон N» и год.
    public static string Text(string html)
        => string.IsNullOrEmpty(html) ? string.Empty : Collapse(AnidubRe.Tag.Replace(html, " "));

    // Сравнение названий: буквы и цифры, одиночные пробелы между словами.
    // Пробелы нужны — по ним работает совпадение первых двух слов.
    public static string NormalizeSearch(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var sb = new StringBuilder(raw.Length);
        bool space = false;

        foreach (char ch in raw.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                space = false;
                continue;
            }

            if (sb.Length > 0)
                space = true;
        }

        return space ? sb.ToString().TrimEnd() : sb.ToString();
    }

    // Отрезает подзаголовок: всё после «:» или « - ».
    public static string TitleBase(string raw)
    {
        string s = AnidubRe.ParenKind.Replace((raw ?? string.Empty).Trim(), " ").Trim();

        int idx = s.IndexOf(':');
        if (idx > 0)
            s = s.Substring(0, idx);

        idx = s.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0)
            s = s.Substring(0, idx);

        return s.Trim();
    }

    // Первые два слова названия — запасной поисковый запрос. Пустая строка,
    // если слов меньше трёх: там укорачивать нечего.
    public static string ShortQuery(string title)
    {
        string[] fields = NormalizeSearch(TitleBase(title)).Split(' ');

        if (fields.Length < 3)
            return string.Empty;

        return fields[0] + " " + fields[1];
    }

    public static bool SharePrefix2(string a, string b)
    {
        string[] fa = (a ?? string.Empty).Split(' ');
        string[] fb = (b ?? string.Empty).Split(' ');

        if (fa.Length < 2 || fb.Length < 2)
            return false;

        return fa[0] == fb[0] && fa[1] == fb[1];
    }

    // Сила совпадения одной стороны названия с запрошенным: 2 — точное, 1 — по
    // основной части до подзаголовка, 0 — мимо.
    //
    // Градация нужна из-за аниме: у AniDub тайтл почти всегда несёт собственный
    // подзаголовок («Реинкарнация безработного: История о приключениях в другом
    // мире»), а TMDB держит короткое имя или ДРУГОЙ подзаголовок. Точное
    // сравнение теряло такие тайтлы целиком. Обратная сторона — «Наруто» и
    // «Наруто: Ураганные хроники» тоже сходятся по основной части, поэтому это
    // совпадение слабее точного и проигрывает ему при сортировке кандидатов.
    public static int TitleMatch(string side, string wantRaw)
    {
        if (string.IsNullOrWhiteSpace(side) || string.IsNullOrWhiteSpace(wantRaw))
            return 0;

        string want = NormalizeSearch(AnidubRe.ParenKind.Replace(wantRaw, " "));

        if (want.Length == 0)
            return 0;

        string wantBase = NormalizeSearch(TitleBase(wantRaw));
        int best = 0;

        foreach (string part in side.Split('/'))
        {
            string got = NormalizeSearch(AnidubRe.ParenKind.Replace(part, " "));

            if (got.Length == 0)
                continue;

            if (got == want)
                return 2;

            string gotBase = NormalizeSearch(TitleBase(part));

            if (gotBase.Length > 0 && (gotBase == want || gotBase == wantBase || got == wantBase))
            {
                best = 1;
                continue;
            }

            // Русские названия у TMDB и AniDub нередко расходятся хвостом:
            // «Рыцарь-скелет ВСТУПАЕТ В ПАРАЛЛЕЛЬНЫЙ МИР» против «Рыцарь-скелет
            // В ИНОМ МИРЕ». Совпадения первых двух слов достаточно, чтобы узнать
            // тайтл, и мало, чтобы спутать «Магическую битву» с «Магической
            // академией» — там расходится уже второе слово.
            if (SharePrefix2(got, want))
                best = 1;
        }

        return best;
    }

    public static string QualityFromRelease(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return string.Empty;

        var m = AnidubRe.ReleaseQuality.Match(filename);
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

public static class AnidubStore
{
    class SearchEntry
    {
        public List<AnidubCard> cards;
        public DateTime expires;
    }

    class Probe
    {
        public bool playable;
        public DateTime expires;
    }

    static readonly object _lock = new object();
    static readonly SemaphoreSlim _inflight = new SemaphoreSlim(AnidubConf.MaxInflight, AnidubConf.MaxInflight);

    static readonly Dictionary<string, SearchEntry> _searches = new Dictionary<string, SearchEntry>();
    static readonly Dictionary<string, AnidubShow> _shows = new Dictionary<string, AnidubShow>();
    static readonly Dictionary<string, Probe> _probes = new Dictionary<string, Probe>();

    static string _host;
    static string _player = AnidubConf.DefaultPlayer;

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

        string host = AnidubUtil.NormalizeHost(configured);
        if (host.Length == 0)
            host = AnidubConf.DefaultHost;

        lock (_lock)
        {
            if (string.IsNullOrEmpty(_host))
                _host = host;

            return _host;
        }
    }

    public static void ResetForMove(string host)
    {
        lock (_lock)
        {
            _host = host;
            _searches.Clear();
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
                return string.IsNullOrEmpty(_player) ? AnidubConf.DefaultPlayer : _player;
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

    #region Поиск
    public static List<AnidubCard> GetSearch(string key)
    {
        lock (_lock)
        {
            if (_searches.TryGetValue(key, out SearchEntry entry) && DateTime.Now < entry.expires)
                return entry.cards;

            return null;
        }
    }

    public static void SetSearch(string key, List<AnidubCard> cards)
    {
        lock (_lock)
        {
            if (_searches.Count > 512)
            {
                DateTime now = DateTime.Now;

                foreach (string k in _searches.Where(i => now >= i.Value.expires).Select(i => i.Key).ToList())
                    _searches.Remove(k);
            }

            _searches[key] = new SearchEntry()
            {
                cards = cards,
                expires = DateTime.Now.Add(AnidubConf.SearchTTL)
            };
        }
    }
    #endregion

    #region Тайтл
    public static AnidubShow GetShow(string url)
    {
        lock (_lock)
        {
            if (_shows.TryGetValue(url, out AnidubShow show) && DateTime.Now < show.expires)
                return show;

            return null;
        }
    }

    public static void SetShow(string url, AnidubShow show)
    {
        lock (_lock)
        {
            if (_shows.Count > 512)
            {
                DateTime now = DateTime.Now;

                foreach (string key in _shows.Where(i => now >= i.Value.expires).Select(i => i.Key).ToList())
                    _shows.Remove(key);
            }

            _shows[url] = show;
        }
    }
    #endregion

    #region Проба
    public static bool? GetProbe(string url)
    {
        lock (_lock)
        {
            if (_probes.TryGetValue(url, out Probe probe) && DateTime.Now < probe.expires)
                return probe.playable;

            return null;
        }
    }

    public static void SetProbe(string url, bool playable)
    {
        lock (_lock)
        {
            if (_probes.Count > 2048)
            {
                DateTime now = DateTime.Now;

                foreach (string key in _probes.Where(i => now >= i.Value.expires).Select(i => i.Key).ToList())
                    _probes.Remove(key);
            }

            _probes[url] = new Probe()
            {
                playable = playable,
                expires = DateTime.Now.Add(playable ? AnidubConf.ShowTTL : AnidubConf.NotFoundTTL)
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
            _nodeExpires[node] = DateTime.Now.Add(state == NodeState.Dead ? AnidubConf.NodeDeadTTL : AnidubConf.NodeAliveTTL);
        }
    }

    public static bool IsBadNode(string node)
        => !string.IsNullOrEmpty(node) && AnidubConf.BadNodes.Contains(node);
    #endregion
}

public struct AniDubInvoke
{
    #region AniDubInvoke
    OnlinesSettings init;
    HttpHydra httpHydra;

    public AniDubInvoke(OnlinesSettings init, HttpHydra httpHydra)
    {
        this.init = init;
        this.httpHydra = httpHydra;
    }
    #endregion

    #region Фильм
    async public Task<AnidubSeason> Movie(string title, string original_title, int year)
    {
        var cards = await Lookup(title, original_title, year, false);
        if (cards == null || cards.Count == 0)
            return null;

        var show = await ResolveShow(cards[0].url);
        if (show == null || show.episodes.Count == 0)
            return null;

        return new AnidubSeason()
        {
            num = show.season,
            name = cards[0].rus,
            quality = show.quality,
            episodes = show.episodes
        };
    }
    #endregion

    #region Сезоны
    // На AniDub каждый сезон — отдельная страница со своим названием, поэтому
    // список сезонов собирается из нескольких результатов поиска: кандидаты
    // разворачиваются параллельно, а номер сезона читается из названия тайтла.
    async public Task<List<AnidubSeason>> Seasons(string title, string original_title, int year, bool checksearch)
    {
        var cards = await Lookup(title, original_title, year, true);
        if (cards == null || cards.Count == 0)
            return null;

        var results = new AnidubShow[cards.Count];

        if (checksearch)
        {
            // Проверка источника: разворачиваем только первого кандидата. Этого
            // достаточно, чтобы ответить «живой/мёртвый», и не стоит двенадцати
            // запросов — настоящий список придёт следующим, уже без checksearch.
            results[0] = await ResolveShow(cards[0].url);
        }
        else
        {
            var self = this;
            var gate = new SemaphoreSlim(AnidubConf.SeasonWorkers, AnidubConf.SeasonWorkers);

            try
            {
                var tasks = new Task[cards.Count];

                for (int i = 0; i < cards.Count; i++)
                {
                    int idx = i;

                    tasks[idx] = Task.Run(async () =>
                    {
                        await gate.WaitAsync().ConfigureAwait(false);

                        try
                        {
                            results[idx] = await self.ResolveShow(cards[idx].url);
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
        }

        var bySeason = new Dictionary<int, AnidubSeason>();

        for (int i = 0; i < cards.Count; i++)
        {
            var show = results[i];

            if (show == null || show.episodes.Count == 0)
                continue;

            int num = SeasonFor(cards[i], show);

            // При коллизии выигрывает тайтл с бОльшим числом серий: у AniDub
            // рядом с сезоном часто лежит его же OVA/спешл с тем же номером.
            if (bySeason.TryGetValue(num, out AnidubSeason prev) && prev.episodes.Count >= show.episodes.Count)
                continue;

            bySeason[num] = new AnidubSeason()
            {
                num = num,
                name = cards[i].rus,
                url = cards[i].url,
                quality = show.quality,
                episodes = show.episodes
            };
        }

        if (bySeason.Count == 0)
            return null;

        return bySeason.Values.OrderBy(i => i.num).ToList();
    }
    #endregion

    #region Серии
    async public Task<AnidubSeason> Season(string url, int num)
    {
        var show = await ResolveShow(SanitizeTitleURL(url));
        if (show == null || show.episodes.Count == 0)
            return null;

        return new AnidubSeason()
        {
            num = num,
            quality = show.quality,
            episodes = show.episodes
        };
    }
    #endregion

    #region Streams
    async public Task<AnidubStream> Streams(string hash, string quality, string ua)
    {
        if (string.IsNullOrEmpty(hash))
            return null;

        hash = hash.Trim();

        if (hash.StartsWith(AnidubConf.SibnetPrefix, StringComparison.Ordinal))
            return await SibnetStream(hash.Substring(AnidubConf.SibnetPrefix.Length), quality, ua);

        if (!AnidubRe.Hash.IsMatch(hash))
            return null;

        string player = AnidubStore.Player;
        string label = AnidubUtil.QualityLabel(quality);
        AnidubStream last = null;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            string body = await Fetch($"{player}/vid.php?v=/{hash}", $"{player}/", ua);
            if (body == null)
                continue;

            var variants = ParseVariants(body, label);
            if (variants.Count == 0)
                continue;

            var stream = new AnidubStream()
            {
                player = player,
                variants = variants
            };

            string node = AnidubUtil.Host(stream.variants[0].url);

            if (AnidubStore.IsBadNode(node))
                continue;

            last = stream;

            if (AnidubStore.GetNode(node) == AnidubStore.NodeState.Dead)
                continue;

            if (AnidubStore.GetNode(node) == AnidubStore.NodeState.Alive)
                return stream;

            string probe = await Fetch(stream.variants[0].url, $"{player}/", ua);

            if (probe != null && probe.Contains("#EXTM3U"))
            {
                AnidubStore.SetNode(node, AnidubStore.NodeState.Alive);
                return stream;
            }

            AnidubStore.SetNode(node, AnidubStore.NodeState.Dead);
        }

        return last;
    }

    // Свой HttpClient: HttpHydra отдаёт только тело и не даёт ни задать
    // диапазон, ни узнать конечный адрес после редиректов.
    static readonly HttpClient sibHttp = new HttpClient(new HttpClientHandler()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    // До файла на CDN ведут два редиректа, и тело тут не нужно — хватит одного
    // байта, важно узнать конечный адрес.
    static async Task<string> SibnetDirect(string url)
    {
        try
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                req.Headers.Referrer = new Uri(AnidubConf.SibnetHost + "/");
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);

                using (var res = await sibHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if ((int)res.StatusCode == 206 || res.IsSuccessStatusCode)
                        return res.RequestMessage?.RequestUri?.ToString();
                }
            }
        }
        catch
        {
            // Не разрешилось — отдадим исходный адрес: клиенту, который всё же
            // шлёт Referer, он подойдёт.
        }

        return null;
    }

    // Sibnet отдаёт не HLS, а обычный mp4, и путь к файлу лежит в шелле вставки.
    // Ссылку на video.sibnet.ru пускают только с Referer своего же хоста — без
    // него 403, поэтому в player идёт sibnet: Controller соберёт из него
    // headers_stream, а клиент дойдёт по редиректу до CDN уже без заголовков.
    async Task<AnidubStream> SibnetStream(string videoid, string quality, string ua)
    {
        if (!AnidubRe.SibnetId.IsMatch(videoid))
            return null;

        string shell = await Fetch($"{AnidubConf.SibnetHost}/shell.php?videoid={videoid}", $"{AnidubConf.DefaultHost}/", ua);
        if (shell == null)
            return null;

        string file = AnidubUtil.First(shell, AnidubRe.SibnetFile);
        if (string.IsNullOrEmpty(file))
            return null;

        if (file.StartsWith("//", StringComparison.Ordinal))
            file = "https:" + file;
        else if (file.StartsWith("/", StringComparison.Ordinal))
            file = AnidubConf.SibnetHost + file;
        else if (!file.StartsWith("http", StringComparison.Ordinal))
            return null;

        // Отдавать клиенту адрес video.sibnet.ru бессмысленно: он пускает
        // только с Referer своего хоста, а плеер его не шлёт — в ответ 403.
        // Разрешаем оба редиректа здесь и отдаём готовый адрес CDN: он живёт
        // около восьми часов и никаких заголовков не требует.
        string direct = await SibnetDirect(file);

        if (!string.IsNullOrEmpty(direct))
            file = direct;

        string label = AnidubUtil.QualityFromRelease(AnidubUtil.First(shell, AnidubRe.SibnetTitle));

        if (string.IsNullOrEmpty(label))
            label = AnidubUtil.QualityLabel(quality);

        if (string.IsNullOrEmpty(label))
            label = "720p";

        return new AnidubStream()
        {
            player = AnidubConf.SibnetHost,
            variants = new List<AnidubVariant>()
            {
                new AnidubVariant() { label = label, url = file }
            }
        };
    }
    #endregion

    #region Поиск и подбор тайтла
    // lookup ищет карточки в каталоге. Возвращает кандидатов, отсортированных по
    // убыванию уверенности: первым идёт лучший.
    async Task<List<AnidubCard>> Lookup(string title, string original_title, int year, bool serial)
    {
        title = (title ?? string.Empty).Trim();
        original_title = (original_title ?? string.Empty).Trim();

        if (title.Length == 0 && original_title.Length == 0)
            return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var picked = new List<AnidubCard>(AnidubConf.MaxCandidates);

        // Полное название TMDB часто длиннее того, что знает каталог, и поиск по
        // нему не находит ничего («Рыцарь-скелет вступает в параллельный мир»).
        // Поэтому вторым заходом идёт укороченный запрос — первые два слова.
        foreach (string query in new string[] { title, original_title, AnidubUtil.ShortQuery(title) })
        {
            if (string.IsNullOrWhiteSpace(query))
                continue;

            var cards = await Search(query);
            if (cards == null)
                continue;

            foreach (var card in PickCards(cards, title, original_title, year, serial))
            {
                if (!seen.Add(card.url))
                    continue;

                picked.Add(card);

                if (picked.Count >= AnidubConf.MaxCandidates)
                    return picked;
            }

            // Русское название — более точный запрос; если оно уже дало
            // попадания, вторым заходом не платим.
            if (picked.Count > 0)
                break;
        }

        return picked;
    }

    // PickCards оставляет карточки, чьё название совпадает с запрошенным, и
    // сортирует их: сперва совпавшие по обоим названиям, затем по году.
    //
    // Год у аниме — слабый признак: TMDB держит дату первого эфира всего
    // сериала, а AniDub — год конкретного сезона, поэтому год только ранжирует,
    // но не отсекает. Отсекает вид контента: фильм для сериальной карточки
    // (и наоборот) — это всегда чужой тайтл.
    static List<AnidubCard> PickCards(List<AnidubCard> cards, string title, string original_title, int year, bool serial)
    {
        var scored = new List<KeyValuePair<AnidubCard, int>>(cards.Count);

        foreach (var card in cards)
        {
            // Вид отсекает только когда он ИЗВЕСТЕН: фильм на сериальной карточке
            // (и наоборот) — всегда чужой тайтл, а «вид неизвестен» значит лишь
            // то, что в подписи оказался не тот текст.
            if ((serial && card.kind == AnidubKind.Movie) || (!serial && card.kind == AnidubKind.Serial))
                continue;

            int ruHit = AnidubUtil.TitleMatch(card.rus, title);
            int origHit = AnidubUtil.TitleMatch(card.orig, original_title);

            int h = AnidubUtil.TitleMatch(card.rus, original_title);
            if (h > origHit)
                origHit = h;

            if (ruHit == 0 && origHit == 0)
                continue;

            // Точное совпадение весит вдвое против совпадения по основной части —
            // «Наруто» не должен обойти «Наруто: Ураганные хроники» на её же
            // карточке.
            int score = 2 * ruHit + 2 * origHit;

            if (year > 0 && card.year > 0)
            {
                int d = card.year - year;

                if (d == 0)
                    score += 2;
                else if (d > -2 && d < 2)
                    score++;
            }

            scored.Add(new KeyValuePair<AnidubCard, int>(card, score));
        }

        return scored
            .OrderByDescending(i => i.Value)
            .Select(i => i.Key)
            .ToList();
    }

    async Task<List<AnidubCard>> Search(string query)
    {
        string key = AnidubUtil.NormalizeSearch(query);
        if (key.Length == 0)
            return null;

        var cached = AnidubStore.GetSearch(key);
        if (cached != null)
            return cached;

        string host = ResolveHost();

        // DLE без search_start=0 отдаёт пустую форму поиска вместо выдачи.
        string target = $"{host}/?do=search&subaction=search&story={HttpUtility.UrlEncode(query)}&full_search=0&result_from=1&search_start=0";

        string body = await Fetch(target, $"{host}/");
        if (body == null)
            return null;

        var cards = ParseCards(body);
        AnidubStore.SetSearch(key, cards);
        return cards;
    }

    static List<AnidubCard> ParseCards(string html)
    {
        string[] chunks = html.Split(new string[] { "<div class=\"th-item\">" }, StringSplitOptions.None);
        if (chunks.Length < 2)
            return new List<AnidubCard>();

        var outp = new List<AnidubCard>(chunks.Length - 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 1; i < chunks.Length; i++)
        {
            string href = AnidubUtil.First(chunks[i], AnidubRe.CardHref);
            if (string.IsNullOrEmpty(href))
                continue;

            if (!seen.Add(href))
                continue;

            SplitTitle(AnidubUtil.First(chunks[i], AnidubRe.CardTitle), out string rus, out string orig);

            if (rus.Length == 0 && orig.Length == 0)
                continue;

            string sub = AnidubUtil.Text(AnidubUtil.First(chunks[i], AnidubRe.CardSub));

            int.TryParse(AnidubUtil.First(sub, AnidubRe.Year), out int year);

            string cat = sub;
            int idx = sub.IndexOf('•');

            if (idx >= 0)
                cat = sub.Substring(idx + 1).Trim();

            outp.Add(new AnidubCard()
            {
                url = href,
                rus = rus,
                orig = orig,
                year = year,
                cat = cat,
                kind = KindOf(cat)
            });
        }

        return outp;
    }

    static readonly HashSet<string> MovieCats = new HashSet<string>(StringComparer.Ordinal)
    {
        "аниме фильмы",
        "азиатские фильмы",
        "полнометражные",
        "мультфильмы"
    };

    static readonly HashSet<string> SerialCats = new HashSet<string>(StringComparer.Ordinal)
    {
        "аниме сериалы",
        "аниме ongoing",
        "аниме ova",
        "аниме ona",
        "дорамы",
        "дубляж"
    };

    // Вид контента карточки. Подпись в выдаче — НЕ всегда категория: у части
    // тайтлов там стоит «1 сезон», и тогда вид неизвестен. Отбрасывать такие
    // карточки нельзя — именно так терялись свежие полнометражки.
    static AnidubKind KindOf(string cat)
    {
        cat = (cat ?? string.Empty).Trim().ToLowerInvariant();

        if (MovieCats.Contains(cat))
            return AnidubKind.Movie;

        if (SerialCats.Contains(cat))
            return AnidubKind.Serial;

        return AnidubKind.Unknown;
    }

    // SplitTitle разбирает «Рус / Original [26 из 26]» на две стороны.
    static void SplitTitle(string raw, out string rus, out string orig)
    {
        string s = AnidubUtil.Collapse(AnidubRe.Bracket.Replace(raw ?? string.Empty, " "));

        rus = string.Empty;
        orig = string.Empty;

        if (s.Length == 0)
            return;

        int idx = s.IndexOf(" / ", StringComparison.Ordinal);

        if (idx >= 0)
        {
            rus = s.Substring(0, idx).Trim();
            orig = s.Substring(idx + 3).Trim();
            return;
        }

        rus = s;
    }
    #endregion

    #region Разворот тайтла
    async Task<AnidubShow> ResolveShow(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var cached = AnidubStore.GetShow(url);
        if (cached != null)
            return cached.episodes.Count > 0 ? cached : null;

        string host = ResolveHost();
        string page = await Fetch(url, $"{host}/");

        if (page == null)
        {
            AnidubStore.SetShow(url, new AnidubShow() { expires = DateTime.Now.Add(AnidubConf.NotFoundTTL) });
            return null;
        }

        // В кэш идёт ТОЛЬКО номер, который назвала страница: он не зависит от
        // того, с какой карточки пришли. Номер из названия релиза считается выше
        // по стеку (см. SeasonFor) — иначе первый же заход за сериями, где
        // названия нет, зафиксировал бы в кэше «1».
        int season = 1;
        var sm = AnidubRe.SeasonPage.Match(AnidubUtil.Text(page));

        if (sm.Success && int.TryParse(sm.Groups[1].Value, out int sn) && sn > 0)
            season = sn;

        var frame = ParseIframe(page);

        if (frame == null)
        {
            // Общего плеера у тайтла нет — это не значит, что играть нечего:
            // большая часть каталога лежит сериями на sibnet.
            var sib = ParseSibnet(page);

            if (sib.Count == 0)
            {
                // Старые тайтлы стоят на умерших плеерах — играть нечего.
                AnidubStore.SetShow(url, new AnidubShow() { season = season, expires = DateTime.Now.Add(AnidubConf.NotFoundTTL) });
                return null;
            }

            var sibShow = new AnidubShow()
            {
                season = season,
                episodes = sib,
                expires = DateTime.Now.Add(AnidubConf.ShowTTL)
            };

            AnidubStore.SetShow(url, sibShow);
            return sibShow;
        }

        AnidubStore.Player = frame.Value.player;

        var show = new AnidubShow()
        {
            season = season,
            expires = DateTime.Now.Add(AnidubConf.ShowTTL)
        };

        string body = await Fetch($"{frame.Value.player}/index.php?v=/{frame.Value.hash}&playlist", $"{host}/");

        if (body != null)
        {
            show.episodes = ParsePlaylist(body);
            show.quality = AnidubUtil.QualityFromRelease(AnidubUtil.First(body, AnidubRe.Filename));
        }

        if (show.episodes.Count == 0)
        {
            // Плейлиста нет — у тайтла одна серия (фильм или спешл).
            show.episodes = new List<AnidubEpisode>() { new AnidubEpisode() { num = 1, hash = frame.Value.hash } };
        }

        AnidubStore.SetShow(url, show);
        return show;
    }

    static (string player, string hash)? ParseIframe(string html)
    {
        foreach (Match m in AnidubRe.Iframe.Matches(html))
        {
            string src = AnidubUtil.UnescapeAmp(m.Groups[1].Value.Trim());

            if (src.StartsWith("//", StringComparison.Ordinal))
                src = "https:" + src;

            var pm = AnidubRe.PlayerSrc.Match(src);
            if (!pm.Success)
                continue;

            string hash = pm.Groups[2].Value.TrimStart('/');

            if (!AnidubRe.Hash.IsMatch(hash))
                continue;

            return (pm.Groups[1].Value.TrimEnd('/'), hash);
        }

        return null;
    }

    // Номер сезона тайтла: название релиза важнее страницы.
    static int SeasonFor(AnidubCard card, AnidubShow show)
    {
        int n = SeasonFromTitle(card.rus, card.orig);

        if (n > 0)
            return n;

        if (show != null && show.season > 0)
            return show.season;

        return 1;
    }

    // SeasonFromTitle достаёт номер сезона из названия тайтла.
    //
    // На AniDub каждый сезон — отдельная страница, и почти на всех написано
    // «Сезон 1»: студия нумерует серии внутри своего релиза, а не внутри
    // сериала. Настоящий номер живёт в названии — «ТВ-2», «TV-2», «2 сезон».
    // Без этого три тайтла «Блича» схлопывались в один «1 сезон», и зритель
    // видел только тот, у кого больше серий.
    static int SeasonFromTitle(params string[] parts)
    {
        foreach (string raw in parts)
        {
            string s = (raw ?? string.Empty).Trim();
            if (s.Length == 0)
                continue;

            foreach (var re in AnidubRe.SeasonTitle)
            {
                var m = re.Match(s);

                if (m.Success && m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out int n) && n > 1 && n < 100)
                    return n;
            }

            int roman = RomanSeason(s);

            if (roman > 1)
                return roman;
        }

        return 0;
    }

    // RomanSeason ловит римскую нумерацию продолжений: «Mushoku Tensei II».
    // Только заглавные и только отдельным словом — иначе «I» из любого названия
    // превратит первый сезон во второй.
    static int RomanSeason(string s)
    {
        foreach (string raw in s.Split(' '))
        {
            switch (raw.Trim(' ', '.', ',', ':', ';', '(', ')', '[', ']'))
            {
                case "II":
                    return 2;
                case "III":
                    return 3;
                case "IV":
                    return 4;
                case "V":
                    return 5;
            }
        }

        return 0;
    }

    static List<AnidubEpisode> ParsePlaylist(string html)
    {
        var outp = ParseContext(html);

        if (outp.Count == 0)
            outp = ParseSpans(html);

        outp.Sort((a, b) => a.num.CompareTo(b.num));
        return outp;
    }

    static List<AnidubEpisode> ParseContext(string html)
    {
        var outp = new List<AnidubEpisode>();
        if (string.IsNullOrEmpty(html))
            return outp;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in AnidubRe.ContextEpisode.Matches(html))
        {
            string hash = m.Groups[1].Value.Trim().TrimStart('/');

            if (!AnidubRe.Hash.IsMatch(hash))
                continue;

            if (!seen.Add(hash))
                continue;

            int.TryParse(m.Groups[2].Value, out int num);
            if (num <= 0)
                continue;

            outp.Add(new AnidubEpisode() { num = num, hash = hash });
        }

        return outp;
    }

    static List<AnidubEpisode> ParseSpans(string html)
    {
        var matches = AnidubRe.PlaylistSpan.Matches(html);
        if (matches.Count == 0)
            return new List<AnidubEpisode>();

        var outp = new List<AnidubEpisode>(matches.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in matches)
        {
            string hash = m.Groups[1].Value.Trim().TrimStart('/');

            if (!AnidubRe.Hash.IsMatch(hash))
                continue;

            if (!seen.Add(hash))
                continue;

            int.TryParse(m.Groups[2].Value, out int num);
            if (num <= 0)
                continue;

            outp.Add(new AnidubEpisode() { num = num, hash = hash });
        }

        return outp;
    }

    // Серии на sibnet разложены прямо на странице тайтла, каждая — своя
    // вставка. Плейлиста у них нет: серия играет сама по себе.
    static List<AnidubEpisode> ParseSibnet(string html)
    {
        var matches = AnidubRe.SibnetSpan.Matches(html);
        if (matches.Count == 0)
            return new List<AnidubEpisode>();

        var outp = new List<AnidubEpisode>(matches.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in matches)
        {
            string id = m.Groups[1].Value.Trim();

            if (!seen.Add(id))
                continue;

            int.TryParse(m.Groups[2].Value, out int num);
            if (num <= 0)
                continue;

            outp.Add(new AnidubEpisode() { num = num, sib = id });
        }

        outp.Sort((a, b) => a.num.CompareTo(b.num));
        return outp;
    }
    #endregion

    #region Варианты качества
    static List<AnidubVariant> ParseVariants(string body, string releaseQuality)
    {
        var matches = AnidubRe.Variant.Matches(body);
        if (matches.Count == 0)
            return new List<AnidubVariant>();

        string top = releaseQuality;
        if (string.IsNullOrEmpty(top))
            top = "1080p";

        var outp = new List<AnidubVariant>(matches.Count);
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

            outp.Add(new AnidubVariant() { label = label, url = u });
        }

        return outp.OrderByDescending(i => AnidubUtil.QualityRank(i.label)).ToList();
    }
    #endregion

    #region Хосты
    string ResolveHost()
    {
        return AnidubStore.GetHost(init.host);
    }

    // SanitizeTitleURL пропускает только адреса тайтлов на своём хосте: ?u=
    // придёт с нашей же страницы сезонов, и превращать его в произвольный
    // запрос нельзя.
    string SanitizeTitleURL(string raw)
    {
        raw = (raw ?? string.Empty).Trim();

        if (raw.Length == 0 || raw.Length > 300)
            return null;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri u) || u.Scheme.Length == 0 || u.Host.Length == 0)
            return null;

        if (!Uri.TryCreate(ResolveHost(), UriKind.Absolute, out Uri self) || !string.Equals(u.Host, self.Host, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!AnidubRe.TitlePath.IsMatch(u.AbsolutePath.ToLowerInvariant()))
            return null;

        return u.Scheme + "://" + u.Host + u.AbsolutePath;
    }
    #endregion

    #region Fetch
    async Task<string> Fetch(string target, string referer, string ua = null)
    {
        if (!await AnidubStore.Enter())
            return null;

        string agent = string.IsNullOrEmpty(ua) ? AnidubConf.UA : ua;

        try
        {
            var headers = string.IsNullOrEmpty(referer)
                ? HeadersModel.Init(
                    ("user-agent", agent),
                    ("accept", AnidubConf.Accept),
                    ("accept-language", AnidubConf.AcceptLang))
                : HeadersModel.Init(
                    ("user-agent", agent),
                    ("accept", AnidubConf.Accept),
                    ("accept-language", AnidubConf.AcceptLang),
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
            AnidubStore.Leave();
        }
    }
    #endregion
}
