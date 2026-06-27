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
    static double? gstVersion;

    public void Loaded(InitspaceModel initspace)
    {
        modpath = initspace.path;

        string cachePath = Path.Combine("cache", "gstranscoding");
        Directory.CreateDirectory(cachePath);

        foreach (string file in Directory.GetFiles(cachePath))
        {
            try
            {
                File.Delete(file);
            }
            catch { }
        }

        updateConf();
        EventListener.UpdateInitFile += updateConf;

        foreach (var m in conf.limit_map)
            CoreInit.conf.WAF.limit_map.Insert(0, m);

        gstVersion = ReadGstVersion();
        if (gstVersion.HasValue)
            conf.gst_version = gstVersion.Value;

        InitGst();
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
        SetupGStreamer();

        Gst.Module.Initialize();
        GstApp.Module.Initialize();

        var gstArgs = Array.Empty<string>();
        Gst.Functions.Init(ref gstArgs);
    }

    static void SetupGStreamer()
    {
        Environment.SetEnvironmentVariable(
            "GST_REGISTRY",
            Path.Combine(
                AppContext.BaseDirectory,
                "cache",
                "gstreamer-registry.bin"
            ),
            EnvironmentVariableTarget.Process
        );

        if (!OperatingSystem.IsWindows())
            return;

        var gstBin = Path.Combine(conf.PATH, "bin");
        if (!Directory.Exists(gstBin))
        {
            gstBin = Path.Combine(modpath, "gst-libs", "win-x86_64", "bin");
            if (!Directory.Exists(gstBin))
                return;
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
                        ? Path.Combine(conf.PATH, "bin", "gst-inspect-1.0.exe")
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
