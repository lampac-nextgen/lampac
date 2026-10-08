using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OidcAuth.Models;
using Shared;
using Shared.Models.AppConf;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;

namespace OidcAuth
{
    public class ModInit : IModuleLoaded
    {
        public static OidcAuthConf conf = new();

        public static string? modpath;

        /// <summary>Module asset installed through LampaWeb's existing deny.js override.</summary>
        public const string DenyAssetPath = "plugins/deny.js";
        const string ManagedHeader = "/* OIDC access screen.";
        // FileCache resolves plugins/override relative to the process working directory.
        static readonly string OverridePath = Path.GetFullPath(
            Path.Combine("plugins", "override", "deny.js"));
        static readonly string BackupPath = OverridePath + ".before-oidc";
        static readonly object _denySync = new();
        static Timer? _denyTimer;

        public void Loaded(InitspaceModel initspace)
        {
            modpath = initspace.path;

            UpdateConf();
            EventListener.UpdateInitFile += UpdateConf;
            EventListener.Accsdb += accsdbEvent;
            _denyTimer = new Timer(_ => SyncDenyScript(), null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= UpdateConf;
            EventListener.Accsdb -= accsdbEvent;
            _denyTimer?.Dispose();
        }

        public static bool DenyInstalled()
        {
            try
            {
                if (!conf.enable || !conf.overrideDeny || string.IsNullOrEmpty(modpath))
                    return false;

                string source = Path.Combine(modpath, DenyAssetPath);
                return File.Exists(source) && File.Exists(OverridePath) &&
                    File.ReadAllText(source) == File.ReadAllText(OverridePath);
            }
            catch { return false; }
        }

        static void SyncDenyScript()
        {
            lock (_denySync)
            {
                try
                {
                    bool managed = File.Exists(OverridePath) &&
                        File.ReadAllText(OverridePath).StartsWith(ManagedHeader, StringComparison.Ordinal);

                    if (!conf.enable || !conf.overrideDeny)
                    {
                        if (managed)
                        {
                            if (File.Exists(BackupPath))
                            {
                                File.Copy(BackupPath, OverridePath, true);
                                File.Delete(BackupPath);
                            }
                            else
                                File.Delete(OverridePath);
                        }
                        return;
                    }

                    if (string.IsNullOrEmpty(modpath))
                        return;

                    string source = Path.Combine(modpath, DenyAssetPath);
                    if (!File.Exists(source))
                    {
                        Console.WriteLine($"OidcAuth: deny.js asset missing: {source}");
                        return;
                    }

                    string content = File.ReadAllText(source);
                    if (File.Exists(OverridePath) && File.ReadAllText(OverridePath) == content)
                        return;

                    Directory.CreateDirectory(Path.GetDirectoryName(OverridePath)!);
                    if (File.Exists(OverridePath) && !managed)
                        File.Copy(OverridePath, BackupPath, true);

                    File.WriteAllText(OverridePath, content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"OidcAuth: cannot install deny.js override: {ex.Message}");
                }
            }
        }

        void UpdateConf()
        {
            conf = ModuleInvoke.Init("OidcAuth", new OidcAuthConf
            {
                enable = false,
                overrideDeny = true,
                name = "OIDC",
                authority = "",
                publicUrl = "",
                clientId = "",
                clientSecret = "",
                scopes = "openid profile email",
                defaultGroup = 0,
                defaultExpiresDays = 0,
                unlinkedLog = true,
                limit_map = new List<WafLimitRootMap>
                {
                    new("^/oidc/", new WafLimitMap { limit = 10, second = 1 })
                }
            });

            if (CoreInit.conf != null && conf.enable)
                CoreInit.conf.accsdb.enable = true;

            if (conf.enable)
                ApplyWafLimitMapFromConf();

            SyncDenyScript();
        }

        /// <summary>Module routes must pass the accsdb gate: /oidc/* carries no known user uid.</summary>
        bool accsdbEvent(EventAccsdb e)
        {
            if (conf.enable &&
                e.httpContext?.Request.Path.Value?.StartsWith("/oidc/", StringComparison.OrdinalIgnoreCase) == true)
            {
                e.requestInfo.IsAnonymousRequest = true;
                return true;
            }

            return false;
        }

        void ApplyWafLimitMapFromConf()
        {
            var waf = CoreInit.conf?.WAF?.limit_map;
            if (waf == null)
                return;

            var ours = conf.limit_map ?? new List<WafLimitRootMap>();
            var patterns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in ours)
            {
                if (!string.IsNullOrEmpty(m?.pattern))
                    patterns.Add(m.pattern);
            }

            if (patterns.Count == 0)
                return;

            waf.RemoveAll(x => x != null && !string.IsNullOrEmpty(x.pattern) && patterns.Contains(x.pattern));
            foreach (var m in ours)
                waf.Insert(0, m);
        }
    }
}
