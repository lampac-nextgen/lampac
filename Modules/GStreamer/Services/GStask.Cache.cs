using GStreamer.Models;
using Microsoft.Win32.SafeHandles;
using Shared.Services.Pools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace GStreamer.Services;

public partial class GStask
{
    readonly object segmentCacheLock = new();
    readonly Dictionary<int, long> readySegmentFiles = new();
    readonly Dictionary<int, int> pinnedSegmentFiles = new();

    int clientSegmentIndex = -1;
    bool segmentCacheTrimPending;
    string segmentCacheDir;
    string initMp4Path;

    int SegmentPast()
        => Math.Max(1, conf.segment_past);

    int SegmentBuffer()
        => Math.Max(2, conf.segment_buffer);

    long SegmentPastMaxBytes()
        => conf.segment_past_mb > 0
            ? (long)conf.segment_past_mb * 1024 * 1024
            : 0;

    long SegmentBufferMaxBytes()
        => conf.segment_buffer_mb > 0
            ? (long)conf.segment_buffer_mb * 1024 * 1024
            : 0;

    void InitSegmentCache()
    {
        segmentCacheDir = Path.Combine(
            "cache",
            "gstranscoding",
            id.ToString(CultureInfo.InvariantCulture)
        );

        initMp4Path = Path.Combine(
            "cache",
            "gstranscoding",
            $"{id.ToString(CultureInfo.InvariantCulture)}.init.mp4"
        );

        ClearSegmentCache();

        try
        {
            Directory.CreateDirectory(segmentCacheDir);
        }
        catch { }
    }

    #region SetClientSegmentIndex
    public void SetClientSegmentIndex(int index, bool cacheHit)
    {
        if (index < 0)
            return;

        lock (segmentCacheLock)
        {
            if (cacheHit && clientSegmentIndex >= 0 && index < clientSegmentIndex)
                return;

            clientSegmentIndex = index;

            if (readySegmentFiles.Count == 0)
                return;

            int[] remove = TakeAndRemoveOutsideCacheLimits();

            foreach (int removeIndex in remove)
                TryDeleteFile(SegmentFilePath(removeIndex));
        }
    }
    #endregion

    #region TryOpenInitFile
    public bool TryOpenInitFile(out CachedSegmentFile file)
    {
        file = default;

        long expectedLength = Volatile.Read(ref initMp4Length);
        if (expectedLength <= 0)
            return false;

        SafeFileHandle handle = null;

        try
        {
            handle = File.OpenHandle(
                initMp4Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            long length = RandomAccess.GetLength(handle);
            if (length != expectedLength)
            {
                handle.Dispose();
                return false;
            }

            file = new CachedSegmentFile(length, handle);
            handle = null;

            return true;
        }
        catch (IOException)
        {
            handle?.Dispose();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            handle?.Dispose();
            return false;
        }
    }
    #endregion

    #region TryOpenSegmentFile
    public bool TryOpenSegmentFile(int index, out CachedSegmentFile file)
    {
        file = default;

        if (index < 0)
            return false;

        lock (segmentCacheLock)
        {
            if (!readySegmentFiles.ContainsKey(index))
                return false;
        }

        string path = SegmentFilePath(index);
        SafeFileHandle handle = null;

        try
        {
            handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            long length = RandomAccess.GetLength(handle);
            if (length <= 0)
            {
                handle.Dispose();
                handle = null;

                RemoveSegmentFile(index, deleteFile: true);
                return false;
            }

            file = new CachedSegmentFile(length, handle);
            handle = null;

            return true;
        }
        catch (IOException ex)
        {
            handle?.Dispose();

            Serilog.Log.Warning(ex, "Unable to open cached GStreamer segment. Segment={Segment}", index);
            RemoveSegmentFile(index, deleteFile: true);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            handle?.Dispose();

            Serilog.Log.Warning(ex, "Unable to open cached GStreamer segment. Segment={Segment}", index);
            RemoveSegmentFile(index, deleteFile: true);
            return false;
        }
    }
    #endregion

    #region Pin/Unpin SegmentFile
    public void PinSegmentFile(int index)
    {
        if (index < 0)
            return;

        lock (segmentCacheLock)
        {
            pinnedSegmentFiles.TryGetValue(index, out int count);
            pinnedSegmentFiles[index] = count + 1;
        }
    }

    public void UnpinSegmentFile(int index)
    {
        if (index < 0)
            return;

        lock (segmentCacheLock)
        {
            if (!pinnedSegmentFiles.TryGetValue(index, out int count))
                return;

            if (count > 1)
                pinnedSegmentFiles[index] = count - 1;
            else
            {
                pinnedSegmentFiles.Remove(index);

                if (segmentCacheTrimPending)
                {
                    foreach (int removeIndex in TakeAndRemoveOutsideCacheLimits())
                        TryDeleteFile(SegmentFilePath(removeIndex));
                }
            }
        }
    }
    #endregion

    #region Helpers
    void StoreInitFile(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new InvalidDataException("GStreamer init segment is empty.");

        try
        {
            using var output = new FileStream(
                initMp4Path,
                new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.SequentialScan,
                    BufferSize = PoolInvk.msmBlockSize,
                    PreallocationSize = data.Length
                }
            );

            output.Write(data);

            if (output.Position != data.Length)
            {
                throw new EndOfStreamException(
                    $"Init writer produced {output.Position} of {data.Length} bytes."
                );
            }
        }
        catch
        {
            TryDeleteFile(initMp4Path);
            throw;
        }
    }

    bool StoreSegmentFile(int index, Segment segment)
    {
        if (index < 0 || segment.length <= 0)
            return false;

        lock (segmentCacheLock)
        {
            if (readySegmentFiles.ContainsKey(index))
                return true;
        }

        string path = SegmentFilePath(index);

        try
        {
            if (!WriteSegmentToFile(segment, path))
            {
                System.Threading.Thread.Sleep(200);

                if (!WriteSegmentToFile(segment, path))
                    return false;
            }

            lock (segmentCacheLock)
                readySegmentFiles[index] = segment.length;

            return true;
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException)
        {
            Serilog.Log.Warning(
                ex,
                "Unable to store GStreamer segment file. Segment={Segment}",
                index
            );

            lock (segmentCacheLock)
                readySegmentFiles.Remove(index);

            TryDeleteFile(path);
            return false;
        }
    }

    void ClearSegmentCache()
    {
        lock (segmentCacheLock)
            readySegmentFiles.Clear();

        try
        {
            if (Directory.Exists(segmentCacheDir))
                Directory.Delete(segmentCacheDir, recursive: true);
        }
        catch { }
    }

    void RemoveSegmentFile(int index, bool deleteFile)
    {
        lock (segmentCacheLock)
            readySegmentFiles.Remove(index);

        if (deleteFile)
            TryDeleteFile(SegmentFilePath(index));
    }

    string SegmentFilePath(int index)
    {
        return Path.Combine(
            segmentCacheDir,
            $"{index.ToString(CultureInfo.InvariantCulture)}.m4s"
        );
    }

    int[] TakeAndRemoveOutsideCacheLimits()
    {
        var set = readySegmentFiles;
        segmentCacheTrimPending = false;

        if (clientSegmentIndex < 0)
            return Array.Empty<int>();

        if (set.Count == 0)
            return Array.Empty<int>();

        int currentIndex = clientSegmentIndex;
        int min = Math.Max(0, currentIndex - SegmentPast());
        int max = currentIndex + SegmentBuffer();

        List<int> remove = null;

        foreach (var item in set)
        {
            int index = item.Key;

            if (index >= min && index <= max)
                continue;

            if (pinnedSegmentFiles.ContainsKey(index))
            {
                segmentCacheTrimPending = true;
                continue;
            }

            remove ??= new List<int>();
            remove.Add(index);
        }

        if (remove != null)
        {
            foreach (int index in remove)
                set.Remove(index);
        }

        long maxPastBytes = SegmentPastMaxBytes();
        if (maxPastBytes > 0)
        {
            long pastBytes = 0;

            foreach (var item in set)
            {
                if (item.Key < min || item.Key >= currentIndex)
                {
                    continue;
                }

                if (pinnedSegmentFiles.ContainsKey(item.Key))
                {
                    segmentCacheTrimPending = true;
                    continue;
                }

                pastBytes = item.Value >= long.MaxValue - pastBytes
                    ? long.MaxValue
                    : pastBytes + item.Value;
            }

            while (pastBytes > maxPastBytes)
            {
                int oldestIndex = int.MaxValue;
                long oldestLength = 0;

                foreach (var item in set)
                {
                    if (item.Key < min ||
                        item.Key >= currentIndex ||
                        item.Key >= oldestIndex ||
                        pinnedSegmentFiles.ContainsKey(item.Key))
                    {
                        continue;
                    }

                    oldestIndex = item.Key;
                    oldestLength = item.Value;
                }

                if (oldestIndex == int.MaxValue)
                    break;

                set.Remove(oldestIndex);
                remove ??= new List<int>();
                remove.Add(oldestIndex);
                pastBytes = Math.Max(0, pastBytes - oldestLength);
            }
        }

        return remove?.ToArray() ?? Array.Empty<int>();
    }

    static bool WriteSegmentToFile(Segment segment, string path)
    {
        if (segment.length <= 0)
            return false;

        try
        {
            using var output = new RandomAccessWriteStream(path, segment.length);

            segment.WriteTo(output);

            if (output.Position != segment.length)
            {
                throw new EndOfStreamException(
                    $"Segment writer produced {output.Position} of {segment.length} bytes."
                );
            }

            output.Flush();

            return true;
        }
        catch (IOException ex)
        {
            Serilog.Log.Warning(ex, "Failed to write file '{Path}'", path);
            TryDeleteFile(path);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Serilog.Log.Warning(ex, "Failed to write file '{Path}'", path);
            TryDeleteFile(path);
            return false;
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }
    }

    static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch { }
    }

    bool SegmentFileReady(int index)
    {
        if (index < 0)
            return false;

        lock (segmentCacheLock)
            return readySegmentFiles.ContainsKey(index);
    }

    bool NeedsSegmentPrefetch(int currentIndex)
    {
        if (currentIndex < 0)
            return false;

        int buffer = SegmentBuffer();

        lock (segmentCacheLock)
        {
            long maxBufferBytes = SegmentBufferMaxBytes();
            if (maxBufferBytes > 0)
            {
                long bufferBytes = 0;
                int maxIndex = currentIndex + buffer;

                foreach (var item in readySegmentFiles)
                {
                    if (item.Key <= currentIndex || item.Key > maxIndex)
                        continue;

                    if (item.Value >= maxBufferBytes - bufferBytes)
                        return false;

                    bufferBytes += item.Value;
                }
            }

            for (int index = currentIndex + 1; index <= currentIndex + buffer; index++)
            {
                if (!readySegmentFiles.ContainsKey(index))
                    return true;
            }
        }

        return false;
    }

    int ClientSegmentIndex()
    {
        lock (segmentCacheLock)
            return clientSegmentIndex;
    }
    #endregion
}
