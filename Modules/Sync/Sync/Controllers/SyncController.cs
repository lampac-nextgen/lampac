using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Services;
using System.Web;

namespace Sync;

public class SyncController : BaseController
{
    #region sync.js
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("sync.js")]
    [Route("sync/js/{token}")]
    public ActionResult SyncJS(string token, bool lite)
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/sync_v2/sync.js", "sync.js")
            .Replace("{sync-invc}", FileCache.ReadAllText($"{ModInit.modpath}/plugins/sync-invc.js", "sync-invc.js"))
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion

    #region invc-ws.js
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("invc-ws.js")]
    [Route("invc-ws/js/{token}")]
    public ActionResult InvcSyncJS(string token)
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/sync_v2/invc-ws.js", "invc-ws.js")
            .Replace("{invc-rch_nws}", FileCache.ReadAllText("plugins/invc-rch_nws.js", "invc-rch_nws.js"))
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion
}
