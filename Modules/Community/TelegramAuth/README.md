# TelegramAuth

Модуль **HTTP API и файлового хранилища** для авторизации клиентов Lampac через привязку устройства (UID) к учётной записи Telegram. Работает совместно с модулем [TelegramAuthBot](../TelegramAuthBot/README.md) и/или с кастомными клиентами (например, плагин в LampaWeb).

### Интеграция с accsdb и `/testaccsdb`

При **`sync_lampa_uid_to_accsdb`: `true`** модуль при загрузке конфига выставляет **`accsdb.enable`** в Core. Для каждого **активного** привязанного Lampa UID в **корневой** **`users.json`** (рядом с `init.conf`) пишется запись в формате **`AccsUser`**: `id` = UID, `expires`, `group`, `ban`, `ban_msg`, `comment`, `IsPasswd`, `ids`, `params` — см. [`Services/AccsdbUidSync.cs`](Services/AccsdbUidSync.cs). Хост подмешивает файл в память (типично до ~1 с после записи).

В **`data_dir`/users.json** у записи Telegram-пользователя опционально объект **`accs`**: те же смыслы, что в accsdb, кроме `id` и `expires` (срок берётся из **`ExpiresAt`** пользователя или из **`accsdb.shared_daytime`** / 365 дней). **`group`**: из **`accs.group`**, иначе из роли и полей **`accsdb_sync_group_admin`** / **`accsdb_sync_group_user`**. **`accs.ban`** — бан в accsdb **без** отключения учётки в Telegram; текст — **`accs.ban_msg`**. В **`params`** при синхронизации добавляются **`telegram_id`** и при наличии **`telegram_username`**.

**Пока учётка в ожидании подтверждения** (`RegistrationPending`) **или отключена** (`Disabled`): в корневой **`users.json` UID не добавляется** (upsert не выполняется); при привязке UID в этом состоянии лишняя строка по этому UID **удаляется** (`RemoveUid`). После **одобрения** или **включения** — снова upsert для активных устройств. **`shared_passwd`**, **`authMesage`**, **`denyMesage`** задаются в секции **`accsdb`** init. Выключение **`sync_lampa_uid_to_accsdb`** не отключает accsdb целиком.

**Лимит активных устройств:** при добавлении нового UID, **реактивации** неактивного или при **`CleanupInactiveDevices`**, если слотов не хватает, самое старое активное устройство (по `LastSeenAt` / `LinkedAt`) переводится в неактивное и его UID **снимается с accsdb** — иначе старый клиент остался бы в `users.json` и проходил бы проверку.

## Назначение

- Хранить пользователей Telegram (`telegramId`, роль, срок доступа, язык) и список привязанных **устройств** (UID).
- Отдавать **статус авторизации** по UID для UI («привязано / ожидание / истёк срок»).
- Ограничивать число активных устройств на пользователя (для роли `user`; у `admin` лимит снят).
- Опционально: импорт из **legacy**-каталога и фоновая очистка старых записей устройств.

**Краткий поток accsdb** (при включённом `sync_lampa_uid_to_accsdb`): upsert в корневой `users.json` только для пользователя **без** `RegistrationPending`, **не** `Disabled`, с **активным** устройством; иначе для затронутых UID вызывается удаление строки. Ресинк всех активных UID одного пользователя — после патча админом, одобрения pending, включения учётки; полное снятие UID — при отключении, отклонении pending, отвязке, вытеснении по лимиту устройств.

Если `auto_provision_users` выключен, при привязке UID запись с таким `telegramId` **уже должна быть** в **`data_dir`/users.json**. Если включён — неизвестный id может быть создан автоматически (см. блок «Регистрация» в конфиге). Владельцы из `owner_telegram_ids` добавляются/обновляются как **admin** при **старте модуля** (не через бота).

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
| `owner_telegram_ids` | Числовые user id владельцев; при старте модуля — запись admin в **`data_dir`/users.json** (хранилище Telegram). |
| `auto_provision_users` | Разрешить создание пользователя при bind для неизвестного `telegramId`. |
| `auto_provision_role` | Роль новой записи (кроме `admin` — принудительно `user`). |
| `auto_provision_lang` | Язык по умолчанию. |
| `auto_provision_expires_days` | Срок доступа в днях; `0` — без срока. |
| `auto_provision_activate_immediately` | `false`: новый пользователь в статусе **ожидания подтверждения** (`RegistrationPending: true`, `Disabled: true`, `ApprovedBy`: `registration-pending` — только метка в JSON) до решения администратора; `true`: сразу активен. |
| `limit_map` | Дополнительные правила WAF (модуль добавляет в начало списка правило для `^/tg/auth`, по умолчанию ~25 запросов/сек). |
| `sync_lampa_uid_to_accsdb` | Включить **`accsdb.enable`** и синхронизировать активные UID в **корневой** `users.json`. |
| `accsdb_sync_group_admin` | Значение **`group`** в accsdb для роли `admin`, если в **`accs.group`** не задано (по умолчанию `100`). |
| `accsdb_sync_group_user` | То же для обычного пользователя (по умолчанию `0`). |

## Хранилище

При старте создаётся каталог (если нужно) и пустые файлы:

| Файл | Содержимое |
| ---- | ---------- |
| `users.json` (в **`data_dir`**, не путать с корневым accsdb) | Массив пользователей Telegram: `TelegramId`, `Role`, `ExpiresAt`, `Devices`, опционально **`accs`** (`group`, `ban`, `ban_msg`, …) |
| `admins.json` | Служебный список (заполняется при импорте из legacy). |
| `user_langs.json` | Словарь `telegramId → язык`. |

Пути задаются относительно `data_dir`.

## HTTP API (префикс `/tg/auth`)

Все перечисленные маршруты помечены в коде как анонимные (`AuthorizeAnonymous`) — доступ не через стандартную cookie-сессию Lampac, а по смыслу операции (UID / Telegram ID в запросе). **Административные мутации** (import, cleanup, список пользователей, отключение учётки, решение по pending) требуют успешной проверки «мутаций»: cookie **`accspasswd`** (= root-пароль сервера) **или** заголовок **`X-TelegramAuth-Mutations-Secret`**, совпадающий с непустым **`mutations_api_secret`**. Если **`mutations_api_secret` пуст**, заголовок мутаций **не принимается** — остаётся только cookie `accspasswd` (удобно для вызовов из браузера под root; серверный бот без секрета админские методы через API не пройдёт). **`POST /tg/auth/bind/complete`** проверяет секрет **только при непустом** `mutations_api_secret`; при пустом секрете привязка возможна без заголовка и без этой cookie.

### Чтение / статус

- **`GET /tg/auth/status?uid=`** — авторизован ли UID, срок доступа, роль, число устройств.
- **`GET /tg/auth/me?uid=`** — полная запись пользователя, которому принадлежит активное устройство с этим UID (404, если не найдено).

### Пользователь по Telegram

- **`GET /tg/auth/user/by-telegram?telegramId=`** — краткая сводка: найден ли пользователь, активен ли доступ, флаги `disabled`, `registrationPending`, лимит устройств и т.д. (используется ботом).
- **`GET /tg/auth/devices?telegramId=`** — список устройств пользователя.

### Привязка устройства

- **`POST /tg/auth/bind/start`** — тело JSON `{ "uid" }`. Опциональный шаг для клиентов до привязки в боте; имя устройства задаётся после входа через **`POST /tg/auth/device/name`**.
- **`POST /tg/auth/bind/complete`** — тело JSON `{ "uid", "telegramId", "username?", "deviceName?" }`. Новый UID или **реактивация** существующего неактивного устройства учитывают лимит активных (вытеснение + `RemoveUid` на accsdb). Пока **`RegistrationPending`** или **`Disabled`**, в accsdb UID **не появляется**; при привязке в этом состоянии UID в корневом `users.json` **удаляется**, если был. Секрет мутаций — как выше, если **`mutations_api_secret`** не пустой.
- **`POST /tg/auth/device/name`** — тело `{ "uid", "name?" }`. **`Devices[].Name`** для активного UID. Плагин Lampa вызывает после успешного `status`; пустой `name` очищает подпись.

### Отвязка

- **`POST /tg/auth/device/unbind`** — тело `{ "telegramId", "uid" }`: помечает устройство неактивным **только** если этот UID принадлежит указанному Telegram-пользователю; иначе **404** (`user not found` / `device not found`). Секрет мутаций не требуется.
- **`POST /tg/auth/device/reactivate`** — тело `{ "telegramId", "uid" }`: снова активирует устройство; при переполнении лимита самое старое активное отключается и его UID снимается с accsdb (как при `bind/complete`). Если аккаунт в pending / отключён — `403`.

### Административные (секрет или root-cookie)

- **`GET /tg/auth/admin/users`** — `{ "ok", "users" }`: краткая сводка по каждому пользователю, в т.ч. урезанный **`accs`** (для списка в боте).
- **`GET /tg/auth/admin/user?telegramId=`** — полная карточка: профиль, **`accs`**, список устройств.
- **`POST /tg/auth/admin/user/patch`** — JSON-патч одного пользователя: обязателен **`telegramId`**; опционально **`expiresAt`**, **`lang`**, **`role`**, объект **`accs`** (слияние полей: `group`, `IsPasswd`, `ban`, `ban_msg`, `comment`, `ids`, `params`), массив **`accsRemove`** (имена полей: `group`, `ispasswd`, `ban`, `ban_msg`, …), **`paramsRemove`** (ключи из `accs.params`). После сохранения выполняется ресинк UID в корневой `users.json` (pending/disabled — только `RemoveUid` по устройствам).
- **`POST /tg/auth/admin/user/disabled`** — тело `{ "telegramId", "disabled" }` (`disabled: true` — отключить доступ, деактивировать все устройства; `false` — снова разрешить вход, сбросить `RegistrationPending`). Учётки с ролью `admin` **нельзя** отключить этим методом.
- **`POST /tg/auth/admin/user/pending`** — тело `{ "telegramId", "approve" }`. `approve: true` — подтвердить регистрацию (`RegistrationPending: false`, `Disabled: false`). `approve: false` — **отклонить**: удалить запись пользователя из `data_dir`/users.json; UID устройств снимаются с корневого accsdb при включённом sync. Запись `admin` отклонить нельзя.
- **`POST /tg/auth/import`** — импорт из `legacy_import_path`: ожидаются `tokens.json`, опционально `admin_ids.json`, `user_langs.json` (формат см. в `TelegramAuthStore.ImportFromLegacy`).
- **`POST /tg/auth/devices/cleanup`** — удаление давно неактивных записей устройств и ужатие превышения лимита активных.

## Безопасность

- Храните **`mutations_api_secret`** как секрет; не коммитьте в репозиторий. Рекомендуется задать ненулевой секрет в продакшене: тогда **`bind/complete`** нельзя вызвать снаружи без секрета или root-cookie, а [TelegramAuthBot](../TelegramAuthBot/README.md) передаёт заголовок автоматически и может рассылать уведомления админам о новых заявках на регистрацию.
- Ограничения WAF на `/tg/auth` снижают перебор; при необходимости расширьте `limit_map` в конфиге.
- Публичные GET с `uid` / `telegramId` раскрывают факт привязки и метаданные — закрывайте доступ на уровне сети или прокси, если это критично для вашей модели угроз.

## Связь с TelegramAuthBot

Бот вызывает этот API по **`TelegramAuthBot.lampac_base_url`**. Секрет **`mutations_api_secret`** должен совпадать у **TelegramAuth** и **TelegramAuthBot**, иначе `bind/complete` и админские HTTP-методы вернут **403**. Команды бота **`/user`**, **`/setuser`**, **`/users`**, **`/import`**, **`/cleanup`** зависят от секрета и роли `admin` так же, как описано в [README бота](../TelegramAuthBot/README.md).

## Legacy-импорт

Каталог `legacy_import_path` должен содержать как минимум `tokens.json` в ожидаемом формате (`LegacyTokenRecord` / устройства `LegacyDeviceRecord` в коде). После импорта пользователи появляются в `users.json`, языки и админы — в соответствующих файлах.

Для **новой модели модерации**: если в `tokens.json` у записи `approved_by` указано значение **`registration-pending`** (без учёта регистра), при импорте выставляются **`RegistrationPending: true`** и **`Disabled: true`**, как при auto-provision без немедленной активации. Для строки с **`telegram_id` из `admin_ids.json`** ожидание подтверждения не применяется. Пустой `approved_by` заменяется на **`legacy-import`**, иначе в запись переносится текст из файла.
