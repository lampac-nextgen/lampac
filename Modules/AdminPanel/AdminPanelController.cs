using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Shared;
using Shared.Attributes;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdminPanel
{
    [Authorization(redirectUri: "/adminpanel/auth")]
    public class AdminPanelController : BaseController
    {
        const string InitFile = "init.conf";
        const string CurrentFile = "current.conf";
        const string UsersFile = "users.json";

        [HttpGet]
        [AllowAnonymous]
        [Route("/adminpanel/auth")]
        public ActionResult Auth()
        {
            var path = Path.Combine(ModInit.modpath, "auth.html");
            var html = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return Content(html, "text/html; charset=utf-8");
        }

        [HttpGet]
        [Route("/adminpanel")]
        public ActionResult Index()
        {
            var path = Path.Combine(ModInit.modpath, "index.html");
            var html = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return Content(html, "text/html; charset=utf-8");
        }

        [HttpGet]
        [Route("/adminpanel/api/groups")]
        public ActionResult Groups()
        {
            var current = LoadCurrentRoot();
            var built = ConfigSectionGroups.Build(current);
            var json = JsonConvert.SerializeObject(built, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            });
            return Content(json, "application/json; charset=utf-8");
        }

        [HttpGet]
        [Route("/adminpanel/api/groups/catalog")]
        public ActionResult GroupsCatalog()
        {
            var built = ConfigSectionGroups.BuildCatalog();
            var current = LoadCurrentRoot();
            var inCatalog = ConfigSectionGroups.CatalogRootKeys;
            var orphans = current.Properties()
                .Select(p => p.Name)
                .Where(k => !inCatalog.Contains(k))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();
            if (orphans.Length > 0)
                built.Add(new GroupDto("other", "Прочее", "Ключи из current, не из каталога групп.", orphans));

            var json = JsonConvert.SerializeObject(built, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            });
            return Content(json, "application/json; charset=utf-8");
        }

        static JObject LoadCurrentRoot()
        {
            try
            {
                if (System.IO.File.Exists(CurrentFile))
                    return JObject.Parse(System.IO.File.ReadAllText(CurrentFile, Encoding.UTF8));
            }
            catch (JsonException)
            {
            }

            if (CoreInit.CurrentConf != null)
            {
                try
                {
                    return (JObject)CoreInit.CurrentConf.DeepClone();
                }
                catch
                {
                }
            }

            return new JObject();
        }

        [HttpGet]
        [Route("/adminpanel/api/init")]
        public ActionResult GetInit()
        {
            if (!System.IO.File.Exists(InitFile))
                return Content("{}", "application/json; charset=utf-8");

            var text = System.IO.File.ReadAllText(InitFile, Encoding.UTF8);
            return Content(NormalizeJsonText(text), "application/json; charset=utf-8");
        }

        [HttpGet]
        [Route("/adminpanel/api/current")]
        public ActionResult GetCurrent()
        {
            if (!System.IO.File.Exists(CurrentFile))
            {
                if (CoreInit.CurrentConf != null)
                    return Content(CoreInit.CurrentConf.ToString(Formatting.Indented), "application/json; charset=utf-8");
                return Content("{}", "application/json; charset=utf-8");
            }

            var text = System.IO.File.ReadAllText(CurrentFile, Encoding.UTF8);
            return Content(NormalizeJsonText(text), "application/json; charset=utf-8");
        }

        [HttpPost]
        [Route("/adminpanel/api/init")]
        public async Task<IActionResult> SaveInit()
        {
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body))
                return AdminJsonError(400, "empty body");

            try
            {
                var parsed = JToken.Parse(body);
                if (parsed.Type != JTokenType.Object)
                    return AdminJsonError(400, "root must be a JSON object");

                var formatted = ((JObject)parsed).ToString(Formatting.Indented);
                await WriteInitAtomicAsync(formatted).ConfigureAwait(false);
                return AdminJsonOk();
            }
            catch (JsonException ex)
            {
                return AdminJsonError(400, "invalid json", ex.Message);
            }
            catch (IOException ex)
            {
                return AdminJsonError(500, "failed to write init.conf", ex.Message);
            }
        }

        [HttpPost]
        [Route("/adminpanel/api/init/section/{key}")]
        public async Task<IActionResult> SaveInitSection(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Contains('\\'))
                return AdminJsonError(400, "invalid section key");

            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body))
                return AdminJsonError(400, "empty body");

            JToken sectionToken;
            try
            {
                sectionToken = JToken.Parse(body);
            }
            catch (JsonException ex)
            {
                return AdminJsonError(400, "invalid json", ex.Message);
            }

            try
            {
                JObject root;
                if (System.IO.File.Exists(InitFile))
                {
                    try
                    {
                        root = JObject.Parse(System.IO.File.ReadAllText(InitFile, Encoding.UTF8));
                    }
                    catch (JsonException)
                    {
                        root = new JObject();
                    }
                }
                else
                    root = new JObject();

                root[key] = sectionToken.DeepClone();
                var formatted = root.ToString(Formatting.Indented);
                await WriteInitAtomicAsync(formatted).ConfigureAwait(false);
                return AdminJsonOk();
            }
            catch (IOException ex)
            {
                return AdminJsonError(500, "failed to write init.conf", ex.Message);
            }
        }

        // tmp+Move — атомарно; на bind mount Move(..., overwrite) часто EBUSY → пишем в init.conf напрямую.
        static async Task WriteInitAtomicAsync(string formatted)
        {
            var tmp = InitFile + ".tmp";
            await System.IO.File.WriteAllTextAsync(tmp, formatted, Encoding.UTF8).ConfigureAwait(false);
            try
            {
                try
                {
                    System.IO.File.Move(tmp, InitFile, overwrite: true);
                }
                catch (IOException ex) when (IsReplaceTargetBusy(ex))
                {
                    await System.IO.File.WriteAllTextAsync(InitFile, formatted, Encoding.UTF8).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tmp))
                        System.IO.File.Delete(tmp);
                }
                catch
                {
                }
            }
        }

        static bool IsReplaceTargetBusy(IOException ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e.Message != null && e.Message.Contains("busy", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static ContentResult AdminJsonOk()
        {
            return new ContentResult
            {
                Content = "{\"ok\":true}",
                ContentType = "application/json; charset=utf-8",
                StatusCode = 200
            };
        }

        static ContentResult AdminJsonError(int status, string error, string? detail = null)
        {
            var o = new JObject { ["error"] = error };
            if (!string.IsNullOrEmpty(detail))
                o["detail"] = detail;
            return new ContentResult
            {
                Content = o.ToString(Formatting.None),
                ContentType = "application/json; charset=utf-8",
                StatusCode = status
            };
        }

        static string NormalizeJsonText(string raw)
        {
            try
            {
                return JToken.Parse(raw).ToString(Formatting.Indented);
            }
            catch
            {
                return raw;
            }
        }

        [HttpGet]
        [Route("/adminpanel/api/users-json")]
        public ActionResult GetUsersJson()
        {
            if (!System.IO.File.Exists(UsersFile))
                return Content("[]", "application/json; charset=utf-8");

            var text = System.IO.File.ReadAllText(UsersFile, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text))
                return Content("[]", "application/json; charset=utf-8");

            return Content(NormalizeJsonText(text), "application/json; charset=utf-8");
        }

        [HttpPost]
        [Route("/adminpanel/api/users-json")]
        public async Task<IActionResult> SaveUsersJson()
        {
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(body))
                return AdminJsonError(400, "empty body");

            try
            {
                var parsed = JToken.Parse(body);
                if (parsed.Type != JTokenType.Array)
                    return AdminJsonError(400, "root must be a JSON array", "users.json must be a list of AccsUser objects");

                foreach (var item in (JArray)parsed)
                {
                    if (item.Type != JTokenType.Object)
                        return AdminJsonError(400, "invalid array item", "each element must be a JSON object");
                }

                var formatted = ((JArray)parsed).ToString(Formatting.Indented);
                await WriteUsersAtomicAsync(formatted).ConfigureAwait(false);
                return AdminJsonOk();
            }
            catch (JsonException ex)
            {
                return AdminJsonError(400, "invalid json", ex.Message);
            }
            catch (IOException ex)
            {
                return AdminJsonError(500, "failed to write users.json", ex.Message);
            }
        }

        static async Task WriteUsersAtomicAsync(string formatted)
        {
            var tmp = UsersFile + ".tmp";
            await System.IO.File.WriteAllTextAsync(tmp, formatted, Encoding.UTF8).ConfigureAwait(false);
            try
            {
                try
                {
                    System.IO.File.Move(tmp, UsersFile, overwrite: true);
                }
                catch (IOException ex) when (IsReplaceTargetBusy(ex))
                {
                    await System.IO.File.WriteAllTextAsync(UsersFile, formatted, Encoding.UTF8).ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(tmp))
                        System.IO.File.Delete(tmp);
                }
                catch
                {
                }
            }
        }
    }
}
