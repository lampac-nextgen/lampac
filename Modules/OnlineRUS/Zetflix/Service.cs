using Shared.Models.Base;
using Shared.Models.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Zetflix
{
    public struct ZetflixInvoke
    {
        string host, apihost;
        bool usehls;
        Func<string, string> onstreamfile;
        Func<string, List<HeadersModel>, Task<string>> onget;

        public ZetflixInvoke(string host, string apihost, bool hls, Func<string, List<HeadersModel>, Task<string>> onget, Func<string, string> onstreamfile)
        {
            this.host = host != null ? $"{host}/" : null;
            this.apihost = apihost;
            this.onstreamfile = onstreamfile;
            this.onget = onget;
            usehls = hls;
        }

        public async Task<EmbedModel> Embed(long kinopoisk_id, int s)
        {
            string html = await onget.Invoke($"{apihost}/iplayer/videodb.php?kp={kinopoisk_id}" + (s > 0 ? $"&season={s}" : ""), HeadersModel.Init(
                ("dnt", "1"),
                ("pragma", "no-cache"),
                ("referer", "https://www.google.com/"),
                ("upgrade-insecure-requests", "1")
            ));

            return Embed(html);
        }

        public EmbedModel Embed(string html)
        {
            if (html == null)
                return null;

            string quality = html.Contains("1080p") ? "1080p" : html.Contains("720p") ? "720p" : "480p";
            string check_url = Regex.Match(html, "(https?://[^\\[\\|,\n\r\t ]+\\.mp4)").Groups[1].Value;

            string file = Regex.Match(html, "file:(\\[[^\n\r]+\\])(,|}\\) ;)").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(file))
                file = Regex.Match(html, "file:(\\[[\\s\\S]+?\\])\\s*(,|\\}\\) ;)", RegexOptions.Multiline).Groups[1].Value;

            if (string.IsNullOrWhiteSpace(file))
            {
                file = Regex.Match(html, "file:\"([^\"]+)\"").Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(file))
                {
                    return new EmbedModel()
                    {
                        pl = new List<RootObject>()
                        {
                            new RootObject() { file = file, title = "Дубляж" }
                        },
                        movie = true,
                        quality = quality,
                        check_url = check_url
                    };
                }

                return null;
            }

            file = Regex.Replace(file.Trim(), "(\\{|, )([a-zA-Z_][a-zA-Z0-9_]*): ?", "$1\"$2\":")
                        .Replace("},]", "}]");

            List<RootObject> pl = null;

            try
            {
                pl = JsonSerializer.Deserialize<List<RootObject>>(file);
                if (pl == null || pl.Count == 0)
                    return null;
            }
            catch
            {
                return null;
            }

            bool isMovie = !file.Contains("\"comment\":", StringComparison.OrdinalIgnoreCase);
            return new EmbedModel() { pl = pl, movie = isMovie, quality = quality, check_url = check_url };
        }

        public async Task<int> number_of_seasons(long id)
        {
            int number_of_seasons = 1;
            string themoviedb = await onget.Invoke($"https://api.themoviedb.org/3/tv/{id}?api_key=4ef0d7355d9ffb5151e987764708ce96&language=ru-RU", HeadersModel.Init(
                ("accept", "application/json")
            ));

            if (themoviedb != null)
            {
                try
                {
                    var root = JsonSerializer.Deserialize<JsonObject>(themoviedb);
                    number_of_seasons = root["number_of_seasons"].GetValue<int>();
                    if (number_of_seasons < 1)
                        number_of_seasons = 1;
                }
                catch { }
            }

            if (number_of_seasons <= 0)
                number_of_seasons = 1;

            return number_of_seasons;
        }

        public ITplResult Tpl(EmbedModel root, int number_of_seasons, long id, long kinopoisk_id, string title, string original_title, string t, int s, bool isbwa = false, bool rjson = false, VastConf vast = null)
        {
            if (root?.pl == null || root.pl.Count == 0)
                return default;

            if (root.movie)
            {
                var mtpl = new MovieTpl(title, original_title, root.pl.Count);

                foreach (var pl in root.pl)
                {
                    string name = pl.title;
                    string file = pl.file;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(file))
                        continue;

                    var streamquality = new StreamQualityTpl();

                    foreach (Match m in Regex.Matches(file, "\\[(1080|720|480|360)p?\\]([^\\[\\|,\n\r\t ]+\\.(mp4|m3u8))"))
                    {
                        string link = m.Groups[2].Value;
                        if (string.IsNullOrEmpty(link))
                            continue;

                        if (usehls && !link.Contains(".m3u"))
                            link += ":hls:manifest.m3u8";
                        else if (!usehls && link.Contains(".m3u"))
                            link = link.Replace(":hls:manifest.m3u8", "");

                        if (isbwa)
                            link = Regex.Replace(link, "/([0-9]+)\\.(m3u8|mp4)", $"/{m.Groups[1].Value}.$2");

                        streamquality.Insert(onstreamfile.Invoke(link), $"{m.Groups[1].Value}p");
                    }

                    mtpl.Append(name, streamquality.Firts().link, streamquality: streamquality, vast: vast);
                }

                return mtpl;
            }

            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (s == -1)
            {
                var tpl = new SeasonTpl(root.quality);

                for (int i = 1; i <= number_of_seasons; i++)
                {
                    string link = host + $"lite/zetflix?serial=1&id={id}&rjson={rjson}&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&s={i}";
                    tpl.Append($"{i} сезон", link, i);
                }

                return tpl;
            }

            var vtpl = new VoiceTpl();
            var etpl = new EpisodeTpl();
            var hashvoices = new HashSet<string>();
            string sArch = s.ToString();

            foreach (var episode in root.pl.AsEnumerable().Reverse())
            {
                var episodes = episode.folder;
                if (episodes == null || episodes.Length == 0)
                    continue;

                string perevod = episode.title;
                if (perevod != null && string.IsNullOrEmpty(t))
                    t = perevod;

                if (perevod != null && !hashvoices.Contains(perevod))
                {
                    hashvoices.Add(perevod);
                    string link = host + $"lite/zetflix?serial=1&id={id}&rjson={rjson}&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&s={s}&t={HttpUtility.UrlEncode(perevod)}";

                    vtpl.Append(perevod, t == perevod, link);
                }

                if (perevod != t)
                    continue;

                foreach (var pl in episodes)
                {
                    string name = pl.comment;
                    string episodeNum = Regex.Match(name ?? string.Empty, "^([0-9]+)").Groups[1].Value;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(episodeNum))
                        continue;

                    string link = host + $"lite/zetflix/video?serial=1&kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&s={s}&e={episodeNum}&t={HttpUtility.UrlEncode(perevod)}";
                    etpl.Append(name, title ?? original_title, sArch, episodeNum, link, "call", streamlink: link + "&play=true", voice_name: perevod, vast: vast);
                }
            }

            etpl.Append(vtpl);

            return etpl;
        }
    }
}
