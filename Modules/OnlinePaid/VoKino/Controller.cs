using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Services;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace VoKino;

public class VoKino : BaseOnlineController<ModuleConf>
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public VoKino() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);
        };

        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("online"))
                i.online = c.online;

            return i;
        };
    }

    #region vokinotk
    [HttpGet]
    [AllowAnonymous]
    [Route("lite/vokinotk")]
    async public Task<ActionResult> Token(string login, string pass)
    {
        string html = string.Empty;

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
        {
            html = "Введите данные аккаунта <a href='https://vokino.pro'>vokino.pro</a> <br> <br><form method=\"get\" action=\"/lite/vokinotk\"><input type=\"text\" name=\"login\" placeholder=\"email\"> &nbsp; &nbsp; <input type=\"text\" name=\"pass\" placeholder=\"пароль\"><br><br><button>Добавить устройство</button></form> ";
        }
        else
        {
            string deviceid = new string(DateTime.Now.ToBinary().ToString().Reverse().ToArray()).Substring(0, 8);
            string uri = $"{init.host}/v2/auth?email={HttpUtility.UrlEncode(login)}&passwd={HttpUtility.UrlEncode(pass)}&deviceid={deviceid}";

            var head = HeadersModel.Init(
                ("user-agent", "lampac")
            );

            var token_request = await Http.Get<JObject>(uri, proxy: proxy, headers: head, timeoutSeconds: 8);

            if (token_request == null)
                return ContentTo($"нет доступа к {init.host}");

            string authToken = token_request.Value<string>("authToken");
            if (string.IsNullOrEmpty(authToken))
                return ContentTo(token_request.Value<string>("error") ?? "Не удалось получить токен");

            html = "Добавьте в init.conf<br><br>\"VoKino\": {<br>&nbsp;&nbsp;\"enable\": true,<br>&nbsp;&nbsp;\"token\": \"" + authToken + "\"<br>}";
        }

        return ContentTo(html);
    }
    #endregion

    [HttpGet]
    [AllowAnonymous]
    [Route("lite/vokino/stream")]
    async public Task<ActionResult> Stream(string url)
    {
        if (string.IsNullOrEmpty(url) || !init.HasToken())
            return OnError();

        string currentToken = init.GetToken();

        string uri = url;
        if (!uri.Contains("token=", StringComparison.OrdinalIgnoreCase))
            uri += (uri.Contains("?") ? "&" : "?") + "token=" + currentToken;

        var json = await Http.Get<JObject>(uri, proxy: proxy, timeoutSeconds: 8);
        if (json == null)
            return OnError();

        var streams = json["streams"] as JArray;
        if (streams == null || streams.Count == 0)
            return OnError();

        string best = null;
        foreach (var s in streams)
        {
            best = s.Value<string>("stream_url");
            if (!string.IsNullOrEmpty(best))
                break;
        }

        if (string.IsNullOrEmpty(best))
            return OnError();

        return Redirect(HostStreamProxy(best));
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vokino")]
    async public Task<ActionResult> Index(bool checksearch, string origid, long kinopoisk_id, string title, string original_title, string balancer, string t, short s = -1, bool rjson = false, string source = null, string id = null)
    {
        if (string.IsNullOrEmpty(origid) && !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(id))
        {
            if (source.Equals("vokino", StringComparison.OrdinalIgnoreCase))
                origid = id;
        }

        if (kinopoisk_id == 0 && string.IsNullOrEmpty(origid))
            return OnError();

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (!init.HasToken())
            return OnError("token", statusCode: 401, gbcache: false);

        // Динамическое определение streamproxy: проверяет init.conf, иначе берет безопасный дефолт
        init.streamproxy = init.IsStreamProxy(balancer);

        if (checksearch)
            return Content("data-json=");

        string currentToken = init.GetToken();

        var oninvk = new VoKinoInvoke
        (
           host,
           init.host,
           currentToken,
           httpHydra,
           streamfile => HostStreamProxy(streamfile)
        );

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"vokino:{kinopoisk_id}:{origid}:{balancer}:{t}"), 60,
            () => oninvk.Embed(origid, kinopoisk_id, balancer, t),
            textJson: true
        );

        if (IsRhubFallback(cache, safety: true))
            goto rhubFallback;

        return ContentTpl(cache,
            () => oninvk.Tpl(cache.Value, origid, kinopoisk_id, title, original_title, balancer, t, s, init.vast, rjson)
        );
    }
}
