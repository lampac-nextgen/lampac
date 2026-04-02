# TelegramAuth

HTTP API и файловое хранилище для привязки **UID устройства Lampa** к учётке Telegram. Работает с [TelegramAuthBot](../TelegramAuthBot/README.md) и с клиентским плагином (см. [обзор Community](../README.md) — `deny.js` / `telegram_auth_gate.js`).

---

## Включение

- `manifest.json`: `"enable": true`.
- `init.conf`: секция **`TelegramAuth`**. Пример: [`init.merge.example.json`](init.merge.example.json).
---

## Назначение

- Пользователи Telegram: `telegramId`, роль, срок доступа, устройства (UID).
- Статус по UID для UI: привязан / ожидание / истёк срок.
- Лимит **активных** устройств для роли `user` (`max_active_devices_per_user`; у `admin` — без лимита).
- Опционально: импорт legacy, очистка устройств.

---

## Связь с accsdb

Если **`TelegramAuth.enable`** = **`true`**:

- В Core выставляется **`accsdb.enable`**.
- Активные привязанные UID синхронизируются в **корневой** **`users.json`** (рядом с `init.conf`) в формате **`AccsUser`**. Хост обычно подхватывает файл в течение ~1 с.

Если **`enable`** = **`false`**: только хранилище и HTTP API, без accsdb и без подмешивания WAF из этой секции.

**Когда UID попадает в корневой `users.json`:** пользователь **не** в `RegistrationPending`, **не** `Disabled`, устройство **активно**. Иначе для затронутых UID выполняется **`RemoveUid`**. При нехватке слотов под новое устройство самое старое активное переводится в неактивное и его UID снимается с accsdb.

Поле **`accs`** в записи пользователя в **`data_dir`/users.json** задаёт группу, бан, `params` (в т.ч. `telegram_id`) и т.д.; срок в accsdb берётся из **`ExpiresAt`** или **`accsdb.shared_daytime`** / 365 дней. Подробная логика: [`Services/AccsdbUidSync.cs`](Services/AccsdbUidSync.cs).

**Регистрация:** при выключенном `auto_provision_users` запись с таким `telegramId` уже должна быть в **`data_dir`/users.json`. При включённом — может создаваться автоматически (см. таблицу конфига). Ожидание модерации: **`RegistrationPending`** + при создании часто **`Disabled`**. Подтверждение: `POST /tg/auth/admin/user/pending` или бот.

**`owner_telegram_ids`:** при старте модуля создаются/обновляются как **admin** в **`data_dir`/users.json**.

Ограничение **чатов** для админ-команд бота задаётся в **`TelegramAuthBot.admin_chat_ids`**, не здесь.

---

## Конфигурация

| Поле | Описание |
|------|----------|
| `enable` | `true` — accsdb + синхронизация UID + применение **`limit_map`** к WAF. `false` (по умолчанию) — только API/хранилище. |
| `data_dir` | Каталог данных (по умолчанию `database/tgauth`). |
| `legacy_import_path` | База для `POST /tg/auth/import`. |
| `enable_import` / `enable_cleanup` | Разрешить импорт / `POST /tg/auth/devices/cleanup`. |
| `max_active_devices_per_user` | Лимит активных устройств для `user`; `0` → **5**. Для `admin` не применяется. |
| `owner_telegram_ids` | Владельцы → admin при старте. |
| `auto_provision_users` | Создавать пользователя при bind неизвестного `telegramId`. |
| `auto_provision_role` | Роль новой записи (`admin` через auto-provision недоступен → `user`). |
| `auto_provision_lang` | Язык по умолчанию. |
| `auto_provision_expires_days` | Срок в днях; `0` — без срока. |
| `auto_provision_activate_immediately` | `false` — pending до решения админа; `true` — сразу активен. |
| `limit_map` | Правила WAF (тот же формат, что **`WAF.limit_map`** в Core). |
| `accsdb_sync_group_admin` / `accsdb_sync_group_user` | Значение **`group`** в accsdb, если не задано в **`accs.group`** (по умолчанию 100 / 0). |

### WAF и `limit_map`

При **`TelegramAuth.enable`** записи из **`TelegramAuth.limit_map`** переносятся в **`CoreInit.conf.WAF.limit_map`**. Перед добавлением из глобального списка удаляются правила с тем же **`pattern`** (точное строковое совпадение, `Ordinal`). Core выбирает **первое** подходящее по пути правило; иначе — глобальный **`WAF.limit_req`**.

Поведение **WAF** в Core без изменений: при **`IsAnonymousRequest`** (в т.ч. из‑за **`[AllowAnonymous]`** на действии) весь WAF для запроса обходится — **`limit_map`** на такие маршруты не действует. Для ограничения нагрузки опирайтесь на глобальный **`WAF`**, **`limit_map`** на путях без полного anonymous-bypass или на сетевой периметр.

Шаблон в примере: [`init.merge.example.json`](init.merge.example.json).

---

## Хранилище (`data_dir`)

| Файл | Содержимое |
|------|------------|
| `users.json` | Пользователи Telegram, устройства, опционально **`accs`**. |
| `admins.json` | Служебно (legacy-импорт). |
| `user_langs.json` | `telegramId → язык`. |

---

## HTTP API (префикс `/tg/auth`)

Как в остальных модулях: **Accsdb** + атрибуты authorization.

**Публичные** (минимально необходимое): только **`[AllowAnonymous]`** — нет **`[Authorize]`**, первая зона Accsdb по атрибуту не включается; для проверки **пользователя** accsdb по `uid`/`token` запрос считается анонимным.

**Чувствительные** (`/tg/auth/admin/...`, **`POST /tg/auth/bind/complete`**, **`import`**, **`devices/cleanup`**): **`[Authorize]`** + **`[AllowAnonymous]`** и **без** **`[AuthorizeAnonymous]`**. Тогда **Accsdb** в своей зоне Authorize требует cookie **`accspasswd`**, совпадающий с паролем из файла **`passwd`** хоста (**`CoreInit.rootPasswd`**), — та же модель, что для других защищённых путей. **`[AllowAnonymous]`** лишь отключает требование **залогиненного пользователя** accsdb по query (бот и служебные вызовы без `uid` в URL).

### Чтение / статус

- `GET /tg/auth/status?uid=` — авторизован ли UID, срок, роль, число устройств.
- `GET /tg/auth/me?uid=` — полная запись владельца UID (404 если нет).

### Пользователь по Telegram

- `GET /tg/auth/user/by-telegram?telegramId=` — сводка для бота.
- `GET /tg/auth/devices?telegramId=` — список устройств.

### Привязка и устройства

- `POST /tg/auth/bind/start` — `{ "uid" }`.
- `POST /tg/auth/bind/complete` — `{ "uid", "telegramId", "username?", "deviceName?" }`.
- `POST /tg/auth/device/name` — `{ "uid", "name?" }` (плагин Lampa после успешного статуса).
- `POST /tg/auth/device/unbind` — `{ "telegramId", "uid" }` (без cookie мутаций).
- `POST /tg/auth/device/reactivate` — `{ "telegramId", "uid" }` (403 если pending/disabled).

### Административные

- `GET /tg/auth/admin/users`
- `GET /tg/auth/admin/user?telegramId=`
- `POST /tg/auth/admin/user/patch` — патч профиля и **`accs`**, см. код/бот `/setuser`.
- `POST /tg/auth/admin/user/disabled` — `{ "telegramId", "disabled" }` (admin нельзя отключить).
- `POST /tg/auth/admin/user/pending` — `{ "telegramId", "approve" }` (отклонение удаляет пользователя из `data_dir` и снимает UID с accsdb).
- `POST /tg/auth/import`
- `POST /tg/auth/devices/cleanup`

---

## Безопасность

- Пароль для **`accspasswd`** — тот же, что в файле **`passwd`** рядом с приложением (как для остального Lampac). Бот задаёт его в **`TelegramAuthBot.lampac_accspasswd`** при HTTP к Lampac.
- Расширяйте **`limit_map`** при необходимости.
- GET с `uid` / `telegramId` раскрывают факт привязки — при жёсткой модели угроз ограничивайте сеть/прокси.

---

## Связь с ботом и клиентом

- Бот для мутаций шлёт cookie **`accspasswd`** = **`lampac_accspasswd`** (содержимое **`passwd`** на сервере Lampac).
- Клиент Lampa: [Community README — плагины](../README.md).

---

## Legacy-импорт

Каталог **`legacy_import_path`**: минимум **`tokens.json`**; опционально `admin_ids.json`, `user_langs.json`. Форматы — `TelegramAuthStore.ImportFromLegacy`. Запись с `approved_by` = **`registration-pending`** → pending/disabled как при auto-provision без немедленной активации.
