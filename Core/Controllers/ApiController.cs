using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared;
using System;
using System.Linq;
using Shared.Services.Utilities;
using Shared.Services;
using Shared.Attributes;

namespace Core.Controllers;

public class ApiController : BaseController
{
    #region Version / Headers / geo / myip
    static readonly string versionHash = CrypTo.md5File("Shared.dll");

    [HttpGet]
    [AllowAnonymous]
    [Route("/version")]
    public ActionResult Version(string type)
    {
        if (CoreInit.conf.listen.version)
        {
            if (type == "hash")
                return Content(versionHash, "text/plain; charset=utf-8");

            if (type == "name")
                return Content("Desert Rose", "text/plain; charset=utf-8");

            return Redirect("https://youtu.be/C3lWwBslWqg");
        }

        return StatusCode(404);
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/api/headers")]
    public ActionResult Headers(string type)
    {
        if (type == "text")
        {
            return Content(string.Join(
                Environment.NewLine,
                HttpContext.Request.Headers.Select(h => $"{h.Key}: {h.Value}")
            ));
        }

        return Json(HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/api/geo")]
    public ActionResult Geo(string select, string ip)
    {
        if (select == "ip")
            return Content(ip ?? requestInfo.IP);

        string country = ip != null
            ? GeoIP2.Country(ip)
            : requestInfo.Country;

        if (select == "country")
            return Content(country);

        return Json(new
        {
            ip = ip ?? requestInfo.IP,
            country
        });
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/api/myip")]
    public ActionResult MyIP() => Content(requestInfo.IP);
    #endregion

    #region Chromium
    [HttpGet]
    [AllowAnonymous]
    [Route("/api/chromium/ping")]
    public string Ping() => "pong";


    [HttpGet]
    [AllowAnonymous]
    [Route("/api/chromium/iframe")]
    public ActionResult RenderIframe(string src)
    {
        SetHeadersNoCache();

        return ContentTo($@"<html lang=""ru"">
                <head>
                    <meta charset=""UTF-8"">
                    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
                    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
                    <title>chromium iframe</title>
                </head>
                <body>
                    <iframe width=""560"" height=""400"" src=""{src}"" frameborder=""0"" allow=""*"" allowfullscreen></iframe>
                </body>
            </html>");
    }
    #endregion

    #region nws-client-es5.js
    [HttpGet, AllowAnonymous]
    [Staticache(10, always: true, setHeadersNoCache: true)]
    [Route("nws-client-es5.js")]
    [Route("js/nws-client-es5.js")]
    public ActionResult NwsClient()
    {
        string source = FileCache.ReadAllText("plugins/nws-client-es5.js", "nws-client-es5.js", saveCache: false)
            .Replace("{localhost}", host);

        return ContentTo(source, "application/javascript; charset=utf-8");
    }
    #endregion
}
