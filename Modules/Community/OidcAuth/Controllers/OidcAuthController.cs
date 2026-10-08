using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OidcAuth.Models;
using OidcAuth.Services;
using Shared;
using System;
using System.Linq;
using System.Threading.Tasks;
using IO = System.IO;

namespace OidcAuth.Controllers
{
    [AllowAnonymous]
    public class OidcAuthController : BaseController
    {
        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        [HttpGet]
        [Route("/oidc/config")]
        public ActionResult Config()
        {
            return JsonOk(new
            {
                enabled = IsEnabled(out _),
                name = ModInit.conf?.name ?? "OIDC",
                loginPath = "/oidc/login",
                denyInstalled = ModInit.DenyInstalled()
            });
        }

        [HttpGet]
        [Route("/oidc/login")]
        public async Task<ActionResult> Login(string uid, string returnUrl, string link)
        {
            if (!IsEnabled(out string? error))
                return TextError(400, error ?? "oidc: disabled");

            DiscoveryDocument? doc = await OidcDiscovery.GetAsync().ConfigureAwait(false);
            if (doc == null)
                return TextError(503, "oidc: discovery unavailable");

            string state = OidcFlowService.RandomBase64Url(32);
            string nonce = OidcFlowService.RandomBase64Url(16);
            string verifier = OidcFlowService.RandomBase64Url(64);

            if (!string.IsNullOrWhiteSpace(link) && !LinkStore.Exists(link))
                return TextError(400, "oidc: unknown link code");

            PendingFlowStore.Add(state, new PendingFlow
            {
                DeviceUid = uid?.Trim(),
                Nonce = nonce,
                CodeVerifier = verifier,
                ReturnUrl = SafeReturnUrl(returnUrl),
                LinkCode = string.IsNullOrWhiteSpace(link) ? null : link.Trim()
            });

            string url = OidcFlowService.BuildAuthorizeUrl(
                doc,
                ModInit.conf,
                RedirectBase() + "/oidc/callback",
                state,
                nonce,
                OidcFlowService.CodeChallenge(verifier));

            return Redirect(url);
        }

        [HttpGet]
        [Route("/oidc/callback")]
        public async Task<ActionResult> Callback(string code, string state, string error, string error_description)
        {
            if (!IsEnabled(out string? cfgError))
                return TextError(400, cfgError ?? "oidc: disabled");

            if (!string.IsNullOrEmpty(error))
                return TextError(400, $"oidc: {error} {error_description}".Trim());

            PendingFlow? flow = PendingFlowStore.Take(state);
            if (flow == null)
                return TextError(400, "oidc: unknown or expired state");

            if (string.IsNullOrEmpty(code))
                return TextError(400, "oidc: code is missing");

            DiscoveryDocument? doc = await OidcDiscovery.GetAsync().ConfigureAwait(false);
            if (doc == null)
                return TextError(503, "oidc: discovery unavailable");

            try
            {
                OidcAuthConf conf = ModInit.conf;

                (string accessToken, string? idToken) = await OidcFlowService
                    .ExchangeCodeAsync(conf, doc, code, RedirectBase() + "/oidc/callback", flow.CodeVerifier)
                    .ConfigureAwait(false);

                OidcFlowService.ValidateIdToken(idToken, conf, flow.Nonce);

                JObject userinfo = await OidcFlowService.GetUserinfoAsync(doc, accessToken).ConfigureAwait(false);

                string? sub = userinfo.Value<string>("sub");
                if (string.IsNullOrWhiteSpace(sub))
                    return TextError(400, "oidc: userinfo has no sub");

                if (sub.Contains('@'))
                    Console.WriteLine("OidcAuth: sub looks like an email - check provider Subject Mode (hashed user id or UUID, not username/email).");

                string? email = userinfo.Value<string>("email");
                string? username = FirstNonEmpty(
                    userinfo.Value<string>("preferred_username"),
                    userinfo.Value<string>("nickname"),
                    userinfo.Value<string>("name"));

                (string uid, _) = AccsdbUserSync.ResolveOrRegister(conf, sub, email, username);

                if (flow.LinkCode != null)
                {
                    if (!LinkStore.Bind(flow.LinkCode, uid))
                        return TextError(410, "oidc: link expired");

                    return QrSuccessPage();
                }

                string target = flow.ReturnUrl ?? "/";
                string sep = target.Contains('?') ? "&" : "?";
                return Redirect($"{target}{sep}oidc=ok&uid={Uri.EscapeDataString(uid)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OidcAuth: callback failed: {ex.Message}");
                return TextError(400, "oidc: login failed");
            }
        }

        /// <summary>Creates a short QR code (TV side). The code expires in 10 minutes.</summary>
        [HttpPost]
        [Route("/oidc/link")]
        public ActionResult CreateLink()
        {
            if (!IsEnabled(out string? error))
                return JsonError(400, error ?? "oidc: disabled");

            string code = LinkStore.Create();

            return JsonOk(new
            {
                link = code,
                url = $"{RedirectBase()}/oidc/link/{code}"
            });
        }

        /// <summary>QR target (phone side): jumps into the login flow carrying the link code.</summary>
        [HttpGet]
        [Route("/oidc/link/{code}")]
        public ActionResult OpenLink(string code)
        {
            if (!IsEnabled(out _) || !LinkStore.Exists(code))
                return TextError(404, "oidc: link expired or unknown");

            return Redirect($"/oidc/login?link={Uri.EscapeDataString(code)}&returnUrl=/");
        }

        /// <summary>Poll endpoint (TV side): returns the uid once it has been bound.</summary>
        [HttpGet]
        [Route("/oidc/link/{code}/status")]
        public ActionResult LinkStatus(string code)
        {
            if (!IsEnabled(out _))
                return JsonError(400, "oidc: disabled");

            string uid = LinkStore.Take(code) ?? string.Empty;

            return JsonOk(new { uid });
        }

        [HttpGet]
        [Route("/oidc/status")]
        public ActionResult Status(string uid)
        {
            bool ok = false;

            if (!string.IsNullOrWhiteSpace(uid))
            {
                var user = CoreInit.conf.accsdb.findUser(uid);
                ok = user != null && !user.ban && user.expires > DateTime.Now;
            }

            return JsonOk(new { ok });
        }

        [HttpGet]
        [Route("/oidc/logout")]
        public async Task<ActionResult> Logout()
        {
            if (!IsEnabled(out _))
                return Redirect("/");

            DiscoveryDocument? doc = await OidcDiscovery.GetAsync().ConfigureAwait(false);
            if (doc?.end_session_endpoint == null)
                return Redirect("/");

            string postLogout = RedirectBase() + "/";
            string sep = doc.end_session_endpoint.Contains('?') ? "&" : "?";

            return Redirect(
                $"{doc.end_session_endpoint}{sep}post_logout_redirect_uri={Uri.EscapeDataString(postLogout)}" +
                $"&client_id={Uri.EscapeDataString(ModInit.conf.clientId)}");
        }

        /// <summary>Returns the module's login screen source for diagnostics.</summary>
        [HttpGet]
        [Route("/oidc/deny.js")]
        public ActionResult DenyJs()
        {
            try
            {
                if (string.IsNullOrEmpty(ModInit.modpath))
                    return StatusCode(404);

                string source = IO.Path.Combine(ModInit.modpath, "plugins", "deny.js");
                if (!IO.File.Exists(source))
                    return StatusCode(404);

                return new ContentResult
                {
                    Content = IO.File.ReadAllText(source),
                    ContentType = "text/javascript; charset=utf-8",
                    StatusCode = 200
                };
            }
            catch
            {
                return StatusCode(404);
            }
        }

        ContentResult QrSuccessPage()
        {
            string preferred = Request.Headers.AcceptLanguage.ToString().Split(',')[0];
            (string lang, string title, string message) = preferred switch
            {
                var value when value.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                    => ("ru", "Вход выполнен", "Вернитесь к экрану телевизора. Авторизация продолжится автоматически."),
                var value when value.StartsWith("uk", StringComparison.OrdinalIgnoreCase)
                    => ("uk", "Вхід виконано", "Поверніться до телевізора. Вхід продовжиться автоматично."),
                var value when value.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                    => ("zh", "登录成功", "请返回电视屏幕。登录将自动继续。"),
                _ => ("en", "Signed in", "Return to your TV. Sign-in will continue automatically.")
            };

            string html = $"<!doctype html><html lang=\"{lang}\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{title}</title><body style=\"background:#15191f;color:#fff;font:18px system-ui;display:grid;place-items:center;min-height:90vh;text-align:center\"><div><h1>{title}</h1><p>{message}</p></div></body></html>";
            return Content(html, "text/html; charset=utf-8");
        }

        static bool IsEnabled(out string? error)
        {
            error = null;
            OidcAuthConf conf = ModInit.conf;

            if (conf == null || !conf.enable)
            {
                error = "oidc: disabled";
                return false;
            }

            if (string.IsNullOrWhiteSpace(conf.authority) || string.IsNullOrWhiteSpace(conf.clientId))
            {
                error = "oidc: not configured (authority/clientId)";
                return false;
            }

            return true;
        }

        string RedirectBase()
        {
            string? publicUrl = ModInit.conf.publicUrl?.Trim();
            if (!string.IsNullOrEmpty(publicUrl))
                return publicUrl.TrimEnd('/');

            return $"{Request.Scheme}://{Request.Host}";
        }

        static string SafeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
                return "/";

            returnUrl = returnUrl.Trim();

            if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//"))
                return "/";

            return returnUrl;
        }

        static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        ContentResult JsonOk(object data)
        {
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(data, JsonSettings),
                ContentType = "application/json; charset=utf-8",
                StatusCode = 200
            };
        }

        ContentResult JsonError(int status, string error)
        {
            return new ContentResult
            {
                Content = JsonConvert.SerializeObject(new { error }, JsonSettings),
                ContentType = "application/json; charset=utf-8",
                StatusCode = status
            };
        }

        ContentResult TextError(int status, string? message)
        {
            return new ContentResult
            {
                Content = message,
                ContentType = "text/plain; charset=utf-8",
                StatusCode = status
            };
        }
    }
}
