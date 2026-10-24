using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;

namespace LampacMcp;

/// <summary>
/// Thin AdminPanel client plus the plugin operations on LampaWeb.customPlugins.
/// Requests never follow redirects and never forward the root password cookie to
/// another origin.
/// </summary>
public sealed class PanelClient
{
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private const int MaxPluginTextRunes = 120;
    private const int MaxPluginUrlLength = 500;
    private const string WriteDisabledMessage =
        "write operations are disabled; set LAMPAC_MCP_ALLOW_WRITE=1 explicitly";

    private readonly HttpClient http;
    private readonly LampacConfig config;
    private readonly SemaphoreSlim writes = new(1, 1);

    public PanelClient(LampacConfig config)
    {
        this.config = config;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Proxy = null,
        };
        http = new HttpClient(handler)
        {
            BaseAddress = config.BaseUri,
            Timeout = TimeSpan.FromSeconds(12),
        };
    }

    public async Task<StatusResult> GetStatusAsync(ModuleStore modules, CancellationToken cancellationToken)
    {
        int? sections = null;
        if (await GetAsync("/adminpanel/api/current", cancellationToken).ConfigureAwait(false) is JsonObject current)
        {
            sections = current.Count;
        }

        int? profiles = null;
        if (await GetAsync("/adminpanel/api/users-json", cancellationToken).ConfigureAwait(false) is JsonArray users)
        {
            profiles = users.Count;
        }

        var plugins = await ListPluginsAsync(cancellationToken).ConfigureAwait(false);

        int? moduleCount = null;
        if (modules.IsConfigured)
        {
            // A wrong LAMPAC_ROOT must not hide the AdminPanel-only diagnostics.
            try
            {
                moduleCount = modules.List().Count;
            }
            catch (McpException)
            {
                moduleCount = null;
            }
        }

        return new StatusResult(true, profiles, plugins.Count, moduleCount, sections);
    }

    public async Task<int?> GetProfilesCountAsync(CancellationToken cancellationToken) =>
        await GetAsync("/adminpanel/api/users-json", cancellationToken).ConfigureAwait(false) is JsonArray users
            ? users.Count
            : null;

    /// <summary>
    /// Reads the operator override first so changes appear immediately, even before
    /// Lampac reloads the merged current.conf.
    /// </summary>
    public async Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(CancellationToken cancellationToken)
    {
        JsonObject? section = null;
        if (await GetAsync("/adminpanel/api/init", cancellationToken).ConfigureAwait(false) is JsonObject init)
        {
            section = init["LampaWeb"] as JsonObject;
        }

        if (section is null || !section.ContainsKey("customPlugins"))
        {
            if (await GetAsync("/adminpanel/api/current", cancellationToken).ConfigureAwait(false) is JsonObject current)
            {
                section = current["LampaWeb"] as JsonObject;
            }
        }

        var result = new List<PluginInfo>();
        if (section?["customPlugins"] is JsonArray plugins)
        {
            foreach (var item in plugins)
            {
                if (item is JsonObject plugin)
                {
                    result.Add(ToPluginInfo(plugin));
                }
            }
        }

        return result;
    }

    public async Task<PluginAddResult> AddPluginAsync(
        string name,
        string url,
        string author,
        CancellationToken cancellationToken)
    {
        RequireWrite();
        ValidatePlugin(name, url, author);

        await writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var section = await ReadLampaWebAsync(cancellationToken).ConfigureAwait(false);
            var plugins = GetPluginArray(section);
            foreach (var item in plugins)
            {
                if (item is JsonObject plugin && Text(plugin["url"]) == url)
                {
                    throw new McpException("plugin URL is already configured");
                }
            }

            plugins.Add(new JsonObject
            {
                ["url"] = url,
                ["status"] = 1,
                ["name"] = name,
                ["author"] = author,
            });
            await WriteLampaWebAsync(section, cancellationToken).ConfigureAwait(false);
            return new PluginAddResult(true, PluginId(url), true);
        }
        finally
        {
            writes.Release();
        }
    }

    public async Task<OkResult> SetPluginEnabledAsync(
        string pluginId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        RequireWrite();

        await writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var section = await ReadLampaWebAsync(cancellationToken).ConfigureAwait(false);
            var plugins = GetPluginArray(section);
            var matches = 0;
            foreach (var item in plugins)
            {
                if (item is JsonObject plugin && PluginId(Text(plugin["url"])) == pluginId)
                {
                    matches++;
                    plugin["status"] = enabled ? 1 : 0;
                }
            }

            if (matches != 1)
            {
                throw new McpException("expected exactly one custom plugin matching this ID in init.conf");
            }

            await WriteLampaWebAsync(section, cancellationToken).ConfigureAwait(false);
            return new OkResult(true, true);
        }
        finally
        {
            writes.Release();
        }
    }

    public async Task<OkResult> DeletePluginAsync(string pluginId, CancellationToken cancellationToken)
    {
        RequireWrite();

        await writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var section = await ReadLampaWebAsync(cancellationToken).ConfigureAwait(false);
            var plugins = GetPluginArray(section);
            var index = -1;
            for (var position = 0; position < plugins.Count; position++)
            {
                if (plugins[position] is JsonObject plugin && PluginId(Text(plugin["url"])) == pluginId)
                {
                    if (index >= 0)
                    {
                        throw new McpException("expected exactly one custom plugin matching this ID in init.conf");
                    }

                    index = position;
                }
            }

            if (index < 0)
            {
                throw new McpException("expected exactly one custom plugin matching this ID in init.conf");
            }

            plugins.RemoveAt(index);
            await WriteLampaWebAsync(section, cancellationToken).ConfigureAwait(false);
            return new OkResult(true, true);
        }
        finally
        {
            writes.Release();
        }
    }

    public static string PluginId(string url)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(digest)[..16].ToLowerInvariant();
    }

    private async Task<JsonObject> ReadLampaWebAsync(CancellationToken cancellationToken)
    {
        if (await GetAsync("/adminpanel/api/init", cancellationToken).ConfigureAwait(false) is not JsonObject init)
        {
            throw new McpException("AdminPanel init.conf is not a JSON object");
        }

        if (init["LampaWeb"] is not JsonObject section)
        {
            throw new McpException("set LampaWeb as an object in init.conf before editing plugins via MCP");
        }

        if (section.TryGetPropertyValue("customPlugins", out var rawPlugins) && rawPlugins is not JsonArray)
        {
            throw new McpException("LampaWeb.customPlugins must be a JSON array");
        }

        return section;
    }

    private Task WriteLampaWebAsync(JsonObject section, CancellationToken cancellationToken) =>
        PostAsync("/adminpanel/api/init/section/LampaWeb", section, cancellationToken);

    private static JsonArray GetPluginArray(JsonObject section)
    {
        if (section.TryGetPropertyValue("customPlugins", out var existing))
        {
            return existing as JsonArray
                ?? throw new McpException("LampaWeb.customPlugins must be a JSON array");
        }

        var plugins = new JsonArray();
        section["customPlugins"] = plugins;
        return plugins;
    }

    private void RequireWrite()
    {
        if (!config.AllowWrite)
        {
            throw new McpException(WriteDisabledMessage);
        }
    }

    private static void ValidatePlugin(string name, string url, string author)
    {
        if (name.Length == 0 || RuneCount(name) > MaxPluginTextRunes ||
            RuneCount(author) > MaxPluginTextRunes || url.Length > MaxPluginUrlLength)
        {
            throw new McpException("plugin name, author, or URL length is invalid");
        }

        if (url.Contains('\r') || url.Contains('\n'))
        {
            throw new McpException("plugin URL must not contain line breaks");
        }

        if (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
            {
                throw new McpException("plugin URL must have a hostname and no embedded credentials");
            }

            return;
        }

        var localPath = url.StartsWith("{localhost}/", StringComparison.Ordinal) ||
                        (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal));
        if (!localPath)
        {
            throw new McpException("plugin URLs must use HTTP(S), a root-relative path, or {localhost}/");
        }
    }

    private static PluginInfo ToPluginInfo(JsonObject plugin)
    {
        var url = Text(plugin["url"]);
        var host = "local";
        if (url.StartsWith("http://", StringComparison.Ordinal) ||
            url.StartsWith("https://", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host))
            {
                host = parsed.Host;
            }
        }

        return new PluginInfo(
            PluginId(url),
            Truncate(Text(plugin["name"]), MaxPluginTextRunes),
            Truncate(Text(plugin["author"]), MaxPluginTextRunes),
            StatusEnabled(plugin["status"]),
            host);
    }

    private static bool StatusEnabled(JsonNode? status)
    {
        if (status is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer == 1;
        }

        if (value.TryGetValue<long>(out var longInteger))
        {
            return longInteger == 1;
        }

        return value.TryGetValue<double>(out var real) && real == 1;
    }

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static int RuneCount(string value) => value.EnumerateRunes().Count();

    private static string Truncate(string value, int maxRunes)
    {
        if (value.Length <= maxRunes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ == maxRunes)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private async Task<JsonNode?> GetAsync(string path, CancellationToken cancellationToken) =>
        await SendAsync(HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);

    private async Task<JsonNode?> PostAsync(string path, JsonNode payload, CancellationToken cancellationToken) =>
        await SendAsync(HttpMethod.Post, path, payload, cancellationToken).ConfigureAwait(false);

    private async Task<JsonNode?> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Cookie", "accspasswd=" + config.Password);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (payload is not null)
        {
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var status = (int)response.StatusCode;
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // 401/403 is the AdminPanel login gate; 3xx is the HTML login form.
        if (status is 401 or 403 || status is >= 300 and < 400)
        {
            throw new McpException(
                "AdminPanel authorization failed; verify the root password and panel activation");
        }

        if (status is < 200 or >= 300)
        {
            throw new McpException($"AdminPanel returned HTTP {status}: {Snippet(body)}");
        }

        if (body.Length > MaxResponseBytes)
        {
            throw new McpException($"AdminPanel response is larger than {MaxResponseBytes} bytes");
        }

        return body.Length == 0 ? null : JsonNode.Parse(body);
    }

    private static string Snippet(byte[] body)
    {
        var text = string.Join(' ', Encoding.UTF8.GetString(body)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 200 ? text : text[..200] + "…";
    }
}
