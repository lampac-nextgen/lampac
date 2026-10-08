using Newtonsoft.Json.Linq;
using Shared.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OidcAuth.Services
{
    internal class DiscoveryDocument
    {
        public string authorization_endpoint { get; set; } = "";
        public string token_endpoint { get; set; } = "";
        public string userinfo_endpoint { get; set; } = "";
        public string? end_session_endpoint { get; set; }
    }

    /// <summary>
    /// Cache of the provider's /.well-known/openid-configuration document.
    /// Lazy on purpose: the IdP may be unreachable when lampac starts.
    /// </summary>
    internal static class OidcDiscovery
    {
        static readonly SemaphoreSlim _lock = new(1, 1);

        static DiscoveryDocument? _cached;
        static DateTimeOffset _cacheExpires;
        static DateTimeOffset _failBackoff;

        public static async Task<DiscoveryDocument?> GetAsync()
        {
            if (_cached != null && _cacheExpires > DateTimeOffset.Now)
                return _cached;

            if (_failBackoff > DateTimeOffset.Now)
                return null;

            await _lock.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_cached != null && _cacheExpires > DateTimeOffset.Now)
                    return _cached;

                string? authority = ModInit.conf?.authority?.Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(authority))
                    return null;

                string json = await Http.Get($"{authority}/.well-known/openid-configuration", timeoutSeconds: 10).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Fail();
                    return null;
                }

                var j = JObject.Parse(json);

                var doc = new DiscoveryDocument
                {
                    authorization_endpoint = j.Value<string>("authorization_endpoint") ?? "",
                    token_endpoint = j.Value<string>("token_endpoint") ?? "",
                    userinfo_endpoint = j.Value<string>("userinfo_endpoint") ?? "",
                    end_session_endpoint = j.Value<string>("end_session_endpoint")
                };

                if (string.IsNullOrEmpty(doc.authorization_endpoint) ||
                    string.IsNullOrEmpty(doc.token_endpoint) ||
                    string.IsNullOrEmpty(doc.userinfo_endpoint))
                {
                    Console.WriteLine("OidcAuth: discovery document is incomplete");
                    Fail();
                    return null;
                }

                _cached = doc;
                _cacheExpires = DateTimeOffset.Now.AddHours(1);
                return doc;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OidcAuth: discovery failed: {ex.Message}");
                Fail();
                return null;
            }
            finally
            {
                _lock.Release();
            }
        }

        static void Fail()
        {
            _failBackoff = DateTimeOffset.Now.AddSeconds(30);
        }
    }
}
