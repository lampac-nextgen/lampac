using Shared.Services.HTTP;

namespace NextHUB;

/// <summary>
/// Resolves models that are intentionally not parsed during list loading.
/// This keeps list pages fast: the client asks this resolver only for the
/// video card whose context menu is opened.
/// </summary>
public sealed partial class ModelProbeResolver
{
    readonly string host;

    public ModelProbeResolver(string host)
    {
        this.host = host?.TrimEnd('/');
    }

    public async Task<List<ModelItem>> Resolve(string plugin, string href, string current)
    {
        href = NormalizeHref(host, href);

        if (!TryGetSourceSettings(plugin, out string cachePrefix, out int timeoutSeconds, out int cacheHours))
            return null;

        var cache = HybridCache.GetMemory();
        string memKey = $"{cachePrefix}:{href}";

        if (cache.TryGetValue(memKey, out object cached))
        {
            if (cached is List<ModelItem> modelItems)
                return FilterCurrentModels(plugin, current, modelItems);

            if (cached is ModelItem modelItem)
                return FilterCurrentModels(plugin, current, new[] { modelItem });
        }

        try
        {
            string card = await Http.Get(
                href,
                referer: ModelProbeReferer(plugin),
                timeoutSeconds: timeoutSeconds
            );

            var models = ParseModels(plugin, card);
            if (models is not { Count: > 0 })
                return null;

            using var entry = cache.CreateEntry(memKey);
            entry.Value = models;
            entry.AbsoluteExpiration = DateTimeOffset.Now.AddHours(cacheHours);

            return FilterCurrentModels(plugin, current, models);
        }
        catch
        {
            return null;
        }
    }

    string ModelProbeReferer(string plugin)
    {
        return string.Equals(plugin, "prostoporno", StringComparison.OrdinalIgnoreCase)
            ? "https://prostoporno.red/"
            : $"{host}/";
    }

    static List<ModelItem> FilterCurrentModels(string plugin, string current, IEnumerable<ModelItem> models)
    {
        if (models == null)
            return null;

        var filtered = new List<ModelItem>();
        foreach (var model in models)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.uri) || string.IsNullOrWhiteSpace(model.name))
                continue;

            if (!IsSameCurrentModel(plugin, current, model))
                AddUniqueModel(filtered, model);
        }

        return filtered.Count == 0 ? null : filtered;
    }

    static void AddUniqueModel(List<ModelItem> models, ModelItem model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.uri) || string.IsNullOrWhiteSpace(model.name))
            return;

        string modelHref = ExtractModelHref(model);

        if (models.Any(i =>
            string.Equals(ExtractModelHref(i), modelHref, StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(modelHref) && string.Equals(i.name, model.name, StringComparison.OrdinalIgnoreCase))
        ))
        {
            return;
        }

        models.Add(model);
    }

    static bool IsSameCurrentModel(string plugin, string current, ModelItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(current))
            return false;

        string pluginName = plugin?.ToLowerInvariant();
        if (pluginName == "batsa" || pluginName == "bigboss")
            return string.Equals(current, item.name, StringComparison.OrdinalIgnoreCase);

        if (pluginName == "ebun" || pluginName == "jopaonline")
            return current.IndexOf(item.name ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;

        string modelHref = ExtractModelHref(item)?.Trim('/');
        if (string.IsNullOrEmpty(modelHref))
            return false;

        if (pluginName == "pornone")
        {
            foreach (string value in current.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                if (modelHref.Equals(value.Trim('/'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (pluginName == "sosushka")
            return modelHref.Equals($"model/{current.Trim('/')}", StringComparison.OrdinalIgnoreCase);

        if (pluginName == "ebasos" || pluginName == "huyamba" || pluginName == "porndig" || pluginName == "pornoakt" || pluginName == "porn4days" || pluginName == "porno666" || pluginName == "pornobolt" || pluginName == "pornobriz" || pluginName == "pornokaef" || pluginName == "rusvideos" || pluginName == "trahkino" || pluginName == "veporn" || pluginName == "xxxperevod")
            return modelHref.Equals(current.Trim('/'), StringComparison.OrdinalIgnoreCase);

        return false;
    }

    static string ExtractModelHref(ModelItem item)
    {
        string uri = item?.uri ?? string.Empty;
        string encrypted = Regex.Match(uri, "model=([^&]+)").Groups[1].Value;
        return string.IsNullOrEmpty(encrypted) ? null : AesTo.Decrypt(encrypted);
    }

    static string NormalizeHref(string host, string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return href;

        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;

        if (href.StartsWith("//"))
            return $"https:{href}";

        return href.StartsWith("/")
            ? $"{host}{href}"
            : $"{host}/{href}";
    }

}
