using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using TelegramBot.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot
{
    public class ModInit : IModuleLoaded
    {
        #region Static data
        public static TelegramBotConf Config { get; private set; } = new();
        public static ConcurrentDictionary<long, TgUser> Users { get; private set; } = new();
        public static ConcurrentDictionary<string, List<Subscription>> Subs { get; private set; } = new();
        public static TelegramBotClient Bot { get; private set; }
        public static bool IsRunning { get; private set; }
        public static string modpath { get; private set; }

        static string DataDir => string.IsNullOrWhiteSpace(Config?.data_dir) ? "database/tgnotify" : Config.data_dir;
        static string configPath => System.IO.Path.Combine(DataDir, "config.json");
        static string subscriptionsPath => System.IO.Path.Combine(DataDir, "subscriptions.json");
        static string usersPath => System.IO.Path.Combine(DataDir, "users.json");
        static readonly HttpClient http = CreateHttpClient();
        static Timer checkTimer;

        static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "LampaNotifications/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        // Быстрый клиент для балансеров (Mirage/Collaps/RHS) — короткий таймаут
        static readonly HttpClient httpFast = CreateFastHttpClient();
        static HttpClient CreateFastHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "LampaNotifications/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(8);
            return client;
        }

        // Кеш для озвучек и поиска (ключ → (результат, время кеширования))
        static readonly ConcurrentDictionary<string, (object result, DateTime cached_at)> _cache = new();
        static readonly TimeSpan CacheTTL = TimeSpan.FromMinutes(30);

        static T GetCache<T>(string key) where T : class
        {
            if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.cached_at < CacheTTL)
                return entry.result as T;
            return null;
        }

        static void SetCache(string key, object value)
        {
            _cache[key] = (value, DateTime.UtcNow);
            // Чистим старые записи (раз в 100 записей)
            if (_cache.Count > 200)
                foreach (var k in _cache.Where(kv => DateTime.UtcNow - kv.Value.cached_at > CacheTTL).Select(kv => kv.Key).ToList())
                    _cache.TryRemove(k, out _);
        }
        #endregion

        #region Models
        public class TgUser
        {
            public long chat_id { get; set; }
            public string username { get; set; }
            public string lampac_uid { get; set; }
            public DateTime linked_at { get; set; }
        }

        public class Subscription
        {
            public long chat_id { get; set; }
            public int tmdb_id { get; set; }
            public string media_type { get; set; } = "tv";
            public string title { get; set; }
            public string voice { get; set; }         // Озвучка: "LostFilm", "HDrezka Studio" и т.д. Пусто = любая
            public string voice_source { get; set; }  // "mirage" или "collaps"
            public string mirage_orid { get; set; }   // ID сериала в Mirage
            public int mirage_voice_id { get; set; }  // ID озвучки (параметр t=)
            public string collaps_orid { get; set; }  // ID сериала в Collaps
            public int kp_id { get; set; }            // kinopoisk_id для VideoHub (резолвится один раз и кешируется)
            public int last_season { get; set; }
            public int last_episode { get; set; }
            public int last_voice_episode { get; set; } // Последняя серия в озвучке
            public DateTime subscribed_at { get; set; }
        }
        #endregion

        public void Loaded(InitspaceModel initspace)
        {
            modpath = initspace.path;

            UpdateConfFromInit();
            EventListener.UpdateInitFile += UpdateConfFromInit;

            Directory.CreateDirectory(DataDir);
            LoadUsers(); LoadSubscriptions();

            if (!Config.enable)
            {
                Console.WriteLine("\n\t[TelegramBot] Модуль отключён (TelegramBot.enable=false в init.conf)\n");
                return;
            }

            if (string.IsNullOrWhiteSpace(Config.bot_token) || Config.bot_token == "YOUR_BOT_TOKEN")
            {
                Console.WriteLine("\n\t[TelegramBot] Установите bot_token в секции \"TelegramBot\" файла init.conf\n");
                return;
            }

            if (IsRunning) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var cts = new CancellationTokenSource();
                    Bot = new TelegramBotClient(Config.bot_token, cancellationToken: cts.Token);
                    var me = await Bot.GetMe();
                    IsRunning = true;
                    Console.WriteLine($"\n\t[TelegramBot] @{me.Username} запущен!\n");

                    // Устанавливаем команды в меню бота
                    try
                    {
                        await Bot.SetMyCommands(new[]
                        {
                            new BotCommand { Command = "list", Description = "📋 Мои подписки" },
                            new BotCommand { Command = "check", Description = "🔍 Проверить сейчас" },
                            new BotCommand { Command = "help", Description = "📖 Помощь" },
                            new BotCommand { Command = "unlink", Description = "🔓 Отвязать аккаунт" }
                        });
                        Console.WriteLine("[TelegramBot] Bot commands set");
                    }
                    catch (Exception ex) { Console.WriteLine($"[TelegramBot] SetMyCommands error: {ex.Message}"); }

                    checkTimer = new Timer(async _ =>
                    {
                        try { await CheckAll(); }
                        catch (Exception ex) { Console.WriteLine($"[TelegramBot] Timer error: {ex.Message}"); }
                    }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(Config.check_interval_minutes));

                    Console.WriteLine("[TelegramBot] Starting polling loop...");
                    await HandleUpdates(cts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TelegramBot] Fatal: {ex}\n");
                    IsRunning = false;
                }
            });
        }

        void UpdateConfFromInit()
        {
            Config = ModuleInvoke.Init("TelegramBot", new TelegramBotConf
            {
                enable = true,
                bot_token = "YOUR_BOT_TOKEN",
                tmdb_api_key = "",
                trakt_client_id = "",
                lampac_host = "http://127.0.0.1:9118",
                lampac_token = "",
                check_interval_minutes = 60,
                tmdb_lang = "ru-RU",
                data_dir = "database/tgnotify"
            });
        }

        #region Telegram polling
        static async Task HandleUpdates(CancellationToken ct)
        {
            int offset = 0;
            Console.WriteLine("[TelegramBot] HandleUpdates started");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var updates = await Bot.GetUpdates(offset, timeout: 30, cancellationToken: ct);
                    foreach (var update in updates)
                    {
                        offset = update.Id + 1;
                        try
                        {
                            if (update.Message?.Text != null)
                            {
                                Console.WriteLine($"[TelegramBot] Msg: {update.Message.Text}");
                                await HandleMessage(update.Message);
                            }
                            else if (update.CallbackQuery != null)
                                await HandleCallback(update.CallbackQuery);
                        }
                        catch (Exception ex) { Console.WriteLine($"[TelegramBot] Handle error: {ex}"); }
                    }
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TelegramBot] Poll error: {ex.Message}");
                    await Task.Delay(5000, ct);
                }
            }
        }

        static ReplyKeyboardMarkup MainKeyboard => new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "📋 Подписки", "🔍 Проверить" },
            new KeyboardButton[] { "📖 Помощь", "🔓 Отвязать" }
        })
        { ResizeKeyboard = true };

        static async Task HandleMessage(Message msg)
        {
            var chatId = msg.Chat.Id;
            var text = msg.Text.Trim();

            if (text.StartsWith("/start"))
            {
                var parts = text.Split(' ');
                if (parts.Length > 1 && parts[1].StartsWith("link_"))
                {
                    var uid = parts[1].Substring(5);
                    Users[chatId] = new TgUser { chat_id = chatId, username = msg.From?.Username ?? "", lampac_uid = uid, linked_at = DateTime.UtcNow };
                    SaveUsers();
                    await Bot.SendMessage(chatId, "✅ *Аккаунт привязан!*\n\nИспользуйте кнопки ниже для управления.", parseMode: ParseMode.Markdown, replyMarkup: MainKeyboard);
                    return;
                }
                await Bot.SendMessage(chatId, "🎬 *Lampa Notifications Bot*\n\nУведомления о новых сериях и озвучках.", parseMode: ParseMode.Markdown, replyMarkup: MainKeyboard);
            }
            else if (text == "/list" || text == "📋 Подписки") await ShowSubscriptions(chatId);
            else if (text == "/check" || text == "🔍 Проверить")
            {
                Console.WriteLine($"[TelegramBot] /check from {chatId}");
                // Запускаем в фоне чтобы не блокировать polling
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Bot.SendMessage(chatId, "🔍 Проверяю...");
                        await CheckAll(chatId);
                    }
                    catch (Exception ex) { Console.WriteLine($"[TelegramBot] /check error: {ex}"); }
                });
            }
            else if (text == "/help" || text == "📖 Помощь")
            {
                await Bot.SendMessage(chatId,
                    "📖 *Помощь*\n\n" +
                    "• Карточка сериала → 🔔 → выберите озвучку\n" +
                    "• Бот пришлёт уведомление когда появится серия в этой озвучке\n" +
                    "• Или подпишитесь на «Любая озвучка» — уведомление при выходе оригинала\n\n" +
                    "Кнопки:\n" +
                    "📋 — список подписок\n" +
                    "🔍 — проверить новые серии\n" +
                    "🔓 — отвязать аккаунт",
                    parseMode: ParseMode.Markdown, replyMarkup: MainKeyboard);
            }
            else if (text == "/unlink" || text == "🔓 Отвязать")
            {
                if (Users.TryRemove(chatId, out _))
                {
                    foreach (var kvp in Subs) kvp.Value.RemoveAll(s => s.chat_id == chatId);
                    SaveUsers(); SaveSubscriptions();
                    await Bot.SendMessage(chatId, "🔓 Аккаунт отвязан.", replyMarkup: new ReplyKeyboardRemove());
                }
                else
                {
                    await Bot.SendMessage(chatId, "Аккаунт не привязан.");
                }
            }
        }

        static async Task HandleCallback(CallbackQuery cb)
        {
            if (cb.Data.StartsWith("unsub_"))
            {
                var tmdbId = cb.Data.Substring(6);
                if (Subs.TryGetValue(tmdbId, out var list) && list.RemoveAll(s => s.chat_id == cb.Message.Chat.Id) > 0)
                {
                    SaveSubscriptions();
                    await Bot.AnswerCallbackQuery(cb.Id, "Удалено ✅");
                    await ShowSubscriptions(cb.Message.Chat.Id);
                    return;
                }
                await Bot.AnswerCallbackQuery(cb.Id, "Не найдено");
            }
        }

        static async Task ShowSubscriptions(long chatId)
        {
            var userSubs = Subs.SelectMany(kvp => kvp.Value).Where(s => s.chat_id == chatId).OrderByDescending(s => s.subscribed_at).ToList();
            if (userSubs.Count == 0) { await Bot.SendMessage(chatId, "📋 Нет подписок."); return; }

            var text = "📋 *Ваши подписки:*\n\n";
            var buttons = new List<List<InlineKeyboardButton>>();
            foreach (var sub in userSubs)
            {
                var v = string.IsNullOrEmpty(sub.voice) ? "Любая озвучка" : sub.voice;
                text += $"• *{EscapeMd(sub.title)}* — {v}\n  S{sub.last_season:D2}E{sub.last_episode:D2}";
                if (sub.last_voice_episode > 0) text += $" (озв: E{sub.last_voice_episode:D2})";
                text += "\n";
                buttons.Add(new List<InlineKeyboardButton> {
                    InlineKeyboardButton.WithCallbackData($"❌ {sub.title} ({v})", $"unsub_{sub.tmdb_id}")
                });
            }
            await Bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: new InlineKeyboardMarkup(buttons));
        }
        #endregion

        #region Проверка — главная
        public static async Task CheckAll(long? onlyChatId = null)
        {
            Console.WriteLine($"[TelegramBot] CheckAll started, onlyChatId={onlyChatId}");
            var allSubs = Subs.SelectMany(kvp => kvp.Value).ToList();
            if (onlyChatId.HasValue) allSubs = allSubs.Where(s => s.chat_id == onlyChatId.Value).ToList();

            int notified = 0;

            foreach (var sub in allSubs.Where(s => s.media_type == "tv"))
            {
                try
                {
                    bool hasVoice = !string.IsNullOrEmpty(sub.voice);
                    bool hasMirage = !string.IsNullOrEmpty(sub.mirage_orid);
                    bool hasCollaps = !string.IsNullOrEmpty(sub.collaps_orid);

                    // 1. Проверить новые серии (Trakt/TMDB)
                    var prevSeason = sub.last_season;
                    var newEpisodes = await CheckNewEpisodes(sub);
                    foreach (var ep in newEpisodes)
                    {
                        if (!hasVoice)
                        {
                            await SendEpisodeNotification(sub, ep);
                            notified++;
                        }
                        sub.last_season = ep.season;
                        sub.last_episode = ep.episode;
                    }

                    // Если сезон сменился — обнуляем счётчик озвучек
                    if (sub.last_season > prevSeason && sub.last_voice_episode > 0)
                    {
                        Console.WriteLine($"[TelegramBot] Season changed {prevSeason}→{sub.last_season} for {sub.title}, resetting voice episode {sub.last_voice_episode}→0");
                        sub.last_voice_episode = 0;
                    }

                    // 2. Проверить озвучки — все источники параллельно
                    if (hasVoice)
                    {
                        int voiceEps = sub.last_voice_episode;

                        var voiceTasks = new List<Task<int>>();
                        if (hasMirage) voiceTasks.Add(CheckMirageVoice(sub));
                        voiceTasks.Add(CheckCollapsVoice(sub));
                        voiceTasks.Add(CheckRHSVoice(sub));
                        voiceTasks.Add(CheckVideoHubVoice(sub));

                        await Task.WhenAll(voiceTasks);

                        foreach (var task in voiceTasks)
                            if (task.Result > voiceEps)
                                voiceEps = task.Result;

                        if (voiceEps > sub.last_voice_episode)
                        {
                            int fromEp = sub.last_voice_episode + 1;
                            int toEp = voiceEps;

                            for (int e = fromEp; e <= toEp; e++)
                            {
                                var (epTitle, epOverview, imgUrl) = await GetTmdbEpisodeInfo(sub.tmdb_id, sub.last_season, e);

                                var msg = $"🎬 *{EscapeMd(sub.title)}*\n🎙 {EscapeMd(sub.voice)}\n📺 S{sub.last_season:D2}E{e:D2}";
                                if (!string.IsNullOrEmpty(epTitle)) msg += $" — _{EscapeMd(epTitle)}_";
                                if (!string.IsNullOrEmpty(epOverview))
                                {
                                    var desc = epOverview.Length > 300 ? epOverview.Substring(0, 300) + "..." : epOverview;
                                    msg += $"\n\n{EscapeMd(desc)}";
                                }

                                try
                                {
                                    if (!string.IsNullOrEmpty(imgUrl))
                                        await Bot.SendPhoto(sub.chat_id, InputFile.FromUri(imgUrl), caption: msg, parseMode: ParseMode.Markdown);
                                    else
                                        await Bot.SendMessage(sub.chat_id, msg, parseMode: ParseMode.Markdown);
                                    notified++;
                                }
                                catch (Exception ex) { Console.WriteLine($"[TelegramBot] Send voice error: {ex.Message}"); }
                            }

                            sub.last_voice_episode = voiceEps;
                            Console.WriteLine($"[TelegramBot] Voice update: {sub.title} {sub.voice} now E{voiceEps}");
                        }
                    }

                    await Task.Delay(500);
                }
                catch (Exception ex) { Console.WriteLine($"[TelegramBot] Check error {sub.tmdb_id}: {ex.Message}"); }
            }

            SaveSubscriptions();
            Console.WriteLine($"[TelegramBot] CheckAll done, notified={notified}");

            if (onlyChatId.HasValue && Bot != null)
                await Bot.SendMessage(onlyChatId.Value, notified > 0 ? $"✅ Уведомлений: {notified}" : "Новых серий/озвучек не найдено.");
        }

        static async Task SendEpisodeNotification(Subscription sub, EpisodeInfo ep)
        {
            var voice = string.IsNullOrEmpty(sub.voice) ? "" : $"\n🎙 {EscapeMd(sub.voice)}";
            var message = $"🎬 *{EscapeMd(ep.show_name)}*\n📺 S{ep.season:D2}E{ep.episode:D2}";
            if (!string.IsNullOrEmpty(ep.title)) message += $" — _{EscapeMd(ep.title)}_";
            message += voice + $"\n📅 {ep.air_date}";

            if (!string.IsNullOrEmpty(ep.overview))
            {
                var desc = ep.overview.Length > 300 ? ep.overview.Substring(0, 300) + "..." : ep.overview;
                message += $"\n\n{EscapeMd(desc)}";
            }

            try
            {
                if (!string.IsNullOrEmpty(ep.image_url))
                    await Bot.SendPhoto(sub.chat_id, InputFile.FromUri(ep.image_url), caption: message, parseMode: ParseMode.Markdown);
                else
                    await Bot.SendMessage(sub.chat_id, message, parseMode: ParseMode.Markdown);
            }
            catch (Exception ex) { Console.WriteLine($"[TelegramBot] Send error: {ex.Message}"); }
        }
        #endregion

        #region Проверка новых эпизодов (Trakt → TMDB)
        class EpisodeInfo
        {
            public string show_name { get; set; }
            public int season { get; set; }
            public int episode { get; set; }
            public string title { get; set; }
            public string air_date { get; set; }
            public string overview { get; set; }
            public string image_url { get; set; }
        }

        static async Task<List<EpisodeInfo>> CheckNewEpisodes(Subscription sub)
        {
            bool useTrakt = !string.IsNullOrEmpty(Config.trakt_client_id);
            if (useTrakt)
            {
                try
                {
                    var (found, episodes) = await CheckViaTrakt(sub);
                    if (found)
                        return episodes; // Trakt нашёл шоу — верим только ему, даже если 0 новых серий

                    // Trakt не нашёл шоу — fallback на TMDB
                    Console.WriteLine($"[TelegramBot] Trakt: show {sub.tmdb_id} not found, using TMDB");
                    return await CheckViaTmdb(sub);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TelegramBot] Trakt error for {sub.tmdb_id}: {ex.Message}, using TMDB");
                    try { return await CheckViaTmdb(sub); } catch { }
                    return new List<EpisodeInfo>();
                }
            }

            try { return await CheckViaTmdb(sub); }
            catch { return new List<EpisodeInfo>(); }
        }

        static async Task<(bool found, List<EpisodeInfo> episodes)> CheckViaTrakt(Subscription sub)
        {
            var result = new List<EpisodeInfo>();

            var searchReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.trakt.tv/search/tmdb/{sub.tmdb_id}?type=show");
            searchReq.Headers.Add("trakt-api-key", Config.trakt_client_id);
            searchReq.Headers.Add("trakt-api-version", "2");
            var searchResp = await http.SendAsync(searchReq);

            Console.WriteLine($"[TelegramBot] Trakt search tmdb/{sub.tmdb_id}: status={searchResp.StatusCode}");

            if (!searchResp.IsSuccessStatusCode)
            {
                Console.WriteLine($"[TelegramBot] Trakt search failed: {await searchResp.Content.ReadAsStringAsync()}");
                return (false, result);
            }

            var searchBody = await searchResp.Content.ReadAsStringAsync();
            var searchData = JArray.Parse(searchBody);

            Console.WriteLine($"[TelegramBot] Trakt search results: {searchData.Count} items");

            if (searchData.Count == 0) return (false, result);

            var showName = searchData[0]?["show"]?.Value<string>("title") ?? sub.title;
            var traktSlug = searchData[0]?["show"]?["ids"]?.Value<string>("slug");
            var traktId = searchData[0]?["show"]?["ids"]?.Value<string>("trakt");
            var showId = traktSlug ?? traktId;

            Console.WriteLine($"[TelegramBot] Trakt found: {showName}, slug={traktSlug}, id={traktId}, using={showId}");

            if (string.IsNullOrEmpty(showId)) return (false, result);

            // Шоу найдено — с этого момента верим только Trakt
            var seasonsReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.trakt.tv/shows/{showId}/seasons?extended=full");
            seasonsReq.Headers.Add("trakt-api-key", Config.trakt_client_id);
            seasonsReq.Headers.Add("trakt-api-version", "2");
            var seasonsResp = await http.SendAsync(seasonsReq);
            if (!seasonsResp.IsSuccessStatusCode) return (true, result); // шоу найдено, но сезоны недоступны — не fallback

            var seasons = JArray.Parse(await seasonsResp.Content.ReadAsStringAsync());
            var seasonsToCheck = seasons
                .Where(s => s.Value<int>("number") >= sub.last_season && s.Value<int>("number") > 0)
                .Where(s => s.Value<int>("aired_episodes") > 0 || s.Value<int>("episode_count") > 0)
                .Select(s => s.Value<int>("number")).OrderBy(n => n).ToList();

            foreach (var seasonNum in seasonsToCheck)
            {
                var epsReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.trakt.tv/shows/{showId}/seasons/{seasonNum}?extended=full");
                epsReq.Headers.Add("trakt-api-key", Config.trakt_client_id);
                epsReq.Headers.Add("trakt-api-version", "2");
                var epsResp = await http.SendAsync(epsReq);
                if (!epsResp.IsSuccessStatusCode) continue;

                foreach (JObject ep in JArray.Parse(await epsResp.Content.ReadAsStringAsync()))
                {
                    int sNum = ep.Value<int>("season"), eNum = ep.Value<int>("number");
                    var firstAired = ep.Value<string>("first_aired");
                    if (sNum < sub.last_season || (sNum == sub.last_season && eNum <= sub.last_episode)) continue;
                    if (string.IsNullOrEmpty(firstAired)) continue;
                    if (!DateTime.TryParse(firstAired, null, System.Globalization.DateTimeStyles.RoundtripKind, out var airDt)) continue;
                    if (airDt > DateTime.UtcNow) continue;

                    // Получаем русское описание и картинку из TMDB
                    var (tmdbTitle, tmdbOverview, tmdbImage) = await GetTmdbEpisodeInfo(sub.tmdb_id, sNum, eNum);

                    result.Add(new EpisodeInfo
                    {
                        show_name = sub.title, // Русское название из подписки
                        season = sNum,
                        episode = eNum,
                        title = !string.IsNullOrEmpty(tmdbTitle) ? tmdbTitle : (ep.Value<string>("title") ?? ""),
                        air_date = airDt.ToString("yyyy-MM-dd HH:mm UTC"),
                        overview = !string.IsNullOrEmpty(tmdbOverview) ? tmdbOverview : (ep.Value<string>("overview") ?? ""),
                        image_url = tmdbImage
                    });
                }
                await Task.Delay(300);
            }
            return (true, result);
        }

        static async Task<List<EpisodeInfo>> CheckViaTmdb(Subscription sub)
        {
            var result = new List<EpisodeInfo>();
            var show = JObject.Parse(await http.GetStringAsync($"https://api.themoviedb.org/3/tv/{sub.tmdb_id}?api_key={Config.tmdb_api_key}&language={Config.tmdb_lang}"));
            var lastSeason = show.Value<int>("number_of_seasons");
            var showName = show.Value<string>("name") ?? sub.title;
            var season = JObject.Parse(await http.GetStringAsync($"https://api.themoviedb.org/3/tv/{sub.tmdb_id}/season/{lastSeason}?api_key={Config.tmdb_api_key}&language={Config.tmdb_lang}"));
            var episodes = season["episodes"] as JArray;
            if (episodes == null) return result;

            foreach (JObject ep in episodes)
            {
                int sNum = ep.Value<int>("season_number"), eNum = ep.Value<int>("episode_number");
                var airDate = ep.Value<string>("air_date");
                if (sNum < sub.last_season || (sNum == sub.last_season && eNum <= sub.last_episode)) continue;
                if (string.IsNullOrEmpty(airDate) || !DateTime.TryParse(airDate, out var airDt) || airDt.Date > DateTime.UtcNow.Date) continue;

                var still = ep.Value<string>("still_path");
                result.Add(new EpisodeInfo
                {
                    show_name = showName,
                    season = sNum,
                    episode = eNum,
                    title = ep.Value<string>("name") ?? "",
                    air_date = airDate,
                    image_url = !string.IsNullOrEmpty(still) ? $"https://image.tmdb.org/t/p/w500{still}" : null
                });
            }
            return result;
        }

        static async Task<string> GetTmdbStill(int tmdbId, int season, int episode)
        {
            try
            {
                var data = JObject.Parse(await http.GetStringAsync($"https://api.themoviedb.org/3/tv/{tmdbId}/season/{season}/episode/{episode}?api_key={Config.tmdb_api_key}"));
                var still = data.Value<string>("still_path");
                return !string.IsNullOrEmpty(still) ? $"https://image.tmdb.org/t/p/w500{still}" : null;
            }
            catch { return null; }
        }

        // Получить полную информацию об эпизоде (название, описание, картинка)
        static async Task<(string title, string overview, string imageUrl)> GetTmdbEpisodeInfo(int tmdbId, int season, int episode)
        {
            try
            {
                var data = JObject.Parse(await http.GetStringAsync(
                    $"https://api.themoviedb.org/3/tv/{tmdbId}/season/{season}/episode/{episode}?api_key={Config.tmdb_api_key}&language={Config.tmdb_lang}"));
                var still = data.Value<string>("still_path");
                var title = data.Value<string>("name") ?? "";
                var overview = data.Value<string>("overview") ?? "";
                var imageUrl = !string.IsNullOrEmpty(still) ? $"https://image.tmdb.org/t/p/w500{still}" : null;

                // Если описание пустое на русском, попробуем английский
                if (string.IsNullOrEmpty(overview))
                {
                    try
                    {
                        var dataEn = JObject.Parse(await http.GetStringAsync(
                            $"https://api.themoviedb.org/3/tv/{tmdbId}/season/{season}/episode/{episode}?api_key={Config.tmdb_api_key}&language=en-US"));
                        overview = dataEn.Value<string>("overview") ?? "";
                        if (string.IsNullOrEmpty(title)) title = dataEn.Value<string>("name") ?? "";
                    }
                    catch { }
                }

                return (title, overview, imageUrl);
            }
            catch { return ("", "", null); }
        }
        #endregion

        #region Mirage — проверка озвучек
        static async Task<int> CheckMirageVoice(Subscription sub)
        {
            if (string.IsNullOrEmpty(sub.mirage_orid) || string.IsNullOrEmpty(Config.lampac_host))
                return sub.last_voice_episode;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var url = $"{Config.lampac_host}/lite/mirage?rjson=true&s={sub.last_season}&t={sub.mirage_voice_id}&orid={sub.mirage_orid}{token}";

                Console.WriteLine($"[TelegramBot] Mirage check: {sub.title} voice={sub.voice} s={sub.last_season} t={sub.mirage_voice_id}");

                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return sub.last_voice_episode;

                int maxEp = 0;
                try
                {
                    var json = JObject.Parse(body);
                    var data = json["data"] as JArray;
                    if (data != null)
                        foreach (var ep in data)
                        {
                            var e = ep.Value<int?>("e") ?? 0;
                            if (e > maxEp) maxEp = e;
                        }
                }
                catch
                {
                    // Fallback HTML парсинг
                    var matches = Regex.Matches(body, @"e=""(\d+)""");
                    foreach (Match m in matches)
                        if (int.TryParse(m.Groups[1].Value, out var ep) && ep > maxEp) maxEp = ep;
                }

                Console.WriteLine($"[TelegramBot] Mirage result: {sub.title} voice={sub.voice} episodes={maxEp} (was {sub.last_voice_episode})");
                return maxEp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelegramBot] Mirage error: {ex.Message}");
                return sub.last_voice_episode;
            }
        }

        public static async Task<List<VoiceInfo>> GetMirageVoices(string orid, int season)
        {
            var cacheKey = $"mirage_voices:{orid}:{season}";
            var cached = GetCache<List<VoiceInfo>>(cacheKey);
            if (cached != null) return cached;

            var result = new List<VoiceInfo>();
            if (string.IsNullOrEmpty(orid) || string.IsNullOrEmpty(Config.lampac_host)) return result;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var url = $"{Config.lampac_host}/lite/mirage?rjson=true&s={season}&orid={orid}{token}";
                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return result;

                try
                {
                    var json = JObject.Parse(body);
                    var voices = json["voice"] as JArray;
                    if (voices != null)
                        foreach (var v in voices)
                        {
                            var name = v.Value<string>("name") ?? "";
                            if (!string.IsNullOrEmpty(name))
                            {
                                int tid = 0;
                                var vUrl = v.Value<string>("url") ?? "";
                                var tm = Regex.Match(vUrl, @"t=(\d+)");
                                if (tm.Success) int.TryParse(tm.Groups[1].Value, out tid);
                                result.Add(new VoiceInfo { id = tid, name = name, source = "mirage" });
                            }
                        }
                }
                catch
                {
                    // Fallback HTML
                    var matches = Regex.Matches(body, @"t=(\d+)&[^""]*""[^>]*>([^<]+)</div>");
                    foreach (Match m in matches)
                        if (int.TryParse(m.Groups[1].Value, out var tid))
                            result.Add(new VoiceInfo { id = tid, name = m.Groups[2].Value.Trim(), source = "mirage" });
                }
            }
            catch (Exception ex) { Console.WriteLine($"[TelegramBot] GetMirageVoices error: {ex.Message}"); }

            if (result.Count > 0) SetCache(cacheKey, result);
            return result;
        }

        public static async Task<List<MirageSearchResult>> SearchMirage(string title, int year)
        {
            var cacheKey = $"mirage_search:{title}:{year}";
            var cached = GetCache<List<MirageSearchResult>>(cacheKey);
            if (cached != null) return cached;

            var result = new List<MirageSearchResult>();
            if (string.IsNullOrEmpty(Config.lampac_host)) return result;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";

                // Помощник: запрос с проглатыванием 503/500/ошибок (возвращает null вместо исключения)
                async Task<string> TryGet(string u)
                {
                    try { return await httpFast.GetStringAsync(u); }
                    catch { return null; }
                }

                var url = $"{Config.lampac_host}/lite/mirage-search?rjson=true&title={Uri.EscapeDataString(title)}&year={year}{token}";
                var body = await TryGet(url);

                if (string.IsNullOrEmpty(body) || body == "null")
                {
                    // Fallback 1: без rjson
                    url = $"{Config.lampac_host}/lite/mirage-search?title={Uri.EscapeDataString(title)}&year={year}{token}";
                    body = await TryGet(url);
                }

                if (string.IsNullOrEmpty(body) || body == "null")
                {
                    // Fallback 2: очищенное название. Mirage отдаёт 503 на цифрах/апострофах/дефисах в запросе.
                    // "Люди Икс '97" -> "Люди Икс"; "Уидоус-Бэй" -> "Уидоус Бэй"
                    var simple = Regex.Replace(title, @"[\u2018\u2019\u02BC'`]", "");
                    simple = Regex.Replace(simple, @"[-\u2013\u2014]", " ");
                    simple = Regex.Replace(simple, @"\d+", "");
                    simple = Regex.Replace(simple, @"\s+", " ").Trim();
                    if (simple.Length >= 3 && !simple.Equals(title, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[TelegramBot] Mirage fallback simplified: '{title}' -> '{simple}'");
                        url = $"{Config.lampac_host}/lite/mirage-search?rjson=true&title={Uri.EscapeDataString(simple)}&year={year}{token}";
                        body = await TryGet(url);
                    }
                }

                if (string.IsNullOrEmpty(body) || body == "null") return result;

                // Попробовать JSON
                try
                {
                    var json = JObject.Parse(body);
                    var data = json["data"] as JArray;
                    if (data != null)
                        foreach (var item in data)
                        {
                            var itemUrl = item.Value<string>("url") ?? "";
                            var itemTitle = item.Value<string>("title") ?? "";
                            var itemYear = item.Value<int?>("year") ?? 0;
                            var oridMatch = Regex.Match(itemUrl, @"orid=([a-f0-9]+)");
                            if (oridMatch.Success)
                                result.Add(new MirageSearchResult { orid = oridMatch.Groups[1].Value, title = itemTitle, year = itemYear });
                        }
                }
                catch
                {
                    // Fallback HTML
                    var matches = Regex.Matches(body, @"data-json='(\{[^']+\})'");
                    foreach (Match m in matches)
                        try
                        {
                            var jj = JObject.Parse(m.Groups[1].Value.Replace("&amp;", "&"));
                            var itemUrl = jj.Value<string>("url") ?? "";
                            var itemTitle = jj.Value<string>("title") ?? "";
                            var itemYear = jj.Value<int?>("year") ?? 0;
                            var oridMatch = Regex.Match(itemUrl, @"orid=([a-f0-9]+)");
                            if (oridMatch.Success)
                                result.Add(new MirageSearchResult { orid = oridMatch.Groups[1].Value, title = itemTitle, year = itemYear });
                        }
                        catch { }
                }

                // Сортировка по точности
                if (result.Count > 1)
                {
                    var searchTitle = title.Trim().ToLowerInvariant();
                    result = result.OrderByDescending(r =>
                    {
                        int score = 0;
                        var it = (r.title ?? "").Trim().ToLowerInvariant();
                        if (it == searchTitle) score += 100;
                        else if (it.Length > 0 && searchTitle.Length > 0 && (it.Contains(searchTitle) || searchTitle.Contains(it)))
                            score += (int)(50.0 * Math.Min(it.Length, searchTitle.Length) / Math.Max(it.Length, searchTitle.Length));
                        if (year > 0 && r.year == year) score += 20;
                        else if (year > 0 && Math.Abs(r.year - year) <= 1) score += 10;
                        return score;
                    }).ToList();
                }

                Console.WriteLine($"[TelegramBot] SearchMirage: '{title}' year={year} -> {result.Count} results, best: {result.FirstOrDefault()?.title} ({result.FirstOrDefault()?.year})");
            }
            catch (Exception ex) { Console.WriteLine($"[TelegramBot] SearchMirage error: {ex.Message}"); }

            if (result.Count > 0) SetCache(cacheKey, result);
            return result;
        }

        public class VoiceInfo { public int id { get; set; } public string name { get; set; } public string source { get; set; } }
        public class MirageSearchResult { public string orid { get; set; } public string title { get; set; } public int year { get; set; } }
        #endregion

        #region Collaps — rjson
        public static async Task<string> SearchCollaps(string title, int year)
        {
            var cacheKey = $"collaps_search:{title}:{year}";
            var cached = GetCache<string>(cacheKey);
            if (cached != null) return cached;

            if (string.IsNullOrEmpty(Config.lampac_host)) return null;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var url = $"{Config.lampac_host}/lite/collaps?rjson=true&title={Uri.EscapeDataString(title)}&year={year}{token}";
                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return null;

                var json = JObject.Parse(body);
                var data = json["data"] as JArray;
                if (data == null || data.Count == 0) return null;

                string bestOrid = null;
                int bestScore = -1;
                foreach (var item in data)
                {
                    var itemUrl = item.Value<string>("url") ?? "";
                    var itemTitle = item.Value<string>("title") ?? "";
                    var itemYear = item.Value<int?>("year") ?? 0;
                    var oridMatch = Regex.Match(itemUrl, @"orid=(\d+)");
                    if (!oridMatch.Success) continue;

                    int score = 0;
                    if (itemYear == year) score += 10;
                    else if (Math.Abs(itemYear - year) <= 1) score += 5;
                    if (itemTitle.Equals(title, StringComparison.OrdinalIgnoreCase)) score += 5;
                    if (score > bestScore) { bestScore = score; bestOrid = oridMatch.Groups[1].Value; }
                }

                Console.WriteLine($"[TelegramBot] SearchCollaps: '{title}' year={year} -> orid={bestOrid}");
                if (!string.IsNullOrEmpty(bestOrid)) SetCache(cacheKey, bestOrid);
                return bestOrid;
            }
            catch (Exception ex) { Console.WriteLine($"[TelegramBot] SearchCollaps error: {ex.Message}"); return null; }
        }

        public static async Task<List<VoiceInfo>> GetCollapsVoices(string collapsOrid, int season)
        {
            var cacheKey = $"collaps_voices:{collapsOrid}:{season}";
            var cached = GetCache<List<VoiceInfo>>(cacheKey);
            if (cached != null) return cached;

            var result = new List<VoiceInfo>();
            if (string.IsNullOrEmpty(collapsOrid) || string.IsNullOrEmpty(Config.lampac_host)) return result;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var url = $"{Config.lampac_host}/lite/collaps?rjson=true&orid={collapsOrid}&s={season}{token}";
                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return result;

                var json = JObject.Parse(body);
                var data = json["data"] as JArray;
                if (data == null || data.Count == 0) return result;

                // Берём details из первого эпизода
                var details = data[0]?.Value<string>("details") ?? data[0]?.Value<string>("voice_name") ?? "";
                if (!string.IsNullOrEmpty(details))
                {
                    int id = 1;
                    foreach (var v in details.Split(','))
                    {
                        var name = v.Trim().Trim('"');
                        if (!string.IsNullOrEmpty(name) && name.Length > 1)
                            result.Add(new VoiceInfo { id = id++, name = name, source = "collaps" });
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[TelegramBot] GetCollapsVoices error: {ex.Message}"); }

            if (result.Count > 0) SetCache(cacheKey, result);
            return result;
        }

        static async Task<int> CheckCollapsVoice(Subscription sub)
        {
            if (string.IsNullOrEmpty(Config.lampac_host))
                return sub.last_voice_episode;

            var orid = sub.collaps_orid;
            if (string.IsNullOrEmpty(orid))
            {
                orid = await SearchCollaps(sub.title, 0);
                if (!string.IsNullOrEmpty(orid))
                {
                    sub.collaps_orid = orid;
                    Console.WriteLine($"[TelegramBot] Collaps auto-found orid={orid} for {sub.title}");
                }
                else return sub.last_voice_episode;
            }

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var url = $"{Config.lampac_host}/lite/collaps?rjson=true&orid={orid}&s={sub.last_season}{token}";

                Console.WriteLine($"[TelegramBot] Collaps check: {sub.title} voice={sub.voice} s={sub.last_season}");

                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return sub.last_voice_episode;

                var json = JObject.Parse(body);
                var data = json["data"] as JArray;
                if (data == null) return sub.last_voice_episode;

                int maxEp = 0;
                foreach (var ep in data)
                {
                    var epNum = ep.Value<int?>("e") ?? 0;
                    var details = ep.Value<string>("details") ?? ep.Value<string>("voice_name") ?? "";
                    if (details.Contains(sub.voice, StringComparison.OrdinalIgnoreCase) && epNum > maxEp)
                        maxEp = epNum;
                }

                Console.WriteLine($"[TelegramBot] Collaps result: {sub.title} voice={sub.voice} episodes={maxEp} (was {sub.last_voice_episode})");
                return maxEp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelegramBot] Collaps check error: {ex.Message}");
                return sub.last_voice_episode;
            }
        }
        #endregion

        #region VideoHub (CDNvideohub) — поиск по kinopoisk_id

        // Резолвер tmdb_id -> kinopoisk_id. Порядок: кеш в подписке -> мост Lampac /externalids -> поиск по названию в KP API.
        // Результат кешируется прямо в sub.kp_id, чтобы не дёргать KP API каждый прогон. -1 = резолв не удался (больше не пытаемся).
        public static async Task<int> GetKpId(Subscription sub)
        {
            if (sub.kp_id > 0) return sub.kp_id;
            if (sub.kp_id == -1) return 0; // ранее не нашли — не долбим API повторно

            // 1) Мост Lampac /externalids (бесплатно, без квоты KP API)
            string imdbId = null;
            if (!string.IsNullOrEmpty(Config.lampac_host))
            {
                try
                {
                    var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                    var url = $"{Config.lampac_host}/externalids?id={sub.tmdb_id}&ts=tv{token}";
                    var body = await httpFast.GetStringAsync(url);
                    if (!string.IsNullOrEmpty(body) && body != "null")
                    {
                        var j = JObject.Parse(body);
                        var kpStr = j.Value<string>("kinopoisk_id");
                        imdbId = j.Value<string>("imdb_id");
                        if (int.TryParse(kpStr, out var kp) && kp > 0)
                        {
                            sub.kp_id = kp;
                            Console.WriteLine($"[TelegramBot] KP resolved via externalids: {sub.title} -> kp={kp}");
                            return kp;
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[TelegramBot] externalids error: {ex.Message}"); }
            }

            // 2) Поиск по названию через kinopoiskapiunofficial.tech (нужен ключ kp_api_key)
            if (!string.IsNullOrEmpty(Config.kp_api_key))
            {
                int year = 0;
                string originalTitle = null;
                try
                {
                    var show = JObject.Parse(await http.GetStringAsync(
                        $"https://api.themoviedb.org/3/tv/{sub.tmdb_id}?api_key={Config.tmdb_api_key}&language={Config.tmdb_lang}"));
                    originalTitle = show.Value<string>("original_name");
                    var firstAir = show.Value<string>("first_air_date") ?? "";
                    if (firstAir.Length >= 4) int.TryParse(firstAir.Substring(0, 4), out year);
                }
                catch (Exception ex) { Console.WriteLine($"[TelegramBot] GetKpId TMDB info error: {ex.Message}"); }

                var kp = await SearchKpByTitle(sub.title, year, originalTitle);
                if (kp > 0)
                {
                    sub.kp_id = kp;
                    Console.WriteLine($"[TelegramBot] KP resolved via search: {sub.title} -> kp={kp}");
                    return kp;
                }
            }

            sub.kp_id = -1; // пометка «не нашли», чтобы не пытаться каждый раз
            Console.WriteLine($"[TelegramBot] KP NOT resolved: {sub.title} (tmdb={sub.tmdb_id})");
            return 0;
        }

        // Поиск kinopoisk_id по названию. Точное совпадение nameRu/nameEn + год + type=TV_SERIES имеет приоритет.
        // Нормализация названия для сравнения: убирает апострофы (обычные и типографские),
        // приводит дефисы/двоеточия/точки к пробелам, схлопывает пробелы, lower-case.
        static string NormTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"[\u2018\u2019\u02BC'`]", "");
            s = Regex.Replace(s, @"[-\u2013\u2014:.,]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
            return s;
        }

        // Поиск kinopoisk_id по названию. Сопоставление нормализованное ('97 vs '97, регистр).
        // Приоритет: имя+TV_SERIES+год -> имя+TV_SERIES -> 0. Мусорный fallback (первый попавшийся) убран:
        // лучше пропустить VideoHub, чем взять чужой фильм.
        static async Task<int> SearchKpByTitle(string title, int year, string originalTitle = null)
        {
            if (string.IsNullOrEmpty(Config.kp_api_key) || string.IsNullOrEmpty(title)) return 0;

            var cacheKey = $"kp_search:{title}:{originalTitle}:{year}";
            var cached = GetCache<string>(cacheKey);
            if (cached != null) return int.TryParse(cached, out var c) ? c : 0;

            var queries = new List<string> { title };
            if (!string.IsNullOrEmpty(originalTitle) && !originalTitle.Equals(title, StringComparison.OrdinalIgnoreCase))
                queries.Add(originalTitle);

            var wantRu = NormTitle(title);
            var wantEn = NormTitle(originalTitle);

            try
            {
                int exactSeriesYear = 0, exactSeries = 0, exactAny = 0;

                foreach (var q in queries)
                {
                    var url = $"https://kinopoiskapiunofficial.tech/api/v2.1/films/search-by-keyword?keyword={Uri.EscapeDataString(q)}";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("X-API-KEY", Config.kp_api_key);
                    req.Headers.Add("accept", "application/json");

                    var resp = await httpFast.SendAsync(req);
                    var body = await resp.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(body)) continue;

                    var films = JObject.Parse(body)["films"] as JArray;
                    if (films == null) continue;

                    foreach (var f in films)
                    {
                        var id = f.Value<int?>("filmId") ?? 0;
                        if (id <= 0) continue;
                        var nRu = NormTitle(f.Value<string>("nameRu"));
                        var nEn = NormTitle(f.Value<string>("nameEn"));
                        int.TryParse(f.Value<string>("year"), out var fy);
                        bool isSeries = (f.Value<string>("type") ?? "") == "TV_SERIES";

                        bool nameMatch =
                            (wantRu.Length > 0 && (nRu == wantRu || (nRu.Length > 3 && wantRu.Length > 3 && (nRu.Contains(wantRu) || wantRu.Contains(nRu))))) ||
                            (wantEn.Length > 0 && (nEn == wantEn || (nEn.Length > 3 && wantEn.Length > 3 && (nEn.Contains(wantEn) || wantEn.Contains(nEn)))));

                        if (!nameMatch) continue;

                        if (isSeries && year > 0 && fy == year && exactSeriesYear == 0) exactSeriesYear = id;
                        if (isSeries && exactSeries == 0) exactSeries = id;
                        if (exactAny == 0) exactAny = id;
                    }

                    if (exactSeriesYear != 0) break;
                }

                int result = exactSeriesYear != 0 ? exactSeriesYear
                           : exactSeries != 0 ? exactSeries
                           : exactAny; // 0, если имя нигде не совпало → VideoHub пропустится

                Console.WriteLine($"[TelegramBot] SearchKpByTitle: '{title}' (orig='{originalTitle}') year={year} -> kp={result}");
                SetCache(cacheKey, result.ToString());
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelegramBot] SearchKpByTitle error: {ex.Message}");
                return 0;
            }
        }

        // Список озвучек VideoHub для сезона (для меню плагина). Возвращает VoiceInfo с source="videohub".
        public static async Task<List<VoiceInfo>> GetVideoHubVoices(int kpId, int season)
        {
            var result = new List<VoiceInfo>();
            if (kpId <= 0 || string.IsNullOrEmpty(Config.lampac_host)) return result;

            var cacheKey = $"vhub_voices:{kpId}:{season}";
            var cached = GetCache<List<VoiceInfo>>(cacheKey);
            if (cached != null) return cached;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var url = $"{Config.lampac_host}/lite/cdnvideohub?rjson=true&kinopoisk_id={kpId}&s={season}{token}";
                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return result;

                var json = JObject.Parse(body);
                var voices = json["voice"] as JArray;
                if (voices != null)
                    foreach (var v in voices)
                    {
                        var name = v.Value<string>("name");
                        if (!string.IsNullOrEmpty(name))
                            result.Add(new VoiceInfo { id = 0, name = name, source = "videohub" });
                    }

                SetCache(cacheKey, result);
            }
            catch (Exception ex) { Console.WriteLine($"[TelegramBot] GetVideoHubVoices error: {ex.Message}"); }

            return result;
        }

        // Проверка максимального вышедшего эпизода в нужной озвучке через VideoHub.
        static async Task<int> CheckVideoHubVoice(Subscription sub)
        {
            if (string.IsNullOrEmpty(Config.lampac_host)) return sub.last_voice_episode;

            int kpId = await GetKpId(sub);
            if (kpId <= 0) return sub.last_voice_episode;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                // t = имя озвучки, обязательно URL-кодировать (кириллица иначе даёт HTTP 400)
                var tParam = !string.IsNullOrEmpty(sub.voice) ? $"&t={Uri.EscapeDataString(sub.voice)}" : "";
                var url = $"{Config.lampac_host}/lite/cdnvideohub?rjson=true&kinopoisk_id={kpId}&s={sub.last_season}{tParam}{token}";

                Console.WriteLine($"[TelegramBot] VideoHub check: {sub.title} voice={sub.voice} kp={kpId} s={sub.last_season}");

                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return sub.last_voice_episode;

                var json = JObject.Parse(body);
                var data = json["data"] as JArray;
                if (data == null) return sub.last_voice_episode;

                int maxEp = 0;
                foreach (var ep in data)
                {
                    var epNum = ep.Value<int?>("e") ?? 0;
                    if (epNum > maxEp) maxEp = epNum;
                }

                Console.WriteLine($"[TelegramBot] VideoHub result: {sub.title} voice={sub.voice} episodes={maxEp} (was {sub.last_voice_episode})");
                return maxEp;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelegramBot] VideoHub check error: {ex.Message}");
                return sub.last_voice_episode;
            }
        }
        #endregion

        #region RedHeadSound (RHS) — rjson
        public static async Task<List<VoiceInfo>> GetRHSVoices(string title, int year, int season)
        {
            var cacheKey = $"rhs_voices:{title}:{year}:{season}";
            var cached = GetCache<List<VoiceInfo>>(cacheKey);
            if (cached != null) return cached;

            var result = new List<VoiceInfo>();
            if (string.IsNullOrEmpty(Config.lampac_host)) return result;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var url = $"{Config.lampac_host}/lite/redheadsound?rjson=true&title={Uri.EscapeDataString(title)}&year={year}&s={season}{token}";
                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return result;

                var json = JObject.Parse(body);
                var voices = json["voice"] as JArray;
                if (voices != null)
                    foreach (var v in voices)
                    {
                        var name = v.Value<string>("name") ?? "";
                        if (string.IsNullOrEmpty(name)) continue;

                        int tid = 0;
                        var vUrl = v.Value<string>("url") ?? "";
                        var tm = Regex.Match(vUrl, @"t=(\d+)");
                        if (tm.Success) int.TryParse(tm.Groups[1].Value, out tid);
                        result.Add(new VoiceInfo { id = tid, name = name, source = "rhs" });
                    }

                Console.WriteLine($"[TelegramBot] GetRHSVoices: '{title}' s{season} -> {result.Count} voices");
            }
            catch (Exception ex) { Console.WriteLine($"[TelegramBot] GetRHSVoices error: {ex.Message}"); }

            if (result.Count > 0) SetCache(cacheKey, result);
            return result;
        }

        static async Task<int> CheckRHSVoice(Subscription sub)
        {
            if (string.IsNullOrEmpty(sub.voice) || string.IsNullOrEmpty(Config.lampac_host))
                return sub.last_voice_episode;

            try
            {
                var token = !string.IsNullOrEmpty(Config.lampac_token) ? $"&token={Config.lampac_token}" : "";
                var voiceUrl = $"{Config.lampac_host}/lite/redheadsound?rjson=true&title={Uri.EscapeDataString(sub.title)}&year=0&s={sub.last_season}{token}";

                Console.WriteLine($"[TelegramBot] RHS check: {sub.title} voice={sub.voice} s={sub.last_season}");

                var voiceBody = await httpFast.GetStringAsync(voiceUrl);
                if (string.IsNullOrEmpty(voiceBody) || voiceBody == "null") return sub.last_voice_episode;

                var voiceJson = JObject.Parse(voiceBody);

                // Ищем озвучку по имени → получаем правильный t=
                int rhsVoiceId = 0;
                var voices = voiceJson["voice"] as JArray;
                if (voices != null)
                    foreach (var v in voices)
                    {
                        var vName = v.Value<string>("name") ?? "";
                        if (vName.Equals(sub.voice, StringComparison.OrdinalIgnoreCase))
                        {
                            var vUrl = v.Value<string>("url") ?? "";
                            var tm = Regex.Match(vUrl, @"t=(\d+)");
                            if (tm.Success) int.TryParse(tm.Groups[1].Value, out rhsVoiceId);
                            break;
                        }
                    }

                if (rhsVoiceId == 0)
                {
                    // Озвучка не найдена по имени — пробуем по details в эпизодах
                    var data0 = voiceJson["data"] as JArray;
                    if (data0 != null)
                    {
                        int maxEp = 0;
                        foreach (var ep in data0)
                        {
                            var details = ep.Value<string>("details") ?? ep.Value<string>("voice_name") ?? "";
                            var e = ep.Value<int?>("e") ?? 0;
                            if (details.Contains(sub.voice, StringComparison.OrdinalIgnoreCase) && e > maxEp)
                                maxEp = e;
                        }
                        if (maxEp > 0)
                        {
                            Console.WriteLine($"[TelegramBot] RHS result (details): {sub.title} voice={sub.voice} episodes={maxEp} (was {sub.last_voice_episode})");
                            return maxEp;
                        }
                    }
                    return sub.last_voice_episode;
                }

                // Запрос с правильным t=
                var url = $"{Config.lampac_host}/lite/redheadsound?rjson=true&title={Uri.EscapeDataString(sub.title)}&year=0&s={sub.last_season}&t={rhsVoiceId}{token}";
                var body = await httpFast.GetStringAsync(url);
                if (string.IsNullOrEmpty(body) || body == "null") return sub.last_voice_episode;

                var json = JObject.Parse(body);
                var data = json["data"] as JArray;
                if (data == null) return sub.last_voice_episode;

                int max = 0;
                foreach (var ep in data)
                {
                    var e = ep.Value<int?>("e") ?? 0;
                    if (e > max) max = e;
                }

                Console.WriteLine($"[TelegramBot] RHS result: {sub.title} voice={sub.voice} t={rhsVoiceId} episodes={max} (was {sub.last_voice_episode})");
                return max;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelegramBot] RHS check error: {ex.Message}");
                return sub.last_voice_episode;
            }
        }
        #endregion

        #region API для контроллера
        public static async Task<object> SubscribeApi(string uid, int tmdbId, string title, string voice,
            int season, int episode, string mirageOrid = null, int mirageVoiceId = 0, int voiceEpisode = 0,
            string collapsOrid = null, string voiceSource = null, int kpId = 0)
        {
            var user = Users.Values.FirstOrDefault(u => u.lampac_uid == uid);
            if (user == null) return new { success = false, msg = "not_linked" };

            var key = tmdbId.ToString();
            if (!Subs.ContainsKey(key)) Subs[key] = new List<Subscription>();

            // Удалить старую подписку с той же озвучкой
            Subs[key].RemoveAll(s => s.chat_id == user.chat_id && s.voice == (voice ?? ""));

            Subs[key].Add(new Subscription
            {
                chat_id = user.chat_id,
                tmdb_id = tmdbId,
                title = title,
                voice = voice ?? "",
                voice_source = voiceSource ?? (string.IsNullOrEmpty(mirageOrid) ? "collaps" : "mirage"),
                mirage_orid = mirageOrid ?? "",
                mirage_voice_id = mirageVoiceId,
                collaps_orid = collapsOrid ?? "",
                kp_id = kpId,
                last_season = season,
                last_episode = episode,
                last_voice_episode = voiceEpisode,
                subscribed_at = DateTime.UtcNow
            });
            SaveSubscriptions();

            if (Bot != null)
            {
                var v = string.IsNullOrEmpty(voice) ? "любая озвучка" : voice;
                try { await Bot.SendMessage(user.chat_id, $"🔔 Подписка!\n*{EscapeMd(title)}* — {v}", parseMode: ParseMode.Markdown); } catch { }
            }
            return new { success = true };
        }

        public static object UnsubscribeApi(string uid, int tmdbId, string voice = null)
        {
            var user = Users.Values.FirstOrDefault(u => u.lampac_uid == uid);
            if (user == null) return new { success = false, msg = "not_linked" };

            var key = tmdbId.ToString();
            if (Subs.TryGetValue(key, out var list))
            {
                if (voice != null)
                    list.RemoveAll(s => s.chat_id == user.chat_id && s.voice == voice);
                else
                    list.RemoveAll(s => s.chat_id == user.chat_id);
                SaveSubscriptions();
            }
            return new { success = true };
        }

        public static object StatusApi(string uid, int tmdbId)
        {
            var user = Users.Values.FirstOrDefault(u => u.lampac_uid == uid);
            if (user == null) return new { success = true, subscribed = false, linked = false, voices = new string[0] };

            var userSubs = Subs.TryGetValue(tmdbId.ToString(), out var list)
                ? list.Where(s => s.chat_id == user.chat_id).ToList()
                : new List<Subscription>();

            return new
            {
                success = true,
                linked = true,
                subscribed = userSubs.Count > 0,
                voices = userSubs.Select(s => s.voice).ToArray()
            };
        }

        /// <summary>
        /// Возвращает список подписок пользователя для страницы подписок в Lampa.
        /// Каждая подписка содержит tmdb_id, title, last_season, last_episode,
        /// last_voice_episode, voice, voice_source — всё необходимое для карточки.
        /// </summary>
        public static object SubscriptionsApi(string uid)
        {
            var user = Users.Values.FirstOrDefault(u => u.lampac_uid == uid);
            if (user == null)
                return new { success = true, linked = false, results = new object[0] };

            var userSubs = Subs
                .SelectMany(kvp => kvp.Value)
                .Where(s => s.chat_id == user.chat_id)
                .OrderByDescending(s => s.subscribed_at)
                .Select(s => new
                {
                    tmdb_id = s.tmdb_id,
                    title = s.title,
                    media_type = s.media_type ?? "tv",
                    last_season = s.last_season,
                    last_episode = s.last_episode,
                    last_voice_episode = s.last_voice_episode,
                    voice = s.voice ?? "",
                    voice_source = s.voice_source ?? "",
                    subscribed_at = s.subscribed_at.ToString("o")
                })
                .ToArray();

            return new { success = true, linked = true, results = userSubs };
        }

        public static async Task<object> GetVoicesApi(string title, int year, int season, int tmdbId = 0)
        {
            // Проверяем кеш
            var cacheKey = $"voices:{tmdbId}:{title}:{year}:{season}";
            var cached = GetCache<object>(cacheKey);
            if (cached != null)
            {
                Console.WriteLine($"[TelegramBot] GetVoices cache hit: {title}");
                return cached;
            }

            // Определить актуальный сезон + альтернативное название — параллельно
            int actualSeason = season > 0 ? season : 1;
            var allTitles = new List<string>();

            if (tmdbId > 0)
            {
                // Одним запросом к TMDB получаем и сезон, и альтернативные названия
                try
                {
                    var show = JObject.Parse(await http.GetStringAsync(
                        $"https://api.themoviedb.org/3/tv/{tmdbId}?api_key={Config.tmdb_api_key}&language={Config.tmdb_lang}"));

                    var tmdbName = show.Value<string>("name") ?? "";
                    var tmdbOriginal = show.Value<string>("original_name") ?? "";

                    // Определяем сезон из TMDB данных (быстро, без доп. запросов)
                    var tmdbSeasons = show["seasons"] as JArray;
                    if (tmdbSeasons != null)
                    {
                        int bestSeason = 1;
                        foreach (var s in tmdbSeasons)
                        {
                            var sNum = s.Value<int>("season_number");
                            var epCount = s.Value<int>("episode_count");
                            var airDate = s.Value<string>("air_date");
                            if (sNum <= 0 || epCount <= 0) continue;
                            if (!string.IsNullOrEmpty(airDate) && DateTime.TryParse(airDate, out var dt) && dt.Date <= DateTime.UtcNow.Date)
                                bestSeason = sNum;
                        }
                        if (bestSeason > 0) actualSeason = bestSeason;
                    }
                    else
                    {
                        var ns = show.Value<int>("number_of_seasons");
                        if (ns > 0) actualSeason = ns;
                    }

                    Console.WriteLine($"[TelegramBot] TMDB info: {tmdbName} / {tmdbOriginal}, season={actualSeason}");

                    // Собираем все уникальные названия для поиска
                    // Порядок: Lampa title, TMDB name (ru), TMDB original_name
                    allTitles.Add(title);
                    if (!string.IsNullOrEmpty(tmdbName) && !allTitles.Any(t => t.Equals(tmdbName, StringComparison.OrdinalIgnoreCase)))
                        allTitles.Add(tmdbName);
                    if (!string.IsNullOrEmpty(tmdbOriginal) && !allTitles.Any(t => t.Equals(tmdbOriginal, StringComparison.OrdinalIgnoreCase)))
                        allTitles.Add(tmdbOriginal);
                }
                catch (Exception ex) { Console.WriteLine($"[TelegramBot] TMDB info error: {ex.Message}"); }
            }

            // Список названий для поиска
            var titles = allTitles.Count > 0 ? allTitles : new List<string> { title };
            Console.WriteLine($"[TelegramBot] GetVoices titles: [{string.Join(", ", titles)}] season={actualSeason}");

            string mirageOrid = "";
            string collapsOrid = "";
            var allVoices = new List<VoiceInfo>();

            // === ПАРАЛЛЕЛЬНЫЙ поиск по всем источникам ===
            var mirageTask = SearchMirageMulti(titles, year);
            var collapsTask = SearchCollapsMulti(titles, year);
            var rhsTask = SearchRHSMulti(titles, year, actualSeason);

            await Task.WhenAll(mirageTask, collapsTask, rhsTask);

            // Получаем озвучки из найденных источников — параллельно
            var mirageResult = mirageTask.Result;
            var cOrid = collapsTask.Result;
            var rhsVoices = rhsTask.Result;

            if (mirageResult.orid != null) mirageOrid = mirageResult.orid;
            if (!string.IsNullOrEmpty(cOrid)) collapsOrid = cOrid;

            // Запускаем получение озвучек параллельно
            var mirageVoicesTask = !string.IsNullOrEmpty(mirageOrid) ? GetMirageVoices(mirageOrid, actualSeason) : Task.FromResult(new List<VoiceInfo>());
            var collapsVoicesTask = !string.IsNullOrEmpty(collapsOrid) ? GetCollapsVoices(collapsOrid, actualSeason) : Task.FromResult(new List<VoiceInfo>());

            await Task.WhenAll(mirageVoicesTask, collapsVoicesTask);

            // Собираем результаты: Mirage → Collaps → RHS (без дубликатов)
            foreach (var v in mirageVoicesTask.Result) { v.source = "mirage"; allVoices.Add(v); }

            var existingNames = new HashSet<string>(allVoices.Select(v => v.name), StringComparer.OrdinalIgnoreCase);
            foreach (var v in collapsVoicesTask.Result)
                if (!existingNames.Contains(v.name)) { allVoices.Add(v); existingNames.Add(v.name); }

            foreach (var v in rhsVoices)
                if (!existingNames.Contains(v.name)) { allVoices.Add(v); existingNames.Add(v.name); }

            // === VideoHub: резолвим kp_id по tmdb и добавляем озвучки ===
            int vhubKp = 0;
            if (tmdbId > 0)
            {
                var probe = new Subscription { tmdb_id = tmdbId, title = title };
                vhubKp = await GetKpId(probe);
                if (vhubKp > 0)
                {
                    var vhubVoices = await GetVideoHubVoices(vhubKp, actualSeason);
                    foreach (var v in vhubVoices)
                        if (!existingNames.Contains(v.name)) { allVoices.Add(v); existingNames.Add(v.name); }
                    Console.WriteLine($"[TelegramBot] GetVoices VideoHub: kp={vhubKp} voices={vhubVoices.Count}");
                }
            }

            Console.WriteLine($"[TelegramBot] GetVoices: {title} ({year}) s{actualSeason} — mirage:{mirageOrid} collaps:{collapsOrid} rhs:{rhsVoices.Count} voices:{allVoices.Count}");

            var result = (object)new { success = true, voices = allVoices, orid = mirageOrid, collaps_orid = collapsOrid, kp_id = vhubKp, season = actualSeason };
            if (allVoices.Count > 0) SetCache(cacheKey, result);
            return result;
        }

        // Параллельный поиск по нескольким названиям — все сразу, берём лучший
        static async Task<(string orid, string title)> SearchMirageMulti(List<string> titles, int year)
        {
            var tasks = titles.Select(t => SearchMirage(t, year)).ToArray();
            await Task.WhenAll(tasks);

            // Собираем ВСЕХ кандидатов со всех названий (без дублей по orid)
            var all = new List<MirageSearchResult>();
            var seen = new HashSet<string>();
            foreach (var task in tasks)
                foreach (var r in task.Result)
                    if (!string.IsNullOrEmpty(r.orid) && seen.Add(r.orid))
                        all.Add(r);

            if (all.Count == 0) return (null, null);

            // Приоритет 1: точное совпадение года (главный критерий — локализованные названия часто расходятся)
            if (year > 0)
            {
                var byYear = all.Where(r => r.year == year).ToList();
                if (byYear.Count > 0)
                {
                    // среди совпавших по году — предпочесть совпадение названия с любым из искомых
                    var best = byYear.OrderByDescending(r => TitleScore(r.title, titles)).First();
                    Console.WriteLine($"[TelegramBot] SearchMirageMulti: matched by year={year} -> {best.title} orid={best.orid}");
                    return (best.orid, best.title);
                }
            }

            // Приоритет 2: года нет или не совпал — берём лучшего по названию
            var fallback = all.OrderByDescending(r => TitleScore(r.title, titles)).First();
            return (fallback.orid, fallback.title);
        }

        // Очки совпадения названия кандидата с любым из искомых названий
        static int TitleScore(string candidate, List<string> titles)
        {
            var c = (candidate ?? "").Trim().ToLowerInvariant();
            if (c.Length == 0) return 0;
            int best = 0;
            foreach (var t in titles)
            {
                var s = (t ?? "").Trim().ToLowerInvariant();
                if (s.Length == 0) continue;
                if (c == s) best = Math.Max(best, 100);
                else if (c.Contains(s) || s.Contains(c))
                    best = Math.Max(best, (int)(50.0 * Math.Min(c.Length, s.Length) / Math.Max(c.Length, s.Length)));
            }
            return best;
        }

        static async Task<string> SearchCollapsMulti(List<string> titles, int year)
        {
            if (titles.Count == 1)
                return await SearchCollaps(titles[0], year);

            var tasks = titles.Select(t => SearchCollaps(t, year)).ToArray();
            await Task.WhenAll(tasks);

            foreach (var task in tasks)
                if (!string.IsNullOrEmpty(task.Result))
                    return task.Result;

            return null;
        }

        static async Task<List<VoiceInfo>> SearchRHSMulti(List<string> titles, int year, int season)
        {
            if (titles.Count == 1)
                return await GetRHSVoices(titles[0], year, season);

            var tasks = titles.Select(t => GetRHSVoices(t, year, season)).ToArray();
            await Task.WhenAll(tasks);

            foreach (var task in tasks)
                if (task.Result.Count > 0)
                    return task.Result;

            return new List<VoiceInfo>();
        }

        // Определить последний вышедший сезон
        static async Task<int> GetCurrentSeason(int tmdbId)
        {
            // Сначала Trakt
            if (!string.IsNullOrEmpty(Config.trakt_client_id))
            {
                try
                {
                    var searchReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.trakt.tv/search/tmdb/{tmdbId}?type=show");
                    searchReq.Headers.Add("trakt-api-key", Config.trakt_client_id);
                    searchReq.Headers.Add("trakt-api-version", "2");
                    var searchResp = await http.SendAsync(searchReq);

                    if (searchResp.IsSuccessStatusCode)
                    {
                        var searchData = JArray.Parse(await searchResp.Content.ReadAsStringAsync());
                        if (searchData.Count > 0)
                        {
                            var slug = searchData[0]?["show"]?["ids"]?.Value<string>("slug");
                            if (!string.IsNullOrEmpty(slug))
                            {
                                var sReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.trakt.tv/shows/{slug}/seasons?extended=full");
                                sReq.Headers.Add("trakt-api-key", Config.trakt_client_id);
                                sReq.Headers.Add("trakt-api-version", "2");
                                var sResp = await http.SendAsync(sReq);

                                if (sResp.IsSuccessStatusCode)
                                {
                                    var seasons = JArray.Parse(await sResp.Content.ReadAsStringAsync());
                                    var lastAired = seasons
                                        .Where(s => s.Value<int>("number") > 0 && s.Value<int>("aired_episodes") > 0)
                                        .OrderByDescending(s => s.Value<int>("number"))
                                        .FirstOrDefault();

                                    if (lastAired != null)
                                    {
                                        var result = lastAired.Value<int>("number");
                                        Console.WriteLine($"[TelegramBot] Trakt current season for tmdb {tmdbId}: {result} (total seasons: {seasons.Count})");
                                        return result;
                                    }
                                    else
                                    {
                                        Console.WriteLine($"[TelegramBot] Trakt: no aired seasons found for tmdb {tmdbId}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[TelegramBot] GetCurrentSeason Trakt error: {ex.Message}"); }
            }

            // Fallback — TMDB: проверяем каждый сезон на наличие вышедших эпизодов
            try
            {
                Console.WriteLine($"[TelegramBot] GetCurrentSeason TMDB fallback for {tmdbId}");
                var show = JObject.Parse(await http.GetStringAsync(
                    $"https://api.themoviedb.org/3/tv/{tmdbId}?api_key={Config.tmdb_api_key}&language={Config.tmdb_lang}"));

                var tmdbSeasons = show["seasons"] as JArray;
                if (tmdbSeasons != null)
                {
                    // Ищем последний сезон с episode_count > 0 и air_date <= сегодня
                    int bestSeason = 1;
                    foreach (var s in tmdbSeasons)
                    {
                        var sNum = s.Value<int>("season_number");
                        var epCount = s.Value<int>("episode_count");
                        var airDate = s.Value<string>("air_date");

                        if (sNum <= 0 || epCount <= 0) continue;
                        if (!string.IsNullOrEmpty(airDate) && DateTime.TryParse(airDate, out var dt) && dt.Date <= DateTime.UtcNow.Date)
                            bestSeason = sNum;
                    }
                    Console.WriteLine($"[TelegramBot] TMDB current season for {tmdbId}: {bestSeason}");
                    return bestSeason;
                }

                return show.Value<int>("number_of_seasons");
            }
            catch { return 1; }
        }

        public static async Task<object> LinkApi(string uid)
        {
            if (Bot == null) return new { success = false, msg = "bot_not_running" };
            var me = await Bot.GetMe();
            return new { success = true, link = $"https://t.me/{me.Username}?start=link_{uid}" };
        }
        #endregion

        #region Storage
        static void LoadUsers()
        {
            try { if (System.IO.File.Exists(usersPath)) { var l = JsonConvert.DeserializeObject<List<TgUser>>(System.IO.File.ReadAllText(usersPath)); if (l != null) foreach (var u in l) Users[u.chat_id] = u; } } catch { }
        }
        public static void SaveUsers() { try { System.IO.File.WriteAllText(usersPath, JsonConvert.SerializeObject(Users.Values.ToList(), Formatting.Indented)); } catch { } }
        static void LoadSubscriptions()
        {
            try { if (System.IO.File.Exists(subscriptionsPath)) { var d = JsonConvert.DeserializeObject<Dictionary<string, List<Subscription>>>(System.IO.File.ReadAllText(subscriptionsPath)); if (d != null) foreach (var kvp in d) Subs[kvp.Key] = kvp.Value; } } catch { }
        }
        public static void SaveSubscriptions()
        {
            try { System.IO.File.WriteAllText(subscriptionsPath, JsonConvert.SerializeObject(Subs.ToDictionary(k => k.Key, k => k.Value), Formatting.Indented)); } catch { }
        }
        #endregion

        static string EscapeMd(string text) =>
            string.IsNullOrEmpty(text) ? "" : text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("]", "\\]").Replace("`", "\\`");

        public void Dispose()
        {
            EventListener.UpdateInitFile -= UpdateConfFromInit;
            try { checkTimer?.Dispose(); } catch { }
            IsRunning = false;
        }
    }
}
