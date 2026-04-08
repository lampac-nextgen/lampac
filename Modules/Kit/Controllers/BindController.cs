using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using IO = System.IO;

namespace KitMod.Controllers
{
    public class SiteConf
    {
        public OnlinesSettings Rezka { get; set; } = new OnlinesSettings("Rezka", "https://hdrezka.me");
        public OnlinesSettings Filmix { get; set; } = new OnlinesSettings("Filmix", "http://filmixapp.cyou");
        public OnlinesSettings VoKino { get; set; } = new OnlinesSettings("VoKino", "http://api.vokino.org");
        public OnlinesSettings KinoPub { get; set; } = new OnlinesSettings("KinoPub", "https://api.srvkp.com");
        public OnlinesSettings GetsTV { get; set; } = new OnlinesSettings("GetsTV", "https://getstv.com");
    }

    public class BindController : BaseOnlineController
    {
        static SiteConf siteConf = ModuleInvoke.DeserializeInit(new SiteConf());

        #region saveBind
        void saveBind(string aesGcmKey, JObject ob)
        {
            if (string.IsNullOrEmpty(aesGcmKey))
                return;

            string md5key = CrypTo.md5(aesGcmKey);
            string filePath = $"database/kit/{md5key[0]}/{md5key}";
            Directory.CreateDirectory($"database/kit/{md5key[0]}");

            string json = JsonConvert.SerializeObject(ob, Formatting.Indented);

            CryptoKit.Write(aesGcmKey, json, filePath);
        }
        #endregion

        #region renderHtml
        string LoadBindFrame(string title, string body)
        {
            string frame = IO.File.ReadAllText($"{ModInit.folder_mod}/html/bind-frame.html");
            return frame
                .Replace("__KIT_TITLE__", WebUtility.HtmlEncode(title))
                .Replace("__KIT_BODY__", body);
        }

        ContentResult renderHtml(string title, string body)
        {
            return Content(LoadBindFrame(title, body), "text/html; charset=utf-8");
        }

        ContentResult renderMessage(string title, string message, bool error = true)
        {
            string cls = error ? "kit-msg kit-msg-err" : "kit-msg";
            return renderHtml(title, "<p class='" + cls + "'>" + WebUtility.HtmlEncode(message) + "</p>");
        }
        #endregion

        #region loadconf
        JObject loadconf(string aesGcmKey, string filePath)
        {
            if (!IO.File.Exists(filePath))
                return new JObject();

            string json = CryptoKit.ReadFile(aesGcmKey, filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new JObject();

            return JsonConvert.DeserializeObject<JObject>(json);
        }
        #endregion

        (string aesGcmKey, string filePath) userInfo()
        {
            if (HttpContext.Request.Cookies.TryGetValue("aesgcmkey", out string aesGcmKey) && !string.IsNullOrWhiteSpace(aesGcmKey))
            {
                if (CryptoKit.TestKey(aesGcmKey))
                {
                    string md5key = CrypTo.md5(aesGcmKey);
                    string filePath = $"database/kit/{md5key[0]}/{md5key}";

                    return (aesGcmKey, filePath);
                }
            }

            return default;
        }


        #region Filmix
        [HttpGet]
        [AllowAnonymous]
        [Route("/bind/filmix")]
        async public Task<ActionResult> Filmix(string filmix_token)
        {
            var filmix = siteConf.Filmix;

            (string aesGcmKey, string filePath) = userInfo();
            if (string.IsNullOrEmpty(aesGcmKey))
                return renderMessage("Ошибка", "Отсутствует параметр aesGcmKey. Откройте /kit и войдите снова.");

            if (string.IsNullOrEmpty(filmix_token))
            {
                var token_request = await Http.Get<JObject>($"{filmix.host}/api/v2/token_request?user_dev_apk=2.2.13&user_dev_id={UnicTo.Code(16)}&user_dev_name=Xiaomi&user_dev_os=12&user_dev_vendor=Xiaomi&user_dev_token=", timeoutSeconds: 10);

                if (token_request == null)
                    return renderMessage("Filmix", $"{filmix.host} недоступен, повторите попытку позже.");

                string body = $@"<div class='steps'>
                    <div class='step'>
                        1. Откройте <a href='https://filmix.my/consoles' target='_blank' class='link'>https://filmix.my/consoles</a>
                    </div>
                    <div class='step'>
                        2. Добавьте идентификатор устройства
                        <div class='code'>{token_request.Value<string>("user_code")}</div>
                    </div>
                </div>
                <a href='/bind/filmix?filmix_token={token_request.Value<string>("code")}' class='btn btn-primary btn-block'>
                    завершить привязку устройства
                </a>";

                return renderHtml("Привязка Filmix", body);
            }
            else
            {
                bool pro = false;
                var root = await Http.Get<JObject>($"{filmix.host}/api/v2/user_profile?app_lang=ru_RU&user_dev_apk=2.2.13&user_dev_id={UnicTo.Code(16)}&user_dev_name=Xiaomi&user_dev_os=12&user_dev_vendor=Xiaomi&user_dev_token=" + filmix_token, timeoutSeconds: 10);
                if (root != null)
                {
                    if (!root.ContainsKey("user_data"))
                        return renderMessage("Filmix", $"Указанный токен не найден или устарел.");

                    var user_data = root["user_data"];
                    if (user_data != null)
                    {
                        pro = user_data.Value<bool>("is_pro");
                        if (pro == false)
                            pro = user_data.Value<bool>("is_pro_plus");
                    }
                }

                var bwaconf = loadconf(aesGcmKey, filePath);
                bwaconf["Filmix"] = new JObject()
                {
                    ["enable"] = true,
                    ["token"] = filmix_token,
                    ["pro"] = pro,
                };

                saveBind(aesGcmKey, bwaconf);
                return LocalRedirect("/kit");
            }
        }
        #endregion

        #region Vokino
        [HttpGet]
        [AllowAnonymous]
        [Route("/bind/vokino")]
        async public Task<ActionResult> Vokino(string login, string pass)
        {
            var vokino = siteConf.VoKino;

            (string aesGcmKey, string filePath) = userInfo();
            if (string.IsNullOrEmpty(aesGcmKey))
                return renderMessage("Ошибка", "Отсутствует параметр aesGcmKey. Откройте /kit и войдите снова.");

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
            {
                string body = $@"<div class='title'>Введите данные аккаунта <a href='https://vokino.pro' target='_blank' class='link'>vokino.pro</a></div>
                    <form method='get' action='/bind/vokino'>
                        <div class='form-group'>
                            <input type='text' name='login' placeholder='Email' required>
                        </div>
                        <div class='form-group'>
                            <input type='password' name='pass' placeholder='Пароль' required>
                        </div>
                        <button type='submit' class='btn btn-primary btn-block'>Добавить устройство</button>
                    </form>";

                return renderHtml("Привязка VoKino", body);
            }
            else
            {
                string deviceid = new string(DateTime.Now.ToBinary().ToString().Reverse().ToArray()).Substring(0, 8);
                var token_request = await Http.Get<JObject>($"{vokino.host}/v2/auth?email={HttpUtility.UrlEncode(login)}&passwd={HttpUtility.UrlEncode(pass)}&deviceid={deviceid}", timeoutSeconds: 10, headers: HeadersModel.Init(("user-agent", "lampac")));

                if (token_request == null)
                    return renderMessage("VoKino", $"{vokino.host} недоступен, повторите попытку позже.");

                string authToken = token_request.Value<string>("authToken");
                if (string.IsNullOrEmpty(authToken))
                    return renderMessage("VoKino", token_request.Value<string>("error") ?? "Не удалось получить токен.");

                var bwaconf = loadconf(aesGcmKey, filePath);

                bwaconf["VoKino"] = new JObject()
                {
                    ["enable"] = true,
                    ["token"] = authToken,
                    ["online"] = new JObject()
                    {
                        ["vokino"] = true,
                        ["filmix"] = true,
                        ["alloha"] = true,
                        ["monframe"] = true,
                        ["remux"] = true,
                        ["ashdi"] = true,
                        ["hdvb"] = true,
                        ["vibix"] = true
                    }
                };

                saveBind(aesGcmKey, bwaconf);
                return LocalRedirect("/kit");
            }
        }
        #endregion

        #region Kinopub
        [HttpGet]
        [AllowAnonymous]
        [Route("/bind/kinopub")]
        async public Task<ActionResult> Kinopub(string code)
        {
            var KinoPub = siteConf.KinoPub;

            (string aesGcmKey, string filePath) = userInfo();
            if (string.IsNullOrEmpty(aesGcmKey))
                return renderMessage("Ошибка", "Отсутствует параметр aesGcmKey. Откройте /kit и войдите снова.");

            if (string.IsNullOrWhiteSpace(code))
            {
                var token_request = await Http.Post<JObject>($"{KinoPub.host}/oauth2/device?grant_type=device_code&client_id=xbmc&client_secret=cgg3gtifu46urtfp2zp1nqtba0k2ezxh", "", timeoutSeconds: 10);
                if (token_request == null || string.IsNullOrWhiteSpace(token_request.Value<string>("user_code")))
                    return renderMessage("KinoPub", "api.srvkp.com недоступен, повторите попытку позже.");

                string body = $@"<div class='steps'>
                    <div class='step'>
                        1. Откройте <a href='https://kino.pub/device' target='_blank' class='link'>https://kino.pub/device</a>
                    </div>
                    <div class='step'>
                        2. Введите код устройства
                        <div class='code'>{token_request.Value<string>("user_code")}</div>
                    </div>
                </div>
                <a href='/bind/kinopub?code={token_request.Value<string>("code")}' class='btn btn-primary btn-block'>
                    завершить привязку устройства
                </a>";

                return renderHtml("Привязка KinoPub", body);
            }
            else
            {
                var device_token = await Http.Post<JObject>($"{KinoPub.host}/oauth2/device?grant_type=device_token&client_id=xbmc&client_secret=cgg3gtifu46urtfp2zp1nqtba0k2ezxh&code={code}", "");

                if (device_token == null)
                    return renderMessage("KinoPub", $"{KinoPub.host} недоступен, повторите попытку позже.");

                if (string.IsNullOrWhiteSpace(device_token.Value<string>("access_token")))
                    return renderMessage("KinoPub", "Токен доступа не получен. Подтвердите устройство на kino.pub и нажмите кнопку завершения снова.");

                var bwaconf = loadconf(aesGcmKey, filePath);

                bwaconf["KinoPub"] = new JObject()
                {
                    ["enable"] = true,
                    ["token"] = device_token.Value<string>("access_token")
                };

                saveBind(aesGcmKey, bwaconf);
                return LocalRedirect("/kit");
            }
        }
        #endregion

        #region Rezka
        [HttpGet]
        [AllowAnonymous]
        [Route("/bind/rezka")]
        async public Task<ActionResult> Rezka(string login, string pass)
        {
            var rezka = siteConf.Rezka;

            (string aesGcmKey, string filePath) = userInfo();
            if (string.IsNullOrEmpty(aesGcmKey))
                return renderMessage("Ошибка", "Отсутствует параметр aesGcmKey. Откройте /kit и войдите снова.");

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
            {
                string body = $@"<div class='title'>Введите данные PREMUIM аккаунта <a href='{siteConf.Rezka.host}' target='_blank' class='link'>HDRezka</a></div>
                    <form method='get' action='/bind/rezka'>
                        <div class='form-group'>
                            <input type='text' name='login' placeholder='Email' required>
                        </div>
                        <div class='form-group'>
                            <input type='password' name='pass' placeholder='Пароль' required>
                        </div>
                        <button type='submit' class='btn btn-primary btn-block'>Привязать устройство</button>
                    </form>";

                return renderHtml("Привязка HDRezka", body);
            }
            else
            {
                string cookie = await getCookieRezka(login, pass);
                if (string.IsNullOrEmpty(cookie))
                    return renderMessage("HDRezka", "Ошибка авторизации, повторите попытку позже.");

                bool premium = false;
                string rezka_main = await Http.Get(siteConf.Rezka.host, cookie: cookie, timeoutSeconds: 10);
                if (rezka_main != null && (rezka_main.Contains("b-premium_user__body") || rezka_main.Contains("b-hd_prem")))
                    premium = true;

                var bwaconf = loadconf(aesGcmKey, filePath);

                if (premium)
                {
                    bwaconf["RezkaPrem"] = new JObject()
                    {
                        ["enable"] = true,
                        ["cookie"] = cookie
                    };

                    if (bwaconf.ContainsKey("Rezka"))
                    {
                        bwaconf["Rezka"]["enable"] = false;
                    }
                    else
                    {
                        bwaconf["Rezka"] = new JObject()
                        {
                            ["enable"] = false
                        };
                    }
                }
                else
                {
                    bwaconf["Rezka"] = new JObject()
                    {
                        ["enable"] = true,
                        ["cookie"] = cookie
                    };
                }

                saveBind(aesGcmKey, bwaconf);
                return LocalRedirect("/kit");
            }
        }
        #endregion

        #region GetsTV
        [HttpGet]
        [AllowAnonymous]
        [Route("/bind/getstv")]
        async public Task<ActionResult> GetsTV(string login, string pass)
        {
            var getstv = siteConf.GetsTV;

            (string aesGcmKey, string filePath) = userInfo();
            if (string.IsNullOrEmpty(aesGcmKey))
                return renderMessage("Ошибка", "Отсутствует параметр aesGcmKey. Откройте /kit и войдите снова.");

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
            {
                string body = $@"<div class='title'>Введите данные аккаунта <a href='https://getstv.com/user' target='_blank' class='link'>getstv.com</a></div>
                    <form method='get' action='/bind/getstv'>
                        <div class='form-group'>
                            <input type='text' name='login' placeholder='Email' required>
                        </div>
                        <div class='form-group'>
                            <input type='password' name='pass' placeholder='Пароль' required>
                        </div>
                        <button type='submit' class='btn btn-primary btn-block'>Добавить устройство</button>
                    </form>";

                return renderHtml("Привязка GetsTV", body);
            }
            else
            {
                string postdata = $"{{\"email\":\"{login}\",\"password\":\"{pass}\",\"fingerprint\":\"{CrypTo.md5(DateTime.Now.ToString())}\",\"device\":{{}}}}";
                var result = await Http.Post<JObject>($"{siteConf.GetsTV.host}/api/login", new System.Net.Http.StringContent(postdata, Encoding.UTF8, "application/json"), headers: httpHeaders(siteConf.GetsTV));

                if (result == null)
                    return renderMessage("GetsTV", $"{siteConf.GetsTV.host} недоступен, повторите попытку позже.");

                string token = result.Value<string>("token");
                if (string.IsNullOrEmpty(token))
                    return renderHtml("GetsTV", "<p class='kit-msg kit-msg-err'>Не удалось получить токен. Ответ сервера:</p><pre class='kit-pre'>" + WebUtility.HtmlEncode(JsonConvert.SerializeObject(result, Formatting.Indented)) + "</pre>");

                var bwaconf = loadconf(aesGcmKey, filePath);

                bwaconf["GetsTV"] = new JObject()
                {
                    ["enable"] = true,
                    ["token"] = token
                };

                saveBind(aesGcmKey, bwaconf);
                return LocalRedirect("/kit");
            }
        }
        #endregion

        #region IptvOnline
        [HttpGet]
        [AllowAnonymous]
        [Route("/bind/iptvonline")]
        public ActionResult IptvOnline(string login, string pass)
        {
            (string aesGcmKey, string filePath) = userInfo();
            if (string.IsNullOrEmpty(aesGcmKey))
                return renderMessage("Ошибка", "Отсутствует параметр aesGcmKey. Откройте /kit и войдите снова.");

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
            {
                string body = $@"<div class='title'>Введите данные <a href='https://iptv.online/ru/dealers/api' target='_blank' class='link'>iptv.online (API дилера)</a></div>
                    <form method='get' action='/bind/iptvonline'>
                        <div class='form-group'>
                            <input type='text' name='login' placeholder='X-API-KEY' required>
                        </div>
                        <div class='form-group'>
                            <input type='text' name='pass' placeholder='X-API-ID' required>
                        </div>
                        <button type='submit' class='btn btn-primary btn-block'>Добавить устройство</button>
                    </form>";

                return renderHtml("Привязка iptv.online", body);
            }
            else
            {
                var bwaconf = loadconf(aesGcmKey, filePath);

                bwaconf["IptvOnline"] = new JObject()
                {
                    ["enable"] = true,
                    ["token"] = $"{login}:{pass}"
                };

                saveBind(aesGcmKey, bwaconf);
                return LocalRedirect("/kit");
            }
        }
        #endregion


        #region getCookie
        async Task<string> getCookieRezka(string login, string passwd)
        {
            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(passwd))
                return null;

            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(20);
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    var postParams = new Dictionary<string, string>
                    {
                        { "login_name", login },
                        { "login_password", passwd },
                        { "login_not_save", "0" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"https://hdrezka.me/ajax/login/", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string cookie = string.Empty;

                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrEmpty(line))
                                        continue;

                                    if (line.Contains("=deleted;"))
                                        continue;

                                    if (line.Contains("dle_user_id") || line.Contains("dle_password"))
                                        cookie += $"{line.Split(";")[0]}; ";
                                }

                                if (cookie.Contains("dle_user_id") && cookie.Contains("dle_password"))
                                    return Regex.Replace(cookie.Trim(), ";$", "");
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "PremiumConf", "id_xwwb0e5e");
            }

            return null;
        }
        #endregion
    }
}
