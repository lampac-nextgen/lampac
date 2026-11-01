#:sdk Microsoft.NET.Sdk.Web
#:project ../Core.csproj

// .NET 10 self-check: dotnet run --file Core/tests/proxyapi-handlers.cs
// Uses the real middleware with an in-memory HTTP transport; no server or provider is contacted.
using Core.Middlewares;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shared;
using Shared.Models.Base;
using Shared.Models.Events;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

var originalDirectory = Environment.CurrentDirectory;
var directory = Path.Combine(Path.GetTempPath(), "proxyapi-check-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
Environment.CurrentDirectory = directory;
var originalLogger = Log.Logger;
var sink = new ErrorSink();
Log.Logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
var transport = new MemoryFactory();
int failed = 0;
try
{
    CoreInit.conf.serverproxy.enable = true;
    CoreInit.conf.serverproxy.verifyip = false;
    CoreInit.conf.serilog = true;
    Http.httpClientFactory = transport;

    foreach (bool dash in new[] { false, true })
    foreach (bool debug in new[] { false, true })
    {
        await Check($"cancelled request: dash={dash}, showOrigUri={debug}", async () =>
        {
            CoreInit.conf.serverproxy.showOrigUri = debug;
            int laterHandlers = 0;
            // Spectre/Phantom use null to reject a request whose session state is missing.
            EventListener.ProxyApiCreateHttpRequest = e =>
            {
                e.requestMessage.RequestUri = null;
                return Task.CompletedTask;
            };
            EventListener.ProxyApiCreateHttpRequest += e =>
            {
                laterHandlers++;
                return Task.CompletedTask;
            };
            var context = await Invoke("https://video.example/", dash);
            Require(context.Response.StatusCode == 502, $"expected 502, got {context.Response.StatusCode}");
            Require(transport.Sends.Count == 0, "cancelled request reached transport");
            Require(laterHandlers == 0, "handler chain continued after cancellation");
        });

        await Check($"valid request: dash={dash}, showOrigUri={debug}", async () =>
        {
            CoreInit.conf.serverproxy.showOrigUri = debug;
            EventListener.ProxyApiCreateHttpRequest = e =>
            {
                e.requestMessage.Headers.TryAddWithoutValidation("X-Check", "kept");
                return Task.CompletedTask;
            };
            var context = await Invoke("https://video.example/", dash);
            Require(context.Response.StatusCode == 200, $"expected 200, got {context.Response.StatusCode}");
            Require(transport.Sends.Count == 1, "valid request did not reach transport exactly once");
            Require(transport.Sends[0] == "proxy", "wrong HTTP client");
            Require(context.Response.Body.Length == 3, "response body was not copied");
            Require(transport.SawHeader, "module headers were lost");
        });
    }

    foreach (bool cancelPrimary in new[] { false, true })
    {
        await Check($"OR selection: cancelPrimary={cancelPrimary}", async () =>
        {
            CoreInit.conf.serverproxy.showOrigUri = false;
            EventListener.ProxyApiCreateHttpRequest = e =>
            {
                if (cancelPrimary && e.uri.Host == "primary.example")
                    e.requestMessage.RequestUri = null;
                return Task.CompletedTask;
            };
            var context = await Invoke("https://primary.example/video.ts or https://fallback.example/video.ts", false);
            Require(context.Response.StatusCode == 302, $"expected redirect, got {context.Response.StatusCode}");
            var location = new Uri(context.Response.Headers.Location.ToString());
            var selected = ProxyLink.Decrypt(location.AbsolutePath.AsSpan(7), "192.0.2.1");
            string expected = cancelPrimary ? "https://fallback.example/video.ts" : "https://primary.example/video.ts";
            Require(selected?.uri == expected, "wrong OR destination");
            Require(transport.Sends.Count == (cancelPrimary ? 0 : 1), "unexpected send after OR redirect/cancellation");
            if (!cancelPrimary)
                Require(transport.Sends[0] == "base", "OR probe used wrong client");
        });
    }

    Console.WriteLine($"ProxyAPI self-check: {10 - failed}/10 passed");
}
finally
{
    EventListener.ProxyApiCreateHttpRequest = null;
    Http.httpClientFactory = null;
    Log.Logger = originalLogger;
    Environment.CurrentDirectory = originalDirectory;
    Directory.Delete(directory, true);
}
return failed == 0 ? 0 : 1;

async Task Check(string name, Func<Task> check)
{
    transport.Sends.Clear();
    transport.SawHeader = false;
    sink.Errors.Clear();
    try
    {
        await check();
        Require(sink.Errors.Count == 0, "middleware logged an exception: " + string.Join(", ", sink.Errors));
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine("FAIL " + name + ": " + ex.Message);
        foreach (string error in sink.Errors)
            Console.WriteLine("  logged: " + error);
    }
}

static async Task<DefaultHttpContext> Invoke(string url, bool dash)
{
    var context = new DefaultHttpContext();
    context.Request.Method = "GET";
    context.Request.Scheme = "http";
    context.Request.Host = new HostString("proxy.example");
    context.Features.Set(new RequestModel { IP = "192.0.2.1" });
    context.Response.Body = new MemoryStream();
    string key = ProxyLink.Encrypt(url, "192.0.2.1", plugin: "selfcheck", verifyip: false, forceMd5: true);
    context.Request.Path = dash ? "/proxy-dash/" + key + "/segment.m4s" : "/proxy/" + key;
    await new ProxyAPI(_ => Task.CompletedTask).InvokeAsync(context);
    return context;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed class ErrorSink : ILogEventSink
{
    public List<string> Errors { get; } = new();
    public void Emit(LogEvent e)
    {
        if (e.Level >= LogEventLevel.Error)
            Errors.Add(e.Exception?.GetType().Name ?? "error");
    }
}

sealed class MemoryFactory : IHttpClientFactory
{
    public List<string> Sends { get; } = new();
    public bool SawHeader;
    public HttpClient CreateClient(string name) => new(new MemoryTransport(this, name));

    sealed class MemoryTransport(MemoryFactory owner, string name) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner.Sends.Add(name);
            owner.SawHeader = request.Headers.Contains("X-Check");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            });
        }
    }
}
