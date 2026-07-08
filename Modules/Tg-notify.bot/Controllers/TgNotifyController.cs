using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using System.IO;
using System.Threading.Tasks;

namespace TelegramBot.Controllers
{
    public class TgNotifyController : BaseController
    {
        [HttpGet]
        [AllowAnonymous]
        [Route("tg-notify.js")]
        public ActionResult TgNotifyPlugin()
        {
            // Ищем плагин в возможных расположениях модуля (NextGen: module/ или mods/)
            string file = null;
            foreach (var p in new[]
            {
                "module/Tg-notify.bot/tg-notify.js",
                "module/Community/Tg-notify.bot/tg-notify.js",
                "mods/TelegramBot/tg-notify.js",
                "module/TelegramBot/tg-notify.js",
                "plugins/tg-notify.js",
                "wwwroot/plugins/tg-notify.js"
            })
            {
                if (System.IO.File.Exists(p)) { file = p; break; }
            }

            if (file == null)
                return Content(string.Empty, "application/javascript; charset=utf-8");

            var js = System.IO.File.ReadAllText(file);
            js = js.Replace("{localhost}", host);
            return Content(js, "application/javascript; charset=utf-8");
        }

        [HttpPost]
        [Route("/api/tg/subscribe")]
        public async Task<ActionResult> Subscribe()
        {
            if (!ModInit.IsRunning)
                return Json(new { success = false, msg = "bot_not_running" });

            var uid = requestInfo.user_uid;
            if (string.IsNullOrEmpty(uid))
                return Json(new { success = false, msg = "no_uid" });

            using var reader = new StreamReader(HttpContext.Request.Body);
            var json = JObject.Parse(await reader.ReadToEndAsync());

            var result = await ModInit.SubscribeApi(
                uid,
                json.Value<int>("tmdb_id"),
                json.Value<string>("title") ?? "",
                json.Value<string>("voice") ?? "",
                json.Value<int>("season"),
                json.Value<int>("episode"),
                json.Value<string>("mirage_orid") ?? "",
                json.Value<int>("mirage_voice_id"),
                json.Value<int>("voice_episode"),
                json.Value<string>("collaps_orid") ?? "",
                json.Value<string>("voice_source") ?? "",
                json.Value<int>("kp_id")
            );
            return Json(result);
        }

        [HttpPost]
        [Route("/api/tg/unsubscribe")]
        public async Task<ActionResult> Unsubscribe()
        {
            if (!ModInit.IsRunning)
                return Json(new { success = false, msg = "bot_not_running" });

            var uid = requestInfo.user_uid;
            if (string.IsNullOrEmpty(uid))
                return Json(new { success = false, msg = "no_uid" });

            using var reader = new StreamReader(HttpContext.Request.Body);
            var json = JObject.Parse(await reader.ReadToEndAsync());

            return Json(ModInit.UnsubscribeApi(uid, json.Value<int>("tmdb_id"), json.Value<string>("voice")));
        }

        [HttpGet]
        [Route("/api/tg/status")]
        public ActionResult Status(int tmdb_id)
        {
            var uid = requestInfo.user_uid;
            if (string.IsNullOrEmpty(uid))
                return Json(new { success = false, msg = "no_uid" });

            return Json(ModInit.StatusApi(uid, tmdb_id));
        }

        [HttpGet]
        [Route("/api/tg/voices")]
        public async Task<ActionResult> Voices(string title, int year, int season, int tmdb_id)
        {
            return Json(await ModInit.GetVoicesApi(title ?? "", year, season, tmdb_id));
        }

        [HttpGet]
        [Route("/api/tg/link")]
        public async Task<ActionResult> GetLink()
        {
            var uid = requestInfo.user_uid;
            if (string.IsNullOrEmpty(uid))
                return Json(new { success = false, msg = "no_uid" });

            return Json(await ModInit.LinkApi(uid));
        }

        /// <summary>
        /// Список всех подписок пользователя с метаданными (для страницы подписок в Lampa)
        /// </summary>
        [HttpGet]
        [Route("/api/tg/subscriptions")]
        public ActionResult Subscriptions()
        {
            var uid = requestInfo.user_uid;
            if (string.IsNullOrEmpty(uid))
                return Json(new { success = false, msg = "no_uid" });

            return Json(ModInit.SubscriptionsApi(uid));
        }
    }
}
