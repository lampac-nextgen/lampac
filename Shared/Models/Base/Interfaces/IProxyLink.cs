namespace Shared.Models.Base;

public interface IProxyLink
{
    public string Encrypt(ReadOnlySpan<char> uri, string plugin, DateTime ex = default, bool IsProxyImg = false);
}
