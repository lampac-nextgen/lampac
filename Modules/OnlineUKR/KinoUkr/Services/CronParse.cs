using Newtonsoft.Json;
using Shared.Services;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace KinoUkr;

public static class CronParse
{
    static int _updatingKinoukrDb = 0;

    async public static void Kinoukr(object state)
    {
        if (Interlocked.Exchange(ref _updatingKinoukrDb, 1) == 1)
            return;

        try
        {
            bool savedb = false;

            string mainHtml = await Http.Get($"{ModInit.conf.host}/main/");
            if (mainHtml == null)
                return;

            var m = Regex.Match(mainHtml, "class=\"mask flex-col ps-link\" href=\"https?://[^/]+/([^\"]+\\.html)\"");
            while (m.Success)
            {
                string link = m.Groups[1].Value;
                string news = await Http.Get($"{ModInit.conf.host}/main/{link}");
                if (news != null)
                {
                    string name = Regex.Match(news, "itemprop=\"name\">([^<]+)</h1>").Groups[1].Value.Trim();
                    string eng_name = Regex.Match(news, "class=\"foriginal\">([^<]+)</div>").Groups[1].Value.Trim();
                    string year = Regex.Match(news, "<span>Рік:</span> ?<a [^>]+>([0-9]+)</a>").Groups[1].Value;

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(eng_name) && !string.IsNullOrEmpty(year))
                    {
                        string tortuga = Regex.Match(news, "src=\"https?://tortuga\\.[a-z]+/([^\"]+)\"").Groups[1].Value;
                        if (string.IsNullOrEmpty(tortuga))
                            tortuga = null;

                        string ashdi = Regex.Match(news, "src=\"https?://ashdi\\.vip/([^\"]+)\"").Groups[1].Value;
                        if (string.IsNullOrEmpty(ashdi))
                            ashdi = null;

                        if (!string.IsNullOrEmpty(tortuga) || !string.IsNullOrEmpty(ashdi))
                        {
                            var md = new DbModel()
                            {
                                ashdi = ashdi,
                                tortuga = tortuga,
                                eng_name = eng_name,
                                name = name,
                                year = year
                            };

                            if (!ModInit.databaseCache.ContainsKey(link))
                            {
                                ModInit.databaseCache.Add(link, md);
                                savedb = true;
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(md.tortuga))
                                    md.tortuga = ModInit.databaseCache[link].tortuga;

                                if (string.IsNullOrEmpty(md.ashdi))
                                    md.ashdi = ModInit.databaseCache[link].ashdi;

                                ModInit.databaseCache[link] = md;
                            }
                        }
                    }
                }

                m = m.NextMatch();
            }

            if (savedb)
                File.WriteAllText("data/kinoukr.json", JsonConvert.SerializeObject(ModInit.databaseCache, Formatting.Indented));
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_5b103f03");
        }
        finally
        {
            Volatile.Write(ref _updatingKinoukrDb, 0);
        }
    }
}
