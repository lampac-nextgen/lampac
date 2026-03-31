using Newtonsoft.Json;
using Telegram.Bot.Types;
using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    sealed class TelegramAuthBotSession
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

        async Task NotifyAdminsOfPendingProvisionAsync(ITelegramBotClient bot, string newUserTgId, string username, string deviceUid, CancellationToken ct)
        {
            var conf = ModInit.conf;
            if (!conf.notify_admins_on_pending_provision)
                return;

            if (string.IsNullOrEmpty(conf.mutations_api_secret?.Trim()))
            {
                TelegramAuthBotSerilog.Log.Warning(
                    "Новый пользователь ожидает активации (TelegramId={TgId}), mutations_api_secret пуст — уведомления админам не отправлены.",
                    newUserTgId);
                return;
            }

            AdminUsersListResponseDto data;
            try
            {
                data = await _api.GetAdminUsersAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TelegramAuthBotSerilog.Log.Warning(ex, "Не удалось загрузить список пользователей для уведомления о новом аккаунте");
                return;
            }

            if (data?.users == null || data.users.Count == 0)
                return;

            var admins = data.users
                .Where(u => string.Equals(u.role, "admin", StringComparison.OrdinalIgnoreCase))
                .Where(u => !string.IsNullOrWhiteSpace(u.telegramId))
                .ToList();

            if (admins.Count == 0)
            {
                TelegramAuthBotSerilog.Log.Warning(
                    "Новый пользователь ожидает активации (TelegramId={TgId}), в базе нет пользователей с ролью admin.",
                    newUserTgId);
                return;
            }

            var at = string.IsNullOrEmpty(username) ? "—" : "@" + EscapeHtml(username);
            var text =
                "🔔 <b>Новый пользователь (ожидает решения)</b>\n\n" +
                $"<b>Telegram ID:</b> <code>{EscapeHtml(newUserTgId)}</code>\n" +
                $"<b>Username:</b> {at}\n" +
                $"<b>UID устройства:</b> <code>{EscapeHtml(deviceUid)}</code>\n\n" +
                "<b>Подтвердить</b> — включить доступ. <b>Отклонить</b> — удалить запись из базы.";

            var cbApprove = CbApprovePending + "0|" + newUserTgId;
            var cbReject = CbRejectPending + "0|" + newUserTgId;
            InlineKeyboardMarkup notifyKb = null;
            if (Encoding.UTF8.GetByteCount(cbApprove) <= 64 && Encoding.UTF8.GetByteCount(cbReject) <= 64)
            {
                notifyKb = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("✅ Принять", cbApprove),
                        InlineKeyboardButton.WithCallbackData("❌ Отклонить", cbReject)
                    }
                });
            }

            foreach (var a in admins)
            {
                if (!long.TryParse(a.telegramId.Trim(), out var adminChatId))
                    continue;
                try
                {
                    await bot.SendMessage(new ChatId(adminChatId), text, parseMode: ParseMode.Html, replyMarkup: notifyKb, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TelegramAuthBotSerilog.Log.Warning(ex, "Не удалось отправить уведомление администратору {AdminTgId}", a.telegramId);
                }
            }
        }

        async Task<bool> TryBindAsync(ITelegramBotClient bot, ChatId chatId, string tgId, string username, string uid, bool fromStartDeepLink, CancellationToken ct)
        {
            var name = _displayName;
            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (user != null && user.found && !user.active)
            {
                string blocked;
                if (user.registrationPending)
                    blocked = "Аккаунт ожидает подтверждения администратора. Когда доступ подтвердят, нажми «Проверить снова» в приложении.";
                else if (user.disabled)
                    blocked = "Доступ отключён администратором. Если только что отправил UID, дождись включения и снова нажми «Проверить снова» в приложении.";
                else
                    blocked = "Твой доступ истёк.";
                await bot.SendMessage(chatId, blocked, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
                return true;
            }

            var bind = await _api.BindCompleteAsync(uid, tgId, username, ct).ConfigureAwait(false);
            if (bind.Ok)
            {
                if (bind.PendingAdminApproval)
                    await NotifyAdminsOfPendingProvisionAsync(bot, tgId, username, uid, ct).ConfigureAwait(false);

                string userText;
                if (bind.PendingAdminApproval)
                {
                    userText =
                        $"✅ <b>Запрос принят</b>\n\n<code>{EscapeHtml(uid)}</code>\n\n" +
                        "Аккаунт создан и <b>ждёт подтверждения</b> администратора (уведомление с кнопками «Принять» / «Отклонить» или список <code>/users</code>).\n\n" +
                        $"После подтверждения вернись в {EscapeHtml(name)} и нажми <b>«Проверить снова»</b>.";
                }
                else
                {
                    var extra = fromStartDeepLink
                        ? ""
                        : "\n\n💡 Подсказка: кнопкой <b>📱 Мои устройства</b> можно посмотреть и отвязать устройства.";
                    userText =
                        $"✅ <b>Устройство привязано</b>\n\n<code>{EscapeHtml(uid)}</code>\n\n" +
                        $"Вернись в {EscapeHtml(name)} и нажми <b>«Проверить снова»</b>.{extra}";
                }

                await bot.SendMessage(chatId, userText, parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
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
            var accessNote = data.registrationPending
                ? " (ожидает подтверждения)"
                : data.disabled ? " (отключён администратором)" : "";
            var text =
                $"<b>Профиль {EscapeHtml(name)}</b>\n\n" +
                $"<b>Пользователь:</b> @{EscapeHtml(data.username ?? "-")}\n" +
                $"<b>Telegram ID:</b> <code>{EscapeHtml(data.telegramId ?? tgId)}</code>\n" +
                $"<b>Роль:</b> {EscapeHtml(data.role ?? "-")}\n" +
                $"<b>Язык:</b> {EscapeHtml(data.lang ?? "-")}\n" +
                $"<b>Активен:</b> {(data.active ? "да" : "нет")}{accessNote}\n" +
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
                else if (!string.IsNullOrEmpty(uid) && !d.active)
                    keyboard.Add(new[] { InlineKeyboardButton.WithCallbackData($"✅ Включить · {devName}", CbReactivateDevice + uid) });
            }

            var markup = keyboard.Count > 0 ? new InlineKeyboardMarkup(keyboard) : null;
            await bot.SendMessage(chatId, string.Join("\n", lines), parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct).ConfigureAwait(false);
        }

        async Task CmdDeviceNameAsync(ITelegramBotClient bot, ChatId chatId, string tgId, string text, CancellationToken ct)
        {
            var t = text.Trim();
            var cmdLen = "/devicename".Length;
            if (t.Length > cmdLen && t[cmdLen] == '@')
            {
                var at = t.IndexOf(' ', cmdLen);
                if (at < 0)
                {
                    await bot.SendMessage(chatId,
                        "<b>Переименование устройства</b>\n\n<code>/devicename &lt;uid&gt; &lt;имя&gt;</code>\n\nUID из «Мои устройства». Чтобы сбросить имя: вместо имени отправь <code>-</code>.",
                        parseMode: ParseMode.Html,
                        replyMarkup: MainMenuKeyboard(),
                        cancellationToken: ct).ConfigureAwait(false);
                    return;
                }

                cmdLen = at;
            }

            var args = t.Length > cmdLen ? t.Substring(cmdLen).TrimStart() : "";
            if (string.IsNullOrEmpty(args))
            {
                await bot.SendMessage(chatId,
                    "<b>Переименование устройства</b>\n\n<code>/devicename &lt;uid&gt; &lt;имя&gt;</code>\n\nПример: <code>/devicename abc12xyz Телевизор в зале</code>\nСброс имени: <code>/devicename abc12xyz -</code>",
                    parseMode: ParseMode.Html,
                    replyMarkup: MainMenuKeyboard(),
                    cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var firstSpace = args.IndexOf(' ');
            if (firstSpace < 0)
            {
                await bot.SendMessage(chatId, "Нужны UID и имя. Пример: <code>/devicename abc12xyz Телевизор</code>", parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var uid = args.Substring(0, firstSpace).Trim();
            var newName = args.Substring(firstSpace + 1).Trim();
            if (string.IsNullOrEmpty(newName))
            {
                await bot.SendMessage(chatId, "Укажи новое имя после UID или <code>-</code> для сброса.", parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            if (!UidRe.IsMatch(uid))
            {
                await bot.SendMessage(chatId, "Некорректный UID.", replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var devicesResp = await _api.GetDevicesAsync(tgId, ct).ConfigureAwait(false);
            var devices = devicesResp?.devices ?? new List<DeviceDto>();
            var owned = devices.Any(d =>
                d != null
                && d.active
                && !string.IsNullOrEmpty(d.uid)
                && string.Equals(d.uid, uid, StringComparison.OrdinalIgnoreCase));
            if (!owned)
            {
                await bot.SendMessage(chatId, "Активного устройства с таким UID у тебя нет. Список — в «Мои устройства».", replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            string apiName = string.Equals(newName, "-", StringComparison.Ordinal) ? null : newName;
            var (ok, detail) = await _api.SetDeviceDisplayNameAsync(uid, apiName, ct).ConfigureAwait(false);
            if (ok)
            {
                var shown = apiName == null ? "сброшено (без имени)" : $"«{EscapeHtml(apiName)}»";
                await bot.SendMessage(chatId, $"✅ Имя устройства <code>{EscapeHtml(uid)}</code> — {shown}.", parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                await bot.SendMessage(chatId,
                    "❌ Не удалось сохранить имя. Если доступ истёк или устройство неактивно — привяжи снова.\n" + TruncateForTelegram(detail, 500),
                    replyMarkup: MainMenuKeyboard(),
                    cancellationToken: ct).ConfigureAwait(false);
            }
        }

        static bool IsTelegramAuthAdmin(UserByTelegramDto user) =>
            user != null && user.found && string.Equals(user.role, "admin", StringComparison.OrdinalIgnoreCase);

        static bool IsAllowedAdminCommandContext(TelegramAuthBotConf conf, ChatType chatType, long chatId, long actorTelegramUserId)
        {
            var ids = conf.admin_chat_ids;
            if (ids == null || ids.Length == 0)
                return true;

            if (ids.Contains(chatId))
                return true;

            if (chatType == ChatType.Private)
            {
                var owners = conf.owner_telegram_ids;
                if (owners != null && owners.Length > 0 && owners.Contains(actorTelegramUserId))
                    return true;
            }

            return false;
        }

        async Task<bool> TryEnsureAdminMutationAccessAsync(ITelegramBotClient bot, Chat chat, string tgId, CancellationToken ct)
        {
            var conf = ModInit.conf;
            if (string.IsNullOrEmpty(conf.mutations_api_secret))
            {
                await bot.SendMessage(chat.Id,
                    "В конфиге бота не задан <code>mutations_api_secret</code> — тот же секрет, что <code>TelegramAuth.mutations_api_secret</code> в init.conf.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            if (!long.TryParse(tgId?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var actorId))
                actorId = 0;

            if (!IsAllowedAdminCommandContext(conf, chat.Type, chat.Id, actorId))
            {
                await bot.SendMessage(chat.Id,
                    "Админ-команды с этого чата запрещены. Из лички: добавь свой id в <code>owner_telegram_ids</code> бота (те же числа, что <code>TelegramAuth.owner_telegram_ids</code>) или пиши из чата <code>admin_chat_ids</code>. Пустой <code>admin_chat_ids</code> — админ-команды из любого чата, в т.ч. лички.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (!IsTelegramAuthAdmin(user))
            {
                await bot.SendMessage(chat.Id,
                    "Команда только для администраторов (роль admin в базе TelegramAuth). Остальные пользователи входят через UID; ты включаешь их в <code>/users</code>.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            return true;
        }

        async Task<bool> TryEnsureAdminForCallbackAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
        {
            var chat = cq.Message?.Chat;
            var chatId = chat?.Id ?? cq.From.Id;
            var conf = ModInit.conf;
            if (string.IsNullOrEmpty(conf.mutations_api_secret))
            {
                await bot.SendMessage(chatId,
                    "В конфиге бота не задан <code>mutations_api_secret</code> — тот же секрет, что <code>TelegramAuth.mutations_api_secret</code> в init.conf.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            var chatType = chat?.Type ?? ChatType.Private;
            if (!IsAllowedAdminCommandContext(conf, chatType, chatId, cq.From.Id))
            {
                await bot.SendMessage(chatId,
                    "Админ-команды с этого чата запрещены. Для лички добавь свой id в <code>owner_telegram_ids</code> или используй чат из <code>admin_chat_ids</code>.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            var tgId = cq.From.Id.ToString();
            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (!IsTelegramAuthAdmin(user))
            {
                await bot.SendMessage(chatId,
                    "Команда только для администраторов (роль admin в базе TelegramAuth).",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            return true;
        }

        static string ShortUserLabel(AdminUserRowDto u)
        {
            var n = string.IsNullOrEmpty(u.username) ? u.telegramId : u.username;
            if (n.Length > 14)
                n = n.Substring(0, 12) + "…";
            return n;
        }

        static (string text, InlineKeyboardMarkup markup) BuildAdminUsersPage(AdminUsersListResponseDto data, int page, string actorTgId)
        {
            var all = data.users ?? new List<AdminUserRowDto>();
            var total = all.Count;
            var totalPages = total == 0 ? 1 : (total + AdminUsersPageSize - 1) / AdminUsersPageSize;
            if (page < 0) page = 0;
            if (page >= totalPages) page = totalPages - 1;

            var slice = all.Skip(page * AdminUsersPageSize).Take(AdminUsersPageSize).ToList();
            var lines = new List<string>
            {
                $"<b>Пользователи TelegramAuth</b> · всего {total} · стр. {page + 1}/{totalPages}\n"
            };

            foreach (var u in slice)
            {
                var adm = string.Equals(u.role, "admin", StringComparison.OrdinalIgnoreCase);
                var st = u.registrationPending
                    ? "⏳ ждёт подтв."
                    : u.disabled ? "🔒 отключён" : u.active ? "✅ доступ" : "⏸ нет доступа";
                var tag = string.IsNullOrEmpty(u.username) ? "—" : "@" + EscapeHtml(u.username);
                var accsHint = "";
                if (u.accs != null)
                {
                    var bits = new List<string>();
                    if (u.accs.group.HasValue)
                        bits.Add($"g:{u.accs.group.Value}");
                    if (u.accs.ban == true)
                        bits.Add("ban");
                    if (u.accs.IsPasswd == true)
                        bits.Add("passwd");
                    if (!string.IsNullOrEmpty(u.accs.comment))
                        bits.Add(TruncatePlain(u.accs.comment, 18));
                    if (bits.Count > 0)
                        accsHint = " · <i>" + EscapeHtml(string.Join(", ", bits)) + "</i>";
                }

                lines.Add($"{tag} · <code>{EscapeHtml(u.telegramId)}</code> · {st}{(adm ? " · <b>admin</b>" : "")}{accsHint}");
            }

            if (total == 0)
                lines.Add("\n<i>Записей пока нет.</i>");

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var u in slice)
            {
                var isAdmin = string.Equals(u.role, "admin", StringComparison.OrdinalIgnoreCase);
                var self = string.Equals(u.telegramId, actorTgId, StringComparison.Ordinal);
                if (isAdmin || self)
                    continue;

                var label = ShortUserLabel(u);
                if (u.registrationPending)
                {
                    var cbA = $"{CbApprovePending}{page}|{u.telegramId}";
                    var cbR = $"{CbRejectPending}{page}|{u.telegramId}";
                    if (Encoding.UTF8.GetByteCount(cbA) <= 64 && Encoding.UTF8.GetByteCount(cbR) <= 64)
                    {
                        rows.Add(new[]
                        {
                            InlineKeyboardButton.WithCallbackData($"✅ Принять · {label}", cbA),
                            InlineKeyboardButton.WithCallbackData($"❌ Отклонить · {label}", cbR)
                        });
                    }

                    continue;
                }

                var cb = u.disabled
                    ? $"{CbEnableUser}{page}|{u.telegramId}"
                    : $"{CbDisableUser}{page}|{u.telegramId}";
                if (Encoding.UTF8.GetByteCount(cb) <= 64)
                {
                    var title = u.disabled ? $"✅ Вкл · {label}" : $"🚫 Выкл · {label}";
                    rows.Add(new[] { InlineKeyboardButton.WithCallbackData(title, cb) });
                }
            }

            if (totalPages > 1)
            {
                var nav = new List<InlineKeyboardButton>();
                if (page > 0)
                    nav.Add(InlineKeyboardButton.WithCallbackData("◀️", CbUsersPage + (page - 1)));
                if (page < totalPages - 1)
                    nav.Add(InlineKeyboardButton.WithCallbackData("▶️", CbUsersPage + (page + 1)));
                if (nav.Count > 0)
                    rows.Add(nav.ToArray());
            }

            var markup = rows.Count > 0 ? new InlineKeyboardMarkup(rows) : null;
            return (string.Join("\n", lines), markup);
        }

        async Task SendOrEditAdminUsersPageAsync(ITelegramBotClient bot, ChatId chatId, int? messageId, AdminUsersListResponseDto data, int page, string actorTgId, CancellationToken ct)
        {
            var (text, markup) = BuildAdminUsersPage(data, page, actorTgId);
            if (messageId.HasValue)
            {
                await bot.EditMessageText(chatId, messageId.Value, text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct).ConfigureAwait(false);
            }
        }

        async Task CmdUsersAsync(ITelegramBotClient bot, Chat chat, string tgId, CancellationToken ct)
        {
            if (!await TryEnsureAdminMutationAccessAsync(bot, chat, tgId, ct).ConfigureAwait(false))
                return;

            var data = await _api.GetAdminUsersAsync(ct).ConfigureAwait(false);
            if (data == null || !data.ok)
            {
                await bot.SendMessage(chat.Id, "❌ Не удалось загрузить список пользователей (проверь секрет и доступ к Lampac).", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            await SendOrEditAdminUsersPageAsync(bot, chat.Id, null, data, 0, tgId, ct).ConfigureAwait(false);
        }

        static string TruncatePlain(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s ?? "";
            return s.Substring(0, max) + "…";
        }

        async Task CmdAdminUserDetailAsync(ITelegramBotClient bot, Chat chat, string actorTgId, string text, CancellationToken ct)
        {
            if (!await TryEnsureAdminMutationAccessAsync(bot, chat, actorTgId, ct).ConfigureAwait(false))
                return;

            var m = AdminUserDetailRe.Match(text.Trim());
            if (!m.Success)
            {
                await bot.SendMessage(chat.Id,
                    "<b>Просмотр пользователя (accsdb-поля)</b>\n\n<code>/user &lt;telegramId&gt;</code>\n\nПоказывает <b>accs</b>, срок <b>expiresAt</b>, устройства. Редактирование — <code>/setuser</code>.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var targetId = m.Groups[1].Value;
            var (ok, jo, errBody) = await _api.GetAdminUserDetailAsync(targetId, ct).ConfigureAwait(false);
            if (!ok || jo == null)
            {
                await bot.SendMessage(chat.Id,
                    "❌ Не удалось загрузить пользователя.\n" + TruncateForTelegram(StripJsonError(errBody), 800),
                    cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var role = jo.Value<string>("role") ?? "—";
            var lang = jo.Value<string>("lang") ?? "—";
            var uname = jo.Value<string>("username");
            var active = jo.Value<bool?>("active") == true;
            var disabled = jo.Value<bool?>("disabled") == true;
            var pending = jo.Value<bool?>("registrationPending") == true;
            var exp = jo["expiresAt"]?.ToString() ?? "—";
            var maxDev = jo.Value<int?>("maxDevices");
            var devCount = jo.Value<int?>("deviceCount");
            var maxStr = maxDev == -1 ? "∞" : (maxDev?.ToString() ?? "—");

            var accsJson = jo["accs"] is JToken at && at.Type != JTokenType.Null
                ? at.ToString(Formatting.Indented)
                : "(нет — действуют значения по умолчанию из роли и init)";

            var devLines = new List<string>();
            if (jo["devices"] is JArray arr)
            {
                foreach (var d in arr.Take(12))
                {
                    var uid = d.Value<string>("uid") ?? "";
                    var nm = d.Value<string>("name");
                    var act = d.Value<bool?>("active") == true;
                    var nmShow = string.IsNullOrEmpty(nm) ? "—" : nm;
                    devLines.Add($"• <code>{EscapeHtml(uid)}</code> · {EscapeHtml(nmShow)} · {(act ? "on" : "off")}");
                }

                if (arr.Count > 12)
                    devLines.Add($"… ещё {arr.Count - 12}");
            }

            var devBlock = devLines.Count > 0 ? string.Join("\n", devLines) : "<i>нет устройств</i>";

            var msg =
                $"<b>Пользователь</b> <code>{EscapeHtml(targetId)}</code>\n" +
                $"@{EscapeHtml(string.IsNullOrEmpty(uname) ? "—" : uname)} · роль <b>{EscapeHtml(role)}</b> · lang {EscapeHtml(lang)}\n" +
                $"активен: {(active ? "да" : "нет")} · отключён: {(disabled ? "да" : "нет")} · ожидание: {(pending ? "да" : "нет")}\n" +
                $"<b>Срок (ExpiresAt):</b> <code>{EscapeHtml(exp)}</code>\n" +
                $"<b>Устройства:</b> {devCount ?? 0} / {maxStr}\n\n" +
                "<b>accs</b> (синхронизируется в корневой users.json):\n<pre>" + EscapeHtml(accsJson) + "</pre>\n\n" +
                "<b>Устройства:</b>\n" + devBlock;

            await bot.SendMessage(chat.Id, msg, parseMode: ParseMode.Html, cancellationToken: ct).ConfigureAwait(false);
        }

        async Task CmdAdminSetUserAsync(ITelegramBotClient bot, Chat chat, string actorTgId, string text, CancellationToken ct)
        {
            if (!await TryEnsureAdminMutationAccessAsync(bot, chat, actorTgId, ct).ConfigureAwait(false))
                return;

            var m = AdminSetUserRe.Match(text.Trim());
            if (!m.Success)
            {
                await bot.SendMessage(chat.Id,
                    "<b>Изменение пользователя (accsdb)</b>\n\n" +
                    "<code>/setuser &lt;telegramId&gt; &lt;команда&gt; …</code>\n\n" +
                    "<b>Команды:</b>\n" +
                    "• <code>group</code> &lt;n&gt; или <code>clear</code>\n" +
                    "• <code>expires</code> &lt;дата ISO или yyyy-MM-dd&gt; или <code>clear</code>\n" +
                    "• <code>role</code> user | admin\n" +
                    "• <code>lang</code> &lt;код&gt;\n" +
                    "• <code>comment</code> &lt;текст&gt;\n" +
                    "• <code>banmsg</code> &lt;текст&gt;\n" +
                    "• <code>passwd</code> on | off\n" +
                    "• <code>ban</code> on | off — бан в accsdb (учётка TG активна); текст — <code>banmsg</code>\n" +
                    "• <code>ids</code> uid1,uid2 или <code>clear</code>\n" +
                    "• <code>param</code> ключ=значение\n" +
                    "• <code>clearparam</code> ключ\n" +
                    "• <code>clear</code> group ban ban_msg comment … — сброс полей accs\n\n" +
                    "Пока аккаунт <b>ожидает подтверждения</b>, UID <b>не попадает</b> в accsdb (при привязке лишний UID удаляется из users.json).\n\n" +
                    "После изменения при включённом sync данные уходят в корневой <code>users.json</code>.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var targetId = m.Groups[1].Value;
            var sub = m.Groups[2].Value.Trim().ToLowerInvariant();
            var rest = m.Groups[3].Value.Trim();

            var patch = new JObject { ["telegramId"] = targetId };
            string err;

            switch (sub)
            {
                case "group":
                    if (string.IsNullOrEmpty(rest))
                        err = "Укажи число или clear.";
                    else if (rest.Equals("clear", StringComparison.OrdinalIgnoreCase))
                    {
                        patch["accsRemove"] = new JArray("group");
                        err = null;
                    }
                    else if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g))
                    {
                        patch["accs"] = new JObject { ["group"] = g };
                        err = null;
                    }
                    else
                        err = "group: нужно целое число или clear.";
                    break;

                case "expires":
                    if (string.IsNullOrEmpty(rest))
                        err = "Укажи дату или clear.";
                    else if (rest.Equals("clear", StringComparison.OrdinalIgnoreCase))
                    {
                        patch["expiresAt"] = JValue.CreateNull();
                        err = null;
                    }
                    else
                    {
                        patch["expiresAt"] = rest;
                        err = null;
                    }

                    break;

                case "role":
                    if (!string.Equals(rest, "user", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(rest, "admin", StringComparison.OrdinalIgnoreCase))
                        err = "role: user или admin.";
                    else
                    {
                        patch["role"] = rest.ToLowerInvariant() == "admin" ? "admin" : "user";
                        err = null;
                    }

                    break;

                case "lang":
                    if (string.IsNullOrEmpty(rest))
                        err = "Укажи код языка.";
                    else
                    {
                        patch["lang"] = rest;
                        err = null;
                    }

                    break;

                case "comment":
                    patch["accs"] = new JObject { ["comment"] = rest };
                    err = null;
                    break;

                case "banmsg":
                    patch["accs"] = new JObject { ["ban_msg"] = rest };
                    err = null;
                    break;

                case "passwd":
                    {
                        var p = rest.ToLowerInvariant();
                        if (p is "on" or "1" or "true" or "yes")
                        {
                            patch["accs"] = new JObject { ["IsPasswd"] = true };
                            err = null;
                        }
                        else if (p is "off" or "0" or "false" or "no")
                        {
                            patch["accs"] = new JObject { ["IsPasswd"] = false };
                            err = null;
                        }
                        else
                            err = "passwd: on или off.";
                        break;
                    }

                case "ban":
                    {
                        var p = rest.ToLowerInvariant();
                        if (p is "on" or "1" or "true" or "yes")
                        {
                            patch["accs"] = new JObject { ["ban"] = true };
                            err = null;
                        }
                        else if (p is "off" or "0" or "false" or "no")
                        {
                            patch["accs"] = new JObject { ["ban"] = false };
                            err = null;
                        }
                        else
                            err = "ban: on или off.";
                        break;
                    }

                case "ids":
                    if (string.IsNullOrEmpty(rest))
                        err = "Укажи список uid через запятую или clear.";
                    else if (rest.Equals("clear", StringComparison.OrdinalIgnoreCase))
                    {
                        patch["accsRemove"] = new JArray("ids");
                        err = null;
                    }
                    else
                    {
                        var ids = rest.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 0)
                            .ToList();
                        if (ids.Count == 0)
                            err = "Пустой список ids.";
                        else
                        {
                            patch["accs"] = new JObject { ["ids"] = new JArray(ids) };
                            err = null;
                        }
                    }

                    break;

                case "param":
                    {
                        var eq = rest.IndexOf('=');
                        if (eq < 1 || eq >= rest.Length - 1)
                            err = "Формат: param ключ=значение";
                        else
                        {
                            var k = rest.Substring(0, eq).Trim();
                            var v = rest.Substring(eq + 1);
                            if (string.IsNullOrEmpty(k))
                                err = "Пустой ключ.";
                            else
                            {
                                patch["accs"] = new JObject { ["params"] = new JObject { [k] = v } };
                                err = null;
                            }
                        }

                        break;
                    }

                case "clearparam":
                    if (string.IsNullOrEmpty(rest))
                        err = "Укажи ключ.";
                    else
                    {
                        patch["paramsRemove"] = new JArray(rest.Trim());
                        err = null;
                    }

                    break;

                case "clear":
                    {
                        var keys = rest.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToLowerInvariant())
                            .Where(s => s.Length > 0)
                            .ToList();
                        if (keys.Count == 0)
                            err = "Укажи поля: group, ispasswd, ban, ban_msg, comment, ids, params";
                        else
                        {
                            patch["accsRemove"] = new JArray(keys);
                            err = null;
                        }

                        break;
                    }

                default:
                    err = "Неизвестная команда. Смотри <code>/setuser</code> без аргументов.";
                    break;
            }

            if (err != null)
            {
                await bot.SendMessage(chat.Id, "❌ " + err, parseMode: ParseMode.Html, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var (pok, detail) = await _api.PatchAdminUserAsync(patch, ct).ConfigureAwait(false);
            if (pok)
                await bot.SendMessage(chat.Id, $"✅ Сохранено для <code>{EscapeHtml(targetId)}</code>.", parseMode: ParseMode.Html, cancellationToken: ct).ConfigureAwait(false);
            else
                await bot.SendMessage(chat.Id,
                    "❌ Ошибка API:\n" + TruncateForTelegram(StripJsonError(detail), 1200),
                    cancellationToken: ct).ConfigureAwait(false);
        }

        async Task CmdImportAsync(ITelegramBotClient bot, Chat chat, string tgId, CancellationToken ct)
        {
            if (!await TryEnsureAdminMutationAccessAsync(bot, chat, tgId, ct).ConfigureAwait(false))
                return;

            await bot.SendMessage(chat.Id, "⏳ Запускаю импорт…", cancellationToken: ct).ConfigureAwait(false);
            var (ok, detail) = await _api.ImportLegacyAsync(ct).ConfigureAwait(false);
            if (ok)
            {
                try
                {
                    var jo = JObject.Parse(detail);
                    var msg =
                        "✅ Импорт завершён.\n" +
                        $"Пользователей: {jo.Value<int?>("importedUsers") ?? 0}, устройств: {jo.Value<int?>("importedDevices") ?? 0}, админов: {jo.Value<int?>("importedAdmins") ?? 0}, языков: {jo.Value<int?>("importedLangs") ?? 0}";
                    await bot.SendMessage(chat.Id, msg, cancellationToken: ct).ConfigureAwait(false);
                }
                catch
                {
                    await bot.SendMessage(chat.Id, "✅ Импорт завершён.\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
                }
            }
            else
            {
                await bot.SendMessage(chat.Id, "❌ Ошибка импорта:\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
            }
        }

        async Task CmdCleanupAsync(ITelegramBotClient bot, Chat chat, string tgId, CancellationToken ct)
        {
            if (!await TryEnsureAdminMutationAccessAsync(bot, chat, tgId, ct).ConfigureAwait(false))
                return;

            await bot.SendMessage(chat.Id, "⏳ Очистка неактивных устройств…", cancellationToken: ct).ConfigureAwait(false);
            var (ok, detail) = await _api.CleanupDevicesAsync(ct).ConfigureAwait(false);
            if (ok)
            {
                try
                {
                    var jo = JObject.Parse(detail);
                    var removed = jo.Value<int?>("removed") ?? 0;
                    await bot.SendMessage(chat.Id, $"✅ Готово. Удалено записей устройств: {removed}", cancellationToken: ct).ConfigureAwait(false);
                }
                catch
                {
                    await bot.SendMessage(chat.Id, "✅ Готово.\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
                }
            }
            else
            {
                await bot.SendMessage(chat.Id, "❌ Ошибка очистки:\n" + TruncateForTelegram(detail, 3500), cancellationToken: ct).ConfigureAwait(false);
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
                "📱 Мои устройства — список; отвязать активные, включить отключённые\n" +
                "<code>/devicename &lt;uid&gt; &lt;имя&gt;</code> — имя в базе (для админки); <code>-</code> сбрасывает\n" +
                "❓ Помощь — эта подсказка\n\n" +
                "<b>Владелец:</b> числовой user id в <code>TelegramAuth.owner_telegram_ids</code> — при старте Lampac запись admin создаётся в базе. Остальные шлют UID; новых пользователей подтверждаешь в <code>/users</code> (Принять / Отклонить) или кнопками в уведомлении.\n\n" +
                "<b>Админ-команды:</b> <code>/users</code>, <code>/user</code> &lt;id&gt;, <code>/setuser</code> …, <code>/import</code>, <code>/cleanup</code> + одинаковый <code>mutations_api_secret</code>." +
                (conf.admin_chat_ids != null && conf.admin_chat_ids.Length > 0
                    ? "\n\n<code>admin_chat_ids</code> задан: команды из группы — только там; из лички — если твой id в <code>owner_telegram_ids</code> бота (как на сервере)."
                    : "\n\nПустой <code>admin_chat_ids</code> — админ-команды из лички без доп. списков.");
            await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
        }

        async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
        {
            var data = cq.Data ?? "";
            if (data.StartsWith("unbind:", StringComparison.Ordinal))
            {
                await HandleUnbindDeviceCallbackAsync(bot, cq, ct).ConfigureAwait(false);
                return;
            }

            if (data.StartsWith(CbReactivateDevice, StringComparison.Ordinal))
            {
                await HandleReactivateDeviceCallbackAsync(bot, cq, ct).ConfigureAwait(false);
                return;
            }

            if (data.StartsWith(CbApprovePending, StringComparison.Ordinal)
                || data.StartsWith(CbRejectPending, StringComparison.Ordinal))
            {
                await HandlePendingRegistrationCallbackAsync(bot, cq, ct).ConfigureAwait(false);
                return;
            }

            if (data.StartsWith(CbUsersPage, StringComparison.Ordinal)
                || data.StartsWith(CbDisableUser, StringComparison.Ordinal)
                || data.StartsWith(CbEnableUser, StringComparison.Ordinal))
            {
                await HandleAdminUsersInlineAsync(bot, cq, ct).ConfigureAwait(false);
                return;
            }

            await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
        }

        async Task HandleUnbindDeviceCallbackAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
        {
            var uid = cq.Data != null && cq.Data.Length > 7 ? cq.Data.Substring(7) : "";
            var tgId = cq.From.Id.ToString();
            var name = _displayName;

            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (user == null || !user.found)
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                await bot.EditMessageText(cq.Message!.Chat.Id, cq.Message.MessageId, $"Тебя нет в базе {name}.", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var devicesResp = await _api.GetDevicesAsync(tgId, ct).ConfigureAwait(false);
            var devices = devicesResp?.devices ?? new List<DeviceDto>();
            var uids = devices.Select(d => d.uid).Where(u => !string.IsNullOrEmpty(u)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!uids.Contains(uid))
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, "Это устройство не принадлежит тебе.", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var ok = await _api.UnbindDeviceAsync(tgId, uid, ct).ConfigureAwait(false);
            await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
            if (ok)
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, $"Устройство {uid} отвязано.", cancellationToken: ct).ConfigureAwait(false);
            else
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, "Не удалось отвязать устройство.", cancellationToken: ct).ConfigureAwait(false);
        }

        async Task HandleReactivateDeviceCallbackAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
        {
            var prefixLen = CbReactivateDevice.Length;
            var uid = cq.Data != null && cq.Data.Length > prefixLen ? cq.Data.Substring(prefixLen) : "";
            var tgId = cq.From.Id.ToString();
            var name = _displayName;

            var user = await _api.GetUserByTelegramAsync(tgId, ct).ConfigureAwait(false);
            if (user == null || !user.found)
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                await bot.EditMessageText(cq.Message!.Chat.Id, cq.Message.MessageId, $"Тебя нет в базе {name}.", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var devicesResp = await _api.GetDevicesAsync(tgId, ct).ConfigureAwait(false);
            var devices = devicesResp?.devices ?? new List<DeviceDto>();
            var uids = devices.Select(d => d.uid).Where(u => !string.IsNullOrEmpty(u)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!uids.Contains(uid))
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, "Это устройство не принадлежит тебе.", cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var (ok, detail) = await _api.ReactivateDeviceAsync(tgId, uid, ct).ConfigureAwait(false);
            await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
            if (ok)
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, $"Устройство <code>{EscapeHtml(uid)}</code> снова активно. Открой приложение и при необходимости нажми «Проверить снова».", parseMode: ParseMode.Html, cancellationToken: ct).ConfigureAwait(false);
            else
                await bot.EditMessageText(cq.Message.Chat.Id, cq.Message.MessageId, "Не удалось включить устройство.\n" + TruncateForTelegram(StripJsonError(detail), 500), cancellationToken: ct).ConfigureAwait(false);
        }

        async Task HandlePendingRegistrationCallbackAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
        {
            if (!await TryEnsureAdminForCallbackAsync(bot, cq, ct).ConfigureAwait(false))
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var data = cq.Data ?? "";
            var approve = data.StartsWith(CbApprovePending, StringComparison.Ordinal);
            if (!approve && !data.StartsWith(CbRejectPending, StringComparison.Ordinal))
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var prefixLen = approve ? CbApprovePending.Length : CbRejectPending.Length;
            var rest = data.Length > prefixLen ? data.Substring(prefixLen) : "";
            var parts = rest.Split('|', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var returnPage) || returnPage < 0 || string.IsNullOrWhiteSpace(parts[1]))
            {
                await bot.AnswerCallbackQuery(cq.Id, "Некорректные данные кнопки.", showAlert: true, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var targetId = parts[1].Trim();
            var (ok, detail) = await _api.ResolveRegistrationPendingAsync(targetId, approve, ct).ConfigureAwait(false);
            if (!ok)
            {
                await bot.AnswerCallbackQuery(cq.Id, TruncateForTelegram(StripJsonError(detail), 180), showAlert: true, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var msg = cq.Message;
            var actorTgId = cq.From.Id.ToString();
            if (msg != null && msg.Text != null && msg.Text.IndexOf("Пользователи TelegramAuth", StringComparison.Ordinal) >= 0)
            {
                var list = await _api.GetAdminUsersAsync(ct).ConfigureAwait(false);
                if (list != null && list.ok)
                    await SendOrEditAdminUsersPageAsync(bot, msg.Chat.Id, msg.MessageId, list, returnPage, actorTgId, ct).ConfigureAwait(false);
            }
            else if (msg != null)
            {
                var suffix = approve
                    ? "\n\n✅ <b>Подтверждено</b> — доступ включён."
                    : "\n\n❌ <b>Отклонено</b> — запись пользователя удалена из базы.";
                try
                {
                    await bot.EditMessageText(msg.Chat.Id, msg.MessageId, msg.Text + suffix, parseMode: ParseMode.Html, replyMarkup: null, cancellationToken: ct).ConfigureAwait(false);
                }
                catch
                {
                    // ignore edit errors (e.g. message too old)
                }
            }

            await bot.AnswerCallbackQuery(cq.Id, approve ? "Доступ подтверждён." : "Запись удалена.", cancellationToken: ct).ConfigureAwait(false);
        }

        async Task HandleAdminUsersInlineAsync(ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
        {
            if (!await TryEnsureAdminForCallbackAsync(bot, cq, ct).ConfigureAwait(false))
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var msg = cq.Message;
            if (msg == null)
            {
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            var chatId = msg.Chat.Id;
            var msgId = msg.MessageId;
            var actorTgId = cq.From.Id.ToString();
            var data = cq.Data ?? "";

            if (data.StartsWith(CbUsersPage, StringComparison.Ordinal))
            {
                if (!int.TryParse(data.AsSpan(CbUsersPage.Length), out var page) || page < 0)
                    page = 0;
                var list = await _api.GetAdminUsersAsync(ct).ConfigureAwait(false);
                if (list == null || !list.ok)
                {
                    await bot.AnswerCallbackQuery(cq.Id, "Не удалось обновить список.", showAlert: true, cancellationToken: ct).ConfigureAwait(false);
                    return;
                }

                await SendOrEditAdminUsersPageAsync(bot, chatId, msgId, list, page, actorTgId, ct).ConfigureAwait(false);
                await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct).ConfigureAwait(false);
                return;
            }

            if (data.StartsWith(CbDisableUser, StringComparison.Ordinal) || data.StartsWith(CbEnableUser, StringComparison.Ordinal))
            {
                var disable = data.StartsWith(CbDisableUser, StringComparison.Ordinal);
                var rest = disable ? data.Substring(CbDisableUser.Length) : data.Substring(CbEnableUser.Length);
                var parts = rest.Split('|', 2);
                if (parts.Length != 2 || !int.TryParse(parts[0], out var returnPage) || returnPage < 0 || string.IsNullOrWhiteSpace(parts[1]))
                {
                    await bot.AnswerCallbackQuery(cq.Id, "Некорректные данные кнопки.", showAlert: true, cancellationToken: ct).ConfigureAwait(false);
                    return;
                }

                var targetId = parts[1].Trim();
                if (disable && string.Equals(targetId, actorTgId, StringComparison.Ordinal))
                {
                    await bot.AnswerCallbackQuery(cq.Id, "Нельзя отключить самого себя.", showAlert: true, cancellationToken: ct).ConfigureAwait(false);
                    return;
                }

                var (ok, detail) = await _api.SetUserDisabledAsync(targetId, disable, ct).ConfigureAwait(false);
                if (!ok)
                {
                    await bot.AnswerCallbackQuery(cq.Id, TruncateForTelegram(StripJsonError(detail), 180), showAlert: true, cancellationToken: ct).ConfigureAwait(false);
                    return;
                }

                var fresh = await _api.GetAdminUsersAsync(ct).ConfigureAwait(false);
                if (fresh != null && fresh.ok)
                    await SendOrEditAdminUsersPageAsync(bot, chatId, msgId, fresh, returnPage, actorTgId, ct).ConfigureAwait(false);

                await bot.AnswerCallbackQuery(cq.Id, disable ? "Доступ отключён." : "Доступ включён.", showAlert: false, cancellationToken: ct).ConfigureAwait(false);
            }
        }

        static string StripJsonError(string json)
        {
            if (string.IsNullOrEmpty(json))
                return "Ошибка API";
            try
            {
                var jo = JObject.Parse(json);
                var err = jo.Value<string>("error");
                var det = jo.Value<string>("detail");
                if (!string.IsNullOrEmpty(det))
                    return det;
                return err ?? json;
            }
            catch
            {
                return json;
            }
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
