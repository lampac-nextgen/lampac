using Core.Endpoints;
using Core.Middlewares;
using Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Newtonsoft.Json;
using Shared;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Entrys;
using Shared.Models.Module.Interfaces;
using Shared.PlaywrightCore;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Core
{
    public class Startup
    {
        #region Startup
        static readonly object _exceptionLogInitLock = new object();
        static FileStream _exceptionLogFileStream;
        static StreamWriter _exceptionLogWriter;

        static IApplicationBuilder _app = null;

        public static bool IsShutdown { get; private set; }

        public IConfiguration Configuration { get; }

        public static IServiceCollection serviceCollection { get; private set; }

        public static IMemoryCache memoryCache { get; private set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        #endregion

        #region ConfigureServices
        public void ConfigureServices(IServiceCollection services)
        {
            var init = CoreInit.conf;
            var mods = init.BaseModule;

            serviceCollection = services;

            #region IHttpClientFactory
            services.AddHttpClient("proxy").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                UseCookies = false
            });

            services.AddHttpClient("proxyRedirect").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                UseCookies = false
            });

            services.AddHttpClient("proxyimg").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                UseCookies = false
            });

            services.AddHttpClient("base").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                UseCookies = false
            });

            services.AddHttpClient("baseNoRedirect").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                UseCookies = false
            });

            services.AddHttpClient("http2", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.AddHttpClient("http2proxyimg", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.AddHttpClient("http2NoRedirect", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version20;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.AddHttpClient("http3", client =>
            {
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate,
                SslOptions = { RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            });

            services.RemoveAll<IHttpMessageHandlerBuilderFilter>();
            #endregion

            services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            if (init.listen.compression)
            {
                services.AddResponseCompression(options =>
                {
                    options.MimeTypes = CoreInit.CompressionMimeTypes;
                });
            }

            services.AddMemoryCache(o =>
            {
                o.TrackStatistics = CoreInit.conf.openstat.enable;
            });

            services.AddSingleton<IActionDescriptorChangeProvider>(DynamicActionDescriptorChangeProvider.Instance);
            services.AddSingleton(DynamicActionDescriptorChangeProvider.Instance);

            IMvcBuilder mvcBuilder = services.AddControllersWithViews();

            mvcBuilder.AddJsonOptions(options =>
            {
                //options.JsonSerializerOptions.IgnoreNullValues = true;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
            });

            Shared.Startup.Configure(null, new NativeWebSocket());

            #region load modules
            var skipCompilationFolders = new HashSet<string>(mods.SkipModules ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (string modfolder in new string[] { "mods", "module" })
            {
                if (Directory.Exists($"{modfolder}/"))
                {
                    #region module references
                    string referencesPath = Path.Combine(Environment.CurrentDirectory, modfolder, "references");

                    if (Directory.Exists(referencesPath))
                    {
                        foreach (string dllFile in Directory.GetFiles(referencesPath, "*.dll", SearchOption.AllDirectories))
                        {
                            try
                            {
                                var loadedAssembly = Assembly.LoadFrom(dllFile);
                                mvcBuilder.AddApplicationPart(loadedAssembly);
                                CSharpEval.appReferences.Add(MetadataReference.CreateFromFile(loadedAssembly.Location));

                                Console.WriteLine($"load reference: {dllFile}");
                            }
                            catch (System.Exception ex)
                            {
                                Console.WriteLine($"Failed to load reference {dllFile}: {ex.Message}");
                                throw new Exception();
                            }
                        }
                    }
                    #endregion

                    #region *.dll
                    foreach (string path in Directory.GetFiles($"{modfolder}/", "*.dll"))
                    {
                        try
                        {
                            var mod = new RootModule();
                            mod.assembly = Assembly.LoadFile(Path.Combine(Environment.CurrentDirectory, path));
                            mod.name = Path.GetFileName(path);
                            CoreInit.modules.Add(mod);

                            Console.WriteLine($"load {modfolder}: " + mod.name);
                            mvcBuilder.AddApplicationPart(mod.assembly);
                        }
                        catch (System.Exception ex)
                        {
                            Console.WriteLine(ex.Message + "\n");
                            throw new Exception();
                        }
                    }
                    #endregion

                    #region compilation
                    foreach (string folderMod in Directory.GetDirectories($"{modfolder}/"))
                    {
                        string folderName = Path.GetFileName(folderMod);
                        if (skipCompilationFolders.Contains(folderName))
                        {
                            Console.WriteLine($"skip compilation folder {modfolder}: {folderName}");
                            continue;
                        }

                        string manifest = Path.Combine(Environment.CurrentDirectory, folderMod, "manifest.json");
                        if (!File.Exists(manifest))
                            continue;

                        var mod = JsonConvert.DeserializeObject<RootModule>(File.ReadAllText(manifest));

                        mod.name = folderName;
                        mod.path = modfolder;

                        if (!mod.enable || CoreInit.modules.FirstOrDefault(i => i.name == mod.name) != null)
                            continue;

                        var build = CSharpEval.Compilation(mod);
                        if (build.assembly == null)
                            throw new Exception();

                        Console.WriteLine($"compilation {modfolder}: {mod.name}");

                        mod.assembly = build.assembly;
                        mod.assemblyLoadContext = build.alc;
                        CoreInit.modules.Add(mod);

                        mvcBuilder.AddApplicationPart(mod.assembly);
                        WatchersDynamicModule(null, mvcBuilder, mod, build.path);
                    }
                    #endregion
                }
            }

            Console.WriteLine();
            #endregion

            #region modules configure
            foreach (var mod in CoreInit.modules)
            {
                try
                {
                    var initType = mod.assembly.GetTypes()
                        .FirstOrDefault(t => typeof(IModuleConfigure).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

                    if (initType != null)
                    {
                        if (Activator.CreateInstance(initType) is not IModuleConfigure confInstance)
                            continue;

                        confInstance.Configure(new ConfigureModel()
                        {
                            mvcBuilder = mvcBuilder,
                            services = services
                        });
                    }
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"Configure module {mod.name}: {ex.Message}\n\n");
                    throw new Exception();
                }
            }
            #endregion
        }
        #endregion


        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IMemoryCache memory, IHttpClientFactory httpClientFactory, IHostApplicationLifetime applicationLifetime)
        {
            _app = app;
            memoryCache = memory;
            var init = CoreInit.conf;
            var mods = init.BaseModule;
            var midd = mods.Middlewares;

            Shared.Startup.Configure(app, memory);
            HybridCache.Configure(memory);
            HybridFileCache.Configure(memory);
            ProxyManager.Configure(memory);

            Http.httpClientFactory = httpClientFactory;

            #region Application Started / Stopping
            applicationLifetime.ApplicationStopping.Register(OnShutdown);

            applicationLifetime.ApplicationStarted.Register(() =>
            {
                if (!string.IsNullOrEmpty(init.listen.sock))
                    _ = Bash.ComandAsync($"while [ ! -S /var/run/{init.listen.sock}.sock ]; do sleep 1; done && chmod 666 /var/run/{init.listen.sock}.sock").ConfigureAwait(false);
            });
            #endregion

            #region modules loaded
            foreach (var mod in CoreInit.modules)
            {
                try
                {
                    LoadedModule(app, mod);
                    Console.WriteLine($"loaded module: {mod.name}");
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"Module {mod.name}: {ex.Message}\n\n");
                    throw new Exception();
                }
            }
            Console.WriteLine("\n");
            #endregion

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            File.WriteAllText("current.conf", JsonConvert.SerializeObject(CoreInit.CurrentConf, Formatting.Indented));

            #region UseExceptionHandler
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    string targetLog = CoreInit.conf.exceptionHandlerLogTarget;
                    if (string.IsNullOrEmpty(targetLog) || targetLog == "none")
                        return;

                    var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                    var exception = exceptionFeature?.Error;

                    var sb = StringBuilderPool.Rent();

                    try
                    {
                        sb.AppendLine("\n[GlobalError]");
                        sb.AppendLine($"Time: {DateTime.Now.ToString()}");
                        sb.AppendLine($"Path: {context.Request.Path}");
                        sb.AppendLine($"Method: {context.Request.Method}");
                        sb.AppendLine($"TraceId: {context.TraceIdentifier}");

                        if (exception != null)
                        {
                            sb.AppendLine($"Message: {exception.Message}");
                            sb.AppendLine($"StackTrace: {exception.StackTrace}");
                        }

                        if (targetLog == "file")
                        {
                            try
                            {
                                if (_exceptionLogWriter == null)
                                {
                                    lock (_exceptionLogInitLock)
                                    {
                                        if (_exceptionLogWriter == null)
                                        {
                                            _exceptionLogFileStream = new FileStream(init.exceptionHandlerLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, bufferSize: PoolInvk.bufferSize, options: FileOptions.Asynchronous);
                                            _exceptionLogWriter = new StreamWriter(_exceptionLogFileStream, Encoding.UTF8, PoolInvk.bufferSize, leaveOpen: true) { AutoFlush = true };
                                        }
                                    }
                                }

                                await _exceptionLogWriter.WriteAsync(sb.ToString());
                            }
                            catch (System.Exception ex)
                            {
                                Serilog.Log.Error(ex, "{Class} {CatchId}", "Startup", "id_v7q7awx1");
                            }
                        }
                        else
                        {
                            Console.WriteLine(sb.ToString());
                        }
                    }
                    finally
                    {
                        StringBuilderPool.Return(sb);
                    }

                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"Internal server error\"}", context.RequestAborted);
                });
            });
            #endregion

            if (init.useDeveloperExceptionPage)
                app.UseDeveloperExceptionPage();

            #region UseForwardedHeaders
            var forwarded = new ForwardedHeadersOptions
            {
                ForwardLimit = null,
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };

            if (init.KnownProxies != null && init.KnownProxies.Count > 0)
            {
                foreach (var k in init.KnownProxies)
                    forwarded.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse(k.ip), k.prefixLength));
            }

            app.UseForwardedHeaders(forwarded);
            #endregion

            app.UseBaseMod();
            app.UseModHeaders();
            app.UseRequestInfo();

            if (mods.nws)
            {
                app.Map("/nws", nwsApp =>
                {
                    nwsApp.UseWAF();
                    nwsApp.UseWebSockets();
                    nwsApp.Run(NativeWebSocket.HandleWebSocketAsync);
                });
            }

            app.UseRouting();

            if (init.listen.compression)
                app.UseResponseCompression();

            if (init.Staticache.enable)
                app.UseStaticache();

            if (midd.anonymousRequest)
                app.UseAnonymousRequest();

            if (EventListener.Middleware != null)
                app.UseModule(first: true);

            #region UseOverrideResponse
            if (CoreInit.conf.overrideResponse?.Count > 0)
            {
                if (CoreInit.conf.overrideResponse.FirstOrDefault(i => i.firstEndpoint) != null)
                    app.UseOverrideResponse(first: true);
            }
            #endregion

            #region proxy
            if (midd.proxy)
            {
                app.MapWhen(context => context.Request.Path.Value.StartsWith("/proxy/") || context.Request.Path.Value.StartsWith("/proxy-dash/"), proxyApp =>
                {
                    proxyApp.UseProxyAPI();
                });
            }

            if (midd.proxyimg)
            {
                app.MapWhen(context => context.Request.Path.Value.StartsWith("/proxyimg"), proxyApp =>
                {
                    proxyApp.UseProxyIMG();
                });
            }
            #endregion

            #region UseStaticFiles
            if (midd.staticFiles)
            {
                var contentTypeProvider = new FileExtensionContentTypeProvider();

                if (midd.staticFilesMappings != null)
                {
                    foreach (var mapping in midd.staticFilesMappings)
                        contentTypeProvider.Mappings[mapping.Key] = mapping.Value;
                }

                app.UseStaticFiles(new StaticFileOptions
                {
                    ServeUnknownFileTypes = midd.unknownStaticFiles,
                    DefaultContentType = "application/octet-stream",
                    ContentTypeProvider = contentTypeProvider
                });
            }
            #endregion

            if (init.WAF.enable)
                app.UseWAF();

            app.UseAuthorization();
            app.UseAccsdb();

            if (EventListener.Middleware != null)
                app.UseModule(first: false);

            #region UseOverrideResponse
            if (CoreInit.conf.overrideResponse?.Count > 0)
            {
                if (CoreInit.conf.overrideResponse.FirstOrDefault(i => i.firstEndpoint == false) != null)
                    app.UseOverrideResponse(first: false);
            }
            #endregion

            if (init.listen.LimitHttpRequests > 0)
                app.UseLimitHttpRequests();

            if (init.openstat.enable)
                app.UseResponseAvgStatistics();

            if (init.Staticache.enable)
                app.UseStaticacheWriter();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapRchApi();
            });
        }


        #region OnShutdown
        void OnShutdown()
        {
            if (Program._reload)
                return;

            IsShutdown = true;
            Shared.Startup.IsShutdown = true;

            Task.WaitAll([
                Task.Run(Chromium.FullDispose),
                Task.Run(Firefox.FullDispose),
                Task.Run(NativeWebSocket.FullDispose),
                Task.Run(() => DisposeModule(null))
            ]);
        }
        #endregion

        #region WatchRebuildModule
        static readonly Dictionary<string, FileSystemWatcher> moduleWatchers = new();

        static readonly object moduleWatcherLock = new object();

        void WatchersDynamicModule(IApplicationBuilder app, IMvcBuilder mvcBuilder, RootModule mod, string path)
        {
            if (!mod.dynamic)
                return;

            path = Path.GetFullPath(path);

            lock (moduleWatcherLock)
            {
                if (moduleWatchers.ContainsKey(path))
                    return;

                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };

                watcher.Filters.Add("*.cs");
                watcher.Filters.Add("manifest.json");

                CancellationTokenSource debounceCts = null;
                object debounceLock = new object();

                void Recompile(object sender, FileSystemEventArgs e)
                {
                    string _file = e.FullPath.Replace("\\", "/").Replace(path.Replace("\\", "/"), "").Replace(Environment.CurrentDirectory.Replace("\\", "/"), "");
                    if (Regex.IsMatch(_file, "(\\.vs|bin|obj|Properties)/", RegexOptions.IgnoreCase))
                        return;

                    CancellationTokenSource cts;

                    lock (debounceLock)
                    {
                        debounceCts?.Cancel();
                        debounceCts = new CancellationTokenSource();
                        cts = debounceCts;
                    }

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

                        if (cts.IsCancellationRequested)
                            return;

                        watcher.EnableRaisingEvents = false;

                        try
                        {
                            var parts = mvcBuilder.PartManager.ApplicationParts
                                .OfType<AssemblyPart>()
                                .Where(p => p.Assembly == mod.assembly)
                                .ToList();

                            #region update manifest.json
                            string manifestPath = Path.Combine(path, "manifest.json");
                            RootModule manifestMod = null;

                            if (File.Exists(manifestPath))
                            {
                                try
                                {
                                    manifestMod = JsonConvert.DeserializeObject<RootModule>(File.ReadAllText(manifestPath));
                                    mod.enable = manifestMod.enable;
                                }
                                catch (Exception manifestEx)
                                {
                                    Serilog.Log.Error(manifestEx, "CatchId={CatchId}", "id_00a4305e");
                                    Console.WriteLine($"Failed to update manifest for {mod.name}: {manifestEx.Message}");
                                }
                            }
                            #endregion

                            var build = CSharpEval.Compilation(mod);
                            if (build.assembly != null)
                            {
                                DisposeModule(mod);

                                foreach (var part in parts)
                                    mvcBuilder.PartManager.ApplicationParts.Remove(part);

                                mod.assembly = build.assembly;
                                mod.assemblyLoadContext = build.alc;

                                LoadedModule(app, mod);

                                mvcBuilder.PartManager.ApplicationParts.Add(new AssemblyPart(mod.assembly));
                                DynamicActionDescriptorChangeProvider.Instance.NotifyChanges();

                                OnlineModuleEntry.EnsureCache(forced: true);
                                SisiModuleEntry.EnsureCache(forced: true);

                                Console.WriteLine("rebuild module: " + mod.name);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_64a00701");
                            Console.WriteLine($"Failed to rebuild module {mod.name}: {ex.Message}");
                        }
                        finally
                        {
                            watcher.EnableRaisingEvents = true;
                        }
                    });
                }

                watcher.Changed += Recompile;
                watcher.Created += Recompile;
                watcher.Deleted += Recompile;
                watcher.Renamed += Recompile;

                watcher.EnableRaisingEvents = true;
                moduleWatchers[path] = watcher;
            }
        }
        #endregion

        #region LoadedModule
        void LoadedModule(IApplicationBuilder app, RootModule mod)
        {
            if (mod == null)
                return;

            // Ищем тип, который реализует IModuleLoaded
            var initType = mod.assembly.GetTypes()
                .FirstOrDefault(t => typeof(IModuleLoaded).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

            if (initType == null)
                return; // или лог

            // Создаем экземпляр
            if (Activator.CreateInstance(initType) is not IModuleLoaded initInstance)
                return;

            // Вызываем интерфейсный метод
            initInstance.Loaded(new InitspaceModel()
            {
                path = $"{mod.path}/{mod.name}",
                nws = new NativeWebSocket(),
                memoryCache = memoryCache,
                configuration = Configuration,
                services = serviceCollection,
                app = app ?? _app
            });
        }
        #endregion

        #region DisposeModule
        void DisposeModule(RootModule module)
        {
            void Dispose(RootModule mod)
            {
                // Ищем тип, который реализует IModuleLoaded
                var initType = mod.assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IModuleLoaded).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

                if (initType == null)
                    return; // или лог

                // Создаем экземпляр
                if (Activator.CreateInstance(initType) is not IModuleLoaded initInstance)
                    return;

                initInstance.Dispose();
            }

            if (module != null)
            {
                Dispose(module);

                module.assemblyLoadContext?.Unload();
                module.assemblyLoadContext = null;
                module.assembly = null;
            }
            else
            {
                if (CoreInit.modules?.Count > 0)
                {
                    foreach (var mod in CoreInit.modules)
                        Dispose(mod);
                }
            }
        }
        #endregion
    }
}
