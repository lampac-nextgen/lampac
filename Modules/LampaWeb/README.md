# LampaWeb

Раздача **веб-клиента Lampa** из **`wwwroot`**, сборка **`lampainit.js`** и вспомогательных скриптов, список расширений, интеграция с **accsdb**, фоновое обновление репозитория (**`LampaCron`**).

## Назначение

- Корень сайта **`/`**: при необходимости подставляется **`<base>`** для каталога из **`conf.index`** (например `lampa-main/index.html`), иначе редирект на статический файл.
- Конфиг модуля задаёт репозиторий Git (**`git`**, **`tree`**) для автоподтягивания исходников и интервал **`intervalupdate`** (минуты), флаг **`autoupdate`**.

## Основные маршруты (`Controllers/ApiController.cs`)

| Маршрут | Назначение |
|---------|------------|
| `/`, `/personal.lampa`, вложенные варианты | Главная страница Lampa или заглушка. |
| `/reqinfo` | JSON с информацией о текущем запросе (**`requestInfo`**). |
| `/extensions` | Кэшируемый **`extensions.json`** из модуля с подстановкой `{localhost}`. |
| `/testaccsdb` | Проверка доступа accsdb (GET/POST); см. также **`EventListener.Accsdb`** в `ModInit`. |
| `/app.min.js`, `/{type}/app.min.js` | Сборка минифицированного приложения. |
| `/css/app.css` | Стили. |
| `msx/start.json`, `samsung.wgt` | Специфичные для ТВ пакеты. |
| `/lampainit.js` | Инициализация клиента (подстановки плагинов, **deny.js**, токены). |
| `/on.js`, `/on/js/{token}`, `/on/h/{token}`, `/on/{token}` | Режим онлайн-плагина. |
| `/privateinit.js` | Доп. инициализация. |
| `/telegram_auth_gate.js` | Плагин сценария Telegram-авторизации (см. модуль Community). |

Событие **`accsdb`** в `ModInit`: для пути **`/testaccsdb`** при совпадении UID с **`shared_passwd`** выставляется **`IsAnonymousRequest`** (обход для общего пароля).

## Конфигурация

Секция в `init.conf`: **`LampaWeb`**.

Ключевые поля в коде по умолчанию:

- **`index`** — путь под `wwwroot` до HTML входа;
- **`basetag`** — вставка `<base>` для SPA;
- **`git`**, **`tree`** — источник обновлений;
- **`intervalupdate`** — период cron (минуты);
- **`limit_map`** — WAF для **`^/(extensions|testaccsdb|msx/)`**.

## Зависимости

- Каталог **`wwwroot/`** в корне приложения с **`lampa-main/`** и статикой.
- Плагины модуля в **`plugins/`** (extensions, deny и т.д.).

## Компоненты

| Компонент | Роль |
|-----------|------|
| `LampaCron` | Фоновое обновление из Git по конфигу. |
| `ErrorDocController` | Доп. маршруты ошибок (`/e/acb`). |
