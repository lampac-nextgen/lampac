using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.Pools.Json;
using SyncEvents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace TimeCode;

public class TimeCodeController : BaseController
{
    #region timecode.js
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("timecode.js")]
    [Route("timecode/js/{token}")]
    public ActionResult timecode(string token)
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "timecode.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token))
            // Идентичность раздаёт сервер, иначе браузер придумает свою и окажется один на один
            // со своей историей просмотров.
            .Replace("{uid}", InstanceIdentity.Assigned ?? string.Empty);

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion

    #region /timecode/all — легаси, веб-Lampa
    /// <summary>
    /// Контракт неизменен: <c>{ хеш: "json-строка road" }</c> по одной карточке. road собирается
    /// из типизированных колонок обратно, поэтому старый plugin.js работает без правок.
    /// </summary>
    [HttpGet]
    [Route("/timecode/all")]
    async public Task<ActionResult> Get(string card_id)
    {
        if (string.IsNullOrEmpty(card_id) || requestInfo.user_uid == null)
            return Json(new { });

        string userId = getUserid(requestInfo);

        List<SqlModel> rows;

        using (var sqlDb = SqlContext.Create())
        {
            rows = await sqlDb.timecodes
                .AsNoTracking()
                .Where(i => i.user == userId && i.card == card_id && i.item != null && !i.deleted)
                .ToListAsync();
        }

        if (rows.Count == 0)
            return Json(new { });

        return Json(rows.ToDictionary(i => i.item, i => RoadJson(i)));
    }
    #endregion

    #region /timecode/add — легаси, веб-Lampa
    /// <summary>
    /// Контракт неизменен: form <c>id</c> (хеш) + <c>data</c> (road). Блоб разбирается в колонки,
    /// нераспознанные ключи road уходят в <c>extra</c>. identity здесь вывести не из чего — хеш не
    /// несёт ни сезона, ни серии, — так что она остаётся NULL до первой записи нативного клиента.
    /// </summary>
    [HttpPost]
    [Route("/timecode/add")]
    async public Task<ActionResult> Set([FromQuery] string card_id, [FromQuery] string connectionId, [FromForm] string id, [FromForm] string data)
    {
        if (string.IsNullOrEmpty(card_id) ||
            string.IsNullOrEmpty(id) ||
            string.IsNullOrEmpty(data) ||
            requestInfo.user_uid == null)
            return JsonFailure();

        JObject road;
        try { road = JsonConvert.DeserializeObject<JObject>(data); }
        catch { return JsonFailure("data"); }

        if (road == null)
            return JsonFailure("data");

        var row = new JObject
        {
            // Идентичность: либо веб её донёс в road (куб хранит запись дословно, поэтому `id`
            // доезжает оттуда так же, как доехал `updated`), либо выводим из card_id — у фильма
            // это `{tmdb}_movie`, то есть вся идентичность. Сериалу так нельзя: card_id называет
            // шоу, а какая это серия, знает только хеш.
            ["id"] = Normalize(road.Value<string>("id")) ?? IdentityFromMovieCard(card_id),
            ["hash"] = id,
            ["card"] = card_id,
            ["position"] = road.Value<double?>("time") ?? 0,
            ["duration"] = road.Value<double?>("duration") ?? 0,
            ["percent"] = road.Value<double?>("percent") ?? 0,
            ["profile"] = road.Value<long?>("profile") ?? 0,
            ["watched_at"] = road.Value<long?>("updated") ?? 0,
            ["extra"] = ExtraJson(road)
        };

        var result = await Write(new[] { row }, connectionId);

        return result.accepted > 0 ? JsonSuccess(result.version) : JsonFailure();
    }
    #endregion

    #region /timecode/dump
    /// <summary>
    /// Всё, что есть у пользователя, включая надгробия — клиент с просроченным курсором обязан
    /// узнать и про удаления, иначе поднимет их обратно.
    /// </summary>
    [HttpGet]
    [Route("/timecode/dump")]
    async public Task<ActionResult> Dump()
    {
        if (requestInfo.user_uid == null)
            return JsonFailure();

        string userId = getUserid(requestInfo);

        using (var sqlDb = SqlContext.Create())
        {
            var rows = await sqlDb.timecodes
                .AsNoTracking()
                .Where(i => i.user == userId)
                .OrderBy(i => i.updated_at)
                .ToListAsync();

            return Json(new
            {
                version = rows.Count == 0 ? 0 : rows[^1].updated_at,
                rows = rows.Select(WireRow).ToArray()
            });
        }
    }
    #endregion

    #region /timecode/changelog
    /// <summary>
    /// Дельты: <c>updated_at &gt; since</c>. Строго «больше» корректно только потому, что
    /// <c>updated_at</c> монотонен внутри пользователя (см. <see cref="NextStamp"/>).
    /// </summary>
    [HttpGet]
    [Route("/timecode/changelog")]
    async public Task<ActionResult> Changelog(long since, int limit = 5000)
    {
        if (requestInfo.user_uid == null)
            return JsonFailure();

        string userId = getUserid(requestInfo);
        limit = Math.Clamp(limit, 1, 20000);

        using (var sqlDb = SqlContext.Create())
        {
            var rows = await sqlDb.timecodes
                .AsNoTracking()
                .Where(i => i.user == userId && i.updated_at > since)
                .OrderBy(i => i.updated_at)
                .Take(limit)
                .ToListAsync();

            return Json(new
            {
                // Курсор двигается только по отданному, иначе усечённый по limit хвост потеряется.
                version = rows.Count == 0 ? since : rows[^1].updated_at,
                truncated = rows.Count == limit,
                rows = rows.Select(WireRow).ToArray()
            });
        }
    }
    #endregion

    #region /timecode/areas
    /// <summary>
    /// Перепись областей данных этого пользователя: общая плюс по одной на каждый profile_id.
    ///
    /// Профиля как сущности у сервера нет — область заводится первой же записью в неё. Поэтому
    /// «а есть ли такой профиль» спросить не у кого, и вопрос сводится к «а лежит ли там хоть
    /// что-нибудь». Клиенту это нужно, чтобы показать, куда он попал, и заметить, что два клиента
    /// назвали один и тот же профиль по-разному и разъехались.
    /// </summary>
    [HttpGet]
    [Route("/timecode/areas")]
    async public Task<ActionResult> Areas()
    {
        if (requestInfo.user_uid == null)
            return JsonFailure();

        string root = Sanitize(requestInfo.user_uid);
        string prefix = $"{root}_";

        using (var sqlDb = SqlContext.Create())
        {
            var areas = await sqlDb.timecodes
                .AsNoTracking()
                .Where(i => i.user == root || i.user.StartsWith(prefix))
                .GroupBy(i => i.user)
                .Select(g => new
                {
                    user = g.Key,
                    rows = g.Count(i => !i.deleted),
                    deleted = g.Count(i => i.deleted),
                    updated_at = g.Max(i => i.updated_at),
                    watched_at = g.Max(i => i.watched_at)
                })
                .ToListAsync();

            return Json(new
            {
                uid = root,
                areas = areas.OrderByDescending(a => a.rows).Select(a => new
                {
                    // Общая область — та, куда пишут клиенты без profile_id.
                    profile_id = a.user == root ? null : a.user.Substring(prefix.Length),
                    a.rows,
                    a.deleted,
                    a.updated_at,
                    a.watched_at
                }).ToArray()
            });
        }
    }
    #endregion

    #region /timecode/set
    /// <summary>
    /// Запись для нативных клиентов. Тело — одна строка, массив строк или <c>{ "rows": [...] }</c>.
    /// Удаление — та же строка с <c>"deleted": true</c>, отдельного эндпоинта нет.
    /// </summary>
    [HttpPost]
    [Route("/timecode/set")]
    async public Task<ActionResult> SetRows(string connectionId)
    {
        if (requestInfo.user_uid == null)
            return JsonFailure();

        string body;
        using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8))
            body = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(body))
            return JsonFailure("body");

        JToken token;
        try { token = JsonConvert.DeserializeObject<JToken>(body); }
        catch { return JsonFailure("body"); }

        var rows = new List<JObject>();

        if (token is JArray array)
            rows.AddRange(array.Children<JObject>());
        else if (token is JObject single)
        {
            if (single["rows"] is JArray nested)
                rows.AddRange(nested.Children<JObject>());
            else
                rows.Add(single);
        }

        if (rows.Count == 0)
            return JsonFailure("rows");

        var result = await Write(rows, connectionId);

        return Json(new
        {
            success = result.accepted > 0 || result.skipped > 0,
            version = result.version,
            accepted = result.accepted,
            skipped = result.skipped
        });
    }
    #endregion

    #region Write
    /// <summary>
    /// Единственный путь записи — и легаси, и нативный. Возвращает новый курсор и сколько строк
    /// приняли. Устаревшее по <c>watched_at</c> не пишется, но и ошибкой не считается: очередь
    /// офлайн-устройства доезжает часами позже, и её строка не должна затирать свежую чужую.
    /// </summary>
    async Task<(long version, int accepted, int skipped)> Write(IEnumerable<JObject> rows, string connectionId)
    {
        string userId = getUserid(requestInfo);
        var semaphore = new SemaphorManager(SqlContext.SemaphoreKeyFor(userId), TimeSpan.FromSeconds(20));

        long version = 0;
        int accepted = 0, skipped = 0;
        var touched = new List<string>();

        try
        {
            if (!await semaphore.WaitAsync())
                return (0, 0, 0);

            using (var sqlDb = SqlContext.Create())
            {
                long stamp = NextStamp(sqlDb, userId);
                // Курсор сервера на момент входа: его же возвращаем, когда не приняли ничего,
                // чтобы клиент не откатил свой `since` в ноль.
                version = stamp - 1;

                // Locate читает базу, а SaveChanges ещё не вызван, поэтому вторая строка пачки
                // с тем же ключом не нашла бы первую и ушла бы вторым Add — в уникальный индекс.
                var batch = new Dictionary<string, SqlModel>();

                foreach (var row in rows)
                {
                    string identity = Normalize(row.Value<string>("id"));
                    string hash = Normalize(row.Value<string>("hash"));
                    string card = Normalize(row.Value<string>("card")) ?? CardOf(identity);

                    if (card == null || (identity == null && hash == null))
                    {
                        skipped++;
                        continue;
                    }

                    string batchKey = identity ?? $"{card}|{hash}";
                    bool known = batch.TryGetValue(batchKey, out var entity);
                    bool hashTaken = false;

                    if (!known)
                        (entity, hashTaken) = await Locate(sqlDb, userId, identity, card, hash);

                    long watchedAt = row.Value<long?>("watched_at") ?? 0;

                    // Оба штампа ненулевые и входящий старше — это догнавшая нас старая правда.
                    if (entity != null && watchedAt > 0 && entity.watched_at > 0 && watchedAt < entity.watched_at)
                    {
                        skipped++;
                        continue;
                    }

                    bool insert = entity == null;
                    entity ??= new SqlModel { user = userId, card = card };

                    entity.identity ??= identity;
                    // Присланный хеш авторитетнее сохранённого: у карточки мог смениться
                    // original_title, и клиент теперь считает другой хеш — по нему и надо
                    // находить запись впредь. Старый хеш становится недостижим, но его никто
                    // уже и не спрашивает. Исключение — занятый слот: писать туда значит
                    // нарушить UNIQUE (user, card, item), и запись живёт без хеша, по identity.
                    if (!known && !hashTaken && hash != null)
                        entity.item = hash;
                    entity.card = card;
                    entity.deleted = row.Value<bool?>("deleted") ?? false;
                    entity.percent = entity.deleted ? 0 : row.Value<double?>("percent") ?? 0;
                    entity.position = entity.deleted ? 0 : row.Value<double?>("position") ?? 0;
                    // Длительность у надгробия сохраняется: из неё Lampa считает позицию при
                    // повторной отметке, и обнулять её — сломать следующий toggle на всех клиентах.
                    entity.duration = row.Value<double?>("duration") ?? entity.duration;
                    entity.profile = row.Value<long?>("profile") ?? entity.profile;
                    entity.extra = row.Value<string>("extra") ?? entity.extra;
                    entity.watched_at = watchedAt > 0 ? watchedAt : entity.watched_at;
                    entity.updated_at = stamp++;

                    // Второй раз тот же ключ в пачке — сущность уже в change tracker, и менять её
                    // состояние нельзя: Update поверх Added превратит вставку в UPDATE пустоты.
                    if (!batch.ContainsKey(batchKey))
                    {
                        if (insert)
                            sqlDb.Add(entity);
                        else
                            sqlDb.Update(entity);
                    }

                    batch[batchKey] = entity;
                    accepted++;
                    version = entity.updated_at;
                    touched.Add(entity.identity ?? entity.item);
                }

                if (accepted > 0)
                    await sqlDb.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "TimeCodeController", "id_oqjny2lw");
            // SaveChanges в конце, поэтому исключение = не записано ничего. Курсор не отдаём.
            return (0, 0, 0);
        }
        finally
        {
            semaphore.Release();
        }

        // Отправителя шина исключает сама (NwsEvents.SendAsync сравнивает connectionId), поэтому
        // своё же эхо до клиента не доходит и пинг-понга прогресса между устройствами нет.
        if (accepted > 0)
        {
            string edata = JsonConvertPool.SerializeObject(new { version, ids = touched });
            _ = NwsEvents.SendAsync(connectionId, requestInfo.user_uid, "timecode", edata).ConfigureAwait(false);
        }

        return (version, accepted, skipped);
    }

    /// <summary>
    /// Строка ищется сначала по identity, потом по паре карточка+хеш: так запись, созданная
    /// вебом, при первой нативной записи получает identity, а не удваивается.
    /// </summary>
    /// <returns>Найденная строка и признак «слот хеша занят чужой identity».</returns>
    static async Task<(SqlModel entity, bool hashTaken)> Locate(SqlContext sqlDb, string userId, string identity, string card, string hash)
    {
        SqlModel byHash = hash == null
            ? null
            : await sqlDb.timecodes.AsNoTracking()
                .FirstOrDefaultAsync(i => i.user == userId && i.card == card && i.item == hash);

        if (identity != null)
        {
            var byIdentity = await sqlDb.timecodes.AsNoTracking()
                .FirstOrDefaultAsync(i => i.user == userId && i.identity == identity);

            if (byIdentity != null)
                return (byIdentity, byHash != null && byHash.Id != byIdentity.Id);
        }

        if (byHash == null)
            return (null, false);

        // Отказываем только когда обе identity известны и разные: один хеш на два разных тайтла —
        // ровно та коллизия, из-за которой хеш и перестал быть ключом. Запись из веба identity не
        // несёт и имеет в виду именно строку с этим хешем, какая бы identity у неё ни стояла, —
        // иначе веб-правка ушла бы в дубль.
        if (identity == null || byHash.identity == null || byHash.identity == identity)
            return (byHash, false);

        Serilog.Log.Warning(
            "{Module} hash {Hash} of card {Card} is held by {Held}, {Incoming} gets its own row",
            "TimeCode", hash, card, byHash.identity, identity);

        return (null, true);
    }

    /// <summary>
    /// Курсор дельт обязан строго возрастать: две строки, принятые в одну миллисекунду, дали бы
    /// одинаковый <c>updated_at</c>, и <c>&gt; since</c> потерял бы вторую. Поэтому штамп — это
    /// max(сейчас, последний + 1), а массовая запись занимает по миллисекунде на строку.
    /// </summary>
    static long NextStamp(SqlContext sqlDb, string userId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long last = sqlDb.timecodes.AsNoTracking()
            .Where(i => i.user == userId)
            .Max(i => (long?)i.updated_at) ?? 0;

        return now > last ? now : last + 1;
    }
    #endregion

    #region Wire helpers
    static Dictionary<string, object> WireRow(SqlModel i)
    {
        var row = new Dictionary<string, object>(10)
        {
            ["id"] = i.identity,
            ["hash"] = i.item,
            ["card"] = i.card,
            ["position"] = i.position,
            ["duration"] = i.duration,
            ["percent"] = i.percent,
            ["watched_at"] = i.watched_at,
            ["updated_at"] = i.updated_at
        };

        if (i.deleted)
            row["deleted"] = true;

        if (i.profile != 0)
            row["profile"] = i.profile;

        return row;
    }

    /// <summary>road-объект Lampa, собранный обратно из колонок, плюс уцелевшие в extra ключи.</summary>
    static string RoadJson(SqlModel i)
    {
        var road = new JObject
        {
            ["duration"] = i.duration,
            ["time"] = i.position,
            ["percent"] = i.percent,
            ["profile"] = i.profile,
            ["updated"] = i.watched_at
        };

        if (i.identity != null)
            road["id"] = i.identity;

        if (!string.IsNullOrEmpty(i.extra))
        {
            try { road.Merge(JObject.Parse(i.extra)); } catch { }
        }

        return road.ToString(Formatting.None);
    }

    static readonly string[] RoadKnownKeys = { "time", "duration", "percent", "profile", "updated", "hash", "id" };

    /// <summary>Ключи road, для которых нет колонки. Пусто — NULL, чтобы не хранить "{}".</summary>
    static string ExtraJson(JObject road)
    {
        var extra = new JObject(road);
        foreach (string key in RoadKnownKeys)
            extra.Remove(key);

        return extra.Count == 0 ? null : extra.ToString(Formatting.None);
    }

    static readonly Regex IdentityPattern = new(@"^(movie|tv)-(\d+)(?:-s(\d+)e(\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// <c>movie-674</c> → <c>674_movie</c>, <c>tv-1851-s1e2</c> → <c>1851_tv</c>. Карточка — это
    /// группировка, и правило её вывода живёт здесь одно: клиент присылает только <c>id</c>.
    /// </summary>
    static string CardOf(string identity)
    {
        if (identity == null)
            return null;

        var match = IdentityPattern.Match(identity);
        if (!match.Success)
            return null;

        return $"{match.Groups[2].Value}_{match.Groups[1].Value.ToLowerInvariant()}";
    }

    static readonly Regex MovieCardPattern = new(@"^(\d+)_movie$", RegexOptions.Compiled);

    /// <summary>
    /// `674_movie` → `movie-674`. Работает только для фильмов: у сериала `card_id` называет шоу,
    /// а сезон и серию несёт хеш, который необратим.
    /// </summary>
    static string IdentityFromMovieCard(string card)
    {
        var match = MovieCardPattern.Match(card ?? string.Empty);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out long id) || id <= 0)
            return null;

        return $"movie-{id}";
    }

    static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    string getUserid(RequestModel requestInfo)
    {
        string user_id = requestInfo.user_uid;

        if (HttpContext.Request.Query.TryGetValue("profile_id", out var profile_id) && !string.IsNullOrEmpty(profile_id) && profile_id != "0")
            user_id = $"{user_id}_{profile_id}";

        return Sanitize(user_id);
    }

    /// <summary>Отбрасывание идёт посимвольно, поэтому склейка до и после даёт одно и то же.</summary>
    static string Sanitize(string value) => Regex.Replace(value, "[^a-z0-9\\-_\\.]+", "", RegexOptions.IgnoreCase);

    JsonResult JsonSuccess(long version) => Json(new { success = true, version });

    ActionResult JsonFailure(string message = null) => ContentTo(JsonConvertPool.SerializeObject(new { success = false, message }));
    #endregion
}
