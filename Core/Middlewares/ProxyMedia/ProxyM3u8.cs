using Microsoft.AspNetCore.Http;
using Shared;
using Shared.Models.Proxy;
using Shared.Models.ServerProxy;
using Shared.Services;
using Shared.Services.Pools;
using System;
using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Middlewares;

public partial class ProxyAPI
{
    async public Task ProxyM3u8(HttpContext httpContext, ServerproxyConf init, ProxyLinkModel decryptLink, HttpResponseMessage response, string contentType, CancellationTokenSource ctsHttp)
    {
        using (HttpContent content = response.Content)
        {
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent or HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (response.Content?.Headers?.ContentLength > init.maxlength_m3u)
                {
                    httpContext.Response.ContentType = "text/plain; charset=utf-8";
                    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    httpContext.Response.BodyWriter.Write("bigfile"u8);
                    await httpContext.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                    return;
                }

                int m3u8Length = 0;
                var encoder = Encoding.UTF8.GetEncoder();

                await using (var stream = await content.ReadAsStreamAsync(ctsHttp.Token).ConfigureAwait(false))
                {
                    if (ctsHttp.IsCancellationRequested)
                        return;

                    var writer = httpContext.Response.BodyWriter;

                    #region Меняем ссылки в hls
                    using (var msmHls = PoolInvk.msm.GetStream())
                    {
                        #region Получаем m3u8 в msm
                        using (var nbuf = new BufferPool())
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
                        #endregion

                        if (ctsHttp.IsCancellationRequested)
                            return;

                        msmHls.Position = 0;

                        #region Пишем данные в BodyWriter
                        OwnerTo.Span(msmHls, Encoding.UTF8, spanHls =>
                        {
                            using (var charBuffer = new BufferCharPool(BufferCharPool.sizeTiny))
                            {
                                #region writePipe
                                void writePipe(ReadOnlySpan<char> chars)
                                {
                                    /// UTF-16: 1 char -> 2 bytes
                                    /// Кириллица: 1 char -> 2-4 bytes
                                    int chunkSize = chars.Length > 1360 // возьмем середину 1 char -> 3 bytes
                                        ? 16384
                                        : 4096;

                                    while (!chars.IsEmpty)
                                    {
                                        Span<byte> dest = writer.GetSpan(chunkSize);

                                        encoder.Convert(
                                            chars,
                                            dest,
                                            flush: false,
                                            out int charsUsed,
                                            out int bytesUsed,
                                            out bool completed);

                                        if (bytesUsed > 0)
                                        {
                                            m3u8Length += bytesUsed;
                                            writer.Advance(bytesUsed);
                                        }

                                        if (completed)
                                            break;

                                        chars = chars.Slice(charsUsed);
                                    }
                                }
                                #endregion

                                #region writeUri
                                void writeUri(ReadOnlySpan<char> prefix, ReadOnlySpan<char> uri)
                                {
                                    int size = prefix.Length + uri.Length;
                                    if (size > charBuffer.Span.Length)
                                        charBuffer.Ensure(size);

                                    Span<char> joinUri = charBuffer.Span.Slice(0, size);

                                    prefix.CopyTo(joinUri);
                                    uri.CopyTo(joinUri[prefix.Length..]);

                                    ProxyLink.Encrypt(joinUri, decryptLink, sbWriter: result =>
                                    {
                                        foreach (var chunk in result.GetChunks())
                                            writePipe(chunk.Span);
                                    });
                                }
                                #endregion

                                string proxyhost = CoreInit.Host(httpContext, "/proxy");
                                ReadOnlySpan<char> decrypturl = decryptLink.uri.AsSpan();
                                ReadOnlySpan<char> hlshost = FindHlsHost(decrypturl);
                                ReadOnlySpan<char> hlspatch = FindHlsPath(decrypturl);

                                foreach (var range in spanHls.Split('\n'))
                                {
                                    ReadOnlySpan<char> line = spanHls[range].Trim();

                                    if (line.IsEmpty || (line.Length == 1 && (line[0] is '\r' or '\n' or '\t')))
                                    {
                                        writePipe("\n");
                                        continue;
                                    }

                                    if (TryFindHttpUrl(line, out Range urlRange))
                                    {
                                        #region https?://[^\n\r\"\# ]+
                                        // prefix
                                        writePipe(line[..urlRange.Start]);

                                        // url
                                        ReadOnlySpan<char> urlSpan = line[urlRange];

                                        writePipe(proxyhost);
                                        writePipe("/");

                                        ProxyLink.Encrypt(urlSpan, decryptLink, sbWriter: result =>
                                        {
                                            foreach (var chunk in result.GetChunks())
                                                writePipe(chunk.Span);
                                        });

                                        // suffix
                                        writePipe(line[urlRange.End..]);
                                        writePipe("\n");
                                        #endregion
                                    }
                                    else if (TryFindUriAttribute(line, out urlRange))
                                    {
                                        #region URI="([^\"]+)"
                                        ReadOnlySpan<char> urlSpan = line[urlRange];

                                        // prefix
                                        writePipe(line[..urlRange.Start]);

                                        if (urlSpan.StartsWith("//"))
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri("https:", urlSpan);
                                        }
                                        else if (urlSpan.StartsWith("./"))
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri(hlspatch, urlSpan.Slice(2));
                                        }
                                        else if (urlSpan.StartsWith("/"))
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri(hlspatch, urlSpan.Slice(1));
                                        }
                                        else
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri(hlspatch, urlSpan);
                                        }

                                        // suffix
                                        writePipe(line[urlRange.End..]);
                                        writePipe("\n");
                                        #endregion
                                    }
                                    else
                                    {
                                        if (line.StartsWith("#", StringComparison.Ordinal) ||
                                            line.Contains("\"", StringComparison.Ordinal))
                                        {
                                            writePipe(line);
                                            writePipe("\n");
                                            continue;
                                        }

                                        if (line.StartsWith("//"))
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri("https:", line);
                                        }
                                        else if (line.StartsWith("./"))
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri(hlspatch, line.Slice(2));
                                        }
                                        else if (line.StartsWith("/"))
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri(hlshost, line.Slice(1));
                                        }
                                        else
                                        {
                                            writePipe(proxyhost);
                                            writePipe("/");
                                            writeUri(hlspatch, line);
                                        }

                                        writePipe("\n");
                                    }
                                }
                            }
                        });
                        #endregion
                    }
                    #endregion

                    #region Ошибка
                    if (m3u8Length == 0)
                    {
                        httpContext.Response.ContentType = "text/plain; charset=utf-8";
                        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        httpContext.Response.BodyWriter.Write("m3u8 length empty"u8);
                        await httpContext.Response.BodyWriter.FlushAsync(ctsHttp.Token).ConfigureAwait(false);
                        return;
                    }
                    #endregion

                    #region Headers
                    httpContext.Response.StatusCode = (int)response.StatusCode;

                    httpContext.Response.ContentType = contentType != null && contentType.StartsWith("application/x-mpegurl", StringComparison.OrdinalIgnoreCase)
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
                        if (init.responseContentLength && !CoreInit.ContainsMimeTypes(httpContext.Response.ContentType))
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

    #region Helpers
    static bool TryFindHttpUrl(ReadOnlySpan<char> line, out Range range)
    {
        range = default;

        int idx = line.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        ReadOnlySpan<char> rest = line[idx..];

        if (!(rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return false;

        int end = idx;

        while (end < line.Length)
        {
            char c = line[end];

            if (c == '\n' || c == '\r' || c == '"' || c == '#' || c == ' ')
                break;

            end++;
        }

        range = idx..end;
        return true;
    }

    static bool TryFindUriAttribute(ReadOnlySpan<char> line, out Range range)
    {
        range = default;

        int start = line.IndexOf("URI=\"", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return false;

        int valueStart = start + 5; // длина URI="
        int valueEnd = valueStart;

        while (valueEnd < line.Length && line[valueEnd] != '"')
            valueEnd++;

        if (valueEnd >= line.Length)
            return false;

        range = valueStart..valueEnd;
        return true;
    }

    /// <summary>
    /// Regex.Match(decryptLink.uri, "(https?://[^/]+)/").Groups[1].Value
    /// </summary>
    static ReadOnlySpan<char> FindHlsHost(ReadOnlySpan<char> decrypturl)
    {
        // найдём "://"
        int schemeEnd = decrypturl.IndexOf("://");

        // начало хоста после "://"
        int hostStart = schemeEnd + 3;

        // первый '/' после хоста
        int slashIndex = decrypturl[hostStart..].IndexOf('/');

        // если слеша нет — берём до конца строки
        int hostEnd = slashIndex >= 0 ? hostStart + slashIndex : decrypturl.Length;

        // "http(s)://host[:port]"
        return decrypturl[..hostEnd];
    }

    /// <summary>
    /// Regex.Match(decryptLink.uri, "(https?://[^\n\r]+/)([^/]+)$").Groups[1].Value
    /// </summary>
    static ReadOnlySpan<char> FindHlsPath(ReadOnlySpan<char> decrypturl)
    {
        int lastSlash = decrypturl.LastIndexOf('/');

        // Если строка заканчивается на '/', то "патч" = вся строка
        return (lastSlash == decrypturl.Length - 1)
            ? decrypturl
            : decrypturl[..(lastSlash + 1)]; // до последнего '/', включая его
    }
    #endregion
}
