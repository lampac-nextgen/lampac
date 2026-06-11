using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Services;

namespace Potok;

public class PotokController : BaseController
{
    [HttpGet, AllowAnonymous]
    [Staticache(5, always: true, setHeadersNoCache: true)]
    [Route("blue-oyster/{file}")]
    [Route("naked-gun/{file}")]
    public ActionResult Online(string file)
    {
        string plugin = HttpContext.Request.Path.Value.StartsWith("/naked-gun/")
            ? FileCache.ReadAllText($"{ModInit.modpath}/the-naked-gun/{file}", $"naked-gun_{file}", saveCache: false)
            : FileCache.ReadAllText($"{ModInit.modpath}/the-blue-oyster/{file}", $"blue-oyster_{file}", saveCache: false);

        string ct = file.EndsWith(".json")
            ? "application/json; charset=utf-8"
            : "application/javascript; charset=utf-8";

        return ContentTo(plugin.Replace("{localhost}", host), ct);
    }
}
