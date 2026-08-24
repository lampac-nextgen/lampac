using Shared.Services;
using System.Collections.Concurrent;
using System.Net;

namespace Music;

public enum MusicProxyPurpose
{
    Api,
    Stream
}

public sealed class MusicProxyLease
{
    readonly ProxyManager manager;

    internal MusicProxyLease(string scope, ProxyManager manager, WebProxy proxy, (string ip, string username, string password) data)
    {
        Scope = scope;
        this.manager = manager;
        Proxy = proxy;
        Data = data;
    }

    public string Scope { get; }
    public WebProxy Proxy { get; }
    public (string ip, string username, string password) Data { get; }
    public bool Enabled => Proxy != null;

    public void Success() => manager?.Success();
    public void Failure() => manager?.Refresh();

    public void ApplyTo(MusicPlaybackSource source, bool overwrite = false)
    {
        if (source == null || !Enabled || (!overwrite && !string.IsNullOrWhiteSpace(source.proxy_url)))
            return;

        source.proxy_url = Data.ip ?? Proxy.Address?.ToString();
        source.proxy_username = Data.username;
        source.proxy_password = Data.password;
        source.proxy_scope = Scope;
    }
}

public static class MusicProxyService
{
    sealed record ManagerEntry(ModuleConf Conf, ProxyManager Manager);

    static readonly ConcurrentDictionary<string, ManagerEntry> managers = new(StringComparer.OrdinalIgnoreCase);
    static long configurationVersion;

    public static long ConfigurationVersion => Interlocked.Read(ref configurationVersion);

    public static void ConfigurationChanged()
    {
        Interlocked.Increment(ref configurationVersion);
        managers.Clear();
    }

    public static MusicProxyLease Acquire(string providerId, MusicProxyPurpose purpose)
    {
        var conf = ModInit.conf;
        bool enabled = purpose == MusicProxyPurpose.Api
            ? conf?.useproxy == true
            : conf?.useproxystream == true;

        if (!enabled || conf == null)
            return new MusicProxyLease(null, null, null, default);

        string provider = string.IsNullOrWhiteSpace(providerId) ? "shared" : providerId.Trim().ToLowerInvariant();
        string scope = $"Music:{provider}:{purpose.ToString().ToLowerInvariant()}";
        var entry = managers.AddOrUpdate(
            scope,
            _ => new ManagerEntry(conf, new ProxyManager(scope, conf)),
            (_, current) => ReferenceEquals(current.Conf, conf)
                ? current
                : new ManagerEntry(conf, new ProxyManager(scope, conf)));

        var selected = entry.Manager.BaseGet();
        return new MusicProxyLease(scope, entry.Manager, selected.proxy, selected.data);
    }

    public static void ApplyStreamProxy(string providerId, IEnumerable<MusicPlaybackSource> sources)
    {
        if (sources == null)
            return;

        MusicProxyLease lease = null;

        foreach (var source in sources)
        {
            if (source == null || !string.IsNullOrWhiteSpace(source.proxy_url))
                continue;

            lease ??= Acquire(providerId, MusicProxyPurpose.Stream);
            lease.ApplyTo(source);
        }
    }

    public static void ReportSuccess(string scope)
        => GetCurrentManager(scope)?.Success();

    public static void ReportFailure(string scope)
        => GetCurrentManager(scope)?.Refresh();

    static ProxyManager GetCurrentManager(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        return managers.TryGetValue(scope, out var entry) ? entry.Manager : null;
    }
}
