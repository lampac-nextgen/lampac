using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Shared.Services.Pools.Json;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;

namespace Shared.Services.Hybrid;

public class HybridFileCache : BaseHybridCache, IHybridCache
{
    #region static
    static IMemoryCache memoryCache;

    static Timer _clearTempDb, _cleanupTimer;

    sealed record class cacheEntry(string path, DateTimeOffset ex, int capacity);
    static readonly ConcurrentDictionary<string, cacheEntry> cacheFiles = new();

    static readonly ConcurrentDictionary<string, TempEntry> tempDb = new();

    public static int Stat_ContTempDb => tempDb.IsEmpty ? 0 : tempDb.Count;

    static JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
    {
        WriteIndented = false
    };
    #endregion

    #region Configure
    public static void Configure(IMemoryCache mem)
    {
        memoryCache = mem;
    }

    public static void LoadCache()
    {
        Directory.CreateDirectory("cache/fdb");

        _clearTempDb = new Timer(ClearTempDb, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        _cleanupTimer = new Timer(ClearCacheFiles, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        var now = DateTimeOffset.Now;

        foreach (string dir in Directory.GetDirectories("cache/fdb"))
        {
            foreach (string inFile in Directory.EnumerateFiles(dir, "*"))
            {
                try
                {
                    // cacheKey-time-capacity
                    string path = Path.GetFileName(inFile);
                    string[] parts = path.Split('-');

                    if (parts.Length != 3)
                    {
                        File.Delete(inFile);
                        continue;
                    }

                    #region ex
                    if (!long.TryParse(parts[1], out long fileTime) || fileTime == 0)
                    {
                        File.Delete(inFile);
                        continue;
                    }

                    var ex = DateTimeOffset.FromUnixTimeMilliseconds(fileTime);

                    if (now > ex)
                    {
                        File.Delete(inFile);
                        continue;
                    }
                    #endregion

                    int.TryParse(parts[2], out int capacity);

                    cacheFiles[parts[0]] = new cacheEntry(path, ex, capacity);
                }
                catch (System.Exception ex)
                {
                    try
                    {
                        File.Delete(inFile);
                    }
                    catch { }

                    Log.Error(ex, "CatchId={CatchId}", "id_zbcq20sy");
                }
            }
        }
    }
    #endregion

    #region ClearTempDb
    static int _updatingDb = 0;
    static readonly Encoding _utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    async static void ClearTempDb(object state)
    {
        if (tempDb.IsEmpty)
            return;

        if (Interlocked.Exchange(ref _updatingDb, 1) == 1)
            return;

        try
        {
            var cutoff = DateTimeOffset.Now;

            foreach (var tdb in tempDb)
            {
                if (cutoff > tdb.Value.ex)
                {
                    tempDb.TryRemove(tdb.Key, out _);
                }
                else if (cutoff > tdb.Value.extend)
                {
                    try
                    {
                        int capacity = GetCapacity(tdb.Value.value);
                        string path = $"{tdb.Key}-{tdb.Value.ex.ToUnixTimeMilliseconds()}-{capacity}";
                        Directory.CreateDirectory($"cache/fdb/{path[0]}");
                        string pathFile = $"cache/fdb/{path[0]}/{path}";

                        if (tdb.Value.IsSerialize)
                        {
                            if (tdb.Value.textJson)
                            {
                                await using (var fs = new FileStream(pathFile, FileMode.Create, FileAccess.Write, FileShare.Read,
                                    bufferSize: PoolInvk.bufferSize,
                                    options: FileOptions.Asynchronous))
                                {
                                    await using (var gzip = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: true))
                                        await System.Text.Json.JsonSerializer.SerializeAsync(gzip, tdb.Value.value, _jsonSerializerOptions);
                                }
                            }
                            else
                            {
                                using (var msm = PoolInvk.msm.GetStream())
                                {
                                    await using (var gzip = new GZipStream(msm, CompressionLevel.Fastest, leaveOpen: true))
                                    {
                                        using (var sw = new StreamWriter(gzip, _utf8NoBom, PoolInvk.bufferSize, leaveOpen: true))
                                        {
                                            using (var jw = new JsonTextWriter(sw)
                                            {
                                                Formatting = Formatting.None,
                                                ArrayPool = NewtonsoftPool.Array
                                            })
                                            {
                                                var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
                                                serializer.Serialize(jw, tdb.Value.value);
                                            }
                                        }
                                    }

                                    msm.Position = 0;
                                    await using (var fs = new FileStream(pathFile, FileMode.Create, FileAccess.Write, FileShare.Read,
                                        bufferSize: PoolInvk.bufferSize,
                                        options: FileOptions.Asynchronous))
                                    {
                                        using (var nbuf = new BufferPool())
                                        {
                                            int bytesRead;
                                            var memBuf = nbuf.Memory;

                                            msm.Position = 0;
                                            while ((bytesRead = msm.Read(memBuf.Span)) > 0)
                                                await fs.WriteAsync(memBuf.Slice(0, bytesRead)).ConfigureAwait(false);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            await File.WriteAllTextAsync(pathFile, (string)tdb.Value.value, _utf8NoBom).ConfigureAwait(false);
                        }

                        cacheFiles[tdb.Key] = new cacheEntry(path, tdb.Value.ex, capacity);
                        tempDb.TryRemove(tdb.Key, out _);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_r3s53fcl");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_388234ed");
        }
        finally
        {
            Volatile.Write(ref _updatingDb, 0);
        }
    }
    #endregion

    #region ClearCacheFiles
    static int _clearCacheFiles = 0;

    static void ClearCacheFiles(object state)
    {
        if (Interlocked.Exchange(ref _clearCacheFiles, 1) == 1)
            return;

        try
        {
            var now = DateTimeOffset.Now;

            foreach (var cache in cacheFiles)
            {
                if (now > cache.Value.ex)
                {
                    if (!cacheFiles.TryRemove(cache.Key, out var removedEntry))
                        continue;

                    string filePath = $"cache/fdb/{removedEntry.path[0]}/{removedEntry.path}";

                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}; CacheKey={CacheKey}; CachePath={CachePath}", "id_m4678k3z_item", cache.Key, filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_m4678k3z");
        }
        finally
        {
            Volatile.Write(ref _clearCacheFiles, 0);
        }
    }
    #endregion


    #region ContainsKey
    public bool ContainsKey<T>(string key, out T value)
        => ContainsKey(key, out value, out _);

    public bool ContainsKey<T>(string key, out T value, out DateTimeOffset ex)
    {
        if (memoryCache.TryGetValue(key, out T _mv))
        {
            value = _mv;
            ex = default;
            return true;
        }

        string md5key = CrypTo.md5(key);
        if (tempDb.TryGetValue(md5key, out var _temp))
        {
            value = (T)_temp.value;
            ex = _temp.ex;
            return true;
        }

        value = default;

        if (cacheFiles.TryGetValue(md5key, out cacheEntry _cache))
        {
            ex = _cache.ex;
            return _cache.ex > DateTimeOffset.Now;
        }

        ex = default;
        return false;
    }
    #endregion

    #region TryGetValue
    public bool TryGetValue<TItem>(string key, out TItem value, JsonTypeInfo<TItem> jsonType = null, bool textJson = false)
    {
        if (ContainsKey(key, out TItem entryValue, out _))
        {
            if (entryValue != null && !entryValue.Equals(default(TItem)))
            {
                value = entryValue;
                return true;
            }
            else
            {
                var entry = EntryAsync(key, fileCache: true, jsonType: jsonType, textJson: textJson).Result;
                if (entry != null && entry.success)
                {
                    value = entry.value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
    #endregion

    #region EntryAsync
    public async Task<HybridCacheEntry<TItem>> EntryAsync<TItem>(string key, bool fileCache = false, JsonTypeInfo<TItem> jsonType = default, bool textJson = false)
    {
        if (!fileCache && memoryCache.TryGetValue(key, out TItem value))
            return new HybridCacheEntry<TItem>(true, value, false);

        var entry = await ReadCacheAsync(key, fileCache, jsonType, textJson);
        if (entry.succes)
            return new HybridCacheEntry<TItem>(true, entry.value, entry.singleCache);

        return new HybridCacheEntry<TItem>(false, default, false);
    }
    #endregion

    #region ReadCacheAsync
    private async Task<(bool succes, TItem value, bool singleCache)> ReadCacheAsync<TItem>(string key, bool fileCache, JsonTypeInfo<TItem> jsonType, bool textJson = false)
    {
        string md5key = CrypTo.md5(key);

        try
        {
            var type = typeof(TItem);
            bool isText = TypeCache<TItem>.IsText;
            bool IsDeserialize = textJson || jsonType != default || TypeCache<TItem>.IsDeserializable;

            if (!isText && !IsDeserialize)
                return default;

            if (!fileCache && tempDb.TryGetValue(md5key, out var _temp))
            {
                return (true, (TItem)_temp.value, false);
            }
            else
            {
                if (!cacheFiles.TryGetValue(md5key, out cacheEntry _cache))
                    return default;

                if (DateTimeOffset.Now > _cache.ex)
                {
                    cacheFiles.TryRemove(md5key, out _);
                    return default;
                }

                TItem value;
                string path = $"cache/fdb/{_cache.path[0]}/{_cache.path}";

                if (IsDeserialize)
                {
                    if (textJson || jsonType != default)
                    {
                        #region System.Text.Json
                        await using (var fs = new FileStream(path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            PoolInvk.bufferSize,
                            options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            await using (var gzip = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: true))
                            {
                                if (jsonType != default)
                                    value = await System.Text.Json.JsonSerializer.DeserializeAsync(gzip, jsonType).ConfigureAwait(false);
                                else
                                    value = await System.Text.Json.JsonSerializer.DeserializeAsync<TItem>(gzip).ConfigureAwait(false);
                            }
                        }
                        #endregion
                    }
                    else
                    {
                        #region Newtonsoft
                        using (var msm = PoolInvk.msm.GetStream())
                        {
                            await using (var fs = new FileStream(path,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read,
                                PoolInvk.bufferSize,
                                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                            {
                                await using (var gzip = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: true))
                                {
                                    using (var byteBuf = new BufferPool())
                                    {
                                        int bytesRead;
                                        var memBuf = byteBuf.Memory;

                                        while ((bytesRead = await gzip.ReadAsync(memBuf).ConfigureAwait(false)) > 0)
                                            msm.Write(memBuf.Span.Slice(0, bytesRead));
                                    }
                                }
                            }

                            msm.Position = 0;
                            using (var sr = new JsonStreamReaderPool(msm, Encoding.UTF8, leaveOpen: true))
                            {
                                using (var jsonReader = new JsonTextReader(sr)
                                {
                                    ArrayPool = NewtonsoftPool.Array
                                })
                                {
                                    var serializer = Newtonsoft.Json.JsonSerializer.CreateDefault();

                                    if (IsCapacityCollection(type) && _cache.capacity > 0)
                                    {
                                        var instance = CreateCollectionWithCapacity(type, _cache.capacity);
                                        if (instance != null)
                                        {
                                            serializer.Populate(jsonReader, instance);
                                            value = (TItem)instance;
                                        }
                                        else
                                        {
                                            value = serializer.Deserialize<TItem>(jsonReader);
                                        }
                                    }
                                    else
                                    {
                                        value = serializer.Deserialize<TItem>(jsonReader);
                                    }
                                }
                            }
                        }
                        #endregion
                    }
                }
                else
                {
                    #region string
                    string val = await File.ReadAllTextAsync(path).ConfigureAwait(false);

                    if (typeof(TItem) == typeof(string))
                        value = (TItem)(object)val;
                    else
                    {
                        value = (TItem)Convert.ChangeType(val, typeof(TItem), CultureInfo.InvariantCulture);
                    }
                    #endregion
                }

                if (value is null)
                    return default;

                bool singleCache = true;

                if (CoreInit.conf.cache.memExtend && CoreInit.conf.cache.extend > 0)
                {
                    singleCache = false;
                    var targetEx = DateTimeOffset.Now.AddSeconds(CoreInit.conf.cache.extend);
                    memoryCache.Set(key, value, targetEx > _cache.ex ? _cache.ex : targetEx);
                }

                return (true, value, singleCache);
            }
        }
        catch (Exception ex)
        {
            if (cacheFiles.TryRemove(md5key, out var badEntry))
            {
                try
                {
                    File.Delete($"cache/fdb/{badEntry.path[0]}/{badEntry.path}");
                }
                catch { }
            }

            tempDb.TryRemove(md5key, out _);
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_76d40030");
            Console.WriteLine($"HybridFileCache.ReadCache({key}): {ex}\n\n");
        }

        return default;
    }
    #endregion


    #region Set
    public TItem Set<TItem>(string key, TItem value, DateTimeOffset absoluteExpiration, bool? inmemory = null, bool textJson = false)
    {
        if (inmemory != true && CoreInit.conf.cache.type != "mem" && WriteCache(key, value, absoluteExpiration, default, textJson))
            return value;

        if (inmemory != true)
            Console.WriteLine($"set memory: {key} / {DateTimeOffset.Now}");

        return memoryCache.Set(key, value, absoluteExpiration);
    }

    public TItem Set<TItem>(string key, TItem value, TimeSpan absoluteExpirationRelativeToNow, bool? inmemory = null, bool textJson = false)
    {
        if (inmemory != true && CoreInit.conf.cache.type != "mem" && WriteCache(key, value, default, absoluteExpirationRelativeToNow, textJson))
            return value;

        if (inmemory != true)
            Console.WriteLine($"set memory: {key} / {DateTimeOffset.Now}");

        return memoryCache.Set(key, value, absoluteExpirationRelativeToNow);
    }
    #endregion

    #region WriteCache
    private bool WriteCache<TItem>(string key, TItem value, DateTimeOffset absoluteExpiration, TimeSpan absoluteExpirationRelativeToNow, bool textJson)
    {
        try
        {
            // кеш уже получен от другого rch клиента
            string md5key = CrypTo.md5(key);
            if (tempDb.ContainsKey(md5key))
                return true;

            var now = DateTimeOffset.Now;

            if (absoluteExpiration == default)
            {
                if (absoluteExpirationRelativeToNow == default)
                    absoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);

                absoluteExpiration = now.Add(absoluteExpirationRelativeToNow);
            }

            /// защита от асинхронных rch запросов которые приходят в рамках 12 секунд
            /// дополнительный кеш для сериалов, что бы выборка сезонов/озвучки не дергала IO
            DateTimeOffset extend = now.AddSeconds(Math.Max(15, CoreInit.conf.cache.extend));

            /// ограничиваем время хранение кеша в RAM для быстрого сброса в IO
            /// не меньше 15s, но не больше 60s
            if (CoreInit.conf.lowMemoryMode || key.StartsWith("ipkey:"))
                extend = now.AddSeconds(Math.Max(15, Math.Min(60, CoreInit.conf.cache.extend)));

            if (extend.AddSeconds(60) >= absoluteExpiration)
            {
                memoryCache.Set(key, value, absoluteExpiration);
                return true;
            }

            var type = typeof(TItem);
            bool isText = TypeCache<TItem>.IsText;
            bool IsSerialize = textJson || TypeCache<TItem>.IsDeserializable;

            if (!isText && !IsSerialize)
                return false;

            tempDb.TryAdd(md5key, new TempEntry(extend, IsSerialize, textJson, absoluteExpiration, value));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CatchId={CatchId}", "id_yypxq9n6");
            return false;
        }
    }
    #endregion
}
