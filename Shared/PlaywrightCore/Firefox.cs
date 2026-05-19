using Microsoft.Playwright;
using Shared.Services;
using Shared.Models.Browser;
using System.Runtime.InteropServices;
using System.Threading;

namespace Shared.PlaywrightCore;

public class Firefox : PlaywrightBase, IDisposable
{
    #region static
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<Firefox>();

    static List<KeepopenPage> pages_keepopen = new();

    public static long stats_keepopen { get; set; }

    public static long stats_newcontext { get; set; }

    static IPlaywright playwright = null;
    static IBrowser browser = null;

    static bool shutdown = false;

    public static PlaywrightStatus Status { get; private set; } = PlaywrightStatus.disabled;

    public static int ContextsCount => browser?.Contexts?.Count ?? 0;

    async public static Task CreateAsync()
    {
        try
        {
            var init = CoreInit.conf.firefox;
            if (!init.enable || browser != null || shutdown)
                return;

            string executablePath = init.executablePath;

            if (string.IsNullOrEmpty(executablePath))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X86:
                        case Architecture.X64:
                            {
                                executablePath = ".playwright\\firefox\\camoufox.exe";
                                break;
                            }
                        default:
                            Console.WriteLine("Firefox: Architecture unknown");
                            return;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X64:
                        case Architecture.Arm64:
                            {
                                executablePath = ".playwright/Camoufox.app/Contents/MacOS/camoufox";
                                break;
                            }
                        default:
                            Console.WriteLine("Firefox: Architecture unknown");
                            return;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    switch (RuntimeInformation.ProcessArchitecture)
                    {
                        case Architecture.X86:
                        case Architecture.X64:
                        case Architecture.Arm64:
                            executablePath = ".playwright/firefox/camoufox";
                            break;
                        default:
                            Console.WriteLine("Firefox: Architecture unknown");
                            return;
                    }
                }
                else
                {
                    Console.WriteLine("Firefox: IsOSPlatform unknown");
                    return;
                }
            }

            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            {
                Console.WriteLine("Firefox: firefox is not installed, please specify full path in executablePath");
                return;
            }

            Console.WriteLine("Firefox: Initialization");

            playwright = await Playwright.CreateAsync();

            Console.WriteLine("Firefox: CreateAsync");

            browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = init.Headless,
                ExecutablePath = executablePath,
                Args = init.Args
            });

            Console.WriteLine("Firefox: LaunchAsync");

            Status = init.Headless ? PlaywrightStatus.headless : PlaywrightStatus.NoHeadless;
            Console.WriteLine($"Firefox: v{browser.Version} / {Status.ToString()} / {browser.IsConnected}");

            browser.Disconnected += Browser_Disconnected;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_ebd0dcdd");
            Status = PlaywrightStatus.disabled;
            Console.WriteLine($"Firefox: {ex.Message}");
        }
    }

    async private static void Browser_Disconnected(object sender, IBrowser e)
    {
        Status = PlaywrightStatus.disabled;
        browser.Disconnected -= Browser_Disconnected;
        Console.WriteLine("Firefox: Browser_Disconnected");

        if (pages_keepopen != null)
            pages_keepopen.Clear();

        try
        {
            await browser.CloseAsync();
            await browser.DisposeAsync();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_do3kv8vg");
        }

        browser = null;

        try
        {
            playwright.Dispose();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_mk2ju3s1");
        }

        playwright = null;
        pages_keepopen = new();
        await Task.Delay(TimeSpan.FromSeconds(10));
        await CreateAsync();
    }
    #endregion

    #region CronStart
    public static void CronStart()
    {
        _closeLifetimeTimer = new Timer(CronCloseLifetimeContext, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    static Timer _closeLifetimeTimer;

    static int _cronCloseLifetimeWork = 0;
    #endregion

    #region CronCloseLifetimeContext
    async static void CronCloseLifetimeContext(object state)
    {
        if (!CoreInit.conf.firefox.enable || Status == PlaywrightStatus.disabled)
            return;

        if (Interlocked.Exchange(ref _cronCloseLifetimeWork, 1) == 1)
            return;

        try
        {
            var init = CoreInit.conf.firefox;
            if (0 >= init.context.keepalive)
                return;

            foreach (var k in pages_keepopen.ToArray())
            {
                if (Math.Max(1, init.context.min) >= pages_keepopen.Count)
                    break;

                if (DateTime.Now > k.lastActive.AddMinutes(init.context.keepalive))
                {
                    try
                    {
                        await k.page.CloseAsync().ConfigureAwait(false);
                        pages_keepopen.Remove(k);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_s487ei0m");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_9sqt3han");
        }
        finally
        {
            Volatile.Write(ref _cronCloseLifetimeWork, 0);
        }
    }
    #endregion


    public bool IsCompleted { get; set; }

    public string failedUrl { get; set; }

    IPage page { get; set; }

    KeepopenPage keepopen_page { get; set; }


    async public Task<IPage> NewPageAsync(string plugin, IReadOnlyDictionary<string, string> headers = null, (string ip, string username, string password) proxy = default, bool keepopen = true)
    {
        try
        {
            if (browser == null)
                return null;

            if (proxy != default)
            {
                #region proxy NewContext
                if (keepopen)
                {
                    foreach (var pg in pages_keepopen.ToArray().Where(i => i.proxy != default))
                    {
                        if (pg.plugin == plugin)
                        {
                            if (pg.proxy.ip != proxy.ip || pg.proxy.username != proxy.username || pg.proxy.password != proxy.password)
                            {
                                _ = pg.page.CloseAsync().ConfigureAwait(false);
                                pages_keepopen.Remove(pg);
                                continue;
                            }
                        }

                        if (pg.proxy.ip == proxy.ip && pg.proxy.username == proxy.username && pg.proxy.password == proxy.password)
                        {
                            stats_keepopen++;
                            pg.busy = true;
                            keepopen_page = pg;
                            page = pg.page;
                            page.RequestFailed += Page_RequestFailed;

                            if (headers != null && headers.Count > 0)
                                await page.SetExtraHTTPHeadersAsync(Http.NormalizeHeaders(headers)).ConfigureAwait(false);

                            return page;
                        }
                    }
                }

                var contextOptions = new BrowserNewContextOptions
                {
                    Proxy = new Proxy
                    {
                        Server = proxy.ip,
                        Bypass = "127.0.0.1",
                        Username = proxy.username,
                        Password = proxy.password
                    }
                };

                stats_newcontext++;
                var context = await browser.NewContextAsync(contextOptions).ConfigureAwait(false);
                page = await context.NewPageAsync().ConfigureAwait(false);
                #endregion
            }
            else
            {
                #region NewContext
                if (keepopen)
                {
                    foreach (var pg in pages_keepopen.Where(i => i.proxy == default))
                    {
                        if (pg.busy == false && DateTime.Now > pg.lockTo)
                        {
                            stats_keepopen++;
                            pg.busy = true;
                            keepopen_page = pg;
                            page = pg.page;
                            page.RequestFailed += Page_RequestFailed;

                            if (headers != null && headers.Count > 0)
                                await page.SetExtraHTTPHeadersAsync(Http.NormalizeHeaders(headers)).ConfigureAwait(false);

                            return page;
                        }
                    }
                }

                stats_newcontext++;
                page = await browser.NewPageAsync().ConfigureAwait(false);
                #endregion
            }

            if (headers != null && headers.Count > 0)
                await page.SetExtraHTTPHeadersAsync(Http.NormalizeHeaders(headers)).ConfigureAwait(false);

            page.Popup += Page_Popup;
            page.Download += Page_Download;

            if (!keepopen || !CoreInit.conf.firefox.context.keepopen || pages_keepopen.Count >= Math.Max(CoreInit.conf.firefox.context.min, CoreInit.conf.firefox.context.max))
            {
                page.RequestFailed += Page_RequestFailed;
                return page;
            }

            keepopen_page = new KeepopenPage() { page = page, busy = true, plugin = plugin, proxy = proxy };
            pages_keepopen.Add(keepopen_page);
            page.RequestFailed += Page_RequestFailed;
            return page;
        }
        catch { return null; }
    }


    void Page_RequestFailed(object sender, IRequest e)
    {
        try
        {
            if (failedUrl != null && e.Url == failedUrl)
            {
                completionSource.SetResult(null);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_9ama5awz");
        }
    }

    void Page_Download(object sender, IDownload e)
    {
        try
        {
            e.CancelAsync().ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_q4se3azw");
        }
    }

    void Page_Popup(object sender, IPage e)
    {
        try
        {
            e.CloseAsync().ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_3al1bth2");
        }
    }


    public void Dispose()
    {
        if (browser == null || CoreInit.conf.firefox.DEV)
            return;

        try
        {
            page.RequestFailed -= Page_RequestFailed;

            if (keepopen_page != null)
            {
                keepopen_page.page.GotoAsync("about:blank").ConfigureAwait(false);
                keepopen_page.lastActive = DateTime.Now;
                keepopen_page.lockTo = DateTime.Now.AddSeconds(1);
                keepopen_page.busy = false;
            }
            else
            {
                page.Popup -= Page_Popup;
                page.Download -= Page_Download;
                page.CloseAsync().ConfigureAwait(false);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_t1n5fjcg");
        }
    }

    public static void FullDispose()
    {
        shutdown = true;
        if (browser == null)
            return;

        try
        {
            browser.CloseAsync().ContinueWith(t => browser.DisposeAsync());
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_3bvrr014");
        }
    }
}
