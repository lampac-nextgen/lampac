using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Lostfilm;

/// <summary>
/// LostFilm.TV — торрент-источник с авторизацией.
///
/// Поток данных:
///   поиск (POST ajaxik.php) -> slug
///     -> /series/{slug}/seasons (HTML: сезоны + эпизоды + 9-значные episodeID)
///     -> play(episodeID) -> v_search.php?a={id} (сессия) -> /V/?... (HTML) -> td.php (.torrent)
///     -> TorrServer /torrent/upload -> hash -> TS /stream
///
/// Креды/домены берутся из секции "Lostfilm" init.conf. Секреты НИКОГДА не логируются.
/// </summary>
public class LostfilmController : BaseOnlineController<LostfilmSettings>
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<LostfilmController>();

    public LostfilmController() : base(ModInit.conf) { }

    #region Routes

    [HttpGet, Staticache(manually: true)]
    [Route("lite/lostfilm")]
    public async Task<ActionResult> Index(string title, string original_title, short year, byte serial, short s = -1, string href = null, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(original_title))
            return OnError("searchTitle");

        var init = ModInit.conf;
        if (init?.torrs == null || init.torrs.Length == 0)
        {
            if (init?.auth_torrs == null || init.auth_torrs.Count == 0)
                return OnError("torrs_empty");
        }

        // ── ПОИСК / РЕЗОЛВ SLUG ─────────────────────────────────────────────
        var searchCache = await InvokeCacheResult<SearchItem>(
            $"lostfilm:v1:search:{Norm(href ?? title ?? original_title)}:{year}:{serial}",
            TimeSpan.FromHours(4), async e =>
        {
            var item = await ResolveSearchItem(title, original_title, year, href, serial);
            if (item == null || string.IsNullOrEmpty(item.Link))
                return e.Fail("search");

            return e.Success(item);
        });

        if (IsRhubFallback(searchCache))
        {
            // единственный разумный fallback — повторить поиск заново
            searchCache = await InvokeCacheResult<SearchItem>(
                $"lostfilm:v1:search:{Norm(href ?? title ?? original_title)}:{year}:{serial}",
                TimeSpan.FromHours(4), async e =>
            {
                var item = await ResolveSearchItem(title, original_title, year, href, serial);
                if (item == null || string.IsNullOrEmpty(item.Link))
                    return e.Fail("search");

                return e.Success(item);
            });
        }

        if (!searchCache.IsSuccess || searchCache.Value == null)
            return OnError();

        var searchItem = searchCache.Value;

        bool isSerial = serial == 1 || !searchItem.IsMovie;

        // ── ФИЛЬМ ───────────────────────────────────────────────────────────
        if (!isSerial)
        {
            // У фильма тоже 9-значный episodeID (movies/{slug} -> data-episode="710001001").
            string movieEp = searchItem.EpisodeID;
            if (string.IsNullOrEmpty(movieEp))
            {
                var mhtml = await SiteGet($"{init.host}{searchItem.Link}", $"{init.host}/");
                movieEp = ParseFirstEpisodeID(mhtml);
            }

            if (string.IsNullOrEmpty(movieEp))
                return OnError("movie_episodeid");

            var mtpl = new MovieTpl(title ?? original_title, original_title);
            mtpl.Append(
                "LostFilm",
                accsArgs($"{host}/lite/lostfilm/play?episodeID={movieEp}&q=0"),
                voice_name: "1080p",
                quality: "1080",
                hls_manifest_timeout: (int)TimeSpan.FromSeconds(30).TotalMilliseconds
            );

            return ContentTpl(mtpl);
        }

        // ── СЕРИАЛ: СЕЗОНЫ/ЭПИЗОДЫ ──────────────────────────────────────────
        string slug = searchItem.Link?.TrimStart('/') ?? string.Empty;
        if (string.IsNullOrEmpty(slug))
            return OnError("slug");

        var seasonsCache = await InvokeCacheResult<SeasonsData>(
            $"lostfilm:v1:seasons:{slug}", TimeSpan.FromHours(4), async e =>
        {
            // ВАЖНО: /series/{slug} отдаёт ТОЛЬКО последний сезон.
            // Полный список сезонов/серий живёт на /series/{slug}/seasons.
            string seasonsUrl = $"{init.host}/{slug.TrimStart('/')}/seasons";

            string html = await SiteGet(seasonsUrl, $"{init.host}/");
            if (string.IsNullOrEmpty(html))
            {
                // fallback: страница без /seasons (одиночный сезон)
                html = await SiteGet($"{init.host}/{slug.TrimStart('/')}", $"{init.host}/");
            }

            if (string.IsNullOrEmpty(html))
                return e.Fail("seasons_fetch");

            var data = ParseSeasons(string.IsNullOrEmpty(html) ? null : html);
            if (data == null || data.Seasons.Count == 0)
                return e.Fail("seasons_parse");

            return e.Success(data);
        });

        if (!seasonsCache.IsSuccess || seasonsCache.Value == null)
            return OnError();

        var seasons = seasonsCache.Value;

        // Селектор сезонов
        if (s == -1 && seasons.Seasons.Count > 1)
        {
            var tpl = new SeasonTpl("1080p");

            string defArgs = $"&title={HttpUtility.UrlEncode(title ?? string.Empty)}" +
                $"&original_title={HttpUtility.UrlEncode(original_title ?? string.Empty)}&serial=1";

            foreach (var sn in seasons.Seasons.OrderBy(x => x.Number))
                tpl.Append($"{sn.Number} сезон", $"{host}/lite/lostfilm?s={sn.Number}{defArgs}", sn.Number);

            return ContentTpl(tpl);
        }

        int seasonSel = s == -1 ? seasons.Seasons.OrderBy(x => x.Number).First().Number : s;
        var season = seasons.Seasons.FirstOrDefault(x => x.Number == seasonSel);
        if (season == null || season.Episodes.Count == 0)
            return OnError("episodes");

        var etpl = new EpisodeTpl(season.Episodes.Count);

        foreach (var ep in season.Episodes.OrderBy(x => x.Episode))
        {
            // Для полного сезона (e999) отдаём index=1 как MVP; точная серия через tsid.
            etpl.Append(
                $"{ep.Episode} серия",
                title ?? original_title,
                (short)ep.Season,
                (short)ep.Episode,
                accsArgs($"{host}/lite/lostfilm/play?episodeID={ep.EpisodeID}"),
                voice_name: ep.Title,
                hls_manifest_timeout: (int)TimeSpan.FromSeconds(30).TotalMilliseconds
            );
        }

        return ContentTpl(etpl);
    }


    [HttpGet]
    [Route("lite/lostfilm/play")]
    public async Task<ActionResult> Play(string episodeID, int q = 0, int fileIndex = -1)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(episodeID) || !Regex.IsMatch(episodeID, "^[0-9]{6,12}$"))
            return OnError("episodeID");

        var init = ModInit.conf;

        var cache = await InvokeCacheResult<string>(
            $"lostfilm:v1:torr:{episodeID}:q{q}", TimeSpan.FromMinutes(30), async e =>
        {
            string redirect = await ResolveStream(episodeID, q, fileIndex);
            if (string.IsNullOrEmpty(redirect))
                return e.Fail("torrent_fetch");

            return e.Success(redirect);
        });

        if (!cache.IsSuccess || string.IsNullOrEmpty(cache.Value))
            return OnError(cache.ErrorMsg ?? "torrent_fetch");

        return Redirect(cache.Value);
    }


    [HttpGet]
    [Route("lite/lostfilm/captcha")]
    public async Task<ActionResult> Captcha(long r = 0, string phpssid = null)
    {
        var init = ModInit.conf;

        string cookie = null;
        if (!string.IsNullOrEmpty(phpssid))
            cookie = $"PHPSESSID={phpssid}";

        byte[] img = await SiteDownload($"{init.host}/simple_captcha.php?{r}", cookie, $"{init.host}/");
        if (img == null || img.Length == 0)
            return StatusCode(502);

        HttpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        HttpContext.Response.Headers["Pragma"] = "no-cache";
        return File(img, "image/gif");
    }


    [HttpGet, HttpPost]
    [Route("/bind/lostfilm")]
    public async Task<ActionResult> Bind(string mail, string pass, string captcha, string phpssid, string r)
    {
        var init = ModInit.conf;

        // GET без параметров → форма
        if (!HttpContext.Request.HasFormContentType && string.IsNullOrEmpty(mail) && string.IsNullOrEmpty(pass))
            return BindForm(null, r);

        if (string.IsNullOrWhiteSpace(mail) || string.IsNullOrWhiteSpace(pass))
            return BindForm("Введите e-mail и пароль", r);

        if (string.IsNullOrWhiteSpace(captcha))
            return BindForm("Введите код с картинки", r);

        string cookie = string.IsNullOrEmpty(phpssid) ? null : $"PHPSESSID={phpssid}";

        // Пароль уходит ТОЛЬКО в POST и НИКОГДА не логируется.
        string postdata =
            "act=users&type=login" +
            $"&mail={HttpUtility.UrlEncode(mail)}" +
            $"&pass={HttpUtility.UrlEncode(pass)}" +
            "&rem=1" +
            $"&captcha={HttpUtility.UrlEncode(captcha)}" +
            "&need_captcha=1";

        var (body, response) = await SitePostBase($"{init.host}/ajaxik.users.php", postdata, cookie, $"{init.host}/");

        if (body == null && response == null)
            return BindForm("Сайт недоступен, повторите попытку позже", r);

        string setCookie = ReadSetCookie(response);
        string session = ExtractCookieValue(setCookie, "lf_session");

        if (string.IsNullOrEmpty(session))
        {
            // error 4 = неверная капча, всё остальное — неверные креды
            bool captchaError = body != null && body.Contains("\"error\":4");

            if (captchaError)
                return BindForm("Неверный код с картинки", r, mail: mail);

            // need_captcha:true / error → повторная форма
            if (body != null && body.Contains("need_captcha"))
                return BindForm("Неверный код с картинки", r, mail: mail);

            return BindForm("Не удалось войти: проверьте логин/пароль", r, mail: mail);
        }

        // УСПЕХ: сохраняем сессию в memoryCache (до конца суток), НЕ в FDB.
        string fullCookie = BuildSessionCookie(setCookie, session);
        memoryCache.Set(LostfilmAuth.CookieKey, fullCookie, DateTime.Today.AddDays(1));

        Log.Information("lostfilm bind success: session updated");
        return BindForm(null, r, mail: mail, success: true);
    }

    #endregion


    #region ClientApi — JSON API клиентского плагина (R1-R7)

    /// <summary>R1. Статус авторизации. Куку НЕ возвращает — только uid из lf_session.</summary>
    [HttpGet]
    [Route("lite/lostfilm/status")]
    public async Task<ActionResult> Status()
    {
        var init = ModInit.conf;

        string cookie = await LostfilmAuth.GetCookie(this, init);
        if (string.IsNullOrEmpty(cookie))
            return JsonApi(new StatusResult { auth = false, cookie_valid = false });

        string sid = ExtractCookieValue(cookie, "lf_session");
        string uid = string.IsNullOrEmpty(sid) ? null : sid.Split('.')[0];

        // v2.2b: передача ВЛАДЕНИЯ сессией клиентскому плагину. Поле session = ПОЛНЫЙ
        // lf_session (32hex.цифры) и отдаётся ТОЛЬКО при auth:true — плагин кладёт его
        // в Lampa.Storage и шлёт обратно заголовком X-Lf-Session (см. LostfilmAuth.GetCookie).
        // Значение наружу через uid не выводится; в лог не пишется.
        bool sessionShape = !string.IsNullOrEmpty(sid) &&
            Regex.IsMatch(sid, "^[0-9a-f]{32}\\.[0-9]{1,10}$");

        return JsonApi(new StatusResult
        {
            auth = true,
            user = uid,
            cookie_valid = await SessionAlive(cookie),
            session = sessionShape ? sid : null
        });
    }


    /// <summary>R2. Прокси getMarks(serialId). Отдаёт ответ сайта как есть ({"data":[...]}).</summary>
    [HttpGet]
    [Route("lite/lostfilm/marks/{id}")]
    public async Task<ActionResult> Marks(string id)
    {
        var init = ModInit.conf;

        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[0-9]{1,6}$"))
            return OnError("id");

        string cookie = await LostfilmAuth.GetCookie(this, init);
        if (string.IsNullOrEmpty(cookie))
            return OnError("not_auth");

        // Проверено live: getMarks авторизуется ТОЛЬКО cookie, session в body не нужен.
        var json = await AjaxikJson($"act=serial&type=getmarks&id={id}", $"{init.host}/", cookie);
        if (json == null)
            return OnError("marks");

        return JsonApi(json);
    }


    /// <summary>
    /// R3. Отметка просмотренного. markseason если episodeID заканчивается на "999" (episodeID % 1000 == 999),
    /// иначе markepisode. Без кеша; параллельные посты одной серии не шлём.
    /// </summary>
    [HttpGet]
    [Route("lite/lostfilm/watched")]
    public async Task<ActionResult> Watched(string episodeID, bool mode)
    {
        var init = ModInit.conf;

        if (string.IsNullOrEmpty(episodeID) || !Regex.IsMatch(episodeID, "^[0-9]{6,12}$"))
            return OnError("episodeID");

        string cookie = await LostfilmAuth.GetCookie(this, init);
        if (string.IsNullOrEmpty(cookie))
            return OnError("not_auth");

        return await InvkSemaphore($"lostfilm:watched:{episodeID}:{mode}", async () =>
        {
            string sid = await GetSessionId(cookie);
            if (string.IsNullOrEmpty(sid))
                return OnError("session");

            // Полный сезон — episodeID % 1000 == 999 → markseason, иначе markepisode.
            string type = (long.Parse(episodeID) % 1000 == 999) ? "markseason" : "markepisode";

            var json = await AjaxikJson(
                $"session={sid}&act=serial&type={type}&val={episodeID}&auto=0&mode={(mode ? "on" : "off")}",
                $"{init.host}/my/order_1/type_1", cookie);

            if (json == null)
                return OnError("watched");

            string error = json.Value<string>("error");
            if (!string.IsNullOrEmpty(error))
            {
                DropSessionIdOnExpired(error);
                Log.Warning("lostfilm watched: сайт вернул error={Error}", error);
                return JsonApi(new { ok = false, error });
            }

            return JsonApi(new { ok = true, result = json.Value<string>("result") });
        });
    }


    /// <summary>R4. Добавить/убрать сериал в избранное (follow — тумблер на стороне сайта).</summary>
    [HttpGet]
    [Route("lite/lostfilm/follow/{id}")]
    public async Task<ActionResult> Follow(string id)
    {
        var init = ModInit.conf;

        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[0-9]{1,6}$"))
            return OnError("id");

        string cookie = await LostfilmAuth.GetCookie(this, init);
        if (string.IsNullOrEmpty(cookie))
            return OnError("not_auth");

        return await InvkSemaphore($"lostfilm:follow:{id}", async () =>
        {
            string sid = await GetSessionId(cookie);
            if (string.IsNullOrEmpty(sid))
                return OnError("session");

            var json = await AjaxikJson($"session={sid}&act=serial&type=follow&id={id}", $"{init.host}/", cookie);
            if (json == null)
                return OnError("follow");

            string error = json.Value<string>("error");
            if (!string.IsNullOrEmpty(error))
            {
                DropSessionIdOnExpired(error);
                Log.Warning("lostfilm follow: сайт вернул error={Error}", error);
                return JsonApi(new { ok = false, error });
            }

            return JsonApi(new { ok = true, result = json.Value<string>("result") });
        });
    }


    /// <summary>R5. Сериалы пользователя (/my/order_1/type_1 → карточки div.serial-box).</summary>
    [HttpGet]
    [Route("lite/lostfilm/my")]
    public async Task<ActionResult> My()
    {
        var init = ModInit.conf;

        string cookie = await LostfilmAuth.GetCookie(this, init);
        if (string.IsNullOrEmpty(cookie))
            return OnError("not_auth");

        var cache = await InvokeCacheResult<List<MySerialItem>>(
            "lostfilm:v1:my", TimeSpan.FromMinutes(5), async e =>
        {
            string html = await SiteGet($"{init.host}/my/order_1/type_1", $"{init.host}/", cookie);
            if (string.IsNullOrEmpty(html))
                return e.Fail("my_fetch");

            // Страница /my/ несёт session-токен — обновляем кеш sessionid из того же ответа.
            string sid = ExtractSessionId(html);
            if (!string.IsNullOrEmpty(sid))
                memoryCache.Set(SessionIdKey, sid, TimeSpan.FromMinutes(10));

            var data = ParseMySerials(html);
            if (data == null || data.Count == 0)
                return e.Fail("my_parse");

            return e.Success(data);
        });

        if (!cache.IsSuccess || cache.Value == null)
            return OnError(cache.ErrorMsg ?? "my");

        return JsonApi(new { data = cache.Value });
    }


    /// <summary>
    /// R6. Новинки серий (блок div.row на /new/). Пагинация /new/page_{page}/, clamp 1..100.
    /// Публичный контент — кеш 15 мин на страницу (кеш-ключ "lostfilm:v1:new:p{page}").
    /// </summary>
    [HttpGet]
    [Route("lite/lostfilm/new")]
    public async Task<ActionResult> New(int limit = 20, int page = 1)
    {
        var init = ModInit.conf;

        if (limit < 1) limit = 1;
        if (limit > 60) limit = 60;

        if (page < 1) page = 1;
        if (page > 100) page = 100;

        string cookie = await LostfilmAuth.GetCookie(this, init);

        var cache = await InvokeCacheResult<List<NewEpisodeItem>>(
            $"lostfilm:v1:new:p{page}", TimeSpan.FromMinutes(15), async e =>
        {
            string url = $"{init.host}/new/" + (page > 1 ? $"page_{page}/" : "");
            string html = await SiteGet(url, $"{init.host}/", cookie);
            var data = ParseNewEpisodes(html);

            if (data == null || data.Count == 0)
                return e.Fail("new_parse");

            return e.Success(data);
        });

        if (!cache.IsSuccess || cache.Value == null)
            return OnError(cache.ErrorMsg ?? "new");

        return JsonApi(new { data = cache.Value.Take(limit), page = page });
    }


    /// <summary>R7. Расписание (/schedule/my_0/type_0 — список с группами дат). Публичный контент — кеш 1 ч.</summary>
    [HttpGet]
    [Route("lite/lostfilm/schedule")]
    public async Task<ActionResult> Schedule()
    {
        var init = ModInit.conf;

        string cookie = await LostfilmAuth.GetCookie(this, init);

        var cache = await InvokeCacheResult<List<ScheduleGroup>>(
            "lostfilm:v1:schedule", TimeSpan.FromHours(1), async e =>
        {
            string html = await SiteGet($"{init.host}/schedule/my_0/type_0", $"{init.host}/", cookie);
            var data = ParseSchedule(html);

            if (data == null || data.Count == 0)
                return e.Fail("schedule_parse");

            return e.Success(data);
        });

        if (!cache.IsSuccess || cache.Value == null)
            return OnError(cache.ErrorMsg ?? "schedule");

        return JsonApi(new { data = cache.Value });
    }

    /// <summary>
    /// R8. Полный каталог (ajaxik act=serial|movies, type=search, страницы по 20).
    /// Публичный контент — кеш 12 ч. Сессия/кука НЕ логируются.
    /// </summary>
    [HttpGet]
    [Route("lite/lostfilm/catalog")]
    public async Task<ActionResult> Catalog(int limit = 2000, string act = "serial")
    {
        var init = ModInit.conf;

        act = (act ?? string.Empty).Trim().ToLowerInvariant();
        if (act != "serial" && act != "movies")
            return OnError("act");

        if (limit < 1) limit = 1;
        if (limit > 3000) limit = 3000;

        string cookie = await LostfilmAuth.GetCookie(this, init);

        var cache = await InvokeCacheResult<List<JObject>>(
            $"lostfilm:v1:catalog:{act}", TimeSpan.FromHours(12), async e =>
        {
            var items = new List<JObject>();

            // Страницы по 20. Стоп: пустой data | o >= 2000 (hard-cap) | аварийный лимит 50 запросов.
            for (int o = 0, req = 0; ; o += 20, req++)
            {
                if (o >= 2000 || req >= 50)
                    break;

                var json = await AjaxikJson($"act={act}&type=search&o={o}&s=20", $"{init.host}/", cookie);
                var data = json?["data"] as JArray;

                if (data == null || data.Count == 0)
                    break;

                foreach (var it in data)
                {
                    if (it is JObject jo)
                        items.Add(jo);
                }
            }

            if (items.Count == 0)
                return e.Fail("catalog");

            return e.Success(items);
        });

        if (!cache.IsSuccess || cache.Value == null)
            return OnError(cache.ErrorMsg ?? "catalog");

        return JsonApi(new { data = cache.Value.Take(limit), total = cache.Value.Count });
    }

    #endregion


    #region ClientApi helpers

    const string SessionIdKey = "lostfilm:sessionid";

    /// <summary>POST /ajaxik.php (x-requested-with + referer). Кука/сессия НЕ логируются.</summary>
    async Task<string> Ajaxik(string data, string referer, string cookie)
    {
        var init = ModInit.conf;
        return await SitePost($"{init.host}/ajaxik.php", data, referer ?? $"{init.host}/", cookie);
    }

    /// <summary>POST /ajaxik.php с разбором JSON. "[...]" приводим к {"data":[...]}.</summary>
    async Task<JObject> AjaxikJson(string data, string referer, string cookie)
    {
        string body = await Ajaxik(data, referer, cookie);
        if (string.IsNullOrEmpty(body))
            return null;

        try
        {
            string trimmed = body.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
                return new JObject { ["data"] = JArray.Parse(trimmed) };

            return JObject.Parse(trimmed);
        }
        catch (Exception ex)
        {
            // Тело ответа не логируем — там могут быть пользовательские данные.
            Log.Error(ex, "CatchId={CatchId} len={Len}", "id_lf_ajaxik_json", body.Length);
            return null;
        }
    }

    /// <summary>
    /// sessionId (38-39 hex) со страницы /my/... Кеш 10 мин ("lostfilm:sessionid").
    /// Разбор как в APK: скрипт с "let userdata" → первый [a-f0-9]{38,39}.
    /// </summary>
    async Task<string> GetSessionId(string cookie)
    {
        if (memoryCache.TryGetValue(SessionIdKey, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var init = ModInit.conf;
        string html = await SiteGet($"{init.host}/my/order_1/type_1", $"{init.host}/", cookie);
        if (string.IsNullOrEmpty(html))
            return null;

        string sid = ExtractSessionId(html);
        if (string.IsNullOrEmpty(sid))
            return null;

        memoryCache.Set(SessionIdKey, sid, TimeSpan.FromMinutes(10));
        return sid;
    }

    static string ExtractSessionId(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, "UserData\\.session\\s*=\\s*'([a-f0-9]{38,39})'", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value;

        foreach (Match sm in Regex.Matches(html, "<script[^>]*>([\\s\\S]*?)</script>", RegexOptions.IgnoreCase))
        {
            string script = sm.Groups[1].Value.ToLowerInvariant();
            if (script.IndexOf("let userdata", StringComparison.Ordinal) < 0)
                continue;

            var hm = Regex.Match(script, "[a-f0-9]{38,39}");
            if (hm.Success)
                return hm.Value;
        }

        return null;
    }

    /// <summary>При error=session сбрасываем кеш sessionid — следующий запрос возьмёт свежий.</summary>
    void DropSessionIdOnExpired(string error)
    {
        if (!string.IsNullOrEmpty(error) && error.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
            memoryCache.Remove(SessionIdKey);
    }

    /// <summary>
    /// Проверка cookie: GET /my/... не должен редиректить на /login.
    /// Гость получает 200 + заглушку 602 байта с location.replace("/").
    /// </summary>
    async Task<bool> SessionAlive(string cookie)
    {
        var init = ModInit.conf;

        try
        {
            var (body, response) = await Http.BaseGet(
                $"{init.host}/my/order_1/type_1",
                cookie: cookie,
                referer: $"{init.host}/",
                timeoutSeconds: 20,
                headers: SiteHeaders($"{init.host}/", cookie),
                useDefaultHeaders: false,
                statusCodeOK: false
            );

            if (response != null)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    return false;

                string location = response.Headers.Location?.ToString();
                if (!string.IsNullOrEmpty(location) && location.Contains("/login"))
                    return false;
            }

            if (string.IsNullOrEmpty(body))
                return false;

            if (body.Length < 2000 && body.Contains("location.replace(\"/\")"))
                return false;

            return body.IndexOf("UserData.id", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_alive");
            return false;
        }
    }

    /// <summary>JSON-ответ служебного API (контент онлайн-плагина отдаётся отдельно через ContentTpl).</summary>
    ActionResult JsonApi(object payload)
    {
        HttpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        return Content(Newtonsoft.Json.JsonConvert.SerializeObject(payload), "application/json; charset=utf-8");
    }

    /// <summary>Карточки /my/order_1/type_1: div.serial-box → avatar / subscribe-box / a.body / rate-box.</summary>
    List<MySerialItem> ParseMySerials(string html)
    {
        var list = new List<MySerialItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var blocks = Regex.Split(html, "<div class=\"serial-box\">", RegexOptions.IgnoreCase);

        for (int i = 1; i < blocks.Length; i++)
        {
            string b = blocks[i];

            var link = Regex.Match(b, "<a class=\"body\" href=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!link.Success)
                continue;

            var item = new MySerialItem
            {
                link = HttpUtility.HtmlDecode(link.Groups[1].Value),
                title = HtmlText(b, "<div class=\"title-ru\">([^<]*)</div>"),
                title_en = HtmlText(b, "<div class=\"title-en\">([^<]*)</div>"),
                poster = PosterUrl(Regex.Match(b, "<img src=\"([^\"]+)\" class=\"avatar\"", RegexOptions.IgnoreCase)),
                follow = b.IndexOf("subscribe-box active", StringComparison.OrdinalIgnoreCase) >= 0
            };

            // serialId: /Static/Images/{id}/Posters/... или FollowSerial({id})
            var idm = Regex.Match(b, "/Images/([0-9]+)/Posters/", RegexOptions.IgnoreCase);
            if (!idm.Success)
                idm = Regex.Match(b, "FollowSerial\\(([0-9]+)", RegexOptions.IgnoreCase);

            if (idm.Success)
                item.id = idm.Groups[1].Value;

            var watched = Regex.Match(b, "bar-active\" style=\"width: [0-9.]+%\">\\s*<div class=\"value\">([0-9]+)</div>", RegexOptions.IgnoreCase);
            if (watched.Success && int.TryParse(watched.Groups[1].Value, out int wv))
                item.watched = wv;

            var total = Regex.Match(b, "<div class=\"bar\">\\s*<div class=\"value\">([0-9]+)</div>", RegexOptions.IgnoreCase);
            if (total.Success && int.TryParse(total.Groups[1].Value, out int tv))
                item.episodes = tv;

            if (string.IsNullOrEmpty(item.id) || string.IsNullOrEmpty(item.title))
                continue;

            list.Add(item);
        }

        return list;
    }

    /// <summary>Новинки с /new/: div.row → a[href] / overlay / name-ru / details-pane / data-episode.</summary>
    List<NewEpisodeItem> ParseNewEpisodes(string html)
    {
        var list = new List<NewEpisodeItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var blocks = Regex.Split(html, "<div class=\"row\">", RegexOptions.IgnoreCase);

        for (int i = 1; i < blocks.Length; i++)
        {
            string b = blocks[i];

            var link = Regex.Match(b, "<a href=\"([^\"]+)\" style=\"text-decoration:none", RegexOptions.IgnoreCase);
            if (!link.Success)
                continue;

            var item = new NewEpisodeItem
            {
                link = HttpUtility.HtmlDecode(link.Groups[1].Value),
                title = HtmlText(b, "<div class=\"name-ru\">([^<]*)</div>"),
                title_en = HtmlText(b, "<div class=\"name-en\">([^<]*)</div>"),
                desc = HtmlText(b, "<div class=\"alpha\">([^<]*)</div>"),
                desc_en = HtmlText(b, "<div class=\"beta\">([^<]*)</div>"),
                poster = PosterUrl(Regex.Match(b, "<img src=\"([^\"]+)\" class=\"thumb\"", RegexOptions.IgnoreCase))
            };

            // overlay: "1 сезон 2 серия" + дата выхода Ru (или "Фильм")
            var overlay = Regex.Match(b,
                "<div class=\"overlay\">\\s*<div class=\"left-part\">([^<]*)</div>\\s*<div class=\"right-part\">([^<]*)</div>",
                RegexOptions.IgnoreCase);

            if (overlay.Success)
            {
                string num = HttpUtility.HtmlDecode(overlay.Groups[1].Value);
                item.date = overlay.Groups[2].Value.Trim();
                item.movie = num.IndexOf("Фильм", StringComparison.OrdinalIgnoreCase) >= 0;

                var sn = Regex.Match(num, "([0-9]+)\\s*сезон");
                if (sn.Success && int.TryParse(sn.Groups[1].Value, out int sv))
                    item.season = sv;

                var en = Regex.Match(num, "([0-9]+)\\s*серия");
                if (en.Success && int.TryParse(en.Groups[1].Value, out int ev))
                    item.episode = ev;
            }

            var epi = Regex.Match(b, "data-episode=\"([0-9]{6,12})\"");
            if (epi.Success)
                item.episodeId = epi.Groups[1].Value;

            var code = Regex.Match(b, "data-code=\"([0-9]+-[0-9]+-[0-9]+)\"");
            if (code.Success)
                item.code = code.Groups[1].Value;

            var rating = Regex.Match(b, "class=\"mark-green-box\"[^>]*>\\s*([0-9.]+)", RegexOptions.IgnoreCase);
            if (rating.Success)
                item.rating = rating.Groups[1].Value;

            var comments = Regex.Match(b, "class=\"comment-blue-box\"[\\s\\S]*?class=\"tail\"></div>\\s*([0-9]+)", RegexOptions.IgnoreCase);
            if (comments.Success && int.TryParse(comments.Groups[1].Value, out int cv))
                item.comments = cv;

            if (string.IsNullOrEmpty(item.title) || string.IsNullOrEmpty(item.link))
                continue;

            list.Add(item);
        }

        return list;
    }

    /// <summary>Расписание: table.schedule-list, th = группа ("сегодня"/"завтра"/...), tr = серия.</summary>
    List<ScheduleGroup> ParseSchedule(string html)
    {
        var groups = new List<ScheduleGroup>();
        if (string.IsNullOrEmpty(html))
            return groups;

        int start = html.IndexOf("class=\"schedule-list\"", StringComparison.OrdinalIgnoreCase);
        string table = start >= 0 ? html.Substring(start) : html;

        var map = new Dictionary<string, ScheduleGroup>();
        ScheduleGroup current = null;

        foreach (Match tr in Regex.Matches(table, "<tr>([\\s\\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            string row = tr.Groups[1].Value;

            var th = Regex.Match(row, "<th[^>]*>([^<]*)</th>", RegexOptions.IgnoreCase);
            if (th.Success)
            {
                string gname = Regex.Replace(HttpUtility.HtmlDecode(th.Groups[1].Value), "\\s+", " ").Trim();
                if (gname.Length == 0)
                    continue;

                if (!map.TryGetValue(gname, out current))
                {
                    current = new ScheduleGroup { group = gname };
                    map[gname] = current;
                    groups.Add(current);
                }

                continue;
            }

            if (current == null || row.IndexOf("class=\"alpha\"", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var seriesLink = Regex.Match(row, "class=\"alpha\"[^>]*onclick=\"goTo\\('([^']+)'", RegexOptions.IgnoreCase);
            if (!seriesLink.Success)
                continue;

            var epLink = Regex.Match(row, "class=\"beta\"[^>]*onclick=\"goTo\\('([^']+)'", RegexOptions.IgnoreCase);
            var ru = Regex.Match(row, "<div class=\"ru\">([^<]*)</div>", RegexOptions.IgnoreCase);
            var en = Regex.Match(row, "<div class=\"en small-text\">([^<]*)</div>", RegexOptions.IgnoreCase);
            var num = Regex.Match(row, "<div class=\"serie-number-box\">\\s*(?:<div class=\"count\">)?([^<]+)", RegexOptions.IgnoreCase);
            var gamma = Regex.Match(row, "class=\"gamma\"[^>]*>([\\s\\S]*?)</td>", RegexOptions.IgnoreCase);
            var delta = Regex.Match(row, "class=\"delta\"[^>]*>([\\s\\S]*?)</td>", RegexOptions.IgnoreCase);

            string seriesHref = HttpUtility.HtmlDecode(seriesLink.Groups[1].Value);
            string numText = num.Success ? Regex.Replace(HttpUtility.HtmlDecode(num.Groups[1].Value), "\\s+", " ").Trim() : null;

            var item = new ScheduleItem
            {
                link = seriesHref,
                title = ru.Success ? Regex.Replace(HttpUtility.HtmlDecode(ru.Groups[1].Value), "\\s+", " ").Trim() : null,
                title_en = en.Success ? Regex.Replace(HttpUtility.HtmlDecode(en.Groups[1].Value), "\\s+", " ").Trim() : null,
                episodeLink = epLink.Success ? HttpUtility.HtmlDecode(epLink.Groups[1].Value) : null,
                poster = PosterUrl(Regex.Match(row, "<img src=\"([^\"]+)\"", RegexOptions.IgnoreCase)),
                movie = seriesHref.IndexOf("/movies/", StringComparison.OrdinalIgnoreCase) >= 0
                    || (numText != null && numText.IndexOf("Фильм", StringComparison.OrdinalIgnoreCase) >= 0),
                desc = gamma.Success ? Regex.Replace(HttpUtility.HtmlDecode(Regex.Replace(gamma.Groups[1].Value, "<[^>]+>", " ")), "\\s+", " ").Trim() : null
            };

            if (numText != null)
            {
                var sn = Regex.Match(numText, "([0-9]+)\\s*сезон");
                if (sn.Success && int.TryParse(sn.Groups[1].Value, out int sv))
                    item.season = sv;

                var ev = Regex.Match(numText, "([0-9]+)\\s*серия");
                if (ev.Success && int.TryParse(ev.Groups[1].Value, out int e2))
                    item.episode = e2;
            }

            if (delta.Success)
            {
                var date = Regex.Match(delta.Groups[1].Value, "([0-9]{2}\\.[0-9]{2}\\.[0-9]{4})");
                if (date.Success)
                    item.date = date.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(item.title))
                continue;

            current.items.Add(item);
        }

        return groups;
    }

    static string HtmlText(string block, string pattern)
    {
        var m = Regex.Match(block, pattern, RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        return Regex.Replace(HttpUtility.HtmlDecode(m.Groups[1].Value), "\\s+", " ").Trim();
    }

    /// <summary>
    /// /Static/Images/127/Posters/icon.jpg → {imgHost}/Images/127/Posters/icon.jpg.
    /// imgHost отдаёт статику без префикса /Static; если imgHost пуст — берём с основного хоста.
    /// </summary>
    string PosterUrl(Match m)
    {
        if (!m.Success)
            return null;

        string p = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
        if (p.Length == 0)
            return null;

        if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return p;

        var init = ModInit.conf;

        if (!string.IsNullOrEmpty(init.imgHost) && p.StartsWith("/Static/", StringComparison.OrdinalIgnoreCase))
            return init.imgHost.TrimEnd('/') + p.Substring("/Static".Length);

        return init.host.TrimEnd('/') + (p.StartsWith("/") ? p : "/" + p);
    }

    #endregion




    #region ResolveStream

    /// <summary>
    /// Полный резолв эпизода в прямой TS-стрим. Возвращает redirect-URL или null.
    /// </summary>
    async Task<string> ResolveStream(string episodeID, int q, int fileIndex)
    {
        var init = ModInit.conf;

        // Анти-stampede: одновременные запросы одного эпизода ждут один резолв.
        SemaphorManager semaphore = null;
        try
        {
            semaphore = new SemaphorManager($"lostfilm:resolve:{episodeID}:q{q}", TimeSpan.FromSeconds(60));
            if (!await semaphore.WaitAsync())
                return null;

            // (а) cookie из AuthEngine
            string cookie = await LostfilmAuth.GetCookie(this, init);
            if (string.IsNullOrEmpty(cookie))
            {
                Log.Warning("lostfilm: cookie отсутствует — требуется /bind/lostfilm");
                return null;
            }

            // (б) v_search.php?a={id} — при 302 на /login сессия мертва
            var (vsearchHtml, expired) = await VSearch(episodeID, cookie);

            if (expired)
            {
                Log.Information("lostfilm: session expired, refreshing cookie");

                memoryCache.Remove(LostfilmAuth.CookieKey);
                cookie = await LostfilmAuth.RefreshCookie(this, init);

                if (string.IsNullOrEmpty(cookie))
                    return null;

                (vsearchHtml, expired) = await VSearch(episodeID, cookie);
                if (expired || string.IsNullOrEmpty(vsearchHtml))
                {
                    Log.Warning("lostfilm: cookie невалиден даже после refresh");
                    return null;
                }
            }

            if (string.IsNullOrEmpty(vsearchHtml))
                return null;

            // (в) /V/?... ссылка из v_search
            string vlink = Regex.Match(vsearchHtml, "(\"/V/\\?[^\"]+)|(/V/\\?[^\"'\\s]+)").Groups[1].Value;
            if (string.IsNullOrEmpty(vlink))
                vlink = Regex.Match(vsearchHtml, "(/V/\\?[^\"'\\s]+)").Groups[1].Value;

            if (string.IsNullOrEmpty(vlink))
            {
                Log.Error("lostfilm: /V/ link not found for {episodeID}", episodeID);
                return null;
            }

            // (г) страница торрентов
            string vhtml = await SiteGet($"{init.host}{vlink}", $"{init.host}/v_search.php?a={episodeID}", cookie);
            if (string.IsNullOrEmpty(vhtml))
                return null;

            var torrents = ParseTorrents(vhtml);
            if (torrents.Count == 0)
            {
                Log.Error("lostfilm: no torrents parsed for {episodeID}", episodeID);
                return null;
            }

            // (д) выбор качества: 0 → 1080 > MP4(720) > SD
            var chosen = PickQuality(torrents, q);
            if (chosen == null)
                return null;

            // (е) скачиваем .torrent (td.php самодостаточен, куки не нужны)
            byte[] torrent = await SiteDownload(chosen.TorrentUrl, null, $"{init.host}/");
            if (torrent == null || torrent.Length == 0 || !IsBencode(torrent))
            {
                Log.Error("lostfilm: torrent fetch failed or not bencode {episodeID}", episodeID);
                return null;
            }

            // (ж) TorrServer: upload multipart -> hash -> redirect
            string tsHost = PickTorrServer(init);
            if (string.IsNullOrEmpty(tsHost))
                return null;

            string hash = await TorrUpload(tsHost, torrent, init);
            if (string.IsNullOrEmpty(hash))
                return null;

            // index = позиция файла внутри раздачи.
            // episodeID = SSS+SSS+EEE => из него известны сезон и серия.
            // Сезон-пак содержит по файлу на серию, поэтому index=1 для E02+ дал бы чужую серию.
            int index = 1;

            if (fileIndex > 0)
            {
                index = fileIndex;
            }
            else if (episodeID.Length == 9 && !episodeID.EndsWith("999"))
            {
                int season = int.Parse(episodeID.Substring(3, 3));
                int episode = int.Parse(episodeID.Substring(6, 3));

                int resolved = await ResolveFileIndex(tsHost, hash, season, episode, init);
                if (resolved > 0)
                    index = resolved;
            }

            return $"{tsHost}/stream?link={hash}&index={index}&play";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_resolve");
            return null;
        }
        finally
        {
            semaphore?.Release();
        }
    }
    #endregion


    #region Site HTTP (UA153 везде)

    IReadOnlyList<HeadersModel> SiteHeaders(string referer, string cookie = null)
    {
        var init = ModInit.conf;

        return HeadersModel.Init(
            ("user-agent", string.IsNullOrEmpty(init?.ua) ? LostfilmSettings.UA153 : init.ua),
            ("referer", referer ?? $"{init?.host}/"),
            ("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"),
            ("accept-language", "ru,en;q=0.9")
        );
    }

    async Task<string> SiteGet(string url, string referer, string cookie = null)
    {
        try
        {
            return await Http.Get(url, cookie: cookie, referer: referer, timeoutSeconds: 20,
                headers: SiteHeaders(referer, cookie), useDefaultHeaders: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_get");
            return null;
        }
    }

    async Task<byte[]> SiteDownload(string url, string cookie, string referer)
    {
        try
        {
            return await Http.Download(url, cookie: cookie, referer: referer, timeoutSeconds: 40,
                headers: SiteHeaders(referer, cookie), useDefaultHeaders: false,
                MaxResponseContentBufferSize: 50_000_000);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_dl");
            return null;
        }
    }

    async Task<(string content, HttpResponseMessage response)> SitePostBase(string url, string data, string cookie, string referer)
    {
        try
        {
            var content = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");

            return await Http.BasePost(url, content,
                cookie: cookie,
                timeoutSeconds: 20,
                headers: HeadersModel.Init(
                    ("user-agent", ModInit.conf?.ua ?? LostfilmSettings.UA153),
                    ("referer", referer),
                    ("x-requested-with", "XMLHttpRequest"),
                    ("accept", "application/json, text/javascript, */*; q=0.01"),
                    ("accept-language", "ru,en;q=0.9")
                ),
                useDefaultHeaders: false,
                statusCodeOK: false,
                disposeData: false
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_post");
            return (null, null);
        }
    }

    async Task<string> SitePost(string url, string data, string referer, string cookie = null)
    {
        var (content, _) = await SitePostBase(url, data, cookie, referer);
        return content;
    }

    /// <summary>v_search.php?a={id}. Возвращает HTML и флаг "сессия истекла" (302 → /login).</summary>
    async Task<(string html, bool expired)> VSearch(string episodeID, string cookie)
    {
        var init = ModInit.conf;

        try
        {
            var (body, response) = await Http.BaseGet(
                $"{init.host}/v_search.php?a={episodeID}",
                cookie: cookie,
                referer: $"{init.host}/",
                timeoutSeconds: 20,
                headers: SiteHeaders($"{init.host}/", cookie),
                useDefaultHeaders: false,
                statusCodeOK: false
            );

            if (response != null)
            {
                string location = response.Headers.Location?.ToString();

                if (response.StatusCode == HttpStatusCode.Redirect || response.StatusCode == HttpStatusCode.Found || response.StatusCode == HttpStatusCode.MovedPermanently)
                {
                    if (!string.IsNullOrEmpty(location) && location.Contains("/login"))
                        return (null, true);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    return (null, true);
            }

            // 200 + страница логина вместо v_search = тоже истёкшая сессия
            if (!string.IsNullOrEmpty(body) && body.Contains("sign-in-panel") && !body.Contains("/V/") && !body.Contains("location.replace"))
                return (null, true);

            return (body, false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_vsearch");
            return (null, false);
        }
    }
    #endregion


    #region TorrServer

    string PickTorrServer(LostfilmSettings init)
    {
        if (init.auth_torrs?.FirstOrDefault(i => i.enable) != null)
        {
            var ts = init.auth_torrs.Where(i => i.enable).OrderBy(_ => Random.Shared.Next()).First();
            return ts.host;
        }

        if (init.torrs != null && init.torrs.Length > 0)
            return init.torrs[Random.Shared.Next(0, init.torrs.Length)];

        return null;
    }

    /// <summary>
    /// Определяет index нужной серии внутри раздачи по file_stats (SxxEyy в имени файла).
    /// Возвращает 0, если сопоставить не удалось (тогда используется index=1).
    /// </summary>
    async Task<int> ResolveFileIndex(string tsHost, string hash, int season, int episode, LostfilmSettings init)
    {
        try
        {
            string stat = await Http.Get($"{tsHost}/stream?link={hash}&index=1&stat",
                timeoutSeconds: 30,
                headers: TsAuthHeaders(init),
                useDefaultHeaders: false,
                statusCodeOK: false);

            if (string.IsNullOrEmpty(stat))
                return 0;

            // Формат имени: Breaking.Bad.S01E02.1080p.rus.LostFilm.TV.mkv
            string pattern = $@"S{season:D2}E{episode:D2}";
            var matches = Regex.Matches(stat, @"""id"":\s*([0-9]+),\s*""path"":\s*""([^""]+)""");

            foreach (Match m in matches)
            {
                string path = m.Groups[2].Value;

                if (path.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (int.TryParse(m.Groups[1].Value, out int id) && id > 0)
                {
                    Log.Debug("lostfilm: resolved {pattern} -> index {id}", pattern, id);
                    return id;
                }
            }

            Log.Debug("lostfilm: {pattern} not found in file_stats, fallback index=1", pattern);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_fileindex");
            return 0;
        }
    }

    /// <summary>POST multipart file[] на /torrent/upload. Возвращает hash или null.</summary>
    async Task<string> TorrUpload(string tsHost, byte[] torrent, LostfilmSettings init)
    {
        try
        {
            var headers = TsAuthHeaders(init);

            using (var form = new MultipartFormDataContent())
            {
                var fileContent = new ByteArrayContent(torrent);
                fileContent.Headers.TryAddWithoutValidation("Content-Type", "application/x-bittorrent");
                form.Add(fileContent, "file[]", "lostfilm.torrent");

                string result = await Http.Post($"{tsHost}/torrent/upload", form,
                    timeoutSeconds: 60,
                    headers: headers,
                    useDefaultHeaders: false,
                    statusCodeOK: false,
                    disposeData: true
                );

                if (string.IsNullOrEmpty(result))
                    return null;

                return Regex.Match(result, "\"hash\":\"([^\"]+)\"").Groups[1].Value is string h && h.Length > 0 ? h : null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_ts");
            return null;
        }
    }

    IReadOnlyList<HeadersModel> TsAuthHeaders(LostfilmSettings init)
    {
        if (init.auth_torrs?.FirstOrDefault(i => i.enable) == null)
            return null;

        var ts = init.auth_torrs.First(i => i.enable);
        if (string.IsNullOrEmpty(ts.login) || string.IsNullOrEmpty(ts.passwd))
            return null;

        string login = ts.login.Replace("{user_uid}", requestInfo?.user_uid ?? string.Empty);

        return HeadersModel.Init(
            ("Authorization", $"Basic {CrypTo.Base64($"{login}:{ts.passwd}")}")
        );
    }
    #endregion


    #region Search / Parse

    async Task<SearchItem> ResolveSearchItem(string title, string original_title, short year, string href, byte serial)
    {
        var init = ModInit.conf;

        // href = уже готовый относительный путь (например /series/Breaking_Bad)
        if (!string.IsNullOrEmpty(href))
        {
            string h = href.StartsWith("http") ? href : $"{init.host}/{href.TrimStart('/')}";
            return new SearchItem
            {
                Link = Regex.Replace(h, $"^{Regex.Escape(init.host)}", string.Empty),
                IsMovie = href.Contains("/movies/")
            };
        }

        var item = await SearchOnSite(title, original_title, year, serial);
        return item;
    }

    /// <summary>POST ajaxik.php act=common&type=search. Пробует original_title, затем title.</summary>
    async Task<SearchItem> SearchOnSite(string title, string original_title, short year, byte serial)
    {
        var init = ModInit.conf;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Query, SearchItem Item)>();

        foreach (string q in new[] { original_title, title })
        {
            string query = (q ?? string.Empty).Trim();
            if (query.Length == 0 || !seen.Add(query))
                continue;

            string body = await SitePost(
                $"{init.host}/ajaxik.php",
                $"act=common&type=search&val={HttpUtility.UrlEncode(query)}",
                $"{init.host}/"
            );

            if (string.IsNullOrEmpty(body))
                continue;

            foreach (var it in ParseSearchJson(body))
                candidates.Add((query, it));
        }

        if (candidates.Count == 0)
            return null;

        // Единственный результат — сайт уже отфильтровал вхождением, принимаем как есть.
        if (candidates.Count == 1)
            return candidates[0].Item;

        int bestIdx = -1, bestScore = -1;

        for (int i = 0; i < candidates.Count; i++)
        {
            var it = candidates[i].Item;

            // Сначала сверяем с original_title, потом с title — берём максимум.
            int score = Math.Max(
                ScoreMatch(Norm(it.TitleOrig), Norm(original_title)),
                ScoreMatch(Norm(it.Title), Norm(title))
            );

            // Жанровый бонус: сериал ожидаем для фильма/сериала соответственно.
            bool isMovie = it.Link.Contains("/movies/");
            if (serial == 1 && !isMovie) score += 25;
            if (serial == 0 && isMovie) score += 25;

            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        if (bestIdx < 0 || bestScore < 20)
            return null;

        return candidates[bestIdx].Item;
    }

    static List<SearchItem> ParseSearchJson(string body)
    {
        var list = new List<SearchItem>();

        try
        {
            var json = JObject.Parse(body);
            var data = json["data"];

            foreach (string arr in new[] { "series", "movies" })
            {
                var items = data?[arr];
                if (items == null)
                    continue;

                foreach (var it in items)
                {
                    string link = it.Value<string>("link") ?? string.Empty;
                    if (link.Length == 0)
                        continue;

                    list.Add(new SearchItem
                    {
                        Id = it.Value<string>("id"),
                        Title = it.Value<string>("title"),
                        TitleOrig = it.Value<string>("title_orig"),
                        Link = link,
                        Icon = it.Value<string>("icon"),
                        IsMovie = link.Contains("/movies/")
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ParseSearchJson failed");
        }

        return list;
    }

    /// <summary>
    /// Парсинг /series/{slug}/seasons.
    /// data-episode="119001001" (SSS+SSS+EEE), data-season="119005999", data-code="119-5-16".
    /// </summary>
    static SeasonsData ParseSeasons(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var flat = Regex.Replace(html, "[\n\r\t]+", " ");
        var result = new SeasonsData();
        var seasons = new Dictionary<int, SeasonData>();
        var seenEpisode = new HashSet<string>();

        // Заголовки сезонов: <h2>5 сезон</h2>
        var seasonTitles = new Dictionary<int, string>();
        foreach (Match m in Regex.Matches(flat, @"<h2[^>]*>\s*([0-9]{1,3})\s*сезон", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(m.Groups[1].Value, out int sn) && !seasonTitles.ContainsKey(sn))
                seasonTitles[sn] = m.Groups[1].Value;
        }

        // Строки серий: ищем блоки <tr>...</tr> с data-episode
        foreach (Match tr in Regex.Matches(flat, @"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            string row = tr.Groups[1].Value;

            var epMatch = Regex.Match(row, @"data-episode=""([0-9]{9})""");
            if (!epMatch.Success)
                continue;

            string episodeID = epMatch.Groups[1].Value;
            if (!seenEpisode.Add(episodeID))
                continue;

            // Полный сезон (e999) в таблице не считаем отдельным эпизодом
            if (episodeID.EndsWith("999"))
                continue;

            var codeMatch = Regex.Match(row, @"data-code=""([0-9]+)-([0-9]+)-([0-9]+)""");
            int season, episode;

            if (codeMatch.Success)
            {
                int.TryParse(codeMatch.Groups[2].Value, out season);
                int.TryParse(codeMatch.Groups[3].Value, out episode);
            }
            else
            {
                // Fallback: SSS+SSS+EEE
                season = int.Parse(episodeID.Substring(3, 3));
                episode = int.Parse(episodeID.Substring(6, 3));
            }

            // Название серии: beta = "5 сезон 16 серия", gamma = "Фелина / Felina"
            string title = string.Empty;
            var gamma = Regex.Match(row, @"<div>\s*([^<]+)<br\s*/?>\s*<span[^>]*>([^<]*)</span>", RegexOptions.IgnoreCase);
            if (gamma.Success)
            {
                string ru = HttpUtility.HtmlDecode(gamma.Groups[1].Value).Trim();
                string en = HttpUtility.HtmlDecode(gamma.Groups[2].Value).Trim();

                title = string.IsNullOrEmpty(en) ? ru : $"{ru} / {en}";
            }

            if (!seasons.TryGetValue(season, out var sd))
            {
                sd = new SeasonData { Number = season };
                seasons[season] = sd;
            }

            sd.Episodes.Add(new EpisodeData
            {
                Season = season,
                Episode = episode,
                EpisodeID = episodeID,
                Title = title
            });
        }

        // Полный сезон (data-season="...999") как fallback, если строк нет
        if (seasons.Count == 0)
        {
            foreach (Match m in Regex.Matches(flat, @"data-season=""([0-9]{9})"""))
            {
                string sid = m.Groups[1].Value;
                int season = int.Parse(sid.Substring(3, 3));

                if (!seasons.ContainsKey(season))
                {
                    var sd = new SeasonData { Number = season };
                    sd.Episodes.Add(new EpisodeData
                    {
                        Season = season,
                        Episode = 1,
                        EpisodeID = sid,
                        Title = "полный сезон"
                    });

                    seasons[season] = sd;
                }
            }
        }

        result.Seasons = seasons.Values.OrderBy(x => x.Number).ToList();
        return result;
    }

    /// <summary>Первый 9-значный episodeID со страницы (для фильмов).</summary>
    static string ParseFirstEpisodeID(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"data-episode=""([0-9]{9})""");
        if (m.Success)
            return m.Groups[1].Value;

        m = Regex.Match(html, @"PlayEpisode\('([0-9]{9})'\)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Страница торрентов: div.inner-box--item { label (SD/1080/MP4), link main a[href=td.php], desc }.
    /// </summary>
    static List<TorrentItem> ParseTorrents(string html)
    {
        var list = new List<TorrentItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var flat = Regex.Replace(html, "[\n\r\t]+", " ");

        foreach (Match m in Regex.Matches(flat,
            @"<div class=""inner-box--item"">\s*<div class=""inner-box--label"">\s*([^<]*?)\s*</div>\s*<div class=""inner-box--link main""><a\s+href=""([^""]+)""[^>]*>([^<]*)</a></div>(?:\s*<div class=""inner-box--link sub"">[\s\S]*?</div>)?\s*<div class=""inner-box--desc"">([^<]*)</div>",
            RegexOptions.IgnoreCase))
        {
            string label = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
            string url = HttpUtility.HtmlDecode(m.Groups[2].Value).Trim();
            string name = HttpUtility.HtmlDecode(m.Groups[3].Value).Trim();
            string desc = Regex.Replace(HttpUtility.HtmlDecode(m.Groups[4].Value).Trim(), @"\s+", " ");

            if (url.Length == 0 || !url.Contains("td.php"))
                continue;

            list.Add(new TorrentItem
            {
                QualityLabel = label,
                Name = name,
                TorrentUrl = url,
                Desc = desc
            });
        }

        return list;
    }

    /// <summary>Выбор качества: q=0 → 1080 > MP4(720) > SD; иначе явный индекс/приоритет.</summary>
    static TorrentItem PickQuality(List<TorrentItem> torrents, int q)
    {
        // Явный выбор: q = 1080 / 720 / 480 и т.п.
        if (q > 0)
        {
            var exact = torrents.FirstOrDefault(t => t.QualityLabel.Contains(q.ToString()));
            if (exact != null)
                return exact;

            // q=720 → MP4-контейнер
            if (q == 720)
            {
                var mp4 = torrents.FirstOrDefault(t => t.QualityLabel.Equals("MP4", StringComparison.OrdinalIgnoreCase));
                if (mp4 != null)
                    return mp4;
            }
        }

        // Приоритет по умолчанию
        return torrents.FirstOrDefault(t => t.QualityLabel.Contains("1080"))
            ?? torrents.FirstOrDefault(t => t.QualityLabel.Equals("MP4", StringComparison.OrdinalIgnoreCase) || t.QualityLabel.Contains("720"))
            ?? torrents.FirstOrDefault(t => t.QualityLabel.Equals("SD", StringComparison.OrdinalIgnoreCase))
            ?? torrents.FirstOrDefault(t => t.QualityLabel.Contains("2160"))
            ?? torrents.First();
    }

    static bool IsBencode(byte[] data)
    {
        if (data == null || data.Length < 2)
            return false;

        // .torrent всегда начинается с "d8:announce" (или "d4:info")
        return data[0] == (byte)'d';
    }

    static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        return Regex.Replace(s.ToLowerInvariant(), @"[^a-zа-яёеіїґ0-9]+", " ").Trim();
    }

    static int ScoreMatch(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;

        if (a == b) return 100;
        if (a.Contains(b)) return 60;
        if (b.Contains(a) && a.Length > 3) return 40;

        int matches = a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Intersect(b.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Count();

        return matches * 20;
    }
    #endregion


    #region Bind helpers
    ActionResult BindForm(string error, string r, string mail = null, bool success = false)
    {
        if (string.IsNullOrEmpty(r))
            r = new Random().Next(100000, 999999).ToString();

        string phpssid = GetOrCreatePhpSessId(r);
        string host0 = ModInit.conf?.host ?? "https://www.lostfilm.tv";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang='ru'><head><meta charset='utf-8'>");
        sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        sb.Append("<title>LostFilm — привязка</title>");
        sb.Append("<style>");
        sb.Append("body{font-family:-apple-system,Segoe UI,Roboto,sans-serif;background:#14161a;color:#e8eaed;margin:0;padding:24px}");
        sb.Append(".box{max-width:420px;margin:0 auto;background:#1d2026;border:1px solid #2c3038;border-radius:14px;padding:24px}");
        sb.Append("h1{font-size:19px;margin:0 0 6px}.sub{color:#9aa0a6;font-size:13px;margin-bottom:18px}");
        sb.Append("label{display:block;font-size:13px;color:#9aa0a6;margin:12px 0 6px}");
        sb.Append("input[type=text],input[type=password]{width:100%;box-sizing:border-box;padding:11px 12px;border-radius:9px;border:1px solid #343943;background:#12141a;color:#e8eaed;font-size:15px}");
        sb.Append("input:focus{outline:none;border-color:#4c8dff}");
        sb.Append("button{margin-top:18px;width:100%;padding:12px;border:0;border-radius:9px;background:#4c8dff;color:#fff;font-size:15px;font-weight:600;cursor:pointer}");
        sb.Append("button:hover{background:#3d7ae8}");
        sb.Append(".cap{display:flex;gap:10px;align-items:center;margin-top:8px}");
        sb.Append(".cap img{height:52px;border-radius:8px;background:#fff}");
        sb.Append(".msg{margin-top:16px;padding:11px 12px;border-radius:9px;font-size:14px}");
        sb.Append(".err{background:#3a1f22;border:1px solid #6b2b31;color:#ffb4b4}");
        sb.Append(".ok{background:#16301f;border:1px solid #2b6b41;color:#a8f0c0}");
        sb.Append("a{color:#4c8dff}");
        sb.Append("</style></head><body><div class='box'>");

        if (success)
        {
            sb.Append("<h1>LostFilm</h1>");
            sb.Append("<div class='msg ok'>Готово, сессия привязана. Можно смотреть через <a href='/lite/lostfilm'>/lite/lostfilm</a>.</div>");
            sb.Append("</div></body></html>");
            return Content(sb.ToString(), "text/html; charset=utf-8");
        }

        sb.Append("<h1>LostFilm</h1>");
        sb.Append("<div class='sub'>Вход в аккаунт LostFilm.TV. Пароль передаётся только на сайт и не сохраняется в логах.</div>");

        if (!string.IsNullOrEmpty(error))
            sb.Append($"<div class='msg err'>{WebUtility.HtmlEncode(error)}</div>");

        sb.Append("<form method='post' action='/bind/lostfilm'>");
        sb.Append($"<input type='hidden' name='r' value='{WebUtility.HtmlEncode(r)}'>");
        sb.Append($"<input type='hidden' name='phpssid' value='{WebUtility.HtmlEncode(phpssid)}'>");
        sb.Append("<label>E-mail</label>");
        sb.Append($"<input type='text' name='mail' value='{WebUtility.HtmlEncode(mail ?? string.Empty)}' required autocomplete='username'>");
        sb.Append("<label>Пароль</label>");
        sb.Append("<input type='password' name='pass' required autocomplete='current-password'>");
        sb.Append("<label>Код с картинки</label>");
        sb.Append("<div class='cap'>");
        sb.Append($"<input type='text' name='captcha' required autocomplete='off'>");
        sb.Append($"<img src='/bind/lostfilm/captcha?r={WebUtility.UrlEncode(r)}&phpssid={WebUtility.UrlEncode(phpssid)}' alt='captcha'>");
        sb.Append("</div>");
        sb.Append("<button type='submit'>Войти</button>");
        sb.Append("</form></div></body></html>");

        return Content(sb.ToString(), "text/html; charset=utf-8");
    }

    [HttpGet]
    [Route("/bind/lostfilm/captcha")]
    public async Task<ActionResult> BindCaptcha(string r, string phpssid)
    {
        var init = ModInit.conf;

        string cookie = string.IsNullOrEmpty(phpssid) ? null : $"PHPSESSID={phpssid}";
        byte[] img = await SiteDownload($"{init.host}/simple_captcha.php?{r}", cookie, $"{init.host}/");

        if (img == null || img.Length == 0)
            return StatusCode(502);

        HttpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        return File(img, "image/gif");
    }

    /// <summary>
    /// PHPSESSID обязан совпадать между получением капчи и POST логина
    /// (капча на сайте привязана к PHP-сессии). Держим его краткосрочно в memoryCache.
    /// </summary>
    string GetOrCreatePhpSessId(string r)
    {
        string key = $"lostfilm:v1:phpsessid:{r}";
        if (memoryCache.TryGetValue(key, out string sid) && !string.IsNullOrEmpty(sid))
            return sid;

        sid = Guid.NewGuid().ToString("N").Substring(0, 26);
        memoryCache.Set(key, sid, DateTime.Now.AddMinutes(10));
        return sid;
    }

    static string ReadSetCookie(HttpResponseMessage response)
    {
        try
        {
            if (response?.Headers == null)
                return null;

            if (response.Headers.TryGetValues("Set-Cookie", out var vals))
                return string.Join(" ", vals);

            return null;
        }
        catch
        {
            return null;
        }
    }

    static string ExtractCookieValue(string setCookie, string name)
    {
        if (string.IsNullOrEmpty(setCookie))
            return null;

        var m = Regex.Match(setCookie, $@"{Regex.Escape(name)}=([^;\s]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    static string BuildSessionCookie(string setCookie, string sessionValue)
    {
        var parts = new List<string> { $"lf_session={sessionValue}" };

        foreach (string n in new[] { "lf_udv", "lnk_uid" })
        {
            string v = ExtractCookieValue(setCookie, n);
            if (!string.IsNullOrEmpty(v))
                parts.Add($"{n}={v}");
        }

        return string.Join("; ", parts);
    }
    #endregion


    #region DATA CLASSES (JSON-roundtrip: только { get; set; })

    public class SearchItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string TitleOrig { get; set; }
        public string Link { get; set; }
        public string Icon { get; set; }
        public bool IsMovie { get; set; }
        public string EpisodeID { get; set; }
    }

    public class SeasonsData
    {
        public List<SeasonData> Seasons { get; set; } = new();
    }

    public class SeasonData
    {
        public int Number { get; set; }
        public List<EpisodeData> Episodes { get; set; } = new();
    }

    public class EpisodeData
    {
        public int Season { get; set; }
        public int Episode { get; set; }
        public string EpisodeID { get; set; }
        public string Title { get; set; }
    }

    public class TorrentItem
    {
        public string QualityLabel { get; set; }
        public string Name { get; set; }
        public string TorrentUrl { get; set; }
        public string Desc { get; set; }
    }
    #endregion



    #region CLIENT API DTO (JSON-roundtrip: только { get; set; })

    /// <summary>
    /// R1: статус авторизации. Поле session (полный lf_session, 32hex.цифры) отдаётся
    /// ТОЛЬКО при auth:true — это осознанная передача владения сессией клиентскому
    /// плагину (v2.2b). Плагин хранит его в Lampa.Storage и присылает назад заголовком
    /// X-Lf-Session; сервер при этом cookie из conf/memoryCache не раскрывает.
    /// </summary>
    public class StatusResult
    {
        public bool auth { get; set; }
        public string user { get; set; }
        public bool cookie_valid { get; set; }
        public string session { get; set; }
    }

    /// <summary>R5: карточка сериала из /my/order_1/type_1.</summary>
    public class MySerialItem
    {
        public string id { get; set; }
        public string link { get; set; }
        public string title { get; set; }
        public string title_en { get; set; }
        public string poster { get; set; }
        public bool follow { get; set; }
        public int watched { get; set; }
        public int episodes { get; set; }
    }

    /// <summary>R6: новинка серии с /new/.</summary>
    public class NewEpisodeItem
    {
        public string link { get; set; }
        public string title { get; set; }
        public string title_en { get; set; }
        public string desc { get; set; }
        public string desc_en { get; set; }
        public string poster { get; set; }
        public string date { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
        public bool movie { get; set; }
        public string episodeId { get; set; }
        public string code { get; set; }
        public string rating { get; set; }
        public int comments { get; set; }
    }

    /// <summary>R7: группа расписания ("сегодня"/"завтра"/...) с сериями.</summary>
    public class ScheduleGroup
    {
        public string group { get; set; }
        public List<ScheduleItem> items { get; set; } = new();
    }

    public class ScheduleItem
    {
        public string link { get; set; }
        public string title { get; set; }
        public string title_en { get; set; }
        public string episodeLink { get; set; }
        public string poster { get; set; }
        public string desc { get; set; }
        public string date { get; set; }
        public int season { get; set; }
        public int episode { get; set; }
        public bool movie { get; set; }
    }
    #endregion
}


/// <summary>
/// AuthEngine модуля Lostfilm.
/// Приоритет получения cookie: memoryCache (после /bind) → conf.cookie → авто-логин.
/// Секреты (cookie/пароль) НИКОГДА не пишутся в лог.
/// </summary>
public static class LostfilmAuth
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext("SourceContext", "LostfilmAuth");

    public const string CookieKey = "lostfilm:cookie";

    /// <summary>Cookie для запросов к сайту; null — если сессии нет.</summary>
    public static async Task<string> GetCookie(LostfilmController controller, LostfilmSettings init)
    {
        // v2.2b: приоритет 0 — сессия от клиентского плагина (header X-Lf-Session).
        // НЕ кешируется в CookieKey — multi-user изоляция; НЕ логируется.
        try
        {
            string hdr = controller.HttpContext?.Request?.Headers["X-Lf-Session"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(hdr) && Regex.IsMatch(hdr, "^[0-9a-f]{32}\\.[0-9]{1,10}$"))
                return $"lf_session={hdr}";
        }
        catch { /* header отсутствует/нечитаем — фолбэк на штатную цепочку */ }

        var mc = controller.memoryCache;

        if (mc.TryGetValue(CookieKey, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        if (!string.IsNullOrEmpty(init.cookie))
            return init.cookie;

        // Авто-логин. need_captcha=true практически всегда — вернёт null.
        return await RefreshCookie(controller, init);
    }

    /// <summary>Сброс кеша cookie (при session_expired) и повторный логин, если есть креды.</summary>
    public static async Task<string> RefreshCookie(LostfilmController controller, LostfilmSettings init)
    {
        var mc = controller.memoryCache;
        mc.Remove(CookieKey);

        if (string.IsNullOrEmpty(init.login) || string.IsNullOrEmpty(init.passwd))
        {
            if (!string.IsNullOrEmpty(init.cookie))
                return init.cookie;

            Log.Warning("lostfilm: сессия не привязана — используйте /bind/lostfilm");
            return null;
        }

        // Авто-логин без капчи обычно возвращает {"need_captcha":true}
        string cookie = await AutoLogin(init);

        if (string.IsNullOrEmpty(cookie))
        {
            Log.Warning("lostfilm: капча требуется, используйте /bind/lostfilm");

            if (!string.IsNullOrEmpty(init.cookie))
                return init.cookie;

            return null;
        }

        mc.Set(CookieKey, cookie, DateTime.Today.AddDays(1));
        return cookie;
    }

    static async Task<string> AutoLogin(LostfilmSettings init)
    {
        try
        {
            string postdata =
                "act=users&type=login" +
                $"&mail={HttpUtility.UrlEncode(init.login)}" +
                $"&pass={HttpUtility.UrlEncode(init.passwd)}" +
                "&rem=1&captcha=&need_captcha=1";

            var content = new StringContent(postdata, Encoding.UTF8, "application/x-www-form-urlencoded");

            // AllowAutoRedirect=false обязателен: иначе 302 на /login съест Set-Cookie.
            var (body, response) = await Http.BasePost($"{init.host}/ajaxik.users.php", content,
                timeoutSeconds: 20,
                headers: HeadersModel.Init(
                    ("user-agent", init.ua ?? LostfilmSettings.UA153),
                    ("referer", $"{init.host}/"),
                    ("x-requested-with", "XMLHttpRequest"),
                    ("accept", "application/json, text/javascript, */*; q=0.01")
                ),
                useDefaultHeaders: false,
                statusCodeOK: false,
                disposeData: false
            );

            if (response?.Headers == null)
                return null;

            if (!response.Headers.TryGetValues("Set-Cookie", out var vals))
                return null;

            string setCookie = string.Join(" ", vals);
            string session = Regex.Match(setCookie, "lf_session=([^;\\s]+)").Groups[1].Value;

            if (string.IsNullOrEmpty(session))
                return null;

            var parts = new List<string> { $"lf_session={session}" };

            foreach (string n in new[] { "lf_udv", "lnk_uid" })
            {
                string v = Regex.Match(setCookie, $"{n}=([^;\\s]+)").Groups[1].Value;
                if (!string.IsNullOrEmpty(v))
                    parts.Add($"{n}={v}");
            }

            return string.Join("; ", parts);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_lf_autologin");
            return null;
        }
    }
}
