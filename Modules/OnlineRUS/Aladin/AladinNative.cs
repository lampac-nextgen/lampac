using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Aladin;

/// <summary>
/// Resolves Alloha player pages and keeps the signed CDN URLs and the
/// telemetry socket alive for the whole playback session.
/// </summary>
internal static class AladinNative
{
    static readonly ConcurrentDictionary<string, NativeStream> _live = new();
    static readonly ConcurrentDictionary<string, DateTime> _deadHosts = new();
    static readonly ConcurrentDictionary<string, Task<NativeStream>> _inflight = new();
    static readonly ConcurrentDictionary<string, (NativeStream Stream, DateTime At)> _resolved = new();

    // The balancer refuses bursts of telemetry handshakes; two at a time is
    // enough and keeps a slow handshake from blocking a resolve.
    static SemaphoreSlim _wsGate = new(2, 2);
    static int _wsLimit = 2;
    internal static SemaphoreSlim WsGate => _wsGate;

    public static void Configure(ModuleConf init)
    {
        int limit = Math.Max(1, init.wsHandshakeConcurrency);
        if (limit == _wsLimit)
            return;

        _wsGate = new SemaphoreSlim(limit, limit);
        _wsLimit = limit;
    }

    static readonly Regex ViewportRegex = new(
        @"<meta\s+name=[""']viewporti[""']\s+content=[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static readonly Regex FileListRegex = new(
        @"fileList\s*=\s*JSON\.parse\('([^']+)'\);",
        RegexOptions.Compiled);

    static readonly Regex FileListRegex2 = new(
        @"fileList\s*=\s*JSON\.parse\(""([^""]+)""\);",
        RegexOptions.Compiled);

    public static async Task<NativeStream> ResolveAsync(
        ModuleConf init,
        long kinopoiskId,
        string imdbId,
        string tokenMovie,
        int translation,
        int season,
        int episode,
        string preferHost = null)
    {
        string cacheKey = $"{init.token}|{kinopoiskId}|{imdbId}|{tokenMovie}|{translation}|{season}|{episode}|{preferHost}";

        if (_resolved.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(Math.Max(60, init.urlTtl)))
        {
            return cached.Stream;
        }

        var task = _inflight.GetOrAdd(cacheKey, _ => Task.Run(() =>
        {
            try
            {
                return ResolveSync(init, kinopoiskId, imdbId, tokenMovie, translation, season, episode, preferHost);
            }
            catch
            {
                return null;
            }
        }));

        try
        {
            var stream = await task.WaitAsync(TimeSpan.FromSeconds(Math.Max(1, init.resolveTimeout)));
            if (stream != null)
            {
                _resolved[cacheKey] = (stream, DateTime.UtcNow);
                PruneResolved();
            }

            return stream;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inflight.TryRemove(cacheKey, out _);
        }
    }

    static void PruneResolved()
    {
        if (_resolved.Count <= 512)
            return;

        var now = DateTime.UtcNow;
        foreach (var pair in _resolved)
        {
            if (now - pair.Value.At > TimeSpan.FromHours(2))
                _resolved.TryRemove(pair.Key, out _);
        }

        if (_resolved.Count > 512)
            _resolved.Clear();
    }

    static NativeStream ResolveSync(
        ModuleConf init,
        long kinopoiskId,
        string imdbId,
        string tokenMovie,
        int translation,
        int season,
        int episode,
        string preferHost)
    {
        var browser = new AladinBrowser(init);
        var hosts = HostPool(init, preferHost);

        foreach (string host in hosts)
        {
            if (IsDead(host))
                continue;

            try
            {
            string playerUrl = BuildPlayerUrl(host, init, tokenMovie, kinopoiskId, imdbId, translation, season, episode);
            string html = null;
            string viewporti = null;
            JObject fileList = null;

            for (int attempt = 0; attempt < Math.Max(1, init.httpRetries); attempt++)
            {
                var page = browser.Get(playerUrl, AladinRequestKind.Page, init.referer);
                if (page.Status == 200 && !string.IsNullOrEmpty(page.Body))
                {
                    html = page.Body;
                    viewporti = ParseViewport(html);
                    fileList = ParseFileList(html);
                    if (!string.IsNullOrEmpty(viewporti))
                        break;
                }

                Thread.Sleep(400 * (attempt + 1));
            }

            if (string.IsNullOrEmpty(viewporti))
                continue;

            string actId = ActIdFor(fileList, translation, season, episode);
            if (string.IsNullOrEmpty(actId))
                continue;

            string borth = AladinBorth.Compute(viewporti, init.fingerprintHash);
            if (string.IsNullOrEmpty(borth))
                continue;

            string body = new StringBuilder()
                .Append("token=").Append(Uri.EscapeDataString(init.token ?? ""))
                .Append("&av1=true")
                .Append("&autoplay=0")
                .Append("&audio=")
                .Append("&subtitle=")
                .ToString();

            var bnsi = browser.Send(
                "POST",
                $"{host}/bnsi/movies/{actId}",
                body,
                AladinRequestKind.Xhr,
                referer: playerUrl,
                origin: host,
                timeoutMilliseconds: 15000,
                edgeHash: null,
                borth: borth);

            if (bnsi.Status != 200)
            {
                if (translation == 0 && season == 0 && episode == 0)
                    MarkDead(host, TimeSpan.FromMinutes(10));

                continue;
            }

            JObject payload;
            try
            {
                payload = JObject.Parse(bnsi.Body ?? "{}");
            }
            catch
            {
                continue;
            }

            var (voices, defaultLabel) = CollectVoices(payload["hlsSource"] as JArray);
            if (voices.Count == 0)
                continue;

            var stream = new NativeStream
            {
                Key = $"{host}|{actId}|{translation}|{season}|{episode}",
                LinkHost = host,
                ActId = actId,
                TokenMovie = tokenMovie ?? "",
                Translation = translation.ToString(),
                Season = season,
                Episode = episode,
                PlayerUrl = playerUrl,
                Voices = voices,
                DefaultLabel = defaultLabel,
                Pnr = payload["pnr"]?.ToString() ?? "",
                Pnk = payload["pnk"]?.ToString() ?? "",
                Tracks = ParseTracks(payload["tracks"] as JArray),
                ResolvedAt = DateTime.UtcNow,
                Browser = browser
            };

            stream.RegisterDirs();

            if (!string.IsNullOrEmpty(stream.Pnr) && !string.IsNullOrEmpty(stream.Pnk))
            {
                stream.Ws = new NativeWsSession(init, stream, browser);
                stream.Ws.Start();
                stream.Ws.WaitForEdge(1000);
            }

            Register(stream, init);
            return stream;
            }
            catch
            {
                // A host that timed out or answered with a broken page must not
                // abort the walk: the next mirror usually has a warm copy.
            }
        }

        return null;
    }

    public static NativeStream Get(string id, int ttlSeconds = 14400)
    {
        if (string.IsNullOrEmpty(id) || !_live.TryGetValue(id, out var stream))
            return null;

        if (DateTime.UtcNow - stream.ResolvedAt > TimeSpan.FromSeconds(ttlSeconds))
        {
            _live.TryRemove(id, out _);
            stream.Ws?.Close();
            return null;
        }

        return stream;
    }

    public static string PickMirror(NativeStream stream, string voice, string quality)
    {
        if (stream == null)
            return null;

        lock (stream.Sync)
        {
            if (!stream.Voices.TryGetValue(voice, out var qualities) ||
                !qualities.TryGetValue(quality, out var mirrors) ||
                mirrors.Count == 0)
                return null;

            foreach (string mirror in mirrors)
            {
                if (!IsCdnDead(HostOf(mirror)))
                    return mirror;
            }

            return mirrors[0];
        }
    }

    public static NativeStream Refresh(NativeStream stream, ModuleConf init)
    {
        if (stream == null)
            return null;

        if (DateTime.UtcNow - stream.LastRefresh < TimeSpan.FromSeconds(2))
            return stream;

        stream.LastRefresh = DateTime.UtcNow;

        var oldWs = stream.Ws;
        oldWs?.Close();
        stream.Ws = null;

        int translation = int.TryParse(stream.Translation, out int parsed) ? parsed : 0;
        var fresh = ResolveSync(init, 0, null, stream.TokenMovie, translation, stream.Season, stream.Episode, stream.LinkHost);
        if (fresh == null)
            return stream;

        _live.TryRemove(fresh.Id, out _);

        lock (stream.Sync)
        {
            stream.Voices = fresh.Voices;
            stream.DefaultLabel = fresh.DefaultLabel;
            stream.LinkHost = fresh.LinkHost;
            stream.ActId = fresh.ActId;
            stream.PlayerUrl = fresh.PlayerUrl;
            stream.Pnr = fresh.Pnr;
            stream.Pnk = fresh.Pnk;
            stream.Tracks = fresh.Tracks;
            stream.Browser = fresh.Browser;
            stream.ResolvedAt = fresh.ResolvedAt;
            if (!string.IsNullOrEmpty(fresh.CdnEdge))
                stream.CdnEdge = fresh.CdnEdge;
            stream.RegisterDirs();
        }

        stream.Ws = fresh.Ws;
        stream.Ws?.Rebind(stream);
        _live[stream.Id] = stream;

        foreach (var pair in _resolved)
        {
            if (ReferenceEquals(pair.Value.Stream, stream))
                _resolved[pair.Key] = (stream, DateTime.UtcNow);
        }

        return stream;
    }

    public static void Register(NativeStream stream, ModuleConf init)
    {
        _live[stream.Id] = stream;

        int maxWs = Math.Max(1, init.wsMaxSessions);
        var wsStreams = _live.Values
            .Where(x => x.Ws != null)
            .OrderBy(x => x.ResolvedAt)
            .ToList();

        for (int i = 0; i < wsStreams.Count - maxWs; i++)
        {
            var old = wsStreams[i];
            if (ReferenceEquals(old, stream))
                continue;

            old.Ws?.Close();
            old.Ws = null;
        }

        if (_live.Count <= Math.Max(1, init.liveMaxStreams))
            return;

        foreach (var pair in _live.OrderBy(x => x.Value.ResolvedAt).Take(_live.Count - Math.Max(1, init.liveMaxStreams)))
        {
            if (pair.Key == stream.Id)
                continue;

            _live.TryRemove(pair.Key, out var dropped);
            dropped?.Ws?.Close();
        }
    }

    public static string CdnControls(NativeStream stream, ModuleConf init)
    {
        string edge = stream?.Ws?.EdgeHash;
        if (string.IsNullOrEmpty(edge))
            edge = stream?.CdnEdge;
        if (string.IsNullOrEmpty(edge))
            edge = init.fingerprintHash;
        return string.IsNullOrEmpty(edge) ? null : edge;
    }

    public static string VoiceKey(string label)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(label ?? ""));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    public static string Rebuild(NativeStream stream, string url)
    {
        if (stream == null || string.IsNullOrEmpty(url))
            return url;

        int slash = url.LastIndexOf('/');
        if (slash <= 0)
            return url;

        string directory = url[..slash];
        string filename = url[(slash + 1)..];

        lock (stream.Sync)
        {
            if (!stream.Dirs.TryGetValue(directory, out var info))
                return url;

            string newDirectory = CurrentDirectory(stream, info.Label, info.Quality, info.Index);
            return string.IsNullOrEmpty(newDirectory) ? url : $"{newDirectory}/{filename}";
        }
    }

    static string CurrentDirectory(NativeStream stream, string label, string quality, int index)
    {
        if (!stream.Voices.TryGetValue(label, out var qualities) || !qualities.TryGetValue(quality, out var mirrors) || mirrors.Count == 0)
            return null;

        for (int offset = 0; offset < mirrors.Count; offset++)
        {
            string mirror = mirrors[(index + offset) % mirrors.Count];
            string host = HostOf(mirror);
            if (!IsCdnDead(host))
                return mirror[..mirror.LastIndexOf('/')];
        }

        string last = mirrors[Math.Min(index, mirrors.Count - 1)];
        return last[..last.LastIndexOf('/')];
    }

    public static void MarkCdnDead(string host)
    {
        if (!string.IsNullOrEmpty(host))
            _deadHosts[host] = DateTime.UtcNow.AddHours(2);
    }

    static bool IsCdnDead(string host)
    {
        if (string.IsNullOrEmpty(host))
            return false;

        if (_deadHosts.TryGetValue(host, out var until))
        {
            if (until > DateTime.UtcNow)
                return true;

            _deadHosts.TryRemove(host, out _);
        }

        return false;
    }

    static void MarkDead(string host, TimeSpan duration)
        => _deadHosts[host] = DateTime.UtcNow.Add(duration);

    static bool IsDead(string host)
    {
        if (!_deadHosts.TryGetValue(host, out var until))
            return false;

        if (until > DateTime.UtcNow)
            return true;

        _deadHosts.TryRemove(host, out _);
        return false;
    }

    static string HostOf(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return null;
        }
    }

    static List<string> HostPool(ModuleConf init, string preferHost)
    {
        var hosts = new List<string>();

        void Add(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            string host = value.Trim().TrimEnd('/');
            if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                host = "https://" + host;

            if (!hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                hosts.Add(host);
        }

        Add(preferHost);

        if (init.linkhosts != null)
        {
            foreach (string host in init.linkhosts)
                Add(host);
        }

        Add(init.linkhost);

        Add("https://scalp-as.stloadi.live");
        Add("https://quadrillion-as.allarknow.online");
        Add("https://Aport-as.allarknow.online");
        return hosts;
    }

    static string BuildPlayerUrl(
        string host,
        ModuleConf init,
        string tokenMovie,
        long kinopoiskId,
        string imdbId,
        int translation,
        int season,
        int episode)
    {
        var sb = new StringBuilder(host).Append("/?token=").Append(Uri.EscapeDataString(init.token ?? ""));

        if (!string.IsNullOrEmpty(tokenMovie))
        {
            sb.Append("&token_movie=").Append(Uri.EscapeDataString(tokenMovie));
            if (translation > 0)
                sb.Append("&translation=").Append(translation);
            if (season > 0)
                sb.Append("&season=").Append(season);
            if (episode > 0)
                sb.Append("&episode=").Append(episode);
        }
        else if (kinopoiskId > 0)
        {
            sb.Append("&kp=").Append(kinopoiskId);
            if (translation > 0)
                sb.Append("&translation=").Append(translation);
        }
        else if (!string.IsNullOrEmpty(imdbId))
        {
            sb.Append("&imdb=").Append(Uri.EscapeDataString(imdbId));
        }

        return sb.ToString();
    }

    static string ParseViewport(string html)
    {
        var match = ViewportRegex.Match(html ?? "");
        return match.Success ? match.Groups[1].Value : null;
    }

    static JObject ParseFileList(string html)
    {
        var match = FileListRegex.Match(html ?? "");
        if (!match.Success)
            match = FileListRegex2.Match(html ?? "");

        if (!match.Success)
            return null;

        try
        {
            return JObject.Parse(match.Groups[1].Value);
        }
        catch
        {
            return null;
        }
    }

    static string ActIdFor(JObject fileList, int translation, int season, int episode)
    {
        if (fileList == null)
            return null;

        var active = fileList["active"] as JObject;
        string activeId = active?["id"]?.ToString();
        string trKey = translation > 0 ? "t" + translation : null;

        if (translation == 0 && season == 0 && episode == 0)
            return string.IsNullOrEmpty(activeId) ? null : activeId;

        var all = fileList["all"] as JObject;

        string CandidateAct(JToken token)
        {
            if (token is not JObject candidate)
                return null;

            if (trKey != null)
            {
                var nested = candidate[trKey] as JObject;
                if (nested?["id"] != null)
                    return nested["id"].ToString();

                if (candidate["id_translation"]?.ToString() == translation.ToString() && candidate["id"] != null)
                    return candidate["id"].ToString();

                return null;
            }

            return candidate["id"]?.ToString();
        }

        if (season > 0 && episode > 0)
        {
            var seasonData = all?[season.ToString()];
            var candidates = new List<JToken>();

            if (seasonData is JObject seasonObject)
            {
                var episodeData = seasonObject[episode.ToString()];
                if (episodeData is JArray episodeArray)
                    candidates.AddRange(episodeArray);
                else if (episodeData != null)
                    candidates.Add(episodeData);
            }
            else if (seasonData is JArray seasonArray)
            {
                candidates.AddRange(seasonArray.Where(x =>
                    x is JObject item &&
                    (item["episode"] == null || item["episode"].ToString() == episode.ToString())));

                if (candidates.Count == 0)
                    candidates.AddRange(seasonArray);
            }

            foreach (var candidate in candidates)
            {
                string act = CandidateAct(candidate);
                if (!string.IsNullOrEmpty(act))
                    return act;
            }
        }

        if (trKey != null)
        {
            if (all?["theatrical"]?[trKey] is JObject theatrical)
            {
                foreach (var value in theatrical.Properties())
                {
                    if (value.Value is JObject entry && entry["id"] != null)
                        return entry["id"].ToString();
                }
            }

            if (all?["directors"]?[trKey] is JObject directors)
            {
                foreach (var value in directors.Properties())
                {
                    if (value.Value is JObject entry && entry["id"] != null)
                        return entry["id"].ToString();
                }
            }
        }

        return string.IsNullOrEmpty(activeId) ? null : activeId;
    }

    static (Dictionary<string, Dictionary<string, List<string>>>, string) CollectVoices(JArray hlsSources)
    {
        var voices = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        string defaultLabel = null;

        if (hlsSources == null)
            return (voices, defaultLabel);

        foreach (var token in hlsSources)
        {
            if (token is not JObject source)
                continue;

            string label = source["label"]?.ToString().Trim() ?? "";
            if (label.Length == 0)
                continue;

            var qualityObject = source["quality"] as JObject;
            if (qualityObject == null || qualityObject.Count == 0)
                continue;

            var qualities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in qualityObject.Properties())
            {
                string value = property.Value?.ToString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var mirrors = value
                    .Split([" or "], StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();

                if (mirrors.Count == 0)
                    mirrors.Add(value.Trim());

                qualities[property.Name] = mirrors;
            }

            if (qualities.Count == 0)
                continue;

            voices[label] = qualities;
            if (source["default"]?.Value<bool>() == true && defaultLabel == null)
                defaultLabel = label;
        }

        return (voices, defaultLabel);
    }

    static List<Track> ParseTracks(JArray tracks)
    {
        var result = new List<Track>();
        if (tracks == null)
            return result;

        foreach (var token in tracks)
        {
            if (token is not JObject track)
                continue;

            string label = track["label"]?.ToString();
            string src = track["src"]?.ToString();
            if (!string.IsNullOrEmpty(src))
                result.Add(new Track { label = label, src = src });
        }

        return result;
    }
}

internal sealed class NativeStream
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Key { get; set; }
    public string LinkHost { get; set; }
    public string ActId { get; set; }
    public string TokenMovie { get; set; }
    public string Translation { get; set; }
    public int Season { get; set; }
    public int Episode { get; set; }
    public string PlayerUrl { get; set; }
    public Dictionary<string, Dictionary<string, List<string>>> Voices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DefaultLabel { get; set; }
    public string Pnr { get; set; }
    public string Pnk { get; set; }
    public string CdnEdge { get; set; }
    public List<Track> Tracks { get; set; } = new();
    public DateTime ResolvedAt { get; set; }
    public DateTime LastRefresh { get; set; }
    public NativeWsSession Ws { get; set; }
    public AladinBrowser Browser { get; set; }
    public readonly object Sync = new();
    public readonly Dictionary<string, (string Label, string Quality, int Index)> Dirs = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string> Items = new(StringComparer.Ordinal);
    int _itemSeq;

    public void RegisterDirs()
    {
        lock (Sync)
        {
            foreach (var voice in Voices)
            {
                foreach (var quality in voice.Value)
                {
                    for (int index = 0; index < quality.Value.Count; index++)
                    {
                        string url = quality.Value[index];
                        int slash = url.LastIndexOf('/');
                        if (slash > 0)
                            Dirs[url[..slash]] = (voice.Key, quality.Key, index);
                    }
                }
            }

            // Keep the previous generations: a playlist handed out before a
            // refresh still points at the old signed directory.
            while (Dirs.Count > 300)
            {
                string first = Dirs.Keys.First();
                Dirs.Remove(first);
            }
        }
    }

    public string AddItem(string url)
    {
        lock (Sync)
        {
            string key = (++_itemSeq).ToString();
            Items[key] = url;
            return key;
        }
    }

    public string GetItem(string key)
    {
        lock (Sync)
            return Items.TryGetValue(key, out string url) ? url : null;
    }
}

internal sealed class NativeWsSession
{
    readonly ModuleConf _init;
    NativeStream _stream;
    readonly AladinBrowser _browser;
    readonly CancellationTokenSource _cts = new();
    readonly object _writeLock = new();
    readonly ManualResetEventSlim _edgeReady = new(false);
    CurlWebSocket _ws;
    Task _task;
    volatile bool _closed;
    int _currentSec;
    int _lastSec;
    bool _seekPending;
    DateTime _lastActive = DateTime.UtcNow;

    public NativeWsSession(ModuleConf init, NativeStream stream, AladinBrowser browser)
    {
        _init = init;
        _stream = stream;
        _browser = browser;
    }

    public string EdgeHash { get; private set; }

    public void Rebind(NativeStream stream)
        => _stream = stream;

    public void WaitForEdge(int milliseconds)
    {
        if (!string.IsNullOrEmpty(EdgeHash))
            return;

        _edgeReady.Wait(milliseconds);
    }

    public void Start()
    {
        if (_task == null)
            _task = Task.Run(RunAsync);
    }

    public void UpdateProgress(string url)
    {
        _lastActive = DateTime.UtcNow;

        var match = Regex.Match(url ?? "", @"seg-(\d+)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int segment))
            return;

        int current = segment <= 25 ? 0 : (segment - 25) * 6;
        if (_lastSec > 0 && Math.Abs(current - _lastSec) > 90)
            _seekPending = true;

        _currentSec = current;
        _lastSec = current;
    }

    async Task RunAsync()
    {
        int maxAttempts = Math.Max(1, _init.wsReconnectAttempts);
        int attempt = 0;

        while (!_closed && attempt < maxAttempts)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 30)), _cts.Token);

            if (DateTime.UtcNow - _lastActive > TimeSpan.FromHours(1))
            {
                Close();
                return;
            }

            attempt++;

            try
            {
                await ConnectAndStreamAsync();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (ContainsCloseCode(ex.Message))
                {
                    _closed = true;
                    return;
                }
            }
        }

        _closed = true;
    }

    async Task ConnectAndStreamAsync()
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string separator = _stream.Pnr.Contains('?') ? "&" : "?";
        string endpoint = $"{_stream.Pnr}{separator}sid={Uri.EscapeDataString(_stream.Pnk)}&v=2.1&t={ts}";

        string cookie = _browser.CookieHeader(_stream.LinkHost);

        await AladinNative.WsGate.WaitAsync(_cts.Token);
        try
        {
            _ws = _browser.WebSocket(endpoint, _stream.LinkHost, cookie);
        }
        finally
        {
            AladinNative.WsGate.Release();
        }

        string now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        Write($"{{\"type\":\"playback_start\",\"current_time\":0,\"resolution\":\"1080\",\"track_id\":\"1\",\"speed\":1,\"subtitle\":-1,\"ts\":{now}}}");

        var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var heartbeat = Task.Run(() => HeartbeatAsync(heartbeatCts.Token));

        try
        {
            while (!_closed && !_cts.IsCancellationRequested)
            {
                var read = _ws.Read(1000);
                if (read.TimedOut)
                    continue;

                if (read.Closed)
                    throw new InvalidOperationException(read.Error ?? "websocket closed");

                if (string.IsNullOrEmpty(read.Data))
                    continue;

                _lastActive = DateTime.UtcNow;

                var edge = Regex.Match(read.Data, "\"edge_hash\"\\s*:\\s*\"([^\"]+)\"");
                if (edge.Success)
                {
                    EdgeHash = edge.Groups[1].Value;
                    _stream.CdnEdge = EdgeHash;
                    _edgeReady.Set();

                    var ttl = Regex.Match(read.Data, "\"ttl\"\\s*:\\s*(\\d+)");
                    if (ttl.Success && int.TryParse(ttl.Groups[1].Value, out int seconds) && seconds > 0)
                        _lastActive = DateTime.UtcNow;
                }
            }
        }
        finally
        {
            try { heartbeatCts.Cancel(); } catch { }
            try { await heartbeat; } catch { }
            _ws?.Dispose();
            _ws = null;
        }
    }

    async Task HeartbeatAsync(CancellationToken token)
    {
        while (!_closed && !token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _lastActive = DateTime.UtcNow;

            if (_seekPending)
            {
                _seekPending = false;
                Write(Message("seeked"));
            }

            Write(Message("playing"));
        }
    }

    string Message(string type)
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{{\"type\":\"{type}\",\"current_time\":{_currentSec},\"resolution\":\"1080\",\"track_id\":\"1\",\"speed\":1,\"subtitle\":-1,\"ts\":{ts}}}";
    }

    void Write(string message)
    {
        lock (_writeLock)
        {
            _ws?.Write(message);
        }
    }

    static bool ContainsCloseCode(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return message.Contains("4002") || message.Contains("4003") || message.Contains("4005");
    }

    public void Close()
    {
        _closed = true;
        try { _cts.Cancel(); } catch { }
        _ws?.Dispose();
    }
}
