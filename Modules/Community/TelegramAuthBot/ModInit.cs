using Microsoft.Extensions.DependencyInjection;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using TelegramAuthBot.Models;
using TelegramAuthBot.Services;

namespace TelegramAuthBot
{
    public class ModInit : IModuleLoaded, IModuleConfigure
    {
        public static TelegramAuthBotConf conf = new();

        public void Configure(ConfigureModel app)
        {
            SyncConfFromInit();
            app.services.AddHostedService<TelegramAuthBotHostedService>();
        }

        public void Loaded(InitspaceModel initspace)
        {
            SyncConfFromInit();
            EventListener.UpdateInitFile += SyncConfFromInit;
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= SyncConfFromInit;
        }

        static void SyncConfFromInit()
        {
            conf = ModuleInvoke.Init("TelegramAuthBot", DefaultConfTemplate());

            if (conf.enable && string.IsNullOrWhiteSpace(conf.bot_token))
            {
                var msg = "TelegramAuthBot: enable=true, но bot_token пустой — проверьте секцию TelegramAuthBot в init.conf рядом с Core.dll.";
                Console.WriteLine(msg);
                TelegramAuthBotSerilog.Log.Warning("{Message}", msg);
            }
        }

        static TelegramAuthBotConf DefaultConfTemplate() =>
            new()
            {
                enable = true,
                bot_token = "",
                lampac_base_url = "http://127.0.0.1:9118",
                request_timeout_sec = 10,
                service_display_name = "Lampac NextGen Bot",
                lampac_accspasswd = ""
            };
    }
}
