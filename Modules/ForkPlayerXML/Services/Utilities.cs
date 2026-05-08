using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shared.Services.Pools;
using System;
using System.Web;

namespace ForkXML;

public class Utilities
{
    public static bool IsForkPlayer(HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue("initial", out StringValues initial) && initial.Count > 0)
            return initial[0].StartsWith("ForkXML", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public static string ClearArgs(IQueryCollection query)
    {
        bool first = true;
        var args = StringBuilderPool.ThreadInstance;

        foreach (var q in query)
        {
            if (q.Key is "box_client" or "box_mac" or "pg" or "initial" or "platform" or "country" or "tvp" or "hw")
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

    public static string ForkArgs(IQueryCollection query)
    {
        bool first = true;
        var args = StringBuilderPool.ThreadInstance;

        foreach (var q in query)
        {
            if (q.Key is "box_client" or "box_mac" or "pg" or "initial" or "platform" or "country" or "tvp" or "hw")
            {
                if (!string.IsNullOrEmpty(q.Key) && !string.IsNullOrEmpty(q.Value))
                {
                    if (!first)
                        args.Append("&");

                    args.Append(q.Key).Append("=").Append(HttpUtility.UrlEncode(q.Value));
                    first = false;
                }
            }
        }

        return args.ToString();
    }
}
