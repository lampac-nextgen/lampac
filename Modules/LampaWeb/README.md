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
| `msx/start.json`, `samsung.wgt`, `lg.ipk` | Специфичные для ТВ пакеты (MSX, Samsung Tizen, LG webOS). Для `samsung.wgt` параметр `?tizen=3` убирает `tizen:service` — без этого виджет не ставится на Tizen 3.0 и ниже. |
| `/lampainit.js` | Инициализация клиента (подстановки плагинов, **deny.js**, токены). |
| `/on.js`, `/on/js/{token}`, `/on/h/{token}`, `/on/{token}` | Режим онлайн-плагина. |
| `/dorama.js`, `/dorama/js/{token}` | Отдельный Lampa-плагин пункта **«Дорамы»** и источника `lampac_dorama`. |
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
- **`initPlugins.dorama`** — подключает отдельный Lampa-плагин **`/dorama.js`** в `/lampainit.js` и `/on.js`;
- **`limit_map`** — WAF для **`^/(extensions|testaccsdb|msx/)`**.

## initPlugins и конфликты URL

Lampac отдаёт плагины двумя путями:

| Entry point | Назначение |
|-------------|------------|
| **`/lampainit.js`** | Регистрация в `Lampa.Plugins` + первичные настройки клиента (`{initiale}`) |
| **`/on.js`**, **`/on/js/{token}`** | Прямая загрузка скриптов без registry |

Список плагинов для обоих маршрутов собирается **`LampaPluginBuilder`** ([`LampaPluginBuilder.cs`](LampaPluginBuilder.cs)) по флагам **`LampaWeb.initPlugins`** и **`customPlugins`**.

### Default URL (flat)

В `{initiale}` всегда подставляются flat-URL вида `{localhost}/online.js`, `{localhost}/sync.js` и т.д. Сервер **не** подменяет их на `*/js/{token}` автоматически.

### Token URL (ручная установка)

Пользователь может добавить token-вариант вручную, например `{localhost}/sync/js/myuser` или `{localhost}/online/js/myuser`. На клиенте [`plugins/lampainit.js`](plugins/lampainit.js) при каждом старте:

- сравнивает server list с уже установленными плагинами;
- **не добавляет** flat-URL, если в registry уже есть любой URL того же семейства (`name.js` и `name/js/{token}` считаются одним плагином);
- при наличии token-варианта может удалить flat-дубликат (если в runtime Lampa доступен `Plugins.remove`).

### Sync orchestrator

Если включён **`initPlugins.sync`**, модуль **`sync.js`** сам загружает bookmark и timecode. Builder **не** добавляет отдельные `bookmark.js` / `timecode.js` в `{initiale}` и `/on.js`, даже если их флаги в конфиге `true`. Рекомендуется в `init.conf` при `sync: true` явно ставить `bookmark: false`, `timecode: false`.

### customPlugins

Записи из **`LampaWeb.customPlugins`** ( `status: 1` ) добавляются as-is. Family dedup на клиенте применяется и к ним — например, custom `{localhost}/music/js/token` блокирует добавление flat `{localhost}/music.js` из другого источника.

### Плагины без token-маршрута

`watchtogether.js`, `startpage.js` — dedup только по exact URL.

### Регрессионный тест dedup

```bash
node Modules/LampaWeb/plugins/lampainit-dedup.test.js
```

## Дорамы

Плагин **`plugins/dorama.js`** добавляет пункт **«Дорамы»** в основное меню Lampa сразу после **«Сериалы»** и регистрирует отдельный источник **`lampac_dorama`**. Он не зависит от SISI и подключается только через **`LampaWeb.initPlugins.dorama`**.

Источник строит секции через TMDB Discover TV для корейских драм: **`with_original_language=ko`**, **`with_genres=18`**, **`include_adult=false`**. Запросы идут через штатный Lampac TMDB proxy **`/tmdb/api/3/...`**, когда `{localhost}` подставлен, иначе используется нативный TMDB URL-builder клиента. Плагин также перехватывает Dorama `category_full` ссылки, чтобы кнопка **«Ещё»** не уходила в активный CUB/TMDB source.

Проверка после изменений:

- включить **`LampaWeb.initPlugins.dorama`**;
- открыть Lampa через **`/lampainit.js`** и убедиться, что **«Дорамы»** стоят сразу после **«Сериалы»**;
- открыть Lampa через **`/on.js`** и убедиться, что пункт **«Дорамы»** появляется без зависимости от SISI;
- открыть **«Дорамы»** и проверить, что экран секций загружает строки;
- открыть **«Ещё»** в любой секции и проверить, что страница 2 грузится из **`lampac_dorama`**, а не из CUB;
- перезагрузить приложение и убедиться, что в меню остаётся один пункт **«Дорамы»**.

## Зависимости

- Каталог **`wwwroot/`** в корне приложения с **`lampa-main/`** и статикой.
- Плагины модуля в **`plugins/`** (extensions, deny и т.д.).

## Компоненты

| Компонент | Роль |
|-----------|------|
| `LampaCron` | Фоновое обновление из Git по конфигу. |
| `ErrorDocController` | Доп. маршруты ошибок (`/e/acb`). |
