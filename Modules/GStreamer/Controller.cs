using GStreamer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;
using Shared;
using Shared.Attributes;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace GStreamer;

public class GStreamerController : BaseController
{
    #region gst.js
    [AllowAnonymous]
    [Staticache(10, always: true, setHeadersNoCache: true)]
    [HttpGet("/gst.js")]
    [HttpGet("/gst/js/{token}")]
    public ActionResult GstJs(string token)
    {
        if (!ModInit.conf.enable)
            return Content(string.Empty, "application/javascript; charset=utf-8");

        var plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/gst.js", "gst.js")
            .Replace("{localhost}", CoreInit.Host(HttpContext))
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion

    #region tracks.js
    [HttpGet, AllowAnonymous]
    [Staticache(10, always: true, setHeadersNoCache: true)]
    [Route("gst/tracks.js")]
    [Route("gst/tracks/js/{token}")]
    public ActionResult Tracks(string token)
    {
        string plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugins/tracks.js", "gstracks.js")
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion


    #region add
    [HttpGet("/gst/add")]
    public async Task<ActionResult> Add(string link, string linkencode, string uid, string token)
    {
        if (!ModInit.conf.enable)
            return StatusCode(403);

        string user_id = uid ?? token;
        if (ModInit.conf.allowed_uids != null && !ModInit.conf.allowed_uids.Contains(user_id))
            return StatusCode(401);

        var gstask = await GService.GetOrAdd(link ?? CrypTo.DecodeBase64(linkencode), user_id);
        if (gstask.task == null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            return Content(gstask.error);
        }

        var task = gstask.task;

        return Json(new
        {
            id = task.id.ToString(),
            task.user_uid,
            hls = $"{host}/gst/{task.id}/master.m3u8",
            task.probe
        });
    }
    #endregion

    #region remove
    [AllowAnonymous]
    [HttpGet("/gst/remove")]
    public async Task<ActionResult> Remove(ulong id)
    {
        if (!ModInit.conf.enable)
            return StatusCode(403);

        var gstask = GService.Get(id);
        if (gstask != null)
        {
            await gstask.semaphore.WaitAsync(TimeSpan.FromSeconds(10));

            if (GService.TryRemove(id))
                return Json(new { success = true });

            gstask.semaphore.Release();
        }

        return StatusCode(404);
    }
    #endregion

    #region Heartbeat
    [AllowAnonymous]
    [HttpGet("/gst/{id}/heartbeat")]
    public ActionResult Heartbeat(ulong id)
    {
        if (!ModInit.conf.enable)
            return StatusCode(403);

        if (GService.Get(id) != null)
            return Ok();

        return StatusCode(404);
    }
    #endregion


    #region start.m3u8
    [HttpGet("/gst/start.m3u8")]
    public async Task<ActionResult> Start(string link, string linkencode, string uid, string token, int audio)
    {
        if (!ModInit.conf.enable)
            return StatusCode(403);

        string user_id = uid ?? token;
        if (ModInit.conf.allowed_uids != null && !ModInit.conf.allowed_uids.Contains(user_id))
            return StatusCode(401);

        var gstask = await GService.GetOrAdd(link ?? CrypTo.DecodeBase64(linkencode), user_id, audio);
        if (gstask.task == null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            return Content(gstask.error);
        }

        return LocalRedirect($"/gst/{gstask.task.id}/master.m3u8?audio={audio}");
    }
    #endregion

    #region master.m3u8
    [AllowAnonymous]
    [HttpGet("/gst/{id}/master.m3u8")]
    public ActionResult MasterPlaylist(ulong id, int audio)
    {
        SetHeadersNoCache();

        var gstask = GService.Get(id);
        if (gstask == null)
            return NotFound();

        int duration = gstask.probe.DurationSeconds;
        if (0 >= duration)
            duration = 200 * 60; // 200 min

        int segmentSeconds = gstask.conf.segment_seconds;
        int count = duration / segmentSeconds;

        var playlist = StringBuilderPool.Rent();

        try
        {
            playlist.AppendLine("#EXTM3U");
            playlist.AppendLine("#EXT-X-PLAYLIST-TYPE:VOD");
            playlist.AppendLine("#EXT-X-VERSION:7");
            playlist.Append("#EXT-X-TARGETDURATION:")
                    .Append(segmentSeconds)
                    .Append('\n');
            playlist.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
            playlist.Append("#EXT-X-MAP:URI=\"init.mp4?audio=")
                    .Append(audio)
                    .AppendLine("\"");

            for (int i = 0; i < count; i++)
            {
                playlist
                    .Append("#EXTINF:")
                    .Append(segmentSeconds)
                    .AppendLine(".00,");

                playlist
                    .Append("seg/")
                    .Append(i)
                    .AppendLine(".m4s");
            }

            playlist.AppendLine("#EXT-X-ENDLIST");

            return ContentTo(
                playlist,
                "application/vnd.apple.mpegurl; charset=utf-8"
            );
        }
        finally
        {
            StringBuilderPool.Return(playlist);
        }
    }
    #endregion

    #region init.mp4
    [AllowAnonymous]
    [HttpGet("/gst/{id}/init.mp4")]
    public async Task<ActionResult> VideoInit(ulong id, int audio)
    {
        SetHeadersNoCache();

        var gstask = GService.Get(id);
        if (gstask == null)
            return NotFound();

        if (gstask.initMp4 == null)
        {
            await gstask.semaphore.WaitAsync(HttpContext.RequestAborted);

            try
            {
                if (gstask.initMp4 == null)
                    gstask.GetSegment(-1, HttpContext.RequestAborted, audio);
            }
            finally
            {
                gstask.semaphore?.Release();
            }
        }

        if (gstask.initMp4 == null)
            return StatusCode(502);

        Response.Headers.ContentLength = gstask.initMp4.Length;
        return File(gstask.initMp4, "video/mp4", true);
    }
    #endregion

    #region seg
    [AllowAnonymous]
    [HttpGet("/gst/{id}/seg/{index:int}.m4s")]
    public async Task VideoSeg(ulong id, int index)
    {
        SetHeadersNoCache();

        var gstask = GService.Get(id);
        if (gstask == null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (gstask.initMp4 == null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        await gstask.semaphore.WaitAsync(HttpContext.RequestAborted);

        try
        {
            #region Seek
            if (gstask.lastSentSegment == -1)
            {
                gstask.lastSentSegment = index;
            }
            else if (gstask.lastSentSegment != index)
            {
                if (index != gstask.lastSentSegment + 1)
                {
                    int diff = index - gstask.lastSentSegment;

                    int cutoff = gstask.conf.tempfs
                        ? gstask.conf.pipeline_videoQueue * (gstask.conf.tempfs_ring + 2)
                        : gstask.conf.pipeline_videoQueue;

                    if (diff > 0 && Math.Max(60, cutoff) >= (diff * gstask.conf.segment_seconds))
                    {
                        for (int i = 0; i < diff - 1; i++)
                        {
                            if (HttpContext.RequestAborted.IsCancellationRequested)
                                return;

                            gstask.lastSentSegment++;
                            gstask.GetSegment(gstask.lastSentSegment, HttpContext.RequestAborted);
                        }
                    }
                    else
                    {
                        gstask.lastSentSegment = index;
                        bool seekok = gstask.Seek(index * gstask.conf.segment_seconds);
                        if (!seekok)
                        {
                            HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                            return;
                        }
                    }
                }

                gstask.lastSentSegment = index;
            }
            #endregion

            Segment seg = gstask.GetSegment(index, HttpContext.RequestAborted);
            if (seg.audio == null || seg.video == null)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            seg.video.Position = 0;
            seg.audio.Position = 0;

            long totalLength = seg.video.Length + seg.audio.Length;

            Response.ContentType = "video/mp4";
            Response.Headers.AcceptRanges = "bytes";

            var range = Request.GetTypedHeaders()?.Range;

            if (range != null && range.Ranges.Count == 1)
            {
                var item = range.Ranges.First();

                long start;
                long end;

                if (item.From.HasValue)
                {
                    start = item.From.Value;
                    end = item.To ?? totalLength - 1;
                }
                else
                {
                    long suffixLength = item.To ?? 0;

                    if (suffixLength <= 0)
                    {
                        Response.StatusCode = 416;
                        Response.Headers.ContentRange = $"bytes */{totalLength}";
                        return;
                    }

                    suffixLength = Math.Min(suffixLength, totalLength);

                    start = totalLength - suffixLength;
                    end = totalLength - 1;
                }

                if (start >= totalLength || end < start)
                {
                    Response.StatusCode = 416;
                    Response.Headers.ContentRange = $"bytes */{totalLength}";
                    return;
                }

                end = Math.Min(end, totalLength - 1);

                Response.StatusCode = 206;
                Response.Headers.ContentRange = $"bytes {start}-{end}/{totalLength}";

                Response.ContentLength = end - start + 1;

                await CopyRange(
                    seg.video,
                    seg.audio,
                    Response.Body,
                    start,
                    Response.ContentLength.Value,
                    HttpContext.RequestAborted
                );
            }
            else
            {
                Response.ContentLength = totalLength;
                await seg.video.CopyToAsync(Response.Body, HttpContext.RequestAborted);
                await seg.audio.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            }
        }
        finally
        {
            gstask.semaphore?.Release();
        }
    }
    #endregion


    #region Helpers
    static async Task CopyRange(RecyclableMemoryStream video, RecyclableMemoryStream audio, Stream body, long offset, long count, CancellationToken cancellationToken)
    {
        using (var nbuf = new BufferPool())
        {
            if (offset < video.Length)
            {
                video.Position = offset;

                long videoCount = Math.Min(
                    count,
                    video.Length - offset
                );

                while (videoCount > 0)
                {
                    int read = video.Read(nbuf.Span);
                    if (read == 0)
                        break;

                    await body.WriteAsync(
                        nbuf.Memory.Slice(0, read),
                        cancellationToken
                    );

                    videoCount -= read;
                    count -= read;
                }

                offset = 0;
            }
            else
            {
                offset -= video.Length;
            }

            if (count <= 0)
                return;

            audio.Position = offset;

            while (count > 0)
            {
                int read = audio.Read(nbuf.Span);
                if (read == 0)
                    break;

                await body.WriteAsync(
                    nbuf.Memory.Slice(0, read),
                    cancellationToken
                );

                count -= read;
            }
        }
    }
    #endregion
}
