using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinoPub;

public class KinoPubController : BaseOnlineController<ModuleConf>
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public KinoPubController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };

        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("filetype"))
                i.filetype = c.filetype;

            i.tokens = c.tokens;
            return i;
        };
    }

    #region kinopubpro
    [HttpGet]
    [AllowAnonymous]
    [Route("lite/kinopubpro")]
    async public Task<ActionResult> Pro(string code, string name)
    {
        if (!requestInfo.IsLocalIp)
            return ContentTo("is not local ip");

        var headers = httpHeaders(init);

        if (string.IsNullOrWhiteSpace(code))
        {
            string uri = $"{init.host}/oauth2/device?grant_type=device_code&client_id=xbmc&client_secret=cgg3gtifu46urtfp2zp1nqtba0k2ezxh";
            var token_request = await Http.Post<JObject>(uri, "", httpversion: init.httpversion, proxy: proxy, headers: headers);

            if (token_request == null)
                return ContentTo($"нет доступа к {init.host}");

            string html = "1. Откройте <a href='https://kino.pub/device'>https://kino.pub/device</a> <br>";
            html += $"2. Введите код активации <b>{token_request.Value<string>("user_code")}</b><br>";
            html += $"3. Когда на сайте kino.pub появится \"Ожидание устройства\", нажмите кнопку \"Проверить активацию\" которая ниже</b>";

            html += $"<br><br><a href='/lite/kinopubpro?code={token_request.Value<string>("code")}&name={name}'><button>Проверить активацию</button></a>";

            return ContentTo(html);
        }
        else
        {
            string uri = $"{init.host}/oauth2/device?grant_type=device_token&client_id=xbmc&client_secret=cgg3gtifu46urtfp2zp1nqtba0k2ezxh&code={code}";
            var device_token = await Http.Post<JObject>(uri, "", httpversion: init.httpversion, proxy: proxy, headers: headers);
            if (device_token == null || string.IsNullOrWhiteSpace(device_token.Value<string>("access_token")))
                return LocalRedirect("/lite/kinopubpro");

            if (!string.IsNullOrEmpty(name))
            {
                uri = $"{init.host}/v1/device/notify?access_token={device_token.Value<string>("access_token")}";
                await Http.Post(uri, $"&title={name}", httpversion: init.httpversion, proxy: proxy, headers: headers);
            }

            return ContentTo("Добавьте в init.conf<br><br>\"KinoPub\": {<br>&nbsp;&nbsp;\"enable\": true,<br>&nbsp;&nbsp;\"token\": \"" + device_token.Value<string>("access_token") + "\"<br>}");
        }
    }
    #endregion

    [HttpGet]
    [Route("lite/kinopub")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int clarification, int postid, int s = -1, int t = -1, string codec = null, bool rjson = false, bool similar = false, string source = null, string id = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (postid == 0 && !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(id))
        {
            if (source.Equals("kinopub", StringComparison.OrdinalIgnoreCase))
                int.TryParse(id, out postid);
        }

        string token = init.token;
        if (init.tokens != null && init.tokens.Length > 1)
            token = init.tokens[Random.Shared.Next(0, init.tokens.Length)];

        if (string.IsNullOrWhiteSpace(token))
            return OnError("token", statusCode: 401, gbcache: false);

        var oninvk = new KinoPubInvoke
        (
           host,
           init.host,
           token,
           httpHydra,
           (stream, filepath) => HostStreamProxy(stream)
        );

        if (postid == 0)
        {
            var search = await InvokeCacheResult($"kinopub:search:{title}:{year}:{clarification}:{imdb_id}:{kinopoisk_id}", 40,
                () => oninvk.Search(title, original_title, year, clarification, imdb_id, kinopoisk_id)
            );

            if (!search.IsSuccess)
                return OnError(search.ErrorMsg);

            if (similar || search.Value.id == 0)
            {
                if (search.Value.similars == null)
                    return OnError();

                return ContentTpl(search.Value.similars);
            }

            postid = search.Value.id;
        }

    rhubFallback:
        var cache = await InvokeCacheResult($"kinopub:post:{postid}:{token}", 10,
            () => oninvk.Post(postid),
            textJson: true
        );

        if (IsRhubFallback(cache, safety: true))
            goto rhubFallback;

        return ContentTpl(cache,
            () => oninvk.Tpl(cache.Value, init.filetype, title, original_title, postid, s, t, codec, vast: init.vast, rjson: rjson)
        );
    }


    [HttpGet]
    [Route("lite/kinopub/subtitles.json")]
    async public Task<ActionResult> Subtitles(int mid)
    {
        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        string token = init.token;
        if (init.tokens != null && init.tokens.Length > 1)
            token = init.tokens[Random.Shared.Next(0, init.tokens.Length)];

        if (string.IsNullOrWhiteSpace(token))
            return ContentTo("[]");

        string uri = $"{init.host}/v1/items/media-links?mid={mid}&access_token={token}";

        var root = await InvokeCache($"kinopub:media-links:{mid}:{token}", 20,
            () => httpHydra.Get<JObject>(uri, safety: true)
        );

        if (root == null || !root.ContainsKey("subtitles"))
        {
            proxyManager?.Refresh();
            return ContentTo("[]");
        }

        var subs = root["subtitles"] as JArray;

        if (subs == null || subs.Count == 0)
            return ContentTo("[]");

        var tpl = new SubtitleTpl(subs.Count);

        foreach (var s in subs.OfType<JObject>())
        {
            try
            {
                string url = s.Value<string>("url");

                if (!string.IsNullOrEmpty(url))
                    tpl.Append(s.Value<string>("lang"), HostStreamProxy(url));
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "KinoPub", "id_8wmz8qvm");
            }
        }

        return ContentTo(tpl.ToJson());
    }
}
