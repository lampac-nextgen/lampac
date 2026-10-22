using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace AniLiberty;

public class AniLibertyController : BaseOnlineController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public AniLibertyController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/aniliberty")]
    async public Task<ActionResult> Index(string title, short year, string releases, bool rjson = false, bool similar = false, string source = null, string id = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(releases) && !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(id))
        {
            if (source.Equals("aniLiberty", StringComparison.OrdinalIgnoreCase))
                releases = id;
        }

        if (string.IsNullOrEmpty(releases))
        {
            #region Поиск
            string stitle = SearchNameTo.Convert(title);
            if (stitle == null)
                return OnError();

        rhubFallback:
            var cache = await InvokeCacheResult<List<(string title, string year, int releases, string cover)>>($"aniliberty:search:{title}:{similar}", TimeSpan.FromHours(4), async e =>
            {
                var search = await httpHydra.Get<List<SearchItem>>($"{init.host}/api/v1/app/search/releases?query={HttpUtility.UrlEncode(title)}");

                if (search == null || search.Count == 0)
                    return e.Fail("search");

                bool checkName = true;
                var catalog = new List<(string title, string year, int releases, string cover)>(search.Count);

            retry:
                foreach (var anime in search)
                {
                    if (!checkName || similar ||
                        SearchNameTo.StartsWith(anime.name?.main, stitle) ||
                        SearchNameTo.StartsWith(anime.name?.english, stitle))
                    {
                        string img = null;
                        var cover = anime.poster;
                        if (cover != null)
                            img = init.host + cover.src;

                        catalog.Add(($"{anime.name?.main} / {anime.name?.english}", anime.year.ToString(), anime.id, img));
                    }
                }

                if (catalog.Count == 0)
                {
                    if (checkName && similar == false)
                    {
                        checkName = false;
                        goto retry;
                    }

                    return e.Fail("catalog");
                }

                return e.Success(catalog);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            if (!similar && cache.Value != null && cache.Value.Count == 1)
                return LocalRedirect(accsArgs($"/lite/aniliberty?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&releases={cache.Value.First().releases}"));

            return ContentTpl(cache, () =>
            {
                var stpl = new SimilarTpl(cache.Value.Count);

                foreach (var res in cache.Value)
                {
                    stpl.Append(
                        res.title,
                        res.year,
                        string.Empty,
                        $"{host}/lite/aniliberty?rjson={rjson}&title={HttpUtility.UrlEncode(title)}&releases={res.releases}",
                        PosterApi.Size(res.cover)
                    );
                }

                return stpl;

            });
            #endregion
        }
        else
        {
            #region Серии
        rhubFallback:
            var cache = await InvokeCacheResult<Release>($"aniliberty:releases:{releases}", 20, async e =>
            {
                var root = await httpHydra.Get<Release>($"{init.host}/api/v1/anime/releases/{releases}");

                if (root?.episodes == null)
                    return e.Fail("episodes");

                return e.Success(root);
            });

            if (IsRhubFallback(cache))
                goto rhubFallback;

            return ContentTpl(cache, () =>
            {
                var episodes = cache.Value.episodes;
                var etpl = new EpisodeTpl(episodes.Length);
                string season = GetSeason(cache.Value);

                foreach (var episode in episodes)
                {
                    string number = episode.ordinal;

                    string name = episode.name;
                    name = string.IsNullOrEmpty(name) ? $"{number} серия" : name;

                    var streams = new StreamQualityTpl();
                    foreach (var f in new List<(string quality, string url)>
                    {
                        ("1080p", episode.hls_1080),
                        ("720p", episode.hls_720),
                        ("480p", episode.hls_480)
                    })
                    {
                        if (string.IsNullOrEmpty(f.url))
                            continue;

                        streams.Append(HostStreamProxy(f.url), f.quality);
                    }

                    var first = streams.Firts();
                    if (first != null)
                    {
                        etpl.Append(
                            name,
                            title,
                            season,
                            number,
                            first.link,
                            streamquality: streams
                        );
                    }
                }

                return etpl;
            });
            #endregion
        }
    }

    static string GetSeason(Release release)
    {
        string alias = release.alias ?? string.Empty;
        Match match = Regex.Match(alias, @"(?:^|-)(?:season-)?([0-9]{1,2})(?:st|nd|rd|th)(?:-|$)|(?:^|-)season-([0-9]{1,2})(?:-|$)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

        foreach (string name in new[] { release.name?.main, release.name?.english })
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            match = Regex.Match(name, @"\b(?:сезон|season)\s*([0-9]{1,2})\b|\b([0-9]{1,2})\s*(?:сезон|season)\b", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
        }

        // A bare number in one title can be part of the title itself. Use it only
        // when the other title confirms the same installment with a Roman numeral.
        Match numeric = Regex.Match(release.name?.main ?? string.Empty, @"\s([0-9]{1,2})(?=\s*(?::|$))");
        Match roman = Regex.Match(release.name?.english ?? string.Empty, @"\b(II|III|IV|V|VI|VII|VIII|IX|X)\b(?=\s*(?::|$))");
        if (numeric.Success && roman.Success)
        {
            string[] romans = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            int romanSeason = Array.IndexOf(romans, roman.Groups[1].Value) + 1;
            if (int.TryParse(numeric.Groups[1].Value, out int numericSeason) && numericSeason == romanSeason)
                return numericSeason.ToString();
        }

        return "1";
    }
}
