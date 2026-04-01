using Telegram.Bot.Types;
using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    sealed partial class TelegramAuthBotSession
    {
        static readonly Regex UidRe = new(@"^[a-zA-Z0-9_-]{6,20}$", RegexOptions.Compiled);
        static readonly Regex StartCommandRe = new(@"^/start(?:@\w+)?(?:\s+(\S+))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex AdminUserDetailRe = new(@"^/user(?:@\w+)?\s+(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex AdminSetUserRe = new(@"^/setuser(?:@\w+)?\s+(\d+)\s+([a-zA-Z_]+)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        const string BtnStatus = "👤 Мой статус";
        const string BtnDevices = "📱 Мои устройства";
        const string BtnHelp = "❓ Помощь";

        const int AdminUsersPageSize = 8;
        const string CbUsersPage = "ulp:";
        const string CbDisableUser = "d|";
        const string CbEnableUser = "e|";
        const string CbApprovePending = "ap|";
        const string CbRejectPending = "rp|";
        const string CbReactivateDevice = "react:";

        readonly LampacTelegramAuthHttpClient _api;
        readonly string _displayName;
        int _firstUpdateLogged;

        public TelegramAuthBotSession(LampacTelegramAuthHttpClient api, string displayName)
        {
            _api = api;
            _displayName = displayName;
        }

        public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref _firstUpdateLogged, 1, 0) == 0)
                TelegramAuthBotSerilog.Log.Information("Первый апдейт {UpdateId}", update.Id);

            if (update.CallbackQuery is { } cq)
            {
                await HandleCallbackAsync(bot, cq, ct).ConfigureAwait(false);
                return;
            }

            var msg = update.Message ?? update.EditedMessage;
            if (msg is not { } m)
                return;

            var text = MessageTextOrCaption(m);
            if (string.IsNullOrEmpty(text))
                return;

            if (!TryResolveTelegramUserId(m, out var tgId))
            {
                TelegramAuthBotSerilog.Log.Warning("Не удалось определить telegram user id UpdateId={UpdateId} ChatType={ChatType}", update.Id, m.Chat.Type);
                await bot.SendMessage(m.Chat.Id,
                    "Не могу определить твой Telegram ID в этом чате. Напиши боту в личные сообщения.",
                    cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            await HandleMessageAsync(bot, m, text, tgId, ct).ConfigureAwait(false);
        }

        static string MessageTextOrCaption(Message msg)
        {
            if (!string.IsNullOrEmpty(msg.Text))
                return msg.Text;
            if (!string.IsNullOrEmpty(msg.Caption))
                return msg.Caption;
            return null;
        }

        static bool TryResolveTelegramUserId(Message msg, out string tgId)
        {
            if (msg.From != null)
            {
                tgId = msg.From.Id.ToString();
                return true;
            }

            if (msg.Chat.Type == ChatType.Private)
            {
                tgId = msg.Chat.Id.ToString();
                return true;
            }

            tgId = "";
            return false;
        }

        static ReplyKeyboardMarkup MainMenuKeyboard() =>
            new(new[]
            {
                new KeyboardButton[] { new(BtnStatus), new(BtnDevices) },
                new KeyboardButton[] { new(BtnHelp) }
            })
            {
                ResizeKeyboard = true
            };

        async Task SendStartText(ITelegramBotClient bot, ChatId chatId, CancellationToken ct)
        {
            var name = _displayName;
            var text =
                $"✨ <b>Привет. Я бот авторизации {EscapeHtml(name)}.</b>\n\n" +
                $"С моей помощью можно быстро войти в {EscapeHtml(name)} через Telegram.\n\n" +
                "<b>Что я умею:</b>\n" +
                "• 🔗 привязать устройство по UID\n" +
                "• 👤 показать твой статус\n" +
                "• 📱 показать список устройств и переименовать (<code>/devicename</code>)\n" +
                "• 🗑️ отвязать устройство кнопкой\n\n" +
                "<b>Как войти:</b>\n" +
                $"1. Открой {EscapeHtml(name)}\n" +
                "2. Скопируй UID с экрана авторизации\n" +
                "3. Отправь его мне\n" +
                "4. Вернись в " + EscapeHtml(name) + " и нажми <b>«Проверить снова»</b>\n\n" +
                "Или просто используй кнопки ниже 👇";
            await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
        }

        async Task HandleMessageAsync(ITelegramBotClient bot, Message msg, string text, string tgId, CancellationToken ct)
        {
            var chatId = msg.Chat.Id;
            var username = msg.From?.Username ?? "";

            if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
            {
                var m = StartCommandRe.Match(text.Trim());
                var deepUid = m.Success && m.Groups[1].Success ? m.Groups[1].Value.Trim() : "";
                if (!string.IsNullOrEmpty(deepUid) && UidRe.IsMatch(deepUid))
                {
                    var handled = await TryBindAsync(bot, chatId, tgId, username, deepUid, fromStartDeepLink: true, ct).ConfigureAwait(false);
                    if (handled)
                        return;
                }

                await SendStartText(bot, chatId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/help"))
            {
                await CmdHelpAsync(bot, chatId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/me"))
            {
                await CmdMeAsync(bot, chatId, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/devices"))
            {
                await CmdDevicesAsync(bot, chatId, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/devicename"))
            {
                await CmdDeviceNameAsync(bot, chatId, tgId, text, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/import"))
            {
                await CmdImportAsync(bot, msg.Chat, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/cleanup"))
            {
                await CmdCleanupAsync(bot, msg.Chat, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/users"))
            {
                await CmdUsersAsync(bot, msg.Chat, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/user"))
            {
                await CmdAdminUserDetailAsync(bot, msg.Chat, tgId, text, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/setuser"))
            {
                await CmdAdminSetUserAsync(bot, msg.Chat, tgId, text, ct).ConfigureAwait(false);
                return;
            }

            var trimmed = text.Trim();
            if (trimmed == BtnStatus)
            {
                await CmdMeAsync(bot, chatId, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (trimmed == BtnDevices)
            {
                await CmdDevicesAsync(bot, chatId, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (trimmed == BtnHelp)
            {
                await CmdHelpAsync(bot, chatId, ct).ConfigureAwait(false);
                return;
            }

            if (trimmed.StartsWith('/'))
                return;

            if (UidRe.IsMatch(trimmed))
            {
                await TryBindAsync(bot, chatId, tgId, username, trimmed, fromStartDeepLink: false, ct).ConfigureAwait(false);
                return;
            }

            var name = _displayName;
            await bot.SendMessage(chatId,
                $"Я жду UID устройства из {EscapeHtml(name)} или используй кнопки ниже 👇",
                parseMode: ParseMode.Html,
                replyMarkup: MainMenuKeyboard(),
                cancellationToken: ct).ConfigureAwait(false);
        }

        static bool IsCommand(string text, string command)
        {
            var t = text.Trim();
            if (!t.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                return false;
            if (t.Length == command.Length)
                return true;
            var c = t[command.Length];
            return c == ' ' || c == '@';
        }
    }
}
