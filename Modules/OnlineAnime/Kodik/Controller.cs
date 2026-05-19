using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared.Attributes;
using Shared.Services.Pools;
using Shared;
using Shared.Models.Templates;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;
using System.Security.Cryptography;
using System.Text;

namespace Kodik;

public class KodikController : BaseOnlineController<ModuleConf>
{
    #region static
    static KodikController()
    {
        CoreInit.BaseModValidQueryValueWhiteList.Add("pick");
    }
    #endregion

    #region HMAC
    const string hexChars = "0123456789abcdef";
    static readonly ConcurrentDictionary<string, byte[]> keyBytes = new ConcurrentDictionary<string, byte[]>();

    static string HMAC(string key, string message)
    {
        byte[] arraykey = keyBytes.GetOrAdd(key, _ => Encoding.UTF8.GetBytes(key));

        int maxbytes = Encoding.UTF8.GetMaxByteCount(message.Length);

        using (var msgBuf = new BufferBytePool(maxbytes))
        {
            Span<byte> hash = stackalloc byte[32];
            Span<char> hex = stackalloc char[64];

            int bytesWritten = Encoding.UTF8.GetBytes(message, msgBuf.Span);

            using (var hmac = new HMACSHA256(arraykey))
            {
                if (!hmac.TryComputeHash(msgBuf.Span.Slice(0, bytesWritten), hash, out _))
                    return null;

                for (int i = 0; i < 32; i++)
                {
                    byte b = hash[i];
                    hex[i * 2] = hexChars[b >> 4];
                    hex[i * 2 + 1] = hexChars[b & 0xF];
                }

                return new string(hex);
            }
        }
    }
    #endregion

    #region KodikInvoke
    KodikInvoke oninvk;

    public KodikController() : base(ModInit.conf)
    {
        loadKitInitialization = (j, i, c) =>
        {
            if (j.ContainsKey("playerhost"))
                i.playerhost = c.playerhost;

            if (j.ContainsKey("secret_token"))
                i.secret_token = c.secret_token;

            if (j.ContainsKey("auto_proxy"))
                i.auto_proxy = c.auto_proxy;

            if (j.ContainsKey("cdn_is_working"))
                i.cdn_is_working = c.cdn_is_working;

            return i;
        };

        requestInitialization = () =>
        {
            oninvk = new KodikInvoke
            (
                host,
                init,
                "video",
                httpHydra,
                streamfile => HostStreamProxy(streamfile)
            );
        };
    }
    #endregion

    [HttpGet]
    [Staticache]
    [Route("lite/kodik")]
    async public Task<ActionResult> Index(string imdb_id, long kinopoisk_id, string title, string original_title, int clarification, string pick, string kid, int s = -1, bool rjson = false, bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        List<Result> content = null;

        if (similar || clarification == 1 || (kinopoisk_id == 0 && string.IsNullOrEmpty(imdb_id)))
        {
            EmbedModel res = null;

            if (clarification == 1)
            {
                if (string.IsNullOrEmpty(title))
                    return OnError();

                res = await InvokeCache($"kodik:search:{title}", 40, () => oninvk.Embed(title, null, clarification));
                if (res?.result == null || res.result.Count == 0)
                    return OnError();
            }
            else
            {
                if (string.IsNullOrEmpty(pick) && string.IsNullOrEmpty(title ?? original_title))
                    return OnError();

                res = await InvokeCache($"kodik:search2:{original_title}:{title}:{clarification}", 40, async () =>
                {
                    var i = await oninvk.Embed(null, original_title, clarification);
                    if (i?.result == null || i.result.Count == 0)
                        return await oninvk.Embed(title, null, clarification);

                    return i;
                });
            }

            if (string.IsNullOrEmpty(pick))
                return ContentTpl(res?.stpl);

            content = oninvk.Embed(res.result, pick);
        }
        else
        {
            content = await InvokeCache($"kodik:search:{kinopoisk_id}:{imdb_id}", 40,
                () => oninvk.Embed(imdb_id, kinopoisk_id, s),
                textJson: true
            );

            if (content == null || content.Count == 0)
                return LocalRedirect(accsArgs($"/lite/kodik?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}"));
        }

        return ContentTpl(oninvk.Tpl(content, accsArgs(string.Empty), imdb_id, kinopoisk_id, title, original_title, clarification, pick, kid, s, rjson));
    }

    #region Video
    [HttpGet]
    [Route("lite/kodik/video")]
    [Route("lite/kodik/video.m3u8")]
    async public Task<ActionResult> VideoAPI(string title, string original_title, string link, int episode, bool play)
    {
        if (await IsRequestBlocked(rch: true, rch_check: !play))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(init.secret_token))
        {
            var streams = await InvokeCache($"kodik:video:{link}:{play}", 40,
                () => oninvk.VideoParse(init.playerhost, link),
                textJson: true
            );

            if (streams == null)
                return OnError();

            string result = oninvk.VideoParse(streams, title, original_title, episode, play, HttpContext, vast: init.vast);
            if (string.IsNullOrEmpty(result))
                return OnError();

            if (play)
                return RedirectToPlay(result);

            return ContentTo(result);
        }
        else
        {
            string userIp = requestInfo.IP;
            if (init.localip)
            {
                userIp = await mylocalip();
                if (userIp == null)
                    return OnError();
            }

            var cache = await InvokeCacheResult<(List<(string q, string url)> streams, SegmentTpl segments)>($"kodik:view:stream:{link}:{init.secret_token}:{requestInfo.IP}", 120, async e =>
            {
                string deadline = DateTime.Now.AddHours(4).ToString("yyyyMMddHH");
                string hmac = HMAC(init.secret_token, $"{link}:{userIp}:{deadline}");
                if (hmac == null)
                    return e.Fail("hmac");

                string uri = $"{init.linkhost}/api/video-links?link={link}&p={init.token}&ip={userIp}&d={deadline}&s={hmac}&auto_proxy={init.auto_proxy.ToString().ToLower()}&skip_segments=true";

                var root = await httpHydra.Get<JObject>(uri, safety: true);

                if (root == null || !root.ContainsKey("links"))
                    return e.Fail("links", refresh_proxy: true);

                var streams = new List<(string q, string url)>(3);

                foreach (var link in root["links"].ToObject<Dictionary<string, JObject>>())
                {
                    string src = link.Value.Value<string>("Src");

                    if (src.StartsWith("http")) { }
                    else if (src.StartsWith("//"))
                        src = $"https:{src}";
                    else
                        src = $"https://{src}";

                    streams.Add(($"{link.Key}p", src));
                }

                if (streams.Count == 0)
                    return e.Fail("streams", refresh_proxy: true);

                streams.Reverse();

                SegmentTpl segments = null;

                if (root.ContainsKey("segments"))
                {
                    var segs = root["segments"] as JObject;
                    if (segs != null)
                    {
                        segments = new SegmentTpl();

                        foreach (string segmentkey in new string[] { "ad", "skip" })
                        {
                            if (segs.ContainsKey(segmentkey))
                            {
                                var arr = segs[segmentkey] as JArray;
                                if (arr != null)
                                {
                                    foreach (var it in arr)
                                    {
                                        int? s = it.Value<int?>("start");
                                        int? e1 = it.Value<int?>("end");
                                        if (s.HasValue && e1.HasValue)
                                            segments.ad(s.Value, e1.Value);
                                    }
                                }
                            }
                        }
                    }
                }

                return e.Success((streams, segments));
            });

            if (!cache.IsSuccess)
                return OnError(cache.ErrorMsg);

            var streamquality = new StreamQualityTpl();
            foreach (var l in cache.Value.streams)
                streamquality.Append(HostStreamProxy(l.url), l.q);

            var first = streamquality.Firts();
            if (first == null)
                return OnError();

            if (play)
                return RedirectToPlay(first.link);

            string name = title ?? original_title;
            if (episode > 0)
                name += $" ({episode} серия)";

            return ContentTo(VideoTpl.ToJson(
                "play",
                first.link,
                name,
                streamquality: streamquality,
                vast: init.vast,
                segments: cache.Value.segments,
                httpContext: HttpContext
            ));
        }
    }
    #endregion
}
