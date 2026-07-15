using Shared.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DLNA;

public static class TrackersCron
{
    static readonly Serilog.ILogger Log =
        Serilog.Log.ForContext(
            "SourceContext",
            nameof(TrackersCron)
        );

    static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    static readonly (string Url, string CachePath)[] Sources =
    {
        (
            "https://raw.githubusercontent.com/ngosang/trackerslist/master/trackers_all_ip.txt",
            "cache/trackers_ngosang.txt"
        ),
        (
            "https://raw.githubusercontent.com/XIU2/TrackersListCollection/master/all.txt",
            "cache/trackers_xiu2.txt"
        ),
        (
            "https://newtrackon.com/api/all",
            "cache/trackers_newtrackon.txt"
        )
    };

    const string TrackersPath = "cache/trackers.txt";

    static Timer _cronTimer;
    static int _updating;

    public static void Start()
    {
        int intervalMinutes = Math.Max(1, ModInit.conf.intervalUpdateTrackers);

        _cronTimer = new Timer(
            Cron,
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(intervalMinutes)
        );
    }

    public static void Stop()
    {
        Interlocked.Exchange(ref _cronTimer, null)?.Dispose();
    }

    static async void Cron(object state)
    {
        if (Interlocked.Exchange(ref _updating, 1) != 0)
            return;

        try
        {
            if (!ModInit.conf.autoupdatetrackers)
                return;

            await UpdateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "CatchId={CatchId}",
                "id_trackers_update"
            );
        }
        finally
        {
            Volatile.Write(ref _updating, 0);
        }
    }

    static async Task UpdateAsync()
    {
        Directory.CreateDirectory("cache");

        var trackers = new List<string>();
        var unique = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var source in Sources)
        {
            List<string> sourceTrackers = await LoadSourceAsync(source).ConfigureAwait(false);

            foreach (string tracker in sourceTrackers)
            {
                if (unique.Add(tracker))
                    trackers.Add(tracker);
            }
        }

        // Не затираем существующий общий файл пустым списком
        if (trackers.Count == 0)
        {
            Log.Warning(
                "Tracker update returned no usable trackers. " +
                "Existing file was preserved."
            );

            return;
        }

        WriteAtomic(TrackersPath, trackers);

        Log.Information(
            "Updated tracker list. Count={Count}",
            trackers.Count
        );
    }

    static async Task<List<string>> LoadSourceAsync((string Url, string CachePath) source)
    {
        try
        {
            string content = await Http.Get(
                source.Url,
                timeoutSeconds: 20
            ).ConfigureAwait(false);

            List<string> trackers = ParseTrackers(content);

            if (trackers.Count > 0)
            {
                try
                {
                    WriteAtomic(
                        source.CachePath,
                        trackers
                    );
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "Failed to save tracker source cache. Url={Url}",
                        source.Url
                    );
                }

                return trackers;
            }

            Log.Warning(
                "Tracker source returned no usable trackers. " +
                "Using cached source data. Url={Url}",
                source.Url
            );
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to download tracker source. " +
                "Using cached source data. Url={Url}",
                source.Url
            );
        }

        return ReadCachedSource(
            source.CachePath,
            source.Url
        );
    }

    static List<string> ReadCachedSource(string cachePath, string sourceUrl)
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                Log.Warning(
                    "Tracker source cache does not exist. Url={Url}",
                    sourceUrl
                );

                return new List<string>();
            }

            string content = File.ReadAllText(
                cachePath,
                Encoding.UTF8
            );

            List<string> trackers = ParseTrackers(content);

            Log.Information(
                "Loaded cached tracker source. " +
                "Url={Url}, Count={Count}",
                sourceUrl,
                trackers.Count
            );

            return trackers;
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to read tracker source cache. " +
                "Url={Url}, Path={Path}",
                sourceUrl,
                cachePath
            );

            return new List<string>();
        }
    }

    static List<string> ParseTrackers(string content)
    {
        var trackers = new List<string>();

        if (string.IsNullOrWhiteSpace(content))
            return trackers;

        var unique = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (string rawLine in content.Split('\n'))
        {
            string tracker = rawLine
                .Trim()
                .TrimStart('\uFEFF');

            if (string.IsNullOrWhiteSpace(tracker))
                continue;

            if (tracker.StartsWith('#'))
                continue;

            if (!Uri.TryCreate(tracker, UriKind.Absolute, out Uri uri))
                continue;

            bool supported =
                uri.Scheme.Equals(
                    Uri.UriSchemeHttp,
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                uri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                uri.Scheme.Equals(
                    "udp",
                    StringComparison.OrdinalIgnoreCase
                );

            // исключаем ws://, wss:// и остальные неподдерживаемые схемы
            if (!supported)
                continue;

            if (string.IsNullOrWhiteSpace(uri.Host))
                continue;

            // UDP tracker без порта практически бесполезен
            if (uri.Scheme.Equals("udp", StringComparison.OrdinalIgnoreCase) && uri.Port <= 0)
                continue;

            if (unique.Add(tracker))
                trackers.Add(tracker);
        }

        return trackers;
    }

    static void WriteAtomic(string path, IEnumerable<string> lines)
    {
        string directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + ".tmp";

        try
        {
            File.WriteAllLines(
                tempPath,
                lines,
                Utf8NoBom
            );

            File.Move(
                tempPath,
                path,
                overwrite: true
            );
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Временный файл будет перезаписан при следующем обновлении
            }
        }
    }
}
