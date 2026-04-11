using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace WatchTogether
{
    [Route("watchtogether")]
    public class WatchTogetherController : BaseController
    {   
        [AllowAnonymous]
        [HttpGet]
        [Route("/watchtogether.js")]
        [Route("/watchtogether/js/{token}")]
        public ActionResult GetPlugin(string token = null)
        {
            string host = $"{Request.Scheme}://{Request.Host.Value}";
            
            string memKey = "watchtogether:plugin.js";
            if (!memoryCache.TryGetValue(memKey, out string js))
            {
                js = System.IO.File.ReadAllText($"{ModInit.modpath}/plugin.js");
                memoryCache.Set(memKey, js, TimeSpan.FromMinutes(10));
            }
            
            js = js.Replace("{localhost}", host);

            if (!string.IsNullOrEmpty(token))
                js = js.Replace("{token}", System.Web.HttpUtility.UrlEncode(token));
            else
                js = js.Replace("{token}", "");

            return Content(js, "application/javascript; charset=utf-8");
        }

        [HttpGet]
        [Route("/watchtogether/create")]
        public async Task<ActionResult> CreateRoom([FromQuery] string title, [FromQuery] int tmdb_id, [FromQuery] string source, [FromQuery] string type)
        {
            string id = GenerateRoomId();
            await using (var db = SqlContext.Create())
            {
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
                await db.Rooms.AddAsync(room);
                await db.SaveChangesAsync();
            }

            return new JsonResult(new { id = id });
        }

        [HttpGet]
        [Route("/watchtogether/info")]
        public async Task<ActionResult> GetRoomInfo([FromQuery] string id)
        {
            if (string.IsNullOrEmpty(id))
                return BadRequest("id required");

            await using (var db = SqlContext.Create())
            {
                var room = await db.Rooms.FirstOrDefaultAsync(r => r.id == id);
                if (room == null)
                    return NotFound(new { error = "Room not found" });

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
        }

        private string GenerateRoomId()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}