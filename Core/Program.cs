using Core.Middlewares;
using Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Serilog;
using Shared;
using Shared.Models.Base;
using Shared.Models.SQL;
using Shared.PlaywrightCore;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Core;

public class Program
{
    #region static
    static IHost _host;
    public static bool _reload { get; private set; } = true;

    public static ConcurrentBag<(IPAddress prefix, int prefixLength)> cloudflare_ips = new();

    static Timer _usersTimer, _lowMemoryModeTimer;
    #endregion

    #region Run
    static HashSet<string> AssemblyLocations = new();

    public static void Main(string[] args)
    {
        string refs = Path.Combine(AppContext.BaseDirectory, "runtimes", "references");

        if (Directory.Exists(refs))
        {
            foreach (string dllPath in Directory.GetFiles(refs, "*.dll"))
            {
                var loadedAssembly = Assembly.LoadFrom(dllPath);
                AssemblyLocations.Add(loadedAssembly.Location);
            }

            AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                foreach (string name in new string[] { $"ru/{assemblyName.Name}", assemblyName.Name })
                {
                    string assemblyPath = Path.Combine(refs, name);
                    if (File.Exists(assemblyPath))
                        return context.LoadFromAssemblyPath(assemblyPath);
                }

                return null;
            };
        }

        Run(args);
    }

    public static void Run(string[] args)
    {
        #region appReferences
        CSharpEval.appReferences = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();

        foreach (var asl in AssemblyLocations)
            CSharpEval.appReferences.Add(MetadataReference.CreateFromFile(asl));
        #endregion

        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        AppContext.SetSwitch("System.Net.Http.EnableActivityPropagation", false);

        var init = CoreInit.conf;
        var mods = init.BaseModule;

        #region GC
        var gc = init.GC;
        if (gc != null && gc.enable)
        {
            if (gc.Concurrent.HasValue)
                AppContext.SetSwitch("System.GC.Concurrent", gc.Concurrent.Value);

            if (gc.ConserveMemory.HasValue)
                AppContext.SetData("System.GC.ConserveMemory", gc.ConserveMemory.Value);

            if (gc.HighMemoryPercent.HasValue)
                AppContext.SetData("System.GC.HighMemoryPercent", gc.HighMemoryPercent.Value);

            if (gc.RetainVM.HasValue)
                AppContext.SetSwitch("System.GC.RetainVM", gc.RetainVM.Value);
        }
        #endregion

        #region RchClient
        if (mods.nws)
        {
            RchClient.hub += (e, req)
                => _ = NativeWebSocket.SendRchRequestAsync(req.connectionId, req.rchId, req.url, req.data, req.headers, req.returnHeaders).ConfigureAwait(false);
        }
        #endregion

        #region Log
        Directory.CreateDirectory("logs");

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Error();

        if (init.serilog)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.File(
                "logs/app-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
        }

        Log.Logger = loggerConfiguration.CreateLogger();
        #endregion

        #region ThreadPool
        int cpu = Environment.ProcessorCount;
        ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
        ThreadPool.SetMinThreads(
            workerThreads: Math.Max(workerThreads, cpu * 16),
            completionPortThreads: Math.Max(completionPortThreads, cpu * 8)
        );
        #endregion

        #region passwd
        if (!File.Exists("passwd"))
        {
            CoreInit.rootPasswd = Guid.NewGuid().ToString();
            File.WriteAllText("passwd", CoreInit.rootPasswd);
        }
        else
        {
            CoreInit.rootPasswd = File.ReadAllText("passwd");
            CoreInit.rootPasswd = Regex.Replace(CoreInit.rootPasswd, "[\n\r\t ]+", "").Trim();
        }
        #endregion

        #region Playwright
        if (init.chromium.enable || init.firefox.enable)
        {
            PlaywrightContext.Initialization();

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                if (await PlaywrightBase.InitializationAsync())
                {
                    if (init.chromium.enable)
                        _ = Chromium.CreateAsync().ConfigureAwait(false);

                    if (init.firefox.enable)
                        _ = Firefox.CreateAsync().ConfigureAwait(false);
                }
            });

            Chromium.CronStart();
            Firefox.CronStart();
        }
        #endregion

        #region cloudflare_ips
        ThreadPool.QueueUserWorkItem(async _ =>
        {
            string ips = await Http.Get("https://www.cloudflare.com/ips-v4");
            if (ips == null || !ips.Contains("173.245."))
                ips = File.Exists("data/cloudflare/ips-v4.txt") ? File.ReadAllText("data/cloudflare/ips-v4.txt") : null;

            if (ips != null)
            {
                string ips_v6 = await Http.Get("https://www.cloudflare.com/ips-v6");
                if (ips_v6 == null || !ips_v6.Contains("2400:cb00"))
                    ips_v6 = File.Exists("data/cloudflare/ips-v6.txt") ? File.ReadAllText("data/cloudflare/ips-v6.txt") : null;

                if (ips_v6 != null)
                {
                    foreach (string ip in (ips + "\n" + ips_v6).Split('\n'))
                    {
                        if (string.IsNullOrEmpty(ip) || !ip.Contains("/"))
                            continue;

                        try
                        {
                            string[] ln = ip.Split('/');
                            cloudflare_ips.Add((IPAddress.Parse(ln[0].Trim()), int.Parse(ln[1].Trim())));
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "Program", "id_5397q95u");
                        }
                    }
                }
            }

            Console.WriteLine($"cloudflare_ips: {cloudflare_ips.Count}");
        });
        #endregion

        Console.WriteLine("load cache");
        ProxyImg.Initialization();
        ProxyAPI.Initialization();
        Staticache.Initialization();
        HybridFileCache.LoadCache();

        _usersTimer = new Timer(UpdateUsersDb, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        if (CoreInit.conf.lowMemoryMode)
        {
            _lowMemoryModeTimer = new Timer(_ =>
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        }

        try
        {
            while (_reload)
            {
                _host = CreateHostBuilder(args).Build();
                _reload = false;
                _host.Run();
            }
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
    #endregion

    #region CreateHostBuilder
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(op =>
                {
                    op.AddServerHeader = false;

                    if (CoreInit.conf.listen.keepalive.HasValue && CoreInit.conf.listen.keepalive.Value > 0)
                        op.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(CoreInit.conf.listen.keepalive.Value);

                    op.ConfigureEndpointDefaults(endpointOptions =>
                    {
                        if (CoreInit.conf.listen.endpointDefaultsProtocols.HasValue)
                            endpointOptions.Protocols = CoreInit.conf.listen.endpointDefaultsProtocols.Value;
                    });

                    if (string.IsNullOrEmpty(CoreInit.conf.listen.sock) && string.IsNullOrEmpty(CoreInit.conf.listen.ip))
                    {
                        op.Listen(IPAddress.Parse("127.0.0.1"), CoreInit.conf.listen.port);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(CoreInit.conf.listen.sock))
                        {
                            if (File.Exists($"/var/run/{CoreInit.conf.listen.sock}.sock"))
                                File.Delete($"/var/run/{CoreInit.conf.listen.sock}.sock");

                            op.ListenUnixSocket($"/var/run/{CoreInit.conf.listen.sock}.sock");
                        }

                        if (!string.IsNullOrEmpty(CoreInit.conf.listen.ip))
                            op.Listen(CoreInit.conf.listen.ip == "any" ? IPAddress.Any : CoreInit.conf.listen.ip == "broadcast" ? IPAddress.Broadcast : IPAddress.Parse(CoreInit.conf.listen.ip), CoreInit.conf.listen.port);
                    }
                })
                .UseStartup<Startup>();
            });
    #endregion


    #region UpdateUsersDb
    static int _updateUsersDb = 0;
    static string _usersKeyUpdate = string.Empty;

    static void UpdateUsersDb(object state)
    {
        if (Interlocked.Exchange(ref _updateUsersDb, 1) == 1)
            return;

        try
        {
            if (File.Exists("users.json"))
            {
                var lastWriteTime = File.GetLastWriteTime("users.json");

                string keyUpdate = $"{CoreInit.conf?.guid}:{CoreInit.conf?.accsdb?.users?.Count ?? 0}:{lastWriteTime}";
                if (keyUpdate == _usersKeyUpdate)
                    return;

                foreach (var user in JsonConvert.DeserializeObject<List<AccsUser>>(File.ReadAllText("users.json")))
                {
                    try
                    {
                        var find = CoreInit.conf.accsdb.findUser(user.id ?? user.ids?.First());
                        if (find != null)
                        {
                            find.id = user.id;
                            find.ids = user.ids;
                            find.group = user.group;
                            find.IsPasswd = user.IsPasswd;
                            find.expires = user.expires;
                            find.ban = user.ban;
                            find.ban_msg = user.ban_msg;
                            find.comment = user.comment;
                            find.@params = user.@params;
                        }
                        else
                        {
                            CoreInit.conf.accsdb.users.Add(user);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Serilog.Log.Error(ex, "{Class} {CatchId}", "Program", "id_85syu64t");
                    }
                }

                _usersKeyUpdate = keyUpdate;
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "Program", "id_gvenci5l");
        }
        finally
        {
            Volatile.Write(ref _updateUsersDb, 0);
        }
    }
    #endregion
}
