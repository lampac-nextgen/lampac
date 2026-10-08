using Microsoft.AspNetCore.Http;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseEditor;

public class ModInit : IModuleLoaded
{
    public static string modpath;

    static readonly Func<bool, EventMiddleware, Task<bool>> MiddlewareHandler = OnMiddleware;

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;
        EventListener.MiddlewareAsync += MiddlewareHandler;
    }

    public void Dispose()
    {
        EventListener.MiddlewareAsync -= MiddlewareHandler;
    }

    static async Task<bool> OnMiddleware(bool first, EventMiddleware e)
    {
        if (first || !HttpMethods.IsGet(e.httpContext.Request.Method))
            return true;

        string path = (e.httpContext.Request.Path.Value ?? string.Empty).TrimEnd('/');
        if (!string.Equals(path, "/weblog", StringComparison.OrdinalIgnoreCase))
            return true;

        string weblogPath = Path.Combine(AppContext.BaseDirectory, "module", "WebLog", "index.html");
        if (!File.Exists(weblogPath))
            return true;

        string html = File.ReadAllText(weblogPath, Encoding.UTF8);
        if (!html.Contains("href=\"/database-editor\"", StringComparison.OrdinalIgnoreCase))
        {
            const string activeLink = "<a href=\"/weblog\" aria-current=\"page\">Weblog</a>";
            const string telemetryLink = "<a href=\"/telemetry\">Telemetry</a>";
            const string databaseLink = "<a href=\"/database-editor\">Databases</a>";

            if (html.Contains(activeLink, StringComparison.Ordinal))
                html = html.Replace(activeLink, activeLink + "\n        " + databaseLink, StringComparison.Ordinal);
            else if (html.Contains(telemetryLink, StringComparison.Ordinal))
                html = html.Replace(telemetryLink, databaseLink + "\n        " + telemetryLink, StringComparison.Ordinal);
            else
            {
                const string navEnd = "</nav>";
                int navIndex = html.IndexOf(navEnd, StringComparison.OrdinalIgnoreCase);
                if (navIndex >= 0)
                    html = html.Insert(navIndex, databaseLink + "\n      ");
            }
        }

        e.httpContext.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        e.httpContext.Response.ContentType = "text/html; charset=utf-8";
        await e.httpContext.Response.WriteAsync(html, e.httpContext.RequestAborted);
        return false;
    }
}
