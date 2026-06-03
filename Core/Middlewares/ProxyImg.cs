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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
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
    static Timer errorDownloadsCleanupTimer;

    static CacheFileWatcher fileWatcher;

    static readonly Regex rexRsize = ProxyImgRegex.Rsize();

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
        ProxyLinkModel decryptLink = ProxyLink.Decrypt(aespath, requestInfo.IP);

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

                    var gimg = rexRsize.Match(httpContext.Request.Path.Value).Groups;
                    if (!int.TryParse(gimg[1].Value, out width) || !int.TryParse(gimg[2].Value, out height))
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                        return;
                    }
                }
                #endregion

                #region md5key / outFile
                string fileName = null;
                string outFile = null;

                if (cacheimg)
                {
                    var fnvhash = Fnv1a.Hash(href);

                    if (EventListener.ProxyImgMd5key != null)
                    {
                        var em = new EventProxyImgMd5key(httpContext, requestInfo, decryptLink, href, width, height);

                        foreach (Func<EventProxyImgMd5key, string> handler in EventListener.ProxyImgMd5key.GetInvocationList())
                        {
                            string newKey = handler(em);
                            if (newKey != null)
                            {
                                if (CoreInit.conf.serverproxy.showOrigUri)
                                    httpContext.Response.Headers["PX-Md5key"] = newKey;

                                fnvhash = Fnv1a.Hash(newKey);
                                break;
                            }
                        }
                    }

                    Fnv1a.Append(ref fnvhash, width);
                    Fnv1a.Append(ref fnvhash, height);
                    fileName = Fnv1a.Base64Url(fnvhash);

                    outFile = fileWatcher.OutFile(fileName);
                }
                #endregion

                #region url_reserve
                string url_reserve = null;
                int fallbackSeparator = href.IndexOf(" or ", StringComparison.Ordinal);
                if (fallbackSeparator >= 0)
                {
                    url_reserve = href.Substring(fallbackSeparator + 4);
                    href = href.Substring(0, fallbackSeparator);
                }
                #endregion

                #region contentType
                string contentType = href.Contains(".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : href.Contains(".webp", StringComparison.OrdinalIgnoreCase)
                        ? "image/webp"
                        : "image/jpeg";

                if (width > 0 || height > 0)
                {
                    if (contentType == "image/webp")
                        contentType = "image/jpeg";
                }
                #endregion

                #region cacheFiles
                if (cacheimg && fileWatcher.TryGetValue(fileName, out int _fileLength))
                {
                    httpContext.Response.Headers["X-Cache-Status"] = "HIT";
                    httpContext.Response.ContentType = contentType;

                    if (_fileLength > 0)
                        httpContext.Response.ContentLength = _fileLength;

                    try
                    {
                        await httpContext.Response.SendFileAsync(outFile, ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        fileWatcher.Remove(fileName);
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
                    if (cacheimg && fileWatcher.TryGetValue(fileName, out _fileLength))
                    {
                        httpContext.Response.Headers["X-Cache-Status"] = "HIT";
                        httpContext.Response.ContentType = contentType;

                        if (_fileLength > 0)
                            httpContext.Response.ContentLength = _fileLength;

                        semaphore?.Release();
                        await httpContext.Response.SendFileAsync(outFile, ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }
                    #endregion

                    httpContext.Response.Headers["X-Cache-Status"] = cacheimg ? "MISS" : "bypass";

                    #region proxyManager
                    ProxyManager proxyManager = null;

                    if (decryptLink?.plugin == "posterapi")
                    {
                        if (CoreInit.conf.posterApi.useproxy)
                            proxyManager = new ProxyManager("posterapi", CoreInit.conf.posterApi);
                    }
                    else
                    {
                        if (init.useproxy)
                            proxyManager = new ProxyManager("proxyimg", init);
                    }

                    WebProxy proxy = proxyManager?.Get();
                    #endregion

                    if (width == 0 && height == 0)
                    {
                        #region bypass
                        var client = FriendlyHttp.MessageClient(
                            "proxyimg",
                            Http.HandlerOrNull(href, proxy),
                            out bool disposeHttpClient,
                            findNoRedirectClient: false
                        );

                        using (var req = new HttpRequestMessage(HttpMethod.Get, href)
                        {
                            Version = HttpVersion.Version11
                        })
                        {
                            bool useDefaultHeaders = ShouldUseDefaultHeaders(decryptLink?.headers);

                            Http.DefaultRequestHeaders(href, req, null, null, decryptLink?.headers, useDefaultHeaders: useDefaultHeaders, prefixCacheHeader: decryptLink.plugin);

                            try
                            {
                                bool isFullyRead = false;
                                long contentLength = -1;

                                using (var msm = new LazyMsm())
                                {
                                    using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctsHttp.Token).ConfigureAwait(false))
                                    {
                                        #region url_reserve
                                        if (response.StatusCode != HttpStatusCode.OK)
                                        {
                                            if (url_reserve != null)
                                            {
                                                decryptLink.uri = url_reserve;

                                                httpContext.Response.Redirect(
                                                    ProxyLink.Encrypt(
                                                        url_reserve,
                                                        decryptLink,
                                                        prefix: [CoreInit.Host(httpContext), "/proxyimg/"]
                                                    )
                                                );
                                            }

                                            if (cacheimg)
                                                MarkDownloadError(href);

                                            proxyManager?.Refresh();
                                            httpContext.Response.Redirect(href);
                                            return;
                                        }
                                        #endregion

                                        if (!cacheimg)
                                            proxyManager?.Success();

                                        httpContext.Response.StatusCode = (int)response.StatusCode;

                                        if (response.Content.Headers.ContentLength.HasValue)
                                        {
                                            contentLength = response.Content.Headers.ContentLength.Value;
                                            httpContext.Response.ContentLength = contentLength;
                                        }

                                        if (response.Content.Headers.TryGetValues("Content-Type", out var contype))
                                            httpContext.Response.ContentType = contype?.FirstOrDefault() ?? contentType;
                                        else
                                            httpContext.Response.ContentType = contentType;

                                        await using (var responseStream = await response.Content.ReadAsStreamAsync(ctsHttp.Token).ConfigureAwait(false))
                                        {
                                            using (var nbuf = new BufferPool())
                                            {
                                                int bytesRead;

                                                while (true)
                                                {
                                                    bytesRead = await responseStream.ReadAsync(nbuf.Memory, ctsHttp.Token).ConfigureAwait(false);
                                                    if (bytesRead <= 0)
                                                    {
                                                        isFullyRead = true;
                                                        break;
                                                    }

                                                    if (ctsHttp.IsCancellationRequested)
                                                        break;

                                                    if (cacheimg)
                                                        msm.Stream.Write(nbuf.Span.Slice(0, bytesRead));
                                                    else
                                                        await httpContext.Response.Body.WriteAsync(nbuf.Memory.Slice(0, bytesRead), ctsHttp.Token).ConfigureAwait(false);
                                                }
                                            }
                                        }
                                    }

                                    if (!msm.IsEmpty)
                                    {
                                        if (isFullyRead)
                                            httpContext.Response.ContentLength = msm.Stream.Length;

                                        msm.Stream.Position = 0;
                                        await msm.Stream.CopyToAsync(httpContext.Response.Body, ctsHttp.Token).ConfigureAwait(false);

                                        if (isFullyRead)
                                        {
                                            if (contentLength > 0 && contentLength != msm.Stream.Length)
                                            {
                                                proxyManager?.Refresh();
                                                MarkDownloadError(href);
                                            }
                                            else
                                            {
                                                proxyManager?.Success();
                                                await fileWatcher.TrySave(fileName, msm.Stream).ConfigureAwait(false);
                                            }
                                        }
                                        else
                                        {
                                            proxyManager?.Refresh();
                                            MarkDownloadError(href);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                if (disposeHttpClient)
                                    client.Dispose();
                            }
                        }
                        #endregion
                    }
                    else
                    {
                        #region rsize
                        httpContext.Response.ContentType = contentType;

                        using (var inArray = new LazyMsm())
                        {
                            var result = await Download(inArray, href, ctsHttp.Token, decryptLink.plugin, decryptLink.headers, proxy).ConfigureAwait(false);

                            #region url_reserve
                            if (!result.success)
                            {
                                if (url_reserve != null)
                                {
                                    decryptLink.uri = url_reserve;

                                    httpContext.Response.Redirect(
                                        ProxyLink.Encrypt(
                                            url_reserve,
                                            decryptLink,
                                            prefix: [CoreInit.Host(httpContext), $"/proxyimg:{width}:{height}"]
                                        )
                                    );
                                }

                                if (cacheimg)
                                    MarkDownloadError(href);

                                proxyManager?.Refresh();
                                httpContext.Response.Redirect(href);
                                return;
                            }
                            #endregion

                            if (ctsHttp.IsCancellationRequested)
                                return;

                            using (var outArray = new LazyMsm())
                            {
                                bool successConvert = false;

                                if ((result.contentType ?? contentType) is "image/png" or "image/webp" or "image/jpeg")
                                {
                                    if (CoreInit.conf.imagelibrary == "NetVips")
                                    {
                                        successConvert = NetVipsImage(href, inArray.Stream, outArray.Stream, width, height);
                                    }
                                    else if (CoreInit.conf.imagelibrary == "ImageMagick" && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                                    {
                                        #region ImageMagick
                                        successConvert = await ImageMagick(inArray.Stream, outArray.Stream, width, height, cacheimg ? outFile : null).ConfigureAwait(false);

                                        if (cacheimg)
                                        {
                                            if (successConvert)
                                            {
                                                inArray.Dispose();
                                                outArray.Dispose();

                                                using (var handle = File.OpenHandle(outFile))
                                                    fileWatcher.Add(fileName, (int)RandomAccess.GetLength(handle));

                                                semaphore?.Release();
                                                await httpContext.Response.SendFileAsync(outFile, ctsHttp.Token).ConfigureAwait(false);
                                                return;
                                            }

                                            proxyManager?.Refresh();
                                            httpContext.Response.Redirect(href);
                                            return;
                                        }
                                        #endregion
                                    }
                                }

                                var resultArray = successConvert ? outArray : inArray;
                                if (resultArray.IsEmpty)
                                {
                                    MarkDownloadError(href);
                                    proxyManager?.Refresh();
                                    return;
                                }

                                proxyManager?.Success();
                                httpContext.Response.ContentLength = resultArray.Stream.Length;

                                try
                                {
                                    resultArray.Stream.Position = 0;
                                    await resultArray.Stream.CopyToAsync(httpContext.Response.Body, ctsHttp.Token).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "CatchId={CatchId}", "id_wwjuvpiz");
                                }
                                finally
                                {
                                    if (cacheimg)
                                        await fileWatcher.TrySave(fileName, resultArray.Stream).ConfigureAwait(false);
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
    async Task<(bool success, string contentType)> Download(LazyMsm ms, string url, CancellationToken cancellationToken, string plugin, IReadOnlyList<HeadersModel> headers = null, WebProxy proxy = null)
    {
        var client = FriendlyHttp.MessageClient(
            "base",
            Http.HandlerOrNull(url, proxy),
            out bool disposeHttpClient,
            findNoRedirectClient: false
        );

        try
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Version = HttpVersion.Version11
            })
            {
                bool useDefaultHeaders = ShouldUseDefaultHeaders(headers);
                Http.DefaultRequestHeaders(url, req, null, null, headers, useDefaultHeaders: useDefaultHeaders, prefixCacheHeader: plugin);

                using (HttpResponseMessage response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    if (response.StatusCode != HttpStatusCode.OK || cancellationToken.IsCancellationRequested)
                        return default;

                    HttpContent content = response.Content;

                    await using (var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    {
                        using (var byteBuf = new BufferPool())
                        {
                            int bytesRead;
                            while ((bytesRead = await stream.ReadAsync(byteBuf.Memory, cancellationToken).ConfigureAwait(false)) > 0)
                                ms.Stream.Write(byteBuf.Span.Slice(0, bytesRead));
                        }
                    }

                    if (ms.Stream.Length == 0 || 1000 > ms.Stream.Length)
                        return default;

                    if (content.Headers != null)
                    {
                        if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength != ms.Stream.Length)
                            return default;

                        response.Content.Headers.TryGetValues("Content-Type", out var _contentType);

                        ms.Stream.Position = 0;
                        return (true, ImageContentType(_contentType?.FirstOrDefault()));
                    }

                    ms.Stream.Position = 0;
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

    static bool NetVipsImage(string href, Stream inArray, Stream outArray, int width, int height)
    {
        if (_initNetVips == false)
        {
            _initNetVips = true;
            NetVips.Cache.Max = 0;      // 0 операций в кэше
            NetVips.Cache.MaxMem = 0;   // 0 байт памяти под кэш
            NetVips.Cache.MaxFiles = 0; // 0 файлов в файловом кэше
            NetVips.Cache.Trace = false;
            NetVips.NetVips.Leak = false;
            NetVips.NetVips.Profile = false;
            NetVips.NetVips.Concurrency = 1;
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
        catch (Exception ex)
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
            inArray.Position = 0;

            await using (var streamFile = new FileStream(inputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0, options: FileOptions.Asynchronous))
                await inArray.CopyToAsync(streamFile).ConfigureAwait(false);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ReadOnlySpan<char> ExtractEncryptedPath(ReadOnlySpan<char> path)
    {
        int separatorIndex = path.Slice(1).IndexOf('/');
        if (separatorIndex < 0)
            return ReadOnlySpan<char>.Empty;

        return path.Slice(separatorIndex + 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void MarkDownloadError(string href)
    {
        errorDownloads[href] = DateTime.UtcNow.AddMinutes(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
