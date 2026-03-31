using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    sealed class TelegramAuthApiClient : IDisposable
    {
        public const string MutationsSecretHeaderName = "X-TelegramAuth-Mutations-Secret";

        readonly HttpClient _http;
        readonly string _mutationsSecret;

        public TelegramAuthApiClient(TelegramAuthBotConf conf)
        {
            var baseUrl = (conf.lampac_base_url ?? "").Trim().TrimEnd('/') + "/";
            _http = new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute) };
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, conf.request_timeout_sec));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _mutationsSecret = conf.mutations_api_secret?.Trim() ?? "";
        }

        public void Dispose() => _http.Dispose();

        public async Task<UserByTelegramDto> GetUserByTelegramAsync(string telegramId, CancellationToken ct)
        {
            var path = "tg/auth/user/by-telegram?" + Uri.EscapeDataString("telegramId") + "=" + Uri.EscapeDataString(telegramId);
            using var resp = await _http.GetAsync(path, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            return JsonConvert.DeserializeObject<UserByTelegramDto>(body);
        }

        public async Task<DevicesResponseDto> GetDevicesAsync(string telegramId, CancellationToken ct)
        {
            var path = "tg/auth/devices?" + Uri.EscapeDataString("telegramId") + "=" + Uri.EscapeDataString(telegramId);
            using var resp = await _http.GetAsync(path, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!resp.IsSuccessStatusCode)
                return null;
            return JsonConvert.DeserializeObject<DevicesResponseDto>(body);
        }

        public async Task<BindCompleteResult> BindCompleteAsync(string uid, string telegramId, string username, CancellationToken ct)
        {
            var payload = new { uid, telegramId, username };
            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("tg/auth/bind/complete", content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new BindCompleteResult();
            var jo = JObject.Parse(body);
            return new BindCompleteResult
            {
                Ok = jo.Value<bool?>("ok") == true,
                PendingAdminApproval = jo.Value<bool?>("pendingAdminApproval") == true
            };
        }

        public async Task<bool> UnbindDeviceAsync(string uid, CancellationToken ct)
        {
            var payload = new { uid };
            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("tg/auth/device/unbind", content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return false;
            var jo = JObject.Parse(body);
            return jo.Value<bool?>("ok") == true;
        }

        public async Task<(bool ok, string detail)> ReactivateDeviceAsync(string telegramId, string uid, CancellationToken ct)
        {
            var payload = new { telegramId, uid };
            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("tg/auth/device/reactivate", content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? (true, body) : (false, body);
        }

        public async Task<(bool ok, string detail)> SetDeviceDisplayNameAsync(string uid, string? name, CancellationToken ct)
        {
            var payload = new { uid, name };
            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync("tg/auth/device/name", content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? (true, body) : (false, body);
        }

        public async Task<(bool ok, string detail)> ImportLegacyAsync(CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "tg/auth/import");
            AddMutationsSecret(req);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? (true, body) : (false, body);
        }

        public async Task<(bool ok, string detail)> CleanupDevicesAsync(CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "tg/auth/devices/cleanup");
            AddMutationsSecret(req);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? (true, body) : (false, body);
        }

        public async Task<AdminUsersListResponseDto> GetAdminUsersAsync(CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "tg/auth/admin/users");
            AddMutationsSecret(req);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            return JsonConvert.DeserializeObject<AdminUsersListResponseDto>(body);
        }

        public async Task<(bool ok, string detail)> SetUserDisabledAsync(string telegramId, bool disabled, CancellationToken ct)
        {
            var payload = new { telegramId, disabled };
            using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, "tg/auth/admin/user/disabled") { Content = content };
            AddMutationsSecret(req);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode ? (true, body) : (false, body);
        }

        void AddMutationsSecret(HttpRequestMessage req)
        {
            if (_mutationsSecret.Length > 0)
                req.Headers.TryAddWithoutValidation(MutationsSecretHeaderName, _mutationsSecret);
        }
    }
}
