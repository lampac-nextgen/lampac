using Filmix.Models;
using FilmixTVModels;
using System.Text.Json;

namespace Filmix;

public class FilmixTVInvoke
{
    #region FilmixTVInvoke
    string host;
    string apihost;
    List<HeadersModel> bearer;
    HttpHydra httpHydra;
    Func<string, string> onstreamfile;
    bool rjson;

    public FilmixTVInvoke(string host, string apihost, List<HeadersModel> bearer, HttpHydra httpHydra, Func<string, string> onstreamfile, bool rjson = false)
    {
        this.host = host != null ? $"{host}/" : null;
        this.apihost = apihost;
        this.bearer = bearer;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
        this.rjson = rjson;
    }
    #endregion

    #region Search
    async public Task<SearchResult> Search(string title, string original_title, int clarification, int year, bool similar)
    {
        if (string.IsNullOrWhiteSpace(title ?? original_title))
            return null;

        string uri = $"{apihost}/api-fx/list?search={HttpUtility.UrlEncode(clarification == 1 ? title : (original_title ?? title))}&limit=48";

        var root = await httpHydra.Get<FilmixTVModels.RootObject>(uri, addheaders: bearer, safety: true);
        if (root?.items == null || root.items.Count == 0)
            return await Search2(title, original_title, year, clarification);

        List<SearchModel> items = root.items;

        var ids = new List<int>(items.Count);
        var stpl = new SimilarTpl(items.Count);

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        string stitle = StringConvert.SearchName(title);
        string sorigtitle = StringConvert.SearchName(original_title);

        foreach (var item in items)
        {
            if (item == null)
                continue;

            string name = !string.IsNullOrEmpty(item.title) && !string.IsNullOrEmpty(item.original_name) ? $"{item.title} / {item.original_name}" : (item.title ?? item.original_name);

            stpl.Append(
                name,
                item.year.ToString(),
                string.Empty,
                host + $"lite/filmixtv?postid={item.id}&title={enc_title}&original_title={enc_original_title}",
                PosterApi.Size(item.poster)
            );

            if ((!string.IsNullOrEmpty(stitle) && StringConvert.SearchName(item.title) == stitle) ||
                (!string.IsNullOrEmpty(sorigtitle) && StringConvert.SearchName(item.original_name) == sorigtitle))
            {
                if (item.year == year)
                    ids.Add(item.id);
            }
        }

        if (ids.Count == 1 && !similar)
            return new SearchResult() { id = ids[0] };

        return new SearchResult() { similars = stpl };
    }
    #endregion

    #region Search2
    async Task<SearchResult> Search2(string title, string original_title, int year, int clarification)
    {
        var result = await gosearch(clarification == 1 ? original_title : title);
        if (result == null)
            result = await gosearch(clarification == 1 ? title : original_title);

        if (result == null)
            return null;

        var ids = new List<int>(result.Count);
        var stpl = new SimilarTpl(result.Count);

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        string stitle = StringConvert.SearchName(title);
        string sorigtitle = StringConvert.SearchName(original_title);

        foreach (var item in result)
        {
            if (item == null)
                continue;

            string name = !string.IsNullOrEmpty(item.title) && !string.IsNullOrEmpty(item.original_title) ? $"{item.title} / {item.original_title}" : (item.title ?? item.original_title);

            stpl.Append(
                name,
                item.year.ToString(),
                string.Empty,
                host + $"lite/filmixtv?postid={item.id}&title={enc_title}&original_title={enc_original_title}",
                PosterApi.Size(item.poster)
            );

            if ((!string.IsNullOrEmpty(stitle) && StringConvert.SearchName(item.title) == stitle) ||
                (!string.IsNullOrEmpty(sorigtitle) && StringConvert.SearchName(item.original_title) == sorigtitle))
            {
                if (item.year == year)
                    ids.Add(item.id);
            }
        }

        if (ids.Count == 1)
            return new SearchResult() { id = ids[0] };

        return new SearchResult() { similars = stpl };
    }

    async Task<List<SearchModel>> gosearch(string story)
    {
        if (string.IsNullOrEmpty(story))
            return null;

        string uri = $"{ModInit.conf.Filmix.host}/api/v2/search?story={HttpUtility.UrlEncode(story)}&user_dev_apk=2.0.1&user_dev_id=&user_dev_name=Xiaomi&user_dev_os=11&user_dev_token=&user_dev_vendor=Xiaomi";

        var root = await httpHydra.Get<List<SearchModel>>(uri, addheaders: bearer, safety: true);

        if (root == null || root.Count == 0)
            return null;

        return root;
    }
    #endregion

    #region Post
    public FilmixTVModels.RootObject Post(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var rootMs = new FilmixTVModels.RootObject();

            if (JsonDocument.Parse(json).RootElement.ValueKind == JsonValueKind.Array)
            {
                rootMs.Movies = JsonSerializer.Deserialize<MovieTV[]>(json, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                });
            }
            else
            {
                rootMs.SerialVoice = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Season>>>(json, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                });
            }

            return rootMs;
        }
        catch { return null; }
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(FilmixTVModels.RootObject root, bool pro, int postid, string title, string original_title, int t, int? s, VastConf vast = null)
    {
        if (root == null)
            return default;

        #region Сериал
        if (root.SerialVoice != null)
        {
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (s == null)
            {
                #region Сезоны
                var maxQuality = root.SerialVoice.SelectMany(i => i.Value)
                    .SelectMany(season => season.Value.episodes)
                    .SelectMany(episode => episode.Value.files)
                    .Max(file => file.quality);

                var tpl = new SeasonTpl($"{maxQuality}p");
                var temp = new HashSet<int>();

                foreach (var translation in root.SerialVoice)
                {
                    foreach (var season in translation.Value)
                    {
                        if (temp.Add(season.Value.season))
                        {
                            tpl.Append(
                                $"{season.Value.season} сезон",
                                $"{host}lite/filmixtv?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={season.Value.season}",
                                season.Value.season
                            );
                        }
                    }
                }

                return tpl;
                #endregion
            }
            else
            {
                #region Перевод
                int indexTranslate = 0;
                var vtpl = new VoiceTpl();

                foreach (var translation in root.SerialVoice)
                {
                    foreach (var season in translation.Value)
                    {
                        if (season.Value.season == s)
                        {
                            string link = host + $"lite/filmixtv?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={s}&t={indexTranslate}";
                            bool active = t == indexTranslate;

                            if (t == -1)
                                t = indexTranslate;

                            vtpl.Append(
                                translation.Key,
                                active,
                                link
                            );
                        }
                    }

                    indexTranslate++;
                }
                #endregion

                var selectedSeason = root.SerialVoice.ElementAt(t).Value.FirstOrDefault(x => x.Value.season == s);

                if (selectedSeason.Value?.episodes == null)
                    return default;

                var etpl = new EpisodeTpl(vtpl, selectedSeason.Value.episodes.Count);

                foreach (var episode in selectedSeason.Value.episodes)
                {
                    var streamquality = new StreamQualityTpl();

                    var sortedFiles = episode.Value.files
                        .Where(file => pro || file.quality <= 720)
                        .OrderByDescending(file => file.quality);

                    foreach (var file in sortedFiles)
                        streamquality.Append(onstreamfile.Invoke(file.url), $"{file.quality}p");

                    var first = streamquality.Firts();
                    if (first != null)
                    {
                        etpl.Append(
                            $"{episode.Key.TrimStart('e')} серия",
                            title ?? original_title,
                            selectedSeason.Value.season.ToString(),
                            episode.Key.TrimStart('e'),
                            first.link,
                            streamquality: streamquality,
                            vast: vast
                        );
                    }
                }

                return etpl;
            }
        }
        #endregion

        #region Фильм
        else if (root.Movies != null)
        {
            var mtpl = new MovieTpl(title, original_title, root.Movies.Length);

            foreach (var item in root.Movies)
            {
                var streamquality = new StreamQualityTpl();

                foreach (var file in item.files)
                {
                    if (!pro)
                    {
                        if (pro && file.quality > 480)
                            continue;

                        if (file.quality > 720)
                            continue;
                    }

                    streamquality.Append(onstreamfile.Invoke(file.url), $"{file.quality}p");
                }

                var first = streamquality.Firts();
                if (first != null)
                {
                    mtpl.Append(
                        item.voiceover,
                        first.link,
                        streamquality: streamquality,
                        vast: vast
                    );
                }
            }

            return mtpl;
        }
        #endregion

        return default;
    }
    #endregion
}
