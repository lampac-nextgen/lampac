using HtmlAgilityPack;
using System.Web;

namespace NextHUB;

public sealed partial class ModelProbeResolver
{
    static ModelItem BuildModel(string plugin, string modelName, string modelHref)
    {
        return string.IsNullOrEmpty(modelHref) || string.IsNullOrEmpty(modelName)
            ? null
            : new ModelItem(
                modelName,
                $"nexthub?plugin={AesTo.Encrypt(plugin)}&model={AesTo.Encrypt(modelHref)}"
            );
    }

    static string ToRelativeHref(string href)
    {
        if (string.IsNullOrEmpty(href))
            return href;

        return Regex.Replace(
            href,
            "^https?://[^/]+/",
            string.Empty,
            RegexOptions.IgnoreCase
        ).TrimStart('/');
    }

    static string ToAsciiSlug(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return null;

        string slug = HtmlEntity.DeEntitize(modelName).ToLowerInvariant();
        slug = Regex.Replace(slug, "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? null : slug;
    }

    static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = HtmlEntity.DeEntitize(value).ToLowerInvariant();
        value = Regex.Replace(value, "[^\\p{L}\\p{Nd}]+", " ");
        value = Regex.Replace(value, "\\s+", " ").Trim();
        return value;
    }


}
