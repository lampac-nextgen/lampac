using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Threading.Tasks;

namespace RgShows;

public class RgShowsController : BaseENGController
{
    public RgShowsController() : base(ModInit.conf)
    {
    }

    [HttpGet]
    [Route("lite/rgshows")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, int serial, int s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, mp4: true, method: "call", hls_manifest_timeout: (int)TimeSpan.FromSeconds(30).TotalMilliseconds);
    }

    [HttpGet]
    [Route("lite/rgshows/video")]
    public async Task<ActionResult> Video(long id, int s = -1, int e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        string embed = $"{init.host}/main/movie/{id}";
        if (s > 0)
            embed = $"{init.host}/main/tv/{id}/{s}/{e}";

        string file = await black_magic(embed);
        if (file == null)
            return OnError("file", 502);

        string stream = HostStreamProxy(file);

        if (play)
            return RedirectToPlay(stream);

        return ContentTo(VideoTpl.ToJson(
            "play",
            stream,
            "English",
            vast: init.vast,
            headers: init.streamproxy
                ? null
                : httpHeaders(init.host, init.headers_stream),
            hls_manifest_timeout: (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
            httpContext: HttpContext
        ));
    }


    async ValueTask<string> black_magic(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return uri;

        try
        {
            string memKey = $"rgshows:{uri}";
            if (!hybridCache.TryGetValue(memKey, out string file))
            {
                var root = await Http.Get<JObject>(uri, proxy: proxy, timeoutSeconds: 40, httpversion: 2, headers: httpHeaders(init));
                if (root == null || !root.ContainsKey("stream"))
                {
                    proxyManager?.Refresh();
                    return null;
                }

                file = root["stream"].Value<string>("url");
                if (string.IsNullOrEmpty(file))
                    return null;

                proxyManager?.Success();
                hybridCache.Set(memKey, file, cacheTime(20));
            }

            return file;
        }
        catch
        {
            return null;
        }
    }
}
