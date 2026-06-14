using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
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
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion

    [HttpGet]
    [Route("/timecode/all")]
    async public Task<ActionResult> Get(string card_id)
    {
        if (string.IsNullOrEmpty(card_id) || requestInfo.user_uid == null)
            return Json(new { });

        string userId = getUserid(requestInfo);

        Dictionary<string, string> timecodes = null;

        using (var sqlDb = SqlContext.Create())
        {
            timecodes = await sqlDb.timecodes
                .AsNoTracking()
                .Where(i => i.user == userId && i.card == card_id)
                .ToDictionaryAsync(i => i.item, i => i.data);
        }

        if (timecodes == null || timecodes.Count == 0)
            return Json(new { });

        return Json(timecodes);
    }

    [HttpPost]
    [Route("/timecode/add")]
    async public Task<ActionResult> Set([FromQuery] string card_id, [FromForm] string id, [FromForm] string data)
    {
        if (string.IsNullOrEmpty(card_id) ||
            string.IsNullOrEmpty(id) ||
            string.IsNullOrEmpty(data) ||
            requestInfo.user_uid == null)
            return ContentTo("{\"success\": false}");

        bool success = false;
        var semaphore = new SemaphorManager(SqlContext.semaphoreKey, TimeSpan.FromSeconds(20));

        try
        {
            string userId = getUserid(requestInfo);

            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return StatusCode(502, "semaphore");

            using (var sqlDb = SqlContext.Create())
            {
                sqlDb.timecodes
                    .Where(i => i.user == userId && i.card == card_id && i.item == id)
                    .ExecuteDelete();

                sqlDb.timecodes.Add(new SqlModel
                {
                    user = userId,
                    card = card_id,
                    item = id,
                    data = data,
                    updated = DateTime.UtcNow
                });

                success = await sqlDb.SaveChangesAsync() > 0;
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "ApiController", "id_oqjny2lw");
        }
        finally
        {
            semaphore.Release();
        }

        return ContentTo($"{{\"success\": {(success ? "true" : "false")}}}");
    }


    string getUserid(RequestModel requestInfo)
    {
        string user_id = requestInfo.user_uid;

        if (HttpContext.Request.Query.TryGetValue("profile_id", out var profile_id) && !string.IsNullOrEmpty(profile_id) && profile_id != "0")
            user_id = $"{user_id}_{profile_id}";

        return Regex.Replace(user_id, "[^a-z0-9\\-_\\.]+", "", RegexOptions.IgnoreCase);
    }
}
