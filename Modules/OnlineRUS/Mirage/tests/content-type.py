#!/usr/bin/env python3
"""Python 3 + .NET 10 self-check of the real Trans action; no provider/browser/network I/O."""
import pathlib
import subprocess
import tempfile

MODULE = pathlib.Path(__file__).resolve().parents[1]
source = (MODULE / "Controller.cs").read_text(encoding="utf-8")
marker = "    async public Task<ActionResult> Trans(string fileName)\n"
assert source.count(marker) == 1, "Trans action not found uniquely"
action = marker + source.split(marker, 1)[1].split("    #endregion", 1)[0]

CHECK = r'''
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

var services = new ServiceCollection();
services.AddLogging();
services.AddControllers();
using var provider = services.BuildServiceProvider();
Directory.CreateDirectory("cache/mirage");
byte[] payload = { 0, 1, 2, 127, 128, 255, 13, 10 };
int checks = 0, failed = 0;

foreach (var (name, mime) in new[]
{
    ("master.m3u8", "application/vnd.apple.mpegurl"),
    ("index-f1-v1-a1.m3u8", "application/vnd.apple.mpegurl"),
    ("legacy.m3u", "application/vnd.apple.mpegurl"),
    ("init.mp4", "video/mp4"),
    ("init-f1-v1-a1.mp4", "video/mp4"),
    ("seg-1-f1-v1-a1.m4s", "video/iso.segment"),
    ("seg-2.ts", "video/MP2T"),
    ("unknown.bin", "video/MP2T")
})
{
    File.WriteAllBytes("cache/mirage/" + name, payload);
    foreach (string request in new[] { "GET", "HEAD", "GET Range" })
    {
        await Check(request + " " + name, async () =>
        {
            var (controller, context, body) = Controller(request);
            var result = await controller.Trans(name);
            Require(result is FileStreamResult, "not a FileStreamResult");
            await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));
            Require(context.Response.ContentType == mime, "Content-Type: " + context.Response.ContentType + ", expected " + mime);
            Require(context.Response.StatusCode == 200, "status changed");
            Require(context.Response.ContentLength == payload.Length, "length changed");
            Require(body.ToArray().SequenceEqual(request == "HEAD" ? Array.Empty<byte>() : payload), "body bytes changed");
            // This action does not enable range processing; keep that existing behavior.
            Require(!context.Response.Headers.ContainsKey("Content-Range"), "range behavior changed");
        });
    }
}

foreach (string invalid in new[] { "../init.mp4", "UPPER.mp4", "init..mp4" })
{
    await Check("reject " + invalid, async () =>
    {
        var (controller, _, _) = Controller("GET");
        var result = await controller.Trans(invalid);
        Require(result is BadRequestResult, "filename validation changed");
    });
}
await Check("blocked request", async () =>
{
    var (controller, _, _) = Controller("GET");
    controller.Blocked = true;
    var result = await controller.Trans("init.mp4");
    Require(result is StatusCodeResult { StatusCode: 403 }, "blocked request reached media response");
});
Console.WriteLine($"Mirage Trans self-check: {checks - failed}/{checks} passed");
return failed == 0 ? 0 : 1;

(ProbeController, DefaultHttpContext, MemoryStream) Controller(string request)
{
    var body = new MemoryStream();
    var context = new DefaultHttpContext { RequestServices = provider };
    context.Request.Method = request == "HEAD" ? "HEAD" : "GET";
    if (request == "GET Range") context.Request.Headers.Range = "bytes=1-3";
    context.Response.Body = body;
    var controller = new ProbeController { ControllerContext = new ControllerContext { HttpContext = context } };
    return (controller, context, body);
}
async Task Check(string name, Func<Task> check)
{
    checks++;
    try { await check(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

class ProbeController : Controller
{
    public bool Blocked;
    (DateTime lastreq, int lastseek) curenthsl;
    Browser page = new();
    ActionResult badInitMsg => StatusCode(403);
    Task<bool> IsRequestBlocked(bool rch) => Task.FromResult(Blocked);

/* TRANS_ACTION */

    class Browser
    {
        public Task EvaluateAsync(string script) => throw new InvalidOperationException("Unexpected browser access");
    }
}
'''

with tempfile.TemporaryDirectory(prefix="mirage-content-type-") as temporary:
    directory = pathlib.Path(temporary)
    check = directory / "check.cs"
    check.write_text(
        "#:sdk Microsoft.NET.Sdk.Web\n#:property StaticWebAssetsEnabled=false\n"
        "#:property PublishAot=false\n#:property PublishSingleFile=false\n"
        + CHECK.replace("/* TRANS_ACTION */", action), encoding="utf-8",
    )
    result = subprocess.run(["dotnet", "run", "--file", str(check)], cwd=directory)
    raise SystemExit(result.returncode)
