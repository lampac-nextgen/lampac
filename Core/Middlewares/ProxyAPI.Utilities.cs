using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services;
using Shared.Services.Buckets;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Core.Middlewares;

public partial class ProxyAPI
{
    #region static
    static readonly IReadOnlyDictionary<string, string> defaultNormalizeHeaders = new Dictionary<string, string>()
    {
        ["Accept"] = "*/*",
        ["Accept-Language"] = "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5",
        ["Pragma"] = "no-cache",
        ["Cache-Control"] = "no-cache",
        ["sec-ch-ua"] = Http.SecChUa,
        ["sec-ch-ua-mobile"] = "?0",
        ["sec-ch-ua-platform"] = "\"Windows\"",
        ["DNT"] = "1",
        ["User-Agent"] = Http.UserAgent
    };

    static readonly FrozenSet<string> responseHeaders = new[]
    {
        "accept-encoding",
        "accept-ranges",
        "content-range",
        "content-length",
        "content-type"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    static async Task InvokeProxyApiCreateHttpRequestHandlers(EventProxyApiCreateHttpRequest eventArgs)
    {
        foreach (Func<EventProxyApiCreateHttpRequest, Task> handler in EventListener.ProxyApiCreateHttpRequest.GetInvocationList())
            await handler(eventArgs).ConfigureAwait(false);
    }
    #endregion

    #region CreateProxyHttpRequest
    static HttpRequestMessage CreateProxyHttpRequest(string plugin, HttpContext context, IReadOnlyList<HeadersModel> headers, Uri uri)
    {
        var request = context.Request;

        var requestMethod = HttpMethods.IsGet(request.Method)
            ? HttpMethod.Get
            : HttpMethods.IsPost(request.Method)
                ? HttpMethod.Post
                : new HttpMethod(request.Method);

        var requestMessage = new HttpRequestMessage();

        if (requestMethod == HttpMethod.Post)
        {
            var streamContent = new StreamContent(request.Body);
            requestMessage.Content = streamContent;
        }

        #region Headers
        request.Headers.TryGetValue("range", out StringValues range);

        if (headers == null || headers.Count == 0)
        {
            foreach (var h in defaultNormalizeHeaders)
                requestMessage.Headers.TryAddWithoutValidation(h.Key, h.Value);

            if (range.Count > 0)
                requestMessage.Headers.TryAddWithoutValidation("range", range.ToArray());
        }
        else
        {
            ulong H1 = BucketHeaders.Hash("ProxyAPI", headers);

            if (BucketHeaders.TryGetValue(H1, out var _bucketHeaders))
            {
                foreach (var h in _bucketHeaders)
                {
                    if (requestMessage.Headers.TryAddWithoutValidation(h.name, h.val)) { }
                    else if (requestMessage.Content?.Headers != null)
                    {
                        // Content-Type, Content-Length, Content-Encoding, Content-Disposition
                        requestMessage.Content.Headers.TryAddWithoutValidation(h.name, h.val);
                    }
                }

                if (range.Count > 0)
                    requestMessage.Headers.TryAddWithoutValidation("range", range.ToArray());
            }
            else
            {
                var addHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["accept"] = "*/*",
                    ["accept-language"] = "ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5"
                };

                foreach (var h in headers)
                    addHeaders[h.name] = h.val;

                foreach (var h in Http.defaultFullHeaders)
                    addHeaders[h.Key] = h.Value;

                var normalizeHeaders = Http.NormalizeHeaders(addHeaders);
                BucketHeaders.AddOrUpdate(H1, HeadersModel.Init(normalizeHeaders));

                foreach (var h in normalizeHeaders)
                {
                    if (requestMessage.Headers.TryAddWithoutValidation(h.Key, h.Value)) { }
                    else if (requestMessage.Content?.Headers != null)
                    {
                        // Content-Type, Content-Length, Content-Encoding, Content-Disposition
                        requestMessage.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    }
                }

                if (range.Count > 0)
                    requestMessage.Headers.TryAddWithoutValidation("range", range.ToArray());
            }
        }
        #endregion

        requestMessage.Headers.Host = uri.Authority;
        requestMessage.RequestUri = uri;
        requestMessage.Method = requestMethod;

        //requestMessage.Version = new Version(2, 0);
        //Console.WriteLine(JsonConvert.SerializeObject(requestMessage.Headers, Formatting.Indented));

        return requestMessage;
    }
    #endregion

    #region CopyProxyHttpResponse
    async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage, string uriKeyFileCache, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return;

        var response = context.Response;
        response.StatusCode = (int)responseMessage.StatusCode;

        #region UpdateHeaders
        void UpdateHeaders(HttpHeaders headers)
        {
            if (headers == null)
                return;

            foreach (var header in headers)
            {
                string key = header.Key;

                if (!string.IsNullOrEmpty(key) && responseHeaders.Contains(key))
                {
                    var values = header.Value;
                    using (var e = values.GetEnumerator())
                    {
                        if (!e.MoveNext())
                            continue;

                        var first = e.Current;

                        response.Headers[key] = e.MoveNext()
                            ? string.Join("; ", values)
                            : first;
                    }
                }
            }
        }
        #endregion

        UpdateHeaders(responseMessage.Headers);
        UpdateHeaders(responseMessage.Content?.Headers);

        await using (var responseStream = await responseMessage.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            if (response.Body == null)
                return;

            if (!responseStream.CanRead && !responseStream.CanWrite)
                return;

            if (!response.Body.CanRead && !response.Body.CanWrite)
                return;

            if (!responseStream.CanRead || !response.Body.CanWrite)
                return;

            var buffering = CoreInit.conf.serverproxy?.buffering;

            if (buffering?.enable == true &&
               ((!string.IsNullOrEmpty(buffering.pattern) && Regex.IsMatch(context.Request.Path.Value, buffering.pattern, RegexOptions.IgnoreCase)) ||
               context.Request.Path.Value.EndsWith(".mp4") || context.Request.Path.Value.EndsWith(".mkv") || responseMessage.Content?.Headers?.ContentLength > CoreInit.conf.serverproxy.maxlength_ts))
            {
                #region buffering
                var channel = Channel.CreateBounded<(BufferPool nbuf, int Length)>(new BoundedChannelOptions(capacity: buffering.bytes / PoolInvk.bufferSize)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = true,
                    SingleReader = true
                });

                var readTask = Task.Factory.StartNew(async () =>
                {
                    try
                    {
                        while (!context.RequestAborted.IsCancellationRequested)
                        {
                            var nbuf = new BufferPool();

                            try
                            {
                                int bytesRead = await responseStream.ReadAsync(nbuf.Memory, context.RequestAborted);

                                if (bytesRead == 0 || context.RequestAborted.IsCancellationRequested)
                                {
                                    nbuf.Dispose();
                                    break;
                                }

                                await channel.Writer.WriteAsync((nbuf, bytesRead), context.RequestAborted);
                            }
                            catch
                            {
                                nbuf.Dispose();
                                break;
                            }
                        }
                    }
                    finally
                    {
                        channel.Writer.Complete();
                    }
                },
                    default, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default
                ).Unwrap();

                var writeTask = Task.Factory.StartNew(async () =>
                {
                    bool IsCancellation = false;

                    await foreach (var (nbuf, length) in channel.Reader.ReadAllAsync())
                    {
                        try
                        {
                            if (!IsCancellation)
                                IsCancellation = context.RequestAborted.IsCancellationRequested;

                            if (!IsCancellation)
                                await response.Body.WriteAsync(nbuf.Memory.Slice(0, length), context.RequestAborted);
                        }
                        catch
                        {
                            IsCancellation = true;
                        }
                        finally
                        {
                            nbuf.Dispose();
                        }
                    }
                },
                    default, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default
                ).Unwrap();

                await Task.WhenAll(readTask, writeTask).ConfigureAwait(false);
                #endregion
            }
            else
            {
                try
                {
                    if (uriKeyFileCache != null &&
                        responseMessage.Content.Headers.ContentLength.HasValue &&
                        CoreInit.conf.serverproxy.maxlength_ts >= responseMessage.Content.Headers.ContentLength)
                    {
                        #region cache
                        string fileName = Fnv1a.HashName(uriKeyFileCache);
                        string targetFile = fileWatcher.OutFile(fileName);

                        var semaphore = new SemaphorManager(targetFile, ct);

                        try
                        {
                            bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                            if (!_acquired || ct.IsCancellationRequested)
                                return;

                            int cacheLength = 0;

                            using (var nbuf = new BufferPool())
                            {
                                await using (var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0, options: FileOptions.Asynchronous))
                                {
                                    int bytesRead;
                                    var memBuf = nbuf.Memory;

                                    while ((bytesRead = await responseStream.ReadAsync(memBuf, ct).ConfigureAwait(false)) != 0)
                                    {
                                        if (ct.IsCancellationRequested)
                                            break;

                                        cacheLength += bytesRead;
                                        await fileStream.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                                        await response.Body.WriteAsync(memBuf.Slice(0, bytesRead), ct).ConfigureAwait(false);
                                    }
                                }
                            }

                            if (responseMessage.Content.Headers.ContentLength.Value == cacheLength)
                            {
                                fileWatcher.Add(targetFile, cacheLength);
                            }
                            else
                            {
                                File.Delete(targetFile);
                            }
                        }
                        catch
                        {
                            File.Delete(targetFile);
                        }
                        finally
                        {
                            semaphore?.Release();
                        }
                        #endregion
                    }
                    else
                    {
                        #region bypass
                        var ctbypass = responseMessage.Content.Headers.ContentLength.HasValue && CoreInit.conf.serverproxy.maxlength_ts >= responseMessage.Content.Headers.ContentLength
                            ? ct
                            : context.RequestAborted;

                        if (ctbypass.IsCancellationRequested)
                            return;

                        using (var nbuf = new BufferPool())
                        {
                            int bytesRead;
                            var memBuf = nbuf.Memory;

                            while ((bytesRead = await responseStream.ReadAsync(memBuf, ctbypass).ConfigureAwait(false)) > 0)
                            {
                                if (ctbypass.IsCancellationRequested)
                                    break;

                                await response.Body.WriteAsync(memBuf.Slice(0, bytesRead), ctbypass).ConfigureAwait(false);
                            }
                        }
                        #endregion
                    }
                }
                catch (System.OperationCanceledException) { }
                catch (System.Net.Http.HttpIOException) { }
            }
        }
    }
    #endregion
}
