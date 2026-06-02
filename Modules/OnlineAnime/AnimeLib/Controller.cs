using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Pools.Json;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace AnimeLib;

public class AnimeLibController : BaseOnlineController
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<AnimeLibController>();

    public AnimeLibController() : base(ModInit.conf) { }


    [HttpGet, Staticache(manually: true)]
    [Route("lite/animelib")]
    async public Task<ActionResult> Index(string title, string original_title, short year, string uri, string t, bool rjson = false, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (rch?.enable != true)
            await EnsureAnimeLibToken();

        if (string.IsNullOrEmpty(init.token))
            return OnError("token", statusCode: 401, gbcache: false);

        var bearer = HeadersModel.Init("authorization", $"Bearer {init.token}");

        if (string.IsNullOrWhiteSpace(uri))
        {
            #region Поиск
            if (string.IsNullOrWhiteSpace(title))
                return OnError();

        rhubFallback:
            var search = await InvokeCacheResult<List<(string title, string year, string uri, bool coincidence, string cover)>>($"animelib:search:{title}:{original_title}", TimeSpan.FromHours(4), async e =>
            {
                async Task<DataSearch[]> goSearch(string q)
                {
                    if (string.IsNullOrEmpty(q))
                        return null;

                    string req_uri = $"{init.host}/api/anime?fields[]=rate_avg&fields[]=rate&fields[]=releaseDate&q={HttpUtility.UrlEncode(q)}";

                    var result = await httpHydra.Get<ApiResponse<DataSearch[]>>(req_uri, addheaders: bearer, safety: true);
                    var data = result?.data;
                    if (data == null || data.Length == 0)
                        return null;

                    return data;
                }

                var result = await goSearch(original_title);

                if (result == null || result.Length == 0)
                    result = await goSearch(title);

                if (result == null || result.Length == 0)
                    return e.Fail(string.Empty, refresh_proxy: true);

                string stitle = SearchNameTo.Convert(title);
                string soriginal = SearchNameTo.Convert(original_title);

                var catalog = new List<(string title, string year, string uri, bool coincidence, string cover)>(result.Length);

                foreach (var anime in result)
                {
                    if (string.IsNullOrEmpty(anime.slug_url))
                        continue;

                    var model = ($"{anime.rus_name} / {anime.eng_name}", (anime.releaseDate != null ? anime.releaseDate.Split("-")[0] : "0"), anime.slug_url, false, anime.cover.@default);

                    if (SearchNameTo.StartsWith(anime.rus_name, stitle) ||
                        SearchNameTo.StartsWith(anime.eng_name, stitle) ||
                        SearchNameTo.StartsWith(anime.eng_name, soriginal))
                        model.Item4 = true;

                    catalog.Add(model);
                }

                if (catalog.Count == 0)
                    return e.Fail("catalog");

                return e.Success(catalog);
            });

            if (IsRhubFallback(search))
                goto rhubFallback;

            if (!search.IsSuccess)
                return OnError(search.ErrorMsg);

            var catalog = search.Value;

            if (!similar && catalog.Count(i => i.coincidence) == 1)
                return LocalRedirect(accsArgs($"/lite/animelib?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&uri={HttpUtility.UrlEncode(catalog.First(i => i.coincidence).uri)}"));

            var stpl = new SimilarTpl(catalog.Count);

            foreach (var res in catalog)
            {
                stpl.Append(
                    res.title,
                    res.year,
                    string.Empty,
                    $"{host}/lite/animelib?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&uri={HttpUtility.UrlEncode(res.uri)}",
                    PosterApi.Size(res.cover, HeadersModel.InitOrNull(init.headers_image))
                );

            }
            return ContentTpl(stpl);
            #endregion
        }
        else
        {
            #region Серии
        rhubFallback:
            var cache = await InvokeCacheResult<Episode[]>($"animelib:playlist:{uri}", TimeSpan.FromHours(1), async e =>
            {
                string req_uri = $"{init.host}/api/episodes?anime_id={uri}";

                var root = await httpHydra.Get<ApiResponse<Episode[]>>(req_uri, addheaders: bearer, safety: true);
                if (root?.data == null)
                    return e.Fail(string.Empty, refresh_proxy: true);

                var episodes = root.data;

                if (episodes == null || episodes.Length == 0)
                    return e.Fail("episodes");

                return e.Success(episodes);
            });

            if (IsRhubFallback(cache, safety: true))
                goto rhubFallback;

            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            var episodes = cache.Value;

            #region Перевод
            string voice_memkey = $"animelib:video:{episodes.First().id}";
            if (!hybridCache.TryGetValue(voice_memkey, out Player[] players))
            {
                string req_uri = $"{init.host}/api/episodes/{episodes.First().id}";

                var root = await httpHydra.Get<ApiResponse<EpisodeDetails>>(req_uri, addheaders: bearer, safety: true);
                var playersData = root?.data?.players;
                if (playersData == null)
                    return OnError(refresh_proxy: true);

                players = playersData;

                if (players == null)
                    return OnError();

                hybridCache.Set(voice_memkey, players, TimeSpan.FromHours(1));
            }

            var vtpl = new VoiceTpl(players.Length);
            string activTranslate = t;

            foreach (var player in players)
            {
                if (player.player != "Animelib")
                    continue;

                if (string.IsNullOrEmpty(activTranslate))
                    activTranslate = player.team.name;

                vtpl.Append(
                    player.team.name,
                    activTranslate == player.team.name,
                    $"{host}/lite/animelib?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&uri={HttpUtility.UrlEncode(uri)}&t={HttpUtility.UrlEncode(player.team.name)}"
                );
            }
            #endregion

            var etpl = new EpisodeTpl(vtpl, episodes.Length);

            foreach (var episode in episodes)
            {
                string link = $"{host}/lite/animelib/video?id={episode.id}&voice={HttpUtility.UrlEncode(activTranslate)}&title={HttpUtility.UrlEncode(title)}";

                etpl.Append(
                    $"{episode.number} серия",
                    string.IsNullOrEmpty(episode.name) ? title : $"{title} / {episode.name}",
                    episode.season,
                    episode.number,
                    link,
                    "call",
                    streamlink: accsArgs($"{link}&play=true")
                );
            }

            return ContentTpl(etpl);
            #endregion
        }
    }

    #region Video
    [HttpGet, Staticache(manually: true)]
    [Route("lite/animelib/video")]
    async public Task<ActionResult> Video(string title, long id, string voice, bool play)
    {
        if (await IsRequestBlocked(rch: true, rch_check: false))
            return badInitMsg;

        if (rch?.enable != true)
            await EnsureAnimeLibToken();

        if (string.IsNullOrEmpty(init.token))
            return OnError();

        if (rch != null)
        {
            if (rch.IsNotConnected())
            {
                if (init.rhub_fallback && play)
                    rch.Disabled();
                else
                    return Content(rch.connectionMsg, "application/json; charset=utf-8");
            }

            if (!play && rch.IsRequiredConnected())
                return Content(rch.connectionMsg, "application/json; charset=utf-8");
        }

    rhubFallback:
        var cache = await InvokeCacheResult<Player[]>($"animelib:video:{id}", 30, async e =>
        {
            string req_uri = $"{init.host}/api/episodes/{id}";
            var bearer = HeadersModel.Init("authorization", $"Bearer {init.token}");

            var root = await httpHydra.Get<ApiResponse<EpisodeDetails>>(req_uri, addheaders: bearer, safety: true);
            if (root?.data?.players == null)
                return e.Fail("data", refresh_proxy: true);

            return e.Success(root.data.players);
        });

        if (IsRhubFallback(cache, safety: true))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var headers_stream = httpHeaders(init.host, init.headers_stream);

        IReadOnlyList<StreamQualityDto> streams;

        if (string.IsNullOrEmpty(voice))
        {
            streams = goStreams(cache.Value, null, headers_stream);
        }
        else
        {
            streams = goStreams(cache.Value, voice, headers_stream);
            if (streams.Count == 0)
                streams = goStreams(cache.Value, null, headers_stream);
        }

        if (streams == null || streams.Count == 0)
            return OnError("streams");

        var streamquality = new StreamQualityTpl(streams);

        var first = streamquality.Firts();
        if (first == null)
            return OnError();

        if (play)
            return RedirectToPlay(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            title,
            streamquality: streamquality,
            vast: init.vast,
            headers: init.streamproxy ? null : headers_stream,
            httpContext: HttpContext
        ));
    }
    #endregion


    #region goStreams
    IReadOnlyList<StreamQualityDto> goStreams(Player[] players, string _voice, IReadOnlyList<HeadersModel> headers_stream)
    {
        var _streams = new List<StreamQualityDto>(20);

        foreach (var player in players)
        {
            if (player.player != "Animelib")
                continue;

            if (!string.IsNullOrEmpty(_voice) && _voice != player.team.name)
                continue;

            foreach (var video in player.video.quality)
            {
                if (string.IsNullOrEmpty(video.href))
                    continue;

                string file = HostStreamProxy("https://video1.cdnlibs.org/.%D0%B0s/" + video.href, headers: headers_stream);

                _streams.Add(new StreamQualityDto(file, $"{video.quality}p"));
            }

            if (_streams.Count > 0)
                break;
        }

        return _streams;
    }
    #endregion

    #region [Codex AI] EnsureAnimeLibToken / RequestAnimeLibToken
    async ValueTask EnsureAnimeLibToken()
    {
        if (!string.IsNullOrEmpty(init.token))
            return;

        var semaphore = new SemaphorManager("EnsureAnimeLibToken", TimeSpan.FromSeconds(30));

        try
        {
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return;

            AnimeLibTokenState cache = null;
            string TokenCachePath = Path.Combine("cache", "animelib.json");

            try
            {
                if (System.IO.File.Exists(TokenCachePath))
                {
                    string json = System.IO.File.ReadAllText(TokenCachePath);
                    cache = JsonConvert.DeserializeObject<AnimeLibTokenState>(json);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_tgxteoac");
            }

            if (cache == null)
                return;

            if (!string.IsNullOrEmpty(cache.token) && cache.refresh_time > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                init.token = cache.token;
                return;
            }

            var tokens = await RequestAnimeLibToken(cache.refresh_token);
            if (tokens == null)
                return;

            cache = new AnimeLibTokenState
            {
                token = tokens.Value.accessToken,
                refresh_token = tokens.Value.refreshToken,
                // 2592000 секунд / 60 = 43200 минут
                // 43200 минут / 60 = 720 часов
                // 720 часов / 24 = 30 дней
                refresh_time = DateTimeOffset.UtcNow.AddDays(20).ToUnixTimeSeconds()
            };

            try
            {
                System.IO.File.WriteAllText(TokenCachePath, JsonConvertPool.SerializeObject(cache));
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_u3rrlihk");
            }

            init.token = cache.token;
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_uvmqs2q9");
        }
        finally
        {
            semaphore.Release();
        }
    }

    async Task<(string accessToken, string refreshToken)?> RequestAnimeLibToken(string refreshToken)
    {
        var payload = JsonConvertPool.SerializeObject(new
        {
            grant_type = "refresh_token",
            client_id = "1",
            refresh_token = refreshToken,
            scope = string.Empty
        });

        using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
        {
            var headers = HeadersModel.Init(Http.defaultFullHeaders,
                ("accept", "*/*"),
                ("origin", "https://anilib.me"),
                ("referer", "https://anilib.me/"),
                ("accept-language", "en-US,en;q=0.9,ru;q=0.8"),
                ("client-time-zone", "Europe/Kiev"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "cross-site"),
                ("site-id", "5")
            );

            var result = await Http.Post<TokenResponse>("https://api.cdnlibs.org/api/auth/oauth/token", content,
                httpversion: init.httpversion, timeoutSeconds: init.httptimeout, headers: headers, useDefaultHeaders: false
            );

            if (result == null)
                return null;

            //{"token_type":"Bearer","expires_in":2592000,"access_token":"*","refresh_token":"*"}

            string accessToken = result.access_token;
            string newRefreshToken = result.refresh_token;

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(newRefreshToken))
                return null;

            return (accessToken, newRefreshToken);
        }
    }
    #endregion
}
