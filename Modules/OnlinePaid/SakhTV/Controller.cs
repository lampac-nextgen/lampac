using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace SakhTV;

public class SakhTVController : BaseOnlineController<ModuleConf>
{
    #region SakhTV
    List<HeadersModel> bearer;
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttp2Client();

    public SakhTVController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(httpClient);
        };

        requestInitialization = () =>
        {
            bearer = HeadersModel.Init(
                ("x-force-code", "1"),
                ("x-app-id", init.app_id),
                ("user-agent", $"SakhTVAndroid/{init.APP_VERSION}/{init.userAgent}/Android {init.release}"),
                ("authorization", init.token)
            );
        };
    }
    #endregion

    #region Bind
    [HttpGet]
    [AllowAnonymous]
    [Route("/lite/sakhtv/bind")]
    async public Task<ActionResult> Bind(string login, string pass)
    {
        if (!requestInfo.IsLocalIp)
            return ContentTo("is not local ip");

        string html = string.Empty;

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass))
        {
            return ContentTo("Введите данные аккаунта sakh.tv <br> <br><form method=\"get\" action=\"/lite/sakhtv/bind\"><input type=\"text\" name=\"login\" placeholder=\"логин\"> &nbsp; &nbsp; <input type=\"text\" name=\"pass\" placeholder=\"пароль\"><br><br><button>Авторизоваться</button></form>");
        }
        else
        {
            var result = await Http.Post<JObject>(
                $"{init.host}/v2/users/login",
                new StringContent($"{{\"login\":\"{login}\",\"password\":\"{pass}\"}}", Encoding.UTF8, "application/json"),
                httpversion: init.httpversion,
                proxy: proxy,
                headers: HeadersModel.Init(
                    ("x-force-code", "1"),
                    ("x-app-id", init.app_id),
                    ("user-agent", $"SakhTVAndroid/{init.APP_VERSION}/{init.userAgent}/Android {init.release}"),
                    ("authorization", Guid.NewGuid().ToString())
                ),
                useDefaultHeaders: false
            );

            if (result == null)
                return ContentTo("Ошибка авторизации ;(");

            string token = result.Value<string>("token");
            if (string.IsNullOrEmpty(token))
                return ContentTo(result.ToString(Formatting.Indented));

            return ContentTo("Добавьте в init.conf<br><br>\"SakhTV\": {<br>&nbsp;&nbsp;\"enable\": true,<br>&nbsp;&nbsp;\"token\": \"" + token + "\"<br>}");
        }
    }
    #endregion

    [HttpGet]
    [Route("lite/sakhtv")]
    public async Task<ActionResult> Index(string orid, string imdb_id, long kinopoisk_id, string title, string original_title, int year, int serial, int s = -1, string t = null, bool rjson = false, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(init.token))
            return OnError();

        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(original_title))
            return OnError();

    rhubFallback:

        #region search
        SearchRoot searchRoot = null;

        if (string.IsNullOrEmpty(orid))
        {
            var search = await InvokeCacheResult<SearchRoot>($"sakhtv:search:{title}:{original_title}", 180, async e =>
            {
                var root = await httpHydra.Get<SearchRoot>($"{init.host}/v2/common/search?query={HttpUtility.UrlEncode(original_title)}&amount=20",
                    useDefaultHeaders: false,
                    newheaders: bearer,
                    safety: true
                );

                if (root?.serials == null && root?.movies == null)
                    return e.Fail("search", refresh_proxy: true);

                return e.Success(root);
            });

            if (IsRhubFallback(search, safety: true))
                goto rhubFallback;

            if (!search.IsSuccess)
                return OnError(search.ErrorMsg);

            searchRoot = search.Value;
        }
        #endregion

        if (serial == 1)
        {
            #region Сериал
            if (searchRoot != null)
            {
                var serials = searchRoot.serials;
                if (serials == null || serials.Count == 0)
                    return OnError();

                var idResult = searchId(serials, imdb_id, kinopoisk_id, title, original_title, year, serial, similar);
                if (idResult.id == null)
                    return ContentTpl(idResult.similar);

                orid = idResult.id;
            }

            var tvshow = await InvokeCacheResult<Season[]>($"sakhtv:tvshow:{orid}", 90, async e =>
            {
                var root = await httpHydra.Get<TvshowDetails>($"{init.host}/v1/serials/get?tvshow={orid}",
                    useDefaultHeaders: false,
                    newheaders: bearer,
                    safety: true
                );

                if (root?.seasons == null || root.seasons.Length == 0)
                    return e.Fail("tvshow", refresh_proxy: true);

                return e.Success(root.seasons);
            });

            if (IsRhubFallback(tvshow, safety: true))
                goto rhubFallback;

            if (!tvshow.IsSuccess)
                return OnError(tvshow.ErrorMsg);

            string defaultargs = $"&orid={orid}&imdb_id={imdb_id}&kinopoisk_id={kinopoisk_id}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial={serial}";

            if (s == -1)
            {
                var tpl = new SeasonTpl();

                foreach (var season in tvshow.Value)
                {
                    if (int.TryParse(season.index, out int seasonNum) && seasonNum > 0)
                    {
                        tpl.Append(
                            $"{seasonNum} сезон",
                            $"{host}/lite/sakhtv?rjson={rjson}&s={seasonNum}{defaultargs}",
                            seasonNum
                        );
                    }
                }

                return ContentTpl(tpl);
            }
            else
            {
                int seasonId = tvshow.Value.FirstOrDefault(i => int.TryParse(i.index, out int seasonNum) && seasonNum == s)?.id ?? 0;
                if (seasonId == 0)
                    return OnError();

                #region episodes
                var episodes = await InvokeCacheResult<EpisodeDetails[]>($"sakhtv:episodes:{seasonId}", 90, async e =>
                {
                    var root = await httpHydra.Get<EpisodeDetails[]>($"{init.host}/v1/serials/get_episodes?season_id={seasonId}",
                        addheaders: bearer,
                        safety: true
                    );

                    if (root == null || root.Length == 0)
                        return e.Fail("episodes", refresh_proxy: true);

                    return e.Success(root);
                });

                if (IsRhubFallback(episodes, safety: true))
                    goto rhubFallback;

                if (!tvshow.IsSuccess)
                    return OnError(episodes.ErrorMsg);
                #endregion

                #region Перевод
                var vtpl = new VoiceTpl();
                var temp_translation = new HashSet<string>();

                foreach (var e in episodes.Value)
                {
                    foreach (var r in e.rgs)
                    {
                        if (temp_translation.Add(r.rg))
                        {
                            if (t == null)
                                t = r.rg;

                            vtpl.Append(
                                r.runame,
                                t == r.rg,
                                $"{host}/lite/sakhtv?rjson={rjson}&s={s}&t={r.rg}{defaultargs}"
                            );
                        }
                    }
                }
                #endregion

                var etpl = new EpisodeTpl(vtpl);
                string sArhc = s.ToString();

                foreach (var episode in episodes.Value)
                {
                    foreach (var r in episode.rgs)
                    {
                        if (r.rg == t)
                        {
                            string e = episode.index;
                            string link = $"{host}/lite/sakhtv/video.m3u8?season_id={seasonId}&rg={r.rg}&e={e}";

                            etpl.Append(
                                $"{e} серия",
                                string.IsNullOrEmpty(episode.name) ? (title ?? original_title) : episode.name,
                                sArhc,
                                e,
                                accsArgs(link)
                            );

                            break;
                        }
                    }
                }

                return ContentTpl(etpl);
            }
            #endregion
        }
        else
        {
            #region Фильм
            if (searchRoot != null)
            {
                var movies = searchRoot.movies;
                if (movies == null || movies.Count == 0)
                    return OnError();

                var idResult = searchId(movies, imdb_id, kinopoisk_id, title, original_title, year, serial, similar);
                if (idResult.id == null)
                    return ContentTpl(idResult.similar);

                orid = idResult.id;
            }

            var cache = await InvokeCacheResult<Source>($"sakhtv:movies:{orid}:{init.token}", 20, async e =>
            {
                var root = await httpHydra.Get<MovieDetails>($"{init.host}/v2/movie/{orid}",
                    useDefaultHeaders: false,
                    newheaders: bearer,
                    safety: true
                );

                if (string.IsNullOrEmpty(root?.sources?.@default))
                    return e.Fail("movies", refresh_proxy: true);

                return e.Success(root.sources);
            });

            if (IsRhubFallback(cache, safety: true))
                goto rhubFallback;

            return ContentTpl(cache, () =>
            {
                var mtpl = new MovieTpl(title, original_title);

                mtpl.Append(
                    title ?? original_title,
                    HostStreamProxy(cache.Value.@default)
                );

                return mtpl;
            });
            #endregion
        }
    }

    #region Video
    [HttpGet]
    [Route("lite/sakhtv/video.m3u8")]
    async public Task<ActionResult> Video(int season_id, string rg, string e)
    {
        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        if (rch != null)
        {
            if (rch.IsNotConnected())
            {
                if (init.rhub_fallback)
                    rch.Disabled();
                else
                    return ContentTo(rch.connectionMsg);
            }
        }

    rhubFallback:
        var cache = await InvokeCacheResult<Episode[]>($"sakhtv:video:{season_id}:{rg}:{init.token}", 20, async e =>
        {
            var root = await httpHydra.Get<Episode[]>($"{init.host}/v1/serial/watch/get_playlist?season_id={season_id}&rg={rg}",
                useDefaultHeaders: false,
                newheaders: bearer,
                safety: true
            );

            if (root == null || root.Length == 0)
                return e.Fail("playlist", refresh_proxy: true);

            return e.Success(root);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        string m3u8 = cache.Value.First(i => i.episode_index == e).episode_playlist;

        return Redirect(HostStreamProxy(m3u8));
    }
    #endregion


    #region searchId
    (string id, SimilarTpl similar) searchId(List<SearchItem> items, string imdb_id, long kinopoisk_id, string title, string original_title, int year, int serial, bool similar)
    {
        var ids = new List<string>();
        var stpl = new SimilarTpl(items.Count);

        string enc_title = HttpUtility.UrlEncode(title);
        string enc_original_title = HttpUtility.UrlEncode(original_title);

        string stitle = SearchNameTo.Convert(title);
        string sorig = SearchNameTo.Convert(original_title);

        foreach (var item in items)
        {
            if (similar == false)
            {
                if (imdb_id != null && item.imdb_url != null && item.imdb_url.EndsWith(imdb_id))
                    return (item.tvshow, null);

                if (kinopoisk_id > 0 && item.kp_id == kinopoisk_id)
                    return (item.tvshow, null);
            }

            string titleRu = item.name ?? item.ru_title;
            string titleEn = item.ename ?? item.origin_title;

            string name = titleRu ?? titleEn;
            if (titleRu != null && titleEn != null)
                name = $"{titleRu} / {titleEn}";

            string release_date = item.release_date != null
                ? item.release_date.Split("-")[0]
                : item.year.ToString();

            stpl.Append(
                name,
                release_date,
                null,
                $"{host}/lite/sakhtv?orid={item.id_alpha ?? item.tvshow}&title={enc_title}&original_title={enc_original_title}&year={year}&serial={serial}",
                PosterApi.Size(item.cover ?? item.poster)
            );

            if (SearchNameTo.Equals(titleRu, stitle) || SearchNameTo.Equals(titleEn, sorig))
            {
                if (release_date != "0" && release_date == year.ToString())
                    ids.Add(item.id_alpha ?? item.tvshow);
            }
        }

        if (ids.Count == 1 && similar == false)
            return (ids[0], stpl);

        return (null, stpl);
    }
    #endregion
}
