using Telegram.Bot.Types;
using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    sealed partial class TelegramAuthBotSession
    {
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
    }
}
