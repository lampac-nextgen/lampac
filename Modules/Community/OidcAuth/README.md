# OidcAuth — вход в Lampa через OpenID Connect (Authentik и другие IdP)

Модуль добавляет авторизацию через OIDC-провайдер (Authentik, Keycloak, Authelia и т.д.),
сохраняя родную для lampac модель доступа: **uid — единственный токен** (случайная строка,
никогда не email). При включении модуля экран входа подключается автоматически.

## Как это работает

```
deny.js (кнопка «Войти через <name>»)
  → GET /oidc/login?uid=<device_uid>
  → 302 /authorize (authorization code + PKCE S256, state, nonce)
  → логин в Authentik
  → 302 /oidc/callback?code&state
  → POST /token, GET /userinfo
  → сопоставление sub → uid (см. ниже)
  → 302 /?oidc=ok&uid=<uid>
deny.js: сохраняет uid в Lampa.Storage и убирает его из адресной строки
```

Вход с ТВ: экран входа показывает QR со ссылкой `GET /oidc/link/<code>`,
телефон открывает её, логинится через Authentik и видит подтверждение входа. ТВ опрашивает
`GET /oidc/link/<code>/status` и получает uid (код одноразовый, живёт 10 минут).
QR содержит публичный адрес из `publicUrl`; этот адрес должен открываться на телефоне.

Дальше всё работает как обычно: запросы SPA несут `?account_email=<uid>`, accsdb пропускает.
Старые клиенты (ТВ, приставки) с uid в localStorage продолжают работать без изменений.

Identity берётся из `/userinfo` (прямое TLS-соединение lampac → authority). Подпись ID token
не проверяется (в рантайме lampac нет JWT-библиотек), но проверяются `iss`, `aud`, `exp`,
`nonce`. Отсутствие проверки подписи ID token — ограничение текущей реализации; учётные
данные берутся только из `/userinfo` по HTTPS.

## Настройка Authentik

1. **Applications → Providers → Create** (тип OAuth2/OpenID Connect):
   - Client type: confidential (или public — тогда оставьте `clientSecret` пустым, PKCE обязателен в любом случае);
   - **Redirect URI: `https://<ваш-адрес-lampac>/oidc/callback`** — внешний публичный HTTPS-адрес
     (тот, по которому lampac открывается из браузера). Если lampac виден по нескольким адресам —
     добавьте по одному URI на каждый или задайте `publicUrl` в конфиге модуля, чтобы адрес
     всегда был один;
   - **Subject Mode**: оставить по умолчанию («хэшированный идентификатор пользователя») или «UUID».
     **Не использовать** username / email / UPN — они угадываемы. Признак неверного режима:
     модуль пишет warning в консоль, если `sub` похож на email;
   - Scopes: `openid`, `email`, `profile`.
2. **Applications → Create** → привязать провайдер. Доступ ограничивается на стороне
   Authentik (policy/group bindings) — lampac доверяет успешному входу и не дублирует проверку.
3. Issuer приложения: `https://<authentik>/application/o/<slug>/` — он же `authority`.

## Настройка lampac

В `init.conf` (пример — `init.merge.example.json`):

```jsonc
{
  "OidcAuth": {
    "enable": true,
    "overrideDeny": true,                                           // Install the login screen automatically
    "name": "Authentik",                                              // Provider name shown on the login button
    "authority": "https://auth.example.com/application/o/lampac/",
    "publicUrl": "",                                                  // Base URL for redirect_uri; empty means use the request origin
    "clientId": "...",
    "clientSecret": "",                                               // Empty for a public client (PKCE only)
    "scopes": "openid profile email",
    "defaultGroup": 0,                                                // Group assigned to new users
    "defaultExpiresDays": 0,                                          // 0 means no expiration
    "unlinkedLog": true                                               // logs/oidc/unlinked.log
  }
}
```

Модуль при включении сам активирует `accsdb.enable`. Конфиг перечитывается на лету.

## Экран входа

Когда `OidcAuth.enable=true` и `overrideDeny=true`, модуль сам устанавливает свой экран входа в штатный путь
`plugins/override/deny.js`. Копировать файл вручную и пересобирать LampaWeb/Shared не нужно.
Если там уже был чужой override, модуль сохраняет его как `deny.js.before-oidc` и
восстанавливает при отключении OIDC. При развёртывании скопируйте каталог модуля целиком,
включая `plugins/deny.js`; рабочий каталог Lampac должен допускать запись в `plugins/override/`.
На экране доступны вход через провайдера, QR для телефона и пароль. Оформление адаптировано из
[QRAuth upstream](https://github.com/lampac-nextgen/lampac/tree/main/Modules/Community/QRAuth):
полноэкранный экран с двумя колонками и фоном из постеров. Постеры загружаются через источник
TMDB клиента Lampa; если он недоступен, остаётся тёмный градиентный фон. Telegram и CUB в
экране OIDC не используются. `LampaWeb.telegramAuthGate`, если включён,
имеет приоритет. Без OIDC остаётся обычный `deny.js` (включая восстановленный override).

В Docker образ запускается от UID 1000. Если в Compose примонтированы только отдельные файлы
`/lampac/plugins/override/lampainit-invc.js` и `privateinit.js`, Docker может создать их
родительский каталог от root. Тогда запись `deny.js` завершится с ошибкой доступа и останется
стандартный экран с CUB.
Монтируйте весь каталог с плагинами вместо отдельных файлов:

```yaml
volumes:
  - ./plugins:/lampac/plugins/override
```

Файлы `lampainit-invc.js` и `privateinit.js` остаются в `./plugins/`. Каталог должен быть
доступен на запись UID 1000 (`chown -R 1000:1000 ./plugins` на хосте, если требуется).
LampaWeb кэширует `deny.js` примерно на 10 минут: после изменения конфига новый экран может
появиться с задержкой, при перезапуске кэш очищается. Сам `/lampainit.js` также может
кэшироваться сервером; после обновления модуля перезапустите Lampac. Поле
`denyInstalled` в `GET /oidc/config` показывает, совпадает ли установленный override с
файлом модуля. Если там `false`, проверьте, что `mods/OidcAuth/plugins/deny.js` существует
и что процесс Lampac может записать `plugins/override/deny.js`.

Для QR задайте `publicUrl`, если lampac доступен на ТВ по локальному адресу, а на телефоне —
по другому адресу. QR изображение загружается через `api.qrserver.com`, поэтому ТВ требуется
доступ к этому сервису; ссылка также показана текстом.

## Привязка существующих пользователей

Ключ сопоставления — `sub` из Authentik. Он хранится в записи users.json:

```jsonc
{
  "id": "k7x92mfa04qz",             // Random uid, never an email address
  "ids": ["<sub>"],                 // Link to the OIDC subject
  "comment": "oidc:vasya@example.com",
  "@params": { "oidc_sub": "<sub>", "oidc_email": "...", "oidc_username": "..." }
}
```

**Новый человек через OIDC** → автоматически создаётся запись выше (uid генерируется
случайно), строка дублируется в `logs/oidc/unlinked.log` — оттуда удобно копировать `sub`.

**Привязать старого пользователя** (users.json подхватывается на лету, без рестарта):

1. Идеальный порядок — привязать **до** первого входа: возьмите `sub` из unlinked.log,
   вкладки Preview провайдера в Authentik или API `GET /api/v3/core/users/?search=<имя>`
   (поле `uuid` — только при режиме UUID), и впишите его в `"ids"` существующей записи.
2. Если человек уже вошёл и создалась запись-«дубль» (`comment: "oidc:..."`): удалите дубль,
   `sub` впишите в старую запись. Устройство человека само очистит невалидный uid
   и при следующем входе через OIDC получит старый аккаунт (история/закладки на месте).

Caveat: при режиме «хэшированный идентификатор» `sub` зависит от unique_identifier
инстанса Authentik — переустановка Authentik с нуля меняет все `sub` (привязки слетают,
лечится повторной привязкой из unlinked.log). Бэкап/перенос БД — без последствий.

## Роуты

| Роут | Назначение |
|------|------------|
| `GET /oidc/config` | `{enabled, name, loginPath, denyInstalled}` — публичные данные и проверка установки экрана |
| `GET /oidc/login?uid=&returnUrl=&link=` | запуск flow (`returnUrl` — только локальный путь; `link` — код QR-входа) |
| `GET /oidc/callback` | завершение flow (code exchange, userinfo, создание/поиск uid) |
| `POST /oidc/link` | создать код QR-входа (ТВ-сторона), TTL 10 минут |
| `GET /oidc/link/{code}` | цель QR (телефон): переход в login flow с этим кодом |
| `GET /oidc/link/{code}/status` | опрос (ТВ): отдаёт uid один раз, затем код гаснет |
| `GET /oidc/status?uid=` | `{ok}` — валиден ли uid (ban/expires учитываются) |
| `GET /oidc/logout` | RP-initiated logout (редирект на end_session_endpoint) |
| `GET /oidc/deny.js` | исходный ассет экрана входа (для диагностики) |

## Безопасность

- PKCE S256 всегда; `state` — 32 байта CSPRNG, single-use, TTL 10 минут (серверное хранилище);
  `nonce` проверяется в ID token.
- `sub` невозможно подделать: он берётся только из ответа Authentik после реальной
  аутентификации. В `ids` попадает лишь собственный `sub` залогинившегося.
- QR-код: случайный 6-символьный код, одноразовый, TTL 10 минут — знать его должен только
  ТВ в комнате; uid отдаётся по коду ровно один раз.
- Токены/коды/`clientSecret` не логируются. В логах только `sub`/email/username/uid.
- Ratelimit `/oidc/*` через WAF limit_map (по умолчанию 10 req/s).

## Ограничения / фаза 2

- Проверка подписи ID token по JWKS — не реализована.
- AdminPanel/`[Authorization]`-роуты продолжают охраняться rootPasswd (`accspasswd`), OIDC их не касается.
