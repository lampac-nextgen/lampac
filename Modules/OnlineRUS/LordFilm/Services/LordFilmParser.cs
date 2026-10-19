using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LordFilm;

/// <summary>
/// Разбор HTML сайта lordfilm и плеера nextembed.ws.
/// Класс не зависит от Shared, поэтому его легко тестировать отдельно.
/// </summary>
public static class LordFilmParser
{
    static readonly JsonSerializerOptions json = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    #region Page
    /// <summary>Идентификатор ролика в плеере nextembed.ws из html страницы.</summary>
    public static string GetEmbedId(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"//api\.nextembed\.ws/embed/(?:movie|serial|embed)/([0-9]+)", RegexOptions.IgnoreCase);

        return m.Success ? m.Groups[1].Value : null;
    }
    #endregion

    #region Search
    /// <summary>Разбор выдачи /search-result/.</summary>
    public static List<SearchItem> ParseSearch(string html)
    {
        var items = new List<SearchItem>();

        if (string.IsNullOrEmpty(html))
            return items;

        string[] chunks = Regex.Split(html, "<div class=\"th-item\">");

        for (int i = 1; i < chunks.Length; i++)
        {
            string block = chunks[i];

            var href = Regex.Match(block, "class=\"th-in[^\"]*\"\\s+href=\"(https?://[^\"]+)\"");
            if (!href.Success)
                continue;

            string name = Regex.Match(block, "class=\"th-title\">([^<]*)</div>").Groups[1].Value;

            items.Add(new SearchItem()
            {
                uri = href.Groups[1].Value,
                name = Decode(name),
                year = Regex.Match(block, "class=\"th-series\">([^<]*)</div>").Groups[1].Value.Trim(),
                poster = Regex.Match(block, "<img[^>]+src=\"([^\"]+)\"").Groups[1].Value
            });
        }

        return items;
    }

    /// <summary>Выбор наиболее подходящей ссылки из выдачи.</summary>
    public static SearchItem PickBest(List<SearchItem> items, string title, short year, short serial)
    {
        if (items == null || items.Count == 0)
            return null;

        string want = Norm(title);

        if (string.IsNullOrEmpty(want))
            return items[0];

        SearchItem best = null;
        int bestScore = int.MinValue;

        foreach (var item in items)
        {
            string raw = item.name ?? string.Empty;

            int colon = raw.IndexOf(':');
            string head = colon > 0 ? raw.Substring(0, colon) : raw;

            string name = StripSeason(Norm(raw));
            string nameHead = StripSeason(Norm(head));

            int score;

            if (nameHead == want)
                score = 100;
            else if (name == want)
                score = 95;
            else if (Phrase(name, want))
                score = 82;
            else if (Phrase(nameHead, want))
                score = 80;
            else if (want.Length >= 3 && Phrase(want, name))
                score = 55;
            else if (nameHead.Length >= 3 && Phrase(want, nameHead))
                score = 50;
            else
                continue;

            if (year > 0 && item.year == year.ToString())
                score += 30;

            bool isSerial = item.uri.Contains("/serialy/", StringComparison.Ordinal) || item.uri.Contains("/show/", StringComparison.Ordinal);

            if (serial == 1)
                score += isSerial ? 15 : -15;
            else
                score += isSerial ? -15 : 15;

            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        return best;
    }

    static string Norm(string str)
    {
        if (string.IsNullOrEmpty(str))
            return string.Empty;

        str = str.ToLowerInvariant().Replace('ё', 'е');
        str = Regex.Replace(str, "[^\\p{L}\\p{Nd}\\s]+", " ");

        return Regex.Replace(str, "\\s+", " ").Trim();
    }

    static string StripSeason(string name)
    {
        return Regex.Replace(name, "\\s*\\(?\\s*[0-9]+\\s*сезон[а-я]*\\s*\\)?\\s*$", string.Empty).Trim();
    }

    /// <summary>Содержится ли фраза целиком (с границами слов) внутри строки.</summary>
    static bool Phrase(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
            return false;

        return (" " + haystack + " ").Contains(" " + needle + " ", StringComparison.Ordinal);
    }
    #endregion

    #region Embed
    public static EmbedModel ParseEmbed(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        if (html.Contains("playlist:", StringComparison.Ordinal))
        {
            var seasons = ParsePlaylist(html);

            if (seasons != null && seasons.Count > 0)
                return new EmbedModel() { seasons = seasons.ToArray() };

            return null;
        }

        if (html.Contains("source:", StringComparison.Ordinal))
        {
            var movie = ParseSource(html);

            if (movie != null)
                return new EmbedModel() { movie = movie };
        }

        return null;
    }

    static MovieItem ParseSource(string html)
    {
        int start = html.IndexOf("source:", StringComparison.Ordinal);
        if (start < 0)
            return null;

        string src = ExtractBalanced(html, html.IndexOf('{', start), '{', '}') ?? html.Substring(start);

        string hls = Url(src, "hls");
        string dash = Url(src, "dasha") ?? Url(src, "dash");

        if (string.IsNullOrEmpty(hls) && string.IsNullOrEmpty(dash))
            return null;

        var audio = AudioList(src);

        return new MovieItem()
        {
            hls = hls,
            dash = dash,
            audio = audio,
            voicename = Join(audio),
            cc = CcList(src)
        };
    }

    static List<SeasonItem> ParsePlaylist(string html)
    {
        int start = html.IndexOf("playlist:", StringComparison.Ordinal);
        if (start < 0)
            return null;

        int marker = html.IndexOf("seasons:", start, StringComparison.Ordinal);
        if (marker < 0)
            return null;

        string arr = ExtractBalanced(html, html.IndexOf('[', marker), '[', ']');
        if (string.IsNullOrEmpty(arr))
            return null;

        NeSeason[] root;

        try
        {
            root = JsonSerializer.Deserialize<NeSeason[]>(arr, json);
        }
        catch
        {
            return null;
        }

        if (root == null || root.Length == 0)
            return null;

        var seasons = new List<SeasonItem>(root.Length);

        foreach (var group in root.Where(s => s != null).GroupBy(s => s.season).OrderBy(s => s.Key))
        {
            var episodes = new List<EpisodeItem>();
            var unique = new HashSet<string>();

            foreach (var ep in group.SelectMany(s => s.episodes ?? Array.Empty<NeEpisode>()))
            {
                if (ep == null || string.IsNullOrWhiteSpace(ep.episode))
                    continue;

                if (!unique.Add(ep.episode.Trim()))
                    continue;

                string stream = Clean(ep.hls);
                string dash = Clean(ep.dasha) ?? Clean(ep.dash);

                if (string.IsNullOrEmpty(stream) && string.IsNullOrEmpty(dash))
                    continue;

                episodes.Add(new EpisodeItem()
                {
                    episode = ep.episode.Trim(),
                    hls = stream,
                    dash = dash,
                    audio = AudioList(ep.audio),
                    voicename = Join(AudioList(ep.audio)),
                    cc = CcList(ep.cc)
                });
            }

            if (episodes.Count == 0)
                continue;

            episodes.Sort((a, b) => Number(a.episode).CompareTo(Number(b.episode)));

            seasons.Add(new SeasonItem()
            {
                season = group.Key,
                episodes = episodes
            });
        }

        return seasons.Count > 0 ? seasons : null;
    }

    static List<string> AudioList(string block)
    {
        try
        {
            int start = block.IndexOf("audio:", StringComparison.Ordinal);
            if (start < 0)
                return null;

            string obj = ExtractBalanced(block, block.IndexOf('{', start), '{', '}');
            if (string.IsNullOrEmpty(obj))
                return null;

            return AudioList(JsonSerializer.Deserialize<NeAudio>(obj, json));
        }
        catch
        {
            return null;
        }
    }

    static List<string> AudioList(NeAudio audio)
    {
        if (audio?.names == null || audio.names.Length == 0)
            return null;

        var list = audio.names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Decode(n).Trim()).ToList();

        return list.Count > 0 ? list : null;
    }

    static List<Cc> CcList(NeCc[] cc)
    {
        if (cc == null || cc.Length == 0)
            return null;

        var list = cc.Where(c => !string.IsNullOrEmpty(c?.url) && !string.IsNullOrEmpty(c?.name))
                     .Select(c => new Cc() { url = Clean(c.url), name = Decode(c.name) })
                     .ToList();

        return list.Count > 0 ? list : null;
    }

    static List<Cc> CcList(string block)
    {
        try
        {
            int start = block.IndexOf("cc:", StringComparison.Ordinal);
            if (start < 0)
                return null;

            string arr = ExtractBalanced(block, block.IndexOf('[', start), '[', ']');
            if (string.IsNullOrEmpty(arr))
                return null;

            return CcList(JsonSerializer.Deserialize<NeCc[]>(arr, json));
        }
        catch
        {
            return null;
        }
    }

    static string Url(string block, string key)
    {
        var m = Regex.Match(block, key + ":\\s*\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase);

        return m.Success ? Clean(m.Groups[1].Value) : null;
    }

    static string Join(IEnumerable<string> names)
    {
        if (names == null)
            return null;

        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();

        return list.Count > 0 ? string.Join(", ", list) : null;
    }

    static int Number(string episode)
    {
        return int.TryParse(Regex.Match(episode ?? string.Empty, "[0-9]+").Value, out int n) ? n : 0;
    }
    #endregion

    #region Playlist
    /// <summary>
    /// Подмена технических имён аудио-дорожек (rus0, ukr4, eng6) на нормальные
    /// названия озвучек из плеера. <paramref name="names"/> — список через '|'.
    /// </summary>
    public static string RenameAudio(string master, string names)
    {
        if (string.IsNullOrEmpty(master) || string.IsNullOrEmpty(names))
            return master;

        string[] list = names.Split('|');

        return Regex.Replace(master, "(#EXT-X-MEDIA:[^\\r\\n]*?NAME=\")[^\"]*(\")", m =>
        {
            // индекс дорожки берём из строки NAME="rus0" / "ukr4" / "eng6" — цифры на конце
            var nameMatch = Regex.Match(m.Value, "NAME=\"([^\"]*)\"");
            if (!nameMatch.Success)
                return m.Value;

            var num = Regex.Match(nameMatch.Groups[1].Value, "([0-9]+)$");
            if (!num.Success)
                return m.Value;

            if (!int.TryParse(num.Groups[1].Value, out int index) || index < 0 || index >= list.Length)
                return m.Value;

            string friendly = (list[index] ?? string.Empty).Replace("\"", string.Empty).Trim();
            if (string.IsNullOrEmpty(friendly))
                return m.Value;

            return m.Groups[1].Value + friendly + m.Groups[2].Value;
        });
    }

    /// <summary>Замена всех вложенных ссылок плейлиста (URI="..." и отдельные строки) через прокси.</summary>
    public static string ProxyUrls(string master, Func<string, string> proxy)
    {
        if (string.IsNullOrEmpty(master) || proxy == null)
            return master;

        master = Regex.Replace(master, "URI=\"(?<u>https?://[^\"]+)\"", m =>
            "URI=\"" + proxy(Clean(m.Groups["u"].Value)) + "\"");

        return Regex.Replace(master, "(?m)^(?<u>https?://[^\\r\\n]+?)\\s*$", m =>
            proxy(Clean(m.Groups["u"].Value)));
    }

    /// <summary>
    /// Подмена технических языков аудио-дорожек в DASH-манифесте (lang="rus0", "ukr4", "eng6")
    /// на нормальные названия озвучек. <paramref name="names"/> — список через '|'.
    /// </summary>
    public static string RenameDashAudio(string mpd, string names)
    {
        if (string.IsNullOrEmpty(mpd) || string.IsNullOrEmpty(names))
            return mpd;

        string[] list = names.Split('|');

        return Regex.Replace(mpd, "<AdaptationSet\\b[^>]*>", m =>
        {
            string tag = m.Value;

            if (tag.IndexOf("contentType=\"audio\"", StringComparison.OrdinalIgnoreCase) < 0)
                return tag;

            var lang = Regex.Match(tag, "lang=\"([^\"]*)\"");
            if (!lang.Success)
                return tag;

            var num = Regex.Match(lang.Groups[1].Value, "([0-9]+)$");
            if (!num.Success)
                return tag;

            if (!int.TryParse(num.Groups[1].Value, out int index) || index < 0 || index >= list.Length)
                return tag;

            string friendly = (list[index] ?? string.Empty).Replace("\"", string.Empty).Trim();
            if (string.IsNullOrEmpty(friendly))
                return tag;

            int pos = lang.Groups[1].Index;
            return tag.Substring(0, pos) + friendly + tag.Substring(pos + lang.Groups[1].Length);
        });
    }
    #endregion

    #region Helpers
    /// <summary>Извлечение сбалансированного блока с учетом строковых литералов.</summary>
    public static string ExtractBalanced(string text, int start, char open, char close)
    {
        if (string.IsNullOrEmpty(text) || start < 0 || start >= text.Length || text[start] != open)
            return null;

        int depth = 0;
        bool inString = false, escape = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            if (c == '"')
                inString = true;
            else if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;

                if (depth == 0)
                    return text.Substring(start, i - start + 1);
            }
        }

        return null;
    }

    static string Clean(string str)
    {
        if (string.IsNullOrEmpty(str))
            return null;

        return str.Replace("\\u0026", "&").Replace("&amp;", "&").Replace("\\/", "/");
    }

    static string Decode(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        return System.Net.WebUtility.HtmlDecode(str).Trim();
    }
    #endregion
}
