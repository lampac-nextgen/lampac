using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace IptvOnline;

public class IptvOnlineController : BaseOnlineController
{
    public IptvOnlineController() : base(ModInit.conf) { }

    #region Bind
    [HttpGet]
    [AllowAnonymous]
    [Route("lite/iptvonline/bind")]
    public ActionResult Bind(string KEY, string ID)
    {
        string html = string.Empty;

        if (string.IsNullOrWhiteSpace(KEY) || string.IsNullOrWhiteSpace(ID))
        {
            html = "Введите данные <a href='https://iptv.online/ru/dealers/api' target=_blank>https://iptv.online/ru/dealers/api</a> <br> <br><form method=\"get\" action=\"/lite/iptvonline/bind\"><input type=\"text\" name=\"KEY\" placeholder=\"X-API-KEY\"> &nbsp; &nbsp; <input type=\"text\" name=\"ID\" placeholder=\"X-API-ID\"><br><br><button>Авторизоваться</button></form> ";
        }
        else
        {
            html = "Добавьте в init.conf<br><br>\"IptvOnline\": {<br>&nbsp;&nbsp;\"enable\": true,<br>&nbsp;&nbsp;\"token\": \"" + HttpUtility.HtmlEncode($"{ID}:{KEY}") + "\"<br>}";
        }

        return ContentTo(html);
    }
    #endregion

    [HttpGet, Staticache(manually: true)]
    [Route("lite/iptvonline")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, short serial = -1, short s = -1, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (string.IsNullOrEmpty(init.token))
            return OnError("token", statusCode: 401, gbcache: false);

        #region AUTH
        if (!hybridCache.TryGetValue($"iptvonline:auth:{init.token}", out string codeauth))
        {
            var auth = await httpHydra.Post<JObject>($"{init.host}/v1/api/auth", "", useDefaultHeaders: false, safety: true, addheaders: HeadersModel.Init(
                ("X-API-KEY", init.token.Split(":")[1]),
                ("X-API-ID", init.token.Split(":")[0])
            ));

            if (auth == null)
                return OnError(refresh_proxy: true);

            string code = auth.Value<string>("code");
            if (string.IsNullOrEmpty(code))
                return OnError(refresh_proxy: true);

            codeauth = code;
            hybridCache.Set($"iptvonline:auth:{init.token}", codeauth, DateTime.Now.AddHours(2));
        }
        #endregion

        var data = await search(codeauth, serial, imdb_id, kinopoisk_id, title, original_title);
        if (data == null)
            return OnError();

        if (data.Value<string>("message") != null)
            return ShowError(data.Value<string>("message"));

        #region media
        string id = data.Value<string>("id");
        var cache = await InvokeCacheResult<JToken>($"IptvOnline:{id}:{init.token}", 20, async e =>
        {
            string uri = $"{init.host}/v1/api/media/{(serial == 1 ? "serials" : "movies")}/{id}/";

            var root = await httpHydra.Get<JObject>(uri, useDefaultHeaders: false, safety: true, addheaders: HeadersModel.Init(
                ("X-API-AUTH", codeauth),
                ("X-API-ID", init.token.Split(":")[0])
            ));

            if (root == null || !root.ContainsKey("data"))
                return e.Fail("data", refresh_proxy: true);

            return e.Success(root["data"]);
        });
        #endregion

        return ContentTpl(cache, () =>
        {
            if (cache.Value.Value<string>("category") == "movie")
            {
                #region Фильм
                var mtpl = new MovieTpl(title, original_title, 1);

                string stream = HostStreamProxy(cache.Value["medias"].First.Value<string>("url") + "#.m3u8");
                string quality = cache.Value.Value<int?>("quality")?.ToString();
                if (quality != null)
                    quality += "p";

                mtpl.Append(
                    quality ?? title,
                    stream,
                    vast: init.vast
                );

                return mtpl;
                #endregion
            }
            else
            {
                #region Сериал
                if (s == -1)
                {
                    string enc_title = HttpUtility.UrlEncode(title);
                    string enc_original_title = HttpUtility.UrlEncode(original_title);

                    string quality = cache.Value.Value<string>("quality");
                    if (quality != null)
                        quality += "p";

                    var tpl = new SeasonTpl(quality);

                    foreach (var media in cache.Value["medias"])
                    {
                        int season = media.Value<int>("season");

                        tpl.Append(
                            $"{season} сезон",
                            $"{host}/lite/iptvonline?rjson={rjson}&serial={serial}&kinopoisk_id={kinopoisk_id}&imdb_id={imdb_id}&title={enc_title}&original_title={enc_original_title}&s={season}",
                            season
                        );
                    }

                    return tpl;
                }
                else
                {
                    var etpl = new EpisodeTpl();

                    foreach (var episode in cache.Value["medias"].First(i => i.Value<int>("season") == s).Value<JArray>("episodes"))
                    {
                        string name = episode.Value<string>("title");
                        string file = episode.Value<string>("url") + "#.m3u8";

                        if (string.IsNullOrEmpty(file))
                            continue;

                        string stream = HostStreamProxy(file);
                        etpl.Append(
                            name ?? $"{episode.Value<int>("episode")} серия",
                            title ?? original_title,
                            s,
                            episode.Value<short>("episode"),
                            stream,
                            vast: init.vast
                        );
                    }

                    return etpl;
                }
                #endregion
            }
        });
    }

    #region search
    async ValueTask<JToken> search(string codeauth, int serial, string imdb_id, long kinopoisk_id, string title, string original_title)
    {
        string memKey = $"iptvonline:view:{kinopoisk_id}:{imdb_id}:{title}:{original_title}";

        if (!hybridCache.TryGetValue(memKey, out JToken data))
        {
            string stitle = SearchNameTo.Convert(title);
            string sorigtitle = SearchNameTo.Convert(original_title);

            async Task<JToken> goSearch(string search)
            {
                if (string.IsNullOrEmpty(search))
                    return null;

                string uri = $"{init.host}/v1/api/media/{(serial == 1 ? "serials" : "movies")}";
                var data = new System.Net.Http.StringContent(JsonConvert.SerializeObject(new { search }), Encoding.UTF8, "application/json");

                var video = await Http.Get<JObject>(uri, body: data, timeoutSeconds: init.httptimeout, proxy: proxy, useDefaultHeaders: false, headers: httpHeaders(init, HeadersModel.Init(
                    ("X-API-AUTH", codeauth),
                    ("X-API-ID", init.token.Split(":")[0])
                )));

                if (video == null)
                {
                    proxyManager?.Refresh();
                    return null;
                }

                if (video.ContainsKey("message") && video.Value<string>("message") == "No Subscribed By Media API")
                    return video;

                if (!video.ContainsKey("data"))
                    return null;

                foreach (var item in video["data"])
                {
                    if (kinopoisk_id > 0)
                    {
                        if (item.Value<long?>("kinopoisk") == kinopoisk_id)
                            return item;
                    }

                    if (!string.IsNullOrEmpty(imdb_id))
                    {
                        if ($"tt{item.Value<long?>("imdb")}" == imdb_id)
                            return item;
                    }
                }

                foreach (var item in video["data"])
                {
                    if (SearchNameTo.Equals(item.Value<string>("orig_title"), sorigtitle) ||
                        SearchNameTo.Equals(item.Value<string>("ru_title"), stitle))
                        return item;
                }

                return null;
            }

            data = await goSearch(original_title) ?? await goSearch(title);
            if (data == null)
                return null;

            proxyManager?.Success();
            hybridCache.Set(memKey, data, cacheTime(30));
        }

        return data;
    }
    #endregion
}
