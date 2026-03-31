# TelegramAuth

Модуль **HTTP API и файлового хранилища** для авторизации клиентов Lampac через привязку устройства (UID) к учётной записи Telegram. Работает совместно с модулем [TelegramAuthBot](../TelegramAuthBot/README.md) и/или с кастомными клиентами (например, плагин в LampaWeb).

## Назначение

- Хранить пользователей Telegram (`telegramId`, роль, срок доступа, язык) и список привязанных **устройств** (UID).
- Отдавать **статус авторизации** по UID для UI («привязано / ожидание / истёк срок»).
- Ограничивать число активных устройств на пользователя (для роли `user`; у `admin` лимит снят).
- Опционально: импорт из **legacy**-каталога и фоновая очистка старых записей устройств.

Если `auto_provision_users` выключен, при привязке UID запись с таким `telegramId` **уже должна быть** в `users.json`. Если включён — неизвестный id может быть создан автоматически (см. блок «Регистрация» в конфиге). Владельцы из `owner_telegram_ids` добавляются/обновляются как **admin** при **старте модуля** (не через бота).

Статус **«ожидает подтверждения модератором»** задаётся только флагом **`RegistrationPending`** в `users.json` (вместе с `Disabled: true` при создании через auto-provision без немедленной активации). Подтверждение и отклонение — через `POST /tg/auth/admin/user/pending` или бота (см. [TelegramAuthBot](../TelegramAuthBot/README.md)).

## Включение

В `manifest.json` модуля: `"enable": true`. Секция конфигурации в `init.conf` (рядом с Core): ключ **`TelegramAuth`**. Пример merge: [`init.merge.example.json`](init.merge.example.json).

## Конфигурация (`TelegramAuth`)

Два смысловых блока:

1. **Владельцы** — `owner_telegram_ids`: при **старте модуля** для каждого числового user id создаётся запись (если нет): `admin`, пустые `Devices`, `ApprovedBy`: `owner-config`. Существующая запись с тем же id приводится к `admin` и снимается `Disabled` (устройства не трогаются).
2. **Регистрация по UID** — `auto_provision_users` и поля `auto_provision_*`: создавать ли новую запись при привязке неизвестного Telegram id, роль/язык/срок, сразу ли активен (`auto_provision_activate_immediately`). Роль `admin` через auto-provision **недоступна** (принудительно `user`).

Ограничение, **из каких чатов** бот принимает `/users` и др., задаётся только в **`TelegramAuthBot.admin_chat_ids`** / **`TelegramAuthBot.owner_telegram_ids`**.

| Поле | Описание |
| ---- | -------- |
| `data_dir` | Каталог данных относительно каталога приложения или абсолютный путь. По умолчанию: `database/tgauth`. |
| `legacy_import_path` | Базовый каталог legacy-данных для `POST /tg/auth/import` (см. ниже). |
| `enable_import` | Разрешить импорт (`true` / `false`). |
| `enable_cleanup` | Разрешить очистку (`POST /tg/auth/devices/cleanup`). |
| `max_active_devices_per_user` | Максимум **активных** устройств для роли `user`. `0` — использовать встроенное значение **5**. Для роли `admin` лимит не применяется (∞). |
| `mutations_api_secret` | Общий секрет через заголовок `X-TelegramAuth-Mutations-Secret` (должен совпадать с `TelegramAuthBot.mutations_api_secret`). Если строка **не пустая**, её же требуют завершение привязки (`POST /tg/auth/bind/complete`) и админские мутации (import, cleanup, список пользователей и т.д.). Если **пусто** — `bind/complete` доступен без этого заголовка (см. «Безопасность»). |
| `owner_telegram_ids` | Числовые user id владельцев; при старте модуля — запись admin в `users.json`. |
| `auto_provision_users` | Разрешить создание пользователя при bind для неизвестного `telegramId`. |
| `auto_provision_role` | Роль новой записи (кроме `admin` — принудительно `user`). |
| `auto_provision_lang` | Язык по умолчанию. |
| `auto_provision_expires_days` | Срок доступа в днях; `0` — без срока. |
| `auto_provision_activate_immediately` | `false`: новый пользователь в статусе **ожидания подтверждения** (`RegistrationPending: true`, `Disabled: true`, `ApprovedBy`: `registration-pending` — только метка в JSON) до решения администратора; `true`: сразу активен. |
| `limit_map` | Дополнительные правила WAF (модуль добавляет в начало списка правило для `^/tg/auth`, по умолчанию ~25 запросов/сек). |

## Хранилище

При старте создаётся каталог (если нужно) и пустые файлы:

| Файл | Содержимое |
| ---- | ---------- |
| `users.json` | Массив пользователей: `TelegramId`, `TgUsername`, `Role`, …, массив `Devices` (`Uid`, `Name` — подпись устройства, `Active`, `LastSeenAt`, …) |
| `admins.json` | Служебный список (заполняется при импорте из legacy). |
| `user_langs.json` | Словарь `telegramId → язык`. |

Пути задаются относительно `data_dir`.

## HTTP API (префикс `/tg/auth`)

Все перечисленные маршруты помечены в коде как анонимные (`AuthorizeAnonymous`) — доступ не через стандартную cookie-сессию Lampac, а по смыслу операции (UID / Telegram ID в запросе). **Административные мутации** (import, cleanup, список пользователей, отключение учётки, решение по pending) дополнительно требуют либо cookie `accspasswd` (значение = root-пароль сервера), либо заголовок `X-TelegramAuth-Mutations-Secret` с тем же значением, что в `mutations_api_secret` (при непустом секрете). **`POST /tg/auth/bind/complete`** использует те же правила **только если** `mutations_api_secret` задан; иначе вызов остаётся без заголовка мутаций.

### Чтение / статус

- **`GET /tg/auth/status?uid=`** — авторизован ли UID, срок доступа, роль, число устройств.
- **`GET /tg/auth/me?uid=`** — полная запись пользователя, которому принадлежит активное устройство с этим UID (404, если не найдено).

### Пользователь по Telegram

- **`GET /tg/auth/user/by-telegram?telegramId=`** — краткая сводка: найден ли пользователь, активен ли доступ, флаги `disabled`, `registrationPending`, лимит устройств и т.д. (используется ботом).
- **`GET /tg/auth/devices?telegramId=`** — список устройств пользователя.

### Привязка устройства

- **`POST /tg/auth/bind/start`** — тело JSON `{ "uid" }`. Опциональный шаг для клиентов до привязки в боте; имя устройства задаётся после входа через **`POST /tg/auth/device/name`**.
- **`POST /tg/auth/bind/complete`** — тело JSON `{ "uid", "telegramId", "username?", "deviceName?" }`. `username` — Telegram @; опционально **`deviceName`** → `Devices[].Name`. Пользователь с таким `telegramId` **должен существовать** (или создаётся при `auto_provision_users`), отключённый аккаунт получает 403. Если в конфиге задан **`mutations_api_secret`**, нужны cookie `accspasswd` или заголовок **`X-TelegramAuth-Mutations-Secret`** (как у админских методов); иначе ответ **403**. Типичный вызывающий — [TelegramAuthBot](../TelegramAuthBot/README.md) с тем же секретом в своей конфигурации.
- **`POST /tg/auth/device/name`** — тело `{ "uid", "name?" }`. **`Devices[].Name`** для активного UID. Плагин Lampa вызывает после успешного `status`; пустой `name` очищает подпись.

### Отвязка

- **`POST /tg/auth/device/unbind`** — тело `{ "telegramId", "uid" }`: помечает устройство неактивным **только** если этот UID принадлежит указанному Telegram-пользователю; иначе **404** (`user not found` / `device not found`). Секрет мутаций не требуется.
- **`POST /tg/auth/device/reactivate`** — тело `{ "telegramId", "uid" }`: снова активирует устройство, если UID принадлежит этому пользователю; учитывается лимит активных устройств (при переполнении самое старое активное отключается). Если аккаунт отключён администратором — `403`.

### Административные (секрет или root-cookie)

- **`GET /tg/auth/admin/users`** — JSON `{ "ok", "users" }`: сводка по всем записям в `users.json` (без полного дампа устройств).
- **`POST /tg/auth/admin/user/disabled`** — тело `{ "telegramId", "disabled" }` (`disabled: true` — отключить доступ, деактивировать все устройства; `false` — снова разрешить вход, сбросить `RegistrationPending`). Учётки с ролью `admin` **нельзя** отключить этим методом.
- **`POST /tg/auth/admin/user/pending`** — тело `{ "telegramId", "approve" }` (секрет мутаций, как у остальных admin-методов). `approve: true` — подтвердить регистрацию: снять ожидание, включить доступ (`RegistrationPending: false`, `Disabled: false`). `approve: false` — **отклонить**: удалить запись пользователя из `users.json` (устройства вместе с ней). Работает только при `RegistrationPending: true`. Запись `admin` отклонить нельзя.
- **`POST /tg/auth/import`** — импорт из `legacy_import_path`: ожидаются `tokens.json`, опционально `admin_ids.json`, `user_langs.json` (формат см. в `TelegramAuthStore.ImportFromLegacy`).
- **`POST /tg/auth/devices/cleanup`** — удаление давно неактивных записей устройств и ужатие превышения лимита активных.

## Безопасность

- Храните **`mutations_api_secret`** как секрет; не коммитьте в репозиторий. Рекомендуется задать ненулевой секрет в продакшене: тогда **`bind/complete`** нельзя вызвать снаружи без секрета или root-cookie, а бот передаёт заголовок автоматически.
- Ограничения WAF на `/tg/auth` снижают перебор; при необходимости расширьте `limit_map` в конфиге.
- Публичные GET с `uid` / `telegramId` раскрывают факт привязки и метаданные — закрывайте доступ на уровне сети или прокси, если это критично для вашей модели угроз.

## Связь с TelegramAuthBot

Бот обращается к этому API по базовому URL (`TelegramAuthBot.lampac_base_url`). Секрет мутаций должен быть **одинаковым** в обеих секциях `init.conf`, если задан **`mutations_api_secret`** — иначе привязка из бота (`bind/complete`) и админские команды (`/users`, `/import`, `/cleanup` и т.д.) получат **403**.

## Legacy-импорт

Каталог `legacy_import_path` должен содержать как минимум `tokens.json` в ожидаемом формате (`LegacyTokenRecord` / устройства `LegacyDeviceRecord` в коде). После импорта пользователи появляются в `users.json`, языки и админы — в соответствующих файлах.

Для **новой модели модерации**: если в `tokens.json` у записи `approved_by` указано значение **`registration-pending`** (без учёта регистра), при импорте выставляются **`RegistrationPending: true`** и **`Disabled: true`**, как при auto-provision без немедленной активации. Для строки с **`telegram_id` из `admin_ids.json`** ожидание подтверждения не применяется. Пустой `approved_by` заменяется на **`legacy-import`**, иначе в запись переносится текст из файла.
