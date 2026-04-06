using Shared.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Shared.Services.Utilities;

namespace LampaWeb.Services
{
    public static class LampaCron
    {
        static string currentapp;

        public static void Start()
        {
            var init = ModInit.conf;
            _cronTimer = new Timer(cron, null, TimeSpan.Zero, TimeSpan.FromMinutes(Math.Max(init.intervalupdate, 5)));
        }

        public static void Stop()
        {
            _cronTimer.Dispose();
        }

        static Timer _cronTimer;

        static int _updatingDb = 0;

        async static void cron(object state)
        {
            if (Interlocked.Exchange(ref _updatingDb, 1) == 1)
                return;

            try
            {
                var init = ModInit.conf;
                bool istree = !string.IsNullOrEmpty(init.tree);

                async Task<bool> update()
                {
                    if (!init.autoupdate)
                        return false;

                    if (!File.Exists("wwwroot/lampa-main/app.min.js"))
                        return true;

                    if (istree && File.Exists("wwwroot/lampa-main/tree") && init.tree == File.ReadAllText("wwwroot/lampa-main/tree"))
                        return false;

                    bool changeversion = false;

                    await Http.GetSpan($"https://raw.githubusercontent.com/{init.git}/{(istree ? init.tree : "main")}/app.min.js", gitapp =>
                    {
                        if (!gitapp.Contains("author: 'Yumata'", StringComparison.Ordinal))
                            return;

                        if (currentapp == null)
                            currentapp = CrypTo.md5File("wwwroot/lampa-main/app.min.js");

                        if (!string.IsNullOrEmpty(currentapp) && CrypTo.md5(gitapp) != currentapp)
                            changeversion = true;

                    });

                    if (istree)
                        File.WriteAllText("wwwroot/lampa-main/tree", init.tree);

                    return changeversion;
                }

                if (await update())
                {
                    string uri = istree ?
                        $"https://github.com/{init.git}/archive/{init.tree}.zip" :
                        $"https://github.com/{init.git}/archive/refs/heads/main.zip";

                    byte[] array = await Http.Download(uri);
                    if (array != null)
                    {
                        currentapp = null;

                        await File.WriteAllBytesAsync("wwwroot/lampa.zip", array);
                        ZipFile.ExtractToDirectory("wwwroot/lampa.zip", "wwwroot/", overwriteFiles: true);

                        var repo = init.git.Split('/')[^1];
                        var targetDirectory = $"{repo}-{(istree ? init.tree : "main")}";
                        var isLampaMainDirectory = targetDirectory.Equals("lampa-main", StringComparison.OrdinalIgnoreCase);

                        if (!isLampaMainDirectory)
                        {
                            foreach (string infilePath in Directory.GetFiles($"wwwroot/{targetDirectory}", "*", SearchOption.AllDirectories))
                            {
                                string outfile = infilePath.Replace($"{targetDirectory}", "lampa-main");
                                Directory.CreateDirectory(Path.GetDirectoryName(outfile));
                                File.Copy(infilePath, outfile, true);
                            }

                            File.WriteAllText("wwwroot/lampa-main/tree", init.tree);
                        }

                        string html = File.ReadAllText("wwwroot/lampa-main/index.html");
                        html = html.Replace("</body>", "<script src=\"/lampainit.js\"></script></body>");

                        File.WriteAllText("wwwroot/lampa-main/index.html", html);
                        File.CreateText("wwwroot/lampa-main/personal.lampa");

                        if (!File.Exists("wwwroot/lampa-main/plugins_black_list.json"))
                            File.WriteAllText("wwwroot/lampa-main/plugins_black_list.json", "[]");

                        if (!File.Exists("wwwroot/lampa-main/plugins/modification.js"))
                        {
                            Directory.CreateDirectory("wwwroot/lampa-main/plugins");
                            File.WriteAllText("wwwroot/lampa-main/plugins/modification.js", string.Empty);
                        }

                        File.Delete("wwwroot/lampa.zip");

                        if (!isLampaMainDirectory)
                            Directory.Delete($"wwwroot/{targetDirectory}", true);
                    }
                }
            } 
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "LampaCron", "id_30qftt0j");
            }
            finally
            {
                Volatile.Write(ref _updatingDb, 0);
            }
        }
    }
}
