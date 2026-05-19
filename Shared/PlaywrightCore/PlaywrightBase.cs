using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Shared.Services;
using Shared.Models.SQL;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Shared.PlaywrightCore;

public enum PlaywrightStatus
{
    disabled,
    headless,
    NoHeadless
}

public class PlaywrightBase
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<PlaywrightBase>();

    static DateTime _nextClearDb = default;

    public TaskCompletionSource<string> completionSource { get; private set; } = new TaskCompletionSource<string>();

    #region WaitPageResult
    async public Task<string> WaitPageResult(int seconds = 10)
    {
        try
        {
            var completionTask = completionSource.Task;
            if (completionTask.IsCompleted || completionTask.IsCanceled)
                return completionTask.Result;

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(seconds));

            var completedTask = await Task.WhenAny(completionTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == completionTask)
                return await completionTask;

            return null;
        }
        catch { return null; }
    }
    #endregion


    #region InitializationAsync
    async public static Task<bool> InitializationAsync()
    {
        try
        {
            if (!CoreInit.conf.chromium.enable && !CoreInit.conf.firefox.enable)
                return false;

            if (!File.Exists(".playwright/package/index.js"))
            {
                Console.WriteLine("Playwright: package not found");
                return false;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (CoreInit.conf.chromium.Headless == false || CoreInit.conf.firefox.Headless == false))
            {
                if (!File.Exists("/usr/bin/Xvfb"))
                {
                    Console.WriteLine("Playwright: install xvfb");
                    await Bash.ComandAsync("apt update");
                    await Bash.ComandAsync("apt install -y xvfb");
                }

                _ = Bash.ComandAsync("Xvfb :99 -screen 0 1280x1024x24").ConfigureAwait(false);
                Environment.SetEnvironmentVariable("DISPLAY", ":99");
                await Task.Delay(TimeSpan.FromSeconds(5));
                Console.WriteLine("Playwright: Xvfb 99");
            }

            Console.WriteLine("Playwright: Initialization");
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_422c14f9");
            Console.WriteLine($"Playwright: {ex.Message}");
            return false;
        }
    }
    #endregion

    #region DownloadFile
    async public static Task<bool> DownloadFile(string uri, string outfile, string folder = null)
    {
        if (File.Exists($"{outfile}.ok"))
            return true;

        if (File.Exists(outfile))
            File.Delete(outfile);

        Directory.CreateDirectory(Path.GetDirectoryName(outfile));

        Console.WriteLine($"Playwright: Download {outfile}");

        if (await Http.DownloadFile(uri, outfile))
        {
            File.Create($"{outfile}.ok");

            if (outfile.EndsWith(".zip"))
            {
                Console.WriteLine($"Playwright: unzip {outfile}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    await Bash.ComandAsync($"unzip {Path.Combine(AppContext.BaseDirectory, outfile)} -d {Path.Combine(AppContext.BaseDirectory, ".playwright", folder ?? string.Empty)}");
                }
                else
                {
                    ZipFile.ExtractToDirectory(outfile, ".playwright/" + folder, overwriteFiles: true);
                }

                File.Delete(outfile);
            }

            return true;
        }
        else
        {
            File.Delete(outfile);
            return false;
        }
    }
    #endregion

    #region IframeUrl
    public static string IframeUrl(string link) => $"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/api/chromium/iframe?src={HttpUtility.UrlEncode(link)}";

    public static string IframeHtml(string link) => $@"<html lang=""ru"">
                <head>
                    <meta charset=""UTF-8"">
                    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
                    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no"">
                </head>
                <body>
                    <iframe id=""player"" width=""560"" height=""400"" src=""{link}"" frameborder=""0"" allow=""*"" allowfullscreen></iframe>
                </body>
            </html>";
    #endregion

    #region AbortOrCache
    async public static ValueTask<bool> AbortOrCache(IPage page, IRoute route, bool abortMedia = false, bool fullCacheJS = false, string patterCache = null)
    {
        try
        {
            if (Regex.IsMatch(route.Request.Url, "(image.tmdb.org|yandex\\.|google-analytics|yahoo\\.|fonts.googleapis|googletagmanager|opensubtitles\\.|/favicon\\.ico$)"))
            {
                await route.AbortAsync();
                return true;
            }

            if (abortMedia && Regex.IsMatch(route.Request.Url.Split("?")[0], "\\.(woff2?|vtt|srt|css|svg|jpe?g|png|gif|webp|ico|ts|m4s)$"))
            {
                if (CoreInit.conf.chromium.consoleLog || CoreInit.conf.firefox.consoleLog)
                    Console.WriteLine($"Playwright: Abort {route.Request.Url}");

                await route.AbortAsync();
                return true;
            }

            if (route.Request.Method == "GET")
            {
                bool valid = false;
                string memkey = route.Request.Url;

                if ((fullCacheJS && route.Request.Url.Contains(".js")) || Regex.IsMatch(route.Request.Url, "(\\.googleapis\\.com/css|gstatic\\.com|plrjs\\.com)", RegexOptions.IgnoreCase))
                {
                    valid = true;
                    memkey = route.Request.Url.Split("?")[0];
                }
                else if (Regex.IsMatch(route.Request.Url, "\\.(js|wasm)$") || Regex.IsMatch(route.Request.Url, "\\.(css|woff2?|svg|jpe?g|png|gif)"))
                {
                    valid = true;
                    memkey = route.Request.Url;
                }
                else if (patterCache != null && Regex.IsMatch(route.Request.Url, patterCache))
                {
                    valid = true;
                    memkey = route.Request.Url;
                }

                if (valid)
                {
                    #region ClearDb
                    try
                    {
                        if (DateTime.Now > _nextClearDb)
                        {
                            _nextClearDb = DateTime.Now.AddMinutes(5);

                            var now = DateTime.Now;

                            await using (var sqlDb = new PlaywrightContext())
                            {
                                await sqlDb.files
                                    .Where(i => now > i.ex)
                                    .ExecuteDeleteAsync();
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_csivqeb0");
                    }
                    #endregion

                    PlaywrightSqlModel doc = null;

                    await using (var sqlDb = new PlaywrightContext())
                        doc = sqlDb.files.Find(memkey);

                    if (doc?.content != null)
                    {
                        if (CoreInit.conf.chromium.consoleLog || CoreInit.conf.firefox.consoleLog)
                            Console.WriteLine($"Playwright: CACHE {route.Request.Url}");

                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            BodyBytes = doc.content,
                            Headers = JsonSerializer.Deserialize<Dictionary<string, string>>(doc.headers)
                        });
                    }
                    else
                    {
                        if (CoreInit.conf.chromium.consoleLog || CoreInit.conf.firefox.consoleLog)
                            Console.WriteLine($"Playwright: MISS {route.Request.Url}");

                        await route.ContinueAsync();

                        try
                        {
                            var response = await page.WaitForResponseAsync(route.Request.Url);
                            if (response != null)
                            {
                                var content = await response.BodyAsync();
                                if (content != null)
                                {
                                    await using (var sqlDb = new PlaywrightContext())
                                    {
                                        sqlDb.files.Add(new PlaywrightSqlModel()
                                        {
                                            Id = memkey,
                                            ex = DateTime.Now.AddHours(1),
                                            headers = JsonSerializer.Serialize(response.Headers.ToDictionary()),
                                            content = content
                                        });

                                        await sqlDb.SaveChangesAsync();
                                    }
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_0g9hewex");
                        }
                    }

                    return true;
                }
            }

            if (CoreInit.conf.chromium.consoleLog || CoreInit.conf.firefox.consoleLog)
                Console.WriteLine($"Playwright: {route.Request.Method} {route.Request.Url}");

            return false;
        }
        catch { return false; }
    }
    #endregion

    #region GotoAsync
    public static void GotoAsync(IPage page, string uri)
    {
        var options = new PageGotoOptions
        {
            Timeout = 40_000, // 40 секунд
            WaitUntil = WaitUntilState.DOMContentLoaded
        };

        _ = page.GotoAsync(uri, options).ConfigureAwait(false);
    }
    #endregion

    #region ConsoleLog
    public static void ConsoleLog(Func<string> func)
        => ConsoleLog(() => (func.Invoke(), null));

    public static void ConsoleLog(Func<(string value, IReadOnlyList<HeadersModel> headers)> func)
    {
        if (CoreInit.conf.chromium.consoleLog || CoreInit.conf.firefox.consoleLog)
        {
            var r = func.Invoke();

            if (r.headers != null && r.headers.Count > 0)
            {
                Console.WriteLine($"\n{r.value}\n{Newtonsoft.Json.JsonConvert.SerializeObject(r.headers.ToDictionary(), Newtonsoft.Json.Formatting.Indented)}\n");
            }
            else
            {
                Console.WriteLine(r.value);
            }
        }
    }
    #endregion
}
