using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared;
using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Shared.Services.Utilities;
using Shared.Services;
using Shared.Attributes;
using Shared.Models.Module;

namespace Core.Controllers;

public class ApiController : BaseController
{
    #region Version / Headers / geo / myip
    const string versionName = "Lord of Rings";
    static readonly string versionHash = CrypTo.md5File("Shared.dll");
    static readonly string buildInfo = System.Text.Json.JsonSerializer.Serialize(GetBuildInfo());

    static object GetBuildInfo()
    {
        var asm = typeof(ApiController).Assembly;
        var version = (asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+', 2);
        string meta(string key)
        {
            string value = asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return new
        {
            name = versionName,
            version = version[0],
            commit = version.Length > 1 ? version[1] : null,
            dirty = meta("BuildDirty") is string dirty ? dirty == "true" : (bool?)null,
            @ref = meta("BuildRef"),
            repository = meta("BuildRepository"),
            runUrl = meta("BuildRunUrl"),
            buildDate = meta("BuildDate"),
            configuration = meta("BuildConfiguration"),
            sdk = meta("BuildSdk"),
            buildOs = meta("BuildOs"),
            buildArch = meta("BuildArch"),
            docker = System.IO.File.Exists("isdocker"),
            hash = versionHash,
            runtime = RuntimeInformation.FrameworkDescription,
            rid = RuntimeInformation.RuntimeIdentifier
        };
    }

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
                return Content(versionName, "text/plain; charset=utf-8");

            if (type == "build")
                return Content(buildInfo, "application/json; charset=utf-8");

            return Redirect("https://youtu.be/7lwJOxN_gXc");
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

    #region Capabilities
    /// <summary>
    /// Что этот сервер умеет для нативного клиента. Возможности заявляют сами модули
    /// (<see cref="ModuleCapabilities"/>), поэтому выключенный модуль тут не появится.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [Route("/api/capabilities")]
    public ActionResult Capabilities()
    {
        SetHeadersNoCache();

        return Json(new
        {
            lampac = true,
            version = versionHash,
            // Область данных, в которую попадёт этот клиент. null = сервер не принял
            // идентичность, и включать синхронизацию нельзя: писать будет некуда.
            uid = requestInfo.user_uid,
            // Идентичность для клиента, у которого своей нет: так телефон и приставка попадают
            // в одну область. null там, где accsdb уже раздал каждому свою.
            assignedUid = InstanceIdentity.Assigned,
            features = ModuleCapabilities.All
        });
    }
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

        if (string.IsNullOrEmpty(src) || !Uri.TryCreate(src, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return BadRequest("invalid src: must be absolute http(s) URL");

        var safeSrc = WebUtility.HtmlEncode(src);

        return ContentTo($@"<html lang=""ru"">
                <head>
                    <meta charset=""UTF-8"">
                    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
                    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
                    <title>chromium iframe</title>
                </head>
                <body>
                    <iframe width=""560"" height=""400"" src=""{safeSrc}"" frameborder=""0"" allow=""*"" allowfullscreen></iframe>
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
