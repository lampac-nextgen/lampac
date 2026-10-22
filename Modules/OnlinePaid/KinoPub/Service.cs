using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace KinoPub;

public class KinoPubInvoke
{
    #region KinoPubInvoke
    string host, token;
    string apihost;
    HttpHydra http;
    Func<string, string, string> onstreamfile;

    public KinoPubInvoke(string host, string apihost, string token, HttpHydra httpHydra, Func<string, string, string> onstreamfile)
    {
        this.host = host != null ? $"{host}/" : null;
        this.apihost = apihost;
        this.token = token;
        http = httpHydra;
        this.onstreamfile = onstreamfile;
    }
    #endregion

    #region Search
    async public Task<SearchResult> Search(string title, string original_title, short year, short clarification, string imdb_id, long kinopoisk_id)
    {
        if (string.IsNullOrEmpty(title ?? original_title))
            return null;

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        #region goSearch
        async Task<SearchResult> goSearch(string q)
        {
            if (string.IsNullOrEmpty(q))
                return null;

            var root = await http.Get<SearchObject>($"{apihost}/v1/items/search?q={HttpUtility.UrlEncode(q)}&access_token={token}&field=title&perpage=200", safety: true, textJson: true);
            if (root?.items == null)
                return null;

            var ids = new List<int>(root.items.Length);
            var result = new SearchResult() { similars = new SimilarTpl(root.items.Length) };

            string _q = SearchNameTo.Convert(q);

            foreach (var item in root.items)
            {
                string img = PosterApi.Size(item.posters?.Skip(1)?.First().Value);
                result.similars.Append(item.title, item.year.ToString(), item.voice, host + $"lite/kinopub?postid={item.id}&title={enc_title}&original_title={enc_original_title}", img);

                if ((item.kinopoisk > 0 && item.kinopoisk == kinopoisk_id) || $"tt{item.imdb}" == imdb_id)
                {
                    if (item.type != "3d")
                        result.id = item.id;
                }
                else
                {
                    if (item.year == year || (item.year == year - 1) || (item.year == year + 1))
                    {
                        if (SearchNameTo.StartsWith(item.title, _q) ||
                            SearchNameTo.EndsWith(item.title, _q))
                            ids.Add(item.id);
                    }
                }
            }

            if (ids.Count == 1 && result.id == 0)
                result.id = ids[0];

            return result;
        }
        #endregion

        if (clarification == 1)
            return await goSearch(title);

        return (await goSearch(original_title)) ?? (await goSearch(title));
    }
    #endregion

    #region Post
    async public Task<RootObject> Post(int postid)
    {
        var root = await http.Get<RootObject>($"{apihost}/v1/items/{postid}?access_token={token}", safety: true, textJson: true);
        if (root?.item.seasons == null && root?.item.videos == null)
            return null;

        return root;
    }
    #endregion

    #region Tpl
    public ITplResult Tpl(RootObject root, string filetype, string title, string original_title, int postid, short s = -1, int t = -1, string codec = null, string lang = null, VastConf vast = null, bool rjson = false)
    {
        if (root == null)
            return default;

        if (root?.item.videos != null)
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, root.item.videos.Length);

            if (filetype == "hls")
            {
                foreach (var a in root.item.videos[0].audios)
                {
                    var streamquality = new StreamQualityTpl();

                    foreach (var f in root.item.videos[0].files)
                    {
                        if (!string.IsNullOrEmpty(f.url.hls))
                            streamquality.Append(onstreamfile(f.url.hls.Replace("a1.m3u8", $"a{a.index}.m3u8"), null), f.quality);
                    }

                    if (streamquality.IsEmpty)
                        continue;

                    string voice = a.type?.title ?? a.lang ?? "оригинал";
                    if (!string.IsNullOrEmpty(a?.author?.title))
                        voice += $" ({a.author.title})";

                    #region subtitle
                    var subtitles = new SubtitleTpl(root.item.videos[0]?.subtitles?.Length ?? 0);

                    if (root.item.videos[0].subtitles != null)
                    {
                        foreach (var sub in root.item.videos[0].subtitles)
                        {
                            if (sub.url != null)
                                subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                        }
                    }

                    string subtitles_call = null;
                    if (subtitles.IsEmpty)
                        subtitles_call = host + $"lite/kinopub/subtitles.json?mid={root.item.videos[0].id}";
                    #endregion

                    var first = streamquality.Firts();

                    mtpl.Append(
                        voice,
                        first.link,
                        quality: first.quality,
                        streamquality: streamquality,
                        subtitles: subtitles,
                        subtitles_call: subtitles_call,
                        voice_name: WithFileDetails(a.codec, root.item.videos[0].files),
                        vast: vast
                    );
                }
            }
            else
            {
                foreach (var v in root.item.videos)
                {
                    #region voicename
                    string voicename = string.Empty;

                    if (v.audios != null)
                    {
                        foreach (var audio in v.audios)
                        {
                            if (audio.lang == "eng")
                            {
                                if (!voicename.Contains(audio.lang))
                                    voicename += "eng, ";
                            }
                            else
                            {
                                string a = audio?.author?.title ?? audio?.type?.title;
                                if (a != null)
                                {
                                    a = $"{a} ({audio.lang})";
                                    if (!voicename.Contains(a))
                                        voicename += $"{a}, ";
                                }
                            }
                        }

                        voicename = Regex.Replace(voicename, "[, ]+$", "");
                    }
                    #endregion

                    if (filetype == "hls4")
                    {
                        if (v.files[0].url.hls4 != null)
                        {
                            mtpl.Append(
                                v.files[0].quality,
                                onstreamfile(v.files[0].url.hls4, null),
                                voice_name: WithFileDetails(voicename, v.files),
                                vast: vast
                            );
                        }
                    }
                    else
                    {
                        if (v.files[0].url.http == null)
                            continue;

                        #region subtitle
                        var subtitles = new SubtitleTpl(v.subtitles?.Length ?? 0);

                        if (v.subtitles != null)
                        {
                            foreach (var sub in v.subtitles)
                            {
                                if (sub.url != null)
                                    subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                            }
                        }

                        string subtitles_call = null;
                        if (subtitles.IsEmpty)
                            subtitles_call = host + $"lite/kinopub/subtitles.json?mid={v.id}";
                        #endregion

                        var streamquality = new StreamQualityTpl(v.files.Length);

                        foreach (var f in v.files)
                            streamquality.Append(onstreamfile(f.url.http, f.file), f.quality);

                        var first = streamquality.Firts();
                        if (first != null)
                        {
                            mtpl.Append(
                                first.quality,
                                first.link,
                                subtitles: subtitles,
                                subtitles_call: subtitles_call,
                                voice_name: WithFileDetails(voicename, v.files),
                                streamquality: streamquality,
                                vast: vast
                            );
                        }
                    }
                }
            }

            return mtpl;
            #endregion
        }
        else
        {
            if (root?.item.seasons == null || root.item.seasons.Length == 0)
                return default;

            #region Сериал
            string enc_title = HttpUtility.UrlEncode(title);
            string enc_original_title = HttpUtility.UrlEncode(original_title);

            if (s == -1)
            {
                #region Сезоны
                var tpl = new SeasonTpl(root.item.quality > 0 ? $"{root.item.quality}p" : null, root.item.seasons.Length);

                foreach (var season in root.item.seasons)
                {
                    tpl.Append(
                        $"{season.number} сезон",
                        host + $"lite/kinopub?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={season.number}",
                        season.number
                    );
                }

                return tpl;
                #endregion
            }
            else
            {
                #region Серии
                if (filetype == "hls")
                {
                    #region Перевод
                    var vtpl = new VoiceTpl();
                    var hash = new HashSet<string>(20);

                    foreach (var a in root.item.seasons.First(i => i.number == s).episodes[0].audios)
                    {
                        string voice = a?.author?.title ?? a?.type?.title;

                        int? idt = a?.author?.id;
                        if (idt == null)
                            idt = a?.type?.id ?? null;

                        if (idt == null)
                        {
                            if (a.lang == "eng")
                            {
                                idt = 6;
                                voice = "Оригинал";
                            }
                            else
                            {
                                idt = 1;
                                voice = "По умолчанию";
                            }
                        }

                        if (string.IsNullOrEmpty(voice))
                            continue;

                        if (t == -1)
                        {
                            t = (int)idt;
                            codec = a.codec;
                            lang = a.lang;
                        }

                        if (!hash.Contains($"{voice}:{a.codec}:{a.lang}"))
                        {
                            hash.Add($"{voice}:{a.codec}:{a.lang}");

                            string link = host + $"lite/kinopub?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={s}&t={idt}&codec={a.codec}";
                            if (!string.IsNullOrEmpty(a.lang))
                                link += $"&lang={HttpUtility.UrlEncode(a.lang)}";
                            bool active = t == idt && (codec == null || codec == a.codec) && (string.IsNullOrEmpty(lang) || lang == a.lang);

                            vtpl.Append(
                                $"{voice} ({a.codec}{(string.IsNullOrEmpty(a.lang) ? "" : ", " + a.lang)})",
                                active,
                                link
                            );
                        }
                    }
                    #endregion

                    #region Серии
                    var etpl = new EpisodeTpl(vtpl);

                    foreach (var episode in root.item.seasons.First(i => i.number == s).episodes)
                    {
                        int voice_index = -1;
                        if (t == 1 && string.IsNullOrEmpty(lang))
                        {
                            voice_index = t;
                        }
                        else
                        {
                            foreach (var a in episode.audios)
                            {
                                int? idt = a?.author?.id ?? a?.type?.id;
                                if (idt == null)
                                    idt = a.lang == "eng" ? 6 : 1;

                                if (t == (int)idt && (codec == null || codec == a.codec) && (string.IsNullOrEmpty(lang) || lang == a.lang))
                                {
                                    voice_index = a!.index;
                                    break;
                                }
                            }

                            if (voice_index == -1)
                                break;
                        }

                        var streamquality = new StreamQualityTpl();

                        foreach (var f in episode.files)
                        {
                            if (!string.IsNullOrEmpty(f.url.hls))
                                streamquality.Append(onstreamfile(f.url.hls.Replace("a1.m3u8", $"a{voice_index}.m3u8"), null), f.quality);
                        }

                        #region subtitle
                        var subtitles = new SubtitleTpl(episode.subtitles?.Length ?? 0);

                        if (episode.subtitles != null)
                        {
                            foreach (var sub in episode.subtitles)
                            {
                                if (sub.url != null)
                                    subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                            }
                        }

                        string subtitles_call = null;
                        if (subtitles.IsEmpty)
                            subtitles_call = host + $"lite/kinopub/subtitles.json?mid={episode.id}";
                        #endregion

                        var first = streamquality.Firts();
                        if (first != null)
                        {
                            etpl.Append(
                                $"{episode.number} серия",
                                title ?? original_title,
                                s,
                                episode.number,
                                first.link,
                                streamquality: streamquality,
                                subtitles: subtitles,
                                subtitles_call: subtitles_call,
                                voice_name: FileDetails(episode.files),
                                vast: vast
                            );
                        }
                    }
                    #endregion

                    return etpl;
                }
                else
                {
                    var episodes = root.item.seasons.First(i => i.number == s).episodes;

                    var etpl = new EpisodeTpl(episodes.Length);

                    foreach (var episode in episodes)
                    {
                        #region voicename
                        string voicename = string.Empty;

                        if (episode.audios != null)
                        {
                            foreach (var audio in episode.audios)
                            {
                                string a = audio.author?.title ?? audio.lang;
                                if (a != null && !voicename.Contains(a) && a != "rus")
                                    voicename += $"{a}, ";
                            }

                            voicename = Regex.Replace(voicename, "[, ]+$", "");
                        }
                        #endregion

                        if (filetype == "hls4")
                        {
                            if (episode.files[0].url.hls4 == null)
                                continue;

                            etpl.Append(
                                $"{episode.number} серия",
                                title ?? original_title,
                                s,
                                episode.number,
                                onstreamfile(episode.files[0].url.hls4,
                                null),
                                voice_name: WithFileDetails(voicename, episode.files),
                                vast: vast
                            );
                        }
                        else
                        {
                            if (episode.files[0].url.http == null)
                                continue;

                            #region subtitle
                            var subtitles = new SubtitleTpl(episode.subtitles?.Length ?? 0);

                            if (episode.subtitles != null)
                            {
                                foreach (var sub in episode.subtitles)
                                {
                                    if (sub.url != null)
                                        subtitles.Append(sub.lang, onstreamfile(sub.url, null));
                                }
                            }

                            string subtitles_call = null;
                            if (subtitles.IsEmpty)
                                subtitles_call = host + $"lite/kinopub/subtitles.json?mid={episode.id}";
                            #endregion

                            #region streams
                            var streamquality = new StreamQualityTpl();

                            foreach (var f in episode.files)
                            {
                                if (f.url.http != null)
                                    streamquality.Append(onstreamfile(f.url.http, f.file), f.quality);
                            }
                            #endregion

                            var first = streamquality.Firts();
                            if (first != null)
                            {
                                etpl.Append(
                                    $"{episode.number} серия",
                                    title ?? original_title,
                                    s,
                                    episode.number,
                                    first.link,
                                    subtitles: subtitles,
                                    subtitles_call: subtitles_call,
                                    voice_name: WithFileDetails(voicename, episode.files),
                                    streamquality: streamquality,
                                    vast: vast
                                );
                            }
                        }
                    }

                    return etpl;
                }
                #endregion
            }
            #endregion
        }
    }
    #endregion

    static string FileDetails(File[] files)
    {
        var f = files != null && files.Length > 0 ? files[0] : null;
        if (f == null)
            return string.Empty;

        string r = string.IsNullOrEmpty(f.codec) ? string.Empty : f.codec;
        if (f.w > 0 && f.h > 0)
            r = r.Length == 0 ? $"{f.w}x{f.h}" : $"{r} · {f.w}x{f.h}";
        return r;
    }

    static string WithFileDetails(string prefix, File[] files)
    {
        string d = FileDetails(files);
        if (d.Length == 0)
            return prefix ?? string.Empty;
        return string.IsNullOrEmpty(prefix) ? d : $"{prefix} · {d}";
    }
}
