# TelegramAuthBot

Фоновый **Telegram-бот** (long polling), который помогает пользователям **привязать UID устройства** к уже существующей учётной записи в модуле [TelegramAuth](../TelegramAuth/README.md), смотреть статус и список устройств, отвязывать устройства и (для администраторов) запускать импорт и очистку через HTTP API Lampac.

## Зависимости

1. **Модуль TelegramAuth** должен быть загружен и доступен по HTTP с того хоста/порта, который вы укажете в `lampac_base_url`.
2. Профиль в TelegramAuth: либо уже есть запись, либо включён `auto_provision_users`, либо владелец попадает в базу при **старте Lampac** по `TelegramAuth.owner_telegram_ids` (см. [TelegramAuth](../TelegramAuth/README.md)).

## Включение

В `manifest.json`: `"enable": true`. Конфигурация в `init.conf`: секция **`TelegramAuthBot`**. Пример: [`init.merge.example.json`](init.merge.example.json).

При `enable: true` и пустом `bot_token` в лог пишется предупреждение; long polling **не стартует**.

## Конфигурация (`TelegramAuthBot`)

| Поле | Описание |
| ---- | -------- |
| `enable` | Включить бота. |
| `bot_token` | Токен от [@BotFather](https://t.me/BotFather). |
| `lampac_base_url` | Базовый URL Lampac **без** завершающего слэша, например `http://127.0.0.1:9118`. К нему добавляются пути вида `tg/auth/...`. |
| `request_timeout_sec` | Таймаут HTTP-клиента к Lampac (минимум 1 сек). |
| `service_display_name` | Отображаемое имя сервиса в текстах бота (HTML-экранирование учитывается в коде). |
| `mutations_api_secret` | Тот же секрет, что **`TelegramAuth.mutations_api_secret`**. Нужен для `/users`, `/import`, `/cleanup` и связанных вызовов API (заголовок `X-TelegramAuth-Mutations-Secret`). Если пусто — админ-команды сообщат об этом. |
| `admin_chat_ids` | Если **не пустой** — админ-команды (`/users`, …) только из этих групп, **кроме** лички пользователей из `owner_telegram_ids`. Если **пустой** — админ-команды из любого чата (удобно для личного бота). |
| `owner_telegram_ids` | Числовые **user** id (как в `TelegramAuth.owner_telegram_ids`). Нужны, если задан `admin_chat_ids`, но владелец хочет вызывать `/users` из лички. |

## Как это работает

1. При старте регистрируется `HostedService`: проверка токена (`GetMe`), снятие webhook, цикл **`GetUpdates`** (long polling).
2. Для каждого апдейта создаётся сессия с HTTP-клиентом к `lampac_base_url` ([`TelegramAuthApiClient`](Services/TelegramAuthApiClient.cs)).
3. Привязка устройства: бот вызывает `GET .../tg/auth/user/by-telegram` — если пользователь не найден или доступ неактивен, привязка не выполняется; иначе `POST .../tg/auth/bind/complete` с `uid`, `telegramId`, `username`. Имя устройства в `users.json` клиент выставляет после входа (`POST .../tg/auth/device/name`) или пользователь — командой `/devicename` (см. [TelegramAuth](../TelegramAuth/README.md)).

**Важно:** процесс должен видеть Lampac по сети. В Docker часто нужен URL вида `http://host.docker.internal:9118` или имя сервиса compose, а не только `127.0.0.1`, если бот крутится в другом контейнере.

## Сценарий для пользователя

1. Открыть клиент Lampac, получить **UID** на экране авторизации.
2. Написать боту в **личку** (в группах без `From` бот может не определить Telegram user id — см. сообщение об ошибке в коде).
3. Отправить UID текстом **или** открыть deep link: `https://t.me/<bot>?start=<uid>` (UID: 6–20 символов, `[a-zA-Z0-9_-]`).
4. В клиенте нажать «Проверить снова» (или аналог), который дергает `GET /tg/auth/status`.

Кнопки меню: **Мой статус**, **Мои устройства**, **Помощь**.

## Команды

| Команда | Кто | Описание |
| ------- | --- | -------- |
| `/start` | Все | Приветствие и инструкция; вариант `/start <uid>` — быстрая привязка. |
| `/help` | Все | Подсказка по входу и кнопкам. |
| `/me` | Все | Профиль из API (роль, срок, число устройств, лимит). |
| `/devices` | Все | Список устройств; для активных — **Отвязать**, для отключённых — **✅ Включить** (снова активирует UID на сервере). |
| `/devicename` | Все | `/devicename <uid> <имя>` — имя в `Devices[].Name` через `POST /tg/auth/device/name` (только **свой** активный UID). Вместо имени `-` сбрасывает подпись. Секрет мутаций не нужен. |
| `/users` | Админы TelegramAuth | Список пользователей из `users.json` (постранично). Для **ожидающих подтверждения** (`registrationPending`) — **Принять** / **Отклонить**; для остальных — **Выкл** / **Вкл**. Нельзя отключить себя и учётки с ролью `admin`. |
| `/import` | Админы TelegramAuth | Вызов `POST /tg/auth/import` (нужен секрет + роль `admin` + при необходимости чат из `admin_chat_ids`). |
| `/cleanup` | Админы TelegramAuth | Вызов `POST /tg/auth/devices/cleanup`. |

Админ определяется по полю **роли** в ответе API пользователя (`role == admin`), не по списку `admin_chat_ids` (этот список только ограничивает **чаты**, не роли).

## Ссылки на код

- Регистрация сервиса: [`ModInit.cs`](ModInit.cs) (`IModuleConfigure` → `AddHostedService<TelegramAuthBotHostedService>`).
- Long polling и жизненный цикл: [`TelegramAuthBotHostedService.cs`](Services/TelegramAuthBotHostedService.cs).
- Диалоги и команды: [`TelegramAuthBotSession.cs`](Services/TelegramAuthBotSession.cs).

## Устранение неполадок

| Симптом | Что проверить |
| ------- | ------------- |
| Бот молчит при старте | `enable`, `bot_token`, сеть до `api.telegram.org`, логи Serilog / консоль. |
| «Тебя нет в базе» | Запись пользователя с вашим Telegram ID в `users.json` (TelegramAuth), импорт или ручное добавление. |
| Привязка не срабатывает | Lampac доступен с хоста бота по `lampac_base_url`; пользователь `active`; лимит устройств не превышен. |
| `/import` или `/cleanup` не работают | Одинаковый `mutations_api_secret`; роль `admin`; если задан `admin_chat_ids` — команда из этой группы или из лички при наличии твоего id в `owner_telegram_ids`. |

Подробное описание HTTP API см. в [README модуля TelegramAuth](../TelegramAuth/README.md).
