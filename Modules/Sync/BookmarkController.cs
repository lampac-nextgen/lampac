using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Models.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Shared.Services.Pools.Json;

namespace Sync.Controllers
{
    public class BookmarkController : BaseController
    {
        #region bookmark.js
        [HttpGet]
        [AllowAnonymous]
        [Route("bookmark.js")]
        [Route("bookmark/js/{token}")]
        public ActionResult BookmarkJS(string token)
        {
            SetHeadersNoCache();

            string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/bookmark.js", "bookmark.js")
                .Replace("{localhost}", host)
                .Replace("{token}", HttpUtility.UrlEncode(token));

            return Content(plugin, "application/javascript; charset=utf-8");
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

            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, false, PoolInvk.bufferSize, leaveOpen: true))
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

                        var (entity, data) = LoadBookmarks(sqlDb, userUid, createIfMissing: true);

                        foreach (var job in jobs)
                        {
                            string where = job.Value<string>("where")?.ToLowerAndTrim();
                            if (string.IsNullOrWhiteSpace(where))
                                return JsonFailure();

                            if (IsDbInitialization /*&& AppInit.conf.sync_user.fullset == false*/)
                            {
                                if (where == "card" || BookmarkCategories.Contains(where))
                                    return JsonFailure("enable sync_user.fullset in init.conf");
                            }

                            if (!job.TryGetValue("data", out var dataValue))
                                return JsonFailure();

                            data[where] = dataValue;
                        }

                        EnsureDefaultArrays(data);

                        Save(sqlDb, entity, data);
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
                    _ = SyncNwsEvents.PublishAsync(connectionId, requestInfo.user_uid, "bookmark", JsonConvertPool.SerializeObject(new
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
                    var (entity, data) = LoadBookmarks(sqlDb, getUserid(requestInfo, HttpContext), createIfMissing: true);
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
                        Save(sqlDb, entity, data);

                        if (readBody.token != null)
                        {
                            string edata = JsonConvertPool.SerializeObject(new
                            {
                                type = isAddedRequest ? "added" : "add",
                                profile_id = getProfileid(requestInfo, HttpContext),
                                data = readBody.token
                            });

                            _ = SyncNwsEvents.PublishAsync(connectionId, requestInfo.user_uid, "bookmark", edata).ConfigureAwait(false);
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
                    var (entity, data) = LoadBookmarks(sqlDb, getUserid(requestInfo, HttpContext), createIfMissing: false);
                    if (entity == null)
                        return JsonSuccess();

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
                        Save(sqlDb, entity, data);

                        if (readBody.token != null)
                        {
                            string edata = JsonConvertPool.SerializeObject(new
                            {
                                type = "remove",
                                profile_id = getProfileid(requestInfo, HttpContext),
                                data = readBody.token
                            });

                            _ = SyncNwsEvents.PublishAsync(connectionId, requestInfo.user_uid, "bookmark", edata).ConfigureAwait(false);
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


        #region static
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

            string user_id = getUserid(requestInfo, HttpContext);
            var entity = sqlDb.bookmarks.AsNoTracking().FirstOrDefault(i => i.user == user_id);
            var data = entity != null ? DeserializeBookmarks(entity.data) : CreateDefaultBookmarks();
            EnsureDefaultArrays(data);
            return data;
        }

        static (SyncUserBookmarkSqlModel entity, JObject data) LoadBookmarks(SqlContext sqlDb, string userUid, bool createIfMissing)
        {
            JObject data = CreateDefaultBookmarks();
            SyncUserBookmarkSqlModel entity = null;

            if (!string.IsNullOrEmpty(userUid))
            {
                entity = sqlDb.bookmarks.FirstOrDefault(i => i.user == userUid);
                if (entity != null && !string.IsNullOrEmpty(entity.data))
                    data = DeserializeBookmarks(entity.data);
            }

            EnsureDefaultArrays(data);

            if (entity == null && createIfMissing && !string.IsNullOrEmpty(userUid))
                entity = new SyncUserBookmarkSqlModel { user = userUid };

            return (entity, data);
        }

        static JObject DeserializeBookmarks(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return CreateDefaultBookmarks();

            try
            {
                var job = JsonConvert.DeserializeObject<JObject>(json) ?? new JObject();
                EnsureDefaultArrays(job);
                return job;
            }
            catch
            {
                return CreateDefaultBookmarks();
            }
        }

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

        static void Save(SqlContext sqlDb, SyncUserBookmarkSqlModel entity, JObject data)
        {
            if (entity == null)
                return;

            entity.data = data.ToString(Formatting.None);
            entity.updated = DateTime.UtcNow;

            if (entity.Id == 0)
                sqlDb.bookmarks.Add(entity);
            else
                sqlDb.bookmarks.Update(entity);

            sqlDb.SaveChanges();
        }

        JsonResult JsonSuccess() => Json(new { success = true });

        ActionResult JsonFailure(string message = null) => ContentTo(JsonConvertPool.SerializeObject(new { success = false, message }));

        async Task<(IReadOnlyList<BookmarkEventPayload> payloads, JToken token)> ReadPayloadAsync()
        {
            JToken token = null;
            var payloads = new List<BookmarkEventPayload>();

            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, false, PoolInvk.bufferSize, leaveOpen: true))
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

        static BookmarkEventPayload ParsePayload(JObject job)
        {
            var payload = new BookmarkEventPayload
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

        #region BookmarkEventPayload
        sealed class BookmarkEventPayload
        {
            public string Method { get; set; }

            public string Where { get; set; }

            public JObject Card { get; set; }

            public string CardIdRaw { get; set; }

            public string ResolveCardId()
            {
                if (!string.IsNullOrWhiteSpace(CardIdRaw))
                    return CardIdRaw.ToLowerAndTrim();

                var token = Card?["id"];
                if (token != null)
                {
                    if (token.Type == JTokenType.Integer)
                        return token.Value<long>().ToString();

                    string _id = token.ToString();
                    if (string.IsNullOrWhiteSpace(_id))
                        return null;

                    return _id.ToLowerAndTrim();
                }

                return null;
            }
        }
        #endregion
    }
}
