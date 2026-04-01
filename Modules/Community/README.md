# Community: Telegram-авторизация

Краткая карта модулей и клиентской части (Lampa). Подробности по API и конфигу — в README соответствующего модуля.

## Состав

| Модуль | Роль |
|--------|------|
| [TelegramAuth](TelegramAuth/README.md) | Хранилище пользователей и устройств, HTTP API `/tg/auth/...`, синхронизация UID в accsdb при `TelegramAuth.enable` |
| [TelegramAuthBot](TelegramAuthBot/README.md) | Telegram-бот (long polling): привязка UID, устройства, админ-команды |

**Типовой поток:** клиент получает UID → пользователь открывает бота (`/start <uid>` или отправляет UID) → бот вызывает `POST /tg/auth/bind/complete` → клиент опрашивает `GET /tg/auth/status?uid=...` → после успеха Lampac видит UID в корневом `users.json` (если включены TelegramAuth + accsdb).

## Быстрый старт

1. В `init.conf` (или merge-файле) задать секции **`TelegramAuth`** и **`TelegramAuthBot`** по примерам:  
   [`TelegramAuth/init.merge.example.json`](TelegramAuth/init.merge.example.json),  
   [`TelegramAuthBot/init.merge.example.json`](TelegramAuthBot/init.merge.example.json).
2. **`mutations_api_secret`** должен **совпадать** в обоих модулях (и быть ненулевым в проде, если нужны бот-админка и защищённый `bind/complete`).
3. Включить модули в `manifest.json` (`"enable": true`).
4. Для входа через accsdb: **`TelegramAuth.enable`: `true`** поднимает **`accsdb.enable`** в Core и синхронизирует привязанные UID в корневой **`users.json`**. Без этого API Telegram живёт, но «дверь» accsdb не заведётся из TelegramAuth.

## Клиент Lampa: `deny.js` и `telegram_auth_gate.js`

Оба сценария завязаны на **`/testaccsdb`**: если ответ говорит, что нужна авторизация (`accsdb`), клиент блокирует интерфейс и предлагает способ входа.

### Где это подключается

- В **`lampainit.js`** в функции `start()` есть плейсхолдер **`{deny}`**.
- [ApiController.cs](../LampaWeb/Controllers/ApiController.cs) при **`accsdb.enable`** подставляет в `{deny}` **содержимое файла** `Modules/LampaWeb/plugins/deny.js` (с заменой `{cubMesage}` на `accsdb.authMesage`). Если accsdb выключен, `{deny}` очищается.
- Скрипт **`telegram_auth_gate.js`** отдаётся отдельным маршрутом **`GET …/telegram_auth_gate.js`** с подстановкой `{localhost}` и `{country}` (как у других плагинов LampaWeb).

### Что делает `deny.js` (стандарт)

- Вызывает `{localhost}/testaccsdb` (с `account_email`, `uid`, опционально `token` по правилам Core).
- При необходимости авторизации выставляет **`window.start_deep_link`** на экран **denypages**, скрывает `#app`, показывает сообщение и через ~5 с открывает модалку: **пароль Lampac** и опционально **аккаунт CUB**.

### Что делает `telegram_auth_gate.js`

- Тот же запрос к **`/testaccsdb`**, но **не** трогает `start_deep_link` (нет принудительного экрана deny в ядре Lampa).
- Показывает полноэкранный оверлей: **UID устройства**, кнопка «Открыть Telegram», QR (на крупных экранах), опрос **`GET /tg/auth/status?uid=...`** каждые `checkIntervalMs`.
- После успеха пишет профиль в `Lampa.Storage` (`tg_auth_user`), отправляет **`POST /tg/auth/device/name`**, снимает блокировку и делает **перезагрузку на главную** (как после успешного пароля в `deny.js`).
- В начале файла нужно задать **`CONFIG.botUsername`** и **`CONFIG.serviceName`** (без `@` у имени бота в логике допускается — код обрежет).

### Как заменить стандартный `deny.js` на Telegram

Нужно, чтобы при включённом accsdb в `start()` выполнялся **только** сценарий с Telegram, а не модалка пароля/CUB.

**Способ 1 (рекомендуется):** подменить содержимое **`Modules/LampaWeb/plugins/deny.js`** копией **`telegram_auth_gate.js`**, выставить `CONFIG`, при необходимости поправить тексты. Сервер по-прежнему вставит этот файл в `{deny}` — отдельно подключать URL `telegram_auth_gate.js` не нужно, подставятся `{localhost}` и `{token}` на этапе сборки ответа `lampainit.js`.

**Способ 2:** очистить **`deny.js`** (или оставить минимальный пустой блок), а **`telegram_auth_gate.js`** добавить в список плагинов LampaWeb (**`customPlugins`** в конфиге модуля LampaWeb) с URL вида `{localhost}/telegram_auth_gate.js` и `status: 1`. Тогда `{deny}` не выполняет проверку, а гейт загрузится вместе с остальными плагинами после `start()`. Убедитесь, что **не** подключены оба полноценных скрипта сразу — будет двойной опрос `/testaccsdb`.

**Способ 3:** держать кастомную ветку **`lampainit.js`** / правку **ApiController**, если нужна явная опция в конфиге «deny vs telegram» без замены файлов на диске (в текущем репозитории отдельного флага нет).

### Плейсхолдеры в плагинах

| Плейсхолдер | Где подставляется |
|-------------|-------------------|
| `{localhost}` | Базовый URL Lampac для запросов |
| `{token}` | Из `accsdb.domainId_pattern` (если задан), иначе пусто |
| `{cubMesage}` | Только в **`deny.js`** при вставке в `lampainit` → `accsdb.authMesage` |

В **`telegram_auth_gate.js`** для подсказок с сервера используются поля ответа **`/testaccsdb`**: `msg`, `denymsg`, `newuid` (как в `deny.js`).

## Документация по модулям

- [TelegramAuth — конфиг, accsdb, API, безопасность](TelegramAuth/README.md)
- [TelegramAuthBot — токен, команды, ограничения чатов](TelegramAuthBot/README.md)
