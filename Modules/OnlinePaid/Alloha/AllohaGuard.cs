using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alloha;

public class AllohaGuard
{
    private static readonly ConcurrentDictionary<string, AllohaGuard> _instances = new();
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _linkHost;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string _token;
    private DateTime _tokenTime = DateTime.MinValue;
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(90);

    private const string ChromeUa = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    private AllohaGuard(string linkHost)
    {
        _linkHost = linkHost.TrimEnd('/');
    }

    public static AllohaGuard ForHost(string linkHost)
    {
        string host = (string.IsNullOrEmpty(linkHost) ? "https://scalp-as.stloadi.live" : linkHost).TrimEnd('/');
        return _instances.GetOrAdd(host, h => new AllohaGuard(h));
    }

    public static string GetLiveToken(string linkHost)
    {
        return ForHost(linkHost).GetCachedToken();
    }

    public string GetCachedToken()
    {
        if (!string.IsNullOrEmpty(_token) && (DateTime.UtcNow - _tokenTime) < (_ttl * 2))
            return _token;
        return null;
    }

    public async Task<string> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_token) && (DateTime.UtcNow - _tokenTime) < _ttl)
            return _token;

        await _lock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_token) && (DateTime.UtcNow - _tokenTime) < _ttl)
                return _token;

            return await FetchTokenAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> FetchTokenAsync()
    {
        try
        {
            string sarnUrl = $"{_linkHost}/sarn";
            using var sarnReq = new HttpRequestMessage(HttpMethod.Get, sarnUrl);
            sarnReq.Headers.Add("User-Agent", ChromeUa);
            sarnReq.Headers.Add("Referer", $"{_linkHost}/");

            using var sarnResp = await _httpClient.SendAsync(sarnReq);
            if (!sarnResp.IsSuccessStatusCode)
                return null;

            string sarnJson = await sarnResp.Content.ReadAsStringAsync();
            using var sarnDoc = JsonDocument.Parse(sarnJson);
            var root = sarnDoc.RootElement;

            if (!root.TryGetProperty("challenge", out var challengeProp) ||
                !root.TryGetProperty("nonce", out var nonceProp))
            {
                return null;
            }

            string challenge = challengeProp.GetString();
            string nonce = nonceProp.GetString();
            string uap = root.TryGetProperty("uap", out var uapProp) ? uapProp.GetString() : "";

            if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(nonce))
                return null;

            string proofStr = $"{nonce}||{uap}|||";
            string proof;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(proofStr));
                var sb = new StringBuilder(64);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                proof = sb.ToString();
            }

            string vorfUrl = $"{_linkHost}/vorf";
            var payload = new
            {
                challenge = challenge,
                proof = proof,
                parentOrigin = "",
                referrer = $"{_linkHost}/"
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            using var vorfReq = new HttpRequestMessage(HttpMethod.Post, vorfUrl)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            vorfReq.Headers.Add("User-Agent", ChromeUa);
            vorfReq.Headers.Add("Referer", $"{_linkHost}/");

            using var vorfResp = await _httpClient.SendAsync(vorfReq);
            if (!vorfResp.IsSuccessStatusCode)
                return null;

            string vorfJson = await vorfResp.Content.ReadAsStringAsync();
            using var vorfDoc = JsonDocument.Parse(vorfJson);
            var vRoot = vorfDoc.RootElement;

            if (vRoot.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                vRoot.TryGetProperty("token", out var tokenProp))
            {
                string newToken = tokenProp.GetString();
                if (!string.IsNullOrEmpty(newToken))
                {
                    _token = newToken;
                    _tokenTime = DateTime.UtcNow;
                    return newToken;
                }
            }
        }
        catch
        {
            // ignore network errors and keep existing token if any
        }

        return _token;
    }
}
