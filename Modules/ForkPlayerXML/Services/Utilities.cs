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


    public static string Description(TmdbMovie movie, string end_title)
    {
        string release_date = movie.release_date ?? movie.first_air_date;
        if (release_date != null)
            release_date = release_date.Split("-")[0];

        return $@"<div class=""description"" style=""display: block; top: 38px; max-height: 1042px;""><div id=""title"" style=""color: #699bbb;""><strong>{end_title}</strong></div><br><div id=""cover_div"" style=""float: left; margin: 0px 1.8% 0px 0px;""><img id=""cover_img"" style=""width: 184px; "" src=""http://image.tmdb.org/t/p/w200/{movie.poster_path}""></div><div><strong><span style=""color: #3974d0;"">Выход:</span></strong> {release_date}<br><strong><span style=""color: #339966;"">Качество:</span></strong> {movie.release_quality}<br><div id=""footer"" style=""clear: both;  ""><br>{movie.overview}</div></div></div>";
    }
}
