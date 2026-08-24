using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Music;

// Похожие артисты с music-map.com (проект Gnod/Gnoosic): один GET на артиста,
// парс якорей #gnodMap, отсортированных по музыкальной близости (краудсорсная
// коллаборативная фильтрация). Используется ТОЛЬКО как additive-слой «специи»
// в «Миксах недели»: не источник в UI, не участник радио, любой сбой — пустой
// список и микс собирается без внешнего слоя. Живой прототип 2026-07-16
// подтвердил стабильность парсинга и RU/транслит-покрытие (10/10 сидов по 48
// артистов, включая кириллические запросы и выдачу).
public static class MusicMapSupport
{
    const string BrowserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";
    const string cacheVersion = "mm-v1";

    // похожесть артистов почти статична; 14 дней — быстро самовосстановится,
    // если сайт/парсер временно отдал мусор (совет Кодекса)
    static readonly TimeSpan similarCacheTtl = TimeSpan.FromDays(14);

    static readonly HttpClient httpClient = MusicHttp.CreateClient("musicmap");
    static readonly Regex mapBlockRegex = new(@"<div id=gnodMap>(.*?)</div>", RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex anchorRegex = new("<a href=\"[^\"]*\" class=S id=s(\\d+)>([^<]*)</a>", RegexOptions.Compiled);

    static MusicMapSupport()
    {
        httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public static async Task<List<string>> GetSimilarArtistsAsync(string artist, CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeName(artist);
        if (string.IsNullOrWhiteSpace(normalized))
            return new List<string>();

        try
        {
            // пустой ответ кэшируется коротким emptyTtl (20 мин) — разовый
            // сбой сайта не залипает на 14 дней
            return await MusicMetadataCacheService.GetOrCreateAsync(
                "musicmap",
                "similar_artists",
                $"{cacheVersion}|{normalized}",
                similarCacheTtl,
                () => FetchSimilarArtistsAsync(artist, cancellationToken),
                cancellationToken
            ) ?? new List<string>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new List<string>();
        }
    }

    static async Task<List<string>> FetchSimilarArtistsAsync(string artist, CancellationToken cancellationToken)
    {
        var result = new List<string>();

        try
        {
            // формат пути music-map: имя в lower, пробелы -> '+', остальное
            // percent-encoded (кириллица и '&' включительно)
            string path = Uri.EscapeDataString(artist.Trim().ToLowerInvariant()).Replace("%20", "+");

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.music-map.com/{path}");
            request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return result;

            string html = await response.Content.ReadAsStringAsync(cancellationToken);
            var block = mapBlockRegex.Match(html ?? string.Empty);
            if (!block.Success)
                return result;

            foreach (Match anchor in anchorRegex.Matches(block.Groups[1].Value))
            {
                // s0 — сам запрошенный артист, не кандидат
                if (anchor.Groups[1].Value == "0")
                    continue;

                string name = System.Net.WebUtility.HtmlDecode(anchor.Groups[2].Value).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(name);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        return result;
    }

    /// <summary>
    /// Фильтр кандидатов карты: выбрасывает алиасы/проекты сид-артистов
    /// (containment + fuzzy + потокенно — ловит «Myagi», «Mijagi»,
    /// «Gruppa Skryptonite»), артистов истории (кросс-скрипт через транслит:
    /// «Скриптонит» == «Scriptonite») и дубли по нормализованному имени.
    /// Порядок карты (по близости) сохраняется.
    /// </summary>
    public static List<string> FilterCandidates(IEnumerable<string> candidates, IEnumerable<string> aliasReferences, IEnumerable<string> excludeNames)
    {
        var references = (aliasReferences ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        var excludedRaw = (excludeNames ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        var excluded = excludedRaw.Select(NormalizeName).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates ?? Enumerable.Empty<string>())
        {
            string normalized = NormalizeName(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                continue;

            if (references.Any(reference => IsAliasOf(candidate, reference)))
                continue;

            if (excluded.Any(name => name == normalized || FuzzyClose(name, normalized))
                || excludedRaw.Any(name => IsAliasOf(candidate, name)))
                continue;

            result.Add(candidate);
        }

        return result;
    }

    internal static bool IsAliasOf(string candidate, string reference)
    {
        string c = NormalizeName(candidate);
        string r = NormalizeName(reference);

        if (string.IsNullOrWhiteSpace(c) || string.IsNullOrWhiteSpace(r))
            return string.Equals(candidate?.Trim(), reference?.Trim(), StringComparison.OrdinalIgnoreCase);

        if (c.Contains(r, StringComparison.Ordinal) || r.Contains(c, StringComparison.Ordinal))
            return true;

        // опечаточные алиасы (Myagi/Mijagi) и транслит-варианты
        if (FuzzyClose(c, r))
            return true;

        // потокенно: алиас внутри многословного имени («Gruppa Skryptonite»
        // для сида «Скриптонит»)
        return TokensOf(candidate).Any(token => FuzzyClose(token, r))
            || TokensOf(reference).Any(token => FuzzyClose(c, token));
    }

    internal static bool FuzzyClose(string a, string b)
        => Levenshtein(a, b) <= Math.Max(1, Math.Min(a.Length, b.Length) / 4);

    static IEnumerable<string> TokensOf(string name)
        => Regex.Split(name ?? string.Empty, @"[^\p{L}\p{Nd}]+")
            .Select(NormalizeName)
            .Where(token => !string.IsNullOrWhiteSpace(token));

    // RU->latin транслит + только [a-z0-9]: кросс-скрипт сравнение имён
    // («Скриптонит» и «Scriptonite» сводятся к сравнимым строкам)
    internal static string NormalizeName(string name)
    {
        var builder = new StringBuilder((name ?? string.Empty).Length);

        foreach (char raw in (name ?? string.Empty).ToLowerInvariant())
        {
            if (translit.TryGetValue(raw, out string mapped))
                builder.Append(mapped);
            else if (raw is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(raw);
        }

        return builder.ToString();
    }

    internal static bool ContainsCyrillic(string value)
        => (value ?? string.Empty).Any(ch => ch is >= 'а' and <= 'я' or 'ё' or >= 'А' and <= 'Я' or 'Ё'
            or >= 'і' and <= 'ї' or 'є' or 'ґ' or >= 'І' and <= 'Ї' or 'Є' or 'Ґ');

    // Для поискового fallback нельзя использовать NormalizeName: он склеивает
    // слова. MusicBrainz лучше понимает "maikl dzhekson", чем
    // "maikldzhekson", поэтому сохраняем пробелы/разделители.
    internal static string TransliterateCyrillicToLatinForSearch(string value)
    {
        var builder = new StringBuilder((value ?? string.Empty).Length);
        bool lastWasSpace = true;

        foreach (char raw in (value ?? string.Empty).ToLowerInvariant())
        {
            if (translit.TryGetValue(raw, out string mapped))
            {
                if (!string.IsNullOrEmpty(mapped))
                {
                    builder.Append(mapped);
                    lastWasSpace = false;
                }
                continue;
            }

            if (raw is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(raw);
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    static readonly Dictionary<char, string> translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "i", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        ['і'] = "i", ['ї'] = "i", ['є'] = "e", ['ґ'] = "g"
    };

    static int Levenshtein(string a, string b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        if (a.Length < b.Length)
            (a, b) = (b, a);

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
