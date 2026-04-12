using Newtonsoft.Json.Linq;
using Shared;
using Shared.Services;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Shared.Services.Utilities;

namespace TorrServer
{
    public class ModInit : IModuleLoaded
    {
        #region static
        public static string modpath;
        public static ModuleConf conf;
        static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ModInit>();

        static bool IsShutdown;

        public static string tspass = CrypTo.md5(DateTime.Now.ToBinary().ToString());

        public static string homedir;

        public static string tspath;

        public static Process tsprocess;
        #endregion

        #region loaded
        public void Loaded(InitspaceModel baseconf)
        {
            modpath = baseconf.path;

            updateConf();
            EventListener.UpdateInitFile += updateConf;

            foreach (var m in conf.limit_map)
                CoreInit.conf.WAF.limit_map.Insert(0, m);

            CoreInit.BaseModPathWhiteList.Add("/ts/");

            #region homedir
            homedir = Directory.GetCurrentDirectory();
            if (string.IsNullOrWhiteSpace(homedir) || homedir == "/")
                homedir = string.Empty;

            homedir = Path.Combine(homedir, "data", "ts");
            #endregion

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                tspath = Path.Combine(homedir, "TorrServer-windows-amd64.exe");
            else
            {
                tspath = Path.Combine(homedir, "TorrServer-linux");

                if (File.Exists(tspath))
                    Bash.Comand($"chmod +x {tspath}");
            }

            if (!File.Exists(tspath))
            {
                Directory.CreateDirectory(homedir);
                File.Copy($"{modpath}/settings.json", Path.Combine(homedir, "settings.json"), true);
            }

            File.WriteAllText(Path.Combine(homedir, "accs.db"), $"{{\"ts\":\"{tspass}\"}}");

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                #region downloadUrl
                string downloadUrl;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    downloadUrl = "https://github.com/YouROK/TorrServer/releases/latest/download/TorrServer-windows-amd64.exe";
                    if (conf.releases != "latest")
                        downloadUrl = $"https://github.com/YouROK/TorrServer/releases/download/{conf.releases}/TorrServer-windows-amd64.exe";
                }
                else
                {
                    string uname = (await Bash.ComandAsync("uname -m")) ?? string.Empty;
                    string arch = uname.Contains("x86_64") ? "amd64" : (uname.Contains("i386") || uname.Contains("i686")) ? "386" : uname.Contains("aarch64") ? "arm64" : uname.Contains("armv7") ? "arm7" : uname.Contains("armv6") ? "arm5" : "amd64";

                    downloadUrl = "https://github.com/YouROK/TorrServer/releases/latest/download/TorrServer-linux-" + arch;
                    if (conf.releases != "latest")
                        downloadUrl = $"https://github.com/YouROK/TorrServer/releases/download/{conf.releases}/TorrServer-linux-" + arch;
                }
                #endregion

                #region updatet/install
                async Task install()
                {
                    try
                    {
                        if (conf.releases == "latest")
                        {
                            var root = await Http.Get<JObject>("https://api.github.com/repos/YouROK/TorrServer/releases/latest");
                            if (root != null && root.ContainsKey("tag_name"))
                            {
                                string tagname = root.Value<string>("tag_name");
                                if (!string.IsNullOrEmpty(tagname))
                                {
                                    if (!File.Exists($"{homedir}/tagname") || tagname != File.ReadAllText($"{homedir}/tagname"))
                                    {
                                        if (File.Exists(tspath))
                                            File.Delete(tspath);

                                        File.WriteAllText($"{homedir}/tagname", tagname);
                                    }
                                }
                            }
                        }

                        if (!File.Exists(tspath))
                        {
                            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                            {
                                tsprocess?.Dispose();
                                bool success = await Http.DownloadFile(downloadUrl, tspath, timeoutSeconds: 200);
                                if (!success)
                                    File.Delete(tspath);
                            }
                            else
                            {
                                tsprocess?.Dispose();
                                bool success = await Http.DownloadFile(downloadUrl, tspath, timeoutSeconds: 200);
                                if (success)
                                    Bash.Comand($"chmod +x {tspath}");
                                else
                                    Bash.Comand($"rm -f {tspath}");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_h3zt61bu");
                    }
                }

                await install();

                if (!File.Exists(tspath))
                {
                    await Task.Delay(10_000);
                    await install();
                }

                if (!File.Exists("isdocker") && conf.checkfile)
                {
                    var response = await Http.ResponseHeaders(downloadUrl, timeoutSeconds: 10, allowAutoRedirect: true);
                    if (response != null && response.Content.Headers.ContentLength.HasValue && new FileInfo(tspath).Length != response.Content.Headers.ContentLength.Value)
                    {
                        File.Delete(tspath);
                        await Task.Delay(10_000);
                        await install();
                    }
                }
                #endregion

                while (!IsShutdown && File.Exists(tspath))
                {
                    try
                    {
                        tsprocess = new Process();
                        tsprocess.StartInfo.UseShellExecute = false;
                        tsprocess.StartInfo.RedirectStandardOutput = true;
                        tsprocess.StartInfo.RedirectStandardError = true;
                        tsprocess.StartInfo.FileName = tspath;
                        tsprocess.StartInfo.Arguments = $"--httpauth -p {conf.tsport} -d \"{homedir}\"";

                        tsprocess.Start();

                        tsprocess.OutputDataReceived += (sender, args) => { };
                        tsprocess.ErrorDataReceived += (sender, args) => { };
                        tsprocess.BeginOutputReadLine();
                        tsprocess.BeginErrorReadLine();

                        await tsprocess.WaitForExitAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_7p35pgty");
                    }

                    await Task.Delay(10_000);
                }
            });
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            try
            {
                IsShutdown = true;
                tsprocess?.Kill(true);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_g06k67q3");
            }

            EventListener.UpdateInitFile -= updateConf;
        }
        #endregion

        void updateConf()
        {
            conf = ModuleInvoke.Init("TorrServer", new ModuleConf()
            {
                tsport = 9080,
                releases = "MatriX.135",
                defaultPasswd = "ts",
                checkfile = true,
                limit_map = new List<WafLimitRootMap>()
                {
                    new("^/ts/", new WafLimitMap { limit = 50, second = 1 })
                }
            });
        }
    }
}
