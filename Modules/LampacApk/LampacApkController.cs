using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Shared;

namespace LampacApk;

[IgnoreAntiforgeryToken]
public class LampacApkController : BaseController
{
    const string ApkContentType = "application/vnd.android.package-archive";
    const string TemplateVersion = "lampac-full-lampa-template-v1";
    const string KeyAlias = "lampac-apk";
    const string LegacyKeyPassword = "LampacApk_2026_Local_Key";
    const int MaxStartUrlLength = 2048;
    const int MaxCachedApkFiles = 8;
    const long MaxCacheBytes = 512L * 1024 * 1024;

    static readonly TimeSpan TempBuildMaxAge = TimeSpan.FromHours(2);
    static readonly object KeyPasswordLock = new();
    static string cachedKeyPassword;

    static readonly SemaphoreSlim BuildLock = new(1, 1);

    static string ModDir => ResolveModDir();
    static string TemplateApk => Path.Combine(ModDir, "assets", "template", "lampac-template-unsigned.apk");
    static string CacheDir => Path.Combine(AppContext.BaseDirectory, "cache", "widgets", "android");
    static string SigningDir => Path.Combine(AppContext.BaseDirectory, "database", "lampac-apk");
    static string KeystorePath => Path.Combine(SigningDir, "lampac-apk.jks");
    static string KeyPasswordPath => Path.Combine(SigningDir, "lampac-apk.pass");
    static string KeyPassword => GetOrCreateKeyPassword();

    static string ResolveModDir()
    {
        if (!string.IsNullOrWhiteSpace(ModInit.ModulePath) && Directory.Exists(ModInit.ModulePath))
            return ModInit.ModulePath;

        foreach (string modFolder in new[] { "mods", "module" })
        {
            string candidate = Path.Combine(AppContext.BaseDirectory, modFolder, "LampacApk");
            if (System.IO.File.Exists(Path.Combine(candidate, "assets", "template", "lampac-template-unsigned.apk")))
                return candidate;
        }

        return Path.Combine(AppContext.BaseDirectory, "mods", "LampacApk");
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("/lampac.apk")]
    [Route("/android.apk")]
    public async System.Threading.Tasks.Task<ActionResult> LampacApk(string overwritehost)
    {
        SetHeadersNoCache();

        try
        {
            string startUrl = NormalizeStartUrl(overwritehost);
            string apk = await BuildApk(startUrl);

            Response.Headers[HeaderNames.ContentDisposition] = "attachment; filename=\"lampac.apk\"";
            return PhysicalFile(apk, ApkContentType);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", nameof(LampacApkController), "id_lampac_apk_build");
            return Content($"Lampac APK build failed: {ex.Message}", "text/plain; charset=utf-8");
        }
    }

    string NormalizeStartUrl(string overwritehost)
    {
        string value = string.IsNullOrWhiteSpace(overwritehost) ? host : overwritehost.Trim();

        if (!Regex.IsMatch(value, "^https?://", RegexOptions.IgnoreCase))
            value = "http://" + value;

        if (value.Length > MaxStartUrlLength)
            throw new ArgumentException("Lampac URL is too long.");

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("Lampac URL must be a valid http or https URL.");
        }

        return value.TrimEnd('/') + "/";
    }

    static async System.Threading.Tasks.Task<string> BuildApk(string startUrl)
    {
        if (!System.IO.File.Exists(TemplateApk))
            throw new FileNotFoundException("Template APK not found", TemplateApk);

        Directory.CreateDirectory(CacheDir);
        PruneTempBuildDirs();

        string cacheKey = Sha256Hex($"{TemplateVersion}|{System.IO.File.GetLastWriteTimeUtc(TemplateApk).Ticks}|{startUrl}")[..32];
        string finalApk = Path.Combine(CacheDir, $"{cacheKey}.apk");

        if (System.IO.File.Exists(finalApk))
        {
            TouchFile(finalApk);
            PruneApkCache(finalApk);
            return finalApk;
        }

        await BuildLock.WaitAsync();
        try
        {
            if (System.IO.File.Exists(finalApk))
            {
                TouchFile(finalApk);
                PruneApkCache(finalApk);
                return finalApk;
            }

            string buildTools = FindAndroidBuildTools();
            string zipalign = FindTool(buildTools, "zipalign.exe", "zipalign");
            string apksigner = FindTool(buildTools, "apksigner.bat", "apksigner");
            string keytool = FindOnPath("keytool.exe") ?? FindOnPath("keytool");

            if (zipalign == null)
                throw new FileNotFoundException("zipalign not found. Install Android SDK build-tools.");
            if (apksigner == null)
                throw new FileNotFoundException("apksigner not found. Install Android SDK build-tools.");
            if (keytool == null && !System.IO.File.Exists(KeystorePath))
                throw new FileNotFoundException("keytool not found. Install JDK or create database/lampac-apk/lampac-apk.jks manually.");

            EnsureKeystore(keytool);

            string tempDir = Path.Combine(CacheDir, "tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string patched = Path.Combine(tempDir, "patched.apk");
                string aligned = Path.Combine(tempDir, "aligned.apk");
                string signed = Path.Combine(tempDir, "lampac.apk");

                PatchTemplateApk(TemplateApk, patched, startUrl);
                RunTool(zipalign, new[] { "-f", "-p", "4", patched, aligned }, tempDir);
                RunTool(apksigner, new[]
                {
                    "sign",
                    "--ks", KeystorePath,
                    "--ks-pass", "pass:" + KeyPassword,
                    "--key-pass", "pass:" + KeyPassword,
                    "--ks-key-alias", KeyAlias,
                    "--out", signed,
                    aligned
                }, tempDir);
                RunTool(apksigner, new[] { "verify", "--verbose", signed }, tempDir);

                System.IO.File.Copy(signed, finalApk, overwrite: true);
                TouchFile(finalApk);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }

            PruneApkCache(finalApk);

            return finalApk;
        }
        finally
        {
            BuildLock.Release();
        }
    }

    static void PatchTemplateApk(string templateApk, string outputApk, string startUrl)
    {
        string json = JsonSerializer.Serialize(new { startUrl }, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        bool configWritten = false;

        using ZipArchive source = ZipFile.OpenRead(templateApk);
        using ZipArchive output = ZipFile.Open(outputApk, ZipArchiveMode.Create);

        foreach (ZipArchiveEntry entry in source.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            if (IsApkSignatureEntry(entry.FullName))
                continue;

            ZipArchiveEntry next = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);

            if (entry.FullName.Equals("assets/lampac.json", StringComparison.OrdinalIgnoreCase))
            {
                using Stream stream = next.Open();
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data, 0, data.Length);
                configWritten = true;
                continue;
            }

            using Stream input = entry.Open();
            using Stream target = next.Open();
            input.CopyTo(target);
        }

        if (!configWritten)
        {
            ZipArchiveEntry config = output.CreateEntry("assets/lampac.json", CompressionLevel.Optimal);
            using Stream stream = config.Open();
            byte[] data = Encoding.UTF8.GetBytes(json);
            stream.Write(data, 0, data.Length);
        }
    }

    static bool IsApkSignatureEntry(string fullName)
    {
        if (!fullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
            return false;

        string fileName = Path.GetFileName(fullName);
        return fileName.Equals("MANIFEST.MF", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".SF", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".EC", StringComparison.OrdinalIgnoreCase);
    }

    static void EnsureKeystore(string keytool)
    {
        Directory.CreateDirectory(SigningDir);

        if (System.IO.File.Exists(KeystorePath))
            return;

        string keyPassword = KeyPassword;

        RunTool(keytool, new[]
        {
            "-genkeypair",
            "-v",
            "-keystore", KeystorePath,
            "-storepass", keyPassword,
            "-keypass", keyPassword,
            "-alias", KeyAlias,
            "-keyalg", "RSA",
            "-keysize", "2048",
            "-validity", "10000",
            "-dname", "CN=Lampac APK, OU=Lampac, O=Lampac, L=Local, ST=Local, C=UA"
        }, SigningDir);

        RestrictFileToOwner(KeystorePath);
    }

    static string GetOrCreateKeyPassword()
    {
        lock (KeyPasswordLock)
        {
            if (!string.IsNullOrWhiteSpace(cachedKeyPassword))
                return cachedKeyPassword;

            Directory.CreateDirectory(SigningDir);

            if (System.IO.File.Exists(KeyPasswordPath))
            {
                cachedKeyPassword = System.IO.File.ReadAllText(KeyPasswordPath, Encoding.UTF8).Trim();
                if (string.IsNullOrWhiteSpace(cachedKeyPassword))
                    throw new InvalidOperationException("lampac-apk.pass is empty.");

                return cachedKeyPassword;
            }

            if (System.IO.File.Exists(KeystorePath))
            {
                cachedKeyPassword = LegacyKeyPassword;
                return cachedKeyPassword;
            }

            cachedKeyPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            System.IO.File.WriteAllText(KeyPasswordPath, cachedKeyPassword + Environment.NewLine, new UTF8Encoding(false));
            RestrictFileToOwner(KeyPasswordPath);

            return cachedKeyPassword;
        }
    }

    static void RestrictFileToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }

    static void TouchFile(string path)
    {
        try { System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }
    }

    static void PruneTempBuildDirs()
    {
        if (!Directory.Exists(CacheDir))
            return;

        DateTime minAllowed = DateTime.UtcNow - TempBuildMaxAge;

        foreach (string dir in Directory.EnumerateDirectories(CacheDir, "tmp-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < minAllowed)
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
    }

    static void PruneApkCache(string currentApk)
    {
        if (!Directory.Exists(CacheDir))
            return;

        List<FileInfo> files = Directory.EnumerateFiles(CacheDir, "*.apk", SearchOption.TopDirectoryOnly)
            .Where(IsGeneratedCacheApk)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => SamePath(file.FullName, currentApk) ? DateTime.MaxValue : file.LastWriteTimeUtc)
            .ToList();

        long totalBytes = files.Sum(file => file.Length);
        int keptFiles = 0;

        foreach (FileInfo file in files)
        {
            bool isCurrent = SamePath(file.FullName, currentApk);
            bool overLimit = keptFiles >= MaxCachedApkFiles || totalBytes > MaxCacheBytes;

            if (!isCurrent && overLimit)
            {
                try
                {
                    long length = file.Length;
                    file.Delete();
                    totalBytes -= length;
                }
                catch { }

                continue;
            }

            keptFiles++;
        }
    }

    static bool IsGeneratedCacheApk(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Length == 36
            && fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
            && fileName.Take(32).All(Uri.IsHexDigit);
    }

    static bool SamePath(string left, string right)
        => !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static string FindAndroidBuildTools()
    {
        foreach (string sdk in CandidateSdkDirs().Where(Directory.Exists))
        {
            string buildTools = Path.Combine(sdk, "build-tools");
            if (!Directory.Exists(buildTools))
                continue;

            string latest = Directory.GetDirectories(buildTools)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (latest != null)
                return latest;
        }

        return "";
    }

    static IEnumerable<string> CandidateSdkDirs()
    {
        string androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        string androidSdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(androidHome)) yield return androidHome;
        if (!string.IsNullOrWhiteSpace(androidSdkRoot)) yield return androidSdkRoot;
        if (!string.IsNullOrWhiteSpace(localAppData)) yield return Path.Combine(localAppData, "Android", "Sdk");
    }

    static string FindTool(string directory, params string[] names)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            foreach (string name in names)
            {
                string candidate = Path.Combine(directory, name);
                if (System.IO.File.Exists(candidate))
                    return candidate;
            }
        }

        foreach (string name in names)
        {
            string found = FindOnPath(name);
            if (found != null)
                return found;
        }

        return null;
    }

    static string FindOnPath(string fileName)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            try
            {
                string candidate = Path.Combine(dir.Trim(), fileName);
                if (System.IO.File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }

        return null;
    }

    static void RunTool(string tool, IReadOnlyList<string> args, string workDir)
    {
        ProcessStartInfo psi;

        if (OperatingSystem.IsWindows() && IsWindowsCommandScript(tool))
        {
            psi = new ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                Arguments = "/d /s /c \"" + string.Join(" ", new[] { tool }.Concat(args).Select(QuoteCmdArg)) + "\""
            };
        }
        else
        {
            psi = new ProcessStartInfo(tool)
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (string arg in args)
                psi.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(psi);
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(tool)} failed with exit code {process.ExitCode}: {stdout} {stderr}".Trim());
    }

    static bool IsWindowsCommandScript(string tool)
        => tool.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || tool.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

    static string QuoteCmdArg(string value)
        => "\"" + value.Replace("\"", "\"\"") + "\"";

    static string Sha256Hex(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
