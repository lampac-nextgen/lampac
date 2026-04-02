using Newtonsoft.Json;
using Telegram.Bot.Types;
using TelegramAuthBot.Models;

namespace TelegramAuthBot.Services
{
    sealed partial class TelegramAuthBotSession
    {
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
            if (string.IsNullOrEmpty(conf.lampac_accspasswd))
            {
                await bot.SendMessage(chat.Id,
                    "В конфиге бота не задан <code>lampac_accspasswd</code> — тот же текст, что в файле <code>passwd</code> на сервере Lampac.",
                    parseMode: ParseMode.Html,
                    cancellationToken: ct).ConfigureAwait(false);
                return false;
            }

            if (!long.TryParse(tgId?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var actorId))
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
            if (string.IsNullOrEmpty(conf.lampac_accspasswd))
            {
                await bot.SendMessage(chatId,
                    "В конфиге бота не задан <code>lampac_accspasswd</code> — тот же текст, что в файле <code>passwd</code> на сервере Lampac.",
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
                await bot.SendMessage(chat.Id, "❌ Не удалось загрузить список пользователей (проверь <code>lampac_accspasswd</code> и доступ к Lampac).", parseMode: ParseMode.Html, cancellationToken: ct).ConfigureAwait(false);
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
    }
}
