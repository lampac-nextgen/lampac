using System.Collections.Concurrent;
using YoutubeExplode;
using YoutubeExplode.Search;
using YoutubeExplode.Videos.Streams;

namespace Music;

public class YouTubeAudioProvider : IMusicAudioProvider
{
    // спекулятивный манифест: после первой пачки поиска манифест
    // промежуточного топ-кандидата тянется ПАРАЛЛЕЛЬНО со второй пачкой —
    // победитель ранжирования после неё почти никогда не меняется, и
    // GetStreamsAsync забирает готовый результат вместо ещё одного ~1.5s
    // запроса. Промах (победитель сменился/спекуляция упала) — обычный путь.
    static readonly ConcurrentDictionary<string, (DateTime at, Task<ResolvedManifest> task)> speculativeManifests = new();
    static readonly TimeSpan speculativeManifestTtl = TimeSpan.FromMinutes(2);

    sealed record ResolvedManifest(StreamManifest Manifest, MusicProxyLease ProxyLease);

    public string Id => "youtubeaudio";
    public string Name => "YouTube Audio";
    public bool Enabled => ModInit.conf?.youtube_audio_enabled == true;
    public bool RequiresAuth => false;
    public bool CacheMissingMatches => false;

    public async Task<IReadOnlyList<MusicAudioMatch>> MatchTrackAsync(MusicTrack track, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (track == null)
            return Array.Empty<MusicAudioMatch>();

        var queries = YouTubeAudioSupport.BuildTrackQueries(track, playbackMode);
        var directMatch = YouTubeAudioSupport.BuildDirectMatch(track);
        if (queries.Count == 0)
            return directMatch != null ? new[] { directMatch } : Array.Empty<MusicAudioMatch>();

        try
        {
            var proxyLease = MusicProxyService.Acquire(Id, MusicProxyPurpose.Api);
            using var youtube = new YoutubeClient(MusicHttp.GetTransport(proxyLease));
            var results = new List<VideoSearchResult>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // batch-parallel: пачки по 3 запроса параллельно (холодный резолв был
            // суммой 3-6 последовательных поисков по 1-2s = главная часть 5-16s
            // ожидания /music/play). Merge строго в порядке исходных queries —
            // вход ранжирования тот же, что у последовательного цикла; dedupe и
            // кап 36 на слиянии; набрали кап — следующая пачка не запускается
            bool speculated = false;

            for (int batchStart = 0; batchStart < queries.Count && results.Count < 36; batchStart += 3)
            {
                var batchTasks = queries
                    .Skip(batchStart)
                    .Take(3)
                    .Select(query => FetchQueryResultsAsync(youtube, query, cancellationToken))
                    .ToList();

                await Task.WhenAll(batchTasks);

                foreach (var batchTask in batchTasks)
                {
                    foreach (var video in batchTask.Result)
                    {
                        var videoId = video.Id.Value;
                        if (!string.IsNullOrWhiteSpace(videoId) && seen.Add(videoId))
                            results.Add(video);

                        if (results.Count >= 36)
                            break;
                    }

                    if (results.Count >= 36)
                        break;
                }

                // впереди ещё пачка — после первой пачки уже часто есть
                // очевидный победитель. Если он проходит строгий гейт
                // уверенности, не жжём вторую пачку YouTube-поисков вообще.
                // Манифест проверяем до раннего выхода: если топ-кандидат
                // оказался без стримов, сохраняем старую полную fallback-схему.
                if (directMatch == null && batchStart == 0 && batchStart + 3 < queries.Count && results.Count < 36)
                {
                    var earlyRanked = YouTubeAudioSupport.RankMatches(track, YouTubeAudioSupport.ConvertSearchResults(results), playbackMode);
                    var earlyTop = earlyRanked.FirstOrDefault();
                    if (YouTubeAudioSupport.IsConfidentEarlyMatch(track, earlyTop))
                    {
                        try
                        {
                            var manifestTask = StartSpeculativeManifest(earlyTop.id);
                            if (manifestTask != null)
                            {
                                await manifestTask.WaitAsync(cancellationToken);
                                proxyLease.Success();
                                return earlyRanked;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // stream-less первый кандидат — не меняем поведение,
                            // продолжаем полный поиск и даём ранжированию шанс.
                        }
                    }
                }

                // впереди ещё пачка — спекулятивно тянем манифест текущего
                // фаворита (directMatch, если есть — резолвер пробует его первым)
                if (!speculated && batchStart + 3 < queries.Count && results.Count < 36)
                {
                    speculated = true;
                    var prelim = YouTubeAudioSupport.RankMatches(track, YouTubeAudioSupport.ConvertSearchResults(results), playbackMode);
                    StartSpeculativeManifest(directMatch?.id ?? prelim.FirstOrDefault()?.id);
                }
            }

            var matches = YouTubeAudioSupport.ConvertSearchResults(results);
            var ranked = YouTubeAudioSupport.RankMatches(track, matches, playbackMode);
            proxyLease.Success();
            if (directMatch == null)
                return ranked;

            var ordered = new List<MusicAudioMatch> { directMatch };
            ordered.AddRange(ranked.Where(i => !string.Equals(i.id, directMatch.id, StringComparison.Ordinal)));
            return ordered;
        }
        catch (Exception ex)
        {
            if (MusicHttp.IsProxyFailure(ex))
                MusicProxyService.ReportFailure($"Music:{Id}:api");

            return directMatch != null ? new[] { directMatch } : Array.Empty<MusicAudioMatch>();
        }
    }

    // «Найти вручную» в Источниках: сырой пользовательский запрос — без
    // переписывания, ранжирования и гейта релевантности, решает человек
    public async Task<IReadOnlyList<MusicAudioMatch>> SearchMatchesByQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        query = query?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<MusicAudioMatch>();

        try
        {
            var proxyLease = MusicProxyService.Acquire(Id, MusicProxyPurpose.Api);
            using var youtube = new YoutubeClient(MusicHttp.GetTransport(proxyLease));
            var results = new List<VideoSearchResult>();

            await foreach (var video in youtube.Search.GetVideosAsync(query, cancellationToken))
            {
                results.Add(video);
                if (results.Count >= 20)
                    break;
            }

            proxyLease.Success();
            return YouTubeAudioSupport.ConvertSearchResults(results);
        }
        catch (Exception ex)
        {
            if (MusicHttp.IsProxyFailure(ex))
                MusicProxyService.ReportFailure($"Music:{Id}:api");

            return Array.Empty<MusicAudioMatch>();
        }
    }

    public async Task<IReadOnlyList<MusicPlaybackSource>> GetStreamsAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        if (match == null || string.IsNullOrWhiteSpace(match.id))
            return Array.Empty<MusicPlaybackSource>();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var resolved = attempt == 0 ? await TryTakeSpeculativeManifestAsync(match.id) : null;
                resolved ??= await ResolveManifestAsync(match.id, cancellationToken);
                if (MusicPlaybackModeService.IsVideo(playbackMode))
                {
                    var videoStreams = resolved.Manifest.GetMuxedStreams();
                    var sources = YouTubeAudioSupport.ConvertVideoStreams(videoStreams);
                    foreach (var source in sources)
                        resolved.ProxyLease.ApplyTo(source, overwrite: true);
                    return sources;
                }

                var audioStreams = resolved.Manifest.GetAudioOnlyStreams();
                var audioSources = YouTubeAudioSupport.ConvertAudioStreams(audioStreams);
                foreach (var source in audioSources)
                    resolved.ProxyLease.ApplyTo(source, overwrite: true);
                return audioSources;
            }
            catch (Exception ex)
            {
                if (attempt == 0 && ShouldRetryManifestFailure(ex))
                {
                    await Task.Delay(250, cancellationToken);
                    continue;
                }

                Console.WriteLine($"[Music] youtube stream manifest failed for {match.id}: {ex.GetType().Name}: {ex.Message}");
                return Array.Empty<MusicPlaybackSource>();
            }
        }

        return Array.Empty<MusicPlaybackSource>();
    }

    public Task<MusicPlaybackSource> TryGetPreferredStreamAsync(MusicAudioMatch match, string playbackMode = null, string profileId = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<MusicPlaybackSource>(null);
    }

    public bool IsRelevantMatch(MusicTrack track, MusicAudioMatch match)
    {
        return YouTubeAudioSupport.IsRelevantMatch(track, match);
    }

    public bool ShouldValidatePinnedMatch(MusicTrack track, MusicAudioMatch match)
    {
        return false;
    }

    public IReadOnlyList<string> GetFallbackProviderIds(MusicTrack track)
    {
        return Array.Empty<string>();
    }

    static Task<ResolvedManifest> StartSpeculativeManifest(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        var proxyLease = MusicProxyService.Acquire("youtubeaudio", MusicProxyPurpose.Stream);
        string cacheKey = BuildManifestCacheKey(videoId, proxyLease);
        if (speculativeManifests.ContainsKey(cacheKey))
            return speculativeManifests.TryGetValue(cacheKey, out var existing) ? existing.task : null;

        // уборка протухших записей (непотреблённые спекуляции — промахи ранжирования)
        foreach (var entry in speculativeManifests)
        {
            if (DateTime.UtcNow - entry.Value.at > speculativeManifestTtl)
                speculativeManifests.TryRemove(entry.Key, out _);
        }

        // CancellationToken.None: спекуляция переживает отмену исходного запроса —
        // результат заберёт следующий GetStreams по тому же видео в пределах TTL
        var task = ResolveManifestAsync(videoId, CancellationToken.None, proxyLease);
        speculativeManifests[cacheKey] = (DateTime.UtcNow, task);
        _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        return task;
    }

    static async Task<ResolvedManifest> TryTakeSpeculativeManifestAsync(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return null;

        var proxyLease = MusicProxyService.Acquire("youtubeaudio", MusicProxyPurpose.Stream);
        string cacheKey = BuildManifestCacheKey(videoId, proxyLease);
        if (!speculativeManifests.TryRemove(cacheKey, out var entry))
            return null;

        if (DateTime.UtcNow - entry.at > speculativeManifestTtl)
            return null;

        try
        {
            return await entry.task;
        }
        catch
        {
            // упавшая спекуляция не считается попыткой — обычный путь
            // с его retry-семантикой отработает как раньше
            return null;
        }
    }

    static async Task<List<VideoSearchResult>> FetchQueryResultsAsync(YoutubeClient youtube, string query, CancellationToken cancellationToken)
    {
        var list = new List<VideoSearchResult>();

        await foreach (var video in youtube.Search.GetVideosAsync(query, cancellationToken))
        {
            list.Add(video);
            if (list.Count >= 16)
                break;
        }

        return list;
    }

    static async Task<ResolvedManifest> ResolveManifestAsync(string videoId, CancellationToken cancellationToken, MusicProxyLease proxyLease = null)
    {
        proxyLease ??= MusicProxyService.Acquire("youtubeaudio", MusicProxyPurpose.Stream);

        try
        {
            using var youtube = new YoutubeClient(MusicHttp.GetTransport(proxyLease));
            var manifest = await youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);
            proxyLease.Success();
            return new ResolvedManifest(manifest, proxyLease);
        }
        catch (Exception ex)
        {
            if (proxyLease.Enabled && MusicHttp.IsProxyFailure(ex))
                proxyLease.Failure();

            throw;
        }
    }

    static string BuildManifestCacheKey(string videoId, MusicProxyLease proxyLease)
    {
        string route = proxyLease?.Enabled == true
            ? $"{proxyLease.Scope}|{proxyLease.Data.ip}|{proxyLease.Data.username}"
            : "direct";

        return $"{videoId}|{route}|cfg:{MusicProxyService.ConfigurationVersion}";
    }

    static bool ShouldRetryManifestFailure(Exception ex)
    {
        return ex != null
            && ex.GetType().Name == "YoutubeExplodeException"
            && (ex.Message?.IndexOf("cipher manifest", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
    }
}
