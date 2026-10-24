using LampacMcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

LampacConfig config;
try
{
    config = LampacConfig.FromEnvironment();
}
catch (InvalidOperationException exception)
{
    Console.Error.WriteLine($"lampac-mcp: {exception.Message}");
    return 2;
}

if (config.Transport == "stdio")
{
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureLogging(builder.Logging);
    RegisterServices(builder.Services, config);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<LampacTools>();
    await builder.Build().RunAsync();
    return 0;
}

if (config.Transport == "streamable-http")
{
    var builder = WebApplication.CreateBuilder(args);
    // Bind loopback only; remote clients must go through an authenticated proxy.
    builder.WebHost.UseUrls($"http://127.0.0.1:{config.Port}");
    builder.WebHost.UseSetting("AllowedHosts", "localhost;127.0.0.1;[::1]");
    ConfigureLogging(builder.Logging);
    RegisterServices(builder.Services, config);
    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
        .WithTools<LampacTools>();

    var app = builder.Build();
    app.MapMcp("/mcp");
    Console.Error.WriteLine(
        $"lampac-mcp: streamable HTTP on http://127.0.0.1:{config.Port}/mcp (local-only)");
    await app.RunAsync();
    return 0;
}

Console.Error.WriteLine($"lampac-mcp: unsupported MCP transport \"{config.Transport}\"");
return 2;

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.SetMinimumLevel(LogLevel.Warning);
    logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
}

static void RegisterServices(IServiceCollection services, LampacConfig config)
{
    services.AddSingleton(config);
    services.AddSingleton<PanelClient>();
    services.AddSingleton<ModuleStore>();
}
