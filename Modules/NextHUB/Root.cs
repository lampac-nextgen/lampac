using HtmlAgilityPack;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Playwright;
using Shared.Services.Pools;
using Shared.Models.SISI.NextHUB;
using YamlDotNet.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace NextHUB;

public static class Root
{
    #region evalOptionsFull
    public readonly static ScriptOptions evalOptionsFull = ScriptOptions.Default
        .AddReferences(typeof(IRoute).Assembly)
        .AddImports("Microsoft.Playwright")
        .AddReferences(typeof(Shared.Startup).Assembly)
        .AddImports("Shared.PlaywrightCore")
        .AddImports("Shared.Services")
        .AddImports("Shared.Services.Utilities")
        .AddImports("Shared.Models.SISI.Base")
        .AddImports("Shared.Models.SISI")
        .AddReferences(typeof(Newtonsoft.Json.JsonConvert).Assembly)
        .AddImports("Newtonsoft.Json")
        .AddImports("Newtonsoft.Json.Linq");
    #endregion

    #region playlistOptions
    public readonly static ScriptOptions playlistOptions = ScriptOptions.Default
        .AddReferences(typeof(Shared.Startup).Assembly)
        .AddImports("Shared.Models.SISI.Base")
        .AddImports("Shared.Models.SISI")
        .AddImports("Shared.Services.Utilities")
        .AddReferences(typeof(System.Text.Json.JsonDocument).Assembly)
        .AddImports("System.Text.Json")
        .AddReferences(typeof(HtmlDocument).Assembly)
        .AddImports("HtmlAgilityPack");
    #endregion

    #region routeOptions
    public readonly static ScriptOptions routeOptions = ScriptOptions.Default
        .AddReferences(typeof(IRoute).Assembly)
        .AddReferences(typeof(Shared.Startup).Assembly)
        .AddImports("Shared.Services.Utilities")
        .AddImports("Microsoft.Playwright");
    #endregion


    public static NxtSettings goInit(string plugin)
    {
        if (string.IsNullOrEmpty(plugin))
            return null;

        plugin = Regex.Replace(plugin, "[^a-z0-9\\-]+", "", RegexOptions.IgnoreCase);

        if (ModInit.conf.sites_enabled != null && !ModInit.conf.sites_enabled.Contains(plugin))
            return null;

        if (!File.Exists($"{ModInit.modpath}/sites/{plugin}.yaml"))
            return null;

        var memoryCache = HybridCache.GetMemory();

        string fileKeyId = changeFileId(plugin, memoryCache);
        string memKey = $"NextHUB:goInit:{plugin}:{fileKeyId}";

        if (!memoryCache.TryGetValue(memKey, out NxtSettings init))
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithTypeMapping<IReadOnlyDictionary<string, string>, Dictionary<string, string>>()
                    .Build();

                // Чтение основного YAML-файла
                string yaml = File.ReadAllText($"{ModInit.modpath}/sites/{plugin}.yaml");
                var target = deserializer.Deserialize<Dictionary<object, object>>(yaml);

                foreach (string y in new string[] { "_", plugin })
                {
                    if (File.Exists($"{ModInit.modpath}/override/{y}.yaml"))
                    {
                        // Чтение пользовательского YAML-файла
                        string myYaml = File.ReadAllText($"{ModInit.modpath}/override/{y}.yaml");
                        var mySource = deserializer.Deserialize<Dictionary<object, object>>(myYaml);

                        // Объединение словарей
                        foreach (var property in mySource)
                        {
                            if (!target.ContainsKey(property.Key))
                            {
                                target[property.Key] = property.Value;
                                continue;
                            }

                            if (property.Value is IDictionary<object, object> sourceDict &&
                                target[property.Key] is IDictionary<object, object> targetDict)
                            {
                                // Рекурсивное объединение вложенных словарей
                                foreach (var item in sourceDict)
                                    targetDict[item.Key] = item.Value;
                            }
                            else
                            {
                                target[property.Key] = property.Value;
                            }
                        }
                    }
                }

                // Преобразование словаря в объект NxtSettings
                var serializer = new SerializerBuilder().Build();

                var yamlResult = serializer.Serialize(target);
                init = deserializer.Deserialize<NxtSettings>(yamlResult);

                if (string.IsNullOrEmpty(init.plugin))
                    init.plugin = plugin;

                if (!init.debug)
                {
                    init = ModuleInvoke.Init(plugin, init);
                    memoryCache.Set(memKey, init, DateTime.Today.AddDays(1));
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "CatchId={CatchId}", "id_bd278ba3");

                init = new NxtSettings();
                memoryCache.Set(memKey, init, DateTime.Now.AddMinutes(5));
            }
        }

        return init;
    }


    static string changeFileId(string plugin, IMemoryCache memoryCache)
    {
        if (CoreInit.conf.lowMemoryMode)
            return string.Empty;

        string memKey = $"NextHUB:changeFileId:{plugin}:{CoreInit.conf.guid}";
        if (!memoryCache.TryGetValue(memKey, out string fileKeyId))
        {
            var sb = StringBuilderPool.ThreadInstance;

            sb.Append(CoreInit.conf.guid);
            sb.Append(File.GetLastWriteTimeUtc($"{ModInit.modpath}/sites/{plugin}.yaml").ToString());

            foreach (string y in new string[] { "_", plugin })
            {
                if (File.Exists($"{ModInit.modpath}/override/{y}.yaml"))
                    sb.Append(File.GetLastWriteTimeUtc($"{ModInit.modpath}/override/{y}.yaml").ToString());
            }

            fileKeyId = sb.ToString();

            memoryCache.Set(memKey, fileKeyId, DateTime.Now.AddMinutes(1));
        }

        return fileKeyId;
    }
}
