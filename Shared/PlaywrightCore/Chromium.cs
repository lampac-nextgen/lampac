using Microsoft.Playwright;
using Shared.Services;
using Shared.Models.Browser;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Shared.PlaywrightCore
{
    public class Chromium : PlaywrightBase, IDisposable
    {
        #region static
        static readonly Serilog.ILogger Log = Serilog.Log.ForContext<Chromium>();

        public static BrowserNewContextOptions baseContextOptions = new BrowserNewContextOptions
        {
            UserAgent = Http.UserAgent,
            ExtraHTTPHeaders = Http.NormalizeHeaders(new Dictionary<string, string>(Http.defaultUaHeaders)
            {
                ["accept-language"] = "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5"
            })
        };

        static ConcurrentDictionary<KeepopenPage, byte> pages_keepopen = new();

        static IBrowserContext keepopen_context { get; set; }

        static DateTime create_keepopen_context { get; set; }

        public static long stats_keepopen { get; set; }

        public static long stats_newcontext { get; set; }

        public static (DateTime time, int status, string ex) stats_ping { get; set; }


        public static IPlaywright playwright { get; private set; } = null;

        static IBrowser browser = null;

        static bool shutdown = false;

        public static PlaywrightStatus Status { get; private set; } = PlaywrightStatus.disabled;

        public static int ContextsCount => browser?.Contexts?.Count ?? 0;

        async public static Task CreateAsync()
        {
            try
            {
                var init = CoreInit.conf.chromium;
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
                            case Architecture.Arm64:
                                {
                                    if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                                        executablePath = ".playwright\\chrome-win32\\chrome.exe";
                                    else
                                        executablePath = ".playwright\\chrome-win\\chrome.exe";
                                    break;
                                }
                            default:
                                Console.WriteLine("Chromium: Architecture unknown");
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
                                    executablePath = ".playwright/chrome-mac/Chromium.app/Contents/MacOS/Chromium";
                                    break;
                                }
                            default:
                                Console.WriteLine("Chromium: Architecture unknown");
                                return;
                        }
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        switch (RuntimeInformation.ProcessArchitecture)
                        {
                            case Architecture.X86:
                            case Architecture.X64:
                                {
                                    executablePath = ".playwright/chrome-linux/chrome";
                                    break;
                                }
                            default:
                                Console.WriteLine("PlaywChromiumright: Architecture unknown");
                                return;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Chromium: IsOSPlatform unknown");
                        return;
                    }
                }

                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    Console.WriteLine("Chromium: chromium is not installed, please specify full path in executablePath");
                    return;
                }

                Console.WriteLine("Chromium: Initialization");

                playwright = await Playwright.CreateAsync();

                Console.WriteLine("Chromium: CreateAsync");

                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = init.Headless,
                    ExecutablePath = executablePath,
                    Args = init.Args,
                    Devtools = init.Devtools
                });

                Console.WriteLine("Chromium: LaunchAsync");

                Status = init.Headless ? PlaywrightStatus.headless : PlaywrightStatus.NoHeadless;
                Console.WriteLine($"Chromium: v{browser.Version} / {Status.ToString()} / {browser.IsConnected}");

                if (CoreInit.conf.chromium.context.keepopen)
                {
                    create_keepopen_context = DateTime.Now;
                    var kpc = await browser.NewContextAsync(baseContextOptions);
                    await kpc.NewPageAsync();
                    keepopen_context = kpc;
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "CatchId={CatchId}", "id_63f456ad");
                Status = PlaywrightStatus.disabled;
                Console.WriteLine($"Chromium: {ex.Message}");
            }
        }
        #endregion

        #region CronStart
        public static void CronStart()
        {
            _closeLifetimeTimer = new Timer(CronCloseLifetimeContext, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            _browserDisconnectedTimer = new Timer(CronBrowserDisconnected, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1));
        }

        static Timer _closeLifetimeTimer, _browserDisconnectedTimer;

        static int _cronCloseLifetimeWork = 0, _cronBrowserDisconnectedWork = 0;
        #endregion

        #region CronCloseLifetimeContext
        async static void CronCloseLifetimeContext(object state)
        {
            if (!CoreInit.conf.chromium.enable || Status == PlaywrightStatus.disabled)
                return;

            if (Interlocked.Exchange(ref _cronCloseLifetimeWork, 1) == 1)
                return;

            try
            {
                var init = CoreInit.conf.chromium;
                if (!init.context.keepopen || 0 >= init.context.keepalive)
                    return;

                if (DateTime.Now.AddMinutes(-init.context.keepalive) > create_keepopen_context)
                {
                    create_keepopen_context = DateTime.Now;
                    var kpc = await browser.NewContextAsync(baseContextOptions);
                    await kpc.NewPageAsync();

                    try
                    {
                        _ = keepopen_context.CloseAsync().ConfigureAwait(false);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_0fjttjws");
                    }

                    keepopen_context = kpc;
                }

                if (pages_keepopen.Count > 0 && pages_keepopen.Count > init.context.min)
                {
                    foreach (var k in pages_keepopen.Keys)
                    {
                        if (init.context.min >= pages_keepopen.Count)
                            break;

                        if (DateTime.Now.AddMinutes(-init.context.keepalive) > k.create)
                        {
                            try
                            {
                                if (pages_keepopen.TryRemove(k, out _))
                                {
                                    _ = Task.Delay(TimeSpan.FromSeconds(20))
                                        .ContinueWith(t => k.context.CloseAsync())
                                        .ConfigureAwait(false);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Log.Error(ex, "CatchId={CatchId}", "id_r70jpgk4");
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_e93m79g5");
            }
            finally
            {
                Volatile.Write(ref _cronCloseLifetimeWork, 0);
            }
        }
        #endregion

        #region CronBrowserDisconnected
        async static void CronBrowserDisconnected(object state)
        {
            if (!CoreInit.conf.chromium.enable)
                return;

            if (Interlocked.Exchange(ref _cronBrowserDisconnectedWork, 1) == 1)
                return;

            try
            {
                stats_ping = (DateTime.Now, 1, null);
                if (shutdown)
                    return;

                stats_ping = (DateTime.Now, 2, null);

                if (CoreInit.conf.chromium.Headless && Status != PlaywrightStatus.disabled)
                {
                    try
                    {
                        stats_ping = (DateTime.Now, 4, null);
                        if (browser == null && keepopen_context == null)
                            return;

                        bool isOk = false;

                        try
                        {
                            stats_ping = (DateTime.Now, 5, null);
                            IPage p = keepopen_context != null ? await keepopen_context.NewPageAsync() : await browser.NewPageAsync();
                            if (p != null)
                            {
                                try
                                {
                                    var options = new PageGotoOptions
                                    {
                                        Timeout = 5000, // 5 секунд
                                        WaitUntil = WaitUntilState.DOMContentLoaded
                                    };

                                    var r = await p.GotoAsync($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/api/chromium/ping", options);
                                    if (r != null)
                                    {
                                        stats_ping = (DateTime.Now, r.Status, null);
                                        if (r.Status == 200)
                                            isOk = true;
                                    }
                                }
                                finally
                                {
                                    await p.CloseAsync();
                                }
                            }
                        }
                        catch
                        {
                            stats_ping = (DateTime.Now, 500, null);
                        }

                        if (!isOk)
                        {
                            Console.WriteLine("\nChromium: Browser_Disconnected");

                            Status = PlaywrightStatus.disabled;

                            keepopen_context = null;

                            if (pages_keepopen != null)
                                pages_keepopen.Clear();

                            try
                            {
                                if (browser != null)
                                {
                                    await browser.CloseAsync();
                                    await browser.DisposeAsync();
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Log.Error(ex, "CatchId={CatchId}", "id_phji1t90");
                            }

                            try
                            {
                                playwright.Dispose();
                            }
                            catch (System.Exception ex)
                            {
                                Log.Error(ex, "CatchId={CatchId}", "id_7ea64mz2");
                            }

                            browser = null;
                            playwright = null;

                            await CreateAsync();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Serilog.Log.Error(ex, "CatchId={CatchId}", "id_009010ae");
                        stats_ping = (DateTime.Now, -1, ex.Message);
                        Console.WriteLine(ex.Message);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_gf96yzkh");
            }
            finally
            {
                Volatile.Write(ref _cronBrowserDisconnectedWork, 0);
            }
        }
        #endregion


        public bool IsCompleted { get; set; }

        bool imitationHuman { get; set; }

        bool deferredDispose { get; set; }

        public string failedUrl { get; set; }

        IPage page { get; set; }

        IBrowserContext context { get; set; }

        KeepopenPage keepopen_page { get; set; }


        async public Task<IPage> NewPageAsync(string plugin, Dictionary<string, string> headers = null, (string ip, string username, string password) proxy = default, bool keepopen = true, bool imitationHuman = false, bool deferredDispose = false)
        {
            try
            {
                if (browser == null)
                    return null;

                this.imitationHuman = imitationHuman;
                this.deferredDispose = deferredDispose;

                if (proxy != default)
                {
                    #region NewPageAsync
                    if (keepopen)
                    {
                        foreach (var pg in pages_keepopen.Keys)
                        {
                            if (pg.plugin == plugin)
                            {
                                if (pg.proxy.ip != proxy.ip || pg.proxy.username != proxy.username || pg.proxy.password != proxy.password)
                                {
                                    _ = pg.context.CloseAsync().ConfigureAwait(false);
                                    pages_keepopen.TryRemove(pg, out _);
                                    continue;
                                }
                            }

                            if (pg.proxy.ip == proxy.ip && pg.proxy.username == proxy.username && pg.proxy.password == proxy.password)
                            {
                                stats_keepopen++;
                                keepopen_page = pg;
                                await ClearCookie(pg.context).ConfigureAwait(false);
                                page = await pg.context.NewPageAsync().ConfigureAwait(false);
                                break;
                            }
                        }
                    }

                    if (page == default)
                    {
                        var contextOptions = new BrowserNewContextOptions
                        {
                            Proxy = new Proxy
                            {
                                Server = proxy.ip,
                                Bypass = "127.0.0.1",
                                Username = proxy.username,
                                Password = proxy.password
                            },
                            UserAgent = baseContextOptions.UserAgent,
                            ExtraHTTPHeaders = baseContextOptions.ExtraHTTPHeaders
                        };

                        stats_newcontext++;
                        context = await browser.NewContextAsync(contextOptions).ConfigureAwait(false);
                        page = await context.NewPageAsync().ConfigureAwait(false);
                    }
                    #endregion

                    if (headers != null && headers.Count > 0)
                        await page.SetExtraHTTPHeadersAsync(Http.NormalizeHeaders(headers)).ConfigureAwait(false);

                    page.Popup += Page_Popup;
                    page.Download += Page_Download;
                    page.RequestFailed += Page_RequestFailed;

                    if (CoreInit.conf.chromium.Devtools)
                        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false); // что бы devtools успел открыться

                    if (!keepopen || keepopen_page != null || !CoreInit.conf.chromium.context.keepopen || pages_keepopen.Count >= CoreInit.conf.chromium.context.max)
                        return page;

                    await context.NewPageAsync().ConfigureAwait(false); // что-бы context не закрывался с последней закрытой вкладкой
                    if (pages_keepopen.Count >= CoreInit.conf.chromium.context.max)
                        return page;

                    // один из контекстов уже использует этот прокси
                    if (proxy != default && pages_keepopen.Keys.FirstOrDefault(i => i.proxy.ip == proxy.ip && i.proxy.username == proxy.username && i.proxy.password == proxy.password)?.proxy != default)
                        return page;

                    keepopen_page = new KeepopenPage() { context = context, plugin = plugin, proxy = proxy };
                    pages_keepopen.TryAdd(keepopen_page, 0);
                    return page;
                }
                else
                {
                    #region NewPageAsync
                    if (keepopen && keepopen_context != default)
                    {
                        stats_keepopen++;
                        await ClearCookie(keepopen_context).ConfigureAwait(false);
                        page = await keepopen_context.NewPageAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        stats_newcontext++;
                        var kpc = await browser.NewContextAsync(baseContextOptions);
                        page = await kpc.NewPageAsync();
                    }
                    #endregion

                    if (headers != null && headers.Count > 0)
                        await page.SetExtraHTTPHeadersAsync(Http.NormalizeHeaders(headers)).ConfigureAwait(false);

                    page.Popup += Page_Popup;
                    page.Download += Page_Download;
                    page.RequestFailed += Page_RequestFailed;

                    if (CoreInit.conf.chromium.Devtools)
                        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false); // что бы devtools успел открыться

                    return page;
                }
            }
            catch { return null; }
        }


        static bool workClearCookie = false;

        async Task ClearCookie(IBrowserContext context)
        {
            if (workClearCookie)
                return;

            try
            {
                workClearCookie = true;
                var cookies = await context.CookiesAsync();

                foreach (var cookie in cookies.Where(c => c.Name == "cf_clearance"))
                {
                    await context.ClearCookiesAsync(new BrowserContextClearCookiesOptions
                    {
                        Name = cookie.Name,
                        Domain = cookie.Domain,
                        Path = cookie.Path
                    });
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_lwqh27k2");
            }

            workClearCookie = false;
        }


        void Page_RequestFailed(object sender, IRequest e)
        {
            try
            {
                if (failedUrl != null && e.Url == failedUrl)
                    completionSource.SetResult(null);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_jqozdabw");
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
                Log.Error(ex, "CatchId={CatchId}", "id_1t0vc1ab");
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
                Log.Error(ex, "CatchId={CatchId}", "id_zd3oph6k");
            }
        }


        public void Dispose()
        {
            if (browser == null || CoreInit.conf.chromium.DEV)
                return;

            try
            {
                page.RequestFailed -= Page_RequestFailed;
                page.Popup -= Page_Popup;
                page.Download -= Page_Download;

                void close()
                {
                    if (keepopen_page != null)
                    {
                        page.CloseAsync().ConfigureAwait(false);
                    }
                    else if (context != null)
                    {
                        context.CloseAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        page.CloseAsync().ConfigureAwait(false);
                    }
                }

                if (imitationHuman || deferredDispose)
                {
                    Task.Delay(deferredDispose ? 2_000 : 10_000)
                        .ContinueWith(t => close());
                }
                else
                {
                    close();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_k0df4qmv");
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
                Log.Error(ex, "CatchId={CatchId}", "id_13g0vhhh");
            }
        }
    }
}
