#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using QRAuth.Models;

namespace QRAuth.Services
{
    /// <summary>
    /// Bot's jobs: confirm the deny-page QR login handshake for a user who already has
    /// active access, let a user request access with an admin (configured via admin_ids)
    /// granting/denying it with a button tap, and let that admin list/revoke access via
    /// /users. Everything beyond that (self-service beyond one request, broadcasts, audit)
    /// is still out of scope — grow it back deliberately if that stops being enough.
    /// </summary>
    public class BotSession
    {
        // reply-keyboard button labels doubled as the exact match text below — keep both in
        // sync via these constants instead of two hand-typed string literals
        const string BtnUsers = "👥  Пользователи";
        const string BtnStats = "📊  Статистика";
        const string BtnPassword = "🔑  Пароль для входа";

        static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(10);
        static readonly ConcurrentDictionary<long, DateTime> _lastRequest = new();
        static readonly ConcurrentDictionary<long, string> _pendingRequesterNames = new();
        static readonly ConcurrentDictionary<long, string> _pendingQrSessions = new();

        readonly UsersRepository _repo;

        public BotSession(UsersRepository repo)
        {
            _repo = repo;
        }

        /// <summary>Has this record been granted access at all, and not since revoked? Access
        /// here is pure authorization (grant/ban), no time limit — see UsersRepository.AddUser.</summary>
        static bool HasAccess([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] LampacUser? u) => u != null && !u.Ban;

        static bool IsAdmin(long tgId) => Array.IndexOf(ModInit.conf.admin_ids, tgId) >= 0;

        public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            try
            {
                if (update.CallbackQuery is { } cb) { await HandleCallbackAsync(bot, cb, ct); return; }
                if (update.Message is { } msg) await HandleMessageAsync(bot, msg, ct);
            }
            catch (Exception ex)
            {
                FileLog.Write("[TelegramBot] HandleUpdate error", ex);

                // otherwise the button just spins forever with no feedback for whoever tapped it
                if (update.CallbackQuery is { } cb)
                {
                    try { await bot.AnswerCallbackQuery(cb.Id, "Ошибка на сервере, смотри tgbot.log.", showAlert: true, cancellationToken: ct); }
                    catch { /* best effort */ }
                }
            }
        }

        async Task HandleMessageAsync(ITelegramBotClient bot, Message msg, CancellationToken ct)
        {
            var text = msg.Text?.Trim() ?? "";

            if (text.StartsWith("/users", StringComparison.Ordinal) || text == BtnUsers)
            {
                // silent no-op for non-admins instead of "Недоступно" — don't confirm to a
                // prober that /users is even a real command
                if (!IsAdmin(msg.From?.Id ?? 0)) return;
                await ShowUsersListAsync(bot, msg.Chat.Id, ct);
                return;
            }

            if (text == BtnStats)
            {
                if (!IsAdmin(msg.From?.Id ?? 0)) return;
                await ShowStatsAsync(bot, msg.Chat.Id, ct);
                return;
            }

            if (text == BtnPassword)
            {
                // silent no-op if access was revoked since the keyboard was last sent — same
                // "don't confirm/deny a prober" reasoning as the /users check above
                var u = _repo.GetByTgId(msg.From?.Id ?? 0);
                if (!HasAccess(u)) return;
                await SendPasswordCardAsync(bot, msg.Chat.Id, u, ct);
                return;
            }

            if (!text.StartsWith("/start")) return;

            var payload = text.Length > 6 ? text.Substring(6).Trim() : "";
            // qr_ = scanned from the TV/desktop QR, tg_ = tapped "Войти через Telegram" on the
            // same device; same session flow, only the wording differs
            if (payload.StartsWith("qr_", StringComparison.Ordinal) || payload.StartsWith("tg_", StringComparison.Ordinal))
            {
                await HandleQrStartAsync(bot, msg, payload.Substring(3), fromQr: payload[0] == 'q', ct);
                return;
            }

            await SendWelcomeAsync(bot, msg, ct);
        }

        async Task SendWelcomeAsync(ITelegramBotClient bot, Message msg, CancellationToken ct)
        {
            long userId = msg.From?.Id ?? 0;

            // admin's own /start has nothing to do with the "request access" flow below — they
            // don't request access from themselves — so it's a fully separate branch, not a
            // keyboard tacked onto the regular user welcome
            if (IsAdmin(userId))
            {
                await bot.SendMessage(msg.Chat.Id,
                    "👑  Привет, админ!\n\nЭтот бот подтверждает вход в Lampa через Telegram и обрабатывает заявки на доступ. Кнопки ниже — панель администратора.",
                    cancellationToken: ct);
                await ShowAdminPanelAsync(bot, msg.Chat.Id, ct);
                return;
            }

            var existing = _repo.GetByTgId(userId);
            if (HasAccess(existing))
            {
                await bot.SendMessage(msg.Chat.Id,
                    "👋  Привет!\n\nУ вас уже есть доступ к Lampa. Кнопка ниже всегда под рукой — если понадобится ввести пароль вручную.",
                    replyMarkup: PasswordKeyboard, cancellationToken: ct);
                return;
            }

            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🔑  Запросить доступ", "reqaccess") }
            });
            await bot.SendMessage(msg.Chat.Id,
                "👋  Привет!\n\nЭтот бот подтверждает вход в Lampa через Telegram и может запросить для вас доступ у администратора.",
                replyMarkup: kb, cancellationToken: ct);
        }

        static readonly ReplyKeyboardMarkup PasswordKeyboard = new(new[]
        {
            new KeyboardButton[] { BtnPassword }
        })
        { ResizeKeyboard = true };

        async Task ShowAdminPanelAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            var kb = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { BtnUsers, BtnStats }
            })
            { ResizeKeyboard = true };
            await bot.SendMessage(chatId, "👑  Панель администратора", replyMarkup: kb, cancellationToken: ct);
        }

        async Task ShowStatsAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            var users = _repo.ReadAll();
            var banned = users.Count(u => u.Ban);
            var active = users.Count - banned;

            var text = $"📊  Статистика\n\nВсего: {users.Count}\n✅  Активных: {active}\n⛔  Заблокированных: {banned}";
            await bot.SendMessage(chatId, text, cancellationToken: ct);
        }

        /// <summary>Deep link from the deny page: https://t.me/&lt;bot&gt;?start=qr_&lt;sessionId&gt; from the
        /// QR, ?start=tg_&lt;sessionId&gt; from the "Войти через Telegram" button.</summary>
        async Task HandleQrStartAsync(ITelegramBotClient bot, Message msg, string sessionId, bool fromQr, CancellationToken ct)
        {
            long userId = msg.From?.Id ?? 0;

            if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 32)
            {
                await bot.SendMessage(msg.Chat.Id, "Некорректная ссылка входа.", cancellationToken: ct);
                return;
            }

            var existing = _repo.GetByTgId(userId);
            if (!HasAccess(existing) && IsAdmin(userId))
            {
                // admin has no reqaccess/grant path of their own (SendWelcomeAsync short-circuits
                // to the admin panel instead) — without this, an admin scanning the QR from their
                // own device would fall through to that panel with no confirm button and no way
                // to ever get a users.json record, so scanning is auto-provisioned here instead.
                try
                {
                    var name = msg.From?.Username is { Length: > 0 } uname ? $"@{uname}" : "admin";
                    _repo.AddUser(userId, name);
                    existing = _repo.GetByTgId(userId);
                    FileLog.Write($"[TelegramBot] Админ tgId={userId} авто-зарегистрирован для QR-входа.");
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[TelegramBot] Авто-регистрация админа tgId={userId} не удалась", ex);
                    await bot.SendMessage(msg.Chat.Id, "Ошибка на сервере, смотри tgbot.log.", cancellationToken: ct);
                    return;
                }
            }

            if (!HasAccess(existing))
            {
                // remembered so a fast admin approval can auto-confirm this same session
                // (see HandleGrantAsync) instead of making the user rescan the QR
                _pendingQrSessions[userId] = sessionId;

                // no dead end — same welcome + "request access" button as plain /start
                await SendWelcomeAsync(bot, msg, ct);
                return;
            }

            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("✅  Подтвердить вход", (fromQr ? "qrauth:" : "tgauth:") + sessionId) }
            });
            await bot.SendMessage(msg.Chat.Id,
                fromQr
                    ? "🖥  Кто-то отсканировал QR-код на экране входа Lampa.\n\nЕсли это вы — нажмите кнопку ниже."
                    : "🖥  Кто-то пытается войти в Lampa через Telegram.\n\nЕсли это вы — нажмите кнопку ниже.",
                replyMarkup: kb, cancellationToken: ct);
        }

        async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            var data = cb.Data ?? "";

            if (data == "reqaccess") { await HandleRequestAccessAsync(bot, cb, ct); return; }
            if (data.StartsWith("grant:", StringComparison.Ordinal)) { await HandleGrantAsync(bot, cb, data.Substring(6), ct); return; }
            if (data.StartsWith("deny:", StringComparison.Ordinal)) { await HandleDenyAsync(bot, cb, data.Substring(5), ct); return; }
            if (data.StartsWith("qrauth:", StringComparison.Ordinal)) { await HandleQrAuthAsync(bot, cb, data.Substring(7), fromQr: true, ct); return; }
            if (data.StartsWith("tgauth:", StringComparison.Ordinal)) { await HandleQrAuthAsync(bot, cb, data.Substring(7), fromQr: false, ct); return; }
            if (data == "ulist") { await HandleUListAsync(bot, cb, ct); return; }
            if (data.StartsWith("uview:", StringComparison.Ordinal)) { await HandleUViewAsync(bot, cb, data.Substring(6), ct); return; }
            if (data.StartsWith("ublockask:", StringComparison.Ordinal)) { await HandleUBlockAskAsync(bot, cb, data.Substring(10), ct); return; }
            if (data.StartsWith("ublockconfirm:", StringComparison.Ordinal)) { await HandleUBlockConfirmAsync(bot, cb, data.Substring(14), ct); return; }
            if (data.StartsWith("uunblock:", StringComparison.Ordinal)) { await HandleUUnblockAsync(bot, cb, data.Substring(9), ct); return; }

            await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
        }

        async Task HandleQrAuthAsync(ITelegramBotClient bot, CallbackQuery cb, string sessionId, bool fromQr, CancellationToken ct)
        {
            var user = _repo.GetByTgId(cb.From.Id);
            if (!HasAccess(user))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Доступ не активен.", showAlert: true, cancellationToken: ct);
                return;
            }

            if (!QrAuthSessions.TryConfirm(sessionId, user.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id,
                    fromQr ? "Ссылка устарела, отсканируйте QR заново." : "Ссылка устарела. Нажмите «Войти через Telegram» ещё раз.",
                    showAlert: true, cancellationToken: ct);
                return;
            }

            await bot.AnswerCallbackQuery(cb.Id, "✅  Готово", cancellationToken: ct);
            long chatId = cb.Message?.Chat.Id ?? 0;
            int msgId = cb.Message?.MessageId ?? 0;
            await bot.EditMessageText(chatId, msgId, fromQr
                ? "✅  Вход подтверждён. Lampa на экране войдёт сама."
                : "✅  Вход подтверждён. Вернитесь в Lampa — вход выполнится сам.", cancellationToken: ct);

            var lines = new List<string>
            {
                fromQr ? "🔓  <b>Вход по QR подтверждён</b>" : "🔓  <b>Вход через Telegram подтверждён</b>",
                $"👤  <b>{HtmlEsc(user.Comment)}</b>",
                $"🆔  <code>{user.TgId}</code>"
            };
            await NotifyAdminsAsync(bot, string.Join("\n", lines), ct);
        }

        /// <summary>Best-effort admin broadcast — one admin's unreachable chat (never started
        /// the bot / blocked it) must not stop the others from being notified. Includes an
        /// admin who is himself the one logging in — the edited "Вход подтверждён" text left
        /// in his chat is not this card, so he'd otherwise never see the login notification.</summary>
        async Task NotifyAdminsAsync(ITelegramBotClient bot, string text, CancellationToken ct)
        {
            foreach (var adminId in ModInit.conf.admin_ids)
            {
                try
                {
                    await bot.SendMessage(adminId, text, parseMode: ParseMode.Html, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    FileLog.Write($"[TelegramBot] notify admin {adminId} failed", ex);
                }
            }
        }

        async Task HandleRequestAccessAsync(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            long userId = cb.From.Id;

            var existing = _repo.GetByTgId(userId);
            if (HasAccess(existing))
            {
                await bot.AnswerCallbackQuery(cb.Id, "У вас уже есть доступ.", showAlert: true, cancellationToken: ct);
                return;
            }

            if (ModInit.conf.admin_ids.Length == 0)
            {
                await bot.AnswerCallbackQuery(cb.Id, "Администратор не настроен.", showAlert: true, cancellationToken: ct);
                return;
            }

            if (_lastRequest.TryGetValue(userId, out var last) && DateTime.UtcNow - last < RequestCooldown)
            {
                await bot.AnswerCallbackQuery(cb.Id, "Заявка уже отправлена, дождитесь ответа администратора.", showAlert: true, cancellationToken: ct);
                return;
            }
            _lastRequest[userId] = DateTime.UtcNow;

            // short "@user / FirstName" form kept for the compact post-decision line
            // (MarkHandledAsync) — separate from the richer card built below
            var name = cb.From.Username is { Length: > 0 } uname
                ? $"@{uname} / {cb.From.FirstName}"
                : cb.From.FirstName;
            _pendingRequesterNames[userId] = name;
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅  Выдать", "grant:" + userId),
                    InlineKeyboardButton.WithCallbackData("❌  Отклонить", "deny:" + userId)
                }
            });

            var caption = BuildRequestCard(cb.From);

            InputFile? photo = null;
            try
            {
                var photos = await bot.GetUserProfilePhotos(userId, limit: 1, cancellationToken: ct);
                if (photos.Photos.Length > 0)
                    photo = InputFile.FromFileId(photos.Photos[0][^1].FileId);
            }
            catch (Exception ex)
            {
                // no photo / privacy settings hide it — card still works as text-only
                FileLog.Write($"[TelegramBot] GetUserProfilePhotos failed (tgId={userId})", ex);
            }

            foreach (var adminId in ModInit.conf.admin_ids)
            {
                try
                {
                    if (photo != null)
                        await bot.SendPhoto(adminId, photo, caption: caption, parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
                    else
                        await bot.SendMessage(adminId, caption, parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    // admin hasn't started the bot / chat unreachable — don't block other admins
                    FileLog.Write($"[TelegramBot] notify admin {adminId} failed", ex);
                }
            }

            await bot.AnswerCallbackQuery(cb.Id, "Заявка отправлена администратору.", cancellationToken: ct);

            try
            {
                await bot.SendMessage(userId, "📨  Заявка на доступ отправлена администратору. Ждите решения.", cancellationToken: ct);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[TelegramBot] notify user {userId} failed (reqaccess ack)", ex);
            }
        }

        /// <summary>Self-service password re-issue — an approved user's password (the record's
        /// <see cref="LampacUser.Id"/> token) already exists and never changes, so this just
        /// re-displays it for manual entry (QR broken, second device, forgot it, etc.) instead
        /// of making them re-request access from an admin. Reached only via the static
        /// <see cref="BtnPassword"/> reply-keyboard button (kept pinned in the chat once access
        /// is granted), not an inline button that would scroll away after one use.</summary>
        async Task SendPasswordCardAsync(ITelegramBotClient bot, long chatId, LampacUser user, CancellationToken ct)
        {
            var text = "🔑  <b>Пароль для входа в Lampa</b>\n\n"
                + "На экране входа нажмите «Войти по паролю» и укажите:\n\n"
                + $"<code>{user.Id}</code>\n\n"
                + "☝️  Нажмите на пароль, чтобы скопировать.\n"
                + "🔒  Никому его не передавайте.";

            await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: PasswordKeyboard, cancellationToken: ct);
        }

        /// <summary>All the account info Telegram actually hands us about a requester, laid
        /// out as a card instead of the old one-line "от Anna (id=...)" — an admin approving
        /// access is making a judgment call about who this is, so show what's available
        /// (name, @username, language, Premium) rather than just an id to trust blindly.</summary>
        static string BuildRequestCard(User u)
        {
            var fullName = string.IsNullOrWhiteSpace(u.LastName) ? u.FirstName : $"{u.FirstName} {u.LastName}";
            var lines = new List<string>
            {
                "📥  <b>Новая заявка на доступ</b>",
                "",
                $"👤  <b>{HtmlEsc(fullName)}</b>"
            };
            if (!string.IsNullOrWhiteSpace(u.Username))
                lines.Add($"🔗  @{HtmlEsc(u.Username)}");
            lines.Add($"🆔  <code>{u.Id}</code>");
            if (!string.IsNullOrWhiteSpace(u.LanguageCode))
                lines.Add($"🌐  {HtmlEsc(u.LanguageCode)}");
            if (u.IsPremium)
                lines.Add("⭐  Telegram Premium");

            return string.Join("\n", lines);
        }

        static string HtmlEsc(string s) => System.Net.WebUtility.HtmlEncode(s);

        // /users lives in ONE message per admin chat that gets edited in place (list → user
        // card → block confirm → back to list), never resent — matches the standard Bot API
        // guidance to edit a message when navigating a menu instead of sending+deleting a new
        // one each step. Users are addressed by their position in a freshly re-read list, not
        // by tg_id: hand-added accounts (family members with no Telegram of their own) all
        // share tg_id=0, so tg_id can't uniquely identify a record to act on.
        static readonly ConcurrentDictionary<long, int> _usersMenuMessageId = new();

        enum CardMode { View, ConfirmBlock }

        async Task ShowUsersListAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
        {
            var users = _repo.ReadAll();
            var (text, kb) = BuildListView(users);
            await RenderMenuAsync(bot, chatId, text, kb, ct);
        }

        async Task ShowUserCardAsync(ITelegramBotClient bot, long chatId, int index, CardMode mode, CancellationToken ct)
        {
            var users = _repo.ReadAll();
            if (index < 0 || index >= users.Count)
            {
                // list changed under us (grant/deny/manual edit) — safest fallback is just the list
                await ShowUsersListAsync(bot, chatId, ct);
                return;
            }
            var (text, kb) = BuildCardView(users[index], index, mode);
            await RenderMenuAsync(bot, chatId, text, kb, ct);
        }

        async Task PerformBlockAsync(ITelegramBotClient bot, long chatId, int index, long adminId, CancellationToken ct)
        {
            var users = _repo.ReadAll();
            if (index >= 0 && index < users.Count)
            {
                var target = users[index];
                _repo.RevokeByToken(target.Id);
                FileLog.Write($"[TelegramBot] Доступ заблокирован вручную: \"{target.Comment}\" (tg_id={target.TgId}), admin={adminId}");
            }
            await ShowUsersListAsync(bot, chatId, ct);
        }

        async Task PerformUnblockAsync(ITelegramBotClient bot, long chatId, int index, long adminId, CancellationToken ct)
        {
            var users = _repo.ReadAll();
            if (index >= 0 && index < users.Count)
            {
                var target = users[index];
                _repo.UnbanByToken(target.Id);
                FileLog.Write($"[TelegramBot] Доступ разблокирован вручную: \"{target.Comment}\" (tg_id={target.TgId}), admin={adminId}");
            }
            await ShowUsersListAsync(bot, chatId, ct);
        }

        static string UserStatus(LampacUser u) => u.Ban ? "заблокирован" : "активен";

        static string UserStatusIcon(LampacUser u) => u.Ban ? "⛔" : "✅";

        /// <summary>Edits the admin's tracked menu message if it's still there; falls back to
        /// sending a fresh one (message too old to edit / chat cleared / first call ever).</summary>
        async Task RenderMenuAsync(ITelegramBotClient bot, long chatId, string text, InlineKeyboardMarkup? kb, CancellationToken ct)
        {
            if (_usersMenuMessageId.TryGetValue(chatId, out var msgId))
            {
                try
                {
                    await bot.EditMessageText(chatId, msgId, text, parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
                    return;
                }
                catch { /* message gone/too old to edit — send a fresh one below */ }
            }

            var sent = await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyMarkup: kb, cancellationToken: ct);
            _usersMenuMessageId[chatId] = sent.MessageId;
        }

        (string text, InlineKeyboardMarkup? kb) BuildListView(List<LampacUser> users)
        {
            if (users.Count == 0)
                return ("👥  <b>Пользователей нет.</b>", null);

            var rows = new List<InlineKeyboardButton[]>();
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                var name = string.IsNullOrWhiteSpace(u.Comment) ? u.TgId.ToString() : u.Comment;
                var label = $"{UserStatusIcon(u)}  {Truncate(name, 30)}";
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData(label, "uview:" + i) });
            }
            return ($"👥  <b>Пользователи ({users.Count})</b>", new InlineKeyboardMarkup(rows));
        }

        (string text, InlineKeyboardMarkup kb) BuildCardView(LampacUser u, int index, CardMode mode)
        {
            var name = string.IsNullOrWhiteSpace(u.Comment) ? u.TgId.ToString() : u.Comment;
            // token (u.Id — the login password) is deliberately not shown here, this is a
            // chat log an admin can screenshot/forward
            var lines = new List<string>
            {
                $"👤  <b>{HtmlEsc(name)}</b>",
                "",
                $"🆔  <code>{u.TgId}</code>",
                $"{UserStatusIcon(u)}  статус: <b>{UserStatus(u)}</b>"
            };
            if (mode == CardMode.ConfirmBlock)
            {
                lines.Add("");
                lines.Add("🚫  <b>Заблокировать доступ?</b> Это отключит уже открытую сессию в течение ~1 сек.");
            }
            var text = string.Join("\n", lines);

            InlineKeyboardButton[] actionRow;
            if (mode == CardMode.ConfirmBlock)
            {
                actionRow = new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚫  Подтвердить", "ublockconfirm:" + index),
                    InlineKeyboardButton.WithCallbackData("Отмена", "uview:" + index)
                };
            }
            else if (u.Ban)
            {
                // reversible, so no confirm step — blocking (below) is the one-way-ish action
                // that gets a confirmation, restoring access doesn't need the same friction
                actionRow = new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔓  Разблокировать", "uunblock:" + index),
                    InlineKeyboardButton.WithCallbackData("⬅  Назад", "ulist")
                };
            }
            else
            {
                actionRow = new[]
                {
                    InlineKeyboardButton.WithCallbackData("🚫  Заблокировать", "ublockask:" + index),
                    InlineKeyboardButton.WithCallbackData("⬅  Назад", "ulist")
                };
            }

            return (text, new InlineKeyboardMarkup(new[] { actionRow }));
        }

        static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        async Task HandleUListAsync(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
        {
            if (!IsAdmin(cb.From.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Недоступно.", showAlert: true, cancellationToken: ct);
                return;
            }
            await ShowUsersListAsync(bot, cb.Message!.Chat.Id, ct);
            await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
        }

        async Task HandleUViewAsync(ITelegramBotClient bot, CallbackQuery cb, string idxStr, CancellationToken ct)
        {
            if (!IsAdmin(cb.From.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Недоступно.", showAlert: true, cancellationToken: ct);
                return;
            }
            if (!int.TryParse(idxStr, out var idx)) { await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct); return; }
            await ShowUserCardAsync(bot, cb.Message!.Chat.Id, idx, CardMode.View, ct);
            await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
        }

        async Task HandleUBlockAskAsync(ITelegramBotClient bot, CallbackQuery cb, string idxStr, CancellationToken ct)
        {
            if (!IsAdmin(cb.From.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Недоступно.", showAlert: true, cancellationToken: ct);
                return;
            }
            if (!int.TryParse(idxStr, out var idx)) { await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct); return; }
            await ShowUserCardAsync(bot, cb.Message!.Chat.Id, idx, CardMode.ConfirmBlock, ct);
            await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
        }

        async Task HandleUBlockConfirmAsync(ITelegramBotClient bot, CallbackQuery cb, string idxStr, CancellationToken ct)
        {
            if (!IsAdmin(cb.From.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Недоступно.", showAlert: true, cancellationToken: ct);
                return;
            }
            if (!int.TryParse(idxStr, out var idx)) { await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct); return; }
            await PerformBlockAsync(bot, cb.Message!.Chat.Id, idx, cb.From.Id, ct);
            await bot.AnswerCallbackQuery(cb.Id, "Заблокировано.", cancellationToken: ct);
        }

        async Task HandleUUnblockAsync(ITelegramBotClient bot, CallbackQuery cb, string idxStr, CancellationToken ct)
        {
            if (!IsAdmin(cb.From.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Недоступно.", showAlert: true, cancellationToken: ct);
                return;
            }
            if (!int.TryParse(idxStr, out var idx)) { await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct); return; }
            await PerformUnblockAsync(bot, cb.Message!.Chat.Id, idx, cb.From.Id, ct);
            await bot.AnswerCallbackQuery(cb.Id, "Разблокировано.", cancellationToken: ct);
        }

        async Task HandleGrantAsync(ITelegramBotClient bot, CallbackQuery cb, string tgIdStr, CancellationToken ct)
        {
            if (!IsAdmin(cb.From.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Недоступно.", showAlert: true, cancellationToken: ct);
                return;
            }
            if (!long.TryParse(tgIdStr, out var tgId))
            {
                await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                return;
            }

            var requester = _pendingRequesterNames.TryGetValue(tgId, out var reqName) ? reqName : tgId.ToString();

            string token;
            try
            {
                // comment is just the account name — kept parseable so an admin listing
                // (or anything else keyed on it) can rely on its shape
                token = _repo.AddUser(tgId, requester);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[TelegramBot] AddUser failed (tgId={tgId})", ex);
                await bot.AnswerCallbackQuery(cb.Id, "Ошибка записи в users.json, смотри tgbot.log на сервере.", showAlert: true, cancellationToken: ct);
                return;
            }

            _pendingRequesterNames.TryRemove(tgId, out _);
            _lastRequest.TryRemove(tgId, out _);
            FileLog.Write($"[TelegramBot] Доступ выдан tgId={tgId}, admin={cb.From.Id}");
            await bot.AnswerCallbackQuery(cb.Id, "✅  Доступ выдан.", cancellationToken: ct);
            await TryMarkHandledAsync(bot, cb, "✅", requester, ct);

            // if the user requested access from a still-live QR scan, confirming it here
            // logs the deny page in immediately, without the user touching anything —
            // works only while that page is still polling that exact session (~3 min)
            var autoConfirmed = _pendingQrSessions.TryRemove(tgId, out var sessionId)
                && QrAuthSessions.TryConfirm(sessionId, token);
            if (autoConfirmed)
                FileLog.Write($"[TelegramBot] QR-сессия {sessionId} авто-подтверждена при выдаче tgId={tgId}");

            var text = autoConfirmed
                ? $"✅  Администратор выдал вам доступ к Lampa. Вернитесь в Lampa — вход выполнится сам.\n\nЕсли нет — пароль: <code>{token}</code>"
                : $"✅  Администратор выдал вам доступ к Lampa.\n\nВернитесь на экран входа Lampa и войдите через Telegram ещё раз. Либо введите пароль вручную: <code>{token}</code>";

            try
            {
                await bot.SendMessage(tgId, text, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html, replyMarkup: PasswordKeyboard, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[TelegramBot] notify user {tgId} failed", ex);
            }
        }

        async Task HandleDenyAsync(ITelegramBotClient bot, CallbackQuery cb, string tgIdStr, CancellationToken ct)
        {
            if (!IsAdmin(cb.From.Id))
            {
                await bot.AnswerCallbackQuery(cb.Id, "Недоступно.", showAlert: true, cancellationToken: ct);
                return;
            }
            if (!long.TryParse(tgIdStr, out var tgId))
            {
                await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);
                return;
            }

            var requester = _pendingRequesterNames.TryGetValue(tgId, out var reqName) ? reqName : tgId.ToString();
            _pendingRequesterNames.TryRemove(tgId, out _);
            _pendingQrSessions.TryRemove(tgId, out _);
            _lastRequest.TryRemove(tgId, out _);
            await bot.AnswerCallbackQuery(cb.Id, "Отклонено.", cancellationToken: ct);
            await TryMarkHandledAsync(bot, cb, "❌", requester, ct);

            try
            {
                await bot.SendMessage(tgId, "❌  Администратор отклонил заявку на доступ.", cancellationToken: ct);
            }
            catch (Exception ex)
            {
                FileLog.Write($"[TelegramBot] notify user {tgId} failed (deny)", ex);
            }
        }

        // Replaces the whole request block with one compact line instead of appending a
        // result under it — appending left a permanently growing 2-line block per request
        // in the admin's chat (request text never shrank once handled), which is what
        // made a busy admin's history unreadable.
        //
        // Best-effort: purely cosmetic (tidies the admin's own chat), so a failure here
        // (message too old to edit, cb.Message missing after a bot restart, etc.) must
        // not stop the caller from going on to actually notify the requesting user —
        // grant/deny already happened in users.json regardless of whether this succeeds.
        static async Task TryMarkHandledAsync(ITelegramBotClient bot, CallbackQuery cb, string icon, string requester, CancellationToken ct)
        {
            try
            {
                long chatId = cb.Message?.Chat.Id ?? 0;
                int msgId = cb.Message?.MessageId ?? 0;
                var resultText = $"{icon}  {requester}";

                // the request card can be a photo message now (profile photo attached) — those
                // take EditMessageCaption, not EditMessageText, or the API rejects the edit
                if (cb.Message?.Photo is { Length: > 0 })
                    await bot.EditMessageCaption(chatId, msgId, caption: resultText, cancellationToken: ct);
                else
                    await bot.EditMessageText(chatId, msgId, resultText, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                FileLog.Write("[TelegramBot] TryMarkHandledAsync failed", ex);
            }
        }
    }
}
