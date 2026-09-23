using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Alloha;

public static class AllohaBorth
{
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
        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes("fingerprint-alloha"));
        StringBuilder sb = new StringBuilder(64);
        foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
        return $"{sb}|{transformed}";
    }
}
