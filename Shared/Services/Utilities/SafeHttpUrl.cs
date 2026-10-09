using System.Net;
using System.Net.Sockets;

namespace Shared.Services.Utilities;

public static class SafeHttpUrl
{
    // ponytail: hostname literals only, no DNS resolve. Upgrade: resolve A/AAAA and reject private.
    static SafeHttpUrl()
    {
        if (IsSafe("http://127.0.0.1") ||
            IsSafe("http://10.1.1.1") ||
            IsSafe("http://169.254.169.254") ||
            IsSafe("http://localhost/x") ||
            IsSafe("http://[::1]/") ||
            IsSafe("http://user:pass@example.com/a") ||
            !IsSafe("https://example.com/a"))
            throw new InvalidOperationException("SafeHttpUrl self-check failed");
    }

    public static bool IsSafe(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        string hostname;
        try
        {
            hostname = uri.IdnHost.TrimEnd('.');
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (string.IsNullOrEmpty(hostname) ||
            hostname.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            hostname.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            hostname.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(hostname, out IPAddress address) || !IsPrivateAddress(address);
    }

    static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte a = bytes[0];
            byte b = bytes[1];

            return a == 0 || a == 10 || a == 127 ||
                   (a == 100 && b >= 64 && b <= 127) ||
                   (a == 169 && b == 254) ||
                   (a == 172 && b >= 16 && b <= 31) ||
                   (a == 192 && (b == 0 || b == 168)) ||
                   (a == 198 && (b == 18 || b == 19)) ||
                   a >= 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return true;

        return address.Equals(IPAddress.IPv6Any) ||
               address.Equals(IPAddress.IPv6None) ||
               address.IsIPv6LinkLocal ||
               address.IsIPv6SiteLocal ||
               address.IsIPv6Multicast ||
               (bytes[0] & 0xfe) == 0xfc;
    }
}
