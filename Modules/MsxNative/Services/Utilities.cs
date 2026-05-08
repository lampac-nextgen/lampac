using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shared.Services.Pools;
using System;
using System.Web;

namespace MsxNative;

public static class Utilities
{
    public static bool IsMsxPlayer(HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue("initial", out StringValues initial) && initial.Count > 0)
            return initial[0].Equals("msx", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public static string ClearArgs(IQueryCollection query)
    {
        bool first = true;
        var args = StringBuilderPool.ThreadInstance;

        foreach (var q in query)
        {
            if (q.Key is "v" or "t" or "initial" or "pg")
                continue;

            if (!string.IsNullOrEmpty(q.Key) && !string.IsNullOrEmpty(q.Value))
            {
                if (!first)
                    args.Append("&");

                args.Append(q.Key).Append("=").Append(HttpUtility.UrlEncode(q.Value));
                first = false;
            }
        }

        return args.ToString();
    }

    public static string Uri(string uri, IQueryCollection query)
    {
        var result = StringBuilderPool.ThreadInstance;
        result.Append(uri + (uri.Contains("?") ? "&" : "?") + "initial=msx");

        foreach (var q in query)
        {
            if (q.Key is "uid" or "token")
            {
                if (!string.IsNullOrEmpty(q.Key) && !string.IsNullOrEmpty(q.Value))
                    result.Append("&").Append(q.Key).Append("=").Append(HttpUtility.UrlEncode(q.Value));
            }
        }

        return result.ToString();
    }
}
