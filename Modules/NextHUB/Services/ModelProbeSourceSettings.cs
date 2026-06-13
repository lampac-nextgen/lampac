namespace NextHUB;

public sealed partial class ModelProbeResolver
{
    readonly record struct ModelProbeSourceSettings(
        string CachePrefix,
        int TimeoutSeconds = 5,
        int CacheHours = 24
    );

    static readonly IReadOnlyDictionary<string, ModelProbeSourceSettings> SourceSettings =
        new Dictionary<string, ModelProbeSourceSettings>(StringComparer.OrdinalIgnoreCase)
        {
            ["24rolika"] = new("24rolika:model:v1"),
            ["24video"] = new("24video:model:v3", TimeoutSeconds: 2),
            ["batsa"] = new("batsa:model:v1", CacheHours: 12),
            ["bigboss"] = new("bigboss:model:v1"),
            ["ebun"] = new("ebun:model:v2"),
            ["ebasos"] = new("ebasos:model:v1"),
            ["huyamba"] = new("huyamba:model:v2"),
            ["jopaonline"] = new("jopaonline:model:v1"),
            ["lenporno"] = new("lenporno:model:v1", TimeoutSeconds: 2),
            ["pornone"] = new("pornone:model:v2"),
            ["pornobolt"] = new("pornobolt:model:v1"),
            ["pornobriz"] = new("pornobriz:model:v1"),
            ["pornokaef"] = new("pornokaef:model:v1"),
            ["porndig"] = new("porndig:model:v1"),
            ["pornoakt"] = new("pornoakt:model:v1"),
            ["porn4days"] = new("porn4days:model:v1"),
            ["porno666"] = new("porno666:model:v1"),
            ["prostoporno"] = new("prostoporno:model:v2", TimeoutSeconds: 6),
            ["rusvideos"] = new("rusvideos:model:v1"),
            ["sosushka"] = new("sosushka:model:v1"),
            ["trahkino"] = new("trahkino:model:v1"),
            ["veporn"] = new("veporn:model:v1"),
            ["vtrahetv"] = new("vtrahetv:model:v1", TimeoutSeconds: 2),
            ["xxxperevod"] = new("xxxperevod:model:v1")
        };

    static bool TryGetSourceSettings(string plugin, out string cachePrefix, out int timeoutSeconds, out int cacheHours)
    {
        cachePrefix = null;
        timeoutSeconds = 5;
        cacheHours = 24;

        if (string.IsNullOrWhiteSpace(plugin) || !SourceSettings.TryGetValue(plugin, out var settings))
            return false;

        cachePrefix = settings.CachePrefix;
        timeoutSeconds = settings.TimeoutSeconds;
        cacheHours = settings.CacheHours;
        return true;
    }
}
