using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MonoTorrent;
using MonoTorrent.Client;
using NetVips;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using IO = System.IO;

namespace DLNA;

public class DLNAController : BaseController
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<DLNAController>();

    #region DLNAController
    static string dlna_path => ModInit.conf.path;

    bool HasDlnaAccess()
        => CoreInit.conf.accsdb.enable
            || requestInfo.IsLocalIp
            || ModInit.conf.allowRemoteWithoutAuth;

    JsonResult DlnaAccessDenied()
        => new(new { error = "authorizationOrLocalIPRequired" }) { StatusCode = 403 };

    // https://github.com/YouROK/TorrServer/blob/master/server/torr/utils/torrent.go
    static string defTrackers = "tr=" + string.Join("&tr=", new string[]
    {
        "http://retracker.local/announce",
        "http://bt4.t-ru.org/ann?magnet",
        "http://retracker.mgts.by:80/announce",
        "http://tracker.city9x.com:2710/announce",
        "http://tracker.electro-torrent.pl:80/announce",
        "http://tracker.internetwarriors.net:1337/announce",
        "http://tracker2.itzmx.com:6961/announce",
        "udp://opentor.org:2710",
        "udp://public.popcorn-tracker.org:6969/announce",
        "udp://tracker.opentrackr.org:1337/announce",
        "http://bt.svao-ix.ru/announce",
        "udp://explodie.org:6969/announce"
    }.Select(t => HttpUtility.UrlEncode(t)));

    static ClientEngine torrentEngine;
    static readonly SemaphoreSlim torrentEngineLock = new(1, 1);
    static readonly ConcurrentDictionary<string, byte> torrentRemovals = new(StringComparer.OrdinalIgnoreCase);
    static CancellationTokenSource torrentCleanupCts;
    static Task torrentCleanupTask = Task.CompletedTask;

    public static void Initialization()
    {
        Directory.CreateDirectory("cache/torrent");
        Directory.CreateDirectory($"{dlna_path}/");
        Directory.CreateDirectory($"{dlna_path}/thumbs/");
        Directory.CreateDirectory($"{dlna_path}/tmdb/");

        try
        {
            bullderClientEngine().GetAwaiter().GetResult();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_dlna_engine_init");
        }

        StartTorrentCleanup();

        if (!Directory.Exists("cache/metadata"))
            return;

        #region Resume download
        var _files = Directory.GetFiles("cache/metadata", "*.torrent");
        if (_files.Length == 0)
            return;

        bullderClientEngine().GetAwaiter().GetResult();

        foreach (string path in _files)
        {
            var t = Torrent.Load(path);
            var manager = ModInit.conf.mode == "stream" ? torrentEngine.AddStreamingAsync(t, $"{dlna_path}/").Result : torrentEngine.AddAsync(t, $"{dlna_path}/").Result;

            //if (FastResume.TryLoad($"cache/fastresume/{t.InfoHash.ToHex()}.fresume", out FastResume resume))
            //    manager.LoadFastResume(resume);

            int[] indexs = null;

            try
            {
                if (IO.File.Exists($"cache/metadata/{t.InfoHashes.V1.ToHex()}.json"))
                    indexs = JsonConvert.DeserializeObject<int[]>(IO.File.ReadAllText($"cache/metadata/{t.InfoHashes.V1.ToHex()}.json"));
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_m7kjuvbk");
            }

            bool setPriority = false;

            manager.TorrentStateChanged += async (s, e) =>
            {
                try
                {
                    if (e != null && e.NewState == TorrentState.Seeding)
                        await e.TorrentManager.StopAsync();

                    if (e != null && (e.NewState == TorrentState.Metadata || e.NewState == TorrentState.Hashing || e.NewState == TorrentState.Downloading))
                    {
                        if (!setPriority)
                        {
                            setPriority = true;

                            if (indexs == null || indexs.Length == 0)
                            {
                                await manager.SetFilePriorityAsync(manager.Files[0], Priority.High);
                            }
                            else
                            {
                                for (int i = 0; i < manager.Files.Count; i++)
                                {
                                    if (indexs.Contains(i))
                                    {
                                        await manager.SetFilePriorityAsync(manager.Files[i], i == indexs[0] ? Priority.High : Priority.Normal);
                                    }
                                    else
                                    {
                                        await manager.SetFilePriorityAsync(manager.Files[i], Priority.DoNotDownload);
                                    }
                                }
                            }
                        }
                    }

                    if (e != null && (e.NewState == TorrentState.Stopped || e.NewState == TorrentState.Stopping))
                    {
                        try
                        {
                            IO.File.Delete(path);
                            IO.File.Delete(path.Replace(".torrent", ".json"));
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_6r935mc6");
                        }

                        foreach (var f in e.TorrentManager.Files)
                        {
                            try
                            {
                                if (f.Priority == Priority.DoNotDownload && IO.File.Exists(f.FullPath))
                                    IO.File.Delete(f.FullPath);
                            }
                            catch (System.Exception ex)
                            {
                                Log.Error(ex, "CatchId={CatchId}", "id_2gl0gtwq");
                            }
                        }

                        await removeTorrentManager(e.TorrentManager.InfoHashes.V1.ToHex());
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error(ex, "CatchId={CatchId}", "id_w3wggblq");
                }
            };
        }
        #endregion
    }
    #endregion

    #region dlna.js
    [HttpGet, AllowAnonymous]
    [Staticache(cacheMinutes: 10, always: true, setHeadersNoCache: true)]
    [Route("dlna.js")]
    [Route("dlna/js/{token}")]
    public ActionResult Plugin(string token)
    {
        var plugin = FileCache.ReadAllText($"{ModInit.modpath}/plugin.js", "dlna.js", saveCache: false)
            .Replace("{localhost}", host)
            .Replace("{token}", HttpUtility.UrlEncode(token));

        return ContentTo(plugin, "application/javascript; charset=utf-8");
    }
    #endregion

    #region bullderClientEngine
    static async Task bullderClientEngine(int connectionTimeout = 10)
    {
        await torrentEngineLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (torrentEngine != null)
                return;

            var engine = new ClientEngine(BuildEngineSettings(connectionTimeout));

            try
            {
                await engine.StartAllAsync().ConfigureAwait(false);
                torrentEngine = engine;
            }
            catch
            {
                engine.Dispose();
                throw;
            }
        }
        finally
        {
            torrentEngineLock.Release();
        }
    }

    static EngineSettings BuildEngineSettings(int connectionTimeout = 10)
        => new EngineSettingsBuilder()
        {
            MaximumHalfOpenConnections = 20,
            ConnectionTimeout = TimeSpan.FromSeconds(connectionTimeout),
            MaximumDownloadRate = ModInit.conf.downloadSpeed,
            MaximumUploadRate = ModInit.conf.uploadSpeed,
            MaximumDiskReadRate = ModInit.conf.maximumDiskReadRate,
            MaximumDiskWriteRate = ModInit.conf.maximumDiskWriteRate
        }.ToSettings();

    public static void UpdateEngineSettings()
    {
        var engine = torrentEngine;
        if (engine == null)
            return;

        try
        {
            engine.UpdateSettingsAsync(BuildEngineSettings()).GetAwaiter().GetResult();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_dlna_engine_settings");
        }
    }
    #endregion

    #region Torrent cleanup
    static void StartTorrentCleanup()
    {
        if (torrentCleanupCts != null)
            return;

        torrentCleanupCts = new CancellationTokenSource();
        torrentCleanupTask = TorrentCleanupLoop(torrentCleanupCts.Token);
    }

    static async Task TorrentCleanupLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                await removeFinishedTorrentManagers().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_2ui4dhce");
            }
        }
    }

    static async Task removeFinishedTorrentManagers()
    {
        var engine = torrentEngine;
        if (engine?.Torrents == null)
            return;

        TorrentManager[] managers;

        try
        {
            managers = engine.Torrents
                .Where(i =>
                    i.State == TorrentState.Seeding ||
                    i.State == TorrentState.Stopped ||
                    i.State == TorrentState.Stopping)
                .ToArray();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_dlna_manager_snapshot");
            return;
        }

        foreach (var manager in managers)
            await removeTorrentManager(
                manager.InfoHashes.V1.ToHex(),
                120
            ).ConfigureAwait(false);
    }

    static async Task removeTorrentManager(
        string hash,
        int stopTimeoutSeconds = 20)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return;

        if (!torrentRemovals.TryAdd(hash, 0))
            return;

        try
        {
            var engine = torrentEngine;
            if (engine?.Torrents == null)
                return;

            TorrentManager manager;

            try
            {
                manager = engine.Torrents.FirstOrDefault(i =>
                    string.Equals(
                        i.InfoHashes.V1.ToHex(),
                        hash,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_dlna_manager_lookup");
                return;
            }

            if (manager != null)
            {
                await removeTorrentManager(
                    engine,
                    manager,
                    stopTimeoutSeconds
                ).ConfigureAwait(false);
            }
        }
        finally
        {
            torrentRemovals.TryRemove(hash, out _);
        }
    }

    static async Task removeTorrentManager(
        ClientEngine engine,
        TorrentManager manager,
        int stopTimeoutSeconds)
    {
        try
        {
            await manager.StopAsync(
                TimeSpan.FromSeconds(stopTimeoutSeconds)
            ).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_dlna_manager_stop");
        }

        try
        {
            await engine.RemoveAsync(manager).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_dlna_manager_remove");
        }
    }

    public static void Shutdown()
    {
        var cleanupCts = Interlocked.Exchange(ref torrentCleanupCts, null);
        cleanupCts?.Cancel();

        var cleanupTask = torrentCleanupTask;
        torrentCleanupTask = Task.CompletedTask;

        try
        {
            cleanupTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cleanupCts?.IsCancellationRequested == true)
        {
        }
        finally
        {
            cleanupCts?.Dispose();
        }

        ClientEngine engine;

        torrentEngineLock.Wait();
        try
        {
            engine = torrentEngine;
            torrentEngine = null;
        }
        finally
        {
            torrentEngineLock.Release();
        }

        if (engine == null)
            return;

        try
        {
            engine.StopAllAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_dlna_engine_stop");
        }

        try
        {
            engine.Dispose();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_dlna_engine_dispose");
        }
    }
    #endregion

    #region getTorrent
    async ValueTask<(string magnet, byte[] torrent)> getTorrent(string path)
    {
        if (!path.StartsWith("http"))
            return (path, null);

        string memkey = $"DLNAController:getTorrent:{path}";
        if (!memoryCache.TryGetValue(memkey, out (string magnet, byte[] torrent) cache))
        {
            var handler = new System.Net.Http.HttpClientHandler()
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = Http.AlwaysAllowCertificate
            };

            using (var client = new System.Net.Http.HttpClient(handler))
            {
                client.Timeout = TimeSpan.FromSeconds(10);

                using (var response = await client.GetAsync(path))
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        using (var content = response.Content)
                        {
                            var t = await content.ReadAsByteArrayAsync();
                            cache.magnet = BencodeTo.Magnet(t);
                            if (cache.magnet != null)
                                cache.torrent = t;
                        }
                    }
                    else if ((int)response.StatusCode is 301 or 302 or 307)
                    {
                        string location = response.Headers.Location?.ToString() ?? response.RequestMessage.RequestUri?.ToString();
                        if (location != null && location.StartsWith("magnet:"))
                            cache.magnet = location;
                    }
                }
            }

            if (cache.magnet == null && cache.torrent == null)
                return (null, null);

            memoryCache.Set(memkey, cache, DateTime.Now.AddMinutes(10));
        }

        return (cache.magnet, cache.torrent);
    }
    #endregion

    #region getTmdb
    JObject getTmdb(string name)
    {
        try
        {
            string file = $"{dlna_path}/tmdb/{CrypTo.md5(name)}.json";
            if (IO.File.Exists(file))
            {
                var tmdb = JsonConvert.DeserializeObject<JObject>(IO.File.ReadAllText(file));
                tmdb.Remove("created_by");
                tmdb.Remove("networks");
                tmdb.Remove("production_companies");

                return tmdb;
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_sra5hz53");
        }

        return null;
    }
    #endregion

    #region getEpisodes
    JArray getEpisodes(JObject tmdb, string fileName)
    {
        try
        {
            if (tmdb == null || !tmdb.ContainsKey("number_of_seasons"))
                return null;

            int season = getSeason(fileName);

            string file = $"{dlna_path}/tmdb/{tmdb.Value<long>("id")}_season-{season}.json";
            if (IO.File.Exists(file))
            {
                if (memoryCache.TryGetValue(file, out JArray episodes))
                    return episodes;

                episodes = JsonConvert.DeserializeObject<JObject>(IO.File.ReadAllText(file)).Value<JArray>("episodes");

                memoryCache.Set(file, episodes, DateTime.Now.AddSeconds(10));
                return episodes;
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_by4i5sd2");
        }

        return null;
    }
    #endregion

    #region getEpisode
    int getEpisode(string fileName)
    {
        if (int.TryParse(Regex.Match(fileName, "EP?([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value, out int _e) && _e > 0)
            return _e;

        return 0;
    }
    #endregion

    #region getSeason
    int getSeason(string fileName)
    {
        if (int.TryParse(Regex.Match(fileName, "S([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value, out int _s) && _s > 0)
            return _s;

        return 0;
    }
    #endregion


    #region Navigation
    [HttpGet]
    [Route("dlna")]
    public ActionResult Index(string path)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        string full = SafePath(path);
        if (full == null || !Directory.Exists(full))
        {
            return ContentTo("[]");
        }

        #region getImage
        string getImage(string name)
        {
            string pathimage = $"thumbs/{name}.jpg";
            if (IO.File.Exists($"{dlna_path}/" + pathimage))
                return $"{host}/dlna/stream?path={HttpUtility.UrlEncode(pathimage)}";

            return null;
        }
        #endregion

        #region getPreview
        string getPreview(string name)
        {
            string pathimage = $"temp/{name}.mp4";
            if (IO.File.Exists($"{dlna_path}/" + pathimage))
                return $"{host}/dlna/stream?path={HttpUtility.UrlEncode(pathimage)}";

            pathimage = $"temp/{name}.webm";
            if (IO.File.Exists($"{dlna_path}/" + pathimage))
                return $"{host}/dlna/stream?path={HttpUtility.UrlEncode(pathimage)}";

            return null;
        }
        #endregion

        #region countFiles
        int countFiles(string _path)
        {
            int count = 0;

            foreach (string file in Directory.GetFiles(_path))
            {
                if (!Regex.IsMatch(Path.GetExtension(file), ModInit.conf.mediaPattern))
                    continue;

                if (new FileInfo(file).Length > 0)
                    count++;
            }

            return count;
        }
        #endregion

        var playlist = new List<DlnaModel>();

        #region folders
        foreach (string folder in Directory.GetDirectories(full))
        {
            if (folder.Contains("thumbs") || folder.Contains("tmdb") || folder.Contains("temp"))
                continue;

            int length = countFiles(folder);
            if (length > 0 || Directory.GetDirectories(folder).Length > 0)
            {
                string relativeFolder = RelativeDlnaPath(folder);

                playlist.Add(new DlnaModel()
                {
                    type = "folder",
                    name = Path.GetFileName(folder),
                    uri = $"{host}/dlna?path={HttpUtility.UrlEncode(relativeFolder)}",
                    img = getImage(CrypTo.md5(Path.GetFileName(folder))),
                    preview = getPreview(CrypTo.md5(Path.GetFileName(folder))),
                    path = relativeFolder,
                    length = countFiles(folder),
                    creationTime = Directory.GetCreationTime(folder),
                    tmdb = getTmdb(Path.GetFileName(folder))
                });
            }
        }
        #endregion

        #region files
        var filesTmdb = getTmdb(path);
        var subtitles = Directory.GetFiles(full, "*.srt");

        foreach (string file in Directory.GetFiles(full))
        {
            if (!Regex.IsMatch(Path.GetExtension(file), ModInit.conf.mediaPattern))
                continue;

            string name = Path.GetFileName(file);
            string relativeFile = RelativeDlnaPath(file);
            var fileinfo = new FileInfo(file);
            if (fileinfo.Length == 0)
                continue;

            JObject episodeTmdb = null;

            string img = getImage(CrypTo.md5(name));
            var episodes = getEpisodes(filesTmdb, name);
            if (episodes != null)
            {
                int episode = getEpisode(name);
                if (episode > 0)
                {
                    episodeTmdb = episodes.FirstOrDefault(i => i.Value<int>("episode_number") == episode)?.ToObject<JObject>();

                    if (episodeTmdb != null)
                    {
                        episodeTmdb.Remove("crew");
                        episodeTmdb.Remove("guest_stars");

                        if (episodeTmdb.ContainsKey("still_path"))
                            img = $"tmdb:/t/p/w400" + episodeTmdb.Value<string>("still_path");
                    }
                }
            }

            if (img == null)
                img = getImage(CrypTo.md5($"{path}/{name}"));

            var dlnaModel = new DlnaModel()
            {
                type = "file",
                name = name,
                uri = $"{host}/dlna/stream?path={HttpUtility.UrlEncode(relativeFile)}",
                img = img,
                preview = getPreview(CrypTo.md5(name)),
                subtitles = new List<Subtitle>(),
                path = relativeFile,
                length = fileinfo.Length,
                creationTime = fileinfo.CreationTime,
                s = getSeason(name),
                e = getEpisode(name),
                tmdb = string.IsNullOrEmpty(path) ? getTmdb(name) : filesTmdb,
                episode = episodeTmdb
            };

            #region subtitles
            foreach (string subfile in subtitles)
            {
                if (subfile.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    dlnaModel.subtitles.Add(new Subtitle()
                    {
                        label = "Sub #1",
                        url = $"{host}/dlna/stream?path={HttpUtility.UrlEncode(RelativeDlnaPath(subfile))}"
                    });
                }
            }
            #endregion

            playlist.Add(dlnaModel);
        }
        #endregion

        var jSettings = new JsonSerializerSettings()
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            #region torrentEngine
            if (torrentEngine?.Torrents != null)
            {
                foreach (var t in torrentEngine.Torrents)
                {
                    if (t.State == TorrentState.Metadata || t.State == TorrentState.Downloading || t.State == TorrentState.Starting)
                    {
                        if (t.Torrent?.Name == null || (!IO.File.Exists($"{dlna_path}/{t.Torrent.Name}") && !Directory.Exists($"{dlna_path}/{t.Torrent.Name}")))
                        {
                            playlist.Add(new DlnaModel()
                            {
                                type = "file",
                                name = t.Torrent?.Name ?? t.InfoHashes.V1.ToHex(),
                                img = getImage(t.InfoHashes.V1.ToHex())
                            });
                        }
                    }
                }
            }
            #endregion

            return ContentTo(JsonConvert.SerializeObject(playlist.OrderByDescending(i => i.creationTime), jSettings));
        }

        return ContentTo(JsonConvert.SerializeObject(playlist.OrderBy(i =>
        {
            ulong.TryParse(Regex.Replace(i.name, "[^0-9]+", ""), out ulong ident);
            return ident;

        }), jSettings));
    }
    #endregion

    #region Stream
    [HttpGet]
    [Route("dlna/stream")]
    public ActionResult Stream(string path)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        string full = SafePath(path);
        if (full == null || !IO.File.Exists(full))
        {
            return NotFound();
        }

        string contentType = "application/octet-stream";

        if (path.EndsWith(".jpg"))
            contentType = "image/jpeg";

        return File(IO.File.OpenRead(full), contentType, true);
    }
    #endregion

    #region Delete
    [HttpPost, HttpDelete]
    [Route("dlna/delete")]
    public ActionResult Delete(string path)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        string full = SafePath(path);

        if (full == null || full == Path.GetFullPath(dlna_path))
        {
            return Content(string.Empty);
        }

        try
        {
            IO.File.Delete(full);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_p39kslrp");
        }

        try
        {
            Directory.Delete(full, true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_k4f5xgy3");
        }

        return Content(string.Empty);
    }
    #endregion


    #region Managers
    [HttpGet]
    [Route("dlna/tracker/managers")]
    public ActionResult Managers()
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        if (torrentEngine?.Torrents == null)
            return Content("[]");

        return Json(torrentEngine.Torrents.Select(i => new
        {
            InfoHash = i.InfoHashes.V1.ToHex(),
            Name = i.Torrent?.Name ?? i.InfoHashes.V1.ToHex(),
            //Engine = new
            //{
            //    i.Engine.ConnectionManager.HalfOpenConnections,
            //    i.Engine.ConnectionManager.OpenConnections,
            //    i.Engine.TotalDownloadSpeed,
            //    i.Engine.TotalUploadSpeed,
            //},
            Files = i.Files?.Select(f => new
            {
                f.Path,
                Priority = f.Priority.ToString(),
                f.Length,
                BytesDownloaded = f.BytesDownloaded()
            }),
            i.Monitor,
            i.OpenConnections,
            i.PartialProgress,
            i.Progress,
            i.Peers,
            State = i.State.ToString(),
            i.UploadingTo
        }));
    }
    #endregion

    #region Show
    [HttpGet]
    [Route("dlna/tracker/show")]
    async public Task<JsonResult> Show(string path)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        try
        {
            var tparse = await getTorrent(path);
            if (tparse.torrent != null)
                return Json(Torrent.Load(tparse.torrent).Files.Select(i => new { i.Path }));

            if (tparse.magnet == null)
                return Json(new { error = "magnet" });

            string hash = Regex.Match(tparse.magnet, "btih:([a-z0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value.ToLower();
            if (IO.File.Exists($"cache/torrent/{hash}"))
                return Json(Torrent.Load(IO.File.ReadAllBytes($"cache/torrent/{hash}")).Files.Select(i => new { i.Path }));

            using var s_cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            string magnet = tparse.magnet;
            magnet += (magnet.Contains("?") ? "&" : "?") + defTrackers;

            #region trackers
            //if (IO.File.Exists("cache/trackers.txt") && AppInit.conf.dlna.addTrackersToMagnet)
            //{
            //    foreach (string line in IO.File.ReadLines("cache/trackers.txt"))
            //    {
            //        if (string.IsNullOrWhiteSpace(line))
            //            continue;

            //        if (line.StartsWith("http") || line.StartsWith("udp:"))
            //        {
            //            string host = line.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
            //            string tr = HttpUtility.UrlEncode(host);

            //            if (!magnet.Contains(tr))
            //                magnet += $"&tr={tr}";
            //        }
            //    }
            //}
            #endregion

            await bullderClientEngine();

            if (torrentEngine.Torrents.FirstOrDefault(i => i.InfoHashes.V1.ToHex().ToLower() == hash) is TorrentManager manager)
            {
                if (manager.Files != null)
                    return Json(manager.Files.Select(i => (ITorrentFile)i).Select(i => new { i.Path }));

                await manager.WaitForMetadataAsync(s_cts.Token);
                var files = manager.Files.Select(i => (ITorrentFile)i);
                return Json(files.Select(i => new { i.Path }));
            }

            var data = await torrentEngine.DownloadMetadataAsync(MagnetLink.Parse(magnet), s_cts.Token);
            if (data.IsEmpty)
                return Json(new { error = "DownloadMetadata" });

            IO.File.WriteAllBytes($"cache/torrent/{hash}", data.Span);

            return Json(Torrent.Load(data.Span).Files.Select(i => new { i.Path }));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_a1cad0cd");
            return Json(new { error = ex.ToString() });
        }
    }
    #endregion

    #region Download
    [HttpPost]
    [Route("dlna/tracker/download")]
    async public Task<JsonResult> Download(string path, int[] indexs, string thumb, long id, bool serial, int lastCount = -1)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        try
        {
            var tparse = await getTorrent(path);
            if (tparse.magnet == null)
                return Json(new { error = "magnet" });

            // cache metadata
            string hash = Regex.Match(tparse.magnet, "btih:([a-z0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value.ToLower();
            if (IO.File.Exists($"cache/torrent/{hash}") && !IO.File.Exists($"cache/metadata/{hash.ToUpper()}.torrent"))
                IO.File.Copy($"cache/torrent/{hash}", $"cache/metadata/{hash.ToUpper()}.torrent");

            var magnetLink = MagnetLink.Parse(tparse.magnet + (tparse.magnet.Contains("?") ? "&" : "?") + defTrackers);

            await bullderClientEngine();
            TorrentManager manager = torrentEngine.Torrents.FirstOrDefault(i => i.InfoHashes.V1.ToHex() == magnetLink.InfoHashes.V1.ToHex());

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                try
                {
                    #region Download thumb
                    if (thumb != null)
                    {
                        try
                        {
                            #region IsValidImg
                            bool IsValidImg(byte[] _img)
                            {
                                if (CoreInit.conf.imagelibrary == "NetVips")
                                    return IsValidImgage(_img, path);

                                return true;
                            }
                            #endregion

                            string uri = Regex.Replace(thumb, "^https?://[^/]+/", "");

                            var array = await Http.Download($"https://image.tmdb.org/{uri}", timeoutSeconds: 10);
                            if (array == null || !IsValidImg(array))
                                array = await Http.Download($"https://imagetmdb.{CoreInit.conf.cub.mirror}/{uri}", timeoutSeconds: 10);

                            if (array != null && IsValidImg(array))
                            {
                                Directory.CreateDirectory($"{dlna_path}/thumbs");
                                IO.File.WriteAllBytes($"{dlna_path}/thumbs/{magnetLink.InfoHashes.V1.ToHex()}.jpg", array);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_dswoc7j1");
                        }
                    }
                    #endregion

                    if (manager == null)
                    {
                        dynamic tlink = tparse.torrent != null ? Torrent.Load(tparse.torrent) : magnetLink;
                        manager = ModInit.conf.mode == "stream" ? await torrentEngine.AddStreamingAsync(tlink, $"{dlna_path}/") : await torrentEngine.AddAsync(tlink, $"{dlna_path}/");

                        #region AddTrackerAsync
                        if (IO.File.Exists("cache/trackers.txt") && ModInit.conf.addTrackersToMagnet)
                        {
                            foreach (string host in IO.File.ReadLines("cache/trackers.txt").OrderBy(x => Random.Shared.Next()))
                            {
                                if (string.IsNullOrWhiteSpace(host))
                                    continue;

                                try
                                {
                                    await manager.TrackerManager.AddTrackerAsync(new Uri(host));
                                }
                                catch (System.Exception ex)
                                {
                                    Log.Error(ex, "CatchId={CatchId}", "id_z79t5hpv");
                                }
                            }
                        }
                        #endregion
                    }

                    await manager.StartAsync();
                    await manager.WaitForMetadataAsync();

                    #region thumb
                    if (IO.File.Exists($"{dlna_path}/thumbs/{magnetLink.InfoHashes.V1.ToHex()}.jpg"))
                    {
                        try
                        {
                            IO.File.Copy($"{dlna_path}/thumbs/{magnetLink.InfoHashes.V1.ToHex()}.jpg", $"{dlna_path}/thumbs/{CrypTo.md5(manager.Torrent.Name)}.jpg", true);
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_owmv3xsm");
                        }
                    }
                    #endregion

                    #region TorrentStateChanged
                    bool dispose = false;

                    manager.TorrentStateChanged += async (s, e) =>
                    {
                        try
                        {
                            if (!dispose && e != null && (e.NewState == TorrentState.Seeding || e.NewState == TorrentState.Stopped || e.NewState == TorrentState.Stopping))
                            {
                                dispose = true;

                                try
                                {
                                    IO.File.Delete($"cache/metadata/{e.TorrentManager.InfoHashes.V1.ToHex()}.torrent");
                                    IO.File.Delete($"cache/metadata/{e.TorrentManager.InfoHashes.V1.ToHex()}.json");
                                }
                                catch (System.Exception ex)
                                {
                                    Log.Error(ex, "CatchId={CatchId}", "id_ts9w2iox");
                                }

                                foreach (var f in e.TorrentManager.Files)
                                {
                                    try
                                    {
                                        if (f.Priority == Priority.DoNotDownload && IO.File.Exists(f.FullPath))
                                        {
                                            if (f.Length == 0 || f.BytesDownloaded() == 0)
                                            {
                                                IO.File.Delete(f.FullPath);
                                            }
                                            else
                                            {
                                                double percentageDownloaded = (double)f.BytesDownloaded() / f.Length;

                                                if (percentageDownloaded < 0.9)
                                                    IO.File.Delete(f.FullPath);
                                            }
                                        }
                                    }
                                    catch (System.Exception ex)
                                    {
                                        Log.Error(ex, "CatchId={CatchId}", "id_vfghgihi");
                                    }
                                }

                                await removeTorrentManager(e.TorrentManager.InfoHashes.V1.ToHex());
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_blszlopk");
                        }
                    };
                    #endregion

                    #region lastCount
                    if (lastCount > 0 && manager.Files.Count >= lastCount)
                    {
                        var _indexs = new List<int>();
                        for (int i = manager.Files.Count - 1; i >= 0; i--)
                        {
                            if (_indexs.Count == lastCount)
                                break;

                            if (!Regex.IsMatch(Path.GetExtension(manager.Files[i].Path), ModInit.conf.mediaPattern))
                                continue;

                            _indexs.Add(i);
                        }

                        indexs = _indexs.ToArray();
                    }
                    #endregion

                    #region indexs
                    if (indexs == null || indexs.Length == 0)
                    {
                        foreach (var file in manager.Files)
                        {
                            if (file.Priority != Priority.Normal)
                                await manager.SetFilePriorityAsync(file, Priority.Normal);
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory("cache/metadata/");
                        IO.File.WriteAllText($"cache/metadata/{manager.InfoHashes.V1.ToHex()}.json", JsonConvert.SerializeObject(indexs));

                        for (int i = 0; i < manager.Files.Count; i++)
                        {
                            if (indexs.Contains(i))
                            {
                                if (manager.Files[i].Priority != Priority.Normal)
                                    await manager.SetFilePriorityAsync(manager.Files[i], Priority.Normal);
                            }
                            else
                            {
                                if (manager.Files[i].Priority != Priority.DoNotDownload)
                                    await manager.SetFilePriorityAsync(manager.Files[i], Priority.DoNotDownload);
                            }
                        }
                    }
                    #endregion

                    #region tmdb
                    string cat = serial ? "tv" : "movie";
                    var header = HeadersModel.Init("lcrqpasswd", CoreInit.rootPasswd);
                    string json = await Http.Get($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/tmdb/api/3/{cat}/{id}?api_key={CoreInit.conf.cub.api_key}&language=ru", timeoutSeconds: 20, headers: header);

                    if (string.IsNullOrEmpty(json))
                        json = await Http.Get($"https://apitmdb.{CoreInit.conf.cub.mirror}/3/{cat}/{id}?api_key={CoreInit.conf.cub.api_key}&language=ru", timeoutSeconds: 20);

                    if (!string.IsNullOrEmpty(json))
                    {
                        IO.File.WriteAllText($"{dlna_path}/tmdb/{CrypTo.md5(manager.Torrent.Name)}.json", json);

                        if (serial)
                        {
                            if (int.TryParse(Regex.Match(json, "\"number_of_seasons\":([0-9 ]+)").Groups[1].Value.Trim(), out int number_of_seasons) && number_of_seasons > 0)
                            {
                                async ValueTask write(int s)
                                {
                                    string seasons = await Http.Get($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/tmdb/api/3/{cat}/{id}/season/{s}?api_key={CoreInit.conf.cub.api_key}&language=ru", timeoutSeconds: 20, headers: header);

                                    if (string.IsNullOrEmpty(seasons))
                                        seasons = await Http.Get($"https://apitmdb.{CoreInit.conf.cub.mirror}/3/{cat}/{id}/season/{s}?api_key={CoreInit.conf.cub.api_key}&language=ru", timeoutSeconds: 20);

                                    if (!string.IsNullOrEmpty(seasons))
                                        IO.File.WriteAllText($"{dlna_path}/tmdb/{id}_season-{s}.json", json);
                                }

                                if (number_of_seasons == 1)
                                    await write(number_of_seasons);
                                else
                                {
                                    foreach (var f in manager.Files)
                                    {
                                        int s = getSeason(Path.GetFileName(f.Path));
                                        if (s > 0)
                                            await write(s);
                                    }
                                }
                            }
                        }
                    }
                    #endregion
                }
                catch (System.Exception ex)
                {
                    Log.Error(ex, "CatchId={CatchId}", "id_rxy0f8nf");
                }
            });
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_652a0c61");
            return Json(new { error = ex.ToString() });
        }

        return Json(new { status = true });
    }
    #endregion


    #region Delete
    [HttpPost, HttpDelete]
    [Route("dlna/tracker/stop")]
    [Route("dlna/tracker/delete")]
    async public Task<JsonResult> TorrentDelete(string infohash)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        if (torrentEngine == null)
            return Json(new { });

        var manager = torrentEngine.Torrents.FirstOrDefault(i => i.InfoHashes.V1.ToHex() == infohash);
        if (manager != null)
        {
            try
            {
                IO.File.Delete($"cache/metadata/{manager.InfoHashes.V1.ToHex()}.torrent");
                IO.File.Delete($"cache/metadata/{manager.InfoHashes.V1.ToHex()}.json");
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_ehvvti8b");
            }

            await removeTorrentManager(manager.InfoHashes.V1.ToHex());
        }

        return Json(new { status = true });
    }
    #endregion

    #region Pause
    [HttpPost]
    [Route("dlna/tracker/pause")]
    async public Task<JsonResult> TorrentPause(string infohash)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        if (torrentEngine == null)
            return Json(new { });

        var manager = torrentEngine.Torrents.FirstOrDefault(i => i.InfoHashes.V1.ToHex() == infohash);
        if (manager != null)
            await manager.PauseAsync();

        return Json(new { status = true });
    }
    #endregion

    #region Start
    [HttpPost]
    [Route("dlna/tracker/start")]
    async public Task<JsonResult> TorrentStart(string infohash)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        if (torrentEngine == null)
            return Json(new { });

        var manager = torrentEngine.Torrents.FirstOrDefault(i => i.InfoHashes.V1.ToHex() == infohash);
        if (manager != null)
            await manager.StartAsync();

        return Json(new { status = true });
    }
    #endregion

    #region ChangeFilePriority
    [HttpPost]
    [Route("dlna/tracker/changefilepriority")]
    async public Task<JsonResult> ChangeFilePriority(string infohash, int[] indexs)
    {
        if (!HasDlnaAccess())
            return DlnaAccessDenied();

        if (torrentEngine == null)
            return Json(new { });

        var manager = torrentEngine.Torrents.FirstOrDefault(i => i.InfoHashes.V1.ToHex() == infohash);
        if (manager != null)
        {
            if (indexs == null || indexs.Length == 0)
            {
                foreach (var file in manager.Files)
                {
                    if (file.Priority != Priority.Normal)
                        await manager.SetFilePriorityAsync(file, Priority.Normal);
                }

                if (IO.File.Exists($"cache/metadata/{manager.InfoHashes.V1.ToHex()}.json"))
                    IO.File.Delete($"cache/metadata/{manager.InfoHashes.V1.ToHex()}.json");
            }
            else
            {
                Directory.CreateDirectory("cache/metadata/");
                IO.File.WriteAllText($"cache/metadata/{manager.InfoHashes.V1.ToHex()}.json", JsonConvert.SerializeObject(indexs));

                for (int i = 0; i < manager.Files.Count; i++)
                {
                    if (indexs.Contains(i))
                    {
                        if (manager.Files[i].Priority != Priority.Normal)
                            await manager.SetFilePriorityAsync(manager.Files[i], Priority.Normal);
                    }
                    else
                    {
                        if (manager.Files[i].Priority != Priority.DoNotDownload)
                            await manager.SetFilePriorityAsync(manager.Files[i], Priority.DoNotDownload);
                    }
                }
            }
        }

        return Json(new { status = true });
    }
    #endregion



    #region IsValidImgage
    static bool IsValidImgage(byte[] _img, string path)
    {
        if (_img == null)
            return false;

        using (var image = Image.NewFromBuffer(_img))
        {
            try
            {
                if (!path.Contains(".svg"))
                {
                    // тестируем jpg/png на целостность
                    byte[] temp = image.JpegsaveBuffer();
                    if (temp == null || temp.Length == 0)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    static string RelativeDlnaPath(string fullPath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dlna_path));
        return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
    }

    static string SafePath(string path)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dlna_path));
            string full = Path.GetFullPath(Path.Combine(root, path ?? string.Empty));
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(full, root, comparison))
            {
                return full;
            }

            if (!full.StartsWith(root + Path.DirectorySeparatorChar, comparison)
                && !full.StartsWith(root + Path.AltDirectorySeparatorChar, comparison))
            {
                return null;
            }

            // GetFullPath only performs lexical normalization. Reject links and
            // junctions so an in-root path cannot resolve outside the DLNA root.
            string relative = Path.GetRelativePath(root, full);
            string current = root;

            foreach (string segment in relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);

                if (!IO.File.Exists(current) && !Directory.Exists(current))
                    break;

                if ((IO.File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return null;
            }

            return full;
        }
        catch
        {
            return null;
        }
    }
    #endregion
}
