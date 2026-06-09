using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using System.Web;

namespace Shared;

public class BaseENGController : BaseOnlineController
{
    public BaseENGController(OnlinesSettings init) : base(init) { }

    static readonly int hlsTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;

    async public Task<ActionResult> ViewTmdb(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false, bool mp4 = false, string method = "play", int hls_manifest_timeout = 0, string extension = "m3u8")
    {
        if (checksearch)
            return Content("data-json=");

        if (await IsRequestBlocked(rch: false))
            return badInitMsg;

        if (tmdb_id > 0)
            id = tmdb_id;

        if (hls_manifest_timeout == 0)
            hls_manifest_timeout = hlsTimeout;

        if (serial == 1)
        {
            #region Сериал
            var tmdb = await InvokeCacheResult<JToken>($"tmdb:seasons:{id}", TimeSpan.FromHours(4), async e =>
            {
                var cub = CoreInit.conf.cub;

                var proxyManager = cub.useproxy
                    ? new ProxyManager("cub_api", cub)
                    : null;

                var root = await Http.Get<JObject>($"{cub.scheme}://tmdb.{cub.mirror}/3/tv/{id}?api_key={cub.api_key}", proxy: proxyManager?.Get());

                if (root == null || !root.ContainsKey("seasons"))
                    return e.Fail("seasons");

                return e.Success(root["seasons"]);
            });

            if (!tmdb.IsSuccess)
                return OnError(tmdb.ErrorMsg);

            if (s == -1)
            {
                #region Сезоны
                var tpl = new SeasonTpl();
                string enc_title = HttpUtility.UrlEncode(title);
                string enc_original_title = HttpUtility.UrlEncode(original_title);

                foreach (var season in tmdb.Value)
                {
                    int number = season.Value<int>("season_number");
                    if (1 > number)
                        continue;

                    tpl.Append(
                        $"{number} сезон",
                        $"{host}/lite/{init.plugin.ToLower()}?id={id}&imdb_id={imdb_id}&serial=1&rjson={rjson}&title={enc_title}&original_title={enc_original_title}&s={number}",
                        number
                    );
                }

                return ContentTpl(tpl);
                #endregion
            }
            else
            {
                #region Серии
                var etpl = new EpisodeTpl();

                foreach (var season in tmdb.Value)
                {
                    if (season.Value<int>("season_number") != s)
                        continue;

                    for (short i = 1; i <= season.Value<short>("episode_count"); i++)
                    {
                        string path = (mp4 || method == "call") ? "video" : $"video.{extension}";
                        string uri = $"{host}/lite/{init.plugin.ToLower()}/{path}?id={id}&imdb_id={imdb_id}&s={s}&e={i}";
                        string stream = method == "call" ? accsArgs($"{host}/lite/{init.plugin.ToLower()}/{(mp4 ? "video" : $"video.{extension}")}?id={id}&imdb_id={imdb_id}&s={s}&e={i}&play=true") : null;

                        if (method == "play")
                            uri = accsArgs(uri);

                        etpl.Append(
                            $"{i} серия",
                            title ?? original_title,
                            s,
                            i,
                            uri,
                            method,
                            streamlink: stream,
                            vast: init.vast,
                            hls_manifest_timeout: hls_manifest_timeout
                        );
                    }
                }

                return ContentTpl(etpl);
                #endregion
            }
            #endregion
        }
        else
        {
            #region Фильм
            var mtpl = new MovieTpl(title, original_title);

            string path = (mp4 || method == "call") ? "video" : $"video.{extension}";
            string uri = $"{host}/lite/{init.plugin.ToLower()}/{path}?id={id}&imdb_id={imdb_id}";
            string stream = method == "call" ? accsArgs($"{host}/lite/{init.plugin.ToLower()}/{(mp4 ? "video" : $"video.{extension}")}?id={id}&imdb_id={imdb_id}&play=true") : null;

            if (method == "play")
                uri = accsArgs(uri);

            mtpl.Append(
                "English",
                uri,
                method,
                stream: stream,
                vast: init.vast,
                hls_manifest_timeout: hls_manifest_timeout
            );

            return ContentTpl(mtpl);
            #endregion
        }
    }
}
