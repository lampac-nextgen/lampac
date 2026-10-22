using System.Reflection;
using System.Text;

namespace Core;

/// <summary>
/// Version string from GitInfo (<see cref="ThisAssembly.Git"/>), with assembly
/// InformationalVersion as the fallback when the build has no git metadata.
/// </summary>
static class VersionInfo
{
    public static bool HasGit { get; } = IsRealSha(ThisAssembly.Git.Sha);

    public static string GitSha { get; } = HasGit ? ShortSha(ThisAssembly.Git.Sha) : FallbackCommit();

    public static bool IsDirty { get; } = HasGit && ThisAssembly.Git.IsDirty;

    public static string Version { get; } = ComputeVersion();

    public static string SemVer { get; } = HasGit
        ? $"{ThisAssembly.Git.SemVer.Major}.{ThisAssembly.Git.SemVer.Minor}.{ThisAssembly.Git.SemVer.Patch}{ThisAssembly.Git.SemVer.DashLabel}"
        : null;

    public static string BaseTag { get; } = HasGit ? NullIfEmpty(ThisAssembly.Git.BaseTag) : null;

    public static string Commits { get; } = HasGit ? NullIfEmpty(ThisAssembly.Git.Commits) : null;

    public static string GitBranch { get; } = HasGit ? NullIfEmpty(ThisAssembly.Git.Branch) : null;

    public static string CommitDate { get; } = HasGit ? NullIfEmpty(ThisAssembly.Git.CommitDate) : null;

    static string ComputeVersion()
    {
        if (!HasGit)
        {
            var stamped = ReadInformational().Split('+', 2)[0];
            return string.IsNullOrEmpty(stamped) ? "unknown" : stamped;
        }

        var sha = GitSha;
        var tag = NullIfEmpty(ThisAssembly.Git.Tag);
        if (tag != null)
            return StripV(tag);

        var baseTag = NullIfEmpty(ThisAssembly.Git.BaseTag);
        if (baseTag != null)
            return $"{StripV(baseTag)}-next+{sha}";

        var branch = NullIfEmpty(ThisAssembly.Git.Branch);
        if (branch != null && branch != "HEAD")
            return $"dev-{SanitizeBranch(branch)}+{sha}";

        return $"dev+{sha}";
    }

    static string FallbackCommit()
    {
        var parts = ReadInformational().Split('+', 2);
        return parts.Length > 1 ? NullIfEmpty(parts[1]) : null;
    }

    static string ReadInformational() =>
        typeof(VersionInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    static bool IsRealSha(string sha) =>
        !string.IsNullOrEmpty(sha) && sha != "0000000" && sha != "unknown";

    static string ShortSha(string sha) =>
        sha.Length <= 8 ? sha : sha[..8];

    static string StripV(string tag) =>
        tag.Length > 0 && (tag[0] == 'v' || tag[0] == 'V') ? tag[1..] : tag;

    static string SanitizeBranch(string branch)
    {
        var sb = new StringBuilder(branch.Length);
        foreach (var c in branch)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-');
        return sb.ToString();
    }

    static string NullIfEmpty(string value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
