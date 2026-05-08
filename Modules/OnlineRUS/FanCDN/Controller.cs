using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BrowserCookie = Microsoft.Playwright.Cookie;

namespace FanCDN;

public class FanCDNController : BaseOnlineController
{
    static List<BrowserCookie> cookies;

    public FanCDNController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (cookies == null && !string.IsNullOrEmpty(init.cookie))
            {
                string fanhost = "." + Regex.Replace(init.host, "^https?://", "");
                var excookie = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();

                cookies = new List<BrowserCookie>();

                foreach (string line in init.cookie.Split(";"))
                {
                    if (string.IsNullOrEmpty(line) || !line.Contains("=") || line.Contains("cf_clearance") || line.Contains("PHPSESSID"))
                        continue;

                    cookies.Add(new BrowserCookie()
                    {
                        Domain = fanhost,
                        Expires = excookie,
                        Path = "/",
                        HttpOnly = true,
                        Secure = true,
                        Name = line.Split("=")[0].Trim(),
                        Value = line.Split("=")[1].Trim()
                    });
                }
            }
        };
    }

    [HttpGet]
    [Route("lite/fancdn")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int year, int serial)
    {
        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (kinopoisk_id == 0 || serial == 1 || cookies == null)
            return OnError();

        var oninvk = new FanCDNInvoke
        (
           init,
           cookies,
           streamfile => HostStreamProxy(streamfile)
        );

        var search = await InvokeCacheResult<(string kp, string key)>($"fancdn:{title}:{original_title}:{year}", TimeSpan.FromHours(4), onget: async e =>
        {
            var result = await oninvk.Search(title, original_title, year);
            if (result.key == null)
                return e.Fail("search");

            return e.Success(result);
        });

        if (!search.IsSuccess)
            return OnError(search.ErrorMsg);

        var cache = await InvokeCacheResult<EmbedModel>($"fancdn:{search.Value}", 20, textJson: true, onget: async e =>
        {
            var result = await oninvk.Embed(search.Value.kp, search.Value.key);
            if (result == null)
                return e.Fail("embed");

            return e.Success(result);
        });

        return ContentTpl(cache,
            () => oninvk.Tpl(cache.Value, imdb_id, kinopoisk_id, title, original_title, vast: init.vast, headers: httpHeaders(init))
        );
    }
}
