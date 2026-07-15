using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Services;
using System;
using System.Collections.Generic;

namespace Potok;

public class PotokController : BaseController
{
    static readonly HashSet<string> BlueOysterFiles = new(StringComparer.Ordinal)
    {
        "index.js",
        "manifest.json"
    };

    static readonly HashSet<string> NakedGunFiles = new(StringComparer.Ordinal)
    {
        "actions.js",
        "api.js",
        "index.js",
        "manifest.json",
        "media.js",
        "state.js",
        "stream-source.js",
        "ui.js"
    };

    [HttpGet, AllowAnonymous]
    [Staticache(5, always: true, setHeadersNoCache: true)]
    [Route("blue-oyster/{file}")]
    [Route("naked-gun/{file}")]
    public ActionResult Online(string file)
    {
        bool nakedGun = HttpContext.Request.Path.StartsWithSegments("/naked-gun");
        HashSet<string> allowedFiles = nakedGun ? NakedGunFiles : BlueOysterFiles;

        if (!allowedFiles.Contains(file))
            return NotFound();

        string plugin = nakedGun
            ? FileCache.ReadAllText($"{ModInit.modpath}/the-naked-gun/{file}", $"naked-gun_{file}", saveCache: false)
            : FileCache.ReadAllText($"{ModInit.modpath}/the-blue-oyster/{file}", $"blue-oyster_{file}", saveCache: false);

        string ct = file.EndsWith(".json")
            ? "application/json; charset=utf-8"
            : "application/javascript; charset=utf-8";

        return ContentTo(plugin.Replace("{localhost}", host), ct);
    }
}
