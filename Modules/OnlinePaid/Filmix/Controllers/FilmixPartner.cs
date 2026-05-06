using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using Filmix.Models;

namespace Filmix;

public class FilmixPartner : BaseOnlineController<FilmixSettings>
{
    public FilmixPartner() : base(ModInit.conf.FilmixPartner) { }

    [HttpGet]
    [Route("lite/fxapi")]
    async public Task<ActionResult> Index(long kinopoisk_id, bool checksearch, string title, string original_title, int year, int postid, int t = -1, int s = -1, bool rjson = false, bool similar = false, string source = null, string id = null)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (postid == 0 && !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(id))
        {
            if (source.Equals("filmix", StringComparison.OrdinalIgnoreCase) ||
                source.Equals("filmixapp", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(id, out postid))
                    int.TryParse(Regex.Match(id, "/([0-9]+)-").Groups[1].Value, out postid);
            }
        }

        if (postid == 0)
        {
            var res = await InvokeCache($"fxapi:search:{title}:{original_title}:{similar}", 40,
                () => Search(title, original_title, year, similar)
            );

            if (similar)
                return ContentTpl(res.similars);

            if (res != null)
                postid = res.id;

            // платный поиск
            if (!checksearch && postid == 0 && kinopoisk_id > 0)
                postid = await searchKp(kinopoisk_id);

            if (postid == 0 && res?.similars != null)
                return ContentTpl(res.similars);
        }

        if (postid == 0)
            return OnError();

        if (checksearch)
            return Content("data-json=");

        string hashKey = $"fxapi:hashfimix:{requestInfo.IP}";
        hybridCache.TryGetValue(hashKey, out string hashfimix);

        var cache = await InvokeCacheResult<JArray>($"fxapi:{postid}:{(string.IsNullOrEmpty(hashfimix) ? requestInfo.IP : "")}", 60 * 2, async e =>
        {
            #region video_links
            string XFXTOKEN = await getXFXTOKEN(requestInfo.user_uid);
            if (string.IsNullOrWhiteSpace(XFXTOKEN))
                return e.Fail("XFXTOKEN");

            var root = await Http.Get<JArray>($"{init.host}/video_links/{postid}", headers: httpHeaders(init, HeadersModel.Init("X-FX-TOKEN", XFXTOKEN)));

            if (root == null || root.Count == 0)
                return e.Fail("video_links");

            var first = root.First.ToObject<JObject>();
            if (!first.ContainsKey("files") && !first.ContainsKey("seasons"))
                return e.Fail("files_or_seasons");

            if (string.IsNullOrEmpty(hashfimix))
            {
                hashfimix = Regex.Match(root.First.ToString().Replace("\\", ""), "/s/([^/]+)/").Groups[1].Value;

                if (!string.IsNullOrEmpty(hashfimix))
                    hybridCache.Set(hashKey, hashfimix, DateTime.Now.AddHours(1));
            }

            return e.Success(root);
            #endregion
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var cacheRoot = cache.Value;

        if (cacheRoot.First.ToObject<JObject>().ContainsKey("files"))
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title, cacheRoot.Count);

            foreach (var movie in cacheRoot)
            {
                var streamquality = new StreamQualityTpl();

                foreach (var file in movie.Value<JArray>("files").OrderByDescending(i => i.Value<int>("quality")))
                {
                    int q = file.Value<int>("quality");
                    string url = file.Value<string>("url");
                    if (!string.IsNullOrEmpty(hashfimix))
                        url = Regex.Replace(url, "/s/[^/]+/", $"/s/{hashfimix}/");

                    streamquality.Append(HostStreamProxy(url), $"{q}p");
                }

                var first = streamquality.Firts();
                if (first != null)
                {
                    mtpl.Append(
                        movie.Value<string>("name"),
                        first.link,
                        streamquality: streamquality,
                        vast: init.vast
                    );
                }
            }

            return ContentTpl(mtpl);
            #endregion
        }
        else
        {
            #region Сериал
            if (s == -1)
            {
                #region Сезоны
                var tpl = new SeasonTpl(cacheRoot.Count);
                var temp_season = new HashSet<int>();
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);

                foreach (var translation in cacheRoot)
                {
                    foreach (var season in translation.Value<JArray>("seasons"))
                    {
                        int sid = season.Value<int>("season");
                        string sname = $"{sid} сезон";

                        if (temp_season.Add(sid))
                        {
                            tpl.Append(
                                sname,
                                $"{host}/lite/fxapi?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={sid}",
                                sid
                            );
                        }
                    }
                }

                return ContentTpl(tpl);
                #endregion
            }
            else
            {
                #region Перевод
                int indexTranslate = 0;
                var vtpl = new VoiceTpl();
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);

                foreach (var translation in cacheRoot)
                {
                    foreach (var season in translation.Value<JArray>("seasons"))
                    {
                        if (season.Value<int>("season") == s)
                        {
                            string link = $"{host}/lite/fxapi?rjson={rjson}&postid={postid}&title={enc_title}&original_title={enc_original_title}&s={s}&t={indexTranslate}";
                            bool active = t == indexTranslate;

                            if (t == -1)
                                t = indexTranslate;

                            vtpl.Append(
                                translation.Value<string>("name"),
                                active,
                                link
                            );
                            break;
                        }
                    }

                    indexTranslate++;
                }
                #endregion

                #region Серии
                var etpl = new EpisodeTpl(vtpl);

                foreach (var episode in cacheRoot[t].Value<JArray>("seasons").FirstOrDefault(i => i.Value<int>("season") == s).Value<JObject>("episodes").ToObject<Dictionary<string, JObject>>().Values)
                {
                    var streamquality = new StreamQualityTpl();

                    foreach (var file in episode.Value<JArray>("files").OrderByDescending(i => i.Value<int>("quality")))
                    {
                        int q = file.Value<int>("quality");
                        string url = file.Value<string>("url");
                        if (!string.IsNullOrEmpty(hashfimix))
                            url = Regex.Replace(url, "/s/[^/]+/", $"/s/{hashfimix}/");

                        string l = HostStreamProxy(url);

                        streamquality.Append(l, $"{q}p");
                    }

                    int e = episode.Value<int>("episode");

                    var first = streamquality.Firts();
                    if (first != null)
                    {
                        etpl.Append(
                            $"{e} серия",
                            title ?? original_title,
                            s.ToString(),
                            e.ToString(),
                            first.link,
                            streamquality: streamquality,
                            vast: init.vast
                        );
                    }
                }
                #endregion

                return ContentTpl(etpl);
            }
            #endregion
        }
    }


    [HttpGet]
    [AllowAnonymous]
    [Route("lite/fxapi/lowlevel/{*uri}")]
    async public Task<ActionResult> LowlevelApi(string uri)
    {
        var init = ModInit.conf.FilmixPartner;

        if (!init.enable)
            return OnError("disable", gbcache: false);

        if (!HttpContext.Request.Headers.TryGetValue("low_passw", out var low_passw) || low_passw.ToString() != init.lowlevel_api_passw)
            return OnError("lowlevel_api", gbcache: false);

        string XFXTOKEN = await getXFXTOKEN();
        if (string.IsNullOrWhiteSpace(XFXTOKEN))
            return OnError("XFXTOKEN", gbcache: false);

        string json = await Http.Get($"{init.host}/{uri}", headers: httpHeaders(init, HeadersModel.Init("X-FX-TOKEN", XFXTOKEN)));

        return Content(json, "application/json; charset=utf-8");
    }


    #region search
    async ValueTask<int> searchKp(long kinopoisk_id)
    {
        if (kinopoisk_id == 0)
            return 0;

        string memKey = $"fxapi:search:{kinopoisk_id}";
        if (!hybridCache.TryGetValue(memKey, out int postid))
        {
            string XFXTOKEN = await getXFXTOKEN();
            if (string.IsNullOrWhiteSpace(XFXTOKEN))
                return 0;

            var root = await Http.Get<JObject>($"{init.host}/film/by-kp/{kinopoisk_id}", proxy: proxy, headers: httpHeaders(init, HeadersModel.Init("X-FX-TOKEN", XFXTOKEN)));

            if (root == null || !root.ContainsKey("id"))
                return 0;

            postid = root.Value<int>("id");

            if (postid > 0)
                hybridCache.Set(memKey, postid, DateTime.Now.AddDays(20));
            else
                hybridCache.Set(memKey, postid, DateTime.Now.AddDays(1));
        }

        return postid;
    }


    async Task<SearchResult> Search(string title, string original_title, int year, bool similar)
    {
        if (string.IsNullOrWhiteSpace(title ?? original_title))
            return null;

        string filmixHost = ModInit.conf.Filmix.host;
        string uri = $"{filmixHost}/api/v2/search?story={HttpUtility.UrlEncode(title)}&user_dev_apk=2.0.1&user_dev_id=&user_dev_name=Xiaomi&user_dev_os=11&user_dev_token={init.token}&user_dev_vendor=Xiaomi";

        var root = await Http.Get<List<SearchModel>>(init.cors(uri), timeoutSeconds: 7, proxy: proxy, useDefaultHeaders: false, headers: HeadersModel.Init(
            ("Accept-Encoding", "gzip")
        ));

        if (root == null || root.Count == 0)
        {
            if (root == null)
                proxyManager?.Refresh();

            return await Search2(title, original_title, year);
        }

        var ids = new List<int>();
        var stpl = new SimilarTpl(root.Count);

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        string stitle = StringConvert.SearchName(title);
        string sorigtitle = StringConvert.SearchName(original_title);

        foreach (var item in root)
        {
            if (item == null)
                continue;

            string name = !string.IsNullOrEmpty(item.title) && !string.IsNullOrEmpty(item.original_title) ? $"{item.title} / {item.original_title}" : (item.title ?? item.original_title);

            stpl.Append(
                name,
                item.year.ToString(),
                string.Empty,
                host + $"lite/fxapi?postid={item.id}&title={enc_title}&original_title={enc_original_title}",
                PosterApi.Size(item.poster)
            );

            if ((!string.IsNullOrEmpty(stitle) && StringConvert.SearchName(item.title) == stitle) ||
                (!string.IsNullOrEmpty(sorigtitle) && StringConvert.SearchName(item.original_title) == sorigtitle))
            {
                if (item.year == year)
                    ids.Add(item.id);
            }
        }

        if (ids.Count == 1 && !similar)
            return new SearchResult() { id = ids[0] };

        return new SearchResult() { similars = stpl };
    }


    async Task<SearchResult> Search2(string title, string original_title, int year)
    {
        async Task<List<SearchModel>> gosearch(string story)
        {
            if (string.IsNullOrEmpty(story))
                return null;

            string filmixTVHost = ModInit.conf.FilmixTV.host;
            string uri = $"{filmixTVHost}/api-fx/list?search={HttpUtility.UrlEncode(story)}&limit=48";

            var root = await Http.Get<JObject>(uri, proxy: proxy, timeoutSeconds: 5);

            if (root == null || !root.ContainsKey("items"))
                return null;

            return root["items"].ToObject<List<SearchModel>>();
        }

        var result = await gosearch(original_title);
        if (result == null)
            result = await gosearch(title);

        if (result == null)
            return default;

        var ids = new List<int>();
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
                host + $"lite/filmix?postid={item.id}&title={enc_title}&original_title={enc_original_title}",
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
    #endregion

    #region getXFXTOKEN
    static long userid = 1;

    static string serverip = null;

    async ValueTask<string> getXFXTOKEN(string uid = null)
    {
        var init = ModInit.conf.FilmixPartner;

        if (serverip == null)
        {
            var myip = await Http.Get<JObject>($"{init.host}/my_ip", headers: httpHeaders(init));
            if (myip == null || string.IsNullOrWhiteSpace(myip.Value<string>("ip")))
                return null;

            serverip = myip.Value<string>("ip");
        }

        if (userid > 20_000)
            userid = 1;
        userid++;

        if (uid != null)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // Преобразуем строку в байты и вычисляем хэш
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(uid));

                // Преобразуем первые 8 байт хэша в число
                long result = BitConverter.ToInt64(bytes, 0);
                userid = Math.Abs(result); // Возвращаем положительное значение
            }
        }

        string XNICK = ReverseString(DateTime.Now.ToString("HHmm")) + DateTime.Now.ToString("yyyyMMdd");
        string XSAM = ReverseString(serverip.Replace(".", "")) + DateTime.Now.ToString("HHmm");

        var salt = await Http.Post<JObject>($"{init.host}/request-salt", $"key={init.APIKEY}", headers: httpHeaders(init, HeadersModel.Init(
            ("X-NICK", SHA1(XNICK)),
            ("X-SAM", SHA1(XSAM))
        )));

        if (salt == null || string.IsNullOrWhiteSpace(salt.Value<string>("salt")))
            return null;

        string token = SHA1(init.APISECRET + init.APIKEY + CrypTo.md5(array_sum(serverip) + salt.Value<string>("salt")));

        var xtk = await Http.Post<JObject>($"{init.host}/request-token", $"user_name={init.user_name}&user_passw={init.user_passw}&key={init.APIKEY}&token={token}", proxy: proxy, headers: httpHeaders(init, HeadersModel.Init("User-Id", userid.ToString())));

        if (xtk != null && !string.IsNullOrWhiteSpace(xtk.Value<string>("token")))
            return xtk.Value<string>("token");

        return null;
    }
    #endregion

    #region ReverseString / array_sum / SHA1
    static string ReverseString(string s)
    {
        char[] charArray = s.ToCharArray();
        Array.Reverse(charArray);
        return new string(charArray);
    }

    static int array_sum(string s)
    {
        List<int> mass = new List<int>();
        foreach (var num in s.Split("."))
            mass.Add(int.Parse(num));

        return mass.Sum();
    }

    static string SHA1(string IntText)
    {
        using (var sha1 = new System.Security.Cryptography.SHA1Managed())
        {
            var result = sha1.ComputeHash(Encoding.UTF8.GetBytes(IntText));
            return BitConverter.ToString(result).Replace("-", "").ToLower();
        }
    }
    #endregion
}
