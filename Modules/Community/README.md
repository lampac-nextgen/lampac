# Community: Telegram-авторизация

Модули расположены в **`Modules/Community/`** ([`NextGen.slnx`](../../NextGen.slnx)). Краткая карта и клиентская часть (Lampa). Подробности по API и конфигу — в README соответствующего подмодуля.

## Включение в поставке по умолчанию

В [`config/base.conf`](../../config/base.conf) в **`BaseModule.SkipModules`** по умолчанию указаны **`TelegramAuth`** и **`TelegramAuthBot`** — хост их не загружает, пока вы не уберёте эти имена из списка. Дополнительно в каждом модуле в **`manifest.json`** должно быть **`"enable": true`**, иначе Roslyn-слой не подхватит проект.

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
5. Чтобы вход был через Telegram (а не модалку пароля/CUB), добавьте флаг **`telegramAuthGate`** в конфиг LampaWeb — см. раздел «Как заменить стандартный deny.js на Telegram» ниже.

## Клиент Lampa: `deny.js` и `telegram_auth_gate.js`

Оба сценария завязаны на **`/testaccsdb`**: если ответ говорит, что нужна авторизация (`accsdb`), клиент блокирует интерфейс и предлагает способ входа.

### Где это подключается

- В **`lampainit.js`** в функции `start()` есть плейсхолдер **`{deny}`**.
- [ApiController.cs](../../LampaWeb/Controllers/ApiController.cs) при **`accsdb.enable`** подставляет в `{deny}` **содержимое файла** `Modules/LampaWeb/plugins/deny.js` (с заменой `{cubMesage}` на `accsdb.authMesage`) — или `telegram_auth_gate.js` при включённом флаге `telegramAuthGate` (см. раздел «Как заменить стандартный deny.js на Telegram»). Если accsdb выключен, `{deny}` очищается.
- Скрипт **`telegram_auth_gate.js`** отдаётся отдельным маршрутом **`GET …/telegram_auth_gate.js`** с подстановкой `{localhost}`, `{country}`, `{token}` (как у других плагинов LampaWeb), а также `{botUsername}`/`{serviceName}` из конфига `telegramAuthGate` (через `TelegramGateJs()`). При вставке в `{deny}` сервер подставляет только `{botUsername}`/`{serviceName}` (остальные плейсхолдеры подставляются глобально на этапе сборки `lampainit.js`).

### Что делает `deny.js` (стандарт)

- Вызывает `{localhost}/testaccsdb` (с `account_email`, `uid`, опционально `token` по правилам Core).
- При необходимости авторизации выставляет **`window.start_deep_link`** на экран **denypages**, скрывает `#app`, показывает сообщение и через ~5 с открывает модалку: **пароль Lampac** и опционально **аккаунт CUB**.

### Что делает `telegram_auth_gate.js`

- Тот же запрос к **`/testaccsdb`**, но **не** трогает `start_deep_link` (нет принудительного экрана deny в ядре Lampa).
- Показывает полноэкранный оверлей: **UID устройства**, кнопка «Открыть Telegram», QR (на крупных экранах), опрос **`GET /tg/auth/status?uid=...`** каждые `checkIntervalMs`.
- После успеха пишет профиль в `Lampa.Storage` (`tg_auth_user`), отправляет **`POST /tg/auth/device/name`**, снимает блокировку и делает **перезагрузку на главную** (как после успешного пароля в `deny.js`). Блокировка окончательно снимается после повторного опроса `/testaccsdb` на перезагруженной странице: гейт не использует `start_deep_link` ядра Lampa, поэтому именно reload запускает повторную проверку, которая видит авторизацию. Убедитесь, что подключён ровно один источник гейта, иначе будет двойной опрос `/testaccsdb`.
- В начале файла нужно задать **`CONFIG.botUsername`** и **`CONFIG.serviceName`** (без `@` у имени бота в логике допускается — код обрежет). Актуально только при подключении скрипта вручную (customPlugins или подмена файла); при конфиге `telegramAuthGate` значения приходят из него.

### Как заменить стандартный deny.js на Telegram

Нужно, чтобы при включённом accsdb в `start()` выполнялся **только** сценарий с Telegram, а не модалка пароля/CUB.

**Рекомендуемый способ — флаг в конфиге (без правки файлов)**

1. В `init.conf` (или merge-файле) убедитесь, что **`accsdb.enable: true`**.
2. Добавьте блок **`telegramAuthGate`** с `enabled: true` и **непустым** `botUsername` (непустой = не состоящий из одних пробелов; код проверяет `IsNullOrWhiteSpace`).
3. Перезапустите LampaWeb.

Пример (`init.conf` или merge-файл):
```json
{
  "LampaWeb": {
    "telegramAuthGate": {
      "enabled": true,
      "botUsername": "lampac_community_bot",
      "serviceName": "lampa"
    }
  }
}
```

> Гейт активируется только при выполнении всех условий: `accsdb.enable` + `telegramAuthGate.enabled` + непустой `botUsername`. Иначе — **тихий откат на стандартный `deny.js` с warning** в логе. Override файла через `FileCache` кэшируется ~10 мин.

Семантика полей: `enabled` — вкл/выкл подстановку гейта; `botUsername` — имя бота **без `@`** (код обрезает ведущий `@`); `serviceName` — отображаемое имя сервиса в текстах оверлея гейта.

**Альтернатива — файловый оверрайд (подмена deny.js)**

Скопируйте содержимое `telegram_auth_gate.js` поверх `Modules/LampaWeb/plugins/deny.js` и задайте `CONFIG.botUsername` / `CONFIG.serviceName` в начале файла вручную. Работает, но правка теряется при обновлении — предпочтителен флаг выше.

⚠️ **Двойной опрос `/testaccsdb`**: `customPlugins` добавляется независимо от флага (см. `ApiController.cs:650-657`). Актуально только при ручном подключении скрипта, а не при использовании флага.

<details>
<summary>Прочие / продвинутые варианты</summary>

- **Гейт как отдельный плагин (`customPlugins`)** — очистите `deny.js` и добавьте URL `{localhost}/telegram_auth_gate.js` со `status: 1`; гейт загружается после `start()` вместе с плагинами. Нишевый сценарий; сохраняется предупреждение о двойном опросе выше.
- **Своя ветка / правка исходников** — для разработчиков: точка расширения выбора скрипта для `{deny}` в `ApiController.cs:671-693`.
- **Override через `FileCache`** — файл `telegram_auth_gate.js` можно переопределить через `plugins/override/telegram_auth_gate.js` (см. корневой `README.md`, раздел override); свежий override может занять до ~10 мин из-за кэша `FileCache.cs:54`.

</details>

### Плейсхолдеры в плагинах

| Плейсхолдер | Где подставляется |
|-------------|-------------------|
| `{localhost}` | Базовый URL Lampac для запросов |
| `{token}` | Из `accsdb.domainId_pattern` (если задан), иначе пусто |
| `{cubMesage}` | Только в **`deny.js`** при вставке в `lampainit` → `accsdb.authMesage` |
| `{botUsername}` | Только в **`telegram_auth_gate.js`** (через `TelegramGateJs()`) из `telegramAuthGate.botUsername`; экранируется `JavaScriptStringEncode` |
| `{serviceName}` | Только в **`telegram_auth_gate.js`** (через `TelegramGateJs()`) из `telegramAuthGate.serviceName`; экранируется `JavaScriptStringEncode` |

В **`telegram_auth_gate.js`** для подсказок с сервера используются поля ответа **`/testaccsdb`**: `msg`, `denymsg`, `newuid` (как в `deny.js`).

## Документация по модулям

- [TelegramAuth — конфиг, accsdb, API, безопасность](TelegramAuth/README.md)
- [TelegramAuthBot — токен, команды, ограничения чатов](TelegramAuthBot/README.md)
