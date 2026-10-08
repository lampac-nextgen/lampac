using Shared.Models.Module;

namespace OidcAuth.Models;

public class OidcAuthConf : ModuleBaseConf
{
    public bool enable { get; set; }

    /// <summary>Install the OIDC login screen through LampaWeb's deny.js override</summary>
    public bool overrideDeny { get; set; } = true;

    /// <summary>Provider name shown on the login button</summary>
    public string name { get; set; } = "OIDC";

    /// <summary>Issuer URL, e.g. https://auth.example.com/application/o/lampac/</summary>
    public string authority { get; set; } = "";

    /// <summary>Public lampac URL used for redirect_uri; empty — derived from the request</summary>
    public string publicUrl { get; set; } = "";

    public string clientId { get; set; } = "";

    /// <summary>Empty = public client (PKCE only)</summary>
    public string clientSecret { get; set; } = "";

    public string scopes { get; set; } = "openid profile email";

    /// <summary>AccsUser.group for users created by this module</summary>
    public int defaultGroup { get; set; }

    /// <summary>Lifetime in days for created users; 0 = never expires</summary>
    public int defaultExpiresDays { get; set; }

    /// <summary>Log created accounts to logs/oidc/unlinked.log (source of sub for manual linking)</summary>
    public bool unlinkedLog { get; set; } = true;
}
