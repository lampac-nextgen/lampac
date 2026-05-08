using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Newtonsoft.Json;
using Shared.Services;
using Shared.Models.AppConf;
using Shared.Models.Browser;
using Shared.Models.Module;
using Shared.Models.ServerProxy;
using Shared.Models.SISI.Base;
using System.Text.RegularExpressions;
using System.Threading;
using YamlDotNet.Serialization;
using Shared.Models.Events;
using Shared.Services.Pools.Json;
using Newtonsoft.Json.Linq;

namespace Shared;

public class CoreInit
{
    #region static
    public static string rootPasswd;

    public static readonly HashSet<string> CompressionMimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml"]).ToHashSet();

    public static bool Win32NT => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static HashSet<string> BaseModPathWhiteList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static HashSet<string> BaseModValidQueryValueWhiteList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public readonly string guid = Guid.NewGuid().ToString();

    public static List<RootModule> modules = new List<RootModule>();

    static CoreInit()
    {
        updateConf();
        if (File.Exists("init.conf"))
            lastUpdateConf = File.GetLastWriteTime("init.conf");

        updateYamlConf();
        if (File.Exists("init.yaml") && File.GetLastWriteTime("init.yaml") > lastUpdateConf)
            lastUpdateConf = File.GetLastWriteTime("init.yaml");

        #region watcherInit
        ThreadPool.QueueUserWorkItem(async _ =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                try
                {
                    if (File.Exists("init.conf") || File.Exists("init.yaml"))
                    {
                        var lwtConf = File.Exists("init.conf") ? File.GetLastWriteTime("init.conf") : DateTime.MinValue;
                        var lwtYaml = File.Exists("init.yaml") ? File.GetLastWriteTime("init.yaml") : DateTime.MinValue;

                        if (lwtConf > lastUpdateConf || lwtYaml > lastUpdateConf)
                        {
                            updateConf();
                            updateYamlConf();

                            CurrentConf = JObject.FromObject(conf);
                            lastUpdateConf = lwtConf > lwtYaml ? lwtConf : lwtYaml;

                            if (EventListener.UpdateInitFile != null)
                            {
                                foreach (Action handler in EventListener.UpdateInitFile.GetInvocationList())
                                    handler();
                            }

                            if (EventListener.UpdateCurrentConf != null)
                            {
                                foreach (Action handler in EventListener.UpdateCurrentConf.GetInvocationList())
                                    handler();
                            }

                            string init = JsonConvert.SerializeObject(CurrentConf, Formatting.Indented, new JsonSerializerSettings()
                            {
                                NullValueHandling = NullValueHandling.Ignore,
                                DefaultValueHandling = DefaultValueHandling.Ignore
                            });

                            Directory.CreateDirectory("database/backup/init");
                            File.WriteAllText($"database/backup/init/{DateTime.Now.ToString("dd-MM-yyyy.HH")}.conf", init);
                            File.WriteAllText("current.conf", JsonConvert.SerializeObject(CurrentConf, Formatting.Indented));
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "CatchId={CatchId}", "id_b0raq9ux");
                }
            }
        });
        #endregion
    }
    #endregion

    #region conf
    static DateTime lastUpdateConf = default;

    public static CoreInit conf = null;

    public static JObject CurrentConf = null;

    static void updateConf()
    {
        if (!File.Exists("init.conf") && !File.Exists("base.conf"))
        {
            conf = new CoreInit();
            return;
        }

        var _tempConf = ModuleInvoke.DeserializeInit<CoreInit>(null);
        if (_tempConf == null)
            throw new Exception("Failed to deserialize init.conf");

        conf = _tempConf;

        if (conf.accsdb.accounts != null)
        {
            foreach (var u in conf.accsdb.accounts)
            {
                if (conf.accsdb.findUser(u.Key) is AccsUser user)
                {
                    if (u.Value > user.expires)
                        user.expires = u.Value;
                }
                else
                {
                    conf.accsdb.users.Add(new AccsUser()
                    {
                        id = u.Key.ToLowerAndTrim(),
                        expires = u.Value
                    });
                }
            }
        }

        PosterApi.Initialization(conf.omdbapi_key, conf.posterApi, new ProxyLink());
    }

    static void updateYamlConf()
    {
        if (conf == null)
            return;

        try
        {
            if (File.Exists("init.yaml"))
            {
                string yaml = File.ReadAllText("init.yaml");
                if (!string.IsNullOrWhiteSpace(yaml))
                {
                    var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
                    var yamlObject = deserializer.Deserialize(new StringReader(yaml));
                    if (yamlObject != null)
                    {
                        string json = JsonConvertPool.SerializeObject(yamlObject);
                        JsonConvert.PopulateObject(json, conf, new JsonSerializerSettings
                        {
                            Error = (se, ev) =>
                            {
                                ev.ErrorContext.Handled = true;
                                Console.WriteLine($"DeserializeObject Exception init.yaml:\n{ev.ErrorContext.Error}\n\n");
                            }
                        });
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_ae3dd533");
            Console.WriteLine($"DeserializeObject Exception init.yaml:\n{ex}\n\n");
        }
    }
    #endregion

    #region Host
    public static string Host(HttpContext httpContext)
    {
        string scheme = string.IsNullOrEmpty(conf.listen.scheme) ? httpContext.Request.Scheme : conf.listen.scheme;
        if (httpContext.Request.Headers.TryGetValue("xscheme", out var xscheme) && xscheme.Count > 0)
            scheme = xscheme;

        if (!string.IsNullOrEmpty(conf.listen.host))
            return $"{scheme}://{conf.listen.host}";

        if (httpContext.Request.Headers.TryGetValue("xhost", out var xhost) && xhost.Count > 0)
            return $"{scheme}://{Regex.Replace(xhost, "^https?://", "")}";

        return $"{scheme}://{httpContext.Request.Host.Value}";
    }
    #endregion


    public ListenConf listen = new ListenConf()
    {
        localhost = "127.0.0.1",
        port = 9118,
        ResponseCancelAfter = 5,
        compression = false,
        frontend = null // cloudflare|nginx
    };

    public bool lowMemoryMode;

    public long freeDiskSpace = -1;

    public bool serilog = true;

    public bool useDeveloperExceptionPage = false;

    public string exceptionHandlerLogTarget = "console"; // console|file|none

    public string exceptionHandlerLogFile = "logs/exceptionHandler.log";

    public string watcherInit = "cron";// system

    public string imagelibrary = "NetVips"; // NetVips|ImageMagick|none

    public bool disableEng = false;

    public string defaultOn = "enable";

    public string omdbapi_key;

    public string corsehost { get; set; } = "";

    public bool DynamicModule { get; set; }

    public BaseModule BaseModule { get; set; } = new BaseModule()
    {
        nws = true,
        ValidateRequest = true,
        ValidateIdentity = true,
        BlockedBots = true,
        Middlewares = new BaseModuleMiddlewares()
        {
            proxy = true,
            proxyimg = true,
            anonymousRequest = true,
            staticFiles = false,
            unknownStaticFiles = false
        }
    };

    public PoolConf pool { get; set; } = new PoolConf()
    {
        BufferValidityMinutes = 180,
        BufferMax = 500_000000, // 500mb
        BufferByteSmallMaxCount = 100,
        BufferByteLargeMaxCount = 100,
        BufferByteMediumMaxCount = 100,
        BufferCharSmallMaxCount = 100,
        BufferCharLargeMaxCount = 100,
        BufferCharMediumMaxCount = 100,
        BufferWriterSmallMaxCount = 100,
        BufferWriterLargeMaxCount = 100,
        StringBuilderSmallMaxCount = 10,
        StringBuilderLargeMaxCount = 20,
        NewtonsoftCharMediumMaxCount = 5,
        NewtonsoftCharLargeMaxCount = 5
    };

    public ApnConf apn { get; set; } = new ApnConf() { secure = "none" };

    public CubConf cub { get; set; } = new CubConf()
    {
        api_key = "4ef0d7355d9ffb5151e987764708ce96",
        scheme = "https",
        domain = "cub.red",
        mirror = "cub.rip"
    };

    public PosterApiConf posterApi = new PosterApiConf()
    {
        rsize = true,
        width = 210,
        bypass = "statichdrezka\\."
    };

    public WsConf WebSocket = new WsConf()
    {
        minVersion = 1,
        send_pong = true,
        inactiveAfterMinutes = 120,
        MaximumReceiveMessageSize = 1024 * 1024, // 1Mb
        rch = true
    };

    public KitConf kit = new KitConf()
    {
        aes = true,
        aesgcmkeyName = "kit_aesgcmkey",
        cacheToSeconds = 60 * 60 * 3, // 3h
        configCheckIntervalSeconds = 20
    };

    public HybridCacheConf cache = new HybridCacheConf()
    {
        type = "fdb", // mem|fdb
        extend = 600, // 10m
        memExtend = true
    };

    public StaticacheConf Staticache { get; set; } = new StaticacheConf();

    public WafConf WAF = new WafConf()
    {
        enable = true,
        disabled = new WafDisabled(),
        bypassLocalIP = false,
        allowExternalIpAccessToLocalRequest = false,
        bruteForceProtection = true,
        limit_map = new List<WafLimitRootMap>
        {
            new("^/rch/", new WafLimitMap { limit = 10, second = 1 }),
            new(".*", new WafLimitMap { limit = 50, second = 1 })
        }
    };

    public OpenStatConf openstat = new OpenStatConf();

    public RchConf rch = new RchConf()
    {
        enable = true,
        autoReconnect = true
    };

    public GCConf GC { get; set; } = new GCConf()
    {
        enable = false,

        // false - GC старается возвращать высвобождённую виртуальную память ОС
        // true - GC сохраняет адресное пространство/сегменты, чтобы быстрее переиспользовать
        RetainVM = false,

        // false — только Stop-the-World сборки
        // true  — фоновые (concurrent) сборки
        Concurrent = null,

        // 1 — минимальная экономия памяти, крупные поколения
        // 9 — максимальная экономия, маленькие поколения, частые GC
        ConserveMemory = 3,

        // Процент доступной памяти, после которого GC считает систему под давлением
        // Ниже значение — раньше и агрессивнее запускаются сборки
        HighMemoryPercent = 80
    };

    public PuppeteerConf chromium = new PuppeteerConf()
    {
        enable = true,
        Headless = true,
        Args = ["--disable-blink-features=AutomationControlled"], // , "--window-position=-2000,100"
        context = new KeepopenContext() { keepopen = true, keepalive = 20, min = 0, max = 4 }
    };

    public PuppeteerConf firefox = new PuppeteerConf()
    {
        enable = false,
        Headless = true,
        context = new KeepopenContext() { keepopen = true, keepalive = 20, min = 1, max = 2 }
    };

    public ServerproxyConf serverproxy = new ServerproxyConf()
    {
        enable = true,
        verifyip = true,
        responseContentLength = true,
        buffering = new ServerproxyBufferingConf()
        {
            enable = true,
            bytes = 16_000000 // 16Mb
        },
        image = new ServerproxyImageConf()
        {
            enable = true,
            NetVipsCache = true,
            cache = true,
            cache_rsize = true,
            cache_time = 60 * 24 // 24h
        },
        cache_hls = 60 * 24,       // 24h
        maxlength_m3u = 10_000000, // 10mb
        maxlength_ts = 70_000000   // 70mb
    };

    public OnlineConf online = new OnlineConf()
    {
        name = "Lampac",
        version = true,
        checkOnlineSearch = true,
        btn_priority_forced = true,
        with_search = new HashSet<string>()
    };

    public SisiConf sisi { get; set; } = new SisiConf()
    {
        heightPicture = 240,
        rsize = true,
        rsize_disable = ["Chaturbate", "PornHub", "PornHubPremium", "HQporner", "Spankbang", "Eporner", "Porntrex", "Xnxx", "Porndig", "Youjizz", "Veporn", "Pornk"],
    };

    public AccsConf accsdb = new AccsConf()
    {
        enable = true,
        authMesage = "Войдите в аккаунт - настройки, синхронизация",
        denyMesage = "Добавьте {account_email} в init.conf",
        denyGroupMesage = "У вас нет прав для просмотра этой страницы",
        expiresMesage = "Время доступа для {account_email} истекло в {expires}",
        maxip_hour = 10,
        maxrequest_hour = 400,
        maxlock_day = 3,
        blocked_hour = 36,
        shared_daytime = 366 * 10, // 10 years
    };

    public VastConf vast = new VastConf();

    /// <summary>
    /// https://infocisco.ru/prefix_network_mask.html
    /// </summary>
    public HashSet<Known> KnownProxies { get; set; } = new HashSet<Known>()
    {
        new Known() { ip = "10.0.0.0", prefixLength = 8 },
        new Known() { ip = "172.16.0.0", prefixLength = 12 },
        new Known() { ip = "192.168.0.0", prefixLength = 16 },
        new Known() { ip = "169.254.0.0", prefixLength = 16 }
    };

    public ProxySettings proxy = new ProxySettings();

    public ProxySettings[] globalproxy = new ProxySettings[]
    {
        //new ProxySettings()
        //{
        //    pattern = "\\.onion",
        //    list = ["socks5://127.0.0.1:9050"]
        //}
    };

    public List<OverrideResponse> overrideResponse { get; set; }
    //{
    //    new OverrideResponse()
    //    {
    //        pattern = "/over/text",
    //        action = "html",
    //        type = "text/plain; charset=utf-8",
    //        val = "text"
    //    },
    //    new OverrideResponse()
    //    {
    //        pattern = "/over/online.js",
    //        action = "file",
    //        type = "application/javascript; charset=utf-8",
    //        val = "plugins/online.js"
    //    },
    //    new OverrideResponse()
    //    {
    //        pattern = "/over/gogoole",
    //        action = "redirect",
    //        val = "https://www.google.com/"
    //    }
    //};
}
