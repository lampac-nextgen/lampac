using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Proxy;
using Shared.Models.ServerProxy;
using Shared.Services;
using Shared.Services.Pools;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares
{
    public partial class ProxyAPI
    {
        static readonly Regex rexM3u = new Regex("(https?://[^\n\r\"\\# ]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex rexUri = new Regex("(URI=\")([^\"]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        async public Task ProxyM3u8(HttpContext httpContext, ServerproxyConf init, ProxyLinkModel decryptLink, HttpResponseMessage response, string contentType, CancellationTokenSource ctsHttp)
        {
            using (HttpContent content = response.Content)
            {
                if (response.StatusCode == HttpStatusCode.OK ||
                    response.StatusCode == HttpStatusCode.PartialContent ||
                    response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    if (response.Content?.Headers?.ContentLength > init.maxlength_m3u)
                    {
                        httpContext.Response.StatusCode = 503;
                        httpContext.Response.ContentType = "text/plain";
                        await httpContext.Response.WriteAsync("bigfile", ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }

                    string proxyhost = $"{CoreInit.Host(httpContext)}/proxy";
                    string hlshost = Regex.Match(decryptLink.uri, "(https?://[^/]+)/").Groups[1].Value;
                    string hlspatch = Regex.Match(decryptLink.uri, "(https?://[^\n\r]+/)([^/]+)$").Groups[1].Value;
                    if (string.IsNullOrEmpty(hlspatch) && decryptLink.uri.EndsWith("/"))
                        hlspatch = decryptLink.uri;

                    int m3u8Length = 0;
                    var encoder = Encoding.UTF8.GetEncoder();

                    await using (var stream = await content.ReadAsStreamAsync(ctsHttp.Token).ConfigureAwait(false))
                    {
                        if (ctsHttp.IsCancellationRequested)
                            return;

                        var writer = httpContext.Response.BodyWriter;
                        writer.GetSpan(256 * 1024); // прогрев на одинаковые блоки

                        #region Меняем ссылки в hls
                        using (var msmHls = PoolInvk.msm.GetStream())
                        {
                            using (var nbuf = new BufferBytePool(BufferBytePool.sizeSmall))
                            {
                                int bytesRead;
                                var memBuf = nbuf.Memory;

                                while ((bytesRead = await stream.ReadAsync(memBuf, ctsHttp.Token).ConfigureAwait(false)) > 0)
                                {
                                    if (ctsHttp.IsCancellationRequested)
                                        break;

                                    msmHls.Write(memBuf.Span.Slice(0, bytesRead));
                                }
                            }

                            msmHls.Position = 0;
                            OwnerTo.Span(msmHls, Encoding.UTF8, spanHls =>
                            {
                                #region writePipe
                                void writePipe(ReadOnlySpan<char> chars)
                                {
                                    if (chars.IsEmpty)
                                        return;

                                    while (true)
                                    {
                                        Span<byte> dest = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(chars.Length));

                                        encoder.Convert(
                                            chars,
                                            dest,
                                            flush: false,
                                            out int charsUsed,
                                            out int bytesUsed,
                                            out bool completed);

                                        m3u8Length += bytesUsed;

                                        writer.Advance(bytesUsed);

                                        if (completed)
                                            break;

                                        chars = chars.Slice(charsUsed);
                                    }
                                }
                                #endregion

                                foreach (var range in spanHls.Split('\n'))
                                {
                                    ReadOnlySpan<char> line = spanHls[range].Trim();

                                    if (line.IsEmpty || (line.Length == 1 && (line[0] is '\r' or '\n' or '\t')))
                                    {
                                        writePipe("\n");
                                        continue;
                                    }

                                    if (rexM3u.IsMatch(line))
                                    {
                                        writePipe(rexM3u.Replace(line.ToString(), m => $"{proxyhost}/{ProxyLink.Encrypt(m.Groups[1].Value, decryptLink)}"));
                                        writePipe("\n");
                                    }
                                    else if (rexUri.IsMatch(line))
                                    {
                                        writePipe(rexUri.Replace(line.ToString(), m =>
                                        {
                                            var uriSpan = m.Groups[2].ValueSpan;

                                            if (uriSpan.Contains("\"", StringComparison.Ordinal) || uriSpan.StartsWith("http"))
                                                return m.Groups[0].Value;

                                            string uri;

                                            if (uriSpan.StartsWith("//"))
                                            {
                                                uri = string.Concat("https:", uriSpan);
                                            }
                                            else if (uriSpan.StartsWith("/"))
                                            {
                                                uri = string.Concat(hlshost, uriSpan);
                                            }
                                            else if (uriSpan.StartsWith("./"))
                                            {
                                                uri = string.Concat(hlspatch, uriSpan.Slice(2));
                                            }
                                            else
                                            {
                                                uri = string.Concat(hlspatch, uriSpan);
                                            }

                                            return $"{m.Groups[1].Value}{proxyhost}/{ProxyLink.Encrypt(uri, decryptLink)}";
                                        }));

                                        writePipe("\n");
                                    }
                                    else
                                    {
                                        if (line.Contains("#", StringComparison.Ordinal) ||
                                            line.Contains("\"", StringComparison.Ordinal))
                                        {
                                            writePipe(line);
                                            writePipe("\n");
                                            continue;
                                        }

                                        string uri;

                                        if (line.StartsWith("//"))
                                        {
                                            uri = string.Concat("https:", line);
                                        }
                                        else if (line.StartsWith("/"))
                                        {
                                            uri = string.Concat(hlshost, line);
                                        }
                                        else if (line.StartsWith("./"))
                                        {
                                            uri = string.Concat(hlspatch, line.Slice(2));
                                        }
                                        else
                                        {
                                            uri = string.Concat(hlspatch, line);
                                        }

                                        writePipe(proxyhost);
                                        writePipe("/");
                                        writePipe(ProxyLink.Encrypt(uri, decryptLink));
                                        writePipe("\n");
                                    }
                                }
                            });
                        }
                        #endregion

                        #region Ошибка
                        if (m3u8Length == 0)
                        {
                            httpContext.Response.StatusCode = 503;
                            await httpContext.Response.WriteAsync("error m3u8", ctsHttp.Token).ConfigureAwait(false);
                            return;
                        }
                        #endregion

                        #region Headers
                        httpContext.Response.StatusCode = (int)response.StatusCode;

                        httpContext.Response.ContentType = contentType != null && contentType.Equals("application/x-mpegurl", StringComparison.OrdinalIgnoreCase)
                            ? "application/x-mpegurl"
                            : "application/vnd.apple.mpegurl";

                        if (httpContext.Response.StatusCode is 206 or 416)
                        {
                            httpContext.Response.Headers["accept-ranges"] = "bytes";

                            if (httpContext.Response.StatusCode == 206)
                                httpContext.Response.Headers["content-range"] = $"bytes 0-{m3u8Length - 1}/{m3u8Length}";

                            if (httpContext.Response.StatusCode == 416)
                                httpContext.Response.Headers["content-range"] = $"bytes */{m3u8Length}";
                        }
                        else
                        {
                            if (init.responseContentLength && !CoreInit.CompressionMimeTypes.Contains(httpContext.Response.ContentType))
                                httpContext.Response.ContentLength = m3u8Length;
                        }
                        #endregion

                        #region границы чанков/суррогаты
                        Span<byte> tail = writer.GetSpan(128);

                        encoder.Convert(
                            ReadOnlySpan<char>.Empty,
                            tail,
                            flush: true,
                            out int _,
                            out int bytesUsed,
                            out bool _);

                        writer.Advance(bytesUsed);
                        #endregion

                        await writer.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    // проксируем ошибку
                    await CopyProxyHttpResponse(httpContext, response, null, ctsHttp.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
