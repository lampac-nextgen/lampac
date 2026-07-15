using Gst;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace GStreamer.Services;

internal static class HdrToneMappingBackend
{
    public const string ElementName = "hdrtonemap";
    public const string UnavailableError = "HDR tone mapping backend is not available";

    static readonly Lazy<bool> available = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool IsAvailable => available.Value;

    public static void Initialize()
    {
        if (IsAvailable)
        {
            Serilog.Log.Information(
                "GStreamer HDR tone mapping backend initialized with automatic OpenCL/CPU selection."
            );
        }
        else
        {
            Serilog.Log.Information(
                "GStreamer HDR tone mapping backend is unavailable; HDR-to-SDR requests will be rejected."
            );
        }
    }

    static bool Probe()
    {
        try
        {
            if (CanCreateElement())
                return true;

            string runtimeId = GetRuntimeId();
            if (runtimeId == null)
                return false;

            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string moduleNativeDirectory = string.IsNullOrWhiteSpace(ModInit.modpath)
                ? null
                : Path.Combine(ModInit.modpath, "native");
            string[] roots = { AppContext.BaseDirectory, assemblyDirectory, moduleNativeDirectory };
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                string pluginDirectory = Path.Combine(root, "runtimes", runtimeId, "native", "gstreamer-1.0");
                if (Directory.Exists(pluginDirectory))
                    Registry.Get().ScanPath(pluginDirectory);
            }

            return CanCreateElement();
        }
        catch
        {
            return false;
        }
    }

    static bool HasFactory()
    {
        using ElementFactory factory = ElementFactory.Find(ElementName);
        return factory != null;
    }

    static bool CanCreateElement()
    {
        if (!HasFactory())
            return false;

        using Element element = ElementFactory.Make(ElementName, "hdrtonemap_startup_probe");
        return element != null;
    }

    static string GetRuntimeId()
    {
        string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : null;
        if (os == null)
            return null;

        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };

        return architecture == null ? null : $"{os}-{architecture}";
    }
}
