using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Microsoft.Win32.SafeHandles;
using Shared.Attributes;
using Shared.Models.AppConf;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.Pools;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Core.Middlewares;

public class StaticacheWriter
{
    private readonly RequestDelegate _next;

    public StaticacheWriter(RequestDelegate next)
    {
        _next = next;
    }

    async public Task InvokeAsync(HttpContext httpContext)
    {
        var stc = httpContext.Features.Get<StaticacheFeature>();
        if (stc == null)
        {
            await _next(httpContext).ConfigureAwait(false);
        }
        else
        {
            using (var msm = new LazyMsm())
            {
                httpContext.Features.Set(msm);
                httpContext.Response.Headers["X-StatiCache-Status"] = "MISS";

                await _next(httpContext);

                bool isRedirect = httpContext.Response.StatusCode == StatusCodes.Status301MovedPermanently ||
                    httpContext.Response.StatusCode == StatusCodes.Status302Found ||
                    httpContext.Response.StatusCode == StatusCodes.Status307TemporaryRedirect ||
                    httpContext.Response.StatusCode == StatusCodes.Status308PermanentRedirect;

                if (!isRedirect && !msm.IsEmpty)
                {
                    var cacheEntry = httpContext.Features.Get<StatiCacheEntry>();

                    #region Дожимаем httpContext
                    var ex = cacheEntry?.ex ?? DateTimeOffset.Now.AddMinutes(stc.cacheMinutes);

                    if (httpContext.Response.StatusCode != 200)
                        ex = DateTimeOffset.Now.AddMinutes(1);

                    int contentLength = (int)(httpContext.Response?.ContentLength ?? 0);
                    if (contentLength > 0 && contentLength != msm.Stream.Length)
                        ex = DateTimeOffset.Now.AddMinutes(1);

                    string ext = "bin";
                    bool isMedia = false;
                    string contentType = httpContext.Response.ContentType;

                    if (contentType != null)
                    {
                        if (contentType.StartsWith("text/html"))
                            ext = "html";
                        else if (contentType.StartsWith("application/json"))
                            ext = "json";
                        else if (contentType.StartsWith("application/javascript"))
                            ext = "js";
                        else if (contentType.StartsWith("text/css"))
                            ext = "css";
                        else if (contentType.StartsWith("image/svg+xml"))
                            ext = "svg";
                        else if (contentType.StartsWith("image/png"))
                        {
                            ext = "png";
                            isMedia = true;
                        }
                        else if (contentType.StartsWith("image/jpeg"))
                        {
                            ext = "jpg";
                            isMedia = true;
                        }
                        else if (contentType.StartsWith("image/webp"))
                        {
                            ext = "webp";
                            isMedia = true;
                        }
                    }

                    if (isMedia && contentLength == 0)
                    {
                        contentLength = (int)msm.Stream.Length;
                        httpContext.Response.ContentLength = contentLength;
                    }

                    if (contentLength > 0)
                        httpContext.Response.Headers[HeaderNames.CacheControl] = "public,max-age=86400,immutable";
                    #endregion

                    #region Сбрасываем поток клиенту
                    msm.Stream.Position = 0;

                    if (contentLength > 0)
                    {
                        /// один overhead "write && flush" при наличии content-length
                        await msm.Stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
                    }
                    else
                    {
                        /// что-бы не было два overhead "write > flush", пишем через BodyWriter
                        var bodyWriter = httpContext.Response.BodyWriter;

                        do
                        {
                            int chunkSize = PoolInvk.ChunkSizeBodyWriter((int)(msm.Stream.Length - msm.Stream.Position));
                            Span<byte> destination = bodyWriter.GetSpan(chunkSize);

                            int bytesRead = msm.Stream.Read(destination);
                            if (bytesRead == 0)
                                break;

                            bodyWriter.Advance(bytesRead);
                        }
                        while (msm.Stream.Position < msm.Stream.Length);

                        /// write && flush в один overhead
                        await httpContext.Response.CompleteAsync();
                    }
                    #endregion

                    #region Сохраняем на диск
                    if ((cacheEntry != null && cacheEntry.saveCache == false) || DateTimeOffset.Now > ex)
                        return;

                    string cachekey = stc.cachekey;
                    var sm = new SemaphorManager(cachekey, TimeSpan.FromSeconds(5));

                    try
                    {
                        bool _acquired = await sm.WaitAsync();
                        if (!_acquired)
                            return;

                        long exTicks = ex.ToUnixTimeMilliseconds();
                        string cachefile = Staticache.GetFilePath(cachekey, exTicks, contentLength, ext);

                        using (SafeFileHandle handle = File.OpenHandle(cachefile,
                            FileMode.Create, FileAccess.Write, FileShare.None,
                            FileOptions.Asynchronous | FileOptions.SequentialScan,
                            preallocationSize: msm.Stream.Length
                        ))
                        {
                            ReadOnlySequence<byte> sequence = msm.Stream.GetReadOnlySequence();

                            if (sequence.IsSingleSegment)
                            {
                                await RandomAccess.WriteAsync(handle, sequence.First, fileOffset: 0);
                            }
                            else
                            {
                                List<ReadOnlyMemory<byte>> buffers = new();

                                foreach (ReadOnlyMemory<byte> segment in sequence)
                                {
                                    if (!segment.IsEmpty)
                                        buffers.Add(segment);
                                }

                                await RandomAccess.WriteAsync(handle, buffers, fileOffset: 0);
                            }
                        }

                        Staticache.cacheFiles[cachekey] = new StaticacheCacheModel(exTicks, ext, (short)httpContext.Response.StatusCode, contentLength);
                    }
                    catch (Exception exception)
                    {
                        Serilog.Log.Error(exception, "CatchId={CatchId}", "id_23a31ad1");
                    }
                    finally
                    {
                        sm.Release();
                    }
                    #endregion
                }
            }
        }
    }
}
