using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shared.Services.Pools.Json;
using SISI.SQL;
using System.Web;

namespace SISI.Controllers
{
    public class HistoryController : BaseController
    {
        static readonly Serilog.ILogger Log = Serilog.Log.ForContext<HistoryController>();

        [HttpGet]
        [Route("sisi/historys")]
        async public Task<ActionResult> List(int pg = 1, int pageSize = 36)
        {
            string md5user = getuser();
            if (md5user == null || !ModInit.conf.history.enable)
                return StatusCode(403, "access denied");

            int total_pages = 0;
            var historys = new List<PlaylistItem>(pageSize);

            await using (var sqlDb = SisiContext.Factory != null
                ? SisiContext.Factory.CreateDbContext()
                : new SisiContext())
            {
                var historysQuery = sqlDb.historys
                    .AsNoTracking()
                    .Where(i => i.user == md5user)
                    .Take(pageSize * 20);

                total_pages = Math.Max(0, await historysQuery.CountAsync() / pageSize) + 1;

                var items = historysQuery
                    .OrderByDescending(i => i.created)
                    .Skip((pg * pageSize) - pageSize)
                    .Take(pageSize);

                if (items.Any())
                {
                    #region getvideLink
                    string getvideLink(PlaylistItem pl)
                    {
                        if (pl.bookmark.site is "phub" or "phubprem")
                            return $"{host}/{pl.bookmark.site}/vidosik?vkey={HttpUtility.UrlEncode(pl.bookmark.href)}";

                        if (pl.bookmark.href?.Contains("_-:-_") == true)
                            return $"{host}/{pl.bookmark.site}/vidosik?uri={EncryptQuery(pl.bookmark.href)}";

                        return $"{host}/{pl.bookmark.site}/vidosik?uri={HttpUtility.UrlEncode(pl.bookmark.href)}";
                    }
                    #endregion

                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(item.json))
                            continue;

                        try
                        {
                            var pl = JsonConvert.DeserializeObject<PlaylistItem>(item.json);
                            if (pl != null)
                            {
                                historys.Add(new PlaylistItem()
                                {
                                    name = pl.name,
                                    video = getvideLink(pl),
                                    picture = pl.bookmark.image != null
                                        ? pl.bookmark.image.StartsWith("bookmarks/") ? $"{host}/{pl.bookmark.image}" : HostImgProxy(new BaseSettings() { plugin = pl.bookmark.site }, pl.bookmark.image)
                                        : null,
                                    time = pl.time,
                                    json = pl.json,
                                    related = pl.related || Regex.IsMatch(pl.bookmark.site, "^(elo|epr|fph|phub|sbg|xmr|xnx|xds)"),
                                    quality = pl.quality,
                                    preview = pl.preview,
                                    model = pl.model,
                                    bookmark = pl.bookmark,
                                    history_uid = pl.history_uid
                                });
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_5ztrqzhr");
                        }
                    }
                }
            }

            return new JsonResult(new Channel()
            {
                list = historys,
                total_pages = total_pages
            });
        }


        [HttpPost]
        [Route("sisi/history/add")]
        async public Task<ActionResult> Add([FromBody] PlaylistItem data)
        {
            string md5user = getuser();
            if (md5user == null || !ModInit.conf.history.enable || data == null || string.IsNullOrEmpty(data?.bookmark?.site) || string.IsNullOrEmpty(data?.bookmark?.href))
                return StatusCode(403, "access denied");

            string uid = CrypTo.md5($"{data.bookmark.site}:{data.bookmark.href}");

            await using (var sqlDb = SisiContext.Factory != null
                ? SisiContext.Factory.CreateDbContext()
                : new SisiContext())
            {
                bool any = await sqlDb.historys.AsNoTracking().AnyAsync(i => i.user == md5user && i.uid == uid);

                if (any == false)
                {
                    #region download image
                    if (ModInit.conf.bookmarks.saveimage)
                    {
                        string pimg = $"bookmarks/img/{uid.Substring(0, 2)}/{uid.Substring(2)}.jpg";

                        if (System.IO.File.Exists($"wwwroot/{pimg}"))
                        {
                            data.bookmark.image = pimg;
                        }
                        else
                        {
                            string img = data.picture ?? data.bookmark.image;
                            if (!string.IsNullOrEmpty(img) && img.StartsWith("http"))
                            {
                                Directory.CreateDirectory($"wwwroot/bookmarks/img/{uid.Substring(0, 2)}");

                                bool success = await Http.DownloadFile(img, $"wwwroot/{pimg}", timeoutSeconds: 10);
                                if (success)
                                    data.bookmark.image = pimg;
                            }
                        }
                    }
                    #endregion

                    data.history_uid = uid;

                    sqlDb.historys.Add(new SisiHistorySqlModel
                    {
                        user = md5user,
                        uid = uid,
                        created = DateTime.UtcNow,
                        json = JsonConvertPool.SerializeObject(data)
                    });

                    await sqlDb.SaveChangesLocks();
                }
            }

            return Json(new
            {
                result = true
            });
        }


        [HttpGet]
        [Route("sisi/history/remove")]
        async public Task<ActionResult> Remove(string id)
        {
            string md5user = getuser();
            if (md5user == null || !ModInit.conf.history.enable || string.IsNullOrEmpty(id))
                return StatusCode(403, "access denied");

            var semaphore = new SemaphorManager(SisiContext.semaphoreKey, TimeSpan.FromSeconds(30));

            try
            {
                bool _acquired = await semaphore.WaitAsync();
                if (!_acquired)
                    return StatusCode(502, "semaphore");

                await using (var sqlDb = SisiContext.Factory != null
                    ? SisiContext.Factory.CreateDbContext()
                    : new SisiContext())
                {
                    await sqlDb.historys
                        .Where(i => i.user == md5user && i.uid == id)
                        .ExecuteDeleteAsync();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_y9kqg240");
            }
            finally
            {
                semaphore.Release();
            }

            return Json(new
            {
                result = true,
            });
        }



        string getuser()
        {
            string user_id = requestInfo.user_uid;
            if (string.IsNullOrEmpty(user_id))
                return null;

            string profile_id = getProfileid();
            if (!string.IsNullOrEmpty(profile_id))
                return CrypTo.md5($"{user_id}_{profile_id}");

            return CrypTo.md5(user_id);
        }

        string getProfileid()
        {
            if (HttpContext.Request.Query.TryGetValue("profile_id", out var profile_id) && !string.IsNullOrEmpty(profile_id) && profile_id != "0")
                return profile_id;

            return string.Empty;
        }
    }
}
