using Telegram.Bot.Types;
using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    sealed class TelegramAuthBotSession
    {
        static readonly Regex UidRe = new(@"^[a-zA-Z0-9_-]{6,20}$", RegexOptions.Compiled);
        static readonly Regex StartCommandRe = new(@"^/start(?:@\w+)?(?:\s+(\S+))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        const string BtnStatus = "👤 Мой статус";
        const string BtnDevices = "📱 Мои устройства";
        const string BtnHelp = "❓ Помощь";

        readonly TelegramAuthApiClient _api;
        readonly string _displayName;
        int _firstUpdateLogged;

        public TelegramAuthBotSession(TelegramAuthApiClient api, string displayName)
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
                "• 📱 показать список устройств\n" +
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

            if (IsCommand(text, "/import"))
            {
                await CmdImportAsync(bot, chatId, tgId, ct).ConfigureAwait(false);
                return;
            }

            if (IsCommand(text, "/cleanup"))
            {
                await CmdCleanupAsync(bot, chatId, tgId, ct).ConfigureAwait(false);
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

        async Task<bool> TryBindAsync(ITelegramBotClient bot, ChatId chatId, string tgId, string username, string uid, bool fromStartDeepLink, CancellationToken ct)
        {
            var name = _displayName;
            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (user != null && user.found && !user.active)
            {
                await bot.SendMessage(chatId, "Твой доступ истёк или отключён.", replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
                return true;
            }

            var ok = await _api.BindCompleteAsync(uid, tgId, username, ct).ConfigureAwait(false);
            if (ok)
            {
                var extra = fromStartDeepLink
                    ? ""
                    : "\n\n💡 Подсказка: кнопкой <b>📱 Мои устройства</b> можно посмотреть и отвязать устройства.";
                await bot.SendMessage(chatId,
                    $"✅ <b>Устройство привязано</b>\n\n<code>{EscapeHtml(uid)}</code>\n\n" +
                    $"Вернись в {EscapeHtml(name)} и нажми <b>«Проверить снова»</b>.{extra}",
                    parseMode: ParseMode.Html,
                    replyMarkup: MainMenuKeyboard(),
                    cancellationToken: ct).ConfigureAwait(false);
                return true;
            }

            if (fromStartDeepLink)
                return false;

            if (user != null && user.found)
                await bot.SendMessage(chatId, "Не удалось привязать устройство.", replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
            else
                await bot.SendMessage(chatId, $"Тебя нет в базе {EscapeHtml(name)}. Обратись к администратору.", parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
            return true;
        }

        async Task CmdMeAsync(ITelegramBotClient bot, ChatId chatId, string tgId, CancellationToken ct)
        {
            var name = _displayName;
            var data = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (data == null || !data.found)
            {
                await bot.SendMessage(chatId, $"Тебя нет в базе авторизации {EscapeHtml(name)}.", parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var maxDev = data.maxDevices == -1 ? "∞" : data.maxDevices.ToString();
            var expires = string.IsNullOrEmpty(data.expiresAt) ? "-" : data.expiresAt;
            var text =
                $"<b>Профиль {EscapeHtml(name)}</b>\n\n" +
                $"<b>Пользователь:</b> @{EscapeHtml(data.username ?? "-")}\n" +
                $"<b>Telegram ID:</b> <code>{EscapeHtml(data.telegramId ?? tgId)}</code>\n" +
                $"<b>Роль:</b> {EscapeHtml(data.role ?? "-")}\n" +
                $"<b>Язык:</b> {EscapeHtml(data.lang ?? "-")}\n" +
                $"<b>Активен:</b> {(data.active ? "да" : "нет")}\n" +
                $"<b>Срок доступа:</b> {EscapeHtml(expires)}\n" +
                $"<b>Устройств:</b> {data.deviceCount} / {maxDev}";
            await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
        }

        async Task CmdDevicesAsync(ITelegramBotClient bot, ChatId chatId, string tgId, CancellationToken ct)
        {
            var data = await _api.GetDevicesAsync(tgId, ct).ConfigureAwait(false);
            var devices = data?.devices ?? new List<DeviceDto>();
            if (devices.Count == 0)
            {
                await bot.SendMessage(chatId, "У тебя пока нет привязанных устройств.", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var lines = new List<string> { $"<b>Устройства @{EscapeHtml(data!.username ?? tgId)}:</b>" };
            var keyboard = new List<InlineKeyboardButton[]>();
            foreach (var d in devices)
            {
                var uid = d.uid ?? "";
                var devName = string.IsNullOrEmpty(d.name) ? "без имени" : d.name;
                var state = d.active ? "активно" : "отключено";
                lines.Add($"• <code>{EscapeHtml(uid)}</code> — {EscapeHtml(devName)} ({state})");
                if (!string.IsNullOrEmpty(uid) && d.active)
                    keyboard.Add(new[] { InlineKeyboardButton.WithCallbackData($"Отвязать {devName}", "unbind:" + uid) });
            }

            var markup = keyboard.Count > 0 ? new InlineKeyboardMarkup(keyboard) : null;
            await bot.SendMessage(chatId, string.Join("\n", lines), parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct).ConfigureAwait(false);
        }

        static bool IsTelegramAuthAdmin(UserByTelegramDto user) =>
            user != null && user.found && string.Equals(user.role, "admin", StringComparison.OrdinalIgnoreCase);

        static bool IsAllowedAdminCommandChat(TelegramAuthBotConf conf, long? chatNumericId)
        {
            var ids = conf.admin_chat_ids;
            if (ids == null || ids.Length == 0)
                return true;
            return chatNumericId.HasValue && ids.Contains(chatNumericId.Value);
        }

        async Task<bool> TryEnsureAdminMutationAccessAsync(ITelegramBotClient bot, ChatId chatId, string tgId, CancellationToken ct)
        {
            var conf = ModInit.conf;
            if (string.IsNullOrEmpty(conf.mutations_api_secret))
            {
                await bot.SendMessage(chatId,
                    "В конфиге бота не задан <code>mutations_api_secret</code> — тот же секрет, что <code>TelegramAuth.mutations_api_secret</code> в init.conf.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            if (!IsAllowedAdminCommandChat(conf, chatId.Identifier))
            {
                await bot.SendMessage(chatId,
                    "Админ-команды с этого чата запрещены. Используй чат из списка <code>admin_chat_ids</code> в конфиге бота.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (!IsTelegramAuthAdmin(user))
            {
                await bot.SendMessage(chatId,
                    "Команда только для администраторов (роль admin в базе TelegramAuth).",
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            return true;
        }

        async Task CmdImportAsync(ITelegramBotClient bot, ChatId chatId, string tgId, CancellationToken ct)
        {
            if (!await TryEnsureAdminMutationAccessAsync(bot, chatId, tgId, ct).ConfigureAwait(false))
                return;

            await bot.SendMessage(chatId, "⏳ Запускаю импорт…", cancellationToken: ct).ConfigureAwait(false);
            var (ok, detail) = await _api.ImportLegacyAsync(ct).ConfigureAwait(false);
            if (ok)
            {
                try
                {
                    var jo = JObject.Parse(detail);
                    var msg =
                        "✅ Импорт завершён.\n" +
                        $"Пользователей: {jo.Value<int?>("importedUsers") ?? 0}, устройств: {jo.Value<int?>("importedDevices") ?? 0}, админов: {jo.Value<int?>("importedAdmins") ?? 0}, языков: {jo.Value<int?>("importedLangs") ?? 0}";
                    await bot.SendMessage(chatId, msg, cancellationToken: ct).ConfigureAwait(false);
                }
                catch
                {
                    await bot.SendMessage(chatId, "✅ Импорт завершён.\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
                }
            }
            else
            {
                await bot.SendMessage(chatId, "❌ Ошибка импорта:\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
            }
        }

        async Task CmdCleanupAsync(ITelegramBotClient bot, ChatId chatId, string tgId, CancellationToken ct)
        {
            if (!await TryEnsureAdminMutationAccessAsync(bot, chatId, tgId, ct).ConfigureAwait(false))
                return;

            await bot.SendMessage(chatId, "⏳ Очистка неактивных устройств…", cancellationToken: ct).ConfigureAwait(false);
            var (ok, detail) = await _api.CleanupDevicesAsync(ct).ConfigureAwait(false);
            if (ok)
            {
                try
                {
                    var jo = JObject.Parse(detail);
                    var removed = jo.Value<int?>("removed") ?? 0;
                    await bot.SendMessage(chatId, $"✅ Готово. Удалено записей устройств: {removed}", cancellationToken: ct).ConfigureAwait(false);
                }
                catch
                {
                    await bot.SendMessage(chatId, "✅ Готово.\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
                }
            }
            else
            {
                await bot.SendMessage(chatId, "❌ Ошибка очистки:\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
            }
        }

        static string TruncateForTelegram(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen)
                return s ?? "";
            return s.Substring(0, maxLen) + "…";
        }

        async Task CmdHelpAsync(ITelegramBotClient bot, ChatId chatId, CancellationToken ct)
        {
            var name = _displayName;
            var conf = ModInit.conf;
            var text =
                $"❓ <b>Помощь по входу в {EscapeHtml(name)}</b>\n\n" +
                "<b>Быстрый вход:</b>\n" +
                $"1. Открой {EscapeHtml(name)} и дойди до экрана авторизации\n" +
                "2. Скопируй UID устройства\n" +
                "3. Отправь UID мне сюда\n" +
                "4. Вернись в " + EscapeHtml(name) + " и нажми <b>«Проверить снова»</b>\n\n" +
                "<b>Кнопки:</b>\n" +
                "👤 Мой статус — профиль и срок доступа\n" +
                "📱 Мои устройства — список устройств и кнопки отвязки\n" +
                "❓ Помощь — эта подсказка\n\n" +
                "<b>Админ (роль admin):</b> <code>/import</code>, <code>/cleanup</code> — нужен <code>mutations_api_secret</code> в конфиге бота и в TelegramAuth." +
                (conf.admin_chat_ids != null && conf.admin_chat_ids.Length > 0
                    ? "\n\nАдмин-команды разрешены только в чатах из <code>admin_chat_ids</code>."
                    : "");
            await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
        }

        async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
        {
            await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);

            var data = cq.Data ?? "";
            if (!data.StartsWith("unbind:", StringComparison.Ordinal))
                return;

            var uid = data.Length > 7 ? data.Substring(7) : "";
            var tgId = cq.From.Id.ToString();
            var name = _displayName;

            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (user == null || !user.found)
            {
                await bot.EditMessageText(cq.Message!.Chat.Id, cq.Message.MessageId, $"Тебя нет в базе {name}.", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var devicesResp = await _api.GetDevicesAsync(tgId, ct).ConfigureAwait(false);
            var devices = devicesResp?.devices ?? new List<DeviceDto>();
            var uids = devices.Select(d => d.uid).Where(u => !string.IsNullOrEmpty(u)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!uids.Contains(uid))
            {
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, "Это устройство не принадлежит тебе.", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var ok = await _api.UnbindDeviceAsync(uid, ct).ConfigureAwait(false);
            if (ok)
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, $"Устройство {uid} отвязано.", cancellationToken: ct).ConfigureAwait(false);
            else
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, "Не удалось отвязать устройство.", cancellationToken: ct).ConfigureAwait(false);
        }

        static string EscapeHtml(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }
    }
}
