using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Shared.Services.Pools.Json;
using SISI.SQL;
using System.Web;

namespace SISI.Controllers
{
    public class BookmarkController : BaseController
    {
        static readonly Serilog.ILogger Log = Serilog.Log.ForContext<BookmarkController>();

        [HttpGet]
        [Route("sisi/bookmarks")]
        async public Task<ActionResult> List(string search, string model, int pg = 1, int pageSize = 36)
        {
            string md5user = getuser();
            if (md5user == null)
                return StatusCode(403, "access denied");

            var menu = new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Поиск",
                    search_on = "search_on",
                    playlist_url = $"{host}/sisi/bookmarks",
                }
            };

            int total_pages = 0;
            var bookmarks = new List<PlaylistItem>(pageSize);

            await using (var sqlDb = SisiContext.Factory != null
                ? SisiContext.Factory.CreateDbContext()
                : new SisiContext())
            {
                var bookmarksQuery = sqlDb.bookmarks
                    .AsNoTracking()
                    .Where(i => i.user == md5user);

                total_pages = Math.Max(0, await bookmarksQuery.CountAsync() / pageSize) + 1;

                #region Модель
                var menu_models = new MenuItem()
                {
                    title = $"Модель: {model ?? "выбрать"}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(20)
                };

                foreach (var m in await bookmarksQuery.Where(i => i.model != null).Select(i => i.model).ToHashSetAsync())
                {
                    if (string.IsNullOrEmpty(m))
                        continue;

                    menu_models.submenu.Add(new MenuItem()
                    {
                        title = m,
                        playlist_url = $"{host}/sisi/bookmarks?model={HttpUtility.UrlEncode(m)}"
                    });
                }

                if (menu_models.submenu.Count > 0)
                    menu.Add(menu_models);
                #endregion

                var items = bookmarksQuery
                    .OrderByDescending(i => i.created)
                    .Skip((pg * pageSize) - pageSize)
                    .Take(pageSize);

                if (!string.IsNullOrEmpty(search))
                    items = items.Where(i => i.name != null && i.name.Contains(search));

                if (!string.IsNullOrEmpty(model))
                    items = items.Where(i => i.model == model);

                if (items.Any())
                {
                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(item.json))
                            continue;

                        try
                        {
                            var pl = JsonConvert.DeserializeObject<PlaylistItem>(item.json);
                            if (pl != null)
                            {
                                bookmarks.Add(new PlaylistItem()
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
                                    preview = pl.preview != null && pl.preview.StartsWith("bookmarks/")
                                        ? $"{host}/{pl.preview}"
                                        : null,
                                    model = pl.model,
                                    bookmark = new Bookmark() { uid = pl.bookmark.uid }
                                });
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_gw4vznko");
                        }
                    }
                }
            }

            return new JsonResult(new Channel()
            {
                menu = menu,
                list = bookmarks,
                total_pages = total_pages
            });
        }


        [HttpPost]
        [Route("sisi/bookmark/add")]
        public async Task<ActionResult> Add([FromBody] PlaylistItem data)
        {
            string md5user = getuser();
            if (md5user == null || data == null || string.IsNullOrEmpty(data?.bookmark?.site) || string.IsNullOrEmpty(data?.bookmark?.href))
                return StatusCode(403, "access denied");

            string uid = CrypTo.md5($"{data.bookmark.site}:{data.bookmark.href}");

            await using (var sqlDb = SisiContext.Factory != null
                ? SisiContext.Factory.CreateDbContext()
                : new SisiContext())
            {
                bool any = await sqlDb.bookmarks.AsNoTracking().AnyAsync(i => i.user == md5user && i.uid == uid);
                if (any == false)
                {
                    string newimage = null;

                    #region download image
                    if (ModInit.conf.bookmarks.saveimage)
                    {
                        string pimg = $"bookmarks/img/{uid.Substring(0, 2)}/{uid.Substring(2)}.jpg";

                        if (System.IO.File.Exists($"wwwroot/{pimg}"))
                        {
                            newimage = pimg;
                        }
                        else
                        {
                            string img = data.picture ?? data.bookmark.image;
                            if (!string.IsNullOrEmpty(img) && img.StartsWith("http"))
                            {
                                Directory.CreateDirectory($"wwwroot/bookmarks/img/{uid.Substring(0, 2)}");

                                bool success = await Http.DownloadFile(img, $"wwwroot/{pimg}", timeoutSeconds: 10);
                                if (success)
                                    newimage = pimg;
                            }
                        }
                    }
                    #endregion

                    #region download preview
                    if (ModInit.conf.bookmarks.savepreview)
                    {
                        if (data.preview != null)
                        {
                            string path = $"bookmarks/preview/{uid.Substring(0, 2)}/{uid.Substring(2)}.{(data.preview.Contains(".webm") ? "webm" : "mp4")}";

                            if (System.IO.File.Exists($"wwwroot/{path}"))
                            {
                                data.preview = path;
                            }
                            else
                            {
                                Directory.CreateDirectory($"wwwroot/bookmarks/preview/{uid.Substring(0, 2)}");

                                bool success = await Http.DownloadFile(data.preview, $"wwwroot/{path}", timeoutSeconds: 10);
                                if (success)
                                    data.preview = path;
                            }
                        }
                    }
                    #endregion

                    var b = data.bookmark;
                    data.bookmark = new Bookmark()
                    {
                        href = b.href,
                        image = newimage ?? b.image,
                        site = b.site,
                        uid = uid
                    };

                    sqlDb.bookmarks.Add(new SisiBookmarkSqlModel
                    {
                        user = md5user,
                        uid = uid,
                        created = DateTime.UtcNow,
                        json = JsonConvertPool.SerializeObject(data),
                        name = data.name,
                        model = data.model?.name
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
        [HttpPost]
        [Route("sisi/bookmark/remove")]
        async public Task<ActionResult> Remove(string id)
        {
            string md5user = getuser();
            if (md5user == null || string.IsNullOrEmpty(id))
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
                    await sqlDb.bookmarks
                        .Where(i => i.user == md5user && i.uid == id)
                        .ExecuteDeleteAsync();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_vxndpq04");
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

        string getvideLink(PlaylistItem pl)
        {
            if (pl.bookmark.site is "phub" or "phubprem")
                return $"{host}/{pl.bookmark.site}/vidosik?vkey={HttpUtility.UrlEncode(pl.bookmark.href)}";

            if (pl.bookmark.href?.Contains("_-:-_") == true)
                return $"{host}/{pl.bookmark.site}/vidosik?uri={EncryptQuery(pl.bookmark.href)}";

            return $"{host}/{pl.bookmark.site}/vidosik?uri={HttpUtility.UrlEncode(pl.bookmark.href)}";
        }
    }
}
