using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.Pools.Json;
using Shared.Services.Utilities;
using SyncEvents;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using IO = System.IO;

namespace Storage;

public class StorageController : BaseController
{
    const long maxRequestSize = 10 * 1024 * 1024;

    #region backup.js
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("backup.js")]
    [Route("backup/js/{token}")]
    public ActionResult Backup(string token)
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/backup.js", "backup.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion


    #region Get
    [HttpGet]
    [Route("/storage/get")]
    async public Task<ActionResult> Get(string path, string pathfile, bool responseInfo)
    {
        string outFile = getFilePath(path, pathfile, false);
        if (outFile == null || !IO.File.Exists(outFile))
            return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

        var file = new FileInfo(outFile);
        var fileInfo = new { file.Name, path = outFile, file.Length, changeTime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds() };

        if (responseInfo)
            return Json(new { success = true, uid = requestInfo.user_uid, fileInfo });

        var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

        try
        {
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
            {
                HttpContext.Response.StatusCode = 502;
                return ContentTo("{\"success\": false, \"msg\": \"semaphore\"}");
            }

            string data = await IO.File.ReadAllTextAsync(outFile);
            return Json(new { success = true, uid = requestInfo.user_uid, fileInfo, data });
        }
        catch
        {
            HttpContext.Response.StatusCode = 503;
            return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
        }
        finally
        {
            semaphore.Release();
        }
    }
    #endregion

    #region Set
    [HttpPost]
    [Route("/storage/set")]
    async public Task<ActionResult> Set([FromQuery] string path, [FromQuery] string pathfile, [FromQuery] string connectionId)
    {
        if (HttpContext.Request.ContentLength > maxRequestSize)
            return ContentTo("{\"success\": false, \"msg\": \"max_size\"}");

        string outFile = getFilePath(path, pathfile, true);
        if (outFile == null)
            return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

        using (var msm = PoolInvk.msm.GetStream())
        {
            try
            {
                using (var byteBuf = new BufferPool())
                {
                    int bytesRead;
                    var memBuf = byteBuf.Memory;

                    while ((bytesRead = await HttpContext.Request.Body.ReadAsync(memBuf, HttpContext.RequestAborted).ConfigureAwait(false)) > 0)
                        msm.Write(memBuf.Span.Slice(0, bytesRead));
                }
            }
            catch
            {
                HttpContext.Response.StatusCode = 503;
                return ContentTo("{\"success\": false, \"msg\": \"Request.Body.CopyToAsync\"}");
            }

            var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

            try
            {
                bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                if (!_acquired)
                {
                    HttpContext.Response.StatusCode = 502;
                    return ContentTo("{\"success\": false, \"msg\": \"semaphore\"}");
                }

                msm.Position = 0;
                await using (var fileStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 0,
                    options: FileOptions.Asynchronous))
                {
                    await msm.CopyToAsync(fileStream).ConfigureAwait(false);
                }
            }
            catch
            {
                HttpContext.Response.StatusCode = 503;
                return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        if (!string.IsNullOrEmpty(requestInfo.user_uid))
        {
            string edata = JsonConvertPool.SerializeObject(new { path, pathfile });
            _ = NwsEvents.SendAsync(connectionId, requestInfo.user_uid, "storage", edata).ConfigureAwait(false);
        }

        var inf = new FileInfo(outFile);

        return Json(new
        {
            success = true,
            uid = requestInfo.user_uid,
            fileInfo = new { inf.Name, path = outFile, inf.Length, changeTime = new DateTimeOffset(inf.LastWriteTimeUtc).ToUnixTimeMilliseconds() }
        });
    }
    #endregion

    #region TempGet
    [HttpGet]
    [Route("/storage/temp/{key}")]
    async public Task<ActionResult> TempGet(string key, bool responseInfo)
    {
        if (!ModInit.conf.enableTemp || string.IsNullOrEmpty(key))
            return ContentTo("{\"success\": false, \"msg\": \"403\"}");

        string outFile = getFilePath("temp", null, false, user_uid: key);
        if (outFile == null || !IO.File.Exists(outFile))
            return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

        var file = new FileInfo(outFile);
        var fileInfo = new { file.Name, path = outFile, file.Length, changeTime = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds() };

        if (responseInfo)
            return Json(new { success = true, uid = requestInfo.user_uid, fileInfo });

        var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

        try
        {
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
            {
                HttpContext.Response.StatusCode = 502;
                return ContentTo("{\"success\": false, \"msg\": \"semaphore\"}");
            }

            string data = await IO.File.ReadAllTextAsync(outFile);

            return Json(new { success = true, uid = requestInfo.user_uid, fileInfo, data });
        }
        catch
        {
            HttpContext.Response.StatusCode = 503;
            return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
        }
        finally
        {
            semaphore.Release();
        }
    }
    #endregion

    #region TempSet
    [HttpPost]
    [Route("/storage/temp/{key}")]
    async public Task<ActionResult> TempSet(string key)
    {
        if (!ModInit.conf.enableTemp || string.IsNullOrEmpty(key))
            return ContentTo("{\"success\": false, \"msg\": \"403\"}");

        if (HttpContext.Request.ContentLength > maxRequestSize)
            return ContentTo("{\"success\": false, \"msg\": \"max_size\"}");

        string outFile = getFilePath("temp", null, true, user_uid: key);
        if (outFile == null)
            return ContentTo("{\"success\": false, \"msg\": \"outFile\"}");

        using (var msm = PoolInvk.msm.GetStream())
        {
            try
            {
                using (var byteBuf = new BufferPool())
                {
                    int bytesRead;
                    var memBuf = byteBuf.Memory;

                    while ((bytesRead = await HttpContext.Request.Body.ReadAsync(memBuf, HttpContext.RequestAborted).ConfigureAwait(false)) > 0)
                        msm.Write(memBuf.Span.Slice(0, bytesRead));
                }
            }
            catch
            {
                HttpContext.Response.StatusCode = 503;
                return ContentTo("{\"success\": false, \"msg\": \"Request.Body.CopyToAsync\"}");
            }

            var semaphore = new SemaphorManager(outFile, TimeSpan.FromSeconds(20));

            try
            {
                bool _acquired = await semaphore.WaitAsync();
                if (!_acquired)
                {
                    HttpContext.Response.StatusCode = 502;
                    return ContentTo("{\"success\": false, \"msg\": \"semaphore\"}");
                }

                msm.Position = 0;
                await using (var fileStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: PoolInvk.bufferSize,
                    options: FileOptions.Asynchronous))
                {
                    await msm.CopyToAsync(fileStream).ConfigureAwait(false);
                }
            }
            catch
            {
                HttpContext.Response.StatusCode = 503;
                return ContentTo("{\"success\": false, \"msg\": \"fileLock\"}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        var inf = new FileInfo(outFile);

        return Json(new
        {
            success = true,
            uid = requestInfo.user_uid,
            fileInfo = new { inf.Name, path = outFile, inf.Length, changeTime = new DateTimeOffset(inf.LastWriteTimeUtc).ToUnixTimeMilliseconds() }
        });
    }
    #endregion


    #region getFilePath
    string getFilePath(string path, string pathfile, bool createDirectory, string user_uid = null)
    {
        path = Regex.Replace(path, "[^a-z0-9\\-]", "", RegexOptions.IgnoreCase);

        string id = user_uid ?? requestInfo.user_uid;
        if (string.IsNullOrEmpty(id))
            return null;

        id += pathfile;
        string md5key = CrypTo.md5(id);

        if (path == "temp")
        {
            return $"database/storage/{path}/{md5key}";
        }
        else
        {
            if (createDirectory)
                Directory.CreateDirectory($"database/storage/{path}/{md5key.Substring(0, 2)}");

            return $"database/storage/{path}/{md5key.Substring(0, 2)}/{md5key.Substring(2)}";
        }
    }
    #endregion
}
