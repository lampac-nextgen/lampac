# TelegramAuthBot

Фоновый **Telegram-бот** (long polling), который помогает пользователям **привязать UID устройства** к уже существующей учётной записи в модуле [TelegramAuth](../TelegramAuth/README.md), смотреть статус и список устройств, отвязывать устройства и (для администраторов) запускать импорт и очистку через HTTP API Lampac.

## Зависимости

1. **Модуль TelegramAuth** должен быть загружен и доступен по HTTP с того хоста/порта, который вы укажете в `lampac_base_url`.
2. У пользователя в базе TelegramAuth должен быть **создан профиль** (Telegram ID, активный доступ и т.д.) — бот **не регистрирует** новых пользователей с нуля, он только привязывает устройства к существующим записям и показывает данные.

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
| `mutations_api_secret` | Тот же секрет, что **`TelegramAuth.mutations_api_secret`**. Нужен для команд `/import` и `/cleanup` (заголовок `X-TelegramAuth-Mutations-Secret`). Если пусто — админ-команды сообщат об этом. |
| `admin_chat_ids` | Список числовых ID чатов (например, супергруппы), **из которых разрешены** админ-команды. Если массив **пустой или не задан** — админ-команды разрешены из **любого** чата (при прочих проверках). |

## Как это работает

1. При старте регистрируется `HostedService`: проверка токена (`GetMe`), снятие webhook, цикл **`GetUpdates`** (long polling).
2. Для каждого апдейта создаётся сессия с HTTP-клиентом к `lampac_base_url` ([`TelegramAuthApiClient`](Services/TelegramAuthApiClient.cs)).
3. Привязка устройства: бот вызывает `GET .../tg/auth/user/by-telegram` — если пользователь не найден или доступ неактивен, привязка не выполняется; иначе `POST .../tg/auth/bind/complete` с `uid`, `telegramId`, `username`.

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
| `/devices` | Все | Список устройств; для активных — inline-кнопки **Отвязать**. |
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
| `/import` или `/cleanup` не работают | Одинаковый `mutations_api_secret` в TelegramAuth и TelegramAuthBot; роль `admin`; чат в `admin_chat_ids`, если список задан. |

Подробное описание HTTP API см. в [README модуля TelegramAuth](../TelegramAuth/README.md).
