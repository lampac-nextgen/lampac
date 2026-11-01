#!/usr/bin/env python3
"""Run with Python 3 and .NET 10; uses the real Video action without browser/network I/O."""
import pathlib
import subprocess
import tempfile

MODULE = pathlib.Path(__file__).resolve().parents[1]

CHECK = r'''
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Caching.Memory;
using Phantom;
using Shared;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

// Compile the actual controller, substituting only its private browser loader.
// The Video action, its cache checks, and the Shared base classes are unchanged.
var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(args[0]));
var root = tree.GetRoot();
var loader = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "goMovie");
root = root.ReplaceNode(loader, loader.WithBody(SyntaxFactory.Block(
    SyntaxFactory.ParseStatement("return await global::PlaybackProbe.Load();"))));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Concat(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
    .Append(Assembly.GetExecutingAssembly().Location)
    .Distinct().Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("PhantomCacheCheck", new[] { CSharpSyntaxTree.Create((CSharpSyntaxNode)root) },
    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var assemblyBytes = new MemoryStream();
var emitted = compilation.Emit(assemblyBytes);
if (!emitted.Success)
    throw new Exception(string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
var controllerType = Assembly.Load(assemblyBytes.ToArray()).GetType("Phantom.PhantomController")!;
var video = controllerType.GetMethod("Video", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Video action not found");

ModInit.conf = new ModuleConf("Phantom", "https://api.example", "https://player.example", "", "", true)
{
    enable = true,
    streamproxy = true
};
CoreInit.conf.serverproxy.enable = true;
CoreInit.conf.serverproxy.verifyip = false;
var hybrid = DispatchProxy.Create<IHybridCache, RecordingCache>();
var cache = (RecordingCache)(object)hybrid;
HybridCache.Configure(hybrid);
int failed = 0;
int checks = 0;

var incomplete = new (string name, (StreamData watch, List<StreamQualityDto> streams) value)[]
{
    ("missing hash", (new StreamData(), Streams())),
    ("empty hash", (new StreamData { edge_hash = "" }, Streams())),
    ("missing watch", (null!, Streams())),
    ("missing streams", (Watch(), null!)),
    ("empty streams", (Watch(), new())),
    ("default loader result", default)
};
foreach (var item in incomplete)
{
    await Check("fresh " + item.name, async () =>
    {
        PlaybackProbe.Next = item.value;
        var (first, context) = await Invoke();
        Require(context.Response.StatusCode == 503 && first is not RedirectResult, "incomplete playback was not rejected");
        Require(cache.Writes == 0 && cache.Items.Count == 0, "incomplete result was cached");
        Require(PlaybackProbe.Calls == 1, "loader was not called");

        // A subsequent successful attempt must not be stuck with the failed result for 10 minutes.
        PlaybackProbe.Next = (Watch(), Streams());
        await RequirePlayback();
        Require(PlaybackProbe.Calls == 2 && cache.Writes == 1, "recovery did not load/cache valid state");
        await RequirePlayback();
        Require(PlaybackProbe.Calls == 2 && cache.Writes == 1, "valid cache entry was not reused");
        Require(cache.Expires > DateTimeOffset.Now.AddMinutes(9) && cache.Expires <= DateTimeOffset.Now.AddMinutes(10), "cache lifetime changed");
    });

    await Check("cached " + item.name, async () =>
    {
        cache.Items[PlaybackProbe.Key] = item.value;
        PlaybackProbe.Next = (Watch(), Streams());
        await RequirePlayback();
        Require(PlaybackProbe.Calls == 1 && cache.Writes == 1, "invalid cache entry bypassed loader/was not replaced");
        await RequirePlayback();
        Require(PlaybackProbe.Calls == 1, "replacement cache entry was not reused");
    });
}
await Check("valid cached playback", async () =>
{
    cache.Items[PlaybackProbe.Key] = (Watch(), Streams());
    PlaybackProbe.Next = default;
    await RequirePlayback();
    Require(PlaybackProbe.Calls == 0 && cache.Writes == 0, "valid cache entry unnecessarily refreshed");
});
await Check("real file backend rejects incomplete state and reuses recovered state", async () =>
{
    using var memory = new MemoryCache(new MemoryCacheOptions());
    string originalType = CoreInit.conf.cache.type;
    int originalExtend = CoreInit.conf.cache.extend;
    try
    {
        CoreInit.conf.cache.type = "fdb";
        CoreInit.conf.cache.extend = 60;
        HybridFileCache.Configure(memory);
        var realCache = new HybridFileCache();
        HybridCache.Configure(realCache);

        PlaybackProbe.Next = (new StreamData(), Streams());
        var (rejected, rejectedContext) = await Invoke();
        Require(rejectedContext.Response.StatusCode == 503 && rejected is not RedirectResult, "incomplete result accepted");
        Require(!realCache.TryGetValue(PlaybackProbe.Key, out (StreamData watch, List<StreamQualityDto> streams) _), "incomplete result cached by real backend");

        realCache.Set(PlaybackProbe.Key, (new StreamData(), Streams()), DateTimeOffset.Now.AddMinutes(10));
        Require(HybridFileCache.Stat_ContTempDb > 0, "test did not seed the file backend's temporary database");
        PlaybackProbe.Calls = 0;
        PlaybackProbe.Next = (Watch(), Streams());
        var (recovered, _) = await Invoke();
        Require(recovered is RedirectResult && PlaybackProbe.Calls == 1, "incomplete entry did not trigger recovery");
        Require(realCache.TryGetValue(PlaybackProbe.Key, out (StreamData watch, List<StreamQualityDto> streams) stored)
            && !string.IsNullOrEmpty(stored.watch?.edge_hash), "real backend retained incomplete state after recovery");
        var (cached, _) = await Invoke();
        Require(cached is RedirectResult && PlaybackProbe.Calls == 1, "recovered playback was loaded again instead of reused");
    }
    finally
    {
        HybridCache.Configure(hybrid);
        CoreInit.conf.cache.type = originalType;
        CoreInit.conf.cache.extend = originalExtend;
    }
});
Console.WriteLine($"Phantom cache self-check: {checks - failed}/{checks} passed");
return failed == 0 ? 0 : 1;

async Task Check(string name, Func<Task> check)
{
    checks++;
    cache.Items.Clear();
    cache.Writes = 0;
    PlaybackProbe.Calls = 0;
    try
    {
        await check();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception e)
    {
        failed++;
        Console.WriteLine("FAIL " + name + ": " + e.GetBaseException());
    }
}

async Task<(ActionResult result, DefaultHttpContext context)> Invoke()
{
    var controller = (Controller)Activator.CreateInstance(controllerType)!;
    var context = new DefaultHttpContext();
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("proxy.example");
    context.Request.Path = "/lite/phantom/video";
    context.Features.Set(new RequestModel { IP = "192.0.2.1" });
    controller.ControllerContext = new ControllerContext { HttpContext = context };
    var result = await (Task<ActionResult>)video.Invoke(controller, new object[] { 1L, "self-check", true })!;
    return (result, context);
}

async Task RequirePlayback()
{
    var (result, _) = await Invoke();
    Require(result is RedirectResult, "valid playback did not produce a redirect");
    string link = ((RedirectResult)result).Url;
    Require(!string.IsNullOrEmpty(link), "empty playback link");
    var entry = ((StreamData watch, List<StreamQualityDto> streams))cache.Items[PlaybackProbe.Key];
    Require(!string.IsNullOrEmpty(entry.watch?.edge_hash), "redirect used incomplete cached session");
}

static StreamData Watch() => new() { edge_hash = "test-session", requestOrigin = "https://player.example", requestReferer = "https://player.example/" };
static List<StreamQualityDto> Streams() => new() { new("https://video.example/master.m3u8", "1080p") };
static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

public static class PlaybackProbe
{
    public const string Key = "phantom:1:self-check";
    public static int Calls;
    public static (StreamData watch, List<StreamQualityDto> streams) Next;
    public static Task<(StreamData watch, List<StreamQualityDto> streams)> Load()
    {
        Calls++;
        return Task.FromResult(Next);
    }
}

public class RecordingCache : DispatchProxy
{
    public Dictionary<string, object> Items { get; } = new();
    public int Writes;
    public DateTimeOffset Expires;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string key = (string)args![0]!;
        if (method!.Name == "TryGetValue")
        {
            bool found = Items.TryGetValue(key, out var value);
            args[1] = found ? value : Activator.CreateInstance(method.GetGenericArguments()[0]);
            return found;
        }
        if (method.Name == "Set")
        {
            Writes++;
            Expires = (DateTimeOffset)args[2]!;
            Items[key] = args[1]!;
            return args[1];
        }
        throw new NotSupportedException(method.Name);
    }
}
'''

with tempfile.TemporaryDirectory(prefix="phantom-cache-check-") as temporary:
    directory = pathlib.Path(temporary).resolve()
    check = directory / "check.cs"
    check.write_text(
        "#:sdk Microsoft.NET.Sdk.Web\n#:property StaticWebAssetsEnabled=false\n"
        "#:property PublishAot=false\n#:property PublishSingleFile=false\n#:project "
        + str(MODULE / "Phantom.csproj") + "\n" + CHECK,
        encoding="utf-8",
    )
    artifacts = directory / "artifacts"
    subprocess.run(
        ["dotnet", "build", str(check), "--artifacts-path", str(artifacts), "--nologo"],
        cwd=directory, check=True,
    )
    result = subprocess.run(
        ["dotnet", str(artifacts / "bin/debug/check.dll"), str(MODULE / "Controller.cs")],
        cwd=directory,
    )
    raise SystemExit(result.returncode)
