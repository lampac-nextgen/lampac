using Newtonsoft.Json.Linq;
using System.Text;
using Shared.Models.JacRed.Tracks;

namespace JacRed.Engine
{
    public static class WebApi
    {
        #region Indexers
        async public static Task<List<TorrentDetails>> Indexers(string query, string title, string title_original, int year, int is_serial, Dictionary<string, string> category)
        {
            var queryString = new StringBuilder();

            if (!string.IsNullOrEmpty(title))
                queryString.Append($"&title={HttpUtility.UrlEncode(title)}");

            if (!string.IsNullOrEmpty(title_original))
                queryString.Append($"&title_original={HttpUtility.UrlEncode(title_original)}");

            if (year > 0)
                queryString.Append($"&year={year}");

            if (is_serial > 0)
                queryString.Append($"&is_serial={is_serial}");

            if (category != null && category.Count > 0)
                queryString.Append($"&category[]={category.First().Value}");

            if (!string.IsNullOrEmpty(ModInit.conf.webApiKey))
                queryString.Append($"&apikey={HttpUtility.UrlEncode(ModInit.conf.webApiKey)}");

            var root = await Http.Get<JObject>($"{ModInit.conf.webApiHost}/api/v2.0/indexers/all/results?query={HttpUtility.UrlEncode(query)}" + queryString.ToString(), timeoutSeconds: 8);
            if (root == null)
                return new List<TorrentDetails>();

            var results = root.GetValue("Results")?.ToObject<JArray>();
            if (results == null || results.Count == 0)
                return new List<TorrentDetails>();

            var torrents = new List<TorrentDetails>(results.Count);

            foreach (var torrent in results)
            {
                try
                {
                    string name = torrent.Value<string>("Title");
                    string tracker = torrent.Value<string>("Tracker");

                    if (ModInit.conf.Red.trackers != null)
                    {
                        if (!tracker.Contains(","))
                        {
                            if (!ModInit.conf.Red.trackers.Contains(tracker))
                                continue;
                        }
                        else
                        {
                            /*
                             * Этот код фильтрует результаты поиска торрентов по списку разрешённых трекеров, который хранится в ModInit.conf.Red.trackers.
                             * Если у торрента в поле Tracker указано несколько трекеров через запятую, то он будет допущен только в том случае, если хотя бы один из этих трекеров есть в разрешённом списке.
                             */
                            var trackers = tracker.Split(',');
                            if (!ModInit.conf.Red.trackers.Any(t => trackers.Contains(t)))
                                continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(ModInit.conf.filter) && !Regex.IsMatch(name, ModInit.conf.filter, RegexOptions.IgnoreCase))
                        continue;

                    if (!string.IsNullOrEmpty(ModInit.conf.filter_ignore) && Regex.IsMatch(name, ModInit.conf.filter_ignore, RegexOptions.IgnoreCase))
                        continue;

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = tracker,
                        url = torrent.Value<string>("Details"),
                        title = name,
                        sid = torrent.Value<int>("Seeders"),
                        pir = torrent.Value<int>("Peers"),
                        size = torrent.Value<double>("Size"),
                        magnet = torrent.Value<string>("MagnetUri"),
                        createTime = torrent.Value<DateTime>("PublishDate"),
                        ffprobe = torrent["ffprobe"]?.ToObject<List<ffStream>>()
                    });
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "{Class} {CatchId}", "WebApi", "id_px2tgfag");
                }
            }

            return torrents;
        }
        #endregion

        #region Api
        public static Task<List<TorrentDetails>> Api(string search)
        {
            return Indexers(search, null, null, 0, 0, null);
        }
        #endregion
    }
}
