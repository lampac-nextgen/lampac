# TelegramAuthBot

Фоновый **Telegram-бот** (long polling): привязка UID устройства к учётке в [TelegramAuth](../TelegramAuth/README.md), статус, устройства, админ-импорт/очистка через HTTP.

**Клиент Lampa** (замена `deny.js`, оверлей входа): [Community README](../README.md).

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
| `mutations_api_secret` | Тот же секрет, что **`TelegramAuth.mutations_api_secret`**. Нужен для админ-команд и их HTTP-вызовов (`X-TelegramAuth-Mutations-Secret`). Если пусто — бот сообщит об этом. |
| `admin_chat_ids` | Если **не пустой** — админ-команды только из этих групп, **кроме** лички пользователей из `owner_telegram_ids`. Если **пустой** — из любого чата. |
| `owner_telegram_ids` | Числовые **user** id (как в `TelegramAuth.owner_telegram_ids`). Нужны, если задан `admin_chat_ids`, но владелец хочет вызывать `/users` из лички. |
| `notify_admins_on_pending_provision` | `true` по умолчанию. После успешной привязки UID, когда новый пользователь создан в режиме **ожидания подтверждения** (`auto_provision_activate_immediately: false`), бот рассылает администраторам (роль `admin` в базе) личные сообщения с кнопками «Принять» / «Отклонить`. Для запроса списка админов используется тот же API, что и `/users`; поэтому при **пустом** `mutations_api_secret` уведомления **не отправляются** (в лог пишется предупреждение). Установите `false`, чтобы отключить рассылку. |

## Как это работает

1. При старте регистрируется `HostedService`: проверка токена (`GetMe`), снятие webhook, цикл **`GetUpdates`** (long polling).
2. Для каждого апдейта создаётся сессия с HTTP-клиентом к `lampac_base_url` ([`LampacTelegramAuthHttpClient`](Services/LampacTelegramAuthHttpClient.cs)).
3. Привязка устройства: бот вызывает `GET .../tg/auth/user/by-telegram`. Если пользователь **найден**, но доступ **неактивен** (ожидание модерации, отключён админом или истёк срок), привязка **не выполняется** — пользователю отправляется пояснение. Если учётки **ещё нет** и включён `auto_provision_users`, привязка выполняется через `POST .../tg/auth/bind/complete` (создание пользователя и устройства на стороне API). Иначе — тот же `bind/complete` для уже существующей записи. При успешной регистрации с ожиданием подтверждения админам может уйти уведомление (см. **`notify_admins_on_pending_provision`**). Имя устройства в `users.json` клиент выставляет после входа (`POST .../tg/auth/device/name`) или пользователь — командой `/devicename` (см. [TelegramAuth](../TelegramAuth/README.md)).

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
| `/users` | Админы TelegramAuth | Список пользователей (постранично); в строке кратко видно **`accs`** (группа, `ban`, …). Ожидающие — **Принять** / **Отклонить**; остальные — **Выкл** / **Вкл**. |
| `/user` | Админы | `/user <telegramId>` — карточка, JSON **`accs`**, устройства (`GET /tg/auth/admin/user`). |
| `/setuser` | Админы | Правка профиля и **`accs`** через `POST /tg/auth/admin/user/patch` (срок, роль, `group`, `ban`, `banmsg`, `comment`, `ids`, `param`, сброс полей — см. подсказку без аргументов). |
| `/import` | Админы TelegramAuth | `POST /tg/auth/import` (секрет + роль `admin` + при необходимости чат из `admin_chat_ids`). |
| `/cleanup` | Админы TelegramAuth | `POST /tg/auth/devices/cleanup`. |

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
| `/import` или `/cleanup` не работают | Непустой `mutations_api_secret` (бот не подставляет root-cookie); значение совпадает с `TelegramAuth.mutations_api_secret`; роль `admin`; если задан `admin_chat_ids` — команда из этой группы или из лички при наличии твоего id в `owner_telegram_ids`. |
| Админы не получили уведомление о новой заявке | Включён `notify_admins_on_pending_provision`; задан ненулевой `mutations_api_secret`; в `users.json` есть хотя бы один пользователь с ролью `admin`. |

Подробное описание HTTP API см. в [README модуля TelegramAuth](../TelegramAuth/README.md).
