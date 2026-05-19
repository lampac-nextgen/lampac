using Microsoft.AspNetCore.Http;
using Microsoft.IO;
using Microsoft.Net.Http.Headers;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Proxy;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares;

partial class ProxyImgRegex
{
    [GeneratedRegex("/proxyimg:([0-9]+):([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Rsize();
}

public class ProxyImg
{
    #region static
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ProxyImg>();

    static readonly ConcurrentDictionary<string, DateTime> errorDownloads = new();
    static readonly ConcurrentDictionary<string, ProxyLinkModel> decryptLinks = new();

    static Timer errorDownloadsCleanupTimer;
    static Timer decryptLinksCleanupTimer;

    static CacheFileWatcher fileWatcher;

    public static int Stat_ContCacheFiles
        => fileWatcher.FilesCount;

    public static void Initialization()
    {
        CacheFileWatcher.Configure("img", CoreInit.conf.serverproxy.image.cache_time);
        fileWatcher = new CacheFileWatcher("img");

        errorDownloadsCleanupTimer = new Timer(_ =>
        {
            DateTime now = DateTime.UtcNow;
            foreach (var pair in errorDownloads)
            {
                if (pair.Value <= now)
                    errorDownloads.TryRemove(pair.Key, out DateTime _);
            }
        }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(20));

        decryptLinksCleanupTimer = new Timer(_ =>
        {
            decryptLinks.Clear();
        }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public ProxyImg(RequestDelegate next) { }
    #endregion

    async public Task InvokeAsync(HttpContext httpContext)
    {
        var init = CoreInit.conf.serverproxy.image;
        if (!init.enable)
        {
            httpContext.Response.StatusCode = 404;
            return;
        }

        #region decrypt link
        bool cacheimg = init.cache;
        var requestInfo = httpContext.Features.Get<RequestModel>();
        ReadOnlySpan<char> aespath = ExtractEncryptedPath(httpContext.Request.Path.Value);

        ProxyLinkModel decryptLink = null;
        if (CoreInit.conf.serverproxy.verifyip || CoreInit.conf.lowMemoryMode)
        {
            decryptLink = ProxyLink.Decrypt(aespath, requestInfo.IP);
        }
        else
        {
            string requestPathValue = httpContext.Request.Path.Value;
            if (!decryptLinks.TryGetValue(requestPathValue, out decryptLink))
            {
                decryptLink = ProxyLink.Decrypt(aespath, requestInfo.IP);
                if (decryptLink != null)
                    decryptLinks.TryAdd(requestPathValue, decryptLink);
            }
        }

        string href = decryptLink?.uri;

        if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("http"))
        {
            httpContext.Response.StatusCode = 404;
            return;
        }
        #endregion

        try
        {
            using (var ctsHttp = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted))
            {
                ctsHttp.CancelAfter(TimeSpan.FromSeconds(10));

                httpContext.Response.Headers[HeaderNames.CacheControl] = "public,max-age=86400,immutable"; // 1 day
                if (CoreInit.conf.serverproxy.showOrigUri)
                    httpContext.Response.Headers["PX-Orig"] = href;

                #region width / height
                int width = 0;
                int height = 0;

                if (httpContext.Request.Path.Value.StartsWith("/proxyimg:", StringComparison.Ordinal))
                {
                    if (!cacheimg)
                        cacheimg = init.cache_rsize;

                    var gimg = ProxyImgRegex.Rsize().Match(httpContext.Request.Path.Value).Groups;
                    if (!int.TryParse(gimg[1].Value, out width) || !int.TryParse(gimg[2].Value, out height))
                    {
                        httpContext.Response.ContentType = "text/plain; charset=utf-8";
                        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        httpContext.Response.BodyWriter.Write("rsize method error"u8);
                        await httpContext.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }
                }
                #endregion

                string md5key = null;
                string outFile = null;

                if (cacheimg)
                {
                    md5key = CrypTo.md5Builder(writer =>
                    {
                        writer.Append(href);
                        writer.Append(':');
                        writer.Append(width);
                        writer.Append(':');
                        writer.Append(height);
                    });

                    if (EventListener.ProxyImgMd5key != null)
                    {
                        var em = new EventProxyImgMd5key(httpContext, requestInfo, decryptLink, href, width, height);

                        foreach (Func<EventProxyImgMd5key, string> handler in EventListener.ProxyImgMd5key.GetInvocationList())
                        {
                            string newKey = handler(em);
                            if (newKey != null)
                            {
                                md5key = CrypTo.md5(newKey);
                                break;
                            }
                        }
                    }

                    outFile = fileWatcher.OutFile(md5key);
                }

                string url_reserve = null;
                int fallbackSeparator = href.IndexOf(" or ", StringComparison.Ordinal);
                if (fallbackSeparator >= 0)
                {
                    url_reserve = href.Substring(fallbackSeparator + 4);
                    href = href.Substring(0, fallbackSeparator);
                }

                string contentType = href.Contains(".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : href.Contains(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/jpeg";

                if (width > 0 || height > 0)
                    contentType = href.Contains(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

                #region cacheFiles
                if (cacheimg && fileWatcher.TryGetValue(md5key, out var _fileCache))
                {
                    httpContext.Response.Headers["X-Cache-Status"] = "HIT";
                    httpContext.Response.ContentType = contentType;

                    if (CoreInit.conf.serverproxy.responseContentLength && _fileCache.Length > 0)
                        httpContext.Response.ContentLength = _fileCache.Length;

                    try
                    {
                        await httpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }
                    catch (System.Exception ex)
                    {
                        fileWatcher.Remove(md5key);
                        Log.Error(ex, "CatchId={CatchId}", "id_7ong4hmg");
                    }
                }
                #endregion

                SemaphorManager semaphore = null;

                try
                {
                    if (errorDownloads.ContainsKey(href))
                    {
                        httpContext.Response.Redirect(href);
                        return;
                    }

                    #region semaphore
                    if (cacheimg)
                    {
                        semaphore = new SemaphorManager(outFile, ctsHttp.Token);
                        bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                        if (!_acquired)
                        {
                            httpContext.Response.Redirect(href);
                            return;
                        }
                    }
                    #endregion

                    if (ctsHttp.IsCancellationRequested)
                        return;

                    #region cacheFiles
                    if (cacheimg && fileWatcher.TryGetValue(md5key, out _fileCache))
                    {
                        httpContext.Response.Headers["X-Cache-Status"] = "HIT";
                        httpContext.Response.ContentType = contentType;

                        if (CoreInit.conf.serverproxy.responseContentLength && _fileCache.Length > 0)
                            httpContext.Response.ContentLength = _fileCache.Length;

                        semaphore?.Release();
                        await httpContext.Response.SendFileAsync(_fileCache.FullPath, ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }
                    #endregion

                    httpContext.Response.Headers["X-Cache-Status"] = cacheimg ? "MISS" : "bypass";

                    #region proxyManager
                    ProxyManager proxyManager = null;

                    if (decryptLink?.plugin == "posterapi" && CoreInit.conf.posterApi.useproxy)
                        proxyManager = new ProxyManager("posterapi", CoreInit.conf.posterApi);

                    if (proxyManager == null && init.useproxy)
                        proxyManager = new ProxyManager("proxyimg", init);

                    WebProxy proxy = proxyManager?.Get();
                    #endregion

                    if (width == 0 && height == 0)
                    {
                        #region bypass
                    bypass_reset:

                        var client = FriendlyHttp.MessageClient(
                            "proxyimg",
                            Http.Handler(href, proxy),
                            out bool disposeHttpClient
                        );

                        var req = new HttpRequestMessage(HttpMethod.Get, href)
                        {
                            Version = HttpVersion.Version11
                        };

                        bool useDefaultHeaders = ShouldUseDefaultHeaders(decryptLink?.headers);
                        string prefixCacheHeader = decryptLink.plugin != null ? $"ProxyImg:{decryptLink.plugin}:{useDefaultHeaders}" : null;
                        Http.DefaultRequestHeaders(href, req, null, null, decryptLink?.headers, useDefaultHeaders: useDefaultHeaders, prefixCacheHeader: prefixCacheHeader);

                        try
                        {
                            using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctsHttp.Token).ConfigureAwait(false))
                            {
                                if (response.StatusCode != HttpStatusCode.OK)
                                {
                                    if (url_reserve != null)
                                    {
                                        href = url_reserve;
                                        url_reserve = null;
                                        goto bypass_reset;
                                    }

                                    if (cacheimg)
                                        MarkDownloadError(href);

                                    proxyManager?.Refresh();
                                    httpContext.Response.Redirect(href);
                                    return;
                                }

                                httpContext.Response.StatusCode = (int)response.StatusCode;

                                if (response.Content.Headers.TryGetValues("Content-Type", out var contype))
                                    httpContext.Response.ContentType = contype?.FirstOrDefault() ?? contentType;
                                else
                                    httpContext.Response.ContentType = contentType;

                                if (CoreInit.conf.serverproxy.responseContentLength && response.Content?.Headers?.ContentLength > 0)
                                {
                                    if (!CoreInit.ContainsMimeTypes(httpContext.Response.ContentType))
                                        httpContext.Response.ContentLength = response.Content.Headers.ContentLength.Value;
                                }

                                await using (var responseStream = await response.Content.ReadAsStreamAsync(ctsHttp.Token).ConfigureAwait(false))
                                {
                                    using (var nbuf = new BufferPool())
                                    {
                                        int bytesRead;
                                        var memBuf = nbuf.Memory;

                                        if (cacheimg)
                                        {
                                            try
                                            {
                                                int cacheLength = 0;
                                                bool isFullyRead = false;
                                                fileWatcher.EnsureDirectory(md5key);

                                                await using (var cacheStream = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: PoolInvk.bufferSize, options: FileOptions.Asynchronous))
                                                {
                                                    while (true)
                                                    {
                                                        bytesRead = await responseStream.ReadAsync(memBuf, ctsHttp.Token).ConfigureAwait(false);
                                                        if (bytesRead <= 0)
                                                        {
                                                            isFullyRead = true;
                                                            break;
                                                        }

                                                        cacheLength += bytesRead;
                                                        if (ctsHttp.IsCancellationRequested)
                                                            break;

                                                        await cacheStream.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                                                        await httpContext.Response.Body.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                                                    }
                                                }

                                                if (isFullyRead)
                                                {
                                                    if (response.Content.Headers.ContentLength.HasValue)
                                                    {
                                                        if (response.Content.Headers.ContentLength.Value == cacheLength)
                                                        {
                                                            fileWatcher.Add(md5key, cacheLength);
                                                        }
                                                        else
                                                        {
                                                            File.Delete(outFile);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        fileWatcher.Add(md5key, cacheLength);
                                                    }
                                                }
                                            }
                                            catch
                                            {
                                                fileWatcher.Remove(md5key);
                                            }
                                        }
                                        else
                                        {
                                            while ((bytesRead = await responseStream.ReadAsync(memBuf, ctsHttp.Token).ConfigureAwait(false)) > 0)
                                            {
                                                if (ctsHttp.IsCancellationRequested)
                                                    break;

                                                await httpContext.Response.Body.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            if (disposeHttpClient)
                                client.Dispose();
                        }
                        #endregion
                    }
                    else
                    {
                        #region rsize
                        httpContext.Response.ContentType = contentType;

                    rsize_reset:

                        using (var inArray = PoolInvk.msm.GetStream())
                        {
                            var result = await Download(inArray, href, ctsHttp.Token, decryptLink.plugin, decryptLink.headers, proxy).ConfigureAwait(false);

                            if (!result.success)
                            {
                                if (url_reserve != null)
                                {
                                    href = url_reserve;
                                    url_reserve = null;
                                    goto rsize_reset;
                                }

                                if (cacheimg)
                                    MarkDownloadError(href);

                                proxyManager?.Refresh();
                                httpContext.Response.Redirect(href);
                                return;
                            }

                            if (ctsHttp.IsCancellationRequested)
                                return;

                            using (var outArray = PoolInvk.msm.GetStream())
                            {
                                bool successConvert = false;

                                if ((result.contentType ?? contentType) is "image/png" or "image/webp" or "image/jpeg")
                                {
                                    if (CoreInit.conf.imagelibrary == "NetVips")
                                    {
                                        successConvert = NetVipsImage(href, inArray, outArray, width, height);
                                    }
                                    else if (CoreInit.conf.imagelibrary == "ImageMagick" && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                                    {
                                        successConvert = await ImageMagick(inArray, outArray, width, height, cacheimg ? outFile : null).ConfigureAwait(false);

                                        if (cacheimg)
                                        {
                                            if (successConvert)
                                            {
                                                inArray.Dispose();
                                                outArray.Dispose();

                                                using (var handle = File.OpenHandle(outFile))
                                                    fileWatcher.Add(md5key, (int)RandomAccess.GetLength(handle));

                                                semaphore?.Release();
                                                await httpContext.Response.SendFileAsync(outFile, ctsHttp.Token).ConfigureAwait(false);
                                                return;
                                            }

                                            proxyManager?.Refresh();
                                            httpContext.Response.Redirect(href);
                                            return;
                                        }
                                    }
                                }

                                if (successConvert)
                                    proxyManager?.Success();

                                var resultArray = successConvert ? outArray : inArray;

                                if (CoreInit.conf.serverproxy.responseContentLength)
                                    httpContext.Response.ContentLength = resultArray.Length;

                                try
                                {
                                    using (var byteBuf = new BufferBytePool((int)resultArray.Length))
                                    {
                                        resultArray.Position = 0;
                                        var memBuf = byteBuf.Memory;
                                        int bytesRead = resultArray.Read(memBuf.Span);

                                        await httpContext.Response.Body.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "CatchId={CatchId}", "id_wwjuvpiz");
                                }
                                finally
                                {
                                    if (cacheimg)
                                        await fileWatcher.TrySave(md5key, resultArray).ConfigureAwait(false);
                                }
                            }
                        }
                        #endregion
                    }
                }
                finally
                {
                    semaphore?.Release();
                }
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_4qyrn7xp");
        }
    }


    #region Download
    async Task<(bool success, string contentType)> Download(RecyclableMemoryStream ms, string url, CancellationToken cancellationToken, string plugin, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null)
    {
        var client = FriendlyHttp.MessageClient(
            "base",
            Http.Handler(url, proxy),
            out bool disposeHttpClient
        );

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = HttpVersion.Version11
            };

            bool useDefaultHeaders = ShouldUseDefaultHeaders(headers);
            string prefixCacheHeader = plugin != null ? $"ProxyImg:{plugin}:{useDefaultHeaders}" : null;
            Http.DefaultRequestHeaders(url, req, null, null, headers, useDefaultHeaders: useDefaultHeaders, prefixCacheHeader: prefixCacheHeader);

            using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode != HttpStatusCode.OK || cancellationToken.IsCancellationRequested)
                    return default;

                using (HttpContent content = response.Content)
                {
                    await using (var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    {
                        using (var byteBuf = new BufferPool())
                        {
                            int bytesRead;
                            var memBuf = byteBuf.Memory;

                            while ((bytesRead = await stream.ReadAsync(memBuf, cancellationToken).ConfigureAwait(false)) > 0)
                                ms.Write(memBuf.Span.Slice(0, bytesRead));
                        }
                    }

                    if (ms.Length == 0 || 1000 > ms.Length)
                        return default;

                    if (content.Headers != null)
                    {
                        if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength != ms.Length)
                            return default;

                        response.Content.Headers.TryGetValues("Content-Type", out var _contentType);

                        ms.Position = 0;
                        return (true, ImageContentType(_contentType?.FirstOrDefault()));
                    }

                    ms.Position = 0;
                    return (true, null);
                }
            }
        }
        catch
        {
            return default;
        }
        finally
        {
            if (disposeHttpClient)
                client.Dispose();
        }
    }
    #endregion

    #region NetVipsImage
    static bool _initNetVips = false;

    private bool NetVipsImage(string href, Stream inArray, Stream outArray, int width, int height)
    {
        if (_initNetVips == false)
        {
            if (CoreInit.conf.serverproxy.image.NetVipsCache == false || CoreInit.conf.lowMemoryMode)
            {
                _initNetVips = true;
                NetVips.Cache.Max = 0;      // 0 операций в кэше
                NetVips.Cache.MaxMem = 0;   // 0 байт памяти под кэш
                NetVips.Cache.MaxFiles = 0; // 0 файлов в файловом кэше
                NetVips.Cache.Trace = false;
            }
        }

        try
        {
            using (var image = NetVips.Image.NewFromStream(inArray, access: NetVips.Enums.Access.Sequential))
            {
                if ((width != 0 && image.Width > width) || (height != 0 && image.Height > height))
                {
                    using (var res = image.ThumbnailImage(width == 0 ? image.Width : width, height == 0 ? image.Height : height, crop: NetVips.Enums.Interesting.None))
                    {
                        if (href.Contains(".png", StringComparison.OrdinalIgnoreCase))
                            res.PngsaveStream(outArray);
                        else
                            res.JpegsaveStream(outArray);

                        if (outArray.Length > 1000)
                            return true;
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_h54cvas0");
        }

        return false;
    }
    #endregion

    #region ImageMagick
    static string imaGikPath = null;

    /// <summary>
    /// apt install -y imagemagick libpng-dev libjpeg-dev libwebp-dev
    /// </summary>
    async static Task<bool> ImageMagick(RecyclableMemoryStream inArray, Stream outArray, int width, int height, string outputFilePath)
    {
        if (imaGikPath == null)
            imaGikPath = File.Exists("/usr/bin/magick") ? "magick" : "convert";

        string inputFilePath = getTempFileName();

        bool outFileIsTemp = false;
        if (outputFilePath == null)
        {
            outFileIsTemp = true;
            outputFilePath = getTempFileName();
        }

        try
        {
            await using (var streamFile = new FileStream(inputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: PoolInvk.bufferSize, options: FileOptions.Asynchronous))
            {
                using (var nbuf = new BufferPool())
                {
                    int bytesRead;
                    var memBuf = nbuf.Memory;

                    inArray.Position = 0;
                    while ((bytesRead = inArray.Read(memBuf.Span)) > 0)
                        await streamFile.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                }
            }

            string argsize = width > 0 && height > 0 ? $"{width}x{height}" : width > 0 ? $"{width}x" : $"x{height}";

            using (Process process = new Process())
            {
                process.StartInfo.FileName = imaGikPath;
                process.StartInfo.Arguments = $"\"{inputFilePath}\" -resize {argsize} \"{outputFilePath}\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    return false;
            }

            if (outFileIsTemp)
            {
                await using (var streamFile = new FileStream(outputFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: PoolInvk.bufferSize, options: FileOptions.Asynchronous))
                    await streamFile.CopyToAsync(outArray);
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(inputFilePath))
                    File.Delete(inputFilePath);

                if (outFileIsTemp && File.Exists(outputFilePath))
                    File.Delete(outputFilePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_hdpzbqp0");
            }
        }
    }


    static bool? shm = null;

    static string getTempFileName()
    {
        if (shm == null)
            shm = Directory.Exists("/dev/shm");

        if (shm == true)
            return $"/dev/shm/{Path.GetRandomFileName()}";

        return Path.GetTempFileName();
    }
    #endregion


    #region Utilities
    static bool ShouldUseDefaultHeaders(IReadOnlyList<HeadersModel> headers)
    {
        if (headers == null || headers.Count == 0)
            return true;

        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (header?.name != null && header.name.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    static ReadOnlySpan<char> ExtractEncryptedPath(ReadOnlySpan<char> path)
    {
        int separatorIndex = path.Slice(1).IndexOf('/');
        if (separatorIndex < 0)
            return ReadOnlySpan<char>.Empty;

        return path.Slice(separatorIndex + 2);
    }

    static void MarkDownloadError(string href)
    {
        errorDownloads[href] = DateTime.UtcNow.AddMinutes(1);
    }

    static string ImageContentType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return null;

        ReadOnlySpan<char> s = contentType.AsSpan();

        if (s.StartsWith("image/jpeg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";

        if (s.StartsWith("image/png", StringComparison.OrdinalIgnoreCase))
            return "image/png";

        if (s.StartsWith("image/webp", StringComparison.OrdinalIgnoreCase))
            return "image/webp";

        return contentType;
    }
    #endregion
}
