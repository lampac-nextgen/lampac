using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FlixCDN;

public struct FlixCDNInvoke
{
    OnlinesSettings init;
    HttpHydra httpHydra;
    Func<string, string> onstreamfile;

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FlixCDNInvoke(OnlinesSettings init, HttpHydra httpHydra, Func<string, string> onstreamfile)
    {
        this.init = init;
        this.httpHydra = httpHydra;
        this.onstreamfile = onstreamfile;
    }


    public string BuildPlayerUrl(long kinopoisk_id)
    {
        return $"{init.host}/show/kinopoisk/{kinopoisk_id}?extrans=1&extepi=1&unfseason=1";
    }


    async public Task<PlayerPayload> GetPlayer(long kinopoisk_id)
    {
        if (kinopoisk_id <= 0)
            return null;

        string html = await httpHydra.Get(BuildPlayerUrl(kinopoisk_id), safety: true);
        if (string.IsNullOrWhiteSpace(html))
            return null;

        const string marker = "window.__PLAYER_PAYLOAD__ = ";
        int start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += marker.Length;
        int end = html.IndexOf(';', start);
        if (end < 0)
            return null;

        string json = html.Substring(start, end - start).Trim();
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PlayerPayload>(json, jsonOptions);
        }
        catch
        {
            return null;
        }
    }


    async public Task<string> GetPlayerFile(long kinopoisk_id, int id, int translation, short season = 0, short episode = 0)
    {
        if (kinopoisk_id <= 0 || id <= 0 || translation <= 0)
            return null;

        string showUrl = BuildPlayerUrl(kinopoisk_id);

        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true
            };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");

            using (var warmRequest = new HttpRequestMessage(HttpMethod.Get, showUrl))
            {
                warmRequest.Headers.Referrer = new Uri(init.host + "/");
                using var warmResponse = await client.SendAsync(warmRequest);
                if (!warmResponse.IsSuccessStatusCode)
                    return null;

                await warmResponse.Content.ReadAsStringAsync();
                showUrl = warmResponse.RequestMessage?.RequestUri?.ToString() ?? showUrl;
            }

            string json = JsonSerializer.Serialize(new
            {
                id,
                translation,
                season_number = season > 0 ? (short?)season : null,
                episode_number = episode > 0 ? (short?)episode : null,
                force_cdn = string.Empty,
                turnstile_token = string.Empty
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, init.host + "/api/player/files");
            request.Headers.Referrer = new Uri(showUrl);
            request.Headers.TryAddWithoutValidation("Origin", init.host);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            string body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
                return null;

            var root = JsonSerializer.Deserialize<PlayerFiles>(body, jsonOptions);
            return root?.file;
        }
        catch
        {
            return null;
        }
    }


    public StreamQualityTpl GetStreamQualityTpl(string file)
    {
        var streamquality = new StreamQualityTpl();
        var qualityByLink = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var linkOrder = new List<string>();

        foreach (Match m in Regex.Matches(file ?? string.Empty, "\\[(?<q>\\d{3,4})\\](?<url>https?://[^,\"\\[\\s]+)"))
        {
            string link = m.Groups["url"].Value;
            if (string.IsNullOrEmpty(link) || !int.TryParse(m.Groups["q"].Value, out int quality))
                continue;

            if (qualityByLink.TryGetValue(link, out int currentQuality))
            {
                if (quality > currentQuality)
                {
                    string restoredLink = RestoreQualityLink(link, currentQuality, quality);
                    if (!string.Equals(restoredLink, link, StringComparison.OrdinalIgnoreCase) && !qualityByLink.ContainsKey(restoredLink))
                    {
                        qualityByLink[restoredLink] = quality;
                        linkOrder.Add(restoredLink);
                    }
                }
                else if (quality < currentQuality)
                {
                    qualityByLink[link] = quality;
                }

                continue;
            }

            qualityByLink[link] = quality;
            linkOrder.Add(link);
        }

        foreach (string link in linkOrder)
            streamquality.Insert(onstreamfile.Invoke(link), $"{qualityByLink[link]}p");

        if (!streamquality.IsEmpty)
            return streamquality;

        foreach (Match m in Regex.Matches(file ?? string.Empty, "(https?://[^,\"\\[\\s]+\\.(m3u8|mp4)(:hls:manifest\\.m3u8)?)"))
        {
            string link = m.Groups[1].Value;
            if (string.IsNullOrEmpty(link))
                continue;

            streamquality.Append(onstreamfile.Invoke(link), "auto");
            break;
        }

        return streamquality;
    }


    static string RestoreQualityLink(string link, int sourceQuality, int targetQuality)
    {
        if (string.IsNullOrEmpty(link) || sourceQuality <= 0 || targetQuality <= sourceQuality)
            return link;

        string source = $"/{sourceQuality}.mp4";
        int index = link.IndexOf(source, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return link;

        return link.Substring(0, index) + $"/{targetQuality}.mp4" + link.Substring(index + source.Length);
    }
}
