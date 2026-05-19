using Shared.Services;
using System.Text.RegularExpressions;

namespace Shared.Models.Base;

public static class PosterApi
{
    static string omdbapi_key;
    static PosterApiConf init;

    public static void Initialization(string omdbkey, PosterApiConf conf)
    {
        omdbapi_key = omdbkey;
        init = conf;
    }

    public static string Find(long? kpid, string imdb)
    {
        string imdb_img = null, kp_img = null;

        if (!string.IsNullOrEmpty(omdbapi_key) && !string.IsNullOrEmpty(imdb))
            imdb_img = $"https://img.omdbapi.com/?apikey={omdbapi_key}&i={imdb}";

        if (kpid > 0)
            kp_img = $"https://st.kp.yandex.net/images/film_iphone/iphone360_{kpid}.jpg";

        if (imdb_img != null && kp_img != null)
            return Size($"{imdb_img} or {kp_img}");

        return Size(imdb_img ?? kp_img);
    }

    public static string Size(string host, string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return uri;

        string img = Size(uri);
        if (img.StartsWith("http"))
            return img;

        return host + img;
    }

    public static string Size(string uri)
    {
        if (string.IsNullOrEmpty(uri) || init == null || !init.rsize || (init.width == 0 && init.height == 0))
            return uri?.Split(" or ")?[0];

        if (!string.IsNullOrEmpty(init.disable_rsize) && Regex.IsMatch(uri, init.disable_rsize, RegexOptions.IgnoreCase))
            return uri?.Split(" or ")?[0];

        if (!string.IsNullOrEmpty(init.bypass) && Regex.IsMatch(uri, init.bypass, RegexOptions.IgnoreCase))
        {
            return ProxyLink.Encrypt(
                uri,
                "posterapi",
                IsProxyImg: true,
                prefix: [init.host, "/proxyimg/"]
            );
        }
        else
        {
            return ProxyLink.Encrypt(
                uri,
                "posterapi",
                IsProxyImg: true,
                prefix: [init.host, "proxyimg", $":{init.width}:{init.height}/"]
            );
        }
    }
}
