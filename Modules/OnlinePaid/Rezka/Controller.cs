using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Rezka
{
    public class RezkaController : BaseOnlineController<RezkaSettings>
    {
        RezkaInvoke oninvk;

        public RezkaController() : base(ModInit.conf)
        {
            loadKitInitialization = (j, i, c) =>
            {
                if (j.ContainsKey("premium"))
                    i.premium = c.premium;

                if (j.ContainsKey("uacdn"))
                    i.uacdn = c.uacdn;

                if (j.ContainsKey("forceua"))
                    i.forceua = c.forceua;

                if (j.ContainsKey("reserve"))
                    i.reserve = c.reserve;

                if (j.ContainsKey("ajax"))
                    i.ajax = c.ajax;

                return i;
            };

            requestInitializationAsync = async () =>
            {
                string country = init.forceua ? "UA" : requestInfo.Country;

                var headers = httpHeaders(init);
                var cookie = init.rhub ? null : await getCookie();
                string cookieRhub = init.rhub ? getRhubCookie() : null;

                if (rch?.enable == true && cookieRhub != null)
                    headers.Add(new HeadersModel("Cookie", cookieRhub));

                oninvk = new RezkaInvoke
                (
                    host,
                    "lite/rezka",
                    init,
                    cookie,
                    cookie != null || cookieRhub != null,
                    headers,
                    httpHydra,
                    streamfile => HostStreamProxy(RezkaInvoke.fixcdn(country, init.uacdn, streamfile), headers: RezkaInvoke.StreamProxyHeaders(init))
                );
            };
        }


        [HttpGet]
        [Route("lite/rezka")]
        async public Task<ActionResult> Index(string title, string original_title, int clarification, int year, int s = -1, string href = null, bool rjson = false, int serial = -1, bool similar = false, string source = null, string id = null)
        {
            if (await IsRequestBlocked(rch: true, rch_check: false))
                return badInitMsg;

            #region Initialization
            if (string.IsNullOrWhiteSpace(href) && string.IsNullOrWhiteSpace(title))
                return OnError();

            if (rch != null)
            {
                if (rch.IsNotConnected() || rch.IsRequiredConnected())
                    return ContentTo(rch.connectionMsg);

                if (rch.enable)
                {
                    if (rch.IsNotSupportRchAccess("web", out string rch_error))
                        return ShowError("На данном устройстве недоступно");

                    if (requestInfo.Country == "RU")
                    {
                        if (rch.InfoConnected()?.rchtype != "apk")
                            return ShowError("На даном устровстве недоступно");

                        if (string.IsNullOrWhiteSpace(init.cookie))
                            return ShowError("Укажите логин/пароль или cookie");
                    }
                }
            }
            #endregion

            if (string.IsNullOrEmpty(href) && !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(id))
            {
                if (source.Equals("rezka", StringComparison.OrdinalIgnoreCase) ||
                    source.Equals("hdrezka", StringComparison.OrdinalIgnoreCase))
                    href = id;
            }

            #region search
            string search_uri = null;

            if (string.IsNullOrEmpty(href))
            {
                var search = await InvokeCacheResult<SearchModel>($"rezka:search:{title}:{original_title}:{clarification}:{year}", 240, textJson: true, onget: async e =>
                {
                    var content = await oninvk.Search(title, original_title, clarification, year);

                    if (content.IsError)
                        return e.Fail(string.Empty, refresh_proxy: true);

                    if (content.IsEmpty)
                    {
                        if (rch.enable || content.content != null)
                            return e.Fail(content.content ?? "content");
                    }

                    return e.Success(content);
                });

                if (search.ErrorMsg != null)
                    return ShowError(string.IsNullOrEmpty(search.ErrorMsg) ? "поиск не дал результатов" : search.ErrorMsg);

                if (similar || string.IsNullOrEmpty(search.Value?.href))
                {
                    if (search.Value?.IsEmpty == true)
                        return ShowError(search.Value.content ?? "поиск не дал результатов");

                    return ContentTpl(search, () =>
                    {
                        if (search.Value.similar == null)
                            return default;

                        var stpl = new SimilarTpl(search.Value.similar.Count);
                        string enc_title = HttpUtility.UrlEncode(title);
                        string enc_original_title = HttpUtility.UrlEncode(original_title);

                        foreach (var similar in search.Value.similar)
                        {
                            string link = $"{host}/lite/rezka?rjson={rjson}&title={enc_title}&original_title={enc_original_title}&href={HttpUtility.UrlEncode(similar.href)}";

                            stpl.Append(similar.title, similar.year, string.Empty, link, PosterApi.Size(similar.img));
                        }

                        return stpl;
                    });
                }

                href = search.Value.href;
                search_uri = search.Value.search_uri;
            }
            #endregion

            var cache = await InvokeCacheResult($"rezka:{href}:{init.login}:{init.cookie}", 10,
                () => oninvk.Embed(href, search_uri),
                textJson: true
            );

            if (cache.Value?.IsEmpty == true)
                return ShowError(cache.Value.content);

            return ContentTpl(cache,
                () => oninvk.Tpl(cache.Value, accsArgs(string.Empty), title, original_title, s, href, true, rjson)
            );
        }


        #region Serial
        [HttpGet]
        [Route("lite/rezka/serial")]
        async public Task<ActionResult> Serial(string title, string original_title, string href, long id, int t, int s = -1, bool rjson = false)
        {
            if (string.IsNullOrEmpty(href))
                return OnError();

            if (await IsRequestBlocked(rch: true))
                return badInitMsg;

            Episodes root = await InvokeCache($"rezka:view:serial:{id}:{t}", 20,
                () => oninvk.SerialEmbed(id, t),
                textJson: true
            );

            if (root == null)
                return OnError();

            var cache = await InvokeCache($"rezka:{href}:{init.login}:{init.cookie}", 20,
                () => oninvk.Embed(href, null),
                textJson: true
            );

            if (cache == null)
                return OnError();

            if (cache.IsEmpty)
                return ShowError(cache.content);

            return ContentTpl(oninvk.Serial(root, cache, accsArgs(string.Empty), title, original_title, href, id, t, s, true, rjson));
        }
        #endregion

        #region Movie
        [HttpGet]
        [Route("lite/rezka/movie")]
        [Route("lite/rezka/movie.m3u8")]
        async public Task<ActionResult> Movie(string title, string original_title, string voice, long id, int t, int director = 0, int s = -1, int e = -1, string favs = null, bool play = false)
        {
            if (await IsRequestBlocked(rch: true, rch_check: false))
                return badInitMsg;

            if (rch != null)
            {
                if (rch.IsNotConnected())
                {
                    if (init.rhub_fallback && play)
                        rch.Disabled();
                    else
                        return ContentTo(rch.connectionMsg);
                }

                if (!play && rch.IsRequiredConnected())
                    return ContentTo(rch.connectionMsg);
            }

            MovieModel md = null;

            /// ajax = true (get_cdn_series)
            /// ajax = false (movie | get_cdn_series)
            /// ajax = null (movie)

            if (init.ajax != null && init.ajax.Value == false && !string.IsNullOrEmpty(voice))
            {
                md = await InvokeCache(ipkey($"rezka:movie:{voice}:{init.login}:{init.cookie}"), 20,
                    () => oninvk.Movie(voice),
                    textJson: true
                );
            }

            if (md == null && init.ajax != null)
            {
                md = await InvokeCache(ipkey($"rezka:view:get_cdn_series:{id}:{t}:{director}:{s}:{e}:{init.login}:{init.cookie}"), 20,
                    () => oninvk.Movie(id, t, director, s, e, favs),
                    textJson: true
                );
            }

            if (md?.links == null || md.links.Count == 0)
                return OnError();

            string result = oninvk.Movie(md, title, original_title, play, vast: init.vast);
            if (result == null)
                return OnError();

            if (play)
                return RedirectToPlay(result);

            return ContentTo(result);
        }
        #endregion


        #region getCookie
        static ConcurrentDictionary<string, CookieContainer> cookieContainer = new();

        async ValueTask<CookieContainer> getCookie()
        {
            string keyCookie = $"{init.cookie}:{init.login}";

            if (cookieContainer.TryGetValue(keyCookie, out CookieContainer _container))
                return _container;

            string domain = Regex.Match(init.host, "https?://([^/]+)").Groups[1].Value;

            #region setCookieContainer
            void setCookieContainer(string coks)
            {
                var container = new CookieContainer();

                if (!coks.Contains("dle_user_taken"))
                    coks = $"dle_user_taken=1; {coks}";

                foreach (string line in coks.Split(";"))
                {
                    if (string.IsNullOrEmpty(line) || !line.Contains("="))
                        continue;

                    var g = Regex.Match(line.Trim(), "^([^=]+)=([^\n\r]+)").Groups;
                    string name = g[1].Value.Trim();
                    string value = g[2].Value.Trim();

                    if (name is "CLID" or "MUID" or "_clck" or "_clsk")
                        continue;

                    if (name.StartsWith("_ym_"))
                        continue;

                    container.Add(new Cookie()
                    {
                        Path = "/",
                        Expires = name == "PHPSESSID" ? default : DateTime.Today.AddYears(1),
                        Domain = $".{domain}",
                        Name = name,
                        Value = value,
                        HttpOnly = true
                    });
                }

                cookieContainer[keyCookie] = container;
            }
            #endregion

            if (!string.IsNullOrEmpty(init.cookie))
            {
                setCookieContainer(init.cookie.Trim());
                return cookieContainer[keyCookie];
            }

            if (string.IsNullOrEmpty(init.login) || string.IsNullOrEmpty(init.passwd))
            {
                setCookieContainer(string.Empty);
                return cookieContainer[keyCookie];
            }

            if (memoryCache.TryGetValue("rezka:login", out _))
                return null;

            memoryCache.Set("rezka:login", 0, TimeSpan.FromMinutes(2));

            try
            {
                using (var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                })
                {
                    clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                    using (var client = new System.Net.Http.HttpClient(clientHandler))
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);

                        foreach (var h in Http.defaultFullHeaders)
                            client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

                        var postParams = new Dictionary<string, string>
                        {
                            { "login_name", init.login },
                            { "login_password", init.passwd },
                            { "login_not_save", "0" }
                        };

                        using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                        {
                            using (var response = await client.PostAsync($"{init.host}/ajax/login/", postContent))
                            {
                                if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                                {
                                    string cookie = string.Empty;

                                    foreach (string line in cook)
                                    {
                                        if (string.IsNullOrEmpty(line))
                                            continue;

                                        if (line.Contains("=deleted;") || !line.Contains(domain))
                                            continue;

                                        string c = line.Split(";")[0];
                                        if (c.Contains("="))
                                        {
                                            string name = c.Split("=")[0];
                                            if (cookie.Contains(name))
                                            {
                                                cookie = Regex.Replace(cookie, $"{name}=[^;]+", $"{name}={c.Split("=")[1]}");
                                            }
                                            else
                                            {
                                                cookie += $"{c}; ";
                                            }
                                        }
                                    }

                                    if (cookie.Contains("dle_user_id") && cookie.Contains("dle_password"))
                                    {
                                        setCookieContainer(cookie.Trim());
                                        return cookieContainer[keyCookie];
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "Rezka", "id_lt5af0qc");
            }

            return null;
        }
        #endregion

        #region getRhubCookie
        static ConcurrentDictionary<string, string> rhubCookies = new();

        string getRhubCookie()
        {
            if (string.IsNullOrWhiteSpace(init.cookie))
                return null;

            if (rhubCookies.TryGetValue(init.cookie, out string _cook))
                return _cook;

            string rhubCookie = string.Empty;
            string domain = Regex.Match(init.host, "https?://([^/]+)").Groups[1].Value;

            foreach (string line in init.cookie.Split(";"))
            {
                if (string.IsNullOrEmpty(line) || !line.Contains("="))
                    continue;

                var g = Regex.Match(line.Trim(), "^([^=]+)=([^\n\r]+)").Groups;
                string name = g[1].Value.Trim();
                string value = g[2].Value.Trim();

                if (name is "CLID" or "MUID" or "_clck" or "_clsk")
                    continue;

                if (name.StartsWith("_ym_"))
                    continue;

                if (name != "PHPSESSID")
                    rhubCookie += $"{name}={value}; ";

            }

            rhubCookie = Regex.Replace(rhubCookie.Trim(), ";$", "");
            rhubCookies[init.cookie] = rhubCookie;

            return rhubCookie;
        }
        #endregion
    }
}
