namespace LampacMcp;

/// <summary>
/// Environment-driven settings. Credentials are read once, never logged and never
/// returned to an MCP client.
/// </summary>
public sealed class LampacConfig
{
    public const int DefaultPort = 8765;
    public const string DefaultRestartScript = "/usr/local/bin/lampac-restart-safe.sh";

    public required Uri BaseUri { get; init; }

    public required string Password { get; init; }

    public string? ModuleRoot { get; init; }

    public required string Transport { get; init; }

    public int Port { get; init; } = DefaultPort;

    public bool AllowWrite { get; init; }

    public bool AllowLogs { get; init; }

    public bool AllowRestart { get; init; }

    public required string RestartScript { get; init; }

    public static LampacConfig FromEnvironment()
    {
        var baseUri = ParseBaseUri(Environment.GetEnvironmentVariable("LAMPAC_URL") ?? "http://127.0.0.1:9118");
        var transport = (Environment.GetEnvironmentVariable("LAMPAC_MCP_TRANSPORT") ?? "stdio").Trim();
        if (transport is not ("stdio" or "streamable-http"))
        {
            throw new InvalidOperationException(
                $"unsupported MCP transport \"{transport}\" (want stdio or streamable-http)");
        }

        var port = DefaultPort;
        var rawPort = Environment.GetEnvironmentVariable("LAMPAC_MCP_PORT")?.Trim();
        if (!string.IsNullOrEmpty(rawPort) &&
            (!int.TryParse(rawPort, out port) || port is < 1 or > 65535))
        {
            throw new InvalidOperationException("LAMPAC_MCP_PORT must be a TCP port between 1 and 65535");
        }

        var moduleRoot = Environment.GetEnvironmentVariable("LAMPAC_ROOT")?.Trim();
        return new LampacConfig
        {
            BaseUri = baseUri,
            Password = LoadPassword(),
            Transport = transport,
            Port = port,
            ModuleRoot = string.IsNullOrWhiteSpace(moduleRoot) ? null : moduleRoot,
            AllowWrite = IsEnabled("LAMPAC_MCP_ALLOW_WRITE"),
            AllowLogs = IsEnabled("LAMPAC_MCP_ALLOW_LOGS"),
            AllowRestart = IsEnabled("LAMPAC_MCP_ALLOW_RESTART"),
            RestartScript = NonEmpty(Environment.GetEnvironmentVariable("LAMPAC_MCP_RESTART_SCRIPT"), DefaultRestartScript),
        };
    }

    /// <summary>Accepts a bare server origin only: no userinfo, path, query or fragment.</summary>
    public static Uri ParseBaseUri(string raw)
    {
        var candidate = raw.Trim().TrimEnd('/');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("LAMPAC_URL must be an http or https server origin");
        }

        if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath is not ("" or "/") || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException("LAMPAC_URL must be a server origin without path or credentials");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new InvalidOperationException("plain HTTP is allowed only for loopback hosts");
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    private static string LoadPassword()
    {
        var path = Environment.GetEnvironmentVariable("LAMPAC_PASSWORD_FILE")?.Trim();
        string? password;
        if (!string.IsNullOrEmpty(path))
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("LAMPAC_PASSWORD_FILE does not exist");
            }

            password = File.ReadAllText(path).Trim();
        }
        else
        {
            password = Environment.GetEnvironmentVariable("LAMPAC_ADMIN_PASSWORD")?.Trim();
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("configure LAMPAC_PASSWORD_FILE or LAMPAC_ADMIN_PASSWORD");
        }

        if (password.Contains('\r') || password.Contains('\n'))
        {
            throw new InvalidOperationException("the AdminPanel password must not contain line breaks");
        }

        return password;
    }

    private static bool IsEnabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name)?.Trim(), "1", StringComparison.Ordinal);

    private static string NonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
