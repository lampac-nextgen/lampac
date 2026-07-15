using GStreamer.Services;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GStreamer;

public class ModInit : IModuleLoaded
{
    public static string modpath;
    public static ModuleConf conf;
    public static string gstRootPath;
    static double? gstVersion;

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;

        string cachePath = Path.Combine("cache", "gstranscoding");

        if (Directory.Exists(cachePath))
        {
            try
            {
                Directory.Delete(cachePath, true);
            }
            catch { }
        }

        Directory.CreateDirectory(cachePath);

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);

        SetupGStreamer();

        gstVersion = ReadGstVersion();
        if (gstVersion.HasValue)
            conf.gst_version = gstVersion.Value;

        InitGst();
        if (conf.useGpu)
        {
            HardwareVideoBackend.Initialize();
            HdrToneMappingBackend.Initialize();
        }
    }

    public void Dispose()
    {
        GService.Dispose();
        EventListener.UpdateInitFile -= updateConf;
    }

    void updateConf()
    {
        conf = ModuleInvoke.Init("gst", new ModuleConf()
        {
            gst_version = OperatingSystem.IsWindows() ? 1.28 : 1.22,
            PATH = @"C:\Program Files\gstreamer\1.0\mingw_x86_64",
            inactiveMinutes = 10,
            limit_map = new List<WafLimitRootMap>()
            {
                new("^/gst/", new WafLimitMap { limit = 50, second = 1 })
            }
        });

        if (gstVersion.HasValue)
            conf.gst_version = gstVersion.Value;
    }


    static void InitGst()
    {
        Gst.Module.Initialize();
        GstApp.Module.Initialize();

        var gstArgs = Array.Empty<string>();
        Gst.Functions.Init(ref gstArgs);
    }

    static void SetupGStreamer()
    {
        string registryPath = Path.Combine(
            AppContext.BaseDirectory,
            "cache",
            "gstreamer-registry.bin"
        );

        Environment.SetEnvironmentVariable(
            "GST_REGISTRY",
            registryPath,
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "GST_REGISTRY_1_0",
            registryPath,
            EnvironmentVariableTarget.Process
        );

        if (!OperatingSystem.IsWindows())
            return;

        string gstRoot = conf.PATH;
        string gstBin = string.IsNullOrWhiteSpace(gstRoot)
            ? null
            : Path.Combine(gstRoot, "bin");

        if (gstBin == null ||
            !File.Exists(Path.Combine(gstBin, "gst-discoverer-1.0.exe")))
        {
            gstRoot = Path.Combine(modpath, "gst-libs", "win-x86_64");
            gstBin = Path.Combine(gstRoot, "bin");
        }

        if (!Directory.Exists(gstBin))
            return;

        gstRootPath = gstRoot;
        conf.PATH = gstRoot;

        string gstPlugins = Path.Combine(gstRoot, "lib", "gstreamer-1.0");
        string gstPluginScanner = Path.Combine(
            gstRoot,
            "libexec",
            "gstreamer-1.0",
            "gst-plugin-scanner.exe"
        );

        Environment.SetEnvironmentVariable(
            "GSTREAMER_1_0_ROOT_MINGW_X86_64",
            gstRoot,
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "GST_PLUGIN_SYSTEM_PATH_1_0",
            gstPlugins,
            EnvironmentVariableTarget.Process
        );

        Environment.SetEnvironmentVariable(
            "GST_PLUGIN_SCANNER_1_0",
            gstPluginScanner,
            EnvironmentVariableTarget.Process
        );

        string gioModules = Path.Combine(gstRoot, "lib", "gio", "modules");
        if (Directory.Exists(gioModules))
        {
            Environment.SetEnvironmentVariable(
                "GIO_EXTRA_MODULES",
                gioModules,
                EnvironmentVariableTarget.Process
            );
        }

        var currentPath = Environment.GetEnvironmentVariable("PATH");

        Environment.SetEnvironmentVariable(
            "PATH",
            string.IsNullOrEmpty(currentPath)
                ? gstBin
                : $"{gstBin}{Path.PathSeparator}{currentPath}",
            EnvironmentVariableTarget.Process
        );

        //Environment.SetEnvironmentVariable(
        //    "GST_DEBUG",
        //    "souphttpsrc:6,matroskademux:5,h264parse:4," +
        //    "hlssink3:4,splitmuxsink:4,mpegtsmux:4,*:2",
        //    EnvironmentVariableTarget.Process
        //);
    }

    static double? ReadGstVersion()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows()
                        ? Path.Combine(
                            gstRootPath ?? conf.PATH,
                            "bin",
                            "gst-inspect-1.0.exe"
                        )
                        : "gst-inspect-1.0",

                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(3000))
            {
                process.Kill(true);
                return null;
            }

            foreach (string output in new string[] { process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd() })
            {
                var match = Regex.Match(output, @"(?:GStreamer|version)\s+(\d+)\.(\d+)(?:\.\d+)?", RegexOptions.IgnoreCase);
                if (!match.Success)
                    return null;

                string major = match.Groups[1].Value;
                string minor = match.Groups[2].Value.PadLeft(2, '0');

                if (double.TryParse($"{major}.{minor}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double version))
                    return version;

                return null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
