using Shared.Models.Base;
using System;
using System.Collections.Generic;

namespace Aladin;

public class ModuleConf : BaseSettings, ICloneable
{
    public ModuleConf(string plugin, string apihost, string linkhost, string token, bool m4s)
    {
        this.plugin = plugin;
        this.token = token;
        this.m4s = m4s;

        this.linkhost = linkhost == null ? string.Empty : (linkhost.StartsWith("http") ? linkhost : Decrypt(linkhost)!);
        this.apihost = apihost == null ? string.Empty : (apihost.StartsWith("http") ? apihost : Decrypt(apihost));
    }

    public string linkhost { get; set; }

    public bool m4s { get; set; }

    public string nativeLibrary { get; set; }

    public string tlsProfile { get; set; }

    public string authorizations { get; set; }

    public string fingerprintHash { get; set; }

    public string referer { get; set; }

    public string acceptLanguage { get; set; }

    public List<string> linkhosts { get; set; }

    public string nativeProxy { get; set; }

    public int httpRetries { get; set; } = 3;

    public int resolveTimeout { get; set; } = 8;

    public int urlTtl { get; set; } = 7200;

    public int wsReconnectAttempts { get; set; } = 3;

    public int wsMaxSessions { get; set; } = 12;

    public int wsHandshakeConcurrency { get; set; } = 2;

    public int liveMaxStreams { get; set; } = 256;

    public int liveStreamTtl { get; set; } = 14400;

    public ModuleConf Clone()
    {
        return (ModuleConf)MemberwiseClone();
    }

    object ICloneable.Clone()
    {
        return MemberwiseClone();
    }
}
