using System.Text.Json.Serialization;

namespace LampacMcp;

public sealed record StatusResult(
    [property: JsonPropertyName("online")] bool Online,
    [property: JsonPropertyName("profiles")] int? Profiles,
    [property: JsonPropertyName("custom_plugins")] int CustomPlugins,
    [property: JsonPropertyName("module_count")] int? ModuleCount,
    [property: JsonPropertyName("configuration_sections")] int? ConfigurationSections);

public sealed record PluginInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("host")] string Host);

public sealed record PluginsResult(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("plugins")] IReadOnlyList<PluginInfo> Plugins);

public sealed record PluginAddResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("restart_may_be_required")] bool RestartMayBeRequired);

public sealed record OkResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("restart_may_be_required")] bool RestartMayBeRequired);

public sealed record ModuleInfo(
    [property: JsonPropertyName("module")] string Module,
    [property: JsonPropertyName("enabled_in_manifest")] bool EnabledInManifest);

public sealed record ModulesResult(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("modules")] IReadOnlyList<ModuleInfo> Modules);

public sealed record ModuleWriteResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("backup")] string Backup,
    [property: JsonPropertyName("restart_required_for_cs")] bool RestartRequiredForCs);

public sealed record ProfilesResult([property: JsonPropertyName("count")] int Count);

public sealed record RestartResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("message")] string Message);
