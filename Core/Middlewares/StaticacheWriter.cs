using Microsoft.AspNetCore.Http;
using Shared.Attributes;
using Shared.Services;
using Shared.Services.Pools;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Core.Middlewares;

public class StaticacheWriter
{
    #region static
    private readonly RequestDelegate _next;

    public StaticacheWriter(RequestDelegate next)
    {
        _next = next;
    }
    #endregion

    async public Task InvokeAsync(HttpContext httpContext)
    {
        var stc = httpContext.Features.Get<StaticacheFeature>();
        if (stc == null)
        {
            await _next(httpContext);
            return;
        }

        using (var buff = new BufferWriterPool<byte>(largePool: true))
        {
            httpContext.Features.Set(buff);
            httpContext.Response.Headers["X-StatiCache-Status"] = "MISS";

            await _next(httpContext);

            if (buff.WrittenCount > 0)
            {
                string cachekey = stc.cachekey;
                var sm = new SemaphorManager($"Staticache:{cachekey}", TimeSpan.FromSeconds(10));

                try
                {
                    bool _acquired = await sm.WaitAsync();
                    if (!_acquired)
                        return;

                    string contentType = httpContext.Response.ContentType.StartsWith("application/json")
                        ? "application/json; charset=utf-8"
                        : "text/html; charset=utf-8";

                    var ex = httpContext.Features.Get<StatiCacheEntry>()?.ex
                        ?? DateTimeOffset.Now.AddMinutes(stc.route.cacheMinutes);

                    if (DateTimeOffset.Now > ex)
                        return;

                    string cachefile = Staticache.getFilePath(cachekey, ex, contentType);

                    await using (var fileStream = new FileStream(cachefile, FileMode.Create, FileAccess.Write, FileShare.None,
                        bufferSize: PoolInvk.bufferSize,
                        options: FileOptions.Asynchronous))
                    {
                        await fileStream.WriteAsync(buff.WrittenMemory);
                    }

                    Staticache.cacheFiles.TryAdd(cachekey, new(ex, contentType, cachefile));
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "CatchId={CatchId}", "id_23a31ad1");
                }
                finally
                {
                    sm.Release();
                }
            }
        }
    }
}
