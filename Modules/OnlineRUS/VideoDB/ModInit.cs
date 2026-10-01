using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Models.Online.Settings;
using Shared.Services;

namespace VideoDB;

public class ModInit : IModuleLoaded
{
    public static OnlinesSettings conf;

    /// <summary>"VideoDB": { "cdp": "http://chrome:9222" } — Chrome для обновления cookie антибота obrut.show</summary>
    public static string cdp;

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("VideoDB", new OnlinesSettings("VideoDB", "https://kinogo.media", streamproxy: true)
        {
            httpversion = 2,
            httptimeout = 30,
            rch_access = "apk",
            stream_access = "apk,cors,web",
            priorityBrowser = "http",
            imitationHuman = true,
            headers = HeadersModel.Init(Http.defaultFullHeaders,
                ("sec-fetch-storage-access", "active"),
                ("upgrade-insecure-requests", "1")
            ).ToDictionary(),
            headers_stream = HeadersModel.Init(Http.defaultFullHeaders,
                ("accept", "*/*"),
                ("origin", "https://kinogo.media"),
                ("referer", "https://kinogo.media/"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-site", "same-site")
            ).ToDictionary()
        });

        cdp = CoreInit.CurrentConf?["VideoDB"]?["cdp"]?.ToString();
    }
}
