using LampaWeb.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Events;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using IO = System.IO;

namespace LampaWeb.Controllers
{
    public class ApiController : BaseController
    {
        [HttpGet]
        [AllowAnonymous]
        [Route("/personal.lampa")]
        [Route("/lampa-main/personal.lampa")]
        [Route("/{myfolder}/personal.lampa")]
        public ActionResult PersonalLampa(string myfolder) => StatusCode(200);


        [HttpGet]
        [AllowAnonymous]
        [Route("/reqinfo")]
        public ActionResult Reqinfo() => ContentTo(JsonConvert.SerializeObject(requestInfo, new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        }));


        #region Index
        [HttpGet]
        [AllowAnonymous]
        [Route("/")]
        public ActionResult Index()
        {
            if (string.IsNullOrWhiteSpace(ModInit.conf.index))
                return Content("api work", contentType: "text/plain; charset=utf-8");

            if (ModInit.conf.basetag && Regex.IsMatch(ModInit.conf.index, "/[^\\./]+\\.html$"))
            {
                if (!memoryCache.TryGetValue($"LampaWeb.index:{ModInit.conf.index}", out string html))
                {
                    html = IO.File.ReadAllText($"wwwroot/{ModInit.conf.index}");
                    html = html.Replace("<head>", $"<head><base href=\"/{Regex.Match(ModInit.conf.index, "^([^/]+)/").Groups[1].Value}/\" />");

                    memoryCache.Set($"LampaWeb.index:{ModInit.conf.index}", html, DateTime.Now.AddMinutes(1));
                }

                return Content(html, "text/html; charset=utf-8");
            }

            return LocalRedirect($"/{ModInit.conf.index}");
        }
        #endregion

        #region Extensions
        [HttpGet]
        [AllowAnonymous]
        [Route("/extensions")]
        public ActionResult Extensions()
        {
            SetHeadersNoCache();

            string extensions = FileCache.ReadAllText($"{ModInit.modpath}/plugins/extensions.json", "extensions.json")
                .Replace("{localhost}", host)
                .Replace("\n", "")
                .Replace("\r", "");

            return ContentTo(extensions);
        }
        #endregion

        #region testaccsdb
        [HttpGet]
        [HttpPost]
        [AllowAnonymous]
        [Route("/testaccsdb")]
        public ActionResult TestAccsdb(string account_email, string uid)
        {
            // uid совпадает с shared_passwd, возвращаем команду изменить uid
            if (!string.IsNullOrEmpty(CoreInit.conf.accsdb.shared_passwd) && uid == CoreInit.conf.accsdb.shared_passwd)
                return ContentTo("{\"accsdb\": true, \"newuid\": true}");

            #region shared_passwd
            if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(account_email) && account_email == CoreInit.conf.accsdb.shared_passwd)
            {
                try
                {
                    string file = "users.json";

                    JArray arr = new JArray();

                    if (IO.File.Exists(file))
                    {
                        var txt = IO.File.ReadAllText(file);
                        if (!string.IsNullOrWhiteSpace(txt))
                            try { arr = JArray.Parse(txt); } catch { arr = new JArray(); }
                    }

                    bool exists = arr.Children<JObject>().Any(o =>
                        (o.Value<string>("id") != null && o.Value<string>("id").Equals(uid, StringComparison.OrdinalIgnoreCase)) ||
                        (o["ids"] != null && o["ids"].Any(t => t.ToString().Equals(uid, StringComparison.OrdinalIgnoreCase)))
                    );

                    if (exists)
                        return ContentTo("{\"accsdb\": false}");

                    var obj = new JObject();
                    obj["id"] = uid;
                    obj["expires"] = DateTime.Now.AddDays(Math.Max(1, CoreInit.conf.accsdb.shared_daytime));

                    arr.Add(obj);

                    IO.File.WriteAllText(file, arr.ToString(Formatting.Indented));

                    return ContentTo("{\"accsdb\": false, \"success\": true, \"uid\": \"" + uid + "\"}");
                }
                catch { return ContentTo("{\"accsdb\": true}"); }
            }
            #endregion

            if (CoreInit.conf.accsdb.enable && requestInfo.user == null)
                return ContentTo("{\"accsdb\": true}");

            return ContentTo("{\"accsdb\": false, \"success\": true}");
        }
        #endregion


        #region app.min.js
        [HttpGet]
        [AllowAnonymous]
        [Route("/app.min.js")]
        [Route("{type}/app.min.js")]
        public ContentResult LampaApp(string type)
        {
            SetHeadersNoCache();

            if (string.IsNullOrEmpty(type))
            {
                if (ModInit.conf.path != null)
                {
                    type = ModInit.conf.path;
                }
                else
                {
                    if (ModInit.conf.index == null || !ModInit.conf.index.Contains("/"))
                        return Content(string.Empty, "application/javascript; charset=utf-8");

                    type = ModInit.conf.index.Split("/")[0];
                }
            }
            else
            {
                type = Regex.Replace(type, "[^a-z0-9\\-]", "");
            }

            string memKey = $"ApiController:{type}:{host}:app.min.js";
            if (!memoryCache.TryGetValue(memKey, out string file))
            {
                file = IO.File.ReadAllText($"wwwroot/{type}/app.min.js");

                #region appReplace
                if (ModInit.conf.appReplace != null)
                {
                    foreach (var r in ModInit.conf.appReplace)
                    {
                        string val = r.Value;
                        if (val.StartsWith("file:"))
                            val = IO.File.ReadAllText(val.Substring(5));

                        val = val.Replace("{localhost}", host).Replace("{host}", Regex.Replace(host, "^https?://", ""));
                        file = Regex.Replace(file, r.Key, val, RegexOptions.IgnoreCase);
                    }
                }
                #endregion

                var bulder = new StringBuilder(file);

                if (ModInit.conf.initPlugins.cubProxy)
                {
                    bulder = bulder.Replace("protocol + mirror + '/api/checker'", $"'{host}/cub/api/checker'");
                    bulder = bulder.Replace("Utils$1.protocol() + 'tmdb.' + object$2.cub_domain + '/' + u,", $"'{host}/cub/tmdb./' + u,");
                    bulder = bulder.Replace("Utils$1.protocol() + object$2.cub_domain", $"'{host}/cub/red'");
                    bulder = bulder.Replace("object$2.cub_domain", $"'{CoreInit.conf.cub.mirror}'");
                }

                bulder = bulder.Replace("http://lite.lampa.mx", $"{host}/{type}");
                bulder = bulder.Replace("https://yumata.github.io/lampa-lite", $"{host}/{type}");

                bulder = bulder.Replace("http://lampa.mx", $"{host}/{type}");
                bulder = bulder.Replace("https://yumata.github.io/lampa", $"{host}/{type}");

                bulder = bulder.Replace("window.lampa_settings.dcma = dcma;", "window.lampa_settings.fixdcma = true;");
                bulder = bulder.Replace("Storage.get('vpn_checked_ready', 'false')", "true");

                bulder = bulder.Replace("status$1 = false;", "status$1 = true;"); // local apk to personal.lampa
                bulder = bulder.Replace("return status$1;", "return true;"); // отключение рекламы
                bulder = bulder.Replace("if (!Storage.get('metric_uid', ''))", "return;"); // metric
                bulder = bulder.Replace("function log(data) {", "function log(data) { return;");
                bulder = bulder.Replace("function stat$1(method, name) {", "function stat$1(method, name) { return;");
                bulder = bulder.Replace("if (domain) {", "if (false) {");

                bulder = bulder.Replace("{localhost}", host);

                file = bulder.ToString();

                if (EventListener.AppReplace != null)
                {
                    foreach (Func<string, EventAppReplace, string> handler in EventListener.AppReplace.GetInvocationList())
                        file = handler.Invoke("appjs", new EventAppReplace(file, null, type, host, requestInfo, HttpContext.Request));
                }

                memoryCache.Set(memKey, file, DateTime.Now.AddMinutes(5));
            }

            return Content(file, "application/javascript; charset=utf-8");
        }
        #endregion

        #region app.css
        [HttpGet]
        [AllowAnonymous]
        [Route("/css/app.css")]
        [Route("{type}/css/app.css")]
        public ContentResult LampaAppCss(string type)
        {
            SetHeadersNoCache();

            if (string.IsNullOrEmpty(type))
            {
                if (ModInit.conf.path != null)
                {
                    type = ModInit.conf.path;
                }
                else
                {
                    if (ModInit.conf.index == null || !ModInit.conf.index.Contains("/"))
                        return Content(string.Empty, "application/javascript; charset=utf-8");

                    type = ModInit.conf.index.Split("/")[0];
                }
            }
            else
            {
                type = Regex.Replace(type, "[^a-z0-9\\-]", "");
            }


            string memKey = $"ApiController:css/app.css:{type}:{host}";
            if (!memoryCache.TryGetValue(memKey, out string css))
            {
                css = IO.File.ReadAllText($"wwwroot/{type}/css/app.css");

                if (ModInit.conf.cssReplace != null)
                {
                    foreach (var r in ModInit.conf.cssReplace)
                    {
                        string val = r.Value;
                        if (val.StartsWith("file:"))
                            val = IO.File.ReadAllText(val.Substring(5));

                        val = val.Replace("{localhost}", host).Replace("{host}", Regex.Replace(host, "^https?://", ""));
                        css = Regex.Replace(css, r.Key, val, RegexOptions.IgnoreCase);
                    }
                }

                if (EventListener.AppReplace != null)
                {
                    foreach (Func<string, EventAppReplace, string> handler in EventListener.AppReplace.GetInvocationList())
                        css = handler.Invoke("appcss", new EventAppReplace(css, null, type, host, requestInfo, HttpContext.Request));
                }

                memoryCache.Set(memKey, css, DateTime.Now.AddMinutes(5));
            }

            return Content(css, "text/css; charset=utf-8");
        }
        #endregion


        #region MSX
        [HttpGet]
        [AllowAnonymous]
        [Route("msx/start.json")]
        public ActionResult MSX()
        {
            SetHeadersNoCache();

            string msx = FileCache.ReadAllText($"{ModInit.modpath}/msx.json", "msx.json")
                .Replace("{localhost}", host);

            return ContentTo(msx);
        }
        #endregion

        #region Widgets
        [HttpGet]
        [AllowAnonymous]
        [Route("samsung.wgt")]
        public ActionResult SamsWgt(string overwritehost)
        {
            string wgt = $"data/{CrypTo.md5(overwritehost ?? host + "v3")}.wgt";
            if (IO.File.Exists(wgt))
                return File(IO.File.OpenRead(wgt), "application/octet-stream");

            var widgetDirectory = $"{ModInit.modpath}/widgets/samsung";

            string index = IO.File.ReadAllText($"{widgetDirectory}/index.html");
            IO.File.WriteAllText($"{widgetDirectory}/publish/index.html", index.Replace("{localhost}", overwritehost ?? host));

            string loader = IO.File.ReadAllText($"{widgetDirectory}/loader.js");
            IO.File.WriteAllText($"{widgetDirectory}/publish/loader.js", loader.Replace("{localhost}", overwritehost ?? host));
            string app = IO.File.ReadAllText($"{widgetDirectory}/app.js");
            IO.File.WriteAllText($"{widgetDirectory}/publish/app.js", app.Replace("{localhost}", overwritehost ?? host));

            IO.File.Copy($"{widgetDirectory}/icon.png", $"{widgetDirectory}/publish/icon.png", overwrite: true);
            IO.File.Copy($"{widgetDirectory}/logo_appname_fg.png", $"{widgetDirectory}/publish/logo_appname_fg.png", overwrite: true);
            IO.File.Copy($"{widgetDirectory}/config.xml", $"{widgetDirectory}/publish/config.xml", overwrite: true);

            string gethash(string file)
            {
                using (SHA512 sha = SHA512.Create())
                {
                    return Convert.ToBase64String(sha.ComputeHash(IO.File.ReadAllBytes(file)));
                }
            }

            string indexhashsha512 = gethash($"{widgetDirectory}/publish/index.html");
            string loaderhashsha512 = gethash($"{widgetDirectory}/publish/loader.js");
            string apphashsha512 = gethash($"{widgetDirectory}/publish/app.js");
            string confighashsha512 = gethash($"{widgetDirectory}/publish/config.xml");
            string iconhashsha512 = gethash($"{widgetDirectory}/publish/icon.png");
            string logohashsha512 = gethash($"{widgetDirectory}/publish/logo_appname_fg.png");

            string author_sigxml = IO.File.ReadAllText($"{widgetDirectory}/author-signature.xml");
            author_sigxml = author_sigxml
                .Replace("loaderhashsha512", loaderhashsha512)
                .Replace("apphashsha512", apphashsha512)
                .Replace("iconhashsha512", iconhashsha512)
                .Replace("logohashsha512", logohashsha512)
                .Replace("confighashsha512", confighashsha512)
                .Replace("indexhashsha512", indexhashsha512);

            IO.File.WriteAllText($"{widgetDirectory}/publish/author-signature.xml", author_sigxml);

            string authorsignaturehashsha512 = gethash($"{widgetDirectory}/publish/author-signature.xml");
            string sigxml1 = IO.File.ReadAllText($"{widgetDirectory}/signature1.xml");
            sigxml1 = sigxml1.Replace("loaderhashsha512", loaderhashsha512).Replace("apphashsha512", apphashsha512)
                             .Replace("confighashsha512", confighashsha512).Replace("authorsignaturehashsha512", authorsignaturehashsha512)
                             .Replace("iconhashsha512", iconhashsha512).Replace("logohashsha512", logohashsha512).Replace("indexhashsha512", indexhashsha512);
            IO.File.WriteAllText($"{widgetDirectory}/publish/signature1.xml", sigxml1);
            ZipFile.CreateFromDirectory($"{widgetDirectory}/publish/", wgt);

            return File(IO.File.OpenRead(wgt), "application/octet-stream");
        }

        #endregion

        #region lampainit.js
        [HttpGet]
        [AllowAnonymous]
        [Route("lampainit.js")]
        public ActionResult LamInit()
        {
            SetHeadersNoCache();

            var sb = new StringBuilder(FileCache.ReadAllText($"{ModInit.modpath}/plugins/lampainit.js", "lampainit.js"));

            #region plugins
            List<LampaPlugin> plugins = new();

            if (ModInit.conf.initPlugins.dlna)
                plugins.Add(new("{localhost}/dlna.js", 1, "DLNA", "lampac"));

            if (ModInit.conf.initPlugins.tracks)
                plugins.Add(new("{localhost}/tracks.js", 1, "Tracks.js", "lampac"));

            if (ModInit.conf.initPlugins.transcoding)
                plugins.Add(new("{localhost}/transcoding.js", 1, "Transcoding video", "lampac"));

            if (ModInit.conf.initPlugins.tmdbProxy)
                plugins.Add(new("{localhost}/tmdbproxy.js", 1, "TMDB Proxy", "lampac"));

            if (ModInit.conf.initPlugins.cubProxy)
                plugins.Add(new("{localhost}/cubproxy.js", 1, "CUB Proxy", "lampac"));

            if (ModInit.conf.initPlugins.online)
                plugins.Add(new("{localhost}/online.js", 1, "Онлайн", "lampac"));

            if (ModInit.conf.initPlugins.watch_together)
                plugins.Add(new("{localhost}/watchtogether.js", 1, "Watch Together", "lampac"));

            if (ModInit.conf.initPlugins.catalog)
                plugins.Add(new("{localhost}/catalog.js", 1, "Альтернативные источники каталога", "lampac"));

            if (ModInit.conf.initPlugins.sisi)
            {
                plugins.Add(new("{localhost}/sisi.js", 1, "Клубничка", "lampac"));
                plugins.Add(new("{localhost}/startpage.js", 1, "Стартовая страница", "lampac"));
            }

            if (ModInit.conf.initPlugins.sync)
                plugins.Add(new("{localhost}/sync.js", 1, "Синхронизация", "lampac"));

            if (ModInit.conf.initPlugins.timecode)
                plugins.Add(new("{localhost}/timecode.js", 1, "Синхронизация тайм-кодов", "lampac"));

            if (ModInit.conf.initPlugins.bookmark)
                plugins.Add(new("{localhost}/bookmark.js", 1, "Синхронизация закладок", "lampac"));

            if (ModInit.conf.initPlugins.torrserver)
                plugins.Add(new("{localhost}/ts.js", 1, "TorrServer", "lampac"));

            if (ModInit.conf.initPlugins.backup)
                plugins.Add(new("{localhost}/backup.js", 1, "Backup", "lampac"));

            if (ModInit.conf.customPlugins != null)
            {
                foreach (var p in ModInit.conf.customPlugins)
                {
                    if (p.status == 1)
                        plugins.Add(p);
                }
            }

            sb = sb.Replace("{initiale}", JsonConvert.SerializeObject(plugins));
            #endregion

            if (ModInit.conf.initPlugins.pirate_store)
                sb = sb.Replace("{pirate_store}", FileCache.ReadAllText($"{ModInit.modpath}/plugins/pirate_store.js", "pirate_store.js"));

            if (CoreInit.conf.accsdb.enable)
                sb = sb.Replace("{deny}", FileCache.ReadAllText($"{ModInit.modpath}/plugins/deny.js", "deny.js").Replace("{cubMesage}", CoreInit.conf.accsdb.authMesage));

            sb = sb.Replace("{lampainit-invc}", FileCache.ReadAllText($"{ModInit.modpath}/plugins/lampainit-invc.js", "lampainit-invc.js"));
            sb = sb.Replace("{country}", requestInfo.Country);
            sb = sb.Replace("{localhost}", host);
            sb = sb.Replace("{deny}", string.Empty);
            sb = sb.Replace("{pirate_store}", string.Empty);

            sb = sb.Replace("{ major: 0, minor: 0 }", $"{{major: 2, minor: 1}}");

            if (ModInit.conf.initPlugins.jacred)
                sb = sb.Replace("{jachost}", Regex.Replace(host, "^https?://", ""));
            else
                sb = sb.Replace("{jachost}", "jac.red");

            #region full_btn_priority_hash
            string online_version = Regex.Match(FileCache.ReadAllText($"{ModInit.modpath}/plugins/online.js", "online.js"), "version: '([^']+)'").Groups[1].Value;

            string LampaUtilshash(string input)
            {
                if (!CoreInit.conf.online.version)
                    input = input.Replace($"v{online_version}", "");

                string str = (input ?? string.Empty);
                int hash = 0;

                if (str.Length == 0) return hash.ToString();

                for (int i = 0; i < str.Length; i++)
                {
                    int _char = str[i];

                    hash = (hash << 5) - hash + _char;
                    hash = hash & hash; // Преобразование в 32-битное целое число
                }

                return Math.Abs(hash).ToString();
            }

            string full_btn_priority_hash = LampaUtilshash($"<div class=\"full-start__button selector view--online lampac--button\" data-subtitle=\"{CoreInit.conf.online.name} v{online_version}\">\n        <svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 392.697 392.697\" xml:space=\"preserve\">\n            <path d=\"M21.837,83.419l36.496,16.678L227.72,19.886c1.229-0.592,2.002-1.846,1.98-3.209c-0.021-1.365-0.834-2.592-2.082-3.145\n                L197.766,0.3c-0.903-0.4-1.933-0.4-2.837,0L21.873,77.036c-1.259,0.559-2.073,1.803-2.081,3.18\n                C19.784,81.593,20.584,82.847,21.837,83.419z\" fill=\"currentColor\"></path>\n            <path d=\"M185.689,177.261l-64.988-30.01v91.617c0,0.856-0.44,1.655-1.167,2.114c-0.406,0.257-0.869,0.386-1.333,0.386\n                c-0.368,0-0.736-0.082-1.079-0.244l-68.874-32.625c-0.869-0.416-1.421-1.293-1.421-2.256v-92.229L6.804,95.5\n                c-1.083-0.496-2.344-0.406-3.347,0.238c-1.002,0.645-1.608,1.754-1.608,2.944v208.744c0,1.371,0.799,2.615,2.045,3.185\n                l178.886,81.768c0.464,0.211,0.96,0.315,1.455,0.315c0.661,0,1.318-0.188,1.892-0.555c1.002-0.645,1.608-1.754,1.608-2.945\n                V180.445C187.735,179.076,186.936,177.831,185.689,177.261z\" fill=\"currentColor\"></path>\n            <path d=\"M389.24,95.74c-1.002-0.644-2.264-0.732-3.347-0.238l-178.876,81.76c-1.246,0.57-2.045,1.814-2.045,3.185v208.751\n                c0,1.191,0.606,2.302,1.608,2.945c0.572,0.367,1.23,0.555,1.892,0.555c0.495,0,0.991-0.104,1.455-0.315l178.876-81.768\n                c1.246-0.568,2.045-1.813,2.045-3.185V98.685C390.849,97.494,390.242,96.384,389.24,95.74z\" fill=\"currentColor\"></path>\n            <path d=\"M372.915,80.216c-0.009-1.377-0.823-2.621-2.082-3.18l-60.182-26.681c-0.938-0.418-2.013-0.399-2.938,0.045\n                l-173.755,82.992l60.933,29.117c0.462,0.211,0.958,0.316,1.455,0.316s0.993-0.105,1.455-0.316l173.066-79.092\n                C372.122,82.847,372.923,81.593,372.915,80.216z\" fill=\"currentColor\"></path>\n        </svg>\n\n        <span>Онлайн</span>\n    </div>");

            sb = sb.Replace("{full_btn_priority_hash}", full_btn_priority_hash)
                   .Replace("{btn_priority_forced}", CoreInit.conf.online.btn_priority_forced.ToString().ToLower());
            #endregion

            #region domain token
            if (!string.IsNullOrEmpty(CoreInit.conf.accsdb.domainId_pattern))
            {
                string token = Regex.Match(HttpContext.Request.Host.Host, CoreInit.conf.accsdb.domainId_pattern).Groups[1].Value;
                sb = sb.Replace("{token}", token);
            }
            else { sb = sb.Replace("{token}", string.Empty); }
            #endregion

            return Content(sb.ToString(), "application/javascript; charset=utf-8");
        }
        #endregion

        #region on.js
        [HttpGet]
        [AllowAnonymous]
        [Route("on.js")]
        [Route("on/js/{token}")]
        [Route("on/h/{token}")]
        [Route("on/{token}")]
        public ActionResult LamOnInit(string token, bool adult = true)
        {
            SetHeadersNoCache();

            if (adult && HttpContext.Request.Path.Value.StartsWith("/on/h/"))
                adult = false;

            var plugins = new List<string>(10);
            var plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/on.js", "on.js");

            void send(string name, bool worktoken)
            {
                if (worktoken && !string.IsNullOrEmpty(token))
                {
                    plugins.Add($"\"{{localhost}}/{name}/js/{HttpUtility.UrlEncode(token)}\"");
                }
                else
                {
                    plugins.Add($"\"{{localhost}}/{name}.js\"");
                }
            }

            if (ModInit.conf.initPlugins.dlna)
                send("dlna", true);

            if (ModInit.conf.initPlugins.tracks)
                send("tracks", true);

            if (ModInit.conf.initPlugins.transcoding)
                send("transcoding", true);

            if (ModInit.conf.initPlugins.tmdbProxy)
                send("tmdbproxy", true);

            if (ModInit.conf.initPlugins.cubProxy)
                send("cubproxy", true);

            if (ModInit.conf.initPlugins.online)
                send("online", true);

            if (ModInit.conf.initPlugins.watch_together)
                send("watchtogether", false);

            if (adult && ModInit.conf.initPlugins.sisi)
            {
                send("sisi", true);
                send("startpage", false);
            }

            if (ModInit.conf.initPlugins.sync)
                send("sync", true);

            if (ModInit.conf.initPlugins.timecode)
                send("timecode", true);

            if (ModInit.conf.initPlugins.bookmark)
                send("bookmark", true);

            if (ModInit.conf.initPlugins.torrserver)
                send("ts", true);

            if (ModInit.conf.initPlugins.backup)
                send("backup", true);

            if (ModInit.conf.customPlugins != null)
            {
                foreach (var p in ModInit.conf.customPlugins)
                {
                    if (p.status == 1)
                        plugins.Add($"\"{p.url}\"");
                }
            }

            if (plugins.Count == 0)
                plugin = plugin.Replace("{plugins}", string.Empty);
            else
            {
                plugin = plugin.Replace("{plugins}", string.Join(",", plugins));
            }

            plugin = plugin
                .Replace("{country}", requestInfo.Country)
                .Replace("{localhost}", host);

            return Content(plugin, "application/javascript; charset=utf-8");
        }
        #endregion

        #region privateinit.js
        [HttpGet]
        [Route("privateinit.js")]
        public ActionResult PrivateInit()
        {
            SetHeadersNoCache();

            var user = requestInfo.user;
            if (user == null || user.ban || DateTime.UtcNow > user.expires)
                return Content(string.Empty, "application/javascript; charset=utf-8");

            string privateinit = FileCache.ReadAllText($"{ModInit.modpath}/plugins/privateinit.js", "privateinit.js")
                .Replace("{country}", requestInfo.Country)
                .Replace("{localhost}", host);

            if (ModInit.conf.initPlugins.jacred)
                privateinit = privateinit.Replace("{jachost}", Regex.Replace(host, "^https?://", ""));
            else
                privateinit = privateinit.Replace("{jachost}", "jac.red");

            return Content(privateinit, "application/javascript; charset=utf-8");
        }
        #endregion

        #region telegram_auth_gate.js
        [HttpGet]
        [Route("telegram_auth_gate.js")]
        public ActionResult TelegramAuthGate()
        {
            SetHeadersNoCache();

            string gate = FileCache.ReadAllText($"{ModInit.modpath}/plugins/telegram_auth_gate.js", "telegram_auth_gate.js")
                .Replace("{country}", requestInfo.Country)
                .Replace("{localhost}", host);

            return Content(gate, "application/javascript; charset=utf-8");
        }
        #endregion
    }
}
