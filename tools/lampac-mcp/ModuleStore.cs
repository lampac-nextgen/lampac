using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace LampacMcp;

/// <summary>
/// Read and write access to the module directory. Every path is resolved through
/// symlinks and must stay inside LAMPAC_ROOT/module/&lt;module&gt;.
/// </summary>
public sealed partial class ModuleStore(LampacConfig config)
{
    public const int MaxFileBytes = 512 * 1024;

    private static readonly HashSet<string> EditableExtensions =
        new(StringComparer.Ordinal) { ".cs", ".csproj", ".js", ".json", ".xml", ".md" };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(config.ModuleRoot);

    public IReadOnlyList<ModuleInfo> List()
    {
        var root = ModuleDirectory();
        var result = new List<ModuleInfo>();
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var info = new DirectoryInfo(directory);
            if (info.LinkTarget is not null || !ModuleNamePattern().IsMatch(info.Name))
            {
                continue;
            }

            try
            {
                var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json"))) as JsonObject;
                var enabled = manifest?["enable"] is JsonValue value &&
                              value.TryGetValue<bool>(out var flag) && flag;
                result.Add(new ModuleInfo(info.Name, enabled));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    public string Read(string module, string file)
    {
        var path = ResolveModuleFile(module, file);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxFileBytes)
        {
            throw new McpException("module file is missing or larger than 512 KiB");
        }

        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new McpException("module file is not valid UTF-8 text");
        }
    }

    public ModuleWriteResult Write(string module, string file, string content)
    {
        if (!config.AllowWrite)
        {
            throw new McpException("write operations are disabled; set LAMPAC_MCP_ALLOW_WRITE=1 explicitly");
        }

        var path = ResolveModuleFile(module, file);
        var info = new FileInfo(path);
        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (!info.Exists || info.Length > MaxFileBytes || contentBytes > MaxFileBytes)
        {
            throw new McpException("only existing text files up to 512 KiB can be replaced");
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".json":
                try
                {
                    JsonNode.Parse(content);
                }
                catch (JsonException)
                {
                    throw new McpException("JSON validation failed: the new content is not valid JSON");
                }

                break;
            case ".js":
                ValidateJavaScript(content);
                break;
        }

        var backup = path + ".bak-mcp";
        File.Copy(path, backup, overwrite: true);
        WriteAtomically(path, content);
        return new ModuleWriteResult(true, Path.GetFileName(backup), extension == ".cs");
    }

    public string ModuleDirectory()
    {
        if (!IsConfigured)
        {
            throw new McpException("LAMPAC_ROOT must point to the Lampac runtime directory");
        }

        string root;
        try
        {
            root = RealPath(Path.GetFullPath(config.ModuleRoot!));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new McpException($"resolve LAMPAC_ROOT: {exception.Message}");
        }

        var moduleDirectory = Path.Combine(root, "module");
        if (!Directory.Exists(moduleDirectory))
        {
            throw new McpException("lampac module/ directory does not exist");
        }

        return RealPath(moduleDirectory);
    }

    public string ResolveModuleFile(string module, string file)
    {
        if (!ModuleNamePattern().IsMatch(module) || module is "." or ".." || module.StartsWith('.'))
        {
            throw new McpException("invalid module or file name");
        }

        if (!ModuleFilePattern().IsMatch(file) || Path.IsPathRooted(file) || file.Contains('\\'))
        {
            throw new McpException("invalid module or file name");
        }

        foreach (var part in file.Split('/'))
        {
            if (part is "." or ".." || part.StartsWith('.'))
            {
                throw new McpException("hidden files and path traversal are forbidden");
            }
        }

        var root = ModuleDirectory();
        var moduleDirectory = Path.Combine(root, module);
        if (!Directory.Exists(moduleDirectory))
        {
            throw new McpException($"module \"{module}\" does not exist");
        }

        var realModule = RealPath(moduleDirectory);
        if (!IsInside(root, realModule))
        {
            throw new McpException("path must be an allowed text file inside the named module");
        }

        var target = Path.Combine(realModule, file.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target))
        {
            throw new McpException($"module file \"{file}\" does not exist");
        }

        var realTarget = RealPath(target);
        if (!IsInside(realModule, realTarget) ||
            !EditableExtensions.Contains(Path.GetExtension(realTarget).ToLowerInvariant()))
        {
            throw new McpException("path must be an allowed text file inside the named module");
        }

        return realTarget;
    }

    /// <summary>Resolves symlinks component by component, like realpath(3).</summary>
    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)
            ?? throw new McpException("path has no root");
        var current = root;
        foreach (var part in full[root.Length..]
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            string? target = null;
            if (Directory.Exists(current))
            {
                target = Directory.ResolveLinkTarget(current, returnFinalTarget: true)?.FullName;
            }
            else if (File.Exists(current))
            {
                target = File.ResolveLinkTarget(current, returnFinalTarget: true)?.FullName;
            }

            if (target is not null)
            {
                current = target;
            }
        }

        return current;
    }

    public static bool IsInside(string root, string target)
    {
        var relative = Path.GetRelativePath(root, target);
        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static void ValidateJavaScript(string content)
    {
        var result = ProcessRunner.Run("node", ["--check", "-"], content, TimeSpan.FromSeconds(10));
        if (result.ExitCode != 0)
        {
            throw new McpException($"JavaScript syntax validation failed: {ProcessRunner.Snippet(result.StandardError)}");
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new McpException("module file has no directory");
        var temporary = Path.Combine(directory, $".mcp-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleNamePattern();

    [GeneratedRegex("^[A-Za-z0-9_./-]{1,300}$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleFilePattern();
}
