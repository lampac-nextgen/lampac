using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using System.IO;
using System.Text;

namespace WebLog
{
    [Authorization(redirectUri: "/weblog/auth")]
    public class ApiController : BaseController
    {
        [HttpGet]
        [AllowAnonymous]
        [Route("/weblog/auth")]
        public ActionResult WebLogAuth()
        {
            var path = Path.Combine(ModInit.modpath, "auth.html");
            var html = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return Content(html, "text/html; charset=utf-8");
        }

        [HttpGet]
        [Route("/weblog")]
        public ActionResult WebLog()
        {
            var path = Path.Combine(ModInit.modpath, "index.html");
            var html = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return Content(html, "text/html; charset=utf-8");
        }
    }
}
