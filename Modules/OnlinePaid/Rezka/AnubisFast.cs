using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Rezka;

static class AnubisFast
{
    static readonly Regex challengeRegex = new(
        "\\bid\\s*=\\s*[\"']anubis_challenge[\"'][^>]*>(?<json>.*?)</",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
    );

    public readonly record struct Challenge(string Json, string Id, string UserAgent);

    public static bool TryParseChallenge(string html, out Challenge result)
    {
        result = default;

        if (string.IsNullOrEmpty(html))
            return false;

        try
        {
            Match match = challengeRegex.Match(html);
            if (!match.Success)
                return false;

            string json = WebUtility.HtmlDecode(match.Groups["json"].Value).Trim();
            if (string.IsNullOrEmpty(json))
                return false;

            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("challenge", out JsonElement challenge) ||
                !challenge.TryGetProperty("id", out JsonElement idElement))
            {
                return false;
            }

            string id = idElement.GetString();
            if (string.IsNullOrEmpty(id))
                return false;

            result = new Challenge(json, id, FindStringProperty(root, "User-Agent"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string FindStringProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();

                string value = FindStringProperty(property.Value, name);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string value = FindStringProperty(item, name);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        return null;
    }

    public static async Task<Result> SolveAsync(
        string challengeJson,
        CancellationToken cancellationToken = default)
    {
        using var json = JsonDocument.Parse(challengeJson);

        JsonElement root = json.RootElement;
        JsonElement rules = root.GetProperty("rules");
        JsonElement challenge = root.GetProperty("challenge");

        string algorithm = rules.GetProperty("algorithm").GetString();
        if (algorithm != "fast")
            throw new NotSupportedException($"Unsupported algorithm: {algorithm}");

        string id = challenge.GetProperty("id").GetString();
        string randomData = challenge.GetProperty("randomData").GetString();
        int difficulty = rules.GetProperty("difficulty").GetInt32();

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(randomData))
            throw new InvalidOperationException("Invalid Anubis challenge.");

        if ((uint)difficulty > 64)
            throw new NotSupportedException($"Unsupported difficulty: {difficulty}");

        int threads = Math.Max(Environment.ProcessorCount / 2, 1);

        var resultSource = new TaskCompletionSource<Result>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        using var stopSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        using var cancellationRegistration = cancellationToken.Register(
            () => resultSource.TrySetCanceled(cancellationToken)
        );

        long startedAt = Stopwatch.GetTimestamp();
        Task[] workers = new Task[threads];

        for (int workerIndex = 0; workerIndex < threads; workerIndex++)
        {
            int startNonce = workerIndex;

            workers[workerIndex] = Task.Run(() =>
            {
                try
                {
                    FindNonce(
                        id,
                        randomData,
                        difficulty,
                        startNonce,
                        threads,
                        startedAt,
                        resultSource,
                        stopSource
                    );
                }
                catch (Exception ex)
                {
                    if (resultSource.TrySetException(ex))
                        stopSource.Cancel();
                }
            });
        }

        try
        {
            return await resultSource.Task.ConfigureAwait(false);
        }
        finally
        {
            stopSource.Cancel();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
    }

    static void FindNonce(
        string id,
        string randomData,
        int difficulty,
        int startNonce,
        int threads,
        long startedAt,
        TaskCompletionSource<Result> resultSource,
        CancellationTokenSource stopSource)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(randomData);
        byte[] input = new byte[prefix.Length + 20];

        prefix.CopyTo(input, 0);

        Span<byte> hash = stackalloc byte[32];
        ulong nonce = (ulong)startNonce;

        while (!stopSource.IsCancellationRequested)
        {
            if (!Utf8Formatter.TryFormat(
                    nonce,
                    input.AsSpan(prefix.Length),
                    out int nonceLength))
            {
                throw new InvalidOperationException("Cannot format nonce.");
            }

            SHA256.HashData(
                input.AsSpan(0, prefix.Length + nonceLength),
                hash
            );

            if (HasRequiredDifficulty(hash, difficulty))
            {
                string hashString = Convert
                    .ToHexString(hash)
                    .ToLowerInvariant();

                long elapsedTime = Math.Max(
                    1,
                    (long)Stopwatch
                        .GetElapsedTime(startedAt)
                        .TotalMilliseconds
                );

                if (resultSource.TrySetResult(new Result(
                        Id: id,
                        Hash: hashString,
                        Nonce: nonce,
                        ElapsedTime: elapsedTime)))
                {
                    stopSource.Cancel();
                }

                return;
            }

            nonce += (ulong)threads;
        }
    }

    static bool HasRequiredDifficulty(
        ReadOnlySpan<byte> hash,
        int difficulty)
    {
        int fullZeroBytes = difficulty / 2;

        for (int i = 0; i < fullZeroBytes; i++)
        {
            if (hash[i] != 0)
                return false;
        }

        return (difficulty & 1) == 0 ||
               (hash[fullZeroBytes] & 0xF0) == 0;
    }

    public readonly record struct Result(
        string Id,
        string Hash,
        ulong Nonce,
        long ElapsedTime)
    {
        public string BuildPassUrl(
            string siteUrl,
            string redir,
            string basePrefix = "")
        {
            string endpoint =
                $"{basePrefix.TrimEnd('/')}" +
                "/.within.website/x/cmd/anubis/api/pass-challenge";

            var uri = new UriBuilder(new Uri(new Uri(siteUrl), endpoint))
            {
                Query =
                    $"id={Uri.EscapeDataString(Id)}" +
                    $"&response={Uri.EscapeDataString(Hash)}" +
                    $"&nonce={Nonce}" +
                    $"&redir={Uri.EscapeDataString(redir)}" +
                    $"&elapsedTime={ElapsedTime}"
            };

            return uri.ToString();
        }
    }
}
