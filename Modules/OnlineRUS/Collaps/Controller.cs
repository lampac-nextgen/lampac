using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web;

namespace Collaps
{
    public class CollapsController : BaseOnlineController<ModuleConf>
    {
        CollapsInvoke oninvk;

        public CollapsController() : base(ModInit.conf)
        {
            loadKitInitialization = (j, i, c) =>
            {
                if (j.ContainsKey("dash"))
                    i.dash = c.dash;

                return i;
            };

            requestInitialization = () =>
            {
                oninvk = new CollapsInvoke
                (
                   host,
                   "lite/collaps",
                   httpHydra,
                   init.host,
                   init.dash,
                   onstreamtofile => rch?.enable == true ? onstreamtofile : HostStreamProxy(onstreamtofile)
                );
            };
        }

        [HttpGet]
        [Staticache]
        [Route("lite/collaps")]
        async public Task<ActionResult> Index(long orid, string imdb_id, long kinopoisk_id, string title, string original_title, int s = -1, bool rjson = false, bool similar = false)
        {
            if (similar || (orid == 0 && kinopoisk_id == 0 && string.IsNullOrWhiteSpace(imdb_id)))
                return await RouteSearch(title);

            if (await IsRequestBlocked(rch: true))
                return badInitMsg;

        rhubFallback:
            var cache = await InvokeCacheResult($"collaps:view:{imdb_id}:{kinopoisk_id}:{orid}", 20,
                () => oninvk.Embed(imdb_id, kinopoisk_id, orid),
                textJson: true
            );

            if (IsRhubFallback(cache))
                goto rhubFallback;

            return ContentTpl(cache,
                () => oninvk.Tpl(cache.Value, imdb_id, kinopoisk_id, orid, title, original_title, s, vast: init.vast, rjson: rjson, headers: httpHeaders(init.host, init.headers_stream))
            );
        }


        [HttpGet]
        [Route("lite/collaps-search")]
        async public Task<ActionResult> RouteSearch(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return OnError();

            if (await IsRequestBlocked(rch: true))
                return badInitMsg;

        rhubFallback:
            var cache = await InvokeCacheResult<List<ResultSearch>>($"collaps:search:{title}", 40, textJson: true, onget: async e =>
            {
                string uri = $"{init.apihost}/list?token={init.token}&name={HttpUtility.UrlEncode(title)}";

                var root = await httpHydra.Get<RootSearch>(uri, safety: true);

                if (root?.results == null)
                    return e.Fail("results", refresh_proxy: true);

                return e.Success(root.results);
            });

            if (IsRhubFallback(cache, safety: true))
                goto rhubFallback;

            return ContentTpl(cache, () =>
            {
                var stpl = new SimilarTpl(cache.Value.Count);

                foreach (var j in cache.Value)
                {
                    string uri = $"{host}/lite/collaps?orid={j.id}";
                    stpl.Append(j.name ?? j.origin_name, j.year.ToString(), string.Empty, uri, PosterApi.Size(j.poster));
                }

                return stpl;
            });
        }
    }
}
