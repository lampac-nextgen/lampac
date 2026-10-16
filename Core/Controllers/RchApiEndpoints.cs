using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.Pools;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Core.Endpoints;

public static class RchApiEndpoints
{
    readonly record struct RchConnectedResponse(int apkVersion, string rchtype);

    const long maxRequestSize = 10 * 1024 * 1024;
    static readonly RchClientInfo emptyRchClientInfo = new RchClientInfo();
    static readonly BaseSettings rchBaseSettings = new BaseSettings() { rhub = true };

    public static void MapRchApi(this IEndpointRouteBuilder endpoints)
    {
        var rchGroup = endpoints.MapGroup("rch");

        rchGroup.MapPost("result", WriteResult)
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(maxRequestSize));

        rchGroup.MapPost("gzresult", WriteZipResult)
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(maxRequestSize));

        rchGroup.MapGet("check/connected", СheckСonnected)
            .AllowAnonymous();
    }

    static bool IsExpectedClient(HttpContext context, RchClient.rchIdEntry rchHub)
    {
        if (string.IsNullOrEmpty(rchHub.expectedIp))
            return true;

        var requestInfo = context.Features.Get<RequestModel>();
        return requestInfo != null && requestInfo.IP == rchHub.expectedIp;
    }

    static async Task<IResult> WriteResult(HttpContext context, [FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
            return Results.BadRequest();

        if (context.Request.ContentLength > maxRequestSize)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        if (!RchClient.rchIds.TryGetValue(id, out var rchHub))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (!IsExpectedClient(context, rchHub))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            using (var byteBuf = new BufferPool())
            {
                int bytesRead;
                var memBuf = byteBuf.Memory;

                while ((bytesRead = await context.Request.Body.ReadAsync(memBuf, rchHub.ct).ConfigureAwait(false)) > 0)
                    rchHub.ms.Write(memBuf.Span.Slice(0, bytesRead));
            }

            rchHub.ms.Position = 0;

            rchHub.tcs.TrySetResult(null);
            return Results.Ok();
        }
        catch
        {
            rchHub.ms.SetLength(0);
            rchHub.tcs.TrySetResult(null);
        }

        return Results.BadRequest();
    }

    static async Task<IResult> WriteZipResult(HttpContext context, [FromQuery] string id)
    {
        if (string.IsNullOrEmpty(id))
            return Results.BadRequest();

        if (context.Request.ContentLength > maxRequestSize)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        if (!RchClient.rchIds.TryGetValue(id, out var rchHub))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (!IsExpectedClient(context, rchHub))
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            using (var gzip = new GZipStream(context.Request.Body, CompressionMode.Decompress, leaveOpen: true))
            {
                using (var byteBuf = new BufferPool())
                {
                    int bytesRead;
                    var memBuf = byteBuf.Memory;

                    while ((bytesRead = await gzip.ReadAsync(memBuf, rchHub.ct).ConfigureAwait(false)) > 0)
                        rchHub.ms.Write(memBuf.Span.Slice(0, bytesRead));
                }

                rchHub.ms.Position = 0;

                rchHub.tcs.TrySetResult(null);
                return Results.Ok();
            }
        }
        catch
        {
            rchHub.ms.SetLength(0);
            rchHub.tcs.TrySetResult(null);
        }

        return Results.BadRequest();
    }

    static IResult СheckСonnected(HttpContext context)
    {
        var requestInfo = context.Features.Get<RequestModel>();
        var host = CoreInit.Host(context);

        var rch = new RchClient(context, host, rchBaseSettings, requestInfo);

        if (rch.IsNotConnected())
            return Results.Content(rch.connectionMsg, "application/json; charset=utf-8");

        var info = rch.InfoConnected();

        return Results.Json(info is null
            ? emptyRchClientInfo
            : new RchConnectedResponse(info.apkVersion, info.rchtype)
        );
    }
}
