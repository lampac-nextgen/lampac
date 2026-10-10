using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jint;

namespace Alloha;

public static class AllohaBorth
{
    private static readonly ConcurrentDictionary<string, string> _scriptRunnerCache = new();
    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    #region Fast Static Algorithm
    private static string Zy(string zj, bool zr = false)
    {
        int zs = zj.Length;
        if (zs <= 0) return zj;
        int zb = 0;
        while ((1 << zb) < zs) zb++;

        int Zk(int q1)
        {
            if (q1 == 0) return 0;
            int q2 = 1;
            while (q1 > 1) { q2++; q1 >>= 1; }
            return q2;
        }

        int[] zp_counts = new int[zb + 1];
        for (int zl = 0; zl < zs; zl++) zp_counts[Zk(zl)]++;

        string[] zp = new string[zb + 1];
        int zf = 0;
        for (int zh = zb; zh >= 0; zh--)
        {
            int zx = zp_counts[zh];
            zp[zh] = zj.Substring(zf, zx);
            zf += zx;
        }

        int[] zy_idx = new int[zb + 1];
        char[] zw = new char[zs];
        for (int zv = 0; zv < zs; zv++)
        {
            int zk = Zk(zv);
            zw[zv] = zp[zk][zy_idx[zk]++];
        }

        string q0 = new string(zw);
        if (zr && q0.Length > 1) q0 = q0.Substring(1) + q0[0];
        return q0;
    }

    private static string Zz(string zj, bool zr = false)
    {
        int zs = zj.Length;
        if (zs <= 0) return zj;
        int zb = 0;
        while ((1 << zb) < zs) zb++;

        int Zk(int q0)
        {
            if (q0 == 0) return zb;
            int q1 = 0;
            while ((1 & q0) == 0) { q1++; q0 >>= 1; }
            return q1;
        }

        int[] zp_counts = new int[zb + 1];
        for (int zl = 0; zl < zs; zl++) zp_counts[Zk(zl)]++;

        string[] zp = new string[zb + 1];
        int zf = 0;
        for (int zh = 0; zh <= zb; zh++)
        {
            zp[zh] = zj.Substring(zf, zp_counts[zh]);
            zf += zp_counts[zh];
        }

        int[] zx = new int[zb + 1];
        char[] zy_chars = new char[zs];
        for (int zw = 0; zw < zs; zw++)
        {
            int zv = Zk(zw);
            zy_chars[zw] = zp[zv][zx[zv]++];
        }

        string zk_res = new string(zy_chars);
        if (zr && zk_res.Length > 2) zk_res = zk_res.Substring(zk_res.Length - 2) + zk_res.Substring(0, zk_res.Length - 2);
        return zk_res;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n % 2 == 0) return n == 2;
        for (int w = 3; w * w <= n; w += 2)
            if (n % w == 0) return false;
        return true;
    }

    private static string Z9(string zj, bool zr = false)
    {
        int zs = zj.Length;
        if (zs <= 1) return zj;
        int w = Math.Max(2, zs + 1);
        while (!IsPrime(w)) w++;
        int zk = w;

        bool[] zp_visited = new bool[zs];
        List<int> zl = new List<int>(zs);
        int zp = 0;
        while (zl.Count < zs)
        {
            zp = (zp + 2) % zk;
            if (zp < zs && !zp_visited[zp])
            {
                zl.Add(zp);
                zp_visited[zp] = true;
            }
        }

        char[] zf = new char[zs];
        for (int zh = 0; zh < zs; zh++) zf[zl[zh]] = zj[zh];
        string zx = new string(zf);
        if (zr && zx.Length > 1) zx = zx.Substring(1) + zx[0];
        return zx;
    }

    public static string Compute(string viewporti)
    {
        if (string.IsNullOrEmpty(viewporti)) return string.Empty;
        string transformed = Z9(Zz(Zy(viewporti, false), false), false);
        return FormatBorth(transformed);
    }
    #endregion

    #region Dynamic JS Runtime Extraction
    public static async Task<string> ComputeFromHtmlAsync(string playerHtml, string viewporti, string linkHost)
    {
        if (string.IsNullOrEmpty(playerHtml) || string.IsNullOrEmpty(viewporti))
            return Compute(viewporti);

        try
        {
            var match = Regex.Match(playerHtml, @"<script[^>]+src=[""']([^""']*(?:app|player|build)[^""']*\.js)[""']", RegexOptions.IgnoreCase);
            if (!match.Success)
                return Compute(viewporti);

            string scriptPath = match.Groups[1].Value;
            string scriptUrl = scriptPath.StartsWith("http")
                ? scriptPath
                : $"{linkHost.TrimEnd('/')}/{scriptPath.TrimStart('/')}";

            if (!_scriptRunnerCache.TryGetValue(scriptUrl, out string runnerCode))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, scriptUrl);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
                req.Headers.Add("Referer", $"{linkHost.TrimEnd('/')}/");

                using var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return Compute(viewporti);

                string jsContent = await resp.Content.ReadAsStringAsync();
                runnerCode = ExtractRunnerFromJs(jsContent);
                if (!string.IsNullOrEmpty(runnerCode))
                {
                    _scriptRunnerCache.TryAdd(scriptUrl, runnerCode);
                }
            }

            if (!string.IsNullOrEmpty(runnerCode))
            {
                string transformed = ExecuteRunnerWithJint(runnerCode, viewporti);
                if (!string.IsNullOrEmpty(transformed))
                    return FormatBorth(transformed);
            }
        }
        catch
        {
            // Fall back to compiled static implementation
        }

        return Compute(viewporti);
    }

    private static string ExtractRunnerFromJs(string appJs)
    {
        try
        {
            var a0zMatch = Regex.Match(appJs, @"function\s+a0Z\(\)\{[\s\S]+?return\s+a0Z\(\);\s*\}");
            if (!a0zMatch.Success) return null;

            var rotMatch = Regex.Match(appJs, @"\(function\([a-zA-Z0-9_$,\s]+\)\{[\s\S]+?\}\(a0Z,[\s\S]+?\)\);");
            if (!rotMatch.Success) return null;

            var zyMatch = Regex.Match(appJs, @"function\s+zy\(zj\)\{[\s\S]+?return\s+zR&&q0[\s\S]+?q0;\}");
            var zZMatch = Regex.Match(appJs, @"function\s+zZ\(zj\)\{[\s\S]+?return\s+zR&&zk[\s\S]+?zk;\}");
            var z9Match = Regex.Match(appJs, @"function\s+z9\(zj\)\{[\s\S]+?return\s+zR&&zx[\s\S]+?zx;\}");

            if (!zyMatch.Success || !zZMatch.Success || !z9Match.Success)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine(a0zMatch.Value);
            sb.AppendLine(rotMatch.Value);
            sb.AppendLine(@"
                function a0y(Z,y){Z=Z-(0x1a5c+0x5d*-0x23+-0xbf7);var M=a0Z();var a=M[Z];return a;}
                function a0qw(Z,y){return a0y(Z- -0x195,y);}
                function qk(Z,y){return a0qw(Z-0x314,y);}
            ");
            sb.AppendLine(zyMatch.Value);
            sb.AppendLine(zZMatch.Value);
            sb.AppendLine(z9Match.Value);
            sb.AppendLine(@"
                function computeBorth(vp) {
                    return z9(zZ(zy(vp, false), false), false);
                }
            ");

            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string ExecuteRunnerWithJint(string runnerCode, string viewporti)
    {
        try
        {
            var engine = new Engine();
            engine.Execute(runnerCode);
            var result = engine.Invoke("computeBorth", viewporti);
            return result?.AsString();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBorth(string transformed)
    {
        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes("fingerprint-alloha"));
        var sb = new StringBuilder(64);
        foreach (byte b in hashBytes)
            sb.Append(b.ToString("x2"));
        return $"{sb}|{transformed}";
    }
    #endregion
}
