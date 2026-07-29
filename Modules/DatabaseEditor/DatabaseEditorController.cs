using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseEditor;

[Authorization(redirectUri: "/weblog/auth")]
public class DatabaseEditorPageController : BaseController
{
    [HttpGet]
    [Route("/database-editor")]
    public ActionResult Index()
    {
        return EditorFile("index.html", "text/html; charset=utf-8");
    }

    [HttpGet]
    [Route("/database-editor/app.js")]
    public ActionResult Script()
    {
        return EditorFile("app.js", "application/javascript; charset=utf-8");
    }

    [HttpGet]
    [Route("/database-editor/style.css")]
    public ActionResult Style()
    {
        return EditorFile("style.css", "text/css; charset=utf-8");
    }

    ActionResult EditorFile(string fileName, string contentType)
    {
        string path = Path.Combine(ModInit.modpath, fileName);
        string content = System.IO.File.ReadAllText(path, Encoding.UTF8);
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        return Content(content, contentType);
    }
}

[Authorization(accessDeniedMessage: "{\"success\":false,\"error\":\"unauthorized\"}")]
public class DatabaseEditorApiController : BaseController
{
    [HttpGet]
    [Route("/database-editor/api/summary")]
    public async Task<ActionResult> Summary()
    {
        try
        {
            return Json(new { success = true, databases = await DatabaseStore.GetSummaryAsync() });
        }
        catch (Exception ex)
        {
            return Unexpected(ex, "summary");
        }
    }

    [HttpGet]
    [Route("/database-editor/api/records")]
    public async Task<ActionResult> Records(string database, string query = null, int page = 1, int pageSize = 25, string user = null)
    {
        try
        {
            var result = await DatabaseStore.GetRecordsAsync(database, query, page, pageSize, user);
            return Json(new
            {
                success = true,
                result.database,
                result.page,
                result.pageSize,
                result.total,
                result.pages,
                result.records
            });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "records");
        }
    }

    [HttpGet]
    [Route("/database-editor/api/users")]
    public async Task<ActionResult> Users(string database)
    {
        try
        {
            return Json(new { success = true, users = await DatabaseStore.GetUsersAsync(database) });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "users");
        }
    }

    [HttpGet]
    [Route("/database-editor/api/record")]
    public async Task<ActionResult> Record(string database, long id)
    {
        try
        {
            var record = await DatabaseStore.GetRecordAsync(database, id);
            return record == null
                ? NotFound(new { success = false, error = "record_not_found" })
                : Json(new { success = true, record });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "record");
        }
    }

    [HttpGet]
    [Route("/database-editor/api/sync-user")]
    public async Task<ActionResult> SyncUser(long id)
    {
        try
        {
            var user = await DatabaseStore.GetSyncUserAsync(id);
            return user == null
                ? NotFound(new { success = false, error = "record_not_found" })
                : Json(new
                {
                    success = true,
                    user,
                    categories = DatabaseStore.SyncEditorCategories,
                    statuses = DatabaseStore.SyncStatusCategories
                });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "sync-user");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/save")]
    public async Task<ActionResult> Save([FromBody] SaveRecordRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            var record = await DatabaseStore.SaveAsync(request);
            return Json(new { success = true, record });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "save");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/delete")]
    public async Task<ActionResult> Delete([FromBody] DeleteRecordRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            bool deleted = await DatabaseStore.DeleteAsync(request?.database, request?.id ?? 0);
            return deleted
                ? Json(new { success = true })
                : NotFound(new { success = false, error = "record_not_found" });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "delete");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/rename-user")]
    public async Task<ActionResult> RenameUser([FromBody] RenameUserRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            var result = await DatabaseStore.RenameUserAsync(request);
            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "rename-user");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/delete-user")]
    public async Task<ActionResult> DeleteUser([FromBody] DeleteUserRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            var result = await DatabaseStore.DeleteUserAsync(request);
            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "delete-user");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/sync-item/save")]
    public async Task<ActionResult> SaveSyncItem([FromBody] SaveSyncItemRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            var categories = await DatabaseStore.SaveSyncItemAsync(request);
            return Json(new { success = true, categories });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "sync-item-save");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/sync-item/delete")]
    public async Task<ActionResult> DeleteSyncItem([FromBody] DeleteSyncItemRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            bool deleted = await DatabaseStore.DeleteSyncItemAsync(request);
            return deleted
                ? Json(new { success = true })
                : NotFound(new { success = false, error = "card_not_found" });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "sync-item-delete");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/backup")]
    public async Task<ActionResult> Backup([FromBody] BackupRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            if (string.IsNullOrWhiteSpace(request?.database) || string.Equals(request.database, "all", StringComparison.OrdinalIgnoreCase))
            {
                var backups = await DatabaseStore.BackupAllAsync();
                return Json(new { success = true, backups });
            }

            string path = await DatabaseStore.BackupAsync(request?.database);
            return Json(new { success = true, path, database = request.database });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "backup");
        }
    }

    [HttpGet]
    [Route("/database-editor/api/backups")]
    public ActionResult Backups(string database)
    {
        try
        {
            return Json(new { success = true, backups = DatabaseStore.GetBackups(database) });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "backups");
        }
    }

    [HttpPost]
    [Route("/database-editor/api/restore")]
    public async Task<ActionResult> Restore([FromBody] RestoreBackupRequest request)
    {
        if (!IsEditorRequest())
            return BadRequest(new { success = false, error = "invalid_editor_request" });

        try
        {
            var result = await DatabaseStore.RestoreAsync(request);
            return Json(new { success = true, result });
        }
        catch (Exception ex)
        {
            return KnownOrUnexpected(ex, "restore");
        }
    }

    bool IsEditorRequest() => string.Equals(Request.Headers["X-Database-Editor"], "1", StringComparison.Ordinal);

    ActionResult KnownOrUnexpected(Exception ex, string operation)
    {
        if (ex is DatabaseEditorValidationException)
            return BadRequest(new { success = false, error = ex.Message });

        if (ex is DatabaseEditorConflictException)
            return Conflict(new { success = false, error = ex.Message });

        if (ex is DatabaseEditorBusyException)
            return StatusCode(503, new { success = false, error = ex.Message });

        return Unexpected(ex, operation);
    }

    ActionResult Unexpected(Exception ex, string operation)
    {
        Serilog.Log.Error(ex, "DatabaseEditor {Operation} failed", operation);
        return StatusCode(500, new { success = false, error = "internal_error" });
    }
}
