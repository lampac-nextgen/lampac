using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Utilities;
using System;

namespace KitMod.Controllers
{
    public class UniteController : Controller
    {
        [AllowAnonymous]
        [Route("/kit/unite")]
        public ActionResult Index()
        {
            if (!HttpContext.Request.Cookies.TryGetValue("aesgcmkey", out string aesGcmKey) || string.IsNullOrWhiteSpace(aesGcmKey))
                return LocalRedirect("/kit");

            if (!CryptoKit.TestKey(aesGcmKey))
                return LocalRedirect("/kit");

            var memoryCache = HybridCache.GetMemory();

            string tempkey = UnicTo.Code(5).ToLower();
            memoryCache.Set($"kit:tempkey:{tempkey}", aesGcmKey, DateTime.Now.AddMinutes(20));

            string lampacBase = CoreInit.Host(HttpContext);
            string pluginUrl = $"{lampacBase}/u/{tempkey}";

            string html = System.IO.File.ReadAllText($"{ModInit.folder_mod}/html/unite.html");

            html = html.Replace("{content}", $@"Добавьте плагин на устройство которое хотите привязать
<br><br>
1. Ссылка действительна 20 минут<br>
2. После привязки устройства, ссылку можно удалить из списка расширений <br>
<br><br>
<b>Ваша ссылка (плагин):</b>
<br>
{pluginUrl}

<br><br>
Настройки > Расширения > Добавить плагин > {pluginUrl}<br>
Если статус плагина 200 > Удалить > привязка завершена");

            return Content(html, "text/html; charset=utf-8");
        }


        [AllowAnonymous]
        [Route("/u/{tempkey}")]
        public ActionResult UIndex(string tempkey)
        {
            if (string.IsNullOrWhiteSpace(tempkey))
                return Ok();

            var memoryCache = HybridCache.GetMemory();
            if (!memoryCache.TryGetValue($"kit:tempkey:{tempkey}", out string aesGcmKey))
                return Ok();

            return Content($@"(function() {{
  'use strict';
Lampa.Storage.set('{CoreInit.conf.kit.aesgcmkeyName}', '{aesGcmKey}');
}})();", "application/javascript; charset=utf-8");
        }
    }
}
