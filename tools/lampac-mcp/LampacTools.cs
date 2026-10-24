using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LampacMcp;

/// <summary>MCP tool surface exposed to the MCP client.</summary>
[McpServerToolType]
public sealed partial class LampacTools
{
    private const int DefaultLogLines = 50;
    private const int MinLogLines = 5;
    private const int MaxLogLines = 100;
    private const int MaxLogChars = 16000;
    private const int MaxRestartMessageChars = 1000;

    private readonly LampacConfig config;
    private readonly PanelClient panel;
    private readonly ModuleStore modules;

    public LampacTools(LampacConfig config, PanelClient panel, ModuleStore modules)
    {
        this.config = config;
        this.panel = panel;
        this.modules = modules;
    }

    [McpServerTool(Name = "lampac_status", UseStructuredContent = true)]
    [Description("Test AdminPanel access and return a non-secret service summary.")]
    public Task<StatusResult> Status(CancellationToken cancellationToken) =>
        panel.GetStatusAsync(modules, cancellationToken);

    [McpServerTool(Name = "lampac_plugins", UseStructuredContent = true)]
    [Description("List configured custom Lampa plugins without leaking tokenized URLs.")]
    public async Task<PluginsResult> Plugins(CancellationToken cancellationToken)
    {
        var plugins = await panel.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
        return new PluginsResult(plugins.Count, plugins);
    }

    [McpServerTool(Name = "lampac_plugin_add", UseStructuredContent = true)]
    [Description("Add a custom Lampa plugin (requires LAMPAC_MCP_ALLOW_WRITE=1).")]
    public Task<PluginAddResult> PluginAdd(
        [Description("Custom plugin display name (1-120 characters)")] string name,
        [Description("HTTP(S) URL, a root-relative path, or a {localhost}/ path")] string url,
        [Description("Plugin author (default admin)")] string author = "admin",
        CancellationToken cancellationToken = default) =>
        panel.AddPluginAsync(name, url, author, cancellationToken);

    [McpServerTool(Name = "lampac_plugin_set_enabled", UseStructuredContent = true)]
    [Description("Enable or disable a custom plugin by the opaque ID returned by lampac_plugins.")]
    public Task<OkResult> PluginSetEnabled(
        [Description("Opaque plugin ID returned by lampac_plugins")] string plugin_id,
        [Description("true to enable the plugin, false to disable it")] bool enabled,
        CancellationToken cancellationToken = default) =>
        panel.SetPluginEnabledAsync(plugin_id, enabled, cancellationToken);

    [McpServerTool(Name = "lampac_plugin_delete", UseStructuredContent = true)]
    [Description("Remove a custom plugin by opaque ID (write access required).")]
    public Task<OkResult> PluginDelete(
        [Description("Opaque plugin ID returned by lampac_plugins")] string plugin_id,
        CancellationToken cancellationToken = default) =>
        panel.DeletePluginAsync(plugin_id, cancellationToken);

    [McpServerTool(Name = "lampac_modules", UseStructuredContent = true)]
    [Description("List installed module manifests (set LAMPAC_ROOT).")]
    public ModulesResult Modules()
    {
        var installed = modules.List();
        return new ModulesResult(installed.Count, installed);
    }

    [McpServerTool(Name = "lampac_module_read")]
    [Description("Read a permitted module text file; its content may be sensitive (administrator only).")]
    public string ModuleRead(
        [Description("Module directory name")] string module,
        [Description("Module-relative text file path")] string file) =>
        modules.Read(module, file);

    [McpServerTool(Name = "lampac_module_write", UseStructuredContent = true)]
    [Description("Replace one module text file, retaining a .bak-mcp backup; restart separately.")]
    public ModuleWriteResult ModuleWrite(
        [Description("Module directory name")] string module,
        [Description("Module-relative text file path")] string file,
        [Description("New file content")] string content) =>
        modules.Write(module, file, content);

    [McpServerTool(Name = "lampac_profiles_count", UseStructuredContent = true)]
    [Description("Return only the number of accsdb profiles, never their names or credentials.")]
    public async Task<ProfilesResult> Profiles(CancellationToken cancellationToken) =>
        new(await panel.GetProfilesCountAsync(cancellationToken).ConfigureAwait(false) ?? 0);

    [McpServerTool(Name = "lampac_logs")]
    [Description("Read limited journal lines (explicitly enable LAMPAC_MCP_ALLOW_LOGS=1).")]
    public string Logs([Description("Journal lines to read (5-100, default 50)")] int lines = DefaultLogLines)
    {
        if (!config.AllowLogs)
        {
            throw new McpException(
                "logs are disabled because they can contain private information; set LAMPAC_MCP_ALLOW_LOGS=1 explicitly");
        }

        lines = lines switch
        {
            <= 0 => DefaultLogLines,
            < MinLogLines => MinLogLines,
            > MaxLogLines => MaxLogLines,
            _ => lines,
        };

        ProcessResult result;
        try
        {
            result = ProcessRunner.Run(
                "journalctl",
                ["-u", "lampac", "-n", lines.ToString(CultureInfo.InvariantCulture), "--no-pager", "--output=cat"],
                standardInput: null,
                TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException exception)
        {
            throw new McpException(exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            throw new McpException($"journalctl could not be started: {exception.Message}");
        }

        if (result.ExitCode != 0)
        {
            throw new McpException(
                $"journalctl returned an error; check journal permissions: {ProcessRunner.Snippet(result.StandardError)}");
        }

        return TailText(LogRedaction().Replace(result.StandardOutput, "$1=[REDACTED]"), MaxLogChars);
    }

    [McpServerTool(Name = "lampac_restart_safe", UseStructuredContent = true)]
    [Description("Run the optional safe restart script; never force active viewers offline.")]
    public RestartResult RestartSafe()
    {
        if (!config.AllowRestart)
        {
            throw new McpException("restart is disabled; set LAMPAC_MCP_ALLOW_RESTART=1 explicitly");
        }

        var script = Path.GetFullPath(config.RestartScript);
        if (!File.Exists(script))
        {
            throw new McpException($"safe restart script is not installed: {script}");
        }

        ProcessResult result;
        try
        {
            result = ProcessRunner.Run(script, [], standardInput: null, TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException exception)
        {
            throw new McpException(exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            throw new McpException($"run restart script: {exception.Message}");
        }

        var message = TailText(result.StandardOutput + result.StandardError, MaxRestartMessageChars);
        return new RestartResult(result.ExitCode == 0, result.ExitCode, message);
    }

    private static string TailText(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var tail = text[^maxChars..];
        if (char.IsLowSurrogate(tail[0]))
        {
            tail = tail[1..];
        }

        return tail;
    }

    [GeneratedRegex(
        "(?i)(password|passwd|token|secret|apikey|cookie)[=:]\\s*[^\\s&]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex LogRedaction();
}
