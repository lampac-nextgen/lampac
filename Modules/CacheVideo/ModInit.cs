using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System.Text.RegularExpressions;

namespace CacheVideo;

public class ModInit : IModuleLoaded
{
    public void Loaded(InitspaceModel conf)
    {
        EventListener.ProxyApiCacheStream += e =>
        {
            switch (e.decryptLink.plugin ?? "")
            {
                case "PornHub":
                case "Youjizz":
                    {
                        string uriKey = Regex.Replace(e.decryptLink.uri.Split("?")[0], "^https?://[^/]+", "");
                        return ($"{e.decryptLink.plugin}:{uriKey}", "video/MP2T");
                    }
                case "Xnxx":
                case "Xvideos":
                    {
                        string uriKey = Regex.Replace(e.decryptLink.uri.Split("?")[0], "^https?://[^/]+/[^,/]+", "");
                        return ($"{e.decryptLink.plugin}:{uriKey}", "video/mp2t");
                    }
                case "Xhamster":
                    {
                        string uriKey = Regex.Replace(e.decryptLink.uri.Split("?")[0], "^https?://[^/]+/[^/]+", "");
                        return ($"{e.decryptLink.plugin}:{uriKey}", "video/MP2T");
                    }
                default:
                    return default;
            }
        };

        EventListener.ProxyImgMd5key += e =>
        {
            switch (e.decryptLink.plugin ?? "")
            {
                case "PornHub":
                case "Porntrex":
                    {
                        string uriKey = Regex.Replace(e.href.Split("?")[0], "^https?://[^/]+", "");
                        return $"{e.decryptLink.plugin}:{uriKey}:{e.width}:{e.height}";
                    }
                default:
                    return default;
            }
        };
    }

    public void Dispose()
    {
    }
}
