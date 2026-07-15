using HtmlAgilityPack;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Primitives;
using Microsoft.Playwright;
using Shared.Services.Pools;
using Shared.Models.SISI.NextHUB;
using YamlDotNet.Serialization;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Net.Sockets;

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
        .AddReferences(typeof(HtmlDocument).Assembly)
        .AddImports("HtmlAgilityPack");
    #endregion

    #region routeOptions
    public readonly static ScriptOptions routeOptions = ScriptOptions.Default
        .AddReferences(typeof(IRoute).Assembly)
        .AddImports("Microsoft.Playwright");
    #endregion


    public static NxtSettings goInit(string plugin)
    {
        if (string.IsNullOrEmpty(plugin) || plugin.Length > 64 ||
            !Regex.IsMatch(plugin, "\\A[a-z0-9-]+\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return null;

        if (!string.IsNullOrWhiteSpace(ModInit.conf.sites_enabled))
        {
            string[] enabledSites = Regex.Split(ModInit.conf.sites_enabled, "[,;|\\s]+")
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .ToArray();

            if (!enabledSites.Contains(plugin, StringComparer.OrdinalIgnoreCase))
                return null;
        }

        string siteFile = Path.Combine(ModInit.modpath, "sites", $"{plugin}.yaml");
        if (!File.Exists(siteFile))
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
                string yaml = File.ReadAllText(siteFile);
                var target = deserializer.Deserialize<Dictionary<object, object>>(yaml);

                foreach (string y in new string[] { "_", plugin })
                {
                    string overrideFile = Path.Combine(ModInit.modpath, "override", $"{y}.yaml");
                    if (File.Exists(overrideFile))
                    {
                        // Чтение пользовательского YAML-файла
                        string myYaml = File.ReadAllText(overrideFile);
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


    #region request security
    public static (IQueryCollection query, string cacheKey) getEvalQuery(IQueryCollection requestQuery, NxtSettings init)
    {
        if (requestQuery == null || requestQuery.Count == 0)
            return (QueryCollection.Empty, string.Empty);

        var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (init.menu?.customs != null)
        {
            foreach (var custom in init.menu.customs)
            {
                if (!string.IsNullOrWhiteSpace(custom.arg))
                    allowedKeys.Add(custom.arg);
            }
        }

        void addArgs(ContentParseSettings parse)
        {
            if (parse?.args == null)
                return;

            foreach (var arg in parse.args)
            {
                if (!string.IsNullOrWhiteSpace(arg.name))
                    allowedKeys.Add(arg.name);
            }
        }

        addArgs(init.contentParse);
        addArgs(init.list?.contentParse);
        addArgs(init.search?.contentParse);
        addArgs(init.model?.contentParse);
        addArgs(init.view?.relatedParse);

        if (allowedKeys.Count == 0)
            return (QueryCollection.Empty, string.Empty);

        var values = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var hash = Fnv1a.Empty;

        foreach (string key in allowedKeys.OrderBy(i => i, StringComparer.Ordinal))
        {
            if (!requestQuery.TryGetValue(key, out StringValues value) || StringValues.IsNullOrEmpty(value))
                continue;

            values[key] = value;
            Fnv1a.Append(ref hash, key);

            foreach (string item in value)
                Fnv1a.Append(ref hash, item);
        }

        if (values.Count == 0)
            return (QueryCollection.Empty, string.Empty);

        return (new QueryCollection(values), $":{Fnv1a.Base64Url(hash)}");
    }

    public static bool isSafeHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string hostname;
        try
        {
            hostname = uri.IdnHost.TrimEnd('.');
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (string.IsNullOrEmpty(hostname) ||
            hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            hostname.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            hostname.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(hostname, out IPAddress address) || !isPrivateAddress(address);
    }

    static bool isPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte a = bytes[0];
            byte b = bytes[1];

            return a == 0 || a == 10 || a == 127 ||
                   (a == 100 && b >= 64 && b <= 127) ||
                   (a == 169 && b == 254) ||
                   (a == 172 && b >= 16 && b <= 31) ||
                   (a == 192 && (b == 0 || b == 168)) ||
                   (a == 198 && (b == 18 || b == 19)) ||
                   a >= 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return true;

        return address.Equals(IPAddress.IPv6Any) ||
               address.Equals(IPAddress.IPv6None) ||
               address.IsIPv6LinkLocal ||
               address.IsIPv6SiteLocal ||
               address.IsIPv6Multicast ||
               (bytes[0] & 0xfe) == 0xfc;
    }
    #endregion


    static string changeFileId(string plugin, IMemoryCache memoryCache)
    {
        if (CoreInit.conf.lowMemoryMode)
            return string.Empty;

        string memKey = $"NextHUB:changeFileId:{plugin}:{CoreInit.conf.guid}";
        if (!memoryCache.TryGetValue(memKey, out string fileKeyId))
        {
            var sb = StringBuilderPool.ThreadInstance;

            sb.Append(CoreInit.conf.guid);
            sb.Append(File.GetLastWriteTimeUtc(Path.Combine(ModInit.modpath, "sites", $"{plugin}.yaml")).ToString());

            foreach (string y in new string[] { "_", plugin })
            {
                string overrideFile = Path.Combine(ModInit.modpath, "override", $"{y}.yaml");
                if (File.Exists(overrideFile))
                    sb.Append(File.GetLastWriteTimeUtc(overrideFile).ToString());
            }

            fileKeyId = sb.ToString();

            memoryCache.Set(memKey, fileKeyId, DateTime.Now.AddMinutes(1));
        }

        return fileKeyId;
    }
}
