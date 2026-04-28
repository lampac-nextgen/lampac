using Newtonsoft.Json;
using Shared.Services.Pools.Json;
using System.Collections;
using System.IO.Compression;
using System.Text;

namespace Shared.Services.Utilities;

public static class JsonHelper
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext("SourceContext", nameof(JsonHelper));

    #region ListReader
    public static async Task<List<T>> ListReader<T>(string filePath, int capacity = 0)
    {
        var items = new List<T>(capacity);

        await using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, PoolInvk.bufferSize, options: FileOptions.Asynchronous))
        {
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: true))
            {
                using (var reader = new JsonStreamReaderPool(gzipStream, Encoding.UTF8, leaveOpen: true))
                {
                    using (var jsonReader = new JsonTextReader(reader))
                    {
                        var serializer = new JsonSerializer();
                        while (jsonReader.Read())
                        {
                            if (jsonReader.TokenType == JsonToken.StartObject)
                            {
                                try
                                {
                                    items.Add(serializer.Deserialize<T>(jsonReader));
                                }
                                catch (System.Exception ex)
                                {
                                    Log.Error(ex, "CatchId={CatchId}", "id_pz0shzjo");
                                }
                            }
                        }
                    }
                }
            }
        }

        return items;
    }
    #endregion

    #region IEnumerableReader
    public static IEnumerable<T> IEnumerableReader<T>(string filePath)
    {
        if (!File.Exists(filePath))
            return Enumerable.Empty<T>();

        return new JsonItemEnumerable<T>(filePath);
    }

    private class JsonItemEnumerable<T> : IEnumerable<T>
    {
        readonly string filePath;

        public JsonItemEnumerable(string filePath)
        {
            this.filePath = filePath;
        }

        public IEnumerator<T> GetEnumerator()
            => new JsonItemEnumerator(filePath);

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        private class JsonItemEnumerator : IEnumerator<T>
        {
            readonly string filePath;
            readonly JsonSerializer serializer = new JsonSerializer();

            FileStream fileStream;
            GZipStream gzipStream;
            StreamReader reader;
            JsonTextReader jsonReader;

            bool disposed;

            public JsonItemEnumerator(string filePath)
            {
                this.filePath = filePath;
                Initialize();
            }

            public T Current { get; private set; }

            object IEnumerator.Current => Current;

            void Initialize()
            {
                try
                {
                    fileStream = FileReaderPool.Rent(filePath);

                    gzipStream = new GZipStream(
                        fileStream,
                        CompressionMode.Decompress,
                        leaveOpen: true);

                    reader = new StreamReader(
                        gzipStream,
                        Encoding.UTF8,
                        false,
                        CoreInit.conf.lowMemoryMode ? 1024 : PoolInvk.bufferSize);

                    jsonReader = new JsonTextReader(reader)
                    {
                        ArrayPool = NewtonsoftPool.Array
                    };
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public bool MoveNext()
            {
                if (jsonReader == null)
                    return false;

                while (jsonReader.Read())
                {
                    if (jsonReader.TokenType == JsonToken.StartObject)
                    {
                        try
                        {
                            Current = serializer.Deserialize<T>(jsonReader);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_qccuip0u");
                        }
                    }
                }

                Current = default;
                return false;
            }

            public void Reset()
                => throw new NotSupportedException();

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;

                try
                {
                    jsonReader?.Close();
                    reader?.Dispose();
                    gzipStream?.Dispose();
                }
                finally
                {
                    jsonReader = null;
                    reader = null;
                    gzipStream = null;

                    if (fileStream != null)
                    {
                        var fs = fileStream;
                        fileStream = null;

                        FileReaderPool.Return(filePath, fs);
                    }
                }
            }
        }
    }
    #endregion

    #region DictionaryReader
    public static IEnumerable<KeyValuePair<string, TValue>> DictionaryReader<TValue>(string filePath)
    {
        if (!File.Exists(filePath))
            return Enumerable.Empty<KeyValuePair<string, TValue>>();

        return new JsonDictionaryEnumerable<TValue>(filePath);
    }

    private class JsonDictionaryEnumerable<TValue> : IEnumerable<KeyValuePair<string, TValue>>
    {
        readonly string filePath;

        public JsonDictionaryEnumerable(string filePath)
        {
            this.filePath = filePath;
        }

        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator()
            => new JsonDictionaryEnumerator(filePath);

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        private class JsonDictionaryEnumerator : IEnumerator<KeyValuePair<string, TValue>>
        {
            readonly string filePath;
            readonly JsonSerializer serializer = new JsonSerializer();

            FileStream fileStream;
            StreamReader reader;
            JsonTextReader jsonReader;

            bool disposed;

            public JsonDictionaryEnumerator(string filePath)
            {
                this.filePath = filePath;
                Initialize();
            }

            public KeyValuePair<string, TValue> Current { get; private set; }

            object IEnumerator.Current => Current;

            void Initialize()
            {
                try
                {
                    fileStream = FileReaderPool.Rent(filePath);

                    reader = new StreamReader(
                        fileStream,
                        Encoding.UTF8,
                        false,
                        CoreInit.conf.lowMemoryMode ? 1024 : PoolInvk.bufferSize);

                    jsonReader = new JsonTextReader(reader)
                    {
                        ArrayPool = NewtonsoftPool.Array
                    };

                    jsonReader.Read();

                    if (jsonReader.TokenType != JsonToken.StartObject)
                        throw new JsonReaderException("Expected JSON object for dictionary.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public bool MoveNext()
            {
                if (jsonReader == null)
                    return false;

                while (jsonReader.Read())
                {
                    if (jsonReader.TokenType == JsonToken.PropertyName)
                    {
                        var key = (string)jsonReader.Value;

                        try
                        {
                            if (!jsonReader.Read())
                                return false;

                            var value = serializer.Deserialize<TValue>(jsonReader);

                            Current = new KeyValuePair<string, TValue>(key, value);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "CatchId={CatchId}", "id_dictionary_reader");
                        }
                    }

                    if (jsonReader.TokenType == JsonToken.EndObject)
                        break;
                }

                Current = default;
                return false;
            }

            public void Reset()
                => throw new NotSupportedException();

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;

                try
                {
                    jsonReader?.Close();
                    reader?.Dispose();
                }
                finally
                {
                    jsonReader = null;
                    reader = null;

                    if (fileStream != null)
                    {
                        var fs = fileStream;
                        fileStream = null;

                        FileReaderPool.Return(filePath, fs);
                    }
                }
            }
        }
    }
    #endregion
}
