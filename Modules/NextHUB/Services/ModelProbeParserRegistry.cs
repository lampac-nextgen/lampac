namespace NextHUB;

public sealed partial class ModelProbeResolver
{
    static ModelItem ParseModel(string plugin, string html)
    {
        return plugin?.ToLowerInvariant() switch
        {
            "24rolika" => Parse24RolikaModel(plugin, html),
            "24video" => Parse24VideoModel(plugin, html),
            "batsa" => ParseBatsaModel(plugin, html),
            "bigboss" => ParseBigbossModel(plugin, html),
            "ebasos" => ParseEbasosModel(plugin, html),
            "ebun" => ParseEbunModel(plugin, html),
            "huyamba" => ParseHuyambaModel(plugin, html),
            "jopaonline" => ParseJopaonlineModel(plugin, html),
            "lenporno" => ParseLenpornoModel(plugin, html),
            "pornone" => ParsePornoneModel(plugin, html),
            "pornobolt" => ParsePornoboltModel(plugin, html),
            "pornobriz" => ParsePornobrizModel(plugin, html),
            "pornokaef" => ParsePornokaefModel(plugin, html),
            "porndig" => ParsePorndigModel(plugin, html),
            "pornoakt" => ParsePornoaktModel(plugin, html),
            "porn4days" => ParsePorn4daysModel(plugin, html),
            "porno666" => ParsePorno666Model(plugin, html),
            "prostoporno" => ParseProstopornoModel(plugin, html),
            "rusvideos" => ParseRusvideosModel(plugin, html),
            "sosushka" => ParseSosushkaModel(plugin, html),
            "trahkino" => ParseTrahkinoModel(plugin, html),
            "veporn" => ParseVepornModel(plugin, html),
            "vtrahetv" => ParseVtrahetvModel(plugin, html),
            "xxxperevod" => ParseXxxperevodModel(plugin, html),
            _ => null
        };
    }

    static List<ModelItem> ParseModels(string plugin, string html)
    {
        if (string.Equals(plugin, "huyamba", StringComparison.OrdinalIgnoreCase))
            return ParseHuyambaModels(plugin, html);

        if (string.Equals(plugin, "ebasos", StringComparison.OrdinalIgnoreCase))
            return ParseEbasosModels(plugin, html);

        if (string.Equals(plugin, "pornoakt", StringComparison.OrdinalIgnoreCase))
            return ParsePornoaktModels(plugin, html);

        if (string.Equals(plugin, "porn4days", StringComparison.OrdinalIgnoreCase))
            return ParsePorn4daysModels(plugin, html);

        if (string.Equals(plugin, "pornobolt", StringComparison.OrdinalIgnoreCase))
            return ParsePornoboltModels(plugin, html);

        if (string.Equals(plugin, "pornobriz", StringComparison.OrdinalIgnoreCase))
            return ParsePornobrizModels(plugin, html);

        if (string.Equals(plugin, "pornokaef", StringComparison.OrdinalIgnoreCase))
            return ParsePornokaefModels(plugin, html);

        var model = ParseModel(plugin, html);
        return model == null ? null : new List<ModelItem> { model };
    }


}
