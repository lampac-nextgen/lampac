using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using QRAuth.Models;
using QRAuth.Services;

namespace QRAuth
{
    public class ModInit : IModuleLoaded, IModuleConfigure
    {
        public static QRAuthBotConf conf = new();

        private static readonly string DenyPagePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "override", "deny.js");

        public static DenyPageConf denyConf = new();

        private static string _denyPageHash = "";
        private static Timer? _denyPageTimer;
        private static Timer? _posterTimer;
        private static readonly object _denyPageLock = new();

        public void Configure(ConfigureModel app)
        {
            SyncConf();
            app.services.AddHostedService<TelegramBotHostedService>();
        }

        public void Loaded(InitspaceModel initspace)
        {
            SyncConf();
            EventListener.UpdateInitFile += SyncConf;
            EventListener.Accsdb += AllowQrRoutes;

            Directory.CreateDirectory(Path.GetDirectoryName(DenyPagePath)!);
            SyncAndGenerateDenyPage();
            EventListener.UpdateInitFile += SyncAndGenerateDenyPage;
            _denyPageTimer = new Timer(_ => SyncAndGenerateDenyPage(), null,
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

            // First run delayed so the host's own /tmdb proxy is already listening; after
            // that a check every 10 minutes — RefreshAsync is a no-op while the set is < 24h
            // old, so this only matters for retrying quickly after a failed fetch.
            _posterTimer = new Timer(_ =>
            {
                if (denyConf.poster_wall)
                    _ = PosterWall.RefreshAsync(denyConf.poster_source ?? "trending");
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(10));
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= SyncConf;
            EventListener.UpdateInitFile -= SyncAndGenerateDenyPage;
            EventListener.Accsdb -= AllowQrRoutes;
            _denyPageTimer?.Dispose();
            _posterTimer?.Dispose();
        }

        /// <summary>
        /// /tgbot/qr/* (QR session start/status, poster wall) is called by the deny page,
        /// i.e. by a visitor who by definition is NOT authorized yet. Instead of relying on
        /// every deployment remembering to add "tgbot/qr" to accsdb.whitepattern (easy to
        /// miss — without it the QR login and the poster wall silently do nothing), the
        /// module lets its own routes through the same way LampaWeb does for /testaccsdb.
        /// Nothing here returns user data without a confirmed session, so this is safe.
        /// Note: accsdb 404s *.jpg/.png/... before these handlers run, hence the poster
        /// route has no file extension.
        /// </summary>
        static bool AllowQrRoutes(EventAccsdb e)
        {
            if (!e.httpContext.Request.Path.StartsWithSegments("/tgbot/qr", StringComparison.OrdinalIgnoreCase))
                return false;

            e.requestInfo.IsAnonymousRequest = true;
            return true;
        }

        static void SyncConf()
        {
            conf = ModuleInvoke.Init("QRAuthBot", DefaultConf());

            if (conf.enable && string.IsNullOrWhiteSpace(conf.bot_token))
                Console.WriteLine("[QRAuthBot] enable=true, но bot_token пустой — проверьте секцию QRAuthBot в init.conf.");
        }

        static QRAuthBotConf DefaultConf() => new()
        {
            enable = true,
            bot_token = "",
            users_file_path = "users.json",
            log_path = "tgbot.log",
            admin_ids = Array.Empty<long>()
        };

        static void SyncAndGenerateDenyPage()
        {
            lock (_denyPageLock)
            {
                try
                {
                    denyConf = ModuleInvoke.Init("DenyPage", new DenyPageConf());
                    if (denyConf.poster_wall)
                        PosterWall.LoadCached(denyConf.poster_source ?? "trending");
                    string content = DenyPageGenerator.Build(denyConf);
                    string hash = content.GetHashCode().ToString();
                    if (hash == _denyPageHash) return;
                    File.WriteAllText(DenyPagePath, content, System.Text.Encoding.UTF8);
                    _denyPageHash = hash;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DenyPage] {ex.Message}");
                }
            }
        }
    }
}
