using Newtonsoft.Json.Linq;
using Shared;
using Shared.Services;
using Shared.Models.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Transcoding;

internal sealed class TranscodingService
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<TranscodingService>();

    static readonly Lazy<TranscodingService> _lazy = new(() => new TranscodingService());

    readonly ConcurrentDictionary<string, TranscodingJob> _jobs = new();
    readonly Regex _safeFileNameRegex = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    readonly Regex _segmentFileRegex = new("^seg_(\\d+)\\.(m4s|ts)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    int _segmentCleanupRunning;
    readonly Timer _segmentCleanupTimer;

    byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);
    static string _ffmpegPath;

    public static TranscodingService Instance => _lazy.Value;

    private TranscodingService()
    {
        _segmentCleanupTimer = new Timer(_ => CleanupSegments(), null, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(20));
    }

    public void Configure(ModuleConf config)
    {
        if (config == null)
            return;

        _ffmpegPath = string.IsNullOrWhiteSpace(config.ffmpeg)
                ? (CoreInit.Win32NT ? "data/ffmpeg.exe" : (File.Exists("data/ffmpeg") ? "data/ffmpeg" : "ffmpeg"))
                : config.ffmpeg;

        if (string.IsNullOrWhiteSpace(config.tempRoot))
            config.tempRoot = Path.Combine("cache", "transcoding");

        try
        {
            if (!Directory.Exists(config.tempRoot))
            {
                Directory.CreateDirectory(config.tempRoot);
            }
            else
            {
                foreach (var dir in Directory.GetDirectories(config.tempRoot))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_g6yuafvi");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_quspivc7");
        }
    }

    public ICollection<TranscodingJob> Jobs => _jobs.Values;

    async public Task<(TranscodingJob job, string error)> Start(TranscodingStartRequest request)
    {
        var config = GetConfig();

        if (request == null || string.IsNullOrWhiteSpace(request.src))
            return (null!, "Source URL is required");

        if (_jobs.Count >= Math.Max(1, config.maxConcurrentJobs))
            return (null!, "Maximum concurrent jobs reached");

        if (!TryValidateSource(request.src, config, out var source, out var error))
            return (null!, error);

        var id = Guid.NewGuid().ToString("N");
        var streamId = BuildToken(id);

        var outputDir = Path.Combine(config.tempRoot!, id);
        Directory.CreateDirectory(outputDir);

        #region ffprobe
        var ffprobe = await Http.Get<JObject>($"http://{CoreInit.conf.listen.localhost}:{CoreInit.conf.listen.port}/ffprobe?media={HttpUtility.UrlEncode(request.src)}", headers: HeadersModel.Init("lcrqpasswd", CoreInit.rootPasswd));

        if (ffprobe == null || !ffprobe.ContainsKey("format"))
            return (null!, "ffprobe");
        #endregion

        var context = new TranscodingStartContext(
            source,
            SanitizeHeader(GetHeader(request.headers, "userAgent"), Http.UserAgent),
            SanitizeHeader(GetHeader(request.headers, "referer")),
            MergeHlsOptions(config, request.hls),
            MergeAudioOptions(config, request.audio),
            request.live,
            request.subtitles ?? config.defaultSubtitles,
            outputDir,
            null,
            ffprobe
        );

        var process = CreateProcess(context);

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return (null!, "Failed to start ffmpeg");
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_71c20640");
            process.Dispose();
            return (null!, $"Failed to start ffmpeg: {ex.Message}");
        }

        var job = new TranscodingJob(id, streamId, outputDir, process, context);
        if (!_jobs.TryAdd(id, job))
        {
            try
            {
                process.Kill(true);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_avj4m3h6");
            }

            process.Dispose();
            try
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, true);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_q5jm90jq");
            }
            return (null!, "Failed to register job");
        }

        _ = Task.Run(() => PumpStdErrAsync(job));
        _ = Task.Run(() => IdleWatchdogAsync(job, config));

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (process.EnableRaisingEvents)
                OnProcessExit(job);
        };

        return (job, string.Empty);
    }

    public bool TryResolveJob(string streamId, out TranscodingJob job)
    {
        job = null!;
        if (string.IsNullOrWhiteSpace(streamId))
            return false;

        if (!TryParseToken(streamId, out var id))
            return false;

        if (!_jobs.TryGetValue(id, out job))
            return false;

        return true;
    }

    public async Task<(bool success, string error)> SeekAsync(string streamId, int seconds, int? startSegment = null)
    {
        if (seconds < 0)
            return (false, "ss must be greater or equal 0");

        if (!TryResolveJob(streamId, out var job))
            return (false, "Job not found");

        var config = GetConfig();

        var newContext = job.Context with
        {
            HlsOptions = new TranscodingHlsOptions
            {
                seek = seconds,
                segDur = job.Context.HlsOptions.segDur,
                winSize = job.Context.HlsOptions.winSize,
                fmp4 = job.Context.HlsOptions.fmp4
            },
            startNumber = startSegment
        };

        job.Process.EnableRaisingEvents = false;
        await StopJobAsync(job, forced: true, cleanup: false);

        Directory.CreateDirectory(newContext.OutputDirectory);

        var process = CreateProcess(newContext);

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return (false, "Failed to start ffmpeg");
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_1af34286");
            process.Dispose();
            return (false, $"Failed to start ffmpeg: {ex.Message}");
        }

        var newJob = new TranscodingJob(job.Id, job.StreamId, job.OutputDirectory, process, newContext);

        if (_jobs.AddOrUpdate(job.Id, newJob, (k, v) => newJob) == null)
        {
            try
            {
                process.Kill(true);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "CatchId={CatchId}", "id_rteqssej");
            }

            process.Dispose();
            return (false, "Failed to restart job");
        }

        _ = Task.Run(() => PumpStdErrAsync(newJob));
        _ = Task.Run(() => IdleWatchdogAsync(newJob, config));

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (process.EnableRaisingEvents)
                OnProcessExit(newJob);
        };

        return (true, string.Empty);
    }

    public async Task<bool> StopAsync(string streamId)
    {
        if (!TryResolveJob(streamId, out var job))
            return false;

        await StopJobAsync(job);
        return true;
    }

    public void Touch(TranscodingJob job) => job.UpdateLastAccess();

    public string GetFilePath(TranscodingJob job, string file)
    {
        if (string.IsNullOrWhiteSpace(file) || !_safeFileNameRegex.IsMatch(file))
            return null;

        var candidate = Path.Combine(job.OutputDirectory, file);
        if (!candidate.StartsWith(job.OutputDirectory, StringComparison.Ordinal))
            return null;

        return File.Exists(candidate) ? candidate : null;
    }

    public void ReportSegmentAccess(TranscodingJob job, int segmentIndex)
    {
        job?.UpdateLastSegmentIndex(segmentIndex);
    }

    private async Task StopJobAsync(TranscodingJob job, bool forced = false, bool cleanup = true)
    {
        try
        {
            if (!job.Process.HasExited)
            {
                if (forced)
                {
                    job.Process.Kill(true);
                }
                else
                {
                    try
                    {
                        await job.Process.StandardInput.WriteLineAsync("q");
                        await job.Process.StandardInput.FlushAsync();
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_a5h7gduz");
                    }

                    var waitTask = job.Process.WaitForExitAsync();
                    var timeout = Task.Delay(TimeSpan.FromMilliseconds(1500));
                    var completed = await Task.WhenAny(waitTask, timeout);
                    if (completed != waitTask)
                    {
                        try
                        {
                            job.Process.Kill(true);
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_9lgmzpo5");
                        }

                        try
                        {
                            await job.Process.WaitForExitAsync();
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_zmkmg1y6");
                        }
                    }
                    else
                    {
                        try
                        {
                            await waitTask;
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_a56zgctr");
                        }
                    }
                }
            }
        }
        finally
        {
            if (cleanup)
                Cleanup(job);
        }
    }

    private void Cleanup(TranscodingJob job)
    {
        var removed = _jobs.TryRemove(job.Id, out _);

        try
        {
            job.StopBackground();
            job.SignalExit();
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_9c5you07");
        }

        if (!removed)
            return;

        try
        {
            if (Directory.Exists(job.OutputDirectory))
                Directory.Delete(job.OutputDirectory, true);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_z4e0wm3m");
        }

        job.Dispose();
    }

    private ModuleConf GetConfig()
    {
        return ModInit.conf;
    }

    private static string GetHeader(Dictionary<string, string> headers, string key)
    {
        if (headers == null || headers.Count == 0)
            return null;

        foreach (var (k, v) in headers)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v;
        }

        return null;
    }

    private static TranscodingHlsOptions MergeHlsOptions(ModuleConf config, TranscodingHlsOptions request)
    {
        var opt = request ?? config.hlsOptions;

        return new TranscodingHlsOptions
        {
            seek = opt?.seek > 0 ? opt.seek : 0,
            segDur = opt?.segDur > 1 ? opt.segDur : 1,
            winSize = opt?.winSize > 5 ? opt.winSize : 5,
            fmp4 = opt?.fmp4 ?? true
        };
    }

    private static TranscodingAudioOptions MergeAudioOptions(ModuleConf config, TranscodingAudioOptions request)
    {
        var opt = request ?? config.audioOptions;

        return new TranscodingAudioOptions
        {
            index = opt?.index >= 0 ? opt.index : 0,
            bitrateKbps = opt?.bitrateKbps is > 0 and <= 512 ? opt.bitrateKbps : 160,
            stereo = opt?.stereo ?? true,
            codec_copy = opt?.codec_copy ?? config.audioOptions.codec_copy
        };
    }

    private static bool TryValidateSource(string src, ModuleConf config, out Uri uri, out string error)
    {
        error = string.Empty;
        uri = null!;

        if (!Uri.TryCreate(src, UriKind.Absolute, out uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "Only http/https URLs are allowed";
            return false;
        }

        if (config.allowHosts != null && config.allowHosts.Length > 0)
        {
            if (!config.allowHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                error = "Source host is not allowed";
                return false;
            }
        }

        return true;
    }

    public void StopAll()
    {
        try
        {
            foreach (var job in _jobs.Values.ToArray())
            {
                try
                {
                    _ = StopJobAsync(job, forced: true).ConfigureAwait(false);
                }
                catch (System.Exception ex)
                {
                    Log.Error(ex, "CatchId={CatchId}", "id_sp9w94ug");
                }
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "ModInit", "id_ley70nc7");
        }
    }

    private Process CreateProcess(TranscodingStartContext context)
    {
        var config = GetConfig();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                CreateNoWindow = true,
                WorkingDirectory = context.OutputDirectory
            }
        };

        var args = process.StartInfo.ArgumentList;

        /*
-hide_banner — отключает вывод баннера ffmpeg (версия/конфигурация) в stderr, чтобы логи были чище.
-user_agent {context.UserAgent} — задаёт заголовок User-Agent для HTTP-запросов к входному URL.
-headers "Referer: {context.Referer}\n" — добавляет произвольные HTTP-заголовки (здесь: Referer) при обращении к входному URL.
-re — читает входной поток в реальном времени; полезно при трансляции/стриминге, чтобы не читать вход быстрее реального времени.
-threads 0 — позволяет ffmpeg автоматически выбрать количество потоков (CPU) для кодирования/декодирования.
-fflags +genpts — генерирует PTS для кадров, если их нет (избегает проблем с отсутствующими временными метками).
-i {Source} — указывает входной источник (URL/файл).
-map 0:v:0 — маппит первый видеопоток первого входа в выход.
-map 0:a:0 — маппит первый аудиопоток первого входа в выход.
-sn (опционально, при subtitles == false) — исключить субтитры из вывода.
-dn — исключить data-потоки (metadata/data tracks).
-map_metadata -1 — не копировать глобальные метаданные в выход (очищает метаданные).
-map_chapters -1 — не копировать главы (удаляет chapters).
-c:v copy — копирование видеопотока без перекодирования (для минимальной нагрузки и без потери качества).

Аудио:
Если transcodeToAac:
-c:a aac — кодировать аудио в AAC.
-ac 2 / -ac 1 — число каналов (стерео/моно) по настройке.
-b:a {N}k — битрейт аудио.
-profile:a aac_low — профиль AAC (обычно LC для совместимости и низкой задержки).
Иначе: -c:a copy — копирование аудио без перекодирования.

HLS / контейнер:
-avoid_negative_ts disabled — не сдвигать временные метки для предотвращения отрицательных TS; сохраняет исходные PTS при HLS-сегментации.
-max_muxing_queue_size 2048 — увеличивает очередь пакетов при мультиплексировании (помогает против “Too many packets buffered”).
-f hls — формат выхода HLS (HTTP Live Streaming).
-max_delay 5000000 — максимальная задержка буфера (в мкс; зависит от контекста), снижает риск избыточного буферинга.
-hls_segment_type fmp4 или mpegts — тип сегментов HLS (CMAF/fMP4 или MPEG-TS).
для mpegts: -bsf:v h264_mp4toannexb — конвертирует H.264 к Annex-B, как требуется для TS.
-hls_time {segDur} — длительность сегмента в секундах.
-hls_flags append_list+omit_endlist —
append_list — дописывать записи в плейлист;
omit_endlist — не добавлять #EXT-X-ENDLIST, чтобы плейлист считался “живым”.
-hls_list_size {winSize} — размер окна плейлиста (сколько последних сегментов видит клиент).
-master_pl_name index.m3u8 — имя master-плейлиста (если используется).
-hls_fmp4_init_filename {init.mp4} — имя init-сегмента для fMP4.
-hls_segment_filename {seg_%05d.m4s | seg_%05d.ts} — шаблон имени файлов сегментов.
-y {PlaylistPath} — перезаписать выходной файл без подтверждения (путь итогового плейлиста/файла).
         */

        args.Add("-hide_banner");

        if (context.UserAgent != null)
        {
            args.Add("-user_agent");
            args.Add(context.UserAgent);
        }

        if (!string.IsNullOrWhiteSpace(context.Referer))
        {
            args.Add("-headers");
            args.Add($"Referer: {context.Referer}\\r\\n");
        }

        if (context.HlsOptions.seek > 0)
        {
            args.Add("-ss");
            args.Add(context.HlsOptions.seek.ToString());
            args.Add("-noaccurate_seek");
        }

        args.Add("-nostats");
        args.Add("-progress");
        args.Add("pipe:2");
        args.Add("-stats_period");
        args.Add(context.live ? "1" : "5");

        #region demuxer
        foreach (var c in config.comand["demuxer"])
        {
            foreach (var a in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(a);
        }
        #endregion

        #region readrate
        if (context.live)
        {
            args.Add("-re");
        }
        else if (config.playlistOptions.readrate > 0)
        {
            args.Add("-readrate");
            args.Add(config.playlistOptions.readrate.ToString().Replace(",", "."));

            if (config.playlistOptions.burst > 0)
            {
                args.Add("-readrate_initial_burst");
                args.Add(config.playlistOptions.burst.ToString());
            }
        }
        #endregion

        args.Add("-i");
        args.Add(context.Source.AbsoluteUri);

        #region input
        foreach (var c in config.comand["input"])
        {
            foreach (var a in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(a);
        }
        #endregion

        #region subtitles map
        if (context.subtitles && !context.live && 0 >= config.playlistOptions.readrate && context.ffprobe.ContainsKey("streams"))
        {
            args.Add("-copyts");

            foreach (var s in context.ffprobe["streams"])
            {
                if (s.Value<string>("codec_type") != "subtitle")
                    continue;

                string codec_name = s.Value<string>("codec_name");
                if (!string.IsNullOrEmpty(codec_name) && config.subtitleOptions.codec.Contains(codec_name))
                {
                    int subIndex = s.Value<int>("index");
                    if (subIndex == 0)
                        continue;

                    foreach (var c in config.subtitleOptions.comand)
                    {
                        foreach (var a in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            args.Add(a.Replace("{subIndex}", subIndex.ToString()));
                    }
                }
            }
        }
        #endregion

        #region HLS map
        foreach (var c in config.comand["output"])
        {
            foreach (var a in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(a.Replace("{audio_index}", $"{(0 >= context.Audio.index ? 0 : context.Audio.index)}"));
        }

        #region -c:v
        if (config.convertOptions.transcodeVideo && config.convertOptions.codec != null &&
            context.ffprobe != null && context.ffprobe.ContainsKey("streams") &&
            config.convertOptions.comand != null && config.convertOptions.comand.Count > 0)
        {
            try
            {
                bool convert = false;

                string codec_name = context.ffprobe["streams"].First.Value<string>("codec_name") ?? "";
                if (!string.IsNullOrEmpty(codec_name) && config.convertOptions.codec.Contains(codec_name))
                    convert = true;

                string pix_fmt = context.ffprobe["streams"].First.Value<string>("pix_fmt") ?? "";
                if (!string.IsNullOrEmpty(pix_fmt) && config.convertOptions.codec.Contains(pix_fmt))
                    convert = true;

                if (config.convertOptions.codec.Contains($"{codec_name}_{pix_fmt}"))
                    convert = true;

                if (convert)
                {
                    string[] comand = config.convertOptions.comand["default"];

                    foreach (string key in new string[] { $"{codec_name}_{pix_fmt}", pix_fmt, codec_name })
                    {
                        if (config.convertOptions.comand.ContainsKey(key))
                        {
                            comand = config.convertOptions.comand[key];
                            break;
                        }
                    }

                    foreach (var c in comand)
                    {
                        foreach (var t in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            args.Add(t);
                    }
                }
                else
                {
                    args.Add("-c:v");
                    args.Add("copy");
                }
            }
            catch
            {
                args.Add("-c:v");
                args.Add("copy");
            }
        }
        else
        {
            args.Add("-c:v");
            args.Add("copy");
        }
        #endregion

        #region -c:a
        {
            bool convert = true;

            var audioCodec = context.ffprobe["streams"].FirstOrDefault(i => i.Value<int>("index") == ((0 >= context.Audio.index ? 0 : context.Audio.index) + 1));

            string codec_name = audioCodec?.Value<string>("codec_name") ?? string.Empty;
            if (!string.IsNullOrEmpty(codec_name) && context.Audio.codec_copy.Contains(codec_name))
                convert = false;

            string channel = audioCodec?.Value<int?>("channels")?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(channel) && context.Audio.codec_copy.Contains(channel))
                convert = false;

            if (context.Audio.codec_copy.Contains($"{codec_name}_{channel}"))
                convert = false;

            if (convert)
            {
                string[] comand = config.audioOptions.comand_transcode["default"];

                foreach (string key in new string[] { $"{codec_name}_{channel}", channel, codec_name })
                {
                    if (config.audioOptions.comand_transcode.ContainsKey(key))
                    {
                        comand = config.audioOptions.comand_transcode[key];
                        break;
                    }
                }

                foreach (var c in comand)
                {
                    foreach (var a in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        args.Add(a.Replace("{stereo}", context.Audio.stereo ? "2" : "1").Replace("{bitrateKbps}", $"{Math.Clamp(context.Audio.bitrateKbps, 32, 512)}k"));
                }
            }
            else
            {
                args.Add("-c:a");
                args.Add("copy");
            }
        }
        #endregion

        args.Add("-f");
        args.Add("hls");

        foreach (var c in config.hlsOptions.comand["output"])
        {
            foreach (var a in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(a);
        }

        #region -hls_segment_type
        args.Add("-hls_segment_type");

        if (context.HlsOptions.fmp4)
        {
            args.Add("fmp4");
        }
        else
        {
            args.Add("mpegts");

            foreach (var c in config.hlsOptions.comand["segment_mpegts"])
            {
                foreach (var a in c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    args.Add(a);
            }
        }
        #endregion

        args.Add("-hls_time");
        args.Add(context.HlsOptions.segDur.ToString(CultureInfo.InvariantCulture));

        if (context.live)
        {
            args.Add("-hls_flags");
            args.Add("append_list+omit_endlist+delete_segments");
        }

        args.Add("-hls_list_size");
        args.Add(context.HlsOptions.winSize.ToString(CultureInfo.InvariantCulture));

        #region -start_number
        int? startNumber = context.startNumber;
        if (!startNumber.HasValue && context.HlsOptions.seek > 0)
        {
            int segDur = Math.Max(1, context.HlsOptions.segDur);
            startNumber = context.HlsOptions.seek / segDur;
        }

        if (startNumber.HasValue)
        {
            args.Add("-start_number");
            args.Add(startNumber.Value.ToString());
        }
        #endregion

        args.Add("-master_pl_name");
        args.Add("index.m3u8");

        if (context.HlsOptions.fmp4)
        {
            args.Add("-hls_fmp4_init_filename");
            args.Add("init.mp4");
        }
        else
        {
            args.Add("-hls_playlist_type");
            args.Add("vod");
        }

        args.Add("-hls_segment_filename");
        args.Add(context.HlsOptions.fmp4 ? "seg_%05d.m4s" : "seg_%05d.ts");

        args.Add("-y");
        args.Add("index.m3u8");
        #endregion

        return process;
    }

    private async Task PumpStdErrAsync(TranscodingJob job)
    {
        try
        {
            string line;
            while ((line = await job.Process.StandardError.ReadLineAsync()) != null)
                job.AppendLog(line);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_9kis6e5h");
        }
    }

    private async Task IdleWatchdogAsync(TranscodingJob job, ModuleConf config)
    {
        var idle = TimeSpan.FromSeconds(Math.Max(180, config.idleTimeoutSec));
        var idle_live = TimeSpan.FromSeconds(Math.Max(20, config.idleTimeoutSec_live));

        try
        {
            while (!job.CancellationToken.IsCancellationRequested && !job.Process.HasExited)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), job.CancellationToken);

                if (job.Context.live)
                {
                    if (config.idleTimeoutSec_live == -1)
                        continue;

                    if (DateTime.UtcNow - job.LastAccessUtc > idle_live)
                    {
                        await StopJobAsync(job);
                        break;
                    }
                }
                else
                {

                    if (config.idleTimeoutSec == -1)
                        continue;

                    if (DateTime.UtcNow - job.LastAccessUtc > idle)
                    {
                        await StopJobAsync(job);
                        break;
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void OnProcessExit(TranscodingJob job)
    {
        if (job.Context.live)
        {
            job.SignalExit();
            Cleanup(job);
        }
    }

    private bool TryParseToken(string streamId, out string id)
    {
        id = string.Empty;
        var parts = streamId.Split('.', 2);
        if (parts.Length != 2)
            return false;

        id = parts[0];
        if (id.Length != 32)
            return false;

        try
        {
            var padded = parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=');
            var hmac = Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
            var expected = ComputeHmac(id);
            if (!CryptographicOperations.FixedTimeEquals(hmac, expected))
                return false;
        }
        catch
        {
            return false;
        }

        return true;
    }

    private string BuildToken(string id)
    {
        var mac = ComputeHmac(id);
        var b64 = Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{id}.{b64}";
    }

    private byte[] ComputeHmac(string id)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(id));
    }

    private static string SanitizeHeader(string value, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var clean = value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private void CleanupSegments()
    {
        if (Interlocked.Exchange(ref _segmentCleanupRunning, 1) == 1)
            return;

        try
        {
            if (!(ModInit.conf?.playlistOptions?.delete_segments ?? false))
                return;

            foreach (var job in _jobs.Values)
            {
                if (job.Context.live)
                    continue;

                var lastIndex = job.LastSegmentIndex;
                if (0 >= lastIndex)
                    continue;

                try
                {
                    if (!Directory.Exists(job.OutputDirectory))
                        continue;

                    foreach (var file in Directory.GetFiles(job.OutputDirectory, "seg_*"))
                    {
                        try
                        {
                            var name = Path.GetFileName(file);
                            var match = _segmentFileRegex.Match(name);
                            if (!match.Success)
                                continue;

                            if (!int.TryParse(match.Groups[1].Value, out var index))
                                continue;

                            if (index >= lastIndex - 1)
                                continue;

                            // первые 10 сегментов оставляем
                            if (10 >= index)
                                continue;

                            File.Delete(file);
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_nwuuqesw");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Error(ex, "CatchId={CatchId}", "id_x7fqvn06");
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_zr6i3b00");
        }
        finally
        {
            Interlocked.Exchange(ref _segmentCleanupRunning, 0);
        }
    }
}
