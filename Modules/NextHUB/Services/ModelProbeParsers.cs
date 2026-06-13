using HtmlAgilityPack;
using System.Web;

namespace NextHUB;

public sealed partial class ModelProbeResolver
{
    static ModelItem Parse24RolikaModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "Актрисы:\\s*<a href=\"([^\"]+)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        string modelHref = m.Groups[1].Value?.Trim();
        string modelName = HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim();

        if (!string.IsNullOrEmpty(modelHref))
            modelHref = modelHref.TrimStart('/');

        return BuildModel(plugin, modelName, modelHref);
    }

    static ModelItem Parse24VideoModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<div class=\"row models\">.*?<a href=\"([^\"]+/pornstar/[^\"]+/)\"[^>]*class=\"link\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            m.Groups[1].Value
        );
    }

    static ModelItem ParseBatsaModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<h3>В ролях:</h3>.*?<a href=\"([^\"]+)\"[^>]*>\\s*<div class=\"grid-item-title-custom-unique\">([^<]+)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        string modelHref = m.Groups[1].Value?.Trim();
        string modelName = HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim();

        if (!string.IsNullOrEmpty(modelHref))
            modelHref = modelHref.TrimStart('/');

        return BuildModel(plugin, modelName, modelHref);
    }

    static ModelItem ParseBigbossModel(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNode = cardDoc.DocumentNode.SelectSingleNode(
            "//div[contains(@class,'video__content-categories-gr')][.//div[contains(@class,'video__content-categories-gr-name') and contains(.,'В этом видео')]]//a[contains(@href,'/tags/')]"
        );

        string modelHref = ToRelativeHref(modelNode?.GetAttributeValue("href", null)?.Trim());
        string modelName = HtmlEntity.DeEntitize(modelNode?.InnerText)?.Trim();

        return BuildModel(plugin, modelName, modelHref);
    }

    static ModelItem ParseEbunModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<a[^>]+href=\"((?:https?:)?//[^\"]+/models/[^\"/]+/?|/models/[^\"/]+/?|models/[^\"/]+/?)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            ToRelativeHref(m.Groups[1].Value?.Trim())
        );
    }

    static ModelItem ParseEbasosModel(string plugin, string html)
    {
        return ParseEbasosModels(plugin, html)?.FirstOrDefault();
    }

    static List<ModelItem> ParseEbasosModels(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNodes = cardDoc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' item ')][contains(., 'Модели')]//a[contains(@href, '/models/') and not(translate(normalize-space(.), 'МОДЕЛИ', 'модели')='модели')]"
        );

        if (modelNodes == null || modelNodes.Count == 0)
            return null;

        var models = new List<ModelItem>(modelNodes.Count);
        foreach (var modelNode in modelNodes)
        {
            string modelName = HtmlEntity.DeEntitize(modelNode.InnerText)?.Trim();
            modelName = Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim();

            AddUniqueModel(
                models,
                BuildModel(
                    plugin,
                    modelName,
                    ToRelativeHref(modelNode.GetAttributeValue("href", null)?.Trim())
                )
            );
        }

        return models.Count == 0 ? null : models;
    }

    static ModelItem ParseHuyambaModel(string plugin, string html)
    {
        return ParseHuyambaModels(plugin, html)?.FirstOrDefault();
    }

    static List<ModelItem> ParseHuyambaModels(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNodes = cardDoc.DocumentNode.SelectNodes(
            "//a[contains(concat(' ', normalize-space(@class), ' '), ' author ') and contains(@href, '/models/')]"
        );

        if (modelNodes == null || modelNodes.Count == 0)
            return null;

        var models = new List<ModelItem>(modelNodes.Count);
        foreach (var modelNode in modelNodes)
        {
            string modelName = HtmlEntity.DeEntitize(
                modelNode.SelectSingleNode(".//*[contains(concat(' ', normalize-space(@class), ' '), ' name ')]")?.InnerText
                ?? modelNode.InnerText
            )?.Trim();

            AddUniqueModel(
                models,
                BuildModel(
                    plugin,
                    Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim(),
                    ToRelativeHref(modelNode.GetAttributeValue("href", null)?.Trim())
                )
            );
        }

        return models.Count == 0 ? null : models;
    }

    static ModelItem ParseJopaonlineModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<a[^>]+href=\"((?:https?:)?//[^\"]+/models/[^\"]+|/models/[^\"]+|models/[^\"]+)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            ToRelativeHref(m.Groups[1].Value?.Trim())
        );
    }

    static ModelItem ParseLenpornoModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<div[^>]+class=\"video_categories\"[^>]*>\\s*<div[^>]+class=\"category_label\"[^>]*>\\s*Модели:\\s*</div>\\s*<a href=\"([^\"]+/model/[^\"]+/)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            m.Groups[1].Value
        );
    }

    static ModelItem ParsePornoneModel(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var block = Regex.Match(
            html,
            "<span[^>]*>\\s*Pornostars\\s*</span>\\s*<p[^>]*>(.*?)</p>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!block.Success)
        {
            block = Regex.Match(
                html,
                "<span[^>]*>\\s*Pornstars\\s*</span>\\s*<p[^>]*>(.*?)</p>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
        }

        if (block.Success)
        {
            var m = Regex.Match(
                block.Groups[1].Value,
                "<a[^>]+href=\"(https?://[^\"#]+|/[^\"#]+)\"[^>]*>\\s*<span[^>]*>([^<]+)</span>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );

            var model = BuildModel(
                plugin,
                HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim().TrimEnd(','),
                ToRelativeHref(m.Groups[1].Value?.Trim())
            );

            if (model != null)
                return model;
        }

        string title = HtmlEntity.DeEntitize(
            Regex.Match(
                html,
                "<title>(.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            ).Groups[1].Value
        );

        title = Regex.Replace(title ?? string.Empty, "<[^>]+>", " ");
        title = Regex.Replace(title, "\\s+PornOne\\s+auf\\s+Deutsch.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        string titleNorm = NormalizeText(title);

        if (string.IsNullOrEmpty(titleNorm))
            return null;

        var tagsBlock = Regex.Match(
            html,
            "<span[^>]*>\\s*Schlagw[oö]rter\\s*</span>\\s*<p[^>]*>(.*?)</p>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (!tagsBlock.Success)
        {
            tagsBlock = Regex.Match(
                html,
                "<span[^>]*>\\s*Tags\\s*</span>\\s*<p[^>]*>(.*?)</p>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            );
        }

        if (!tagsBlock.Success)
            return null;

        var tags = Regex.Matches(
            tagsBlock.Groups[1].Value,
            "<a[^>]+href=\"([^\"]+)\"[^>]*>\\s*<span[^>]*>([^<]+)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        foreach (Match tag in tags)
        {
            string tagHref = tag.Groups[1].Value?.Trim();
            string tagName = HtmlEntity.DeEntitize(tag.Groups[2].Value)?.Trim().TrimEnd(',');
            string tagNorm = NormalizeText(tagName);

            if (string.IsNullOrEmpty(tagHref) || string.IsNullOrEmpty(tagNorm))
                continue;

            if (!tagHref.Contains("/suche?q=") && !tagHref.Contains("/search?q="))
                continue;

            if (tagNorm.Length < 6)
                continue;

            if (!tagNorm.Contains(" ") && titleNorm != tagNorm)
                continue;

            if (tagNorm.Contains(" ") && titleNorm.IndexOf(tagNorm, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (Regex.IsMatch(tagNorm, "\\b(porn|sex|xxx|teen|milf|bbc|anal|cum|cock|video|videos|amateur|babe|braut|hochzeit|latina|korean|russisch|teenager|pornostar|gesicht|nackt|porno)\\b"))
                continue;

            return BuildModel(plugin, tagName, ToRelativeHref(tagHref));
        }

        return null;
    }

    static ModelItem ParsePornoboltModel(string plugin, string html)
    {
        return ParsePornoboltModels(plugin, html)?.FirstOrDefault();
    }

    static List<ModelItem> ParsePornoboltModels(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var models = new List<ModelItem>();

        void addNodes(HtmlNodeCollection nodes)
        {
            if (nodes == null || nodes.Count == 0)
                return;

            foreach (var modelNode in nodes)
            {
                string modelName = HtmlEntity.DeEntitize(
                    modelNode.GetAttributeValue("content", null)
                    ?? modelNode.InnerText
                )?.Trim();

                AddUniqueModel(
                    models,
                    BuildModel(
                        plugin,
                        Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim(),
                        ToRelativeHref(modelNode.GetAttributeValue("href", null)?.Trim())
                    )
                );
            }
        }

        var modelNodes = cardDoc.DocumentNode.SelectNodes(
            "//span[@itemprop='actor']//a[contains(@href, '/models/')]"
        );

        addNodes(modelNodes);

        if (models.Count == 0)
        {
            modelNodes = cardDoc.DocumentNode.SelectNodes(
                "//div[contains(concat(' ', normalize-space(@class), ' '), ' additional-info__list ')][.//*[contains(concat(' ', normalize-space(@class), ' '), ' fa-model ')]]//a[contains(@href, '/models/')]"
            );

            addNodes(modelNodes);
        }

        if (models.Count == 0)
        {
            var studioNodes = cardDoc.DocumentNode.SelectNodes(
                "//a[@itemprop='productionCompany' and contains(@href, '/kanal/')]"
            );

            addNodes(studioNodes);

            if (models.Count == 0)
            {
                studioNodes = cardDoc.DocumentNode.SelectNodes(
                    "//div[contains(concat(' ', normalize-space(@class), ' '), ' additional-info__list ')][.//*[contains(concat(' ', normalize-space(@class), ' '), ' fa-tv ')]]//a[contains(@href, '/kanal/')]"
                );

                addNodes(studioNodes);
            }
        }

        return models.Count == 0 ? null : models;
    }

    static ModelItem ParsePornobrizModel(string plugin, string html)
    {
        return ParsePornobrizModels(plugin, html)?.FirstOrDefault();
    }

    static List<ModelItem> ParsePornobrizModels(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNodes = cardDoc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' full-tags ')]//a[contains(@href, '/models/')]"
        );

        if (modelNodes == null || modelNodes.Count == 0)
            return null;

        var models = new List<ModelItem>(modelNodes.Count);

        foreach (var modelNode in modelNodes)
        {
            string modelName = HtmlEntity.DeEntitize(
                modelNode.ChildNodes.FirstOrDefault(i => i.NodeType == HtmlNodeType.Text && !string.IsNullOrWhiteSpace(i.InnerText))?.InnerText
                ?? modelNode.GetAttributeValue("title", null)
                ?? modelNode.InnerText
            )?.Trim();

            modelName = Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim();

            AddUniqueModel(
                models,
                BuildModel(
                    plugin,
                    modelName,
                    ToRelativeHref(modelNode.GetAttributeValue("href", null)?.Trim())
                )
            );
        }

        return models.Count == 0 ? null : models;
    }

    static ModelItem ParsePornokaefModel(string plugin, string html)
    {
        return ParsePornokaefModels(plugin, html)?.FirstOrDefault();
    }

    static List<ModelItem> ParsePornokaefModels(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNodes = cardDoc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' pornstars_list ')]//div[contains(concat(' ', normalize-space(@class), ' '), ' starring ')]//a[contains(@href, '/model/')]"
        );

        if (modelNodes == null || modelNodes.Count == 0)
            return null;

        var models = new List<ModelItem>(modelNodes.Count);
        foreach (var modelNode in modelNodes)
        {
            string modelName = HtmlEntity.DeEntitize(modelNode.InnerText)?.Trim();
            modelName = Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim();

            AddUniqueModel(
                models,
                BuildModel(
                    plugin,
                    modelName,
                    ToRelativeHref(modelNode.GetAttributeValue("href", null)?.Trim())
                )
            );
        }

        return models.Count == 0 ? null : models;
    }

    static ModelItem ParsePorndigModel(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var obfs = Regex.Match(
            html,
            "<span[^>]+data-pornstar_id=\"(\\d+)\"[^>]+data-obfs=\"([^\"]+)\"[^>]*>([^<]+)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        if (obfs.Success)
        {
            string modelId = obfs.Groups[1].Value?.Trim();
            string modelName = HtmlEntity.DeEntitize(obfs.Groups[3].Value)?.Trim();
            string decoded = HttpUtility.UrlDecode(CrypTo.DecodeBase64(obfs.Groups[2].Value)) ?? string.Empty;
            string modelSlug = Regex.Match(decoded, "/video_pornstar/p\\d+/([^/]+)", RegexOptions.IgnoreCase).Groups[1].Value;

            if (string.IsNullOrEmpty(modelSlug))
                modelSlug = ToAsciiSlug(modelName);

            return BuildModel(plugin, modelName, $"pornstars/{modelId}/{modelSlug}.html");
        }

        var m = Regex.Match(
            html,
            "<a[^>]+data-pornstar_id=\"\\d+\"[^>]+href=\"([^\"]*/pornstars/[^\"]+)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            ToRelativeHref(m.Groups[1].Value?.Trim())
        );
    }

    static ModelItem ParsePornoaktModel(string plugin, string html)
    {
        return ParsePornoaktModels(plugin, html)?.FirstOrDefault();
    }

    static List<ModelItem> ParsePornoaktModels(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var actorNodes = cardDoc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' col2-item ')][.//b[contains(., 'Актер')]]//a[contains(@href, '/xfsearch/actors/')]"
        );

        if (actorNodes == null || actorNodes.Count == 0)
            return null;

        var models = new List<ModelItem>(actorNodes.Count);
        foreach (var actorNode in actorNodes)
        {
            string modelName = HtmlEntity.DeEntitize(actorNode.InnerText)?.Trim();

            AddUniqueModel(
                models,
                BuildModel(
                    plugin,
                    Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim(),
                    ToRelativeHref(actorNode.GetAttributeValue("href", null)?.Trim())
                )
            );
        }

        return models.Count == 0 ? null : models;
    }

    static ModelItem ParsePorn4daysModel(string plugin, string html)
    {
        return ParsePorn4daysModels(plugin, html)?.FirstOrDefault();
    }

    static List<ModelItem> ParsePorn4daysModels(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNodes = cardDoc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' fw-bold ') and normalize-space(.)='Pornstars']/following-sibling::div[1]//a[contains(@href, 'search/?s=')]"
        );

        if (modelNodes == null || modelNodes.Count == 0)
            return null;

        var models = new List<ModelItem>(modelNodes.Count);
        foreach (var modelNode in modelNodes)
        {
            string modelName = HtmlEntity.DeEntitize(modelNode.InnerText)?.Trim();

            AddUniqueModel(
                models,
                BuildModel(
                    plugin,
                    Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim(),
                    string.IsNullOrWhiteSpace(modelName) ? null : $"search/?s={HttpUtility.UrlEncode(modelName)}"
                )
            );
        }

        return models.Count == 0 ? null : models;
    }

    static ModelItem ParsePorno666Model(string plugin, string html)
    {
        string modelName = Regex.Match(
            html ?? string.Empty,
            "video_models:\\s*'([^']+)'",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        ).Groups[1].Value;

        if (string.IsNullOrWhiteSpace(modelName))
        {
            modelName = Regex.Match(
                html ?? string.Empty,
                "Модели:\\s*<a[^>]*>([^<]+)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline
            ).Groups[1].Value;
        }

        modelName = HtmlEntity.DeEntitize(modelName)?.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        string modelSlug = ToAsciiSlug(modelName);

        return BuildModel(plugin, modelName, string.IsNullOrEmpty(modelSlug) ? null : $"models/{modelSlug}");
    }

    static ModelItem ParseProstopornoModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<li[^>]*><a[^>]+href=\"((?:https?:)?//[^\"/]+/models?/[^\"/]+/?|/models?/[^\"/]+/?|models?/[^\"/]+/?)\"[^>]*>([^<]+)</a></li>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            ToRelativeHref(m.Groups[1].Value?.Trim())
        );
    }

    static ModelItem ParseRusvideosModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<div[^>]+class=\"v-tags\"[^>]*>\\s*<i[^>]+class=\"fa fa-star\"[^>]*></i>\\s*<a[^>]+href=\"/russkie-porno-modeli\"[^>]*>\\s*Порноактрисы:\\s*</a>.*?<a[^>]+href=\"([^\"]*/aktrisa/[^\"]+)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            ToRelativeHref(m.Groups[1].Value?.Trim())
        );
    }

    static ModelItem ParseSosushkaModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "<a[^>]+href=\"((?:https?:)?//[^\"]+/model/[^\"/]+/?|/model/[^\"/]+/?|model/[^\"/]+/?)\"[^>]*>\\s*(?:<i[^>]*></i>\\s*)?([^<]+?)\\s*</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            ToRelativeHref(m.Groups[1].Value?.Trim())
        );
    }

    static ModelItem ParseTrahkinoModel(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNode = cardDoc.DocumentNode.SelectSingleNode(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' info-content ')]//a[contains(@href, '/models/')]"
        );

        string modelHref = ToRelativeHref(modelNode?.GetAttributeValue("href", null)?.Trim());
        string modelName = HtmlEntity.DeEntitize(
            modelNode?.SelectSingleNode(".//*[contains(concat(' ', normalize-space(@class), ' '), ' name ')]")?.InnerText
            ?? modelNode?.InnerText
        )?.Trim();

        modelName = Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim();
        modelName = Regex.Replace(modelName, "\\s+\\d+$", string.Empty).Trim();

        return BuildModel(plugin, modelName, modelHref);
    }

    static ModelItem ParseVepornModel(string plugin, string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var cardDoc = new HtmlDocument();
        cardDoc.LoadHtml(html);

        var modelNode = cardDoc.DocumentNode.SelectSingleNode(
            "//a[contains(concat(' ', normalize-space(@class), ' '), ' tag-prst ') and contains(@href, '/pornstar/')]"
        );

        string modelHref = ToRelativeHref(modelNode?.GetAttributeValue("href", null)?.Trim());
        string modelName = HtmlEntity.DeEntitize(modelNode?.InnerText)?.Trim();

        return BuildModel(plugin, modelName, modelHref);
    }

    static ModelItem ParseVtrahetvModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "href=\"(https?://[^\" ]*/pornstar/[^\"/]+/)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        return BuildModel(
            plugin,
            HtmlEntity.DeEntitize(m.Groups[2].Value)?.Trim(),
            m.Groups[1].Value
        );
    }

    static ModelItem ParseXxxperevodModel(string plugin, string html)
    {
        var m = Regex.Match(
            html ?? string.Empty,
            "Модели:\\s*<div[^>]*class=\"[^\"]*table-model-right[^\"]*\"[^>]*>.*?<a[^>]+href=\"([^\"]*/models/[^\"/]+/)\"[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        string modelName = HtmlEntity.DeEntitize(
            Regex.Replace(m.Groups[2].Value ?? string.Empty, "<[^>]+>", " ")
        );

        modelName = Regex.Replace(modelName ?? string.Empty, "\\bнет\\s+аватара\\b", string.Empty, RegexOptions.IgnoreCase);
        modelName = Regex.Replace(modelName ?? string.Empty, "\\s+", " ").Trim();

        return BuildModel(
            plugin,
            modelName,
            ToRelativeHref(m.Groups[1].Value?.Trim())
        );
    }


}
