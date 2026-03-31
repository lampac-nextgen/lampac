namespace TelegramAuthBot.Services
{
    internal static class TelegramAuthBotSerilog
    {
        public static readonly Serilog.ILogger Log =
            Serilog.Log.ForContext("SourceContext", "TelegramAuthBot");
    }
}
