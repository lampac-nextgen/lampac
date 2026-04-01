using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    public sealed class TelegramAuthBotHostedService : BackgroundService
    {
        const int GetUpdatesLimit = 100;
        const int GetUpdatesTimeoutSeconds = 50;
        static readonly TimeSpan GetUpdatesErrorDelay = TimeSpan.FromSeconds(5);

        public TelegramAuthBotHostedService()
        {
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await RunBotAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                TelegramAuthBotSerilog.Log.Error(ex, "CatchId={CatchId}", "id_tgauthbot_execute");
                throw;
            }
        }

        async Task RunBotAsync(CancellationToken stoppingToken)
        {
            var conf = ModInit.conf;
            if (!conf.enable || string.IsNullOrWhiteSpace(conf.bot_token))
            {
                var msg = "TelegramAuthBot: отключён (enable=false или пустой bot_token); long polling не запущен.";
                Console.WriteLine(msg);
                TelegramAuthBotSerilog.Log.Information("{Message}", msg);
                return;
            }

            using var api = new LampacTelegramAuthHttpClient(conf);
            var bot = new TelegramBotClient(conf.bot_token.Trim());
            var displayName = conf.service_display_name ?? "Lampac NextGen Bot";

            try
            {
                var me = await bot.GetMe(stoppingToken).ConfigureAwait(false);
                var line = $"TelegramAuthBot: @{me.Username} — Telegram API OK; TelegramAuth HTTP {conf.lampac_base_url}";
                Console.WriteLine(line);
                TelegramAuthBotSerilog.Log.Information("Telegram OK @{BotUsername}; TelegramAuth {BaseUrl}", me.Username, conf.lampac_base_url);
            }
            catch (Exception ex)
            {
                Console.WriteLine("TelegramAuthBot: GetMe не удался (токен или сеть до api.telegram.org). " + ex.Message);
                TelegramAuthBotSerilog.Log.Error(ex, "CatchId={CatchId}", "id_tgauthbot_getme");
                return;
            }

            try
            {
                await bot.DeleteWebhook(dropPendingUpdates: false, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TelegramAuthBotSerilog.Log.Error(ex, "CatchId={CatchId}", "id_tgauthbot_deletewebhook");
            }

            var session = new TelegramAuthBotSession(api, displayName);
            var pollMsg =
                $"TelegramAuthBot: long polling GetUpdates (limit {GetUpdatesLimit}, timeout {GetUpdatesTimeoutSeconds}s).";
            Console.WriteLine(pollMsg);
            TelegramAuthBotSerilog.Log.Information(
                "Long polling GetUpdates limit {Limit} timeout {TimeoutSeconds}s",
                GetUpdatesLimit, GetUpdatesTimeoutSeconds);

            await PollUpdatesAsync(bot, session, stoppingToken).ConfigureAwait(false);
        }

        async Task PollUpdatesAsync(ITelegramBotClient bot, TelegramAuthBotSession session, CancellationToken stoppingToken)
        {
            int? offset = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                Update[] updates;
                try
                {
                    updates = await bot.GetUpdates(
                        offset,
                        limit: GetUpdatesLimit,
                        timeout: GetUpdatesTimeoutSeconds,
                        allowedUpdates: null,
                        cancellationToken: stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TelegramAuthBotSerilog.Log.Error(ex, "CatchId={CatchId}, DelaySeconds={Delay}", "id_tgauthbot_getupdates", GetUpdatesErrorDelay.TotalSeconds);
                    try
                    {
                        await Task.Delay(GetUpdatesErrorDelay, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    try
                    {
                        await session.HandleUpdateAsync(bot, update, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        TelegramAuthBotSerilog.Log.Error(ex, "CatchId={CatchId}, UpdateId={UpdateId}", "id_tgauthbot_update", update.Id);
                    }
                }
            }
        }
    }
}
