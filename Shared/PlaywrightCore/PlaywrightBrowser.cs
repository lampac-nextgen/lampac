using Microsoft.Playwright;
using Shared.Models.Events;

namespace Shared.PlaywrightCore;

public class PlaywrightBrowser : IDisposable
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<PlaywrightBrowser>();

    public static PlaywrightStatus Status
    {
        get
        {
            if (Chromium.Status == PlaywrightStatus.NoHeadless || Firefox.Status != PlaywrightStatus.disabled)
                return PlaywrightStatus.NoHeadless;

            if (Chromium.Status == PlaywrightStatus.headless)
                return PlaywrightStatus.headless;

            return PlaywrightStatus.disabled;
        }
    }

    public bool IsCompleted
    {
        get
        {
            if (chromium != null)
                return chromium.IsCompleted;

            return firefox.IsCompleted;
        }
    }

    public TaskCompletionSource<string> completionSource
    {
        get
        {
            if (chromium != null)
                return chromium.completionSource;

            return firefox.completionSource;
        }
    }


    public Chromium chromium = null;

    public Firefox firefox = null;


    public PlaywrightBrowser(string priorityBrowser = null)
    {
        if (priorityBrowser == "firefox" && Firefox.Status != PlaywrightStatus.disabled)
        {
            firefox = new Firefox();
            return;
        }

        chromium = new Chromium();
    }

    public void SetFailedUrl(string url)
    {
        if (chromium != null)
        {
            chromium.failedUrl = url;
        }
        else
        {
            firefox.failedUrl = url;
        }
    }

    async public Task<IPage> NewPageAsync(string plugin, IReadOnlyDictionary<string, string> headers = null, (string ip, string username, string password) proxy = default, bool keepopen = true, bool imitationHuman = false, bool deferredDispose = false)
    {
        try
        {
            if (chromium == null && firefox == null)
                return default;

            IPage page = default;

            if (chromium != null)
                page = await chromium.NewPageAsync(plugin, headers, proxy, keepopen: keepopen, imitationHuman: imitationHuman, deferredDispose: deferredDispose).ConfigureAwait(false);
            else
                page = await firefox.NewPageAsync(plugin, headers, proxy, keepopen: keepopen).ConfigureAwait(false);

            return page;
        }
        catch { return default; }
    }


    public void SetPageResult(string val)
    {
        try
        {
            if (chromium != null)
            {
                chromium.IsCompleted = true;
                chromium.completionSource.SetResult(val);
            }
            else
            {
                firefox.IsCompleted = true;
                firefox.completionSource.SetResult(val);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_tjv9tao1");
        }
    }

    public Task<string> WaitPageResult(int seconds = 10)
    {
        try
        {
            if (chromium != null)
                return chromium.WaitPageResult(seconds);

            return firefox.WaitPageResult(seconds);
        }
        catch { return default; }
    }


    public Task WaitForAnySelectorAsync(IPage page, params string[] selectors)
    {
        var tasks = selectors.Select(selector =>
            page.WaitForSelectorAsync(selector)
        ).ToArray();

        return Task.WhenAny(tasks);
    }


    async public Task ClearContinueAsync(IRoute route, IPage page)
    {
        var cookies = await page.Context.CookiesAsync();
        if (cookies == null || cookies.Count == 0)
        {
            // нету куки, продолжаем
            await route.ContinueAsync();
            return;
        }

        var filteredCookies = cookies.Where(c => c.Name != "cf_clearance").Select(c => new Cookie
        {
            Name = c.Name,
            Value = c.Value,
            Domain = c.Domain,
            Path = c.Path,
            Expires = c.Expires,
            HttpOnly = c.HttpOnly,
            Secure = c.Secure,
            SameSite = c.SameSite
        }).ToList();

        if (filteredCookies.Count == cookies.Count)
        {
            // Если куки не содержат cf_clearance, продолжаем
            await route.ContinueAsync();
            return;
        }

        if (filteredCookies.Count == 0)
        {
            // после удаления cf_clearance не осталось других куки
            await page.Context.ClearCookiesAsync();
            await route.ContinueAsync();
            return;
        }

        await page.Context.ClearCookiesAsync();
        await page.Context.AddCookiesAsync(filteredCookies);

        await route.ContinueAsync();
    }


    public void Dispose()
    {
        chromium?.Dispose();
        firefox?.Dispose();
    }




    async public static Task<string> Get(BaseSettings init, string url, IReadOnlyList<HeadersModel> headers = null, (string ip, string username, string password) proxy = default, List<Cookie> cookies = null, bool viewsource = true)
    {
        IResponse response = default;
        string result = null;

        try
        {
            using (var browser = new PlaywrightBrowser(init?.priorityBrowser))
            {
                var page = await browser.NewPageAsync(init?.plugin, headers?.ToDictionary(), proxy).ConfigureAwait(false);
                if (page == null)
                    return null;

                if (cookies != null)
                    await page.Context.AddCookiesAsync(cookies).ConfigureAwait(false);

                if (browser.firefox != null)
                {
                    response = await page.GotoAsync(url, new PageGotoOptions() { WaitUntil = WaitUntilState.DOMContentLoaded }).ConfigureAwait(false);
                }
                else
                {
                    response = await page.GotoAsync(viewsource ? $"view-source:{url}" : url, new PageGotoOptions()
                    {
                        Timeout = 10_000,
                        WaitUntil = WaitUntilState.DOMContentLoaded
                    }).ConfigureAwait(false);
                }

                if (response != null)
                    result = await response.TextAsync().ConfigureAwait(false);
            }

            if (EventListener.PlaywrightHttpResponse != null)
            {
                await SendPlaywrightHttpResponseEvent(
                    new EventPlaywrightHttpResponse(
                        url: url,
                        method: response?.Request?.Method,
                        status: response?.Status ?? 0,
                        requestHeaders: response?.Request?.Headers,
                        responseHeaders: response?.Headers,
                        result: result,
                        error: null
                    )
                ).ConfigureAwait(false);
            }

            return result;
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_i56q6uea");

            if (EventListener.PlaywrightHttpResponse != null)
            {
                await SendPlaywrightHttpResponseEvent(
                    new EventPlaywrightHttpResponse(
                        url: url,
                        method: response?.Request?.Method,
                        status: response?.Status ?? 0,
                        requestHeaders: response?.Request?.Headers,
                        responseHeaders: response?.Headers,
                        result: result,
                        error: ex.ToString()
                    )
                ).ConfigureAwait(false);
            }
        }

        return null;
    }

    async static Task SendPlaywrightHttpResponseEvent(EventPlaywrightHttpResponse eventData)
    {
        foreach (Func<EventPlaywrightHttpResponse, Task> handler in EventListener.PlaywrightHttpResponse.GetInvocationList())
            await handler.Invoke(eventData).ConfigureAwait(false);
    }
}
