using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers
{
    [AllowAnonymous]
    [Route("selezen/[action]")]
    public class SelezenController : JacBaseController
    {
        #region search
        public static Task<bool> search(string host, ConcurrentBag<TorrentDetails> torrents, string query)
        {
            if (!jackett.Selezen.enable || string.IsNullOrEmpty(jackett.Selezen.cookie ?? jackett.Selezen.login.u) || jackett.Selezen.showdown)
                return Task.FromResult(false);

            return Joinparse(torrents, () => parsePage(host, query));
        }
        #endregion


        #region parseMagnet
        async public Task<ActionResult> parseMagnet(string url)
        {
            if (!jackett.Selezen.enable || ModInit.conf.disableJackett)
                return Content("disable");

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri abs) ||
                !Uri.TryCreate(jackett.Selezen.host, UriKind.Absolute, out Uri tracker) ||
                (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps) ||
                !abs.IdnHost.Equals(tracker.IdnHost, StringComparison.OrdinalIgnoreCase))
                return Content("error");

            string cookie = await getCookie();
            if (string.IsNullOrEmpty(cookie))
                return Content("cookie == null");

            var proxyManager = new ProxyManager("selezen", jackett.Selezen);

            string html = await Http.Get(abs.AbsoluteUri, cookie: cookie, proxy: proxyManager.Get());
            string magnet = new Regex("href=\"(magnet:[^\"]+)\"").Match(html ?? string.Empty).Groups[1].Value;

            if (html == null)
                return Content("error");

            #region Download
            if (jackett.Selezen.priority == "torrent")
            {
                string id = new Regex("href=\"/index.php\\?do=download&id=([0-9]+)").Match(html).Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    var _t = await Http.Download($"{jackett.Selezen.host}/index.php?do=download&id={id}", cookie: cookie, referer: jackett.Selezen.host, timeoutSeconds: 10);
                    if (_t != null && BencodeTo.Magnet(_t) != null)
                        return File(_t, "application/x-bittorrent");
                }
            }
            #endregion

            if (string.IsNullOrWhiteSpace(magnet))
                return Content("error");

            return Redirect(magnet);
        }
        #endregion

        #region parsePage
        async static ValueTask<List<TorrentDetails>> parsePage(string host, string query)
        {
            #region Авторизация
            string cookie = await getCookie();
            if (string.IsNullOrEmpty(cookie))
            {
                consoleErrorLog("selezen");
                return null;
            }
            #endregion

            #region html
            var proxyManager = new ProxyManager("selezen", jackett.Selezen);

            string html = await Http.Post($"{jackett.Selezen.host}/index.php?do=search", $"do=search&subaction=search&search_start=0&full_search=0&result_from=1&story={HttpUtility.UrlEncode(query)}&titleonly=0&searchuser=&replyless=0&replylimit=0&searchdate=0&beforeafter=after&sortby=date&resorder=desc&showposts=0&catlist%5B%5D=9", proxy: proxyManager.Get(), cookie: cookie, timeoutSeconds: jackett.timeoutSeconds);

            if (html != null && html.Contains("dle_root"))
            {
                if (!html.Contains($">{jackett.Selezen.login.u}<"))
                {
                    consoleErrorLog("selezen");
                    return null;
                }
            }
            #endregion

            var torrents = new List<TorrentDetails>();

            foreach (string row in html.Split("class=\"card radius-10 overflow-hidden\"").Skip(1))
            {
                if (string.IsNullOrWhiteSpace(row) || row.Contains(">Аниме</a>") || row.Contains(" [S0"))
                    continue;

                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                #region Данные раздачи
                var g = Regex.Match(row, "<a href=\"(https?://[^<]+)\"><h4 class=\"card-title\">([^<]+)</h4>").Groups;
                string url = g[1].Value;
                string title = g[2].Value;

                string _sid = Match("<i class=\"bx bx-chevrons-up\"></i>([0-9 ]+)").Trim();
                string _pir = Match("<i class=\"bx bx-chevrons-down\"></i>([0-9 ]+)").Trim();
                string sizeName = Match("<span class=\"bx bx-download\"></span>([^<]+)</a>").Trim();
                DateTime createTime = tParse.ParseCreateTime(Match("class=\"bx bx-calendar\"></span> ?([0-9]{2}\\.[0-9]{2}\\.[0-9]{4} [0-9]{2}:[0-9]{2})</a>"), "dd.MM.yyyy HH:mm");

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                    continue;
                #endregion

                #region types
                string[] types = new string[] { "movie" };
                if (row.Contains(">Мульт") || row.Contains(">мульт"))
                    types = new string[] { "multfilm" };
                #endregion

                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentDetails()
                {
                    types = types,
                    url = url,
                    title = title,
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    createTime = createTime,
                    parselink = $"{host}/selezen/parsemagnet?url={HttpUtility.UrlEncode(url)}"
                });
            }

            return torrents;
        }
        #endregion


        #region getCookie
        async static ValueTask<string> getCookie()
        {
            if (!string.IsNullOrEmpty(jackett.Selezen.cookie))
                return jackett.Selezen.cookie;

            string authKey = "selezen:TakeLogin()";
            if (Startup.memoryCache.TryGetValue(authKey, out string _cookie))
                return _cookie;

            if (Startup.memoryCache.TryGetValue($"{authKey}:error", out _))
                return null;

            Startup.memoryCache.Set($"{authKey}:error", 0, TimeSpan.FromSeconds(20));

            try
            {
                using (var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = Http.AlwaysAllowCertificate
                })
                {
                    using (var client = new System.Net.Http.HttpClient(clientHandler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(jackett.timeoutSeconds);
                        client.MaxResponseContentBufferSize = 2000000; // 2MB

                        foreach (var h in Http.defaultFullHeaders)
                            client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

                        var postParams = new Dictionary<string, string>
                        {
                            { "login_name", jackett.Selezen.login.u },
                            { "login_password", jackett.Selezen.login.p },
                            { "login_not_save", "1" },
                            { "login", "submit" }
                        };

                        using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                        {
                            using (var response = await client.PostAsync(jackett.Selezen.host, postContent))
                            {
                                if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                                {
                                    string PHPSESSID = null;
                                    foreach (string line in cook)
                                    {
                                        if (string.IsNullOrWhiteSpace(line))
                                            continue;

                                        if (line.Contains("PHPSESSID="))
                                            PHPSESSID = new Regex("PHPSESSID=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                    }

                                    if (!string.IsNullOrWhiteSpace(PHPSESSID))
                                    {
                                        string cookie = $"PHPSESSID={PHPSESSID}; _ym_isad=2;";
                                        Startup.memoryCache.Set(authKey, cookie, DateTime.Today.AddDays(1));
                                        return cookie;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "SelezenController", "id_cugo46yt");
            }

            return null;
        }
        #endregion
    }
}
