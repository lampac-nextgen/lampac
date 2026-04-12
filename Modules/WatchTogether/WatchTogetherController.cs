using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using System;
using System.IO;
using System.Linq;

namespace WatchTogether
{
    [Route("watchtogether")]
    public class WatchTogetherController : BaseController
    {   
        [AllowAnonymous]
        [HttpGet]
        [Route("/watchtogether.js")]
        public ActionResult GetPlugin()
        {
            string memKey = "watchtogether:plugin.js";
            if (!memoryCache.TryGetValue(memKey, out string rawJs))
            {
                rawJs = System.IO.File.ReadAllText(Path.Combine(ModInit.modpath, "plugin.js"));
                memoryCache.Set(memKey, rawJs, TimeSpan.FromMinutes(10));
            }
            
            string js = rawJs;
            js = js.Replace("{localhost}", this.host);

            return Content(js, "application/javascript; charset=utf-8");
        }

        [HttpGet]
        [Route("/watchtogether/create")]
        public ActionResult CreateRoom([FromQuery] string title, [FromQuery] int tmdb_id, [FromQuery] string source, [FromQuery] string type)
        {
            string id = GenerateRoomId();
            var room = new RoomModel
            {
                id = id,
                title = title ?? string.Empty,
                tmdb_id = tmdb_id,
                source = string.IsNullOrEmpty(source) ? "tmdb" : source,
                type = string.IsNullOrEmpty(type) ? "movie" : type,
                state = "playing",
                position = 0,
                season = 1,
                episode = 1,
                create_time = DateTime.UtcNow,
                update_time = DateTime.UtcNow
            };
            
            RoomDb.Rooms.TryAdd(id, room);

            return new JsonResult(new { id = id });
        }

        [HttpGet]
        [Route("/watchtogether/info")]
        public ActionResult GetRoomInfo([FromQuery] string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest("id required");

            if (RoomDb.Rooms.TryGetValue(id, out var room))
            {
                return new JsonResult(new
                {
                    id = room.id,
                    title = room.title,
                    tmdb_id = room.tmdb_id,
                    source = room.source,
                    type = room.type,
                    state = room.state,
                    position = room.position,
                    season = room.season,
                    episode = room.episode
                });
            }

            return NotFound(new { error = "Room not found" });
        }

        private string GenerateRoomId()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}