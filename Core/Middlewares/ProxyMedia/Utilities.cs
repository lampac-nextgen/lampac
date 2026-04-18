using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.Utilities;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Core.Middlewares
{
    public partial class ProxyAPI
    {
        static readonly HttpClientHandler baseHandler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        static async Task InvokeProxyApiCreateHttpRequestHandlers(EventProxyApiCreateHttpRequest eventArgs)
        {
            foreach (Func<EventProxyApiCreateHttpRequest, Task> handler in EventListener.ProxyApiCreateHttpRequest.GetInvocationList())
                await handler(eventArgs).ConfigureAwait(false);
        }

        #region CreateProxyHttpRequest
        static HttpRequestMessage CreateProxyHttpRequest(string plugin, HttpContext context, List<HeadersModel> headers, Uri uri)
        {
            var request = context.Request;
            var requestMethod = request.Method;

            var requestMessage = new HttpRequestMessage();

            if (HttpMethods.IsPost(requestMethod))
            {
                var streamContent = new StreamContent(request.Body);
                requestMessage.Content = streamContent;
            }

            #region Headers
            request.Headers.TryGetValue("range", out StringValues range);

            if (range.Count == 0 && plugin != null && cacheDefaultRequestHeaders.TryGetValue(plugin, out var _cacheHeaders))
            {
                foreach (var h in _cacheHeaders)
                {
                    if (!requestMessage.Headers.TryAddWithoutValidation(h.Key, h.Value))
                    {
                        if (requestMessage.Content?.Headers != null)
                            requestMessage.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    }
                }
            }
            else
            {
                var addHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["accept"] = ["*/*"],
                    ["accept-language"] = ["ru-RU,ru;q=0.9,uk-UA;q=0.8,uk;q=0.7,en-US;q=0.6,en;q=0.5"]
                };

                if (headers != null && headers.Count > 0)
                {
                    foreach (var h in headers)
                        addHeaders[h.name] = [h.val];
                }

                if (range.Count > 0)
                    addHeaders["range"] = range.ToArray();

                foreach (var h in Http.defaultFullHeaders)
                    addHeaders[h.Key] = [h.Value];

                var normalizeHeaders = Http.NormalizeHeaders(addHeaders);

                if (range.Count == 0 && plugin != null)
                    cacheDefaultRequestHeaders.AddOrUpdate(plugin, normalizeHeaders, (k, v) => normalizeHeaders);

                foreach (var h in normalizeHeaders)
                {
                    if (!requestMessage.Headers.TryAddWithoutValidation(h.Key, h.Value))
                    {
                        if (requestMessage.Content?.Headers != null)
                            requestMessage.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    }
                }
            }
            #endregion

            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = new HttpMethod(request.Method);

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
                foreach (var header in headers)
                {
                    var key = header.Key.AsSpan();

                    if (key.Equals("accept-encoding", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("accept-ranges", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("content-range", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                    {
                        response.Headers[header.Key] = header.Value.ToArray();
                    }
                }
            }
            #endregion

            UpdateHeaders(responseMessage.Headers);
            UpdateHeaders(responseMessage.Content.Headers);

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
                            string md5key = CrypTo.md5(uriKeyFileCache);
                            string targetFile = fileWatcher.OutFile(md5key);

                            var semaphore = new SemaphorManager(targetFile, ct);

                            try
                            {
                                bool _acquired = await semaphore.WaitAsync().ConfigureAwait(false);
                                if (!_acquired || ct.IsCancellationRequested)
                                    return;

                                int cacheLength = 0;

                                using (var nbuf = new BufferPool())
                                {
                                    await using (var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, PoolInvk.bufferSize, options: FileOptions.Asynchronous))
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
                                    fileWatcher.Add(md5key, cacheLength);
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
}
