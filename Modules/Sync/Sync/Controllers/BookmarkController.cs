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
using Shared.Services.Pools;
using Shared.Services.Pools.Json;
using SyncEvents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Sync;

public class BookmarkController : BaseController
{
    #region bookmark.js
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("bookmark.js")]
    [Route("bookmark/js/{token}")]
    public ActionResult BookmarkJS(string token)
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/bookmark.js", "bookmark.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion

    static readonly string[] BookmarkCategories = {
        "history",
        "like",
        "watch",
        "wath",
        "book",
        "look",
        "viewed",
        "scheduled",
        "continued",
        "thrown"
    };


    #region List
    [HttpGet]
    [Route("/bookmark/list")]
    public async Task<ActionResult> List(string filed)
    {
        string userUid = getUserid(requestInfo, HttpContext);

        using (var sqlDb = SqlContext.Create())
        {
            bool IsDbInitialization = sqlDb.bookmarks.AsNoTracking().FirstOrDefault(i => i.user == userUid) != null;
            if (!IsDbInitialization)
                return Json(new { dbInNotInitialization = true });

            var data = GetBookmarksForResponse(sqlDb);
            if (!string.IsNullOrEmpty(filed))
                return ContentTo(data[filed].ToString(Formatting.None));

            return ContentTo(data.ToString(Formatting.None));
        }
    }
    #endregion

    #region Set
    [HttpPost]
    [Route("/bookmark/set")]
    public async Task<ActionResult> Set(string connectionId)
    {
        if (string.IsNullOrEmpty(requestInfo.user_uid))
            return JsonFailure();

        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, false, PoolInvk.bufferSizeStreamReader, leaveOpen: true))
        {
            string body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
                return JsonFailure();

            var token = JsonConvert.DeserializeObject<JToken>(body);
            if (token == null)
                return JsonFailure();

            var jobs = new List<JObject>();
            if (token.Type == JTokenType.Array)
            {
                foreach (var obj in token.Children<JObject>())
                    jobs.Add(obj);
            }
            else if (token is JObject singleJob)
            {
                jobs.Add(singleJob);
            }

            bool IsDbInitialization = false;
            var semaphore = new SemaphorManager(SqlContext.semaphoreKey, TimeSpan.FromSeconds(30));

            try
            {
                bool _acquired = await semaphore.WaitAsync();
                if (!_acquired)
                    return JsonFailure();

                using (var sqlDb = SqlContext.Create())
                {
                    string userUid = getUserid(requestInfo, HttpContext);

                    IsDbInitialization = sqlDb.bookmarks.AsNoTracking().FirstOrDefault(i => i.user == userUid) != null;

                    var data = LoadBookmarks(sqlDb, userUid);

                    foreach (var job in jobs)
                    {
                        string where = job.Value<string>("where")?.ToLowerAndTrim();
                        if (string.IsNullOrWhiteSpace(where))
                            return JsonFailure();

                        if (IsDbInitialization && ModInit.conf.fullset == false)
                        {
                            if (where == "card" || BookmarkCategories.Contains(where))
                                return JsonFailure("enable Sync.fullset in init.conf");
                        }

                        if (!job.TryGetValue("data", out var dataValue))
                            return JsonFailure();

                        data[where] = dataValue;
                    }

                    EnsureDefaultArrays(data);

                    Save(sqlDb, userUid, data);
                }
            }
            catch
            {
                return JsonFailure();
            }
            finally
            {
                semaphore.Release();
            }

            if (IsDbInitialization)
            {
                _ = NwsEvents.SendAsync(connectionId, requestInfo.user_uid, "bookmark", JsonConvertPool.SerializeObject(new
                {
                    type = "set",
                    data = token,
                    profile_id = getProfileid(requestInfo, HttpContext)
                })).ConfigureAwait(false);
            }

            return JsonSuccess();
        }
    }

    #endregion

    #region Add/Added
    [HttpPost]
    [Route("/bookmark/add")]
    [Route("/bookmark/added")]
    public async Task<ActionResult> Add(string connectionId)
    {
        if (string.IsNullOrEmpty(requestInfo.user_uid))
            return JsonFailure();

        var readBody = await ReadPayloadAsync();

        if (readBody.payloads.Count == 0)
            return JsonFailure();

        bool isAddedRequest = HttpContext?.Request?.Path.Value?.StartsWith("/bookmark/added", StringComparison.OrdinalIgnoreCase) == true;
        var semaphore = new SemaphorManager(SqlContext.semaphoreKey, TimeSpan.FromSeconds(30));

        try
        {
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return JsonFailure();

            using (var sqlDb = SqlContext.Create())
            {
                string userUid = getUserid(requestInfo, HttpContext);
                var data = LoadBookmarks(sqlDb, userUid);
                bool changed = false;

                foreach (var payload in readBody.payloads)
                {
                    var cardId = payload.ResolveCardId();
                    if (cardId == null)
                        continue;

                    changed |= EnsureCard(data, payload.Card, cardId);

                    if (payload.Where != null)
                        changed |= AddToCategory(data, payload.Where, cardId);

                    if (isAddedRequest)
                        changed |= MoveIdToFrontInAllCategories(data, cardId);
                }

                if (changed)
                {
                    Save(sqlDb, userUid, data);

                    if (readBody.token != null)
                    {
                        string edata = JsonConvertPool.SerializeObject(new
                        {
                            type = isAddedRequest ? "added" : "add",
                            profile_id = getProfileid(requestInfo, HttpContext),
                            data = readBody.token
                        });

                        _ = NwsEvents.SendAsync(connectionId, requestInfo.user_uid, "bookmark", edata).ConfigureAwait(false);
                    }
                }
            }

            return JsonSuccess();
        }
        catch
        {
            return JsonFailure();
        }
        finally
        {
            semaphore.Release();
        }
    }
    #endregion

    #region Remove
    [HttpPost]
    [Route("/bookmark/remove")]
    public async Task<ActionResult> Remove(string connectionId)
    {
        if (string.IsNullOrEmpty(requestInfo.user_uid))
            return JsonFailure();

        var readBody = await ReadPayloadAsync();

        if (readBody.payloads.Count == 0)
            return JsonFailure();

        var semaphore = new SemaphorManager(SqlContext.semaphoreKey, TimeSpan.FromSeconds(30));

        try
        {
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return JsonFailure();

            using (var sqlDb = SqlContext.Create())
            {
                string userUid = getUserid(requestInfo, HttpContext);
                var data = LoadBookmarks(sqlDb, userUid);

                bool changed = false;

                foreach (var payload in readBody.payloads)
                {
                    var cardId = payload.ResolveCardId();
                    if (cardId == null)
                        continue;

                    if (payload.Where != null)
                        changed |= RemoveFromCategory(data, payload.Where, cardId);

                    if (payload.Method == "card")
                    {
                        changed |= RemoveIdFromAllCategories(data, cardId);
                        changed |= RemoveCard(data, cardId);
                    }
                }

                if (changed)
                {
                    Save(sqlDb, userUid, data);

                    if (readBody.token != null)
                    {
                        string edata = JsonConvertPool.SerializeObject(new
                        {
                            type = "remove",
                            profile_id = getProfileid(requestInfo, HttpContext),
                            data = readBody.token
                        });

                        _ = NwsEvents.SendAsync(connectionId, requestInfo.user_uid, "bookmark", edata).ConfigureAwait(false);
                    }
                }
            }

            return JsonSuccess();
        }
        catch
        {
            return JsonFailure();
        }
        finally
        {
            semaphore.Release();
        }
    }
    #endregion


    #region Utilities
    static string getUserid(RequestModel requestInfo, HttpContext httpContext)
    {
        string user_id = requestInfo.user_uid;
        string profile_id = getProfileid(requestInfo, httpContext);

        if (!string.IsNullOrEmpty(profile_id))
            return $"{user_id}_{profile_id}";

        return user_id;
    }

    static string getProfileid(RequestModel requestInfo, HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue("profile_id", out var profile_id) && !string.IsNullOrEmpty(profile_id) && profile_id != "0")
            return profile_id;

        return string.Empty;
    }

    JObject GetBookmarksForResponse(SqlContext sqlDb)
    {
        if (string.IsNullOrEmpty(requestInfo.user_uid))
            return CreateDefaultBookmarks();

        return LoadBookmarks(sqlDb, getUserid(requestInfo, HttpContext));
    }

    /// <summary>
    /// Строки → прежний объект Lampa. Контракт веба не изменился, изменилось только хранилище:
    /// порядок внутри категории восстанавливается по метке времени, чем свежее — тем ближе к началу.
    /// </summary>
    static JObject LoadBookmarks(SqlContext sqlDb, string userUid)
    {
        var data = CreateDefaultBookmarks();
        if (string.IsNullOrEmpty(userUid))
            return data;

        var rows = sqlDb.bookmarks.AsNoTracking().Where(i => i.user == userUid).ToList();
        var order = new Dictionary<string, List<(string id, long at)>>(StringComparer.Ordinal);
        var cards = new List<(JObject card, long at)>(rows.Count);

        foreach (var row in rows)
        {
            var owned = ParseObject(row.categories);

            // Пустой объект категорий — надгробие: карточку убрали отовсюду, показывать её нечему.
            if (!owned.HasValues)
                continue;

            long newest = 0;

            foreach (var pair in owned)
            {
                long at = pair.Value?.Value<long>() ?? 0;
                if (at > newest)
                    newest = at;

                if (!order.TryGetValue(pair.Key, out var list))
                    order[pair.Key] = list = new List<(string, long)>();

                list.Add((row.card_id, at));
            }

            if (!string.IsNullOrEmpty(row.card))
            {
                var card = ParseObject(row.card);
                if (card.HasValues)
                    cards.Add((card, newest));
            }
        }

        var cardArray = GetCardArray(data);
        foreach (var card in cards.OrderByDescending(i => i.at))
            cardArray.Add(card.card);

        foreach (var pair in order)
        {
            var array = GetCategoryArray(data, pair.Key);
            foreach (var item in pair.Value.OrderByDescending(i => i.at))
                array.Add(AsId(item.id));
        }

        EnsureDefaultArrays(data);
        return data;
    }

    static JObject ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JObject();

        try { return JObject.Parse(json) ?? new JObject(); }
        catch { return new JObject(); }
    }

    /// <summary>Lampa хранит идентификаторы числами, когда они числовые, — сохраняем это.</summary>
    static JToken AsId(string id)
        => long.TryParse(id, out long numeric) && numeric > 0 ? new JValue(numeric) : new JValue(id);

    static JObject CreateDefaultBookmarks()
    {
        var obj = new JObject
        {
            ["card"] = new JArray()
        };

        foreach (var category in BookmarkCategories)
            obj[category] = new JArray();

        return obj;
    }

    static void EnsureDefaultArrays(JObject root)
    {
        if (root == null)
            return;

        if (root["card"] is not JArray)
            root["card"] = new JArray();

        foreach (var category in BookmarkCategories)
        {
            if (root[category] is not JArray)
                root[category] = new JArray();
        }
    }

    static bool EnsureCard(JObject data, JObject card, string idStr, bool insert = true)
    {
        if (data == null || card == null || string.IsNullOrWhiteSpace(idStr))
            return false;

        var cardArray = GetCardArray(data);
        var newCard = (JObject)card.DeepClone();

        foreach (var existing in cardArray.Children<JObject>().ToList())
        {
            var token = existing["id"];
            if (token != null && token.ToString() == idStr)
            {
                if (!JToken.DeepEquals(existing, newCard))
                {
                    existing.Replace(newCard);
                    return true;
                }

                return false;
            }
        }

        if (insert)
            cardArray.Insert(0, newCard);
        else
            cardArray.Add(newCard);

        return true;
    }

    static bool AddToCategory(JObject data, string category, string idStr)
    {
        var array = GetCategoryArray(data, category);

        foreach (var token in array)
        {
            if (token.ToString() == idStr)
                return false;
        }

        if (long.TryParse(idStr, out long _id) && _id > 0)
            array.Insert(0, _id);
        else
            array.Insert(0, idStr);

        return true;
    }

    static bool MoveIdToFrontInAllCategories(JObject data, string idStr)
    {
        bool changed = false;

        foreach (var prop in data.Properties())
        {
            if (string.Equals(prop.Name, "card", StringComparison.OrdinalIgnoreCase))
                continue;

            if (prop.Value is JArray array)
                changed |= MoveIdToFront(array, idStr);
        }

        return changed;
    }

    static bool MoveIdToFront(JArray array, string idStr)
    {
        if (array == null)
            return false;

        for (int i = 0; i < array.Count; i++)
        {
            var token = array[i];
            if (token?.ToString() == idStr)
            {
                if (i == 0)
                    return false;

                token.Remove();
                array.Insert(0, token);
                return true;
            }
        }

        return false;
    }

    static bool RemoveFromCategory(JObject data, string category, string idStr)
    {
        if (data[category] is not JArray array)
            return false;

        return RemoveFromArray(array, idStr);
    }

    static bool RemoveIdFromAllCategories(JObject data, string idStr)
    {
        bool changed = false;

        foreach (var property in data.Properties().ToList())
        {
            if (property.Name == "card")
                continue;

            if (property.Value is JArray array && RemoveFromArray(array, idStr))
                changed = true;
        }

        return changed;
    }

    static bool RemoveCard(JObject data, string idStr)
    {
        if (data["card"] is JArray cardArray)
        {
            foreach (var card in cardArray.Children<JObject>().ToList())
            {
                var token = card["id"];
                if (token != null && token.ToString() == idStr)
                {
                    card.Remove();
                    return true;
                }
            }
        }

        return false;
    }

    static JArray GetCardArray(JObject data)
    {
        if (data["card"] is JArray array)
            return array;

        array = new JArray();
        data["card"] = array;
        return array;
    }

    static JArray GetCategoryArray(JObject data, string category)
    {
        if (data[category] is JArray array)
            return array;

        array = new JArray();
        data[category] = array;
        return array;
    }

    static bool RemoveFromArray(JArray array, string idStr)
    {
        foreach (var token in array.ToList())
        {
            if (token.ToString() == idStr)
            {
                token.Remove();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Объект Lampa → строки. Пишутся только изменившиеся: курсор дельт должен двигаться ровно
    /// там, где что-то поменялось, иначе одна правка выглядит для клиентов как «поменялось всё».
    ///
    /// Метка времени у членства сохраняется, пока карточка стоит в категории не первой. Новая
    /// выдаётся вновь добавленным и тем, кто переехал в начало, — а это единственная перестановка,
    /// которую Lampa делает (<c>MoveIdToFrontInAllCategories</c>).
    /// </summary>
    static void Save(SqlContext sqlDb, string userUid, JObject data)
    {
        if (string.IsNullOrEmpty(userUid))
            return;

        var rows = sqlDb.bookmarks.Where(i => i.user == userUid).ToList();
        var byId = rows.ToDictionary(i => i.card_id, StringComparer.Ordinal);
        long stamp = NextStamp(sqlDb, userUid);

        #region желаемое состояние
        var cards = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var card in GetCardArray(data).Children<JObject>())
        {
            string id = card["id"]?.ToString();
            if (!string.IsNullOrEmpty(id))
                cards[id] = card.ToString(Formatting.None);
        }

        // Метка членства — ключ порядка, а не время. Голову категории надо знать заранее: «стоит
        // первым» и «только что переехал в начало» различаются только этим.
        var front = new Dictionary<string, long>(StringComparer.Ordinal);
        var stored = new Dictionary<string, JObject>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var owned = ParseObject(row.categories);
            stored[row.card_id] = owned;

            foreach (var pair in owned)
            {
                long at = pair.Value?.Value<long>() ?? 0;
                if (!front.TryGetValue(pair.Key, out long top) || at > top)
                    front[pair.Key] = at;
            }
        }

        var wanted = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (string category in BookmarkCategories)
        {
            if (data[category] is not JArray array)
                continue;

            for (int i = 0; i < array.Count; i++)
            {
                string id = array[i]?.ToString();
                if (string.IsNullOrEmpty(id))
                    continue;

                if (!wanted.TryGetValue(id, out var owned))
                    wanted[id] = owned = new JObject();

                long kept = 0;
                if (stored.TryGetValue(id, out var existing))
                {
                    long at = existing[category]?.Value<long>() ?? 0;

                    // Уже стоял в этой категории — метку сохраняем, иначе курсор двигали бы
                    // строки, которых никто не трогал. Новая нужна только тому, кто реально
                    // переехал в начало: он теперь первый, а раньше первым был не он.
                    if (at > 0 && (i > 0 || at >= front.GetValueOrDefault(category)))
                        kept = at;
                }

                // Новому нужен ключ выше всех сохранённых, поэтому отсчёт идёт вверх от текущего
                // курсора, а не вниз: иначе длинный список утопил бы хвост ниже старых меток.
                owned[category] = kept > 0 ? kept : stamp + (array.Count - i);
            }
        }
        #endregion

        int changed = 0;

        foreach (var pair in wanted)
        {
            string categories = pair.Value.ToString(Formatting.None);
            cards.TryGetValue(pair.Key, out string card);

            if (byId.TryGetValue(pair.Key, out var row))
            {
                if (row.categories == categories && (card == null || row.card == card))
                    continue;

                row.categories = categories;
                if (card != null)
                    row.card = card;
                row.changed_at = stamp;
                row.updated_at = stamp;
                sqlDb.bookmarks.Update(row);
            }
            else
            {
                sqlDb.bookmarks.Add(new SyncUserBookmarkSqlModel
                {
                    user = userUid,
                    card_id = pair.Key,
                    card = card,
                    categories = categories,
                    changed_at = stamp,
                    updated_at = stamp
                });
            }

            changed++;
        }

        // Пропавшее из объекта — это удаление. Строка остаётся пустым надгробием, иначе следующая
        // сверка не увидит разницы и вернёт карточку всем клиентам назад.
        foreach (var row in rows)
        {
            if (wanted.ContainsKey(row.card_id) || row.categories == "{}")
                continue;

            row.categories = "{}";
            row.changed_at = stamp;
            row.updated_at = stamp;
            sqlDb.bookmarks.Update(row);
            changed++;
        }

        if (changed > 0)
            sqlDb.SaveChanges();
    }

    /// <summary>Курсор обязан строго возрастать: две строки в одну миллисекунду слились бы в одну точку.</summary>
    static long NextStamp(SqlContext sqlDb, string userUid)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long last = sqlDb.bookmarks.AsNoTracking()
            .Where(i => i.user == userUid)
            .Max(i => (long?)i.updated_at) ?? 0;

        return now > last ? now : last + 1;
    }

    JsonResult JsonSuccess() => Json(new { success = true });

    ActionResult JsonFailure(string message = null) => ContentTo(JsonConvertPool.SerializeObject(new { success = false, message }));

    async Task<(IReadOnlyList<EventPayload> payloads, JToken token)> ReadPayloadAsync()
    {
        JToken token = null;
        var payloads = new List<EventPayload>();

        using (var reader = new StreamReader(Request.Body, Encoding.UTF8, false, PoolInvk.bufferSizeStreamReader, leaveOpen: true))
        {
            try
            {
                string json = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return (payloads, token);

                token = JsonConvert.DeserializeObject<JToken>(json);
                if (token == null)
                    return (payloads, token);

                if (token.Type == JTokenType.Array)
                {
                    foreach (var obj in token.Children<JObject>())
                        payloads.Add(ParsePayload(obj));
                }
                else if (token is JObject job)
                {
                    payloads.Add(ParsePayload(job));
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "BookmarkController", "id_u9kme0pp");
            }
        }

        return (payloads, token);
    }

    static EventPayload ParsePayload(JObject job)
    {
        var payload = new EventPayload
        {
            Method = job.Value<string>("method"),
            CardIdRaw = job.Value<string>("id") ?? job.Value<string>("card_id")
        };

        payload.Where = (job.Value<string>("where") ?? job.Value<string>("list"))?.ToLowerAndTrim();
        if (string.IsNullOrEmpty(payload.Where) || payload.Where == "card")
            payload.Where = null;

        if (job.TryGetValue("card", out var cardToken) && cardToken is JObject cardObj)
            payload.Card = cardObj;

        return payload;
    }
    #endregion
}
