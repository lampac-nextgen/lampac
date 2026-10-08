using Newtonsoft.Json.Linq;
using OidcAuth.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OidcAuth.Services
{
    /// <summary>
    /// Minimal OIDC client: authorize URL, PKCE, code exchange, userinfo.
    /// The ID token signature is not verified (no JWT libs in the lampac runtime);
    /// identity comes from /userinfo fetched over TLS straight from the configured
    /// authority. Only iss/aud/exp/nonce are checked on the ID token.
    /// </summary>
    internal static class OidcFlowService
    {
        static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public static string RandomBase64Url(int bytesCount)
            => Base64UrlEncode(RandomNumberGenerator.GetBytes(bytesCount));

        public static string CodeChallenge(string codeVerifier)
            => Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        public static string BuildAuthorizeUrl(DiscoveryDocument doc, OidcAuthConf conf, string redirectUri, string state, string nonce, string codeChallenge)
        {
            var query = new List<string>
            {
                "response_type=code",
                "client_id=" + Uri.EscapeDataString(conf.clientId),
                "redirect_uri=" + Uri.EscapeDataString(redirectUri),
                "scope=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(conf.scopes) ? "openid" : conf.scopes),
                "state=" + Uri.EscapeDataString(state),
                "nonce=" + Uri.EscapeDataString(nonce),
                "code_challenge=" + Uri.EscapeDataString(codeChallenge),
                "code_challenge_method=S256"
            };

            string sep = doc.authorization_endpoint.Contains('?') ? "&" : "?";
            return doc.authorization_endpoint + sep + string.Join("&", query);
        }

        public static async Task<(string accessToken, string? idToken)> ExchangeCodeAsync(OidcAuthConf conf, DiscoveryDocument doc, string code, string redirectUri, string codeVerifier)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = conf.clientId,
                ["code_verifier"] = codeVerifier
            };

            if (!string.IsNullOrWhiteSpace(conf.clientSecret))
                form["client_secret"] = conf.clientSecret;

            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(doc.token_endpoint, content).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"token endpoint returned {(int)response.StatusCode}");

            var json = JObject.Parse(body);

            string? accessToken = json.Value<string>("access_token");
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException("token endpoint response has no access_token");

            return (accessToken, json.Value<string>("id_token"));
        }

        public static void ValidateIdToken(string? idToken, OidcAuthConf conf, string nonce)
        {
            if (string.IsNullOrEmpty(idToken))
                return;

            JObject payload;
            try
            {
                payload = JwtPayload(idToken);
            }
            catch
            {
                throw new InvalidOperationException("malformed id_token");
            }

            string? iss = payload.Value<string>("iss")?.Trim().TrimEnd('/');
            if (!string.Equals(iss, conf.authority.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("id_token iss mismatch");

            var aud = payload["aud"];
            bool audOk = aud?.Type switch
            {
                JTokenType.Array => aud.Any(t => string.Equals((string?)t, conf.clientId, StringComparison.Ordinal)),
                JTokenType.String => string.Equals((string?)aud, conf.clientId, StringComparison.Ordinal),
                _ => false
            };
            if (!audOk)
                throw new InvalidOperationException("id_token aud mismatch");

            long? exp = payload.Value<long?>("exp");
            if (exp.HasValue && DateTimeOffset.FromUnixTimeSeconds(exp.Value) < DateTimeOffset.Now)
                throw new InvalidOperationException("id_token expired");

            if (!string.IsNullOrEmpty(nonce))
            {
                string? tokenNonce = payload.Value<string>("nonce");
                if (!string.Equals(tokenNonce, nonce, StringComparison.Ordinal))
                    throw new InvalidOperationException("id_token nonce mismatch");
            }
        }

        public static async Task<JObject> GetUserinfoAsync(DiscoveryDocument doc, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, doc.userinfo_endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"userinfo endpoint returned {(int)response.StatusCode}");

            return JObject.Parse(body);
        }

        public static JObject JwtPayload(string jwt)
        {
            string[] parts = jwt.Split('.');
            if (parts.Length < 2)
                throw new InvalidOperationException("malformed jwt");

            return JObject.Parse(Base64UrlDecodeToString(parts[1]));
        }

        public static string Base64UrlDecodeToString(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: s += "==="; break;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }

        public static string Base64UrlEncode(byte[] data)
            => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
