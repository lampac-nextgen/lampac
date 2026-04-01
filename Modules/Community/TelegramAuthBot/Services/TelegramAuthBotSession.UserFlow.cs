using Telegram.Bot.Types;
using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    sealed partial class TelegramAuthBotSession
    {
        async Task NotifyAdminsOfPendingProvisionAsync(ITelegramBotClient bot, string newUserTgId, string username, string deviceUid, CancellationToken ct)
        {
            var conf = ModInit.conf;
            if (!conf.notify_admins_on_pending_provision)
                return;

            if (string.IsNullOrEmpty(conf.lampac_accspasswd?.Trim()))
            {
                TelegramAuthBotSerilog.Log.Warning(
                    "Новый пользователь ожидает активации (TelegramId={TgId}), lampac_accspasswd пуст — уведомления админам не отправлены.",
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
                "<b>Админ-команды:</b> <code>/users</code>, <code>/user</code> &lt;id&gt;, <code>/setuser</code> …, <code>/import</code>, <code>/cleanup</code> + cookie <code>accspasswd</code> (<code>lampac_accspasswd</code> = файл <code>passwd</code> на Lampac)." +
                (conf.admin_chat_ids != null && conf.admin_chat_ids.Length > 0
                    ? "\n\n<code>admin_chat_ids</code> задан: команды из группы — только там; из лички — если твой id в <code>owner_telegram_ids</code> бота (как на сервере)."
                    : "\n\nПустой <code>admin_chat_ids</code> — админ-команды из лички без доп. списков.");
            await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: MainMenuKeyboard(), cancellationToken: ct).ConfigureAwait(false);
        }
    }
}
