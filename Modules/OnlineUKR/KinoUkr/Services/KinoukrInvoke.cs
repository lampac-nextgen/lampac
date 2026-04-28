using Shared.Services;
using Shared.Services.RxEnumerate;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KinoUkr;

public class KinoukrInvoke
{
    #region KinoukrInvoke
    HttpHydra http;

    public KinoukrInvoke(HttpHydra httpHydra)
    {
        http = httpHydra;
    }
    #endregion

    #region Search
    public EmbedModel Search(string title, string original_title, string imdb_id, long kinopoisk_id)
    {
        var root = SearchDb(title, original_title, (kinopoisk_id > 0 ? kinopoisk_id.ToString() : null), imdb_id);
        if (root == null || root.Count == 0)
            return null;

        var result = new EmbedModel
        {
            similars = new List<Similar>(root.Count)
        };

        foreach (var item in root)
        {
            result.similars.Add(new Similar()
            {
                href = !string.IsNullOrEmpty(item.tortuga) ? $"https://tortuga.tw/{item.tortuga}" : $"https://ashdi.vip/{item.ashdi}",
                title = $"{item.name} / {item.eng_name}",
                year = item.year
            });
        }

        if (result.similars.Count == 0)
            return new EmbedModel() { IsEmpty = true };

        return result;
    }
    #endregion

    #region SearchDb
    static List<DbModel> SearchDb(string name, string eng_name, string kp, string imdb)
    {
        if (string.IsNullOrEmpty(name ?? eng_name ?? kp ?? imdb))
            return null;

        if (!string.IsNullOrEmpty(kp) || !string.IsNullOrEmpty(imdb))
        {
            var resultId = ModInit.database.Where(i =>
            {
                if (!string.IsNullOrEmpty(kp) && i.Value.kp_id == kp)
                    return true;

                if (!string.IsNullOrEmpty(imdb) && i.Value.imdb_id == imdb)
                    return true;

                return false;
            });

            if (resultId != null && resultId.Count() > 0)
                return resultId.Select(i => i.Value).ToList();
        }

        string sname = StringConvert.SearchName(name);
        string seng_name = StringConvert.SearchName(eng_name);

        var result = ModInit.database.Where(i =>
        {
            if (sname != null && StringConvert.SearchName(i.Value.name) == sname)
                return true;

            if (seng_name != null && StringConvert.SearchName(i.Value.eng_name) == seng_name)
                return true;

            return false;
        });

        return result.Select(i => i.Value).ToList();
    }
    #endregion

    #region Embed
    public async Task<string> Embed(string href)
    {
        string iframeUri = null;

        await http.GetSpan(href, news =>
        {
            iframeUri = Rx.Match(news, "src=\"(https?://tortuga\\.[a-z]+/[^\"]+)\"");
            if (string.IsNullOrEmpty(iframeUri))
                iframeUri = Rx.Match(news, "src=\"(https?://ashdi\\.vip/[^\"]+)\"");
        });

        if (string.IsNullOrEmpty(iframeUri))
            return null;

        return iframeUri;
    }
    #endregion
}
