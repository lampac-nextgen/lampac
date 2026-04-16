using Newtonsoft.Json;
using Shared.PlaywrightCore;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PizdatoeHD
{
    public static class CronParse
    {
        static int _updating = 0;
        static object _lock = new object();

        async public static void Pizda(object state)
        {
            if (Interlocked.Exchange(ref _updating, 1) == 1)
                return;

            try
            {
                if (PlaywrightBrowser.Status != PlaywrightStatus.disabled && ModInit.conf.enable)
                    await Parse(1, null);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "CatchId={CatchId}", "id_4fc1gkyo");
            }
            finally
            {
                Volatile.Write(ref _updating, 0);
            }
        }


        async public static void PizdaBobra()
        {
            try
            {
                int curentproxy = 0;
                string proxy_list = await Http.Get("");
                List<string> proxyids = new List<string>();

                foreach (string line in proxy_list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.IsNullOrEmpty(line))
                        continue;

                    string id = Regex.Match(line, "http://([^:]+):").Groups[1].Value;
                    if (!string.IsNullOrEmpty(id))
                        proxyids.Add(id);
                }

                async Task ProcessPage(int i)
                {
                reset:
                    string idproxy = proxyids[Interlocked.Increment(ref curentproxy) - 1];

                    Console.WriteLine("\n");
                    Console.WriteLine(i);
                    Console.WriteLine(idproxy);

                    var credentials = new NetworkCredential(idproxy, "");
                    var proxy = new WebProxy("", true, null, credentials);

                    var result = await Parse(i, proxy);
                    if (result == false)
                    {
                        Console.WriteLine("reset " + i);
                        goto reset;
                    }
                }

                var tasks = new List<Task>();
                var semaphore = new SemaphoreSlim(20);

                for (int i = 1; i <= 2416; i++)
                {
                    int page = i;

                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();

                        try
                        {
                            await ProcessPage(page);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                Console.WriteLine("\n\n\n\nexit");
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "CatchId={CatchId}", "id_5fc1gkyo");
            }
        }


        async static Task<bool> Parse(int page, WebProxy proxy)
        {
            string mainHtml = proxy != null
                ? await Http.Get($"{ModInit.conf.host}/page/{page}/", proxy: proxy, timeoutSeconds: 4)
                : await PlaywrightBrowser.Get(ModInit.conf, $"{ModInit.conf.host}/page/{page}/");

            if (mainHtml == null || !mainHtml.Contains("class=\"b-content__inline_item\""))
                return default;

            bool savedb = false;

            var m = Regex.Match(mainHtml, "class=\"b-content__inline_item\" data-id=\"[0-9]+\" data-url=\"(https?://[^/]+)?/([^\"]+)\"");
            while (m.Success)
            {
                string link = m.Groups[2].Value;
                if (string.IsNullOrEmpty(link) || ModInit.PizdatoeDb.ContainsKey(link))
                {
                    m = m.NextMatch();
                    continue;
                }

                if (proxy == null)
                    await Task.Delay(5000);

                string news = proxy != null
                    ? await Http.Get($"{ModInit.conf.host}/{link}", proxy: proxy, timeoutSeconds: 10)
                    : await PlaywrightBrowser.Get(ModInit.conf, $"{ModInit.conf.host}/{link}");

                if (news != null)
                {
                    string name = Regex.Match(news, "itemprop=\"name\">([^<]+)").Groups[1].Value.Trim();
                    string eng_name = Regex.Match(news, "itemprop=\"alternativeHeadline\">([^<]+)").Groups[1].Value.Trim();
                    string year = Regex.Match(news, "<a href=\"(https?://[^/]+)?/year/([0-9]+)").Groups[2].Value;

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(year))
                    {
                        Console.WriteLine("\t" + link);

                        string image = Regex.Match(news, "<img itemprop=\"image\" src=\"([^\"]+)\"").Groups[1].Value;
                        if (string.IsNullOrEmpty(image))
                            image = null;

                        string imdb = Regex.Match(news, "href=\"/help/([^/\"]+)/\" target=\"_blank\" rel=\"nofollow\">IMDb</a>").Groups[1].Value;
                        if (!string.IsNullOrEmpty(imdb))
                        {
                            string url_imdb = CrypTo.DecodeBase64(imdb);
                            if (!string.IsNullOrEmpty(url_imdb))
                            {
                                url_imdb = HttpUtility.UrlDecode(url_imdb);
                                imdb = Regex.Match(url_imdb, "(tt[0-9]+)").Groups[1].Value;
                            }
                        }

                        string kp = Regex.Match(news, "href=\"/help/([^/\"]+)/\" target=\"_blank\" rel=\"nofollow\">Кинопоиск</a>").Groups[1].Value;
                        if (!string.IsNullOrEmpty(kp))
                        {
                            string url_kp = CrypTo.DecodeBase64(kp);
                            if (!string.IsNullOrEmpty(url_kp))
                            {
                                url_kp = HttpUtility.UrlDecode(url_kp);
                                kp = Regex.Match(url_kp, "/([0-9]+)").Groups[1].Value;
                            }
                        }

                        var md = new DbModel()
                        {
                            title = name,
                            original_title = eng_name,
                            year = year,
                            href = link,
                            img = image,
                            imdb = string.IsNullOrEmpty(imdb) ? null : imdb,
                            kp = string.IsNullOrEmpty(kp) ? null : kp
                        };

                        ModInit.PizdatoeDb.TryAdd(link, md);
                        savedb = true;
                    }
                }

                m = m.NextMatch();
            }

            if (savedb)
            {
                lock (_lock)
                    File.WriteAllText("data/PizdatoeDb.json", JsonConvert.SerializeObject(ModInit.PizdatoeDb, Formatting.Indented));
            }

            return true;
        }
    }
}
