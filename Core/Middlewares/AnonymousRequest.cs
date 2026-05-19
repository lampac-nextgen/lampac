using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Shared.Models.Base;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Core.Middlewares;

partial class AnonymousRequestRegex
{
    [GeneratedRegex("^/[0-9a-zA-Z\\-]+\\.js", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex Js();
}

public class AnonymousRequest
{
    private readonly RequestDelegate _next;

    public AnonymousRequest(RequestDelegate next)
    {
        _next = next;
    }

    public Task Invoke(HttpContext httpContext)
    {
        var requestInfo = httpContext.Features.Get<RequestModel>();
        if (requestInfo.IsProxyRequest || requestInfo.IsProxyImg)
            return _next(httpContext);

        if (httpContext.Request.Path.Value is "/" or "/favicon.ico" or "/.well-known/appspecific/com.chrome.devtools.json")
        {
            requestInfo.IsAnonymousRequest = true;
        }
        else if (AnonymousRequestRegex.Js().IsMatch(httpContext.Request.Path.Value))
        {
            requestInfo.IsAnonymousRequest = true;
        }
        else
        {
            var endpoint = httpContext.GetEndpoint();
            if (endpoint != null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
                requestInfo.IsAnonymousRequest = true;
        }

        return _next(httpContext);
    }
}
