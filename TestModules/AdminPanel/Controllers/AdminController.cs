using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using IO = System.IO;

namespace AdminPanel.Controllers
{
    public class AdminController : BaseController
    {
        #region TryAuthorizeAdmin
        bool TryAuthorizeAdmin(string passwd, out ActionResult result)
        {
            result = null;

            if (CoreInit.rootPasswd == "termux")
            {
                HttpContext.Response.Cookies.Append("passwd", "termux");
                return true;
            }

            if (string.IsNullOrWhiteSpace(passwd))
                HttpContext.Request.Cookies.TryGetValue("passwd", out passwd);

            if (string.IsNullOrWhiteSpace(passwd))
            {
                result = Redirect("/admin/auth");
                return false;
            }

            string ipKey = $"Accsdb:auth:IP:{requestInfo.IP}";
            if (!memoryCache.TryGetValue(ipKey, out ConcurrentDictionary<string, byte> passwds))
            {
                passwds = new ConcurrentDictionary<string, byte>();
                memoryCache.Set(ipKey, passwds, DateTime.Today.AddDays(1));
            }

            passwds.TryAdd(passwd, 0);

            if (passwds.Count > 10)
            {
                result = Content("Too many attempts, try again tomorrow.");
                return false;
            }

            if (CoreInit.rootPasswd == passwd)
                return true;

            HttpContext.Response.Cookies.Delete("passwd");
            result = Redirect("/admin/auth");
            return false;
        }
        #endregion

        #region admin / auth
        [AuthorizeAnonymous]
        [HttpGet]
        [HttpPost]
        [Route("/admin")]
        [Route("/admin/auth")]
        public ActionResult Authorization([FromForm] string parol)
        {
            string passwd = parol?.Trim();

            if (string.IsNullOrWhiteSpace(passwd))
                HttpContext.Request.Cookies.TryGetValue("passwd", out passwd);

            if (string.IsNullOrWhiteSpace(passwd))
            {
                string html = @"
<!DOCTYPE html>
<html>
<head>
	<title>Authorization</title>
</head>
<body>

<style type=""text/css"">
	* {
	    box-sizing: border-box;
	    outline: none;
	}
	body{
		padding: 40px;
		font-family: sans-serif;
	}
	label{
		display: block;
		font-weight: 700;
		margin-bottom: 8px;
	}
	input,
	textarea,
	select{
		width: 340px;
		padding: 8px;
	}
	button{
		padding: 10px;
	}
	form > * + *{
		margin-top: 20px;
	}
</style>

<form method=""post"" action=""/admin/auth"" id=""form"">
	<div>
		<input type=""text"" name=""parol"" placeholder=""пароль из файла passwd""></input>
	</div>

	<button type=""submit"">войти</button>

</form>

<div style=""margin-top: 4em;""><b style=""color: cadetblue;"">Выполните одну из команд через ssh</b><br><br>
	cat /home/lampac/passwd<br><br>
	docker exec -it lampac cat passwd
</div>

</body>
</html>
";
                return Content(html, "text/html; charset=utf-8");
            }
            else
            {
                if (!TryAuthorizeAdmin(passwd, out ActionResult badresult))
                    return badresult;

                HttpContext.Response.Cookies.Append("passwd", passwd);
                return renderAdmin();
            }
        }

        ActionResult renderAdmin()
        {
            string adminHtml = IO.File.Exists("wwwroot/mycontrol/index.html")
                ? IO.File.ReadAllText("wwwroot/mycontrol/index.html")
                : IO.File.Exists("wwwroot/control/index.html")
                    ? IO.File.ReadAllText("wwwroot/control/index.html")
                    : "<html><body><h2>Admin panel UI not found.<br>Place index.html in wwwroot/control/</h2></body></html>";

            return Content(adminHtml, "text/html; charset=utf-8");
        }
        #endregion


        #region init
        [AuthorizeAnonymous]
        [HttpPost]
        [Route("/admin/init/save")]
        public ActionResult InitSave([FromForm] string json)
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            try
            {
                JsonConvert.DeserializeObject<CoreInit>(json);
            }
            catch (Exception ex) { return Json(new { error = true, ex = ex.Message }); }

            var jo = JsonConvert.DeserializeObject<JObject>(json);

            JToken users = null;
            var accsdbNode = jo["accsdb"] as JObject;
            if (accsdbNode != null)
            {
                var usersNode = accsdbNode["users"];
                if (usersNode != null)
                {
                    users = usersNode.DeepClone();
                    accsdbNode.Remove("users");

                    IO.File.WriteAllText("users.json", JsonConvert.SerializeObject(users, Formatting.Indented));
                }
            }

            IO.File.WriteAllText("init.conf", JsonConvert.SerializeObject(jo, Formatting.Indented));

            return Json(new { success = true });
        }

        [AuthorizeAnonymous]
        [HttpGet]
        [Route("/admin/init/custom")]
        public ActionResult InitCustom()
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            string json = IO.File.Exists("init.conf") ? IO.File.ReadAllText("init.conf") : null;
            if (json != null && !json.Trim().StartsWith("{"))
                json = "{" + json + "}";

            var ob = json != null ? JsonConvert.DeserializeObject<JObject>(json) : new JObject { };
            return ContentTo(JsonConvert.SerializeObject(ob));
        }

        [AuthorizeAnonymous]
        [HttpGet]
        [Route("/admin/init/current")]
        public ActionResult InitCurrent()
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            return Content(JsonConvert.SerializeObject(CoreInit.conf), "application/json; charset=utf-8");
        }

        [AuthorizeAnonymous]
        [HttpGet]
        [Route("/admin/init/default")]
        public ActionResult InitDefault()
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            return Content(JsonConvert.SerializeObject(new CoreInit()), "application/json; charset=utf-8");
        }

        [AuthorizeAnonymous]
        [HttpGet]
        [Route("/admin/init/example")]
        public ActionResult InitExample()
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            return Content(IO.File.Exists("example.conf") ? IO.File.ReadAllText("example.conf") : string.Empty);
        }
        #endregion

        #region sync/init
        [AuthorizeAnonymous]
        [HttpGet]
        [Route("/admin/sync/init")]
        public ActionResult Synchtml()
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            string html = @"
<!DOCTYPE html>
<html>
<head>
	<title>Редактор sync.conf</title>
</head>
<body>

<style type=""text/css"">
	* {
	    box-sizing: border-box;
	    outline: none;
	}
	body{
		padding: 40px;
		font-family: sans-serif;
	}
	label{
		display: block;
		font-weight: 700;
		margin-bottom: 8px;
	}
	input,
	textarea,
	select{
		width: 100%;
		padding: 10px;
	}
	button{
		padding: 10px;
	}
	form > * + *{
		margin-top: 30px;
	}
</style>

<form method=""post"" action="""" id=""form"">
	<div>
		<label>Ваш sync.conf
		<textarea id=""value"" name=""value"" rows=""30"">{conf}</textarea>
	</div>

	<button type=""submit"">Сохранить</button>

</form>

<script type=""text/javascript"">
	document.getElementById('form').addEventListener(""submit"", (e) => {
		let json = document.getElementById('value').value

		e.preventDefault()

		try{
			let formData = new FormData()
				formData.append('json', json)

			fetch('/admin/sync/init/save',{
			    method: ""POST"",
			    body: formData
			})
			.then((response)=>{
				if (!response.ok) {
					return response.json().then(err => {
						throw new Error(err.ex || 'Не удалось сохранить настройки');
					});
				}
				return response.json();
			 })
			.then((data)=>{
				if (data.success) {
					alert('Сохранено');
				} else if (data.error) {
					throw new Error(data.ex);
				} else {
					throw new Error('Не удалось сохранить настройки');
				}
			})
			.catch((e)=>{
				alert(e.message)
			})
		}
		catch(e){
			alert('Ошибка: ' + e.message)
		}
	})
</script>

</body>
</html>
";

            string conf = IO.File.Exists("sync.conf") ? IO.File.ReadAllText("sync.conf") : string.Empty;
            return Content(html.Replace("{conf}", conf), "text/html; charset=utf-8");
        }

        [AuthorizeAnonymous]
        [HttpPost]
        [Route("/admin/sync/init/save")]
        public ActionResult SyncSave([FromForm] string json)
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            try
            {
                string testjson = json.Trim();
                if (!testjson.StartsWith("{"))
                    testjson = "{" + testjson + "}";

                JsonConvert.DeserializeObject<CoreInit>(testjson);
            }
            catch (Exception ex) { return Json(new { error = true, ex = ex.Message }); }

            IO.File.WriteAllText("sync.conf", json);
            return Json(new { success = true });
        }
        #endregion

        #region manifest/install
        [AuthorizeAnonymous]
        [HttpGet]
        [HttpPost]
        [Route("/admin/manifest/install")]
        public ActionResult ManifestInstallHtml(
            string online, string sisi, string jac, string dlna,
            string tracks, string ts, string catalog, string merch, string eng)
        {
            if (!TryAuthorizeAdmin(null, out ActionResult badresult))
                return badresult;

            if (CoreInit.rootPasswd == "termux")
                return Content("В termux операция недоступна");

            if (HttpContext.Request.Method == "POST")
            {
                var modules = new List<object>();

                if (online == "on")
                    modules.Add(new { enable = true, path = "Online" });

                if (sisi == "on")
                    modules.Add(new { enable = true, path = "SISI" });

                if (!string.IsNullOrEmpty(jac))
                    modules.Add(new { enable = true, path = "JacRed" });

                if (dlna == "on")
                    modules.Add(new { enable = true, path = "DLNA" });

                if (tracks == "on")
                    modules.Add(new { enable = true, path = "Tracks" });

                if (ts == "on")
                    modules.Add(new { enable = true, path = "TorrServer" });

                if (catalog == "on")
                    modules.Add(new { enable = true, path = "Catalog" });

                if (merch == "on")
                    modules.Add(new { enable = false, path = "Merchant" });

                IO.Directory.CreateDirectory("module");
                IO.File.WriteAllText("module/manifest.json", JsonConvert.SerializeObject(modules, Formatting.Indented));

                if (eng != "on")
                    UpdateInitConf(j => j["disableEng"] = true);

                string host = CoreInit.Host(HttpContext);
                string shared_passwd = Guid.NewGuid().ToString("N")[..8];

                UpdateInitConf(j =>
                {
                    var accsdb = j["accsdb"] as JObject ?? new JObject();
                    accsdb["enable"] = true;
                    accsdb["shared_passwd"] = shared_passwd;
                    j["accsdb"] = accsdb;
                });

                string passwdFile = IO.File.Exists("passwd") ? IO.File.ReadAllText("passwd") : "(файл passwd не знайдено)";

                string htmlSuccess = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <title>Настройка завершена</title>
</head>
<body>

<style type=""text/css"">
    * {{ box-sizing: border-box; outline: none; }}
    body {{ padding: 40px; font-family: sans-serif; }}
    h1 {{ color: #2b7a78; margin-bottom: 1em; text-align: center; }}
    hr {{ margin-top: 1em; margin-bottom: 2em; }}
    .block {{ margin-top: 20px; }}
    pre {{ background: #f5f5f5; padding: 12px; border-radius: 6px; white-space: pre-wrap; word-break: break-all; }}
</style>

<h1>Настройка завершена</h1>

<div class=""block""><b>Авторизация в Lampa</b><br /><br />
    Пароль: {shared_passwd}
</div><hr />

<div class=""block"">
    <b>Админ панель</b><br /><br />
    Адрес: {host}/admin<br />
    Пароль: {passwdFile}
</div>

<hr />

<div class=""block"">
    <div style=""margin-top:10px"">
        <b>Media Station X</b><br /><br />
        Settings -> Start Parameter -> Setup<br />
        Enter current ip address and port: {HttpContext.Request.Host.Value}<br /><br />
        Убрать/Добавить адреса можно в msx.json
    </div>
</div>

<hr />

<div class=""block"">
    <b>Виджет для Samsung</b><br /><br />
    {host}/samsung.wgt
</div>

<hr />

<div class=""block"">
    <b>Для android apk</b><br /><br />
    Зажмите кнопку назад и введите новый адрес: {host}
</div>

<hr />

<div class=""block"">
    <b>Плагины для Lampa</b><br /><br />
    Заходим в настройки - расширения, жмем на кнопку ""добавить плагин"". В окне ввода вписываем адрес плагина {host}/on.js и перезагружаем виджет удерживая кнопку ""назад"" пока виджет не закроется.
</div>

<hr />

<div class=""block"">
    <b>TorrServer (если установлен)</b><br /><br />
    {host}/ts
</div>

</body>
</html>";

                Startup.appReload?.Reload();
                return Content(htmlSuccess, "text/html; charset=utf-8");
            }

            string renderHtml()
            {
                return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
	<title>Модули</title>
</head>
<body>

<style type='text/css'>
	* {{
	    box-sizing: border-box;
	    outline: none;
	}}
	body{{
		padding: 40px;
		font-family: sans-serif;
	}}
	label{{
		display: block;
		font-weight: 700;
		margin-bottom: 8px;
	}}
	input,
	select{{
		margin: 10px;
		margin-left: 0px;
	}}
	button{{
		padding: 10px;
	}}
	form > * + *{{
		margin-top: 30px;
	}}
	.flex{{
		display: flex;
		align-items: center;
	}}
</style>

<form method='post' action='/admin/manifest/install' id='form'>
	<div>
		<label>Установка модулей</label>
		<div class='flex'>
			<input name='online' type='checkbox' checked /> Онлайн балансеры Rezka, Filmix, etc
		</div>
		<div class='flex'>
			&nbsp; &nbsp; &nbsp; <input name='eng' type='checkbox' checked /> ENG балансеры
		</div>
		<div class='flex'>
			<input name='sisi' type='checkbox' checked /> Клубничка 18+, PornHub, Xhamster, etc
		</div>
		<div class='flex'>
			<input name='catalog' type='checkbox' checked /> Альтернативные источники каталога cub и tmdb
		</div>
		<div class='flex'>
			<input name='dlna' type='checkbox' checked /> DLNA - Загрузка торрентов и просмотр медиа файлов с локального устройства
		</div>
		<div class='flex'>
			<input name='ts' type='checkbox' checked /> TorrServer - возможность просматривать торренты в онлайн
		</div>
		<div class='flex'>
			<input name='tracks' type='checkbox' checked /> Tracks - транскодинг видео и замена названий аудиодорожек
		</div>
		<div class='flex'>
			<input name='merch' type='checkbox' /> Автоматизация оплаты FreeKassa, Streampay, Litecoin, CryptoCloud
		</div>

		<br><br>
		<label>Поиск торрентов</label>
		<div class='flex'>
			<input name='jac' type='radio' value='webapi' checked /> Быстрый поиск по внешним базам JacRed, Rutor, Kinozal, NNM-Club, Rutracker, etc
		</div>
		<div class='flex'>
			<input name='jac' type='radio' value='fdb' /> Локальный jacred.xyz (не рекомендуется ставить на домашние устройства) - 2GB HDD
		</div>
	</div>

	<button type='submit'>Завершить настройку</button></form></body></html>";
            }

            return Content(renderHtml(), "text/html; charset=utf-8");
        }
        #endregion


        #region UpdateInitConf
        void UpdateInitConf(Action<JObject> modify)
        {
            JObject jo;

            if (IO.File.Exists("init.conf"))
            {
                string initconf = IO.File.ReadAllText("init.conf").Trim();
                if (string.IsNullOrEmpty(initconf))
                    jo = new JObject();
                else
                {
                    if (!initconf.StartsWith("{"))
                        initconf = "{" + initconf + "}";

                    try
                    {
                        jo = JsonConvert.DeserializeObject<JObject>(initconf) ?? new JObject();
                    }
                    catch
                    {
                        jo = new JObject();
                    }
                }
            }
            else
            {
                jo = new JObject();
            }

            modify?.Invoke(jo);

            IO.File.WriteAllText("init.conf", JsonConvert.SerializeObject(jo, Formatting.Indented));
        }
        #endregion
    }
}
