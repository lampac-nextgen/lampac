# Tg-notify.bot

Фоновый **Telegram-бот уведомлений о новых сериях и озвучках** (long polling). Пользователь подписывается на сериал прямо из карточки Lampa, а бот сам отслеживает выход новых эпизодов и появление выбранной озвучки и шлёт уведомление в Telegram с обложкой, номером серии и описанием из TMDB.

Проект: **`Modules/Tg-notify.bot/`**. C#-namespace модуля — `TelegramBot`.

Бот объединяет три подсистемы:

1. **Трекер новых серий** — Trakt API (приоритет) с фоллбэком на TMDB.
2. **Трекер озвучек** — параллельный опрос балансеров **Mirage**, **Collaps** и **VideoHub** (поиск по названию или kinopoisk_id, мультиозвучка, чистый JSON). Берётся максимальный эпизод из всех источников — кто первым выложил серию в нужной озвучке, от того и уведомление.
3. **Клиентский плагин Lampa** (`tg-notify.js`) — вкладка «TG Подписки», перехват кнопки 🔔 в карточке, меню выбора озвучки.

---

## Включение

1. В шаблоне [`config/base.conf`](../../../config/base.conf) при необходимости проверьте, что **`TelegramBot`** не попал в **`BaseModule.SkipModules`** (если попал — уберите имя из списка, иначе модуль не загрузится).
2. [`manifest.json`](manifest.json): выставьте **`"enable": true`** (в репозитории по умолчанию `false`).
3. `init.conf`: добавьте секцию **`TelegramBot`**. Пример — [`init.merge.example.json`](init.merge.example.json).
4. Перезапустите сервис: `systemctl restart lampac` (или имя вашего systemd-юнита).

При `enable: true` и пустом `bot_token` long polling **не стартует** — в лог пишется предупреждение.

---

## Конфигурация (секция `init.conf`)

```json
"TelegramBot": {
  "enable": true,
  "bot_token": "ТОКЕН_ОТ_BOTFATHER",
  "tmdb_api_key": "ВАШ_КЛЮЧ_TMDB",
  "trakt_client_id": "",
  "lampac_host": "http://127.0.0.1:9118",
  "lampac_token": "",
  "kp_api_key": "",
  "check_interval_minutes": 60,
  "tmdb_lang": "ru-RU",
  "data_dir": "database/tgnotify"
}
```

| Поле | Назначение |
|------|-----------|
| `bot_token` | Токен бота от [@BotFather](https://t.me/BotFather). **Обязателен.** |
| `tmdb_api_key` | Ключ TMDB v3 — обложки, описания эпизодов, определение актуального сезона. |
| `trakt_client_id` | Client ID приложения Trakt. Если задан — основной источник новых серий; если пуст — используется только TMDB. |
| `lampac_host` | Базовый URL самого Lampac для запросов к балансерам (`/lite/mirage`, `/lite/collaps`). Обычно локальный: `http://127.0.0.1:9118`. |
| `lampac_token` | Токен **accsdb**. Обязателен, если у вас включена защита `accsdb` — без него балансеры вернут `{"accsdb":true,"msg":"Войдите в аккаунт"}`. |
| `kp_api_key` | Ключ [kinopoiskapiunofficial.tech](https://kinopoiskapiunofficial.tech/) — резолвинг `tmdb_id → kinopoisk_id` по названию для источника **VideoHub**. Опционально: если пусто, `kinopoisk_id` берётся только из встроенного моста Lampac `/externalids` (покрытие ~40%); с ключом — фоллбэк через поиск по названию поднимает покрытие почти до полного. |
| `check_interval_minutes` | Период фоновой проверки подписок. По умолчанию 60. Первый прогон — через 2 минуты после старта. |
| `tmdb_lang` | Язык метаданных TMDB. По умолчанию `ru-RU` (с фоллбэком на `en-US` при пустом описании). |
| `data_dir` | Каталог хранения `users.json` / `subscriptions.json`. По умолчанию `database/tgnotify`. |

---

## Зависимости

- **`Telegram.Bot` 22.4.4** — кладётся в [`references/`](references/) (для рантайм-компиляции Roslyn) и подтягивается через `PackageReference` при локальной сборке.
- **TMDB API** — ключ v3 (`tmdb_api_key`).
- **Trakt API** (опционально) — `trakt_client_id` для более точного и быстрого трекинга выхода серий.
- Балансеры **Mirage**, **Collaps** и **VideoHub** (`/lite/cdnvideohub`) должны быть доступны по `lampac_host`.
- **kinopoiskapiunofficial.tech** (опционально) — `kp_api_key`. Нужен только для VideoHub: резолвит `kinopoisk_id` по названию, когда встроенный мост Lampac его не знает.

---

## Структура модуля

```
Tg-notify.bot/
├── manifest.json              # dynamic:true, references+tree
├── Tg-notify.bot.csproj       # только для локальной сборки; на сервере не используется
├── init.merge.example.json    # пример секции init.conf
├── ModInit.cs                 # ядро: polling, трекинг серий, парсеры озвучек, HTTP API
├── Models/
│   └── TelegramBotConf.cs     # конфиг (наследует ModuleBaseConf)
├── Controllers/
│   └── TgNotifyController.cs   # HTTP API + раздача tg-notify.js
├── references/
│   └── Telegram.Bot.dll        # 22.4.4
└── tg-notify.js                # клиентский плагин Lampa
```

> На сервере модуль компилируется рантайм-движком **Roslyn** по `manifest.json` (`dynamic: true`) — `.csproj` нужен только для локальной разработки. Поля `references` и `tree` обязательны.

---

## Как работает

### Привязка аккаунта

Из вкладки «TG Подписки» в Lampa пользователь получает deep-link `/start link_{uid}`. Бот сохраняет связку `chat_id ↔ lampac_uid` (`users.json`), после чего подписки этого UID становятся «его».

### Подписка

В карточке сериала плагин перехватывает кнопку 🔔 и показывает меню озвучек (`/api/tg/voices` → Mirage + Collaps + VideoHub). Выбор отправляется в `/api/tg/subscribe` и сохраняется в `subscriptions.json`.

### Фоновый цикл `CheckAll`

Раз в `check_interval_minutes` (и вручную командой `/check`) по каждой подписке:

1. **Новые серии** — `CheckNewEpisodes` (Trakt → TMDB). Если у подписки не выбрана конкретная озвучка — уведомление шлётся о самом факте выхода серии.
2. **Озвучки** (если озвучка выбрана) — **параллельно** (`Task.WhenAll`) опрашиваются Mirage, Collaps и VideoHub; берётся максимальный доступный эпизод; кто первый нашёл новую серию — от того уведомление. На каждую новую серию шлётся отдельное сообщение с инфой из TMDB.

> **VideoHub и kinopoisk_id.** VideoHub ищет контент только по `kinopoisk_id`, тогда как бот работает по `tmdb_id`. Резолвинг выполняется лениво и кешируется в самой подписке: сначала через встроенный мост Lampac `/externalids`, при неудаче — поиском по названию через `kp_api_key`. Имя озвучки в запросе URL-кодируется (кириллица иначе даёт HTTP 400). Если `kinopoisk_id` не нашёлся, VideoHub для этой подписки молча пропускается — Mirage и Collaps продолжают работать.

Формат уведомления:

```
🎬 Название
🎙 Озвучка
📺 S05E08 — Название эпизода

Описание (до 300 символов)
```

С обложкой кадра (`SendPhoto`), если у эпизода есть `still_path`.

---

## Команды бота

| Команда / кнопка | Действие |
|------------------|----------|
| `/start` | Приветствие; `/start link_{uid}` — привязка аккаунта |
| `/list` · «📋 Подписки» | Список активных подписок |
| `/check` · «🔍 Проверить» | Принудительная проверка (в фоне) |
| `/help` · «📖 Помощь» | Справка |
| `/unlink` · «🔓 Отвязать» | Отвязать аккаунт |

---

## HTTP API (`TgNotifyController : BaseController`)

| Метод | Маршрут | Назначение |
|-------|---------|-----------|
| `GET` | `/tg-notify.js` | Раздаёт клиентский плагин с автозаменой `{localhost}` → реальный хост. `[AllowAnonymous]` |
| `POST` | `/api/tg/subscribe` | Подписка на сериал/озвучку |
| `POST` | `/api/tg/unsubscribe` | Отписка |
| `GET` | `/api/tg/status?tmdb_id=` | Статус подписки текущего пользователя |
| `GET` | `/api/tg/voices?title=&year=&season=&tmdb_id=` | Список доступных озвучек |
| `GET` | `/api/tg/link` | Ссылка привязки бота |
| `GET` | `/api/tg/subscriptions` | Все подписки пользователя с метаданными |

Все маршруты, кроме `/tg-notify.js` и `/api/tg/voices`, требуют `requestInfo.user_uid`.

---

## Клиентский плагин (`tg-notify.js`)

Подключается через `customPlugins` в `init.conf` либо раздаётся самим модулем по `/tg-notify.js`:

```json
"LampaWeb": {
  "customPlugins": [
    { "url": "{localhost}/tg-notify.js", "status": 1 }
  ]
}
```

Возможности: вкладка «TG Подписки» (сетка карточек с бейджем `S5 E4`), перехват кнопки 🔔 в полной карточке, меню выбора озвучки (`Lampa.Select.show`, метки `Mirage [—]` / `Collaps [C]` / `VideoHub [V]`), загрузка метаданных через `Lampa.TMDB` / `Lampa.Network.silent` с кешем. Хост подставляется через `{localhost}` — хардкода адресов нет.
